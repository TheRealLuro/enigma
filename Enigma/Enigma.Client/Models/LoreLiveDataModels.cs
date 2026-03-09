using System.Globalization;
using System.Text.Json.Serialization;

namespace Enigma.Client.Models;

public sealed class LoreTelemetryResponse
{
    public string Status { get; set; } = "error";
    public LoreTelemetryOverview Overview { get; set; } = new();
    public List<LoreSectorRecord> RecentSectors { get; set; } = [];
    public List<LoreExplorerRecord> TopExplorers { get; set; } = [];
    public LoreGovernanceSnapshot Governance { get; set; } = LoreGovernanceSnapshot.Unavailable();
}

public sealed class LoreTelemetryOverview
{
    public int SectorCount { get; set; }
    public int ExplorerCount { get; set; }
    public string LatestSectorFoundedDisplay { get; set; } = string.Empty;
    public bool SectorDataAvailable { get; set; }
    public bool ExplorerDataAvailable { get; set; }
    public bool GovernanceDataAvailable { get; set; }
}

public sealed class LoreSectorAtlasResponse
{
    public string Status { get; set; } = "error";
    public List<LoreSectorRecord> Sectors { get; set; } = [];
}

public sealed class LoreSectorRecord
{
    public string Id { get; set; } = string.Empty;
    public string MapName { get; set; } = string.Empty;
    public string MapImage { get; set; } = string.Empty;
    public string Theme { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;
    public string Founder { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public int Plays { get; set; }
    public double RatingAverage { get; set; }
    public int RatingCount { get; set; }
    public string BestTimeDisplay { get; set; } = string.Empty;
    public string TimeFounded { get; set; } = string.Empty;
    public string TimeFoundedDisplay { get; set; } = string.Empty;
}

public sealed class LoreExplorerRecord
{
    public string Username { get; set; } = string.Empty;
    public long MazeNuggets { get; set; }
    public int OwnedMapsCount { get; set; }
    public int DiscoveredMapsCount { get; set; }
    public int MapsCompleted { get; set; }
    public int MapsLost { get; set; }
    public int MapsPlayed { get; set; }
    public double WinRate { get; set; }
    public bool IsOnline { get; set; }
    public string ProfileImage { get; set; } = string.Empty;

    [JsonIgnore]
    public double CompletionRate
    {
        get
        {
            var denominator = Math.Max(MapsPlayed, MapsCompleted + MapsLost);
            return denominator <= 0
                ? 0
                : (double)MapsCompleted / denominator;
        }
    }
}

public sealed class LoreGovernanceSnapshot
{
    public bool Available { get; set; }
    public bool VotingOpen { get; set; }
    public string Title { get; set; } = string.Empty;
    public int TotalMnSpent { get; set; }
    public double TotalVotePower { get; set; }
    public int TotalVotesCast { get; set; }
    public int UniqueVoterCount { get; set; }
    public string AvailabilityLabel { get; set; } = "Public live governance data unavailable";

    public static LoreGovernanceSnapshot Unavailable()
    {
        return new LoreGovernanceSnapshot
        {
            Available = false,
            AvailabilityLabel = "Public live governance data unavailable",
        };
    }
}

public sealed class ArchiveSystemLogEntry
{
    public string Timestamp { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Tone { get; init; } = "stable";
}

public static class LoreLiveDataComposer
{
    public static LoreTelemetryResponse ComposeTelemetry(
        LeaderboardResponse? sectorFeed,
        PlayerLeaderboardResponse? explorerFeed,
        LoreGovernanceSnapshot? governance = null,
        int recentSectorLimit = 6,
        int topExplorerLimit = 6)
    {
        var recentSectors = BuildSectorRecords(sectorFeed?.Maps)
            .Take(Math.Clamp(recentSectorLimit, 1, 12))
            .ToList();
        var topExplorers = BuildExplorerRecords(explorerFeed?.Players)
            .Take(Math.Clamp(topExplorerLimit, 1, 12))
            .ToList();
        governance ??= LoreGovernanceSnapshot.Unavailable();

        var sectorDataAvailable = sectorFeed is not null;
        var explorerDataAvailable = explorerFeed is not null;

        return new LoreTelemetryResponse
        {
            Status = sectorDataAvailable || explorerDataAvailable
                ? (sectorDataAvailable && explorerDataAvailable ? "success" : "partial")
                : "error",
            Overview = new LoreTelemetryOverview
            {
                SectorCount = sectorFeed?.TotalCount ?? recentSectors.Count,
                ExplorerCount = explorerFeed?.TotalCount ?? topExplorers.Count,
                LatestSectorFoundedDisplay = recentSectors.FirstOrDefault()?.TimeFoundedDisplay ?? string.Empty,
                SectorDataAvailable = sectorDataAvailable,
                ExplorerDataAvailable = explorerDataAvailable,
                GovernanceDataAvailable = governance.Available,
            },
            RecentSectors = recentSectors,
            TopExplorers = topExplorers,
            Governance = governance,
        };
    }

    public static LoreSectorAtlasResponse ComposeSectorAtlas(LeaderboardResponse? sectorFeed, int limit)
    {
        var sectors = BuildSectorRecords(sectorFeed?.Maps)
            .Take(Math.Clamp(limit, 1, 72))
            .ToList();

        return new LoreSectorAtlasResponse
        {
            Status = sectorFeed is null
                ? "error"
                : (sectors.Count == 0 ? "empty" : "success"),
            Sectors = sectors,
        };
    }

    public static LoreSectorRecord ToSectorRecord(MapSummary map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return new LoreSectorRecord
        {
            Id = map.Id,
            MapName = map.MapName,
            MapImage = map.MapImage ?? string.Empty,
            Theme = !string.IsNullOrWhiteSpace(map.ThemeLabel) ? map.ThemeLabel : map.Theme,
            Difficulty = map.Difficulty,
            Founder = map.Founder,
            Owner = map.Owner,
            Plays = map.Plays,
            RatingAverage = Math.Round(map.RatingAverage, 2),
            RatingCount = map.RatingCount,
            BestTimeDisplay = map.BestTimeDisplay,
            TimeFounded = map.TimeFounded ?? string.Empty,
            TimeFoundedDisplay = map.TimeFoundedDisplay,
        };
    }

    public static LoreExplorerRecord ToExplorerRecord(PlayerLeaderboardEntry explorer)
    {
        ArgumentNullException.ThrowIfNull(explorer);

        return new LoreExplorerRecord
        {
            Username = explorer.Username,
            MazeNuggets = explorer.MazeNuggets,
            OwnedMapsCount = explorer.OwnedMapsCount,
            DiscoveredMapsCount = explorer.DiscoveredMapsCount,
            MapsCompleted = explorer.MapsCompleted,
            MapsLost = explorer.MapsLost,
            MapsPlayed = explorer.MapsPlayed,
            WinRate = explorer.WinRate,
            IsOnline = explorer.IsOnline,
            ProfileImage = explorer.ProfileImage?.ImageUrl ?? string.Empty,
        };
    }

    public static LoreGovernanceSnapshot ToGovernanceSnapshot(GovernanceSessionResponse? response)
    {
        if (response?.ActiveSession is null &&
            response?.LatestClosedSession is null &&
            response?.ClosedSession is null)
        {
            return LoreGovernanceSnapshot.Unavailable();
        }

        var session = response.ActiveSession ?? response.LatestClosedSession ?? response.ClosedSession!;

        return new LoreGovernanceSnapshot
        {
            Available = true,
            VotingOpen = response.VotingOpen,
            Title = session.Title,
            TotalMnSpent = session.TotalMnSpent,
            TotalVotePower = Math.Round(session.TotalVotePower, 2),
            TotalVotesCast = session.TotalVotesCast,
            UniqueVoterCount = session.UniqueVoterCount,
            AvailabilityLabel = response.VotingOpen ? "Live governance session available" : "Latest governance session archived",
        };
    }

    private static IReadOnlyList<LoreSectorRecord> BuildSectorRecords(IEnumerable<MapSummary>? maps)
    {
        return (maps ?? [])
            .Where(map => !string.IsNullOrWhiteSpace(map.Id) && !string.IsNullOrWhiteSpace(map.MapName))
            .Select(ToSectorRecord)
            .OrderByDescending(record => ParseFoundedAt(record.TimeFounded))
            .ThenBy(record => record.MapName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<LoreExplorerRecord> BuildExplorerRecords(IEnumerable<PlayerLeaderboardEntry>? explorers)
    {
        return (explorers ?? [])
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Username))
            .Select(ToExplorerRecord)
            .ToList();
    }

    private static DateTimeOffset ParseFoundedAt(string raw)
    {
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
    }
}
