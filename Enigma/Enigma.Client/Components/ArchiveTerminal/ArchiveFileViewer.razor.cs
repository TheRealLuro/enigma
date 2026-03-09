using System.Globalization;
using Enigma.Client.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Enigma.Client.Components.ArchiveTerminal;

public partial class ArchiveFileViewer
{
    protected sealed record BodyLineVm(string Text, string Mode);
    protected sealed record TimelineEntryVm(string Title, string Detail, string Tone);
    protected sealed record WorldMapMarkerVm(LoreSectorRecord Sector, double XPercent, double YPercent, string Tone, string RegionLabel);
    protected sealed record SectorImageFailureLogVm(string Channel, string Title, string Detail);
    protected sealed record InterferenceTraceVm(string Label, string Status, string Detail, string Tone);
    protected sealed record AwarenessStepVm(string Title, string Detail, string Outcome);
    protected sealed record ExplorerStatVm(string Label, string Value, string Detail, string Tone);
    protected sealed record GovernanceStatVm(string Label, string Value, string Detail);
    private sealed record MapAnchorVm(string Label, double XPercent, double YPercent);

    private static readonly IReadOnlyList<InterferenceTraceVm> InterferenceTraces =
    [
        new("Cameras", "Offline", "Visual capture drops on entry.", "critical"),
        new("Radio", "Desynced", "Outbound comms fail under interference.", "warning"),
        new("Sensors", "Corrupted", "Readouts fragment inside the maze.", "critical"),
        new("Exit State", "Sealed", "Entrance closes immediately after entry.", "warning"),
    ];

    private static readonly IReadOnlyList<AwarenessStepVm> AwarenessSteps =
    [
        new("Explorer enters", "An active participant stabilizes the maze state.", "Stable"),
        new("E-Unit link persists", "The anomaly continues presenting chambers and mechanisms.", "Observed"),
        new("Explorer withdraws", "Shutdown is treated as abandonment, not absence.", "Warning"),
        new("Rift terminates", "The anomaly closes itself and erases the remaining record.", "Critical"),
    ];

    private static readonly IReadOnlyList<SectorImageFailureLogVm> SectorImageFailureLogs =
    [
        new("CACHE-01", "DECODE INTERRUPTED", "Signal lost during image reconstruction."),
        new("CACHE-02", "ARCHIVE GLITCH", "Recovered fragment references a missing destination frame."),
        new("CACHE-03", "SYSTEM REDACTION", "The image exists in metadata but cannot be rendered."),
    ];

    private static readonly IReadOnlyList<TimelineEntryVm> CollapseTimeline =
    [
        new("Final puzzle resolved", "Stability breaks without external force.", "stable"),
        new("Maze folds inward", "Geometry compresses toward a single point.", "warning"),
        new("Sector remains", "The anomaly exits the world as a perfect cube.", "stable"),
    ];

    private static readonly IReadOnlyList<TimelineEntryVm> FailureTimeline =
    [
        new("E-Unit deactivated", "Anomaly detects abandonment immediately.", "critical"),
        new("Environment terminates", "No collapse cube is formed.", "critical"),
        new("Archive loss", "No spatial data or Sector Image survives.", "warning"),
    ];

    private static readonly IReadOnlyList<MapAnchorVm> PublicMapAnchors =
    [
        new("North Pacific", 11.8d, 25.5d),
        new("Western Canada", 16.4d, 21.8d),
        new("United States", 20.3d, 29.4d),
        new("Caribbean", 26.4d, 36.4d),
        new("Brazil", 29.1d, 61.8d),
        new("Andes", 26.4d, 71.6d),
        new("North Atlantic", 39.8d, 17.5d),
        new("Iberia", 45.4d, 28.1d),
        new("Mediterranean", 49.7d, 33.3d),
        new("West Africa", 47.6d, 47.8d),
        new("East Africa", 54.2d, 57.4d),
        new("Arabian Sea", 58.6d, 38.7d),
        new("Central Asia", 63.5d, 25.8d),
        new("India", 64.7d, 43.1d),
        new("East Asia", 72.2d, 30.4d),
        new("Japan", 78.4d, 27.6d),
        new("Indonesia", 74.3d, 58.7d),
        new("Australia", 82.7d, 70.1d),
    ];

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [CascadingParameter]
    public ArchiveTerminalController Controller { get; set; } = default!;

