using Enigma.Client.Models;
using Xunit;

namespace Enigma.Client.Tests;

public sealed class LoreArchiveContentTests
{
    [Fact]
    public void LoreFilesRemainInCanonicalNarrativeOrder()
    {
        var titles = LoreArchiveContent.Files.Select(file => file.Title).ToArray();

        Assert.Equal(
            [
                "Enigma Corporation",
                "The Exploration Problem",
                "Enigma Exploration Units (E-Units)",
                "The Explorers",
                "Anomaly Collapse",
                "Expedition Failure",
                "Anomaly Awareness",
                "Sectors",
                "Sector Reactivation",
                "Sector Images",
                "Maze Nuggets (MN)",
                "Governance",
                "The Unanswered Questions"
            ],
            titles);
    }

    [Fact]
    public void LoreFilesExposeExpectedCanonicalUrlsAndRightPanels()
    {
        var routes = LoreArchiveContent.Files
            .Select(file => (file.Title, file.CanonicalHref, file.RightPanelType))
            .ToArray();

        Assert.Equal(
            new[]
            {
                ("Enigma Corporation", "/lore?file=enigma-corporation", ArchiveRightPanelType.GeospatialWatch),
                ("The Exploration Problem", "/lore?file=exploration-problem", ArchiveRightPanelType.InterferenceMatrix),
                ("Enigma Exploration Units (E-Units)", "/lore?file=e-units", ArchiveRightPanelType.RadarReconstruction),
                ("The Explorers", "/lore?file=explorers", ArchiveRightPanelType.ExplorerRoster),
                ("Anomaly Collapse", "/lore?file=anomaly-collapse", ArchiveRightPanelType.CollapseSequence),
                ("Expedition Failure", "/lore?file=expedition-failure", ArchiveRightPanelType.LossPlayback),
                ("Anomaly Awareness", "/lore?file=anomaly-awareness", ArchiveRightPanelType.BehaviorMonitor),
                ("Sectors", "/lore?file=sectors", ArchiveRightPanelType.CubeAnalysis),
                ("Sector Reactivation", "/lore?file=sector-reactivation", ArchiveRightPanelType.SpatialReconstruction),
                ("Sector Images", "/lore?file=sector-images", ArchiveRightPanelType.RecoveredImageCache),
                ("Maze Nuggets (MN)", "/lore?file=maze-nuggets", ArchiveRightPanelType.MaterialAnalysis),
                ("Governance", "/lore?file=governance", ArchiveRightPanelType.GovernanceLattice),
                ("The Unanswered Questions", "/lore?file=unanswered-questions", ArchiveRightPanelType.FinalRedaction),
            },
            routes);
    }

    [Fact]
    public void LoreArchiveExposesRequiredClosingLineAndCallToAction()
    {
        Assert.Equal(
            "Are we exploring the anomalies… or are the anomalies exploring us?",
            LoreArchiveContent.FinalQuestionLine);
        Assert.Equal(
            "Join the Enigma Exploration Program",
            LoreArchiveContent.JoinProgramCallToAction);
    }

    [Fact]
    public void LoreCatalogNoLongerStoresSyntheticMetrics()
    {
        Assert.All(LoreArchiveContent.Files, file => Assert.Empty(file.Metrics));
    }
}
