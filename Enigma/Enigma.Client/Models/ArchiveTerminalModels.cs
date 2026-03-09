namespace Enigma.Client.Models;

public enum ArchiveFileStatus
{
    Open,
    Locked,
    Corrupted,
    Restricted,
    Hidden,
    Observed,
    Volatile,
    Archived,
    Redacted,
}

public enum ArchiveRightPanelType
{
    GeospatialWatch,
    InterferenceMatrix,
    RadarReconstruction,
    ExplorerRoster,
    CollapseSequence,
    LossPlayback,
    BehaviorMonitor,
    CubeAnalysis,
    SpatialReconstruction,
    RecoveredImageCache,
    MaterialAnalysis,
    GovernanceLattice,
    FinalRedaction,
}

public enum ArchiveUnlockRuleType
{
    ViewedFile,
    ViewedFragment,
    SelectedFileCount,
}

public enum ArchiveDirectoryFilter
{
    All,
    Open,
    Restricted,
    Corrupted,
}

public enum ArchiveFileAccessTier
{
    Hidden,
    Denied,
    Partial,
    Full,
}

public sealed class ArchiveUnlockRule
{
    public ArchiveUnlockRuleType Type { get; init; }
    public string Value { get; init; } = string.Empty;
    public int Count { get; init; }
}

public sealed class ArchiveMetricDefinition
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

public sealed class ArchiveFileDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string Classification { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public ArchiveFileStatus Status { get; init; }
    public string ThreatLevel { get; init; } = string.Empty;
    public string SignalState { get; init; } = string.Empty;
    public bool IsUnlocked { get; init; }
    public bool IsHidden { get; init; }
    public bool IsCorrupted { get; init; }
    public bool IsRedacted { get; init; }
    public IReadOnlyList<ArchiveUnlockRule> UnlockRequirements { get; init; } = [];
    public string FragmentLabel { get; init; } = string.Empty;
    public string FragmentContent { get; init; } = string.Empty;
    public IReadOnlyList<string> Categories { get; init; } = [];
    public int SortOrder { get; init; }
    public IReadOnlyList<string> RelatedFiles { get; init; } = [];
    public ArchiveRightPanelType RightPanelType { get; init; }
    public IReadOnlyList<ArchiveMetricDefinition> Metrics { get; init; } = [];
    public string VisualTheme { get; init; } = string.Empty;
    public string LastUpdatedText { get; init; } = string.Empty;
    public string PreviewText { get; init; } = string.Empty;
    public IReadOnlyList<string> PreviewFacts { get; init; } = [];
    public string PrimaryAccent { get; init; } = string.Empty;
    public string SecondaryAccent { get; init; } = string.Empty;
    public string Lead { get; init; } = string.Empty;
    public IReadOnlyList<string> Paragraphs { get; init; } = [];
    public IReadOnlyList<string> Highlights { get; init; } = [];
    public IReadOnlyList<ArchiveUnlockRule> FragmentUnlockRequirements { get; init; } = [];
    public ArchiveFileStatus UnlockedStatus { get; init; } = ArchiveFileStatus.Open;

    public string CanonicalHref => LoreArchiveContent.GetCanonicalFileHref(Id);
}

public sealed class ArchiveTerminalStateSnapshot
{
    public bool BootSeen { get; set; }
    public string LastOpenedFileId { get; set; } = string.Empty;
    public List<string> ViewedFileIds { get; set; } = [];
    public List<string> ViewedFragmentIds { get; set; } = [];
    public List<string> NewlyUnlockedIds { get; set; } = [];
    public string LastAccessedUtc { get; set; } = string.Empty;
}

public sealed class ArchiveResolvedFile
{
    public required ArchiveFileDefinition Definition { get; init; }
    public required ArchiveFileStatus Status { get; init; }
    public required ArchiveFileAccessTier AccessTier { get; init; }
    public required bool IsVisible { get; init; }
    public required bool IsViewed { get; init; }
    public required bool IsNewlyUnlocked { get; init; }
    public required bool IsFragmentViewed { get; init; }
    public required bool CanOpenFragment { get; init; }

    public bool CanSelect => AccessTier is ArchiveFileAccessTier.Partial or ArchiveFileAccessTier.Full;
    public bool IsFullyReadable => AccessTier == ArchiveFileAccessTier.Full;
    public bool IsPartialReadable => AccessTier == ArchiveFileAccessTier.Partial;
}

public sealed class ArchiveDirectoryGroupDefinition
{
    public string Key { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<string> FileIds { get; init; } = [];
}

public sealed class ArchiveRouteResolution
{
    public string SelectedFileId { get; init; } = string.Empty;
    public string CanonicalUrl { get; init; } = string.Empty;
    public bool RequiresNavigation { get; init; }
    public bool ShowFullBoot { get; init; }
    public bool ShowReentryFlash { get; init; }
}

public sealed class ArchiveSelectionResult
{
    public bool WasDenied { get; init; }
    public bool SelectedChanged { get; init; }
    public bool RequiresTransition { get; init; }
    public bool TriggerCorruptionPulse { get; init; }
    public string CanonicalUrl { get; init; } = string.Empty;
    public string DeniedMessage { get; init; } = string.Empty;
    public IReadOnlyList<string> NewlyUnlockedIds { get; init; } = [];
}

public sealed class ArchiveFragmentResult
{
    public bool Opened { get; init; }
    public IReadOnlyList<string> NewlyUnlockedIds { get; init; } = [];
}

public static class ArchiveTerminalStorageKeys
{
    public const string BootSeen = "enigma.archive.boot-seen.v1";
    public const string State = "enigma.archive.state.v1";
    public const string LastFile = "enigma.archive.last-file.v1";
}