    [Parameter]
    public IReadOnlyList<LoreSectorImageRecord> SectorImages { get; set; } = [];

    [Parameter]
    public bool IsSectorImagesLoading { get; set; }

    [Parameter]
    public EventCallback<string> OnOpenFragment { get; set; }

    protected ElementReference _viewerRef;

    private string _lastRenderedFileId = string.Empty;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var selectedId = Controller.SelectedFile?.Definition.Id ?? string.Empty;
        if (string.Equals(selectedId, _lastRenderedFileId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _lastRenderedFileId = selectedId;
        await JS.InvokeVoidAsync("enigmaArchiveTerminal.playDossierTransition", _viewerRef, selectedId, Controller.SelectedFile?.Status == ArchiveFileStatus.Corrupted);
    }

    protected IReadOnlyList<BodyLineVm> GetBodyLines(ArchiveResolvedFile file)
    {
        if (file.IsFullyReadable)
        {
            return file.Definition.Paragraphs
                .Select(paragraph => new BodyLineVm(NormalizeLoreCopy(paragraph), "body"))
                .ToList();
        }

        return file.Status switch
        {
            ArchiveFileStatus.Observed =>
            [
                new(NormalizeLoreCopy(file.Definition.Paragraphs.ElementAtOrDefault(0) ?? file.Definition.Lead), "body"),
                new(NormalizeLoreCopy(file.Definition.Paragraphs.ElementAtOrDefault(1) ?? "Observer extract ends before deeper operational detail is decrypted."), "body"),
                new("PUBLIC OBSERVER EXTRACT // deeper routing records remain unavailable until this file is fully decrypted.", "notice"),
            ],
            ArchiveFileStatus.Restricted or ArchiveFileStatus.Redacted =>
            [
                new(NormalizeLoreCopy(file.Definition.Paragraphs.ElementAtOrDefault(0) ?? file.Definition.Lead), "body"),
                new(NormalizeLoreCopy(file.Definition.Paragraphs.ElementAtOrDefault(1) ?? "Restricted lines remain partially visible."), "body"),
                new("PUBLIC REDACTION APPLIED // internal annotations and concluding claims remain censored.", "redacted"),
            ],
            ArchiveFileStatus.Corrupted =>
            [
                new(NormalizeLoreCopy(file.Definition.Paragraphs.ElementAtOrDefault(0) ?? file.Definition.Lead), "body"),
                new("SIGNAL LOSS // record continuity broken // remaining lines may be incomplete or falsified.", "corrupted"),
            ],
            _ => [],
        };
    }

    protected IReadOnlyList<string> GetHighlights(ArchiveResolvedFile file)
    {
        if (file.Definition.Highlights.Count == 0)
        {
            return [];
        }

        return file.IsFullyReadable
            ? file.Definition.Highlights
            : file.Definition.Highlights.Take(2).ToList();
    }

    protected string GetNoticeClass(ArchiveResolvedFile file)
    {
        return file.Status switch
        {
            ArchiveFileStatus.Corrupted => "critical",
            ArchiveFileStatus.Restricted or ArchiveFileStatus.Redacted => "warning",
            ArchiveFileStatus.Locked => "critical",
            ArchiveFileStatus.Observed => "warning",
            _ => "stable",
        };
    }

    protected string GetNoticeLabel(ArchiveResolvedFile file)
    {
        return file.IsFullyReadable ? "Decrypted dossier" : "Layered access state";
    }

    protected string GetNoticeTitle(ArchiveResolvedFile file)
    {
        if (file.IsFullyReadable)
        {
            return "Full record available.";
        }

        return file.Status switch
        {
            ArchiveFileStatus.Observed => "Observer extract only.",
            ArchiveFileStatus.Restricted or ArchiveFileStatus.Redacted => "Public redaction applied.",
            ArchiveFileStatus.Corrupted => "Signal fracture detected.",
            _ => "Record remains sealed.",
        };
    }

    protected string GetBodyLineClass(string mode)
    {
        return mode switch
        {
            "notice" => "archive-viewer-line notice",
            "redacted" => "archive-viewer-line redacted",
            "corrupted" => "archive-viewer-line corrupted",
            _ => "archive-viewer-line",
        };
    }

    protected string GetSectorImageTitle()
    {
        if (IsSectorImagesLoading)
        {
            return "Decoding recovered fragments";
        }

        return SectorImages.Count > 0
            ? "Recovered visual fragments"
            : "Recovery subsystem glitched";
    }

    protected IReadOnlyList<WorldMapMarkerVm> GetSectorMapMarkers()
    {
        var records = GetAtlasRecords();
        if (records.Count == 0)
        {
            return [];
        }

        var availableAnchors = PublicMapAnchors.ToList();
        var markers = new List<WorldMapMarkerVm>(records.Count);

        foreach (var record in records.OrderByDescending(record => ParseFoundedAt(record.TimeFounded)))
        {
            var hash = PositiveHash(ComputeStableHash(record.Id));
            var anchorIndex = availableAnchors.Count == 0 ? 0 : hash % availableAnchors.Count;
            var anchor = availableAnchors.Count == 0
                ? PublicMapAnchors[hash % PublicMapAnchors.Count]
                : availableAnchors[anchorIndex];

            if (availableAnchors.Count > 0)
            {
                availableAnchors.RemoveAt(anchorIndex);
            }

            markers.Add(new WorldMapMarkerVm(record, anchor.XPercent, anchor.YPercent, GetSectorTone(record), anchor.Label));
        }

        return markers;
    }

    protected IReadOnlyList<LoreSectorRecord> GetRecentSectorCards(int count)
    {
        return Controller.RecentSectors.Take(count).ToList();
    }

    protected IReadOnlyList<LoreExplorerRecord> GetTopExplorerCards(int count)
    {
        return Controller.TopExplorers.Take(count).ToList();
    }

    protected IReadOnlyList<ExplorerStatVm> GetMazeNuggetLeaders()
    {
        return Controller.TopExplorers
            .OrderByDescending(explorer => explorer.MazeNuggets)
            .ThenBy(explorer => explorer.Username, StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .Select(explorer => new ExplorerStatVm(
                explorer.Username,
                $"{FormatCompactNumber(explorer.MazeNuggets)} MN",
                $"{explorer.DiscoveredMapsCount} discovered // {explorer.MapsCompleted} completed // {FormatCompletionRate(explorer)} completion rate",
                explorer.IsOnline ? "stable" : "warning"))
            .ToList();
    }

    protected IReadOnlyList<GovernanceStatVm> GetGovernanceStats()
    {
        if (!Controller.Governance.Available)
        {
            return
            [
                new GovernanceStatVm("Public state", "Unavailable", "Anonymous archive access does not expose live governance telemetry.")
            ];
        }

        return
        [
            new GovernanceStatVm("Session", Controller.Governance.Title, Controller.Governance.VotingOpen ? "Voting is currently open." : "Latest archived governance session."),
            new GovernanceStatVm("Votes cast", Controller.Governance.TotalVotesCast.ToString(CultureInfo.InvariantCulture), $"{Controller.Governance.UniqueVoterCount} unique voters"),
            new GovernanceStatVm("MN committed", FormatCompactNumber(Controller.Governance.TotalMnSpent), $"{Controller.Governance.TotalVotePower:0.##} total vote power"),
        ];
    }

    protected string GetExplorerStatusLabel(LoreExplorerRecord explorer)
    {
        return explorer.IsOnline ? "Online" : "Observed";
    }

    protected string GetExplorerTone(LoreExplorerRecord explorer)
    {
        return explorer.IsOnline ? "stable" : "warning";
    }

    protected string GetSectorTone(LoreSectorRecord sector)
    {
        return NormalizeDifficulty(sector.Difficulty) switch
        {
            "easy" => "stable",
            "medium" => "warning",
            "hard" => "critical",
            _ => sector.RatingAverage >= 4.5 ? "stable" : "warning",
        };
    }

    protected string GetSectorStat(LoreSectorRecord sector)
    {
        if (sector.RatingCount > 0)
        {
            return $"{sector.RatingAverage:0.0} rating";
        }

        if (sector.Plays > 0)
        {
            return $"{sector.Plays} plays";
        }

        return "No ratings yet";
    }

    protected string GetSectorMeta(LoreSectorRecord sector)
    {
        var theme = string.IsNullOrWhiteSpace(sector.Theme) ? "Unknown theme" : sector.Theme;
        var founder = string.IsNullOrWhiteSpace(sector.Founder) ? "Unknown founder" : sector.Founder;
        return $"{theme} // {sector.Difficulty} // {founder}";
    }

    protected string GetAtlasHeadline()
    {
        return Controller.Overview.SectorDataAvailable
            ? "Obfuscated global sector watch"
            : "Sector discovery data unavailable";
    }

    protected string GetAtlasSummary()
    {
        return Controller.ActiveSectorRecord is null
            ? "Public archive telemetry is not currently exposing sector discovery records."
            : $"{Controller.ActiveSectorRecord.MapName} // {Controller.ActiveSectorRecord.TimeFoundedDisplay}";
    }

    protected string GetRadarSummary()
    {
        return Controller.Overview.SectorDataAvailable
            ? $"{Controller.Overview.SectorCount} archived sectors currently visible through the public relay."
            : "No live sector telemetry is currently available to the public relay.";
    }

    protected string GetEUnitStageStyle()
    {
        return "--room-stage-tint-start:#23374a;" +
               "--room-stage-tint-end:#0a1018;" +
               "--radar-x:50%;" +
               "--radar-y:56%;" +
               "--radar-core-radius:12%;" +
               "--radar-detail-radius:30%;" +
               "--radar-outer-radius:56%;" +
               "--radar-pulse-scale:3.18;" +
               "--radar-cycle:5.6s;" +
               "--radar-primary-spacing:2.8s;" +
               "--radar-secondary-spacing:0.933s;" +
               "--radar-pulse-strength:0.42;" +
               "--radar-wall-proximity:1;" +
               "--radar-interference:0;";
    }

    protected string GetExplorerCompletionRateLabel(LoreExplorerRecord explorer)
    {
        return $"{FormatCompletionRate(explorer)} completion rate";
    }

    private IReadOnlyList<LoreSectorRecord> GetAtlasRecords()
    {
        return Controller.SectorAtlas.Count > 0
            ? Controller.SectorAtlas.Take(18).ToList()
            : Controller.RecentSectors.Take(12).ToList();
    }

    private static string NormalizeDifficulty(string difficulty)
    {
        return difficulty.Trim().ToLowerInvariant() switch
        {
            "easy" => "easy",
            "medium" => "medium",
            "hard" => "hard",
            _ => "unknown",
        };
    }

    private static string FormatCompletionRate(LoreExplorerRecord explorer)
    {
        return explorer.CompletionRate.ToString("0.#%", CultureInfo.InvariantCulture);
    }

    private static string NormalizeLoreCopy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value
            .Replace("Ã¢â‚¬â„¢", "'", StringComparison.Ordinal)
            .Replace("â€¦", "...", StringComparison.Ordinal);
    }

    private static DateTimeOffset ParseFoundedAt(string raw)
    {
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
    }

    private static int ComputeStableHash(string input)
    {
        unchecked
        {
            var hash = 23;
            foreach (var character in input)
            {
                hash = (hash * 31) + character;
            }

            return hash;
        }
    }

    private static int PositiveHash(int value)
    {
        return (int)((uint)value & 0x7FFFFFFF);
    }

    private static double ClampPercent(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private static string FormatCompactNumber(long value)
    {
        return value switch
        {
            >= 1_000_000_000 => $"{value / 1_000_000_000d:0.#}B",
            >= 1_000_000 => $"{value / 1_000_000d:0.#}M",
            >= 1_000 => $"{value / 1_000d:0.#}K",
            _ => value.ToString(CultureInfo.InvariantCulture),
        };
    }
}
