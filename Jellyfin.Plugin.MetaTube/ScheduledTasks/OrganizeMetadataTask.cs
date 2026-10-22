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
                var versionEvidence = GetVersionEvidence(item);
                var (hasChineseSubtitle, hasUncensored) =
                    OrganizeMetadataHelper.GetClassification(versionEvidence);

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

    private static List<OrganizeMetadataHelper.VersionEvidence> GetVersionEvidence(BaseItem item)
    {
        var relatedFiles = Directory.GetParent(item.Path)?.GetFiles().Select(info => info.Name)
                            .ToArray() ?? Array.Empty<string>();
        var versions = new List<OrganizeMetadataHelper.VersionEvidence>();

        void AddVersion(string path)
        {
            var filename = GetFilenameWithoutExtension(path);
            if (!string.IsNullOrWhiteSpace(filename) &&
                versions.All(version => !version.FilenameWithoutExtension.Equals(
                    filename, StringComparison.OrdinalIgnoreCase)))
                versions.Add(new OrganizeMetadataHelper.VersionEvidence(filename, relatedFiles));
        }

        AddVersion(item.Path);
        if (item is IHasMediaSources hasMediaSources)
        {
#if __EMBY__
            var mediaSources = hasMediaSources.GetMediaSources(null, false, false, null);
#else
            var mediaSources = hasMediaSources.GetMediaSources(false);
#endif
            foreach (var mediaSource in mediaSources)
            {
                AddVersion(GetFilenameWithoutExtension(mediaSource.Path, mediaSource.Name));
            }
        }

        return versions;
    }

    private static string GetFilenameWithoutExtension(string path, string fallbackName = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Path.GetFileNameWithoutExtension(fallbackName ?? string.Empty);

        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var filenameIndex = Array.FindIndex(segments, segment =>
                segment.Equals("n", StringComparison.OrdinalIgnoreCase));
            if (filenameIndex >= 0 && filenameIndex + 1 < segments.Length)
                path = Uri.UnescapeDataString(segments[filenameIndex + 1]);
            else if (!string.IsNullOrWhiteSpace(fallbackName))
                path = fallbackName;
            else
                path = Uri.UnescapeDataString(segments.LastOrDefault() ?? string.Empty);
        }

        return Path.GetFileNameWithoutExtension(path);
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
