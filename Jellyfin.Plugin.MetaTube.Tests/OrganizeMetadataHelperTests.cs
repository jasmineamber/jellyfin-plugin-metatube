using Jellyfin.Plugin.MetaTube.ScheduledTasks;
using Xunit;

namespace Jellyfin.Plugin.MetaTube.Tests;

public class OrganizeMetadataHelperTests
{
    [Theory]
    [InlineData("ABC-破解版", true)]
    [InlineData("ABC-破解版x", false)]
    [InlineData("ABC-破解版-4K", false)]
    [InlineData("ABC-U", false)]
    [InlineData("ABC-u", false)]
    [InlineData("ABC_U", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void HasUncensoredSuffix_OnlyMatchesFinalCrackedSuffix(string? filename, bool expected)
    {
        Assert.Equal(expected, OrganizeMetadataHelper.HasUncensoredSuffix(filename));
    }

    [Theory]
    [InlineData("影片 中文字幕", true)]
    [InlineData("影片-C", true)]
    [InlineData("影片_UC", true)]
    [InlineData("影片 ch", true)]
    [InlineData("影片-CH", true)]
    [InlineData("影片-uncensored", false)]
    [InlineData("", false)]
    public void HasEmbeddedChineseSubtitle_PreservesExistingMarkers(string? filename, bool expected)
    {
        Assert.Equal(expected, OrganizeMetadataHelper.HasEmbeddedChineseSubtitle(filename));
    }

    [Theory]
    [InlineData("Movie", "Movie.zh-cn.srt", true)]
    [InlineData("Movie", "Movie.chi.ass", true)]
    [InlineData("Movie", "Movie.en.srt", false)]
    [InlineData("Movie", "Other.zh-cn.srt", false)]
    public void HasExternalChineseSubtitle_MatchesSupportedSubtitleName(
        string basename,
        string file,
        bool expected)
    {
        Assert.Equal(expected,
            OrganizeMetadataHelper.HasExternalChineseSubtitle(basename, new[] { file }));
    }

    [Theory]
    [InlineData("中文字幕")]
    [InlineData("无码破解")]
    public void SynchronizeGenre_AddsAndRemovesClassificationWithoutDuplicates(string genre)
    {
        var genres = new List<string>();

        Assert.True(OrganizeMetadataHelper.SynchronizeGenre(genres, genre, true));
        Assert.False(OrganizeMetadataHelper.SynchronizeGenre(genres, genre, true));
        Assert.Equal(new[] { genre }, genres);

        Assert.True(OrganizeMetadataHelper.SynchronizeGenre(genres, genre, false));
        Assert.False(OrganizeMetadataHelper.SynchronizeGenre(genres, genre, false));
        Assert.Empty(genres);
    }

    [Theory]
    [InlineData(false, false, "zimu.png", "")]
    [InlineData(true, false, "custom.png", "custom.png")]
    [InlineData(false, true, "zimu.png", "u.png")]
    [InlineData(true, true, "zimu.png", "uc.png")]
    public void SelectBadge_MapsClassificationCombination(
        bool hasChineseSubtitle,
        bool hasUncensored,
        string chineseBadge,
        string expected)
    {
        Assert.Equal(expected,
            OrganizeMetadataHelper.SelectBadge(hasChineseSubtitle, hasUncensored, chineseBadge));
    }

    [Theory]
    [InlineData(true, true, false, false, false, true)]
    [InlineData(false, true, true, true, true, false)]
    [InlineData(true, false, true, true, true, true)]
    [InlineData(true, false, true, false, false, false)]
    [InlineData(true, false, false, true, false, false)]
    public void ShouldUpdatePrimaryImage_UsesGenreTransitionsAndRefresh(
        bool enableBadges,
        bool genresChanged,
        bool refresh,
        bool hasChineseSubtitle,
        bool hasUncensored,
        bool expected)
    {
        Assert.Equal(expected, OrganizeMetadataHelper.ShouldUpdatePrimaryImage(
            enableBadges, genresChanged, refresh, hasChineseSubtitle, hasUncensored));
    }
}
