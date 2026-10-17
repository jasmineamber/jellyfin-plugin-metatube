using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.MetaTube.ScheduledTasks;

internal static class OrganizeMetadataHelper
{
    internal const string ChineseSubtitleGenre = "中文字幕";
    internal const string UncensoredGenre = "无码破解";
    internal const string UncensoredBadge = "u.png";
    internal const string UncensoredChineseBadge = "uc.png";

    private static readonly Regex TagSeparator = new(@"[-_\s]", RegexOptions.Compiled);
    private static readonly Regex ExternalChineseSubtitle = new(
        @"\.(ch[ist]|zho?(-(cn|hk|sg|tw))?)(?:\.[^.]+)*\.(ass|srt|ssa|smi|sub|idx|psb|vtt)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static bool HasEmbeddedChineseSubtitle(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return false;

        return filename.Contains(ChineseSubtitleGenre) || HasTag(filename, "C", "UC", "ch");
    }

    internal static bool HasExternalChineseSubtitle(string basename, IEnumerable<string> files)
    {
        if (string.IsNullOrWhiteSpace(basename) || files == null)
            return false;

        return files.Any(name => ExternalChineseSubtitle.IsMatch(name) &&
                                 ExternalChineseSubtitle.Replace(name, string.Empty)
                                     .Equals(basename, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool HasUncensoredSuffix(string filenameWithoutExtension)
    {
        return !string.IsNullOrWhiteSpace(filenameWithoutExtension) &&
               filenameWithoutExtension.EndsWith("-U", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool SynchronizeGenre(List<string> genres, string genre, bool shouldExist)
    {
        if (shouldExist)
        {
            if (genres.Contains(genre))
                return false;

            genres.Add(genre);
            return true;
        }

        var removed = genres.RemoveAll(value => value.Equals(genre, StringComparison.Ordinal));
        return removed > 0;
    }

    internal static string SelectBadge(bool hasChineseSubtitle, bool hasUncensored, string chineseSubtitleBadge)
    {
        if (hasChineseSubtitle && hasUncensored)
            return UncensoredChineseBadge;
        if (hasChineseSubtitle)
            return chineseSubtitleBadge;
        if (hasUncensored)
            return UncensoredBadge;

        return string.Empty;
    }

    internal static bool ShouldUpdatePrimaryImage(
        bool enableBadges,
        bool classificationGenresChanged,
        bool refreshBadges,
        bool hasChineseSubtitle,
        bool hasUncensored)
    {
        return enableBadges &&
               (classificationGenresChanged ||
                (refreshBadges && (hasChineseSubtitle || hasUncensored)));
    }

    private static bool HasTag(string filename, string tag)
    {
        return TagSeparator.Split(filename).Contains(tag, StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasTag(string filename, params string[] tags)
    {
        return tags.Any(tag => HasTag(filename, tag));
    }
}
