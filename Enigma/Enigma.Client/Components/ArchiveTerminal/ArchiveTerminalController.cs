using System.Globalization;
using System.Text.Json;
using Enigma.Client.Models;
using Microsoft.JSInterop;

namespace Enigma.Client.Components.ArchiveTerminal;

public sealed class ArchiveTerminalController
{
    private readonly Dictionary<string, ArchiveResolvedFile> _resolvedFiles =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedFileIds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ArchiveSystemLogEntry> _systemLog = [];

    private ArchiveTerminalStateSnapshot _snapshot = new();
    private LoreGovernanceSnapshot? _authenticatedGovernance;
    private string _activeSectorId = string.Empty;

    public bool IsReady { get; private set; }
    public bool ShowBootSequence { get; private set; }
    public bool ShowReentryFlash { get; private set; }
    public string SelectedFileId { get; private set; } = string.Empty;
    public string PreviewFileId { get; private set; } = string.Empty;
    public string FocusedFileId { get; private set; } = string.Empty;
    public string FragmentFileId { get; private set; } = string.Empty;
    public ArchiveDirectoryFilter ActiveFilter { get; private set; } = ArchiveDirectoryFilter.All;
    public bool IsSidebarOpen { get; private set; }
    public string CurrentSystemMessage { get; private set; } = "Awaiting live archive telemetry";
    public string UnlockNotification { get; private set; } = string.Empty;
    public LoreTelemetryResponse LiveTelemetry { get; private set; } = CreateUnavailableTelemetry();
    public IReadOnlyList<LoreSectorRecord> SectorAtlas { get; private set; } = [];
    public DateTimeOffset? LastLiveDataRefreshUtc { get; private set; }

    public IReadOnlyList<ArchiveDirectoryGroupDefinition> Groups => LoreArchiveContent.DirectoryGroups;
    public IReadOnlyCollection<string> ViewedFileIds => _snapshot.ViewedFileIds;
    public IReadOnlyCollection<string> ViewedFragmentIds => _snapshot.ViewedFragmentIds;
    public IReadOnlyList<ArchiveSystemLogEntry> SystemLogEntries => _systemLog;
    public ArchiveResolvedFile? SelectedFile => TryGetResolvedFile(SelectedFileId);
    public ArchiveResolvedFile? PreviewFile => TryGetResolvedFile(string.IsNullOrWhiteSpace(PreviewFileId) ? SelectedFileId : PreviewFileId);
    public ArchiveResolvedFile? FragmentFile => TryGetResolvedFile(FragmentFileId);
    public bool IsFragmentOpen => !string.IsNullOrWhiteSpace(FragmentFileId);
    public DateTimeOffset? LastAccessedUtc => ParseTimestamp(_snapshot.LastAccessedUtc);
    public LoreTelemetryOverview Overview => LiveTelemetry.Overview;
    public IReadOnlyList<LoreSectorRecord> RecentSectors => LiveTelemetry.RecentSectors;
    public IReadOnlyList<LoreExplorerRecord> TopExplorers => LiveTelemetry.TopExplorers;
    public LoreGovernanceSnapshot Governance => _authenticatedGovernance ?? LiveTelemetry.Governance;
    public LoreSectorRecord? ActiveSectorRecord => ResolveActiveSectorRecord();

    public async Task<ArchiveRouteResolution> InitializeAsync(IJSRuntime js, string? requestedFileId, string? legacyGroup)
    {
        _snapshot = await LoadSnapshotAsync(js);
        CleanupSnapshot();

        ShowBootSequence = !_snapshot.BootSeen;
        ShowReentryFlash = _snapshot.BootSeen;

        RebuildResolvedFiles();

        var resolution = ResolveRoute(requestedFileId, legacyGroup);
        ApplySelection(resolution.SelectedFileId, markViewed: true, clearPreview: true);
        if (_systemLog.Count == 0)
        {
            AppendSystemLog("Archive terminal initialized // awaiting live feeds", "warning");
        }

        IsReady = true;
        return resolution;
    }

