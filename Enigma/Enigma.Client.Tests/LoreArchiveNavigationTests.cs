using Enigma.Client.Models;
using Xunit;

namespace Enigma.Client.Tests;

public sealed class LoreArchiveNavigationTests
{
    [Fact]
    public void LegacyGroupDefaultsResolveToExpectedCanonicalFiles()
    {
        Assert.True(LoreArchiveContent.TryResolveLegacyGroupDefaultFileId("explorers", out var explorers));
        Assert.True(LoreArchiveContent.TryResolveLegacyGroupDefaultFileId("sectors", out var sectors));
        Assert.True(LoreArchiveContent.TryResolveLegacyGroupDefaultFileId("directive", out var directive));

        Assert.Equal("explorers", explorers);
        Assert.Equal("sectors", sectors);
        Assert.Equal("governance", directive);
    }

    [Fact]
    public void CanonicalHelperBuildsExpectedQueryUrls()
    {
        Assert.Equal("/lore?file=anomaly-awareness", LoreArchiveContent.GetCanonicalFileHref("anomaly-awareness"));
        Assert.Equal("/lore?file=sector-images", LoreArchiveContent.GetCanonicalFileHref("sector-images"));
        Assert.Equal("/lore?file=unanswered-questions", LoreArchiveContent.GetCanonicalFileHref("unanswered-questions"));
    }

    [Fact]
    public void FileGroupLookupReturnsExpectedDirectoryGroup()
    {
        Assert.Equal("discovery", LoreArchiveContent.GetGroupForFile("e-units")?.Key);
        Assert.Equal("sectors", LoreArchiveContent.GetGroupForFile("sector-images")?.Key);
        Assert.Equal("directive", LoreArchiveContent.GetGroupForFile("unanswered-questions")?.Key);
    }
}
