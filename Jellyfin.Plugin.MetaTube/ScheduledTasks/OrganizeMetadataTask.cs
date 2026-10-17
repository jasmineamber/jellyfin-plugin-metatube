using Jellyfin.Plugin.MetaTube.Extensions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
#if __EMBY__
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Logging;

#else
using MediaBrowser.Controller.Sorting;
using Microsoft.Extensions.Logging;
using Jellyfin.Data.Enums;
#endif

namespace Jellyfin.Plugin.MetaTube.ScheduledTasks;

public class OrganizeMetadataTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger _logger;

#if __EMBY__
    public OrganizeMetadataTask(ILogManager logManager, ILibraryManager libraryManager)
    {
        _logger = logManager.CreateLogger<OrganizeMetadataTask>();
        _libraryManager = libraryManager;
    }
#else
    public OrganizeMetadataTask(ILogger<OrganizeMetadataTask> logger, ILibraryManager libraryManager)
    {
        _logger = logger;
        _libraryManager = libraryManager;
    }
#endif

    public string Key => $"{Plugin.Instance.Name}OrganizeMetadata";

    public string Name => "Organize Metadata";

    public string Description => $"Organizes video metadata provided by {Plugin.Instance.Name} in library.";

    public string Category => Plugin.Instance.Name;

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfo.TriggerDaily,
            TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
        };
    }

#if __EMBY__
    public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
#else
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
#endif
    {
        await Task.Yield();

        progress?.Report(0);

        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            MediaTypes = new[] { MediaType.Video },
#if __EMBY__
            HasAnyProviderId = new[] { Plugin.Instance.Name },
            IncludeItemTypes = new[] { nameof(Movie) },
#else
            HasAnyProviderId = new Dictionary<string, string> { { Plugin.Instance.Name, string.Empty } },
            IncludeItemTypes = new[] { BaseItemKind.Movie }
#endif
        }).ToList();

        foreach (var (idx, item) in items.WithIndex())
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report((double)idx / items.Count * 100);

            var genres = item.Genres?.ToList() ?? new List<string>();
            var itemChanged = false;

            try
            {
                var hasChineseSubtitle =
                    OrganizeMetadataHelper.HasEmbeddedChineseSubtitle(item.FileNameWithoutExtension) ||
                    HasExternalChineseSubtitle(item.Path);
                var hasUncensored = OrganizeMetadataHelper.HasUncensoredSuffix(item.FileNameWithoutExtension);

                var classificationGenresChanged = OrganizeMetadataHelper.SynchronizeGenre(
                    genres, OrganizeMetadataHelper.ChineseSubtitleGenre, hasChineseSubtitle);
                classificationGenresChanged |= OrganizeMetadataHelper.SynchronizeGenre(
                    genres, OrganizeMetadataHelper.UncensoredGenre, hasUncensored);

                if (OrganizeMetadataHelper.ShouldUpdatePrimaryImage(
                        Plugin.Instance.Configuration.EnableBadges,
                        classificationGenresChanged,
                        Plugin.Instance.Configuration.RefreshBadges,
                        hasChineseSubtitle,
                        hasUncensored))
                {
                    var badge = OrganizeMetadataHelper.SelectBadge(
                        hasChineseSubtitle, hasUncensored, Plugin.Instance.Configuration.BadgeUrl);
                    itemChanged |= await SetPrimaryImage(item, badge, cancellationToken);
                }
            }
            catch (Exception e)
            {
                _logger.Error("Update subtitle badges for video {0}: {1}", item.Name, e.Message);
            }

            // Remove duplicates.
            var orderedGenres =
                (Plugin.Instance.Configuration.EnableGenreSubstitution
                    // Substitute genres.
                    ? Plugin.Instance.Configuration.GetGenreSubstitutionTable().Substitute(genres)
                    : genres).Distinct().OrderByString(genre => genre).ToList();

            // Skip updating item if equal.
            var genresUnchanged = item.Genres == null
                ? orderedGenres.Count == 0
                : item.Genres.SequenceEqual(orderedGenres, StringComparer.OrdinalIgnoreCase);
            if (!genresUnchanged)
            {
                item.Genres = orderedGenres.ToArray();
                itemChanged = true;
            }

            if (!itemChanged)
                continue;

            _logger.Info("Organize metadata for video: {0}", item.Name);

#if __EMBY__
            _libraryManager.UpdateItem(item, item, ItemUpdateType.MetadataEdit, null);
#else
            await _libraryManager
                .UpdateItemAsync(item, item, ItemUpdateType.MetadataEdit, cancellationToken)
                .ConfigureAwait(false);
#endif
        }

        progress?.Report(100);
    }

    #region Helper

    private static bool HasExternalChineseSubtitle(string path)
    {
        var files = Directory.GetParent(path)?.GetFiles().Select(info => info.Name) ??
                    Enumerable.Empty<string>();
        return OrganizeMetadataHelper.HasExternalChineseSubtitle(
            Path.GetFileNameWithoutExtension(path), files);
    }

    private static async Task<bool> SetPrimaryImage(BaseItem item, string badge, CancellationToken cancellationToken)
    {
        var pid = item.GetPid(Plugin.Instance.Name);
        if (string.IsNullOrWhiteSpace(pid.Id) || string.IsNullOrWhiteSpace(pid.Provider))
            return false;

        var m = await ApiClient.GetMovieInfoAsync(pid.Provider, pid.Id, cancellationToken);
        // Set first primary image.
        item.SetImage(new ItemImageInfo
        {
            Path = ApiClient.GetPrimaryImageApiUrl(m.Provider, m.Id, pid.Position ?? -1, badge),
            Type = ImageType.Primary
        }, 0);

        return true;
    }

    #endregion
}