    public ArchiveRouteResolution ApplyRoute(string? requestedFileId, string? legacyGroup)
    {
        if (!IsReady)
        {
            return new ArchiveRouteResolution
            {
                SelectedFileId = LoreArchiveContent.DefaultFile.Id,
                CanonicalUrl = LoreArchiveContent.GetCanonicalFileHref(LoreArchiveContent.DefaultFile.Id),
            };
        }

        RebuildResolvedFiles();
        var resolution = ResolveRoute(requestedFileId, legacyGroup);
        ApplySelection(resolution.SelectedFileId, markViewed: true, clearPreview: false);
        return resolution;
    }

    public async Task PersistAsync(IJSRuntime js)
    {
        _snapshot.LastOpenedFileId = SelectedFileId;
        _snapshot.LastAccessedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        await js.InvokeVoidAsync("enigmaArchiveTerminal.setStorageItem", "local", ArchiveTerminalStorageKeys.BootSeen, _snapshot.BootSeen ? "true" : "false");
        await js.InvokeVoidAsync("enigmaArchiveTerminal.setStorageItem", "local", ArchiveTerminalStorageKeys.LastFile, _snapshot.LastOpenedFileId);
        await js.InvokeVoidAsync("enigmaArchiveTerminal.setStorageItem", "local", ArchiveTerminalStorageKeys.State, JsonSerializer.Serialize(_snapshot));
    }

    public void CompleteBoot()
    {
        _snapshot.BootSeen = true;
        ShowBootSequence = false;
        ShowReentryFlash = false;
        AppendSystemLog("Observer terminal access granted", "stable");
    }

    public ArchiveSelectionResult SelectFile(string fileId)
    {
        UnlockNotification = string.Empty;

        if (!_resolvedFiles.TryGetValue(fileId, out var resolved))
        {
            var missingMessage = "RECORD NOT FOUND IN CURRENT INDEX";
            AppendSystemLog(missingMessage, "critical");
            return new ArchiveSelectionResult
            {
                WasDenied = true,
                CanonicalUrl = LoreArchiveContent.GetCanonicalFileHref(SelectedFileId),
                DeniedMessage = missingMessage,
            };
        }

        if (!resolved.CanSelect)
        {
            var deniedMessage = $"ACCESS DENIED // {resolved.Definition.Title.ToUpperInvariant()}";
            AppendSystemLog(deniedMessage, "critical");
            return new ArchiveSelectionResult
            {
                WasDenied = true,
                CanonicalUrl = LoreArchiveContent.GetCanonicalFileHref(SelectedFileId),
                DeniedMessage = deniedMessage,
            };
        }

        var previouslyFull = GetFullyReadableIds();
        var changed = !string.Equals(SelectedFileId, fileId, StringComparison.OrdinalIgnoreCase);

        ApplySelection(fileId, markViewed: true, clearPreview: false);

        var newlyUnlockedIds = PromoteNewUnlocks(previouslyFull);
        UnlockNotification = BuildUnlockNotification(newlyUnlockedIds);
        AppendSystemLog($"Accessed {resolved.Definition.Code} // {resolved.Definition.Title}", GetToneForStatus(resolved.Status));

        if (newlyUnlockedIds.Count > 0)
        {
            AppendSystemLog(UnlockNotification, "stable");
        }

        return new ArchiveSelectionResult
        {
            SelectedChanged = changed,
            RequiresTransition = changed,
            TriggerCorruptionPulse = resolved.Status == ArchiveFileStatus.Corrupted,
            CanonicalUrl = LoreArchiveContent.GetCanonicalFileHref(fileId),
            NewlyUnlockedIds = newlyUnlockedIds,
        };
    }

