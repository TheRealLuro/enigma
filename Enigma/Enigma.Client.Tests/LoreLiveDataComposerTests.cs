using Enigma.Client.Models;
using Xunit;

namespace Enigma.Client.Tests;

public sealed class LoreLiveDataComposerTests
{
    [Fact]
    public void ComposeTelemetryMapsLiveOverviewFromWebsiteData()
    {
        var sectors = new LeaderboardResponse
        {
            TotalCount = 12,
            Maps =
            [
                new MapSummary
                {
                    Id = "sector-2",
                    MapName = "Second Sector",
                    Theme = "Sewer",
                    ThemeLabel = "Sewer",
                    Difficulty = "hard",
                    Founder = "Beta",
                    Owner = "Beta",
                    Plays = 8,
                    RatingAverage = 4.8,
                    RatingCount = 4,
                    BestTimeDisplay = "00:10:12:555",
                    TimeFounded = "2026-03-08T12:00:00Z",
                    TimeFoundedDisplay = "Mar 08, 2026",
                },
                new MapSummary
                {
                    Id = "sector-1",
                    MapName = "First Sector",
                    Theme = "Void",
                    ThemeLabel = "Void",
                    Difficulty = "easy",
                    Founder = "Alpha",
                    Owner = "Alpha",
                    Plays = 3,
                    RatingAverage = 4.1,
                    RatingCount = 2,
                    BestTimeDisplay = "00:05:11:000",
                    TimeFounded = "2026-03-09T12:00:00Z",
                    TimeFoundedDisplay = "Mar 09, 2026",
                },
            ],
        };

        var explorers = new PlayerLeaderboardResponse
        {
            TotalCount = 24,
            Players =
            [
                new PlayerLeaderboardEntry
                {
                    Username = "OperatorOne",
                    MazeNuggets = 1200,
                    DiscoveredMapsCount = 7,
                    MapsCompleted = 14,
                    MapsPlayed = 20,
                    WinRate = 0.7,
                },
            ],
        };

        var telemetry = LoreLiveDataComposer.ComposeTelemetry(sectors, explorers);

        Assert.Equal("success", telemetry.Status);
        Assert.True(telemetry.Overview.SectorDataAvailable);
        Assert.True(telemetry.Overview.ExplorerDataAvailable);
        Assert.Equal(12, telemetry.Overview.SectorCount);
        Assert.Equal(24, telemetry.Overview.ExplorerCount);
        Assert.Equal("Mar 09, 2026", telemetry.Overview.LatestSectorFoundedDisplay);
        Assert.Equal("First Sector", telemetry.RecentSectors[0].MapName);
        Assert.Equal("OperatorOne", telemetry.TopExplorers[0].Username);
        Assert.False(telemetry.Governance.Available);
    }

    [Fact]
    public void ComposeSectorAtlasOrdersNewestSectorFirst()
    {
        var sectors = new LeaderboardResponse
        {
            Maps =
            [
                new MapSummary
                {
                    Id = "older",
                    MapName = "Older Sector",
                    TimeFounded = "2026-03-07T12:00:00Z",
                    TimeFoundedDisplay = "Mar 07, 2026",
                },
                new MapSummary
                {
                    Id = "newer",
                    MapName = "Newer Sector",
                    TimeFounded = "2026-03-09T12:00:00Z",
                    TimeFoundedDisplay = "Mar 09, 2026",
                },
            ],
        };

        var atlas = LoreLiveDataComposer.ComposeSectorAtlas(sectors, 12);

        Assert.Equal("success", atlas.Status);
        Assert.Equal(["newer", "older"], atlas.Sectors.Select(sector => sector.Id).ToArray());
    }

    [Fact]
    public void GovernanceSnapshotFallsBackToUnavailableWhenNoSessionExists()
    {
        var snapshot = LoreLiveDataComposer.ToGovernanceSnapshot(new GovernanceSessionResponse());

        Assert.False(snapshot.Available);
        Assert.Equal("Public live governance data unavailable", snapshot.AvailabilityLabel);
    }

    [Fact]
    public void CompletionRateUsesCompletionCountsInsteadOfBackendWinRateField()
    {
        var record = LoreLiveDataComposer.ToExplorerRecord(new PlayerLeaderboardEntry
        {
            Username = "OperatorOne",
            MapsCompleted = 14,
            MapsLost = 6,
            MapsPlayed = 20,
            WinRate = 63.6,
        });

        Assert.Equal(0.7d, record.CompletionRate, 6);
    }
}