    public ArchiveFragmentResult OpenFragment(string fileId)
    {
        UnlockNotification = string.Empty;

        if (!_resolvedFiles.TryGetValue(fileId, out var resolved) || !resolved.CanOpenFragment)
        {
            return new ArchiveFragmentResult();
        }

        var previouslyFull = GetFullyReadableIds();
        FragmentFileId = fileId;
        AddUnique(_snapshot.ViewedFragmentIds, fileId);
        var newlyUnlockedIds = PromoteNewUnlocks(previouslyFull);
        UnlockNotification = BuildUnlockNotification(newlyUnlockedIds);
        AppendSystemLog($"Recovered fragment // {resolved.Definition.Code} // {resolved.Definition.FragmentLabel}", "warning");

        if (newlyUnlockedIds.Count > 0)
        {
            AppendSystemLog(UnlockNotification, "stable");
        }

        return new ArchiveFragmentResult
        {
            Opened = true,
            NewlyUnlockedIds = newlyUnlockedIds,
        };
    }

    public void CloseFragment()
    {
        FragmentFileId = string.Empty;
    }

    public void SetPreviewFile(string? fileId)
    {
        if (string.IsNullOrWhiteSpace(fileId))
        {
            PreviewFileId = string.Empty;
            return;
        }

        PreviewFileId = _resolvedFiles.ContainsKey(fileId) ? fileId : string.Empty;
    }

    public void SetFocusedFile(string? fileId)
    {
        FocusedFileId = fileId ?? string.Empty;
        SetPreviewFile(fileId);
    }

    public void SetFilter(ArchiveDirectoryFilter filter)
    {
        ActiveFilter = filter;
    }

    public void ToggleSidebar()
    {
        IsSidebarOpen = !IsSidebarOpen;
    }

    public void CloseSidebar()
    {
        IsSidebarOpen = false;
    }

    public void ApplyTelemetrySnapshot(LoreTelemetryResponse? telemetry)
    {
        LiveTelemetry = telemetry ?? CreateUnavailableTelemetry();
        LastLiveDataRefreshUtc = DateTimeOffset.UtcNow;
        EnsureActiveSectorSelection();

        var tone = LiveTelemetry.Status switch
        {
            "success" => "stable",
            "partial" => "warning",
            _ => "critical",
        };

        var message = LiveTelemetry.Status switch
        {
            "success" => $"Telemetry synced // {Overview.SectorCount} sectors // {Overview.ExplorerCount} explorers",
            "partial" => "Telemetry synced with public redactions",
            _ => "Live telemetry unavailable",
        };

        AppendSystemLog(message, tone);
    }

    public void ApplySectorAtlasSnapshot(LoreSectorAtlasResponse? atlas)
    {
        SectorAtlas = atlas?.Sectors ?? [];
        LastLiveDataRefreshUtc = DateTimeOffset.UtcNow;
        EnsureActiveSectorSelection();

        var tone = atlas?.Status switch
        {
            "success" => "stable",
            "empty" => "warning",
            _ => "critical",
        };
        var message = atlas?.Status switch
        {
            "success" => $"Sector discovery atlas synced // {SectorAtlas.Count} records",
            "empty" => "Sector discovery atlas returned no records",
            _ => "Sector discovery atlas unavailable",
        };

        AppendSystemLog(message, tone);
    }

    public void ApplyAuthenticatedGovernanceSnapshot(LoreGovernanceSnapshot? governance)
    {
        _authenticatedGovernance = governance?.Available == true ? governance : null;

        if (_authenticatedGovernance is not null)
        {
            AppendSystemLog(
                _authenticatedGovernance.VotingOpen
                    ? $"Governance feed synced // {_authenticatedGovernance.Title}"
                    : $"Governance archive synced // {_authenticatedGovernance.Title}",
                "stable");
        }
        else if (!LiveTelemetry.Governance.Available)
        {
            AppendSystemLog("Public live governance data unavailable", "warning");
        }
    }

    public void RegisterSectorImagesState(int imageCount, bool requestFailed)
    {
        if (requestFailed)
        {
            AppendSystemLog("Recovered image cache unavailable", "critical");
            return;
        }

        if (imageCount > 0)
        {
            AppendSystemLog($"Recovered image cache synced // {imageCount} fragments", "stable");
            return;
        }

        AppendSystemLog("Recovered image cache returned no image fragments", "warning");
    }

    public void SetActiveSector(string? sectorId)
    {
        if (string.IsNullOrWhiteSpace(sectorId))
        {
            return;
        }

        if (FindSectorRecord(sectorId) is not null)
        {
            _activeSectorId = sectorId;
        }
    }

    public IReadOnlyList<ArchiveResolvedFile> GetFilesForGroup(string groupKey)
    {
        var group = Groups.FirstOrDefault(entry => string.Equals(entry.Key, groupKey, StringComparison.OrdinalIgnoreCase));
        if (group is null)
        {
            return [];
        }

        return group.FileIds
            .Select(TryGetResolvedFile)
            .Where(file => file is not null && file.IsVisible && MatchesFilter(file))
            .Cast<ArchiveResolvedFile>()
            .ToList();
    }

    public IReadOnlyList<ArchiveResolvedFile> GetVisibleFiles()
    {
        return LoreArchiveContent.Files
            .Select(file => TryGetResolvedFile(file.Id))
            .Where(file => file is not null && file.IsVisible && MatchesFilter(file))
            .Cast<ArchiveResolvedFile>()
            .ToList();
    }

    public int GetDecryptedFileCount()
    {
        return _resolvedFiles.Values.Count(file => file.IsFullyReadable);
    }

    public ArchiveResolvedFile? TryGetResolvedFile(string? fileId)
    {
        if (string.IsNullOrWhiteSpace(fileId))
        {
            return null;
        }

        return _resolvedFiles.TryGetValue(fileId, out var file)
            ? file
            : null;
    }

    public bool IsFilterActive(ArchiveDirectoryFilter filter) => ActiveFilter == filter;

    public string GetDirectoryStatusLabel(ArchiveResolvedFile file)
    {
        return file.Status switch
        {
            ArchiveFileStatus.Locked => "Locked",
            ArchiveFileStatus.Restricted => "Restricted",
            ArchiveFileStatus.Redacted => "Redacted",
            ArchiveFileStatus.Corrupted => "Corrupted",
            ArchiveFileStatus.Observed => "Observed",
            ArchiveFileStatus.Volatile => "Volatile",
            _ => "Open",
        };
    }

    public string GetAccessLabel(ArchiveResolvedFile file)
    {
        return file.AccessTier switch
        {
            ArchiveFileAccessTier.Full => "Full record access",
            ArchiveFileAccessTier.Partial => "Observer extract",
            ArchiveFileAccessTier.Denied => "Access denied",
            _ => "Hidden record",
        };
    }

    public string? GetDirectoryLiveFact(ArchiveResolvedFile file)
    {
        return file.Definition.RightPanelType switch
        {
            ArchiveRightPanelType.GeospatialWatch => Overview.SectorDataAvailable
                ? $"{Overview.SectorCount} live sectors indexed"
                : "Sector telemetry unavailable",
            ArchiveRightPanelType.ExplorerRoster => Overview.ExplorerDataAvailable && TopExplorers.Count > 0
                ? $"Top explorer {TopExplorers[0].Username}"
                : "Explorer telemetry unavailable",
            ArchiveRightPanelType.CubeAnalysis => Overview.SectorDataAvailable
                ? $"{RecentSectors.Count} recent sector recoveries"
                : "Sector telemetry unavailable",
            ArchiveRightPanelType.RecoveredImageCache => Overview.SectorDataAvailable
                ? $"{RecentSectors.Count(sector => !string.IsNullOrWhiteSpace(sector.MapImage))} image-ready sectors"
                : "Image telemetry unavailable",
            ArchiveRightPanelType.MaterialAnalysis => Overview.ExplorerDataAvailable && TopExplorers.Count > 0
                ? $"{FormatCompactNumber(TopExplorers[0].MazeNuggets)} MN held by current leader"
                : "MN telemetry unavailable",
            ArchiveRightPanelType.GovernanceLattice => Governance.Available
                ? (Governance.VotingOpen ? "Live governance session" : "Archived governance session")
                : "Public governance feed unavailable",
            _ => null,
        };
    }

    private ArchiveRouteResolution ResolveRoute(string? requestedFileId, string? legacyGroup)
    {
        string? selectedId = null;
        var currentFile = GetAccessibleFile(requestedFileId);
        if (currentFile is not null)
        {
            selectedId = currentFile.Id;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(requestedFileId) &&
                LoreArchiveContent.TryResolveLegacyGroupDefaultFileId(legacyGroup, out var legacyDefault))
            {
                selectedId = GetAccessibleFile(legacyDefault)?.Id;
            }

            if (string.IsNullOrWhiteSpace(selectedId))
            {
                selectedId = GetAccessibleFile(_snapshot.LastOpenedFileId)?.Id;
            }
        }

        selectedId ??= GetFirstAccessibleFileId();
        var canonicalUrl = LoreArchiveContent.GetCanonicalFileHref(selectedId);
        var requiresNavigation =
            !string.IsNullOrWhiteSpace(legacyGroup) ||
            !string.Equals(selectedId, requestedFileId, StringComparison.OrdinalIgnoreCase);

        return new ArchiveRouteResolution
        {
            SelectedFileId = selectedId,
            CanonicalUrl = canonicalUrl,
            RequiresNavigation = requiresNavigation,
            ShowFullBoot = ShowBootSequence,
            ShowReentryFlash = ShowReentryFlash,
        };
    }

    private ArchiveFileDefinition? GetAccessibleFile(string? fileId)
    {
        var resolved = TryGetResolvedFile(fileId);
        return resolved is not null && resolved.CanSelect
            ? resolved.Definition
            : null;
    }

    private string GetFirstAccessibleFileId()
    {
        foreach (var file in LoreArchiveContent.Files.OrderBy(file => file.SortOrder))
        {
            var resolved = TryGetResolvedFile(file.Id);
            if (resolved?.CanSelect == true)
            {
                return file.Id;
            }
        }

        return LoreArchiveContent.DefaultFile.Id;
    }

    private void ApplySelection(string fileId, bool markViewed, bool clearPreview)
    {
        if (!_resolvedFiles.TryGetValue(fileId, out var resolved))
        {
            return;
        }

        SelectedFileId = fileId;
        _snapshot.LastOpenedFileId = fileId;
        AddUniqueToSelection(fileId);

        if (clearPreview || string.IsNullOrWhiteSpace(PreviewFileId))
        {
            PreviewFileId = fileId;
        }

        if (markViewed && resolved.IsFullyReadable)
        {
            AddUnique(_snapshot.ViewedFileIds, fileId);
        }

        _snapshot.NewlyUnlockedIds.RemoveAll(id => string.Equals(id, fileId, StringComparison.OrdinalIgnoreCase));
        _snapshot.LastAccessedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        RebuildResolvedFiles();
    }

    private IReadOnlyList<string> PromoteNewUnlocks(HashSet<string> previouslyFull)
    {
        RebuildResolvedFiles();
        var currentFull = GetFullyReadableIds();
        var newlyUnlocked = currentFull
            .Where(id => !previouslyFull.Contains(id) && !ContainsIgnoreCase(_snapshot.ViewedFileIds, id))
            .ToList();

        foreach (var id in newlyUnlocked)
        {
            AddUnique(_snapshot.NewlyUnlockedIds, id);
        }

        return newlyUnlocked;
    }

    private HashSet<string> GetFullyReadableIds()
    {
        return _resolvedFiles.Values
            .Where(file => file.IsFullyReadable)
            .Select(file => file.Definition.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void RebuildResolvedFiles()
    {
        _resolvedFiles.Clear();

        foreach (var definition in LoreArchiveContent.Files.OrderBy(file => file.SortOrder))
        {
            var resolved = ResolveFile(definition);
            if (resolved.IsVisible)
            {
                _resolvedFiles[definition.Id] = resolved;
            }
        }
    }

    private ArchiveResolvedFile ResolveFile(ArchiveFileDefinition definition)
    {
        var requirementsMet = definition.IsUnlocked || RequirementsMet(definition.UnlockRequirements);
        var isHidden = definition.IsHidden && !requirementsMet;

        ArchiveFileAccessTier accessTier;
        ArchiveFileStatus status;

        if (isHidden)
        {
            accessTier = ArchiveFileAccessTier.Hidden;
            status = ArchiveFileStatus.Hidden;
        }
        else if (requirementsMet)
        {
            accessTier = ArchiveFileAccessTier.Full;
            status = definition.UnlockedStatus;
        }
        else
        {
            status = definition.Status;
            accessTier = definition.Status switch
            {
                ArchiveFileStatus.Locked => ArchiveFileAccessTier.Denied,
                ArchiveFileStatus.Hidden => ArchiveFileAccessTier.Hidden,
                _ => ArchiveFileAccessTier.Partial,
            };
        }

        var canOpenFragment =
            !string.IsNullOrWhiteSpace(definition.FragmentContent) &&
            accessTier is ArchiveFileAccessTier.Partial or ArchiveFileAccessTier.Full &&
            RequirementsMet(definition.FragmentUnlockRequirements);

        return new ArchiveResolvedFile
        {
            Definition = definition,
            Status = status,
            AccessTier = accessTier,
            IsVisible = accessTier != ArchiveFileAccessTier.Hidden,
            IsViewed = ContainsIgnoreCase(_snapshot.ViewedFileIds, definition.Id),
            IsNewlyUnlocked = ContainsIgnoreCase(_snapshot.NewlyUnlockedIds, definition.Id),
            IsFragmentViewed = ContainsIgnoreCase(_snapshot.ViewedFragmentIds, definition.Id),
            CanOpenFragment = canOpenFragment,
        };
    }

    private bool RequirementsMet(IReadOnlyList<ArchiveUnlockRule> requirements)
    {
        if (requirements.Count == 0)
        {
            return true;
        }

        foreach (var rule in requirements)
        {
            var satisfied = rule.Type switch
            {
                ArchiveUnlockRuleType.ViewedFile => ContainsIgnoreCase(_snapshot.ViewedFileIds, rule.Value),
                ArchiveUnlockRuleType.ViewedFragment => ContainsIgnoreCase(_snapshot.ViewedFragmentIds, rule.Value),
                ArchiveUnlockRuleType.SelectedFileCount => _selectedFileIds.Count >= rule.Count,
                _ => false,
            };

            if (!satisfied)
            {
                return false;
            }
        }

        return true;
    }

    private bool MatchesFilter(ArchiveResolvedFile? file)
    {
        if (file is null)
        {
            return false;
        }

        return ActiveFilter switch
        {
            ArchiveDirectoryFilter.Open => file.Status == ArchiveFileStatus.Open,
            ArchiveDirectoryFilter.Restricted => file.Status is ArchiveFileStatus.Restricted or ArchiveFileStatus.Redacted or ArchiveFileStatus.Observed,
            ArchiveDirectoryFilter.Corrupted => file.Status is ArchiveFileStatus.Corrupted or ArchiveFileStatus.Volatile,
            _ => true,
        };
    }

    private static string BuildUnlockNotification(IReadOnlyList<string> newlyUnlockedIds)
    {
        if (newlyUnlockedIds.Count == 0)
        {
            return string.Empty;
        }

        var file = LoreArchiveContent.GetFileById(newlyUnlockedIds[0]);
        return file is null
            ? "NEW RECORD AVAILABLE"
            : $"NEW RECORD AVAILABLE // {file.Code} // {file.Title.ToUpperInvariant()}";
    }

    private async Task<ArchiveTerminalStateSnapshot> LoadSnapshotAsync(IJSRuntime js)
    {
        try
        {
            var stateJson = await js.InvokeAsync<string?>("enigmaArchiveTerminal.getStorageItem", "local", ArchiveTerminalStorageKeys.State);
            var lastFile = await js.InvokeAsync<string?>("enigmaArchiveTerminal.getStorageItem", "local", ArchiveTerminalStorageKeys.LastFile);
            var bootSeen = await js.InvokeAsync<string?>("enigmaArchiveTerminal.getStorageItem", "local", ArchiveTerminalStorageKeys.BootSeen);

            ArchiveTerminalStateSnapshot? snapshot = null;
            if (!string.IsNullOrWhiteSpace(stateJson))
            {
                snapshot = JsonSerializer.Deserialize<ArchiveTerminalStateSnapshot>(stateJson);
            }

            snapshot ??= new ArchiveTerminalStateSnapshot();
            snapshot.LastOpenedFileId = string.IsNullOrWhiteSpace(lastFile) ? snapshot.LastOpenedFileId : lastFile.Trim();
            if (bool.TryParse(bootSeen, out var parsedBootSeen))
            {
                snapshot.BootSeen = parsedBootSeen;
            }

            return snapshot;
        }
        catch
        {
            return new ArchiveTerminalStateSnapshot();
        }
    }

    private void CleanupSnapshot()
    {
        _snapshot.ViewedFileIds = NormalizeIds(_snapshot.ViewedFileIds);
        _snapshot.ViewedFragmentIds = NormalizeIds(_snapshot.ViewedFragmentIds);
        _snapshot.NewlyUnlockedIds = NormalizeIds(_snapshot.NewlyUnlockedIds)
            .Where(id => !ContainsIgnoreCase(_snapshot.ViewedFileIds, id))
            .ToList();

        if (LoreArchiveContent.GetFileById(_snapshot.LastOpenedFileId) is null)
        {
            _snapshot.LastOpenedFileId = string.Empty;
        }
    }

    private static List<string> NormalizeIds(IEnumerable<string>? source)
    {
        return (source ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id) && LoreArchiveContent.GetFileById(id) is not null)
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void EnsureActiveSectorSelection()
    {
        if (FindSectorRecord(_activeSectorId) is not null)
        {
            return;
        }

        _activeSectorId = SectorAtlas.FirstOrDefault()?.Id
            ?? RecentSectors.FirstOrDefault()?.Id
            ?? string.Empty;
    }

    private LoreSectorRecord? ResolveActiveSectorRecord()
    {
        var active = FindSectorRecord(_activeSectorId);
        return active ?? SectorAtlas.FirstOrDefault() ?? RecentSectors.FirstOrDefault();
    }

    private LoreSectorRecord? FindSectorRecord(string? sectorId)
    {
        if (string.IsNullOrWhiteSpace(sectorId))
        {
            return null;
        }

        return SectorAtlas.FirstOrDefault(sector => string.Equals(sector.Id, sectorId, StringComparison.OrdinalIgnoreCase))
            ?? RecentSectors.FirstOrDefault(sector => string.Equals(sector.Id, sectorId, StringComparison.OrdinalIgnoreCase));
    }

    private void AppendSystemLog(string message, string tone)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        CurrentSystemMessage = message;
        _systemLog.Insert(0, new ArchiveSystemLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            Message = message,
            Tone = tone,
        });

        if (_systemLog.Count > 8)
        {
            _systemLog.RemoveRange(8, _systemLog.Count - 8);
        }
    }

    private static LoreTelemetryResponse CreateUnavailableTelemetry()
    {
        return new LoreTelemetryResponse
        {
            Status = "error",
            Governance = LoreGovernanceSnapshot.Unavailable(),
            Overview = new LoreTelemetryOverview(),
        };
    }

    private static bool ContainsIgnoreCase(IEnumerable<string> source, string value)
    {
        return source.Any(entry => string.Equals(entry, value, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddUnique(List<string> target, string value)
    {
        if (!ContainsIgnoreCase(target, value))
        {
            target.Add(value);
        }
    }

    private void AddUniqueToSelection(string fileId)
    {
        _selectedFileIds.Add(fileId);
    }

    private static DateTimeOffset? ParseTimestamp(string raw)
    {
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp)
            ? timestamp
            : null;
    }

    private static string GetToneForStatus(ArchiveFileStatus status)
    {
        return status switch
        {
            ArchiveFileStatus.Corrupted or ArchiveFileStatus.Locked => "critical",
            ArchiveFileStatus.Restricted or ArchiveFileStatus.Redacted or ArchiveFileStatus.Observed => "warning",
            _ => "stable",
        };
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
