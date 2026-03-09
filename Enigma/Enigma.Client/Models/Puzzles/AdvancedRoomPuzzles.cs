namespace Enigma.Client.Models.Gameplay;

using System.Globalization;

public readonly record struct SoloPanelHudItem(string Label, string Value, string Tone = "shared");
public readonly record struct SoloPanelActionItem(string Command, string Label, string Tone = "shared", bool Enabled = true, bool Active = false);
public sealed record SoloPanelView(
    string FamilyId,
    int TierLevel,
    PuzzleStatus Status,
    PuzzlePhase Phase,
    string StatusText,
    double Progress,
    double TimerRemainingSeconds,
    int AttemptsRemaining,
    string FailureCode,
    string FailureLabel,
    string RecoveryText,
    string ProgressLabel,
    double ProgressValue,
    string ProgressTrend,
    string StageVisualProfile,
    IReadOnlyList<SoloPanelHudItem> Hud,
    IReadOnlyList<SoloPanelActionItem> Actions,
    IReadOnlyDictionary<string, string> Board);

public interface ISoloPanelPuzzle
{
    string FamilyId { get; }
    int TierLevel { get; }
    bool TierInitialized { get; }
    double RewardPickupMultiplier { get; }
    void EnsureTierLevel(int solvedRoomsBeforeCurrent, int totalPuzzleRooms);
    SoloPanelView BuildPanelView(double nowSeconds);
    bool ApplyAction(string command, double nowSeconds);
}

public interface ISolverBackedPanelPuzzle
{
    bool TryBuildPanelSolveTrace(out PuzzleSolveTrace trace);
}

internal enum SoloPanelMechanic
{
    DialCommit,
    ChromaticLock,
    SignalDecay,
    CoupledSystem,
    CellCommit,
    PipeRotate,
    MemoryPair,
}

public sealed partial class SoloPanelBiblePuzzle : RoomPuzzle, ISoloPanelPuzzle, ISolverBackedPanelPuzzle
{
    private sealed record FailureLanguage(string Code, string Label, string VisualCue, string Recovery);

    private static readonly Dictionary<char, Dictionary<string, FailureLanguage>> FailureLanguages = new()
    {
        ['p'] = new Dictionary<string, FailureLanguage>(StringComparer.OrdinalIgnoreCase)
        {
            ["phase_drift"] = new("phase_drift", "Phase Drift", "Waveform shear detected.", "Recenter channels before attempting lock."),
            ["stability_loss"] = new("stability_loss", "Stability Loss", "Dampers are flashing fault.", "Rebuild overlap and hold stability before commit."),
            ["sync_collapse"] = new("sync_collapse", "Sync Collapse", "Lock ring breakup detected.", "Reset channel stack and re-synchronize."),
        },
        ['q'] = new Dictionary<string, FailureLanguage>(StringComparer.OrdinalIgnoreCase)
        {
            ["phase_drift"] = new("phase_drift", "Phase Drift Detected", "Channel drift exceeds tolerance.", "Recenter drifting channels and rebuild coherence."),
            ["stability_loss"] = new("stability_loss", "Stability Loss", "Waveform noise exceeded lock threshold.", "Recalibrate channels and reduce signal noise."),
            ["sync_collapse"] = new("sync_collapse", "Sync Collapse", "Coherence window collapsed during commit.", "Reinitialize channel alignment and restabilize."),
        },
        ['w'] = new Dictionary<string, FailureLanguage>(StringComparer.OrdinalIgnoreCase)
        {
            ["containment_leak"] = new("containment_leak", "Containment Leak", "Energy bleed detected on the lattice.", "Seal open branches and reroute the trunk."),
            ["pressure_breach"] = new("pressure_breach", "Pressure Breach", "Overpressure pulse across conduits.", "Reduce unstable branches and stabilize pressure."),
            ["routing_fault"] = new("routing_fault", "Routing Fault", "Receiver path lost in diagnostics.", "Reconnect source to receiver before commit."),
        },
        ['x'] = new Dictionary<string, FailureLanguage>(StringComparer.OrdinalIgnoreCase)
        {
            ["archive_conflict"] = new("archive_conflict", "Archive Conflict", "Fragment mismatch clash detected.", "Re-evaluate pair candidates and relock known fragments."),
            ["sequence_corruption"] = new("sequence_corruption", "Sequence Corruption", "Temporal distortion ripple detected.", "Reconstruct the sequence using stable anchors."),
            ["reconstruction_fault"] = new("reconstruction_fault", "Reconstruction Fault", "Rollback marker triggered.", "Reset reconstruction pass and rebuild from stable memory."),
        },
    };

    private readonly string _seed;
    private readonly string _runNonce;
    private readonly GridPoint _room;
    private readonly MazeDifficulty _difficulty;
    private readonly int _baseLayoutSeed;
    private readonly int _baseSolutionSeed;
    private readonly Random _rng;

    private readonly Dictionary<string, int> _current = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _initial = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _target = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _max = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _cellActionActive = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _pipeMask = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _pipeFlowActive = new(StringComparer.Ordinal);
    private readonly List<int> _memoryValues = [];
    private readonly List<bool> _memoryMatched = [];
    private readonly List<int> _memoryOpen = [];
    private readonly List<string> _orderedKeys = [];

    private PuzzleStatus _status = PuzzleStatus.NotStarted;
    private PuzzlePhase _phase = PuzzlePhase.Observe;
    private SoloPanelMechanic _mechanic = SoloPanelMechanic.DialCommit;
    private int _stagesRequired = 1;
    private int _stageIndex;
    private int _attemptsRemaining = 3;
    private int _hintsRemaining;
    private int _mistakes;
    private double _timerSeconds = 120d;
    private double _timerRemaining = 120d;
    private double _cooldownUntil;
    private bool _showTarget = true;
    private bool _hardOscillation;
    private bool _hardDrift;
    private string _statusText;
    private string _hintText = string.Empty;
    private string _failureCode = string.Empty;
    private string _failureLabel = string.Empty;
    private string _recoveryText = string.Empty;
    private string _progressLabel = "System Progress";
    private string _progressTrend = "steady";
    private double _lastProgressValue;
    private int _layoutSeed;
    private int _solutionSeed;
    private int _cellPageIndex;
    private int _pipeRows;
    private int _pipeCols;
    private string _pipeSourceKey = string.Empty;
    private string _pipeSinkKey = string.Empty;
    private int _stageLevel = 1;
    private string _stageVisualProfile = "intro";

    public SoloPanelBiblePuzzle(char key, string title, string familyId, string instruction, string seed, string runNonce, GridPoint room, MazeDifficulty difficulty)
        : base(key, title, instruction)
    {
        FamilyId = familyId;
        _seed = seed;
        _runNonce = runNonce;
        _room = room;
        _difficulty = difficulty;
        _statusText = instruction;
        _baseLayoutSeed = PuzzleFactory.StableHash($"medhard|layout|{seed}|{room.X}|{room.Y}|{key}|{difficulty}");
        _baseSolutionSeed = PuzzleFactory.StableHash($"medhard|solution|{seed}|{runNonce}|{room.X}|{room.Y}|{key}|{difficulty}");
        _rng = new Random(_baseSolutionSeed);
    }

    public string FamilyId { get; }
    public int TierLevel { get; private set; }
    public bool TierInitialized { get; private set; }
    public double RewardPickupMultiplier { get; private set; } = 1d;

    public void EnsureTierLevel(int solvedRoomsBeforeCurrent, int totalPuzzleRooms)
    {
        if (TierInitialized)
        {
            return;
        }

        var normalizedTotal = Math.Max(1, totalPuzzleRooms - 1);
        var progress = Math.Clamp(solvedRoomsBeforeCurrent / (double)normalizedTotal, 0d, 0.999999d);
        var band = Math.Clamp((int)Math.Floor(progress * 4d), 0, 3);
        _stageLevel = band + 1;
        _stageVisualProfile = _stageLevel switch
        {
            1 => "intro",
            2 => "expand",
            3 => "constraint",
            _ => "master",
        };
        TierLevel = _difficulty switch
        {
            MazeDifficulty.Easy => 1 + band,
            MazeDifficulty.Medium => 3 + band,
            MazeDifficulty.Hard => 7 + band,
            _ => 0,
        };

        _layoutSeed = PuzzleFactory.StableHash($"{_baseLayoutSeed}|tier|{TierLevel}");
        _solutionSeed = PuzzleFactory.StableHash($"{_baseSolutionSeed}|tier|{TierLevel}");
        ConfigureFamilyState();
        if (!EnsurePreflight())
        {
            ApplyDeterministicFallbackTemplate();
        }
        TierInitialized = true;
        _status = PuzzleStatus.Active;
        _phase = PuzzlePhase.Configure;
        _statusText = _mechanic == SoloPanelMechanic.ChromaticLock
            ? BuildChromaticLockStatusText()
            : $"Tier L{TierLevel} online. Configure the system and commit.";
        StatusText = _statusText;
    }

    public override void Update(PuzzleUpdateContext context)
    {
        if (!TierInitialized || IsCompleted)
        {
            return;
        }

        if (_status == PuzzleStatus.Cooldown)
        {
            if (context.NowSeconds >= _cooldownUntil)
            {
                _status = PuzzleStatus.Active;
                _phase = PuzzlePhase.Configure;
                _statusText = "Cooldown cleared. Continue configuration.";
                StatusText = _statusText;
            }
            else
            {
                return;
            }
        }

        _timerRemaining = Math.Max(0d, _timerRemaining - context.DeltaTimeSeconds);
        if (_timerRemaining <= 0d && !IsCompleted)
        {
            RestartPuzzleAfterTimeout();
            return;
        }

        if (_mechanic == SoloPanelMechanic.SignalDecay)
        {
            UpdateSignalDecay(context);
            return;
        }

        if (_mechanic == SoloPanelMechanic.ChromaticLock)
        {
            UpdateChromaticLock(context);
            return;
        }

        if (_mechanic == SoloPanelMechanic.MemoryPair)
        {
            UpdateMemoryPalace(context);
        }

        if (_hardOscillation)
        {
            ApplyOscillation(context.NowSeconds);
        }
        else if (_hardDrift && ((int)(context.NowSeconds * 10d) % 80 == 0))
        {
            ApplyDrift();
        }
    }

    public SoloPanelView BuildPanelView(double nowSeconds)
    {
        var hud = BuildHud();
        var actions = BuildActions(nowSeconds);
        var board = BuildBoardSnapshot();
        var progress = Math.Clamp((_stageIndex + GetMechanicProgress()) / Math.Max(1d, _stagesRequired), 0d, 1d);
        var axisValue = Math.Clamp(GetFamilyProgressAxisValue(), 0d, 1d);
        var delta = axisValue - _lastProgressValue;
        _progressTrend = delta > 0.01d ? "up" : delta < -0.01d ? "down" : "steady";
        _lastProgressValue = axisValue;

        return new SoloPanelView(
            FamilyId,
            TierLevel,
            _status,
            _phase,
            _statusText,
            progress,
            _timerRemaining,
            _attemptsRemaining,
            _failureCode,
            _failureLabel,
            _recoveryText,
            _progressLabel,
            axisValue,
            _progressTrend,
            _stageVisualProfile,
            hud,
            actions,
            board);
    }

    public bool ApplyAction(string command, double nowSeconds)
    {
        if (!TierInitialized || IsCompleted)
        {
            return false;
        }

        if (_status == PuzzleStatus.Cooldown && nowSeconds < _cooldownUntil)
        {
            return false;
        }

        var normalized = (command ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (_mechanic == SoloPanelMechanic.SignalDecay)
        {
            return HandleSignalDecayAction(normalized, nowSeconds);
        }

        if (_mechanic == SoloPanelMechanic.ChromaticLock)
        {
            return HandleChromaticLockAction(normalized, nowSeconds);
        }

        if (_mechanic == SoloPanelMechanic.CoupledSystem)
        {
            return HandleCoupledSystemAction(normalized, nowSeconds);
        }

        if (normalized == "hint")
        {
            if (_hintsRemaining <= 0)
            {
                _statusText = "No hints remaining.";
                StatusText = _statusText;
                return true;
            }

            _hintsRemaining--;
            _hintText = BuildHintText();
            _statusText = _hintText;
            StatusText = _statusText;
            return true;
        }

        if (_mechanic == SoloPanelMechanic.CellCommit)
        {
            if (normalized == "page:next")
            {
                var totalPages = Math.Max(1, (int)Math.Ceiling(_orderedKeys.Count / (double)GetCellPageSize()));
                _cellPageIndex = Math.Clamp(_cellPageIndex + 1, 0, totalPages - 1);
                _statusText = $"Cell page {_cellPageIndex + 1}/{totalPages}.";
                StatusText = _statusText;
                return true;
            }

            if (normalized == "page:prev")
            {
                var totalPages = Math.Max(1, (int)Math.Ceiling(_orderedKeys.Count / (double)GetCellPageSize()));
                _cellPageIndex = Math.Clamp(_cellPageIndex - 1, 0, totalPages - 1);
                _statusText = $"Cell page {_cellPageIndex + 1}/{totalPages}.";
                StatusText = _statusText;
                return true;
            }
        }

        if (normalized == "reset")
        {
            ResetStage();
            _statusText = "Stage reset.";
            StatusText = _statusText;
            return true;
        }

        var handled = _mechanic switch
        {
            SoloPanelMechanic.DialCommit => HandleDialAction(normalized),
            SoloPanelMechanic.CellCommit => HandleCellAction(normalized),
            SoloPanelMechanic.PipeRotate => HandlePipeAction(normalized),
            SoloPanelMechanic.MemoryPair => HandleMemoryAction(normalized),
            _ => false,
        };

        if (!handled)
        {
            return false;
        }

        if (normalized == "commit")
        {
            ResolveCommit();
        }

        return true;
    }

    public bool TryBuildPanelSolveTrace(out PuzzleSolveTrace trace)
    {
        var steps = new List<PuzzleSolveStep>();
        switch (_mechanic)
        {
            case SoloPanelMechanic.SignalDecay:
                return TryBuildSignalDecaySolveTrace(out trace);
            case SoloPanelMechanic.ChromaticLock:
                return TryBuildChromaticLockSolveTrace(out trace);
            case SoloPanelMechanic.CoupledSystem:
                return TryBuildCoupledSystemSolveTrace(out trace);
            case SoloPanelMechanic.DialCommit:
                foreach (var key in _orderedKeys)
                {
                    if (!_current.TryGetValue(key, out var current) || !_target.TryGetValue(key, out var target) || !_max.TryGetValue(key, out var max))
                    {
                        continue;
                    }

                    var diff = target - current;
                    if (Math.Abs(diff) > (max / 2))
                    {
                        diff = diff > 0 ? diff - max - 1 : diff + max + 1;
                    }

                    var command = diff >= 0 ? "up" : "down";
                    for (var count = 0; count < Math.Abs(diff); count++)
                    {
                        steps.Add(new PuzzleSolveStep($"dial:{key}:{command}"));
                    }
                }

                steps.Add(new PuzzleSolveStep("commit"));
                break;
            case SoloPanelMechanic.CellCommit:
                if (TryBuildAdvancedCellSolveTrace(out trace))
                {
                    return true;
                }

                foreach (var key in _orderedKeys)
                {
                    if (!_current.TryGetValue(key, out var current) || !_target.TryGetValue(key, out var target) || !_max.TryGetValue(key, out var max))
                    {
                        continue;
                    }

                    var span = max + 1;
                    if (span <= 0)
                    {
                        continue;
                    }

                    var delta = (target - current + span) % span;
                    for (var count = 0; count < delta; count++)
                    {
                        steps.Add(new PuzzleSolveStep($"cell:{key}"));
                    }
                }
                steps.Add(new PuzzleSolveStep("commit"));
                break;
            case SoloPanelMechanic.PipeRotate:
                foreach (var key in _orderedKeys)
                {
                    if (!_current.TryGetValue(key, out var current) || !_target.TryGetValue(key, out var target))
                    {
                        continue;
                    }

                    var delta = (target - current + 4) % 4;
                    for (var count = 0; count < delta; count++)
                    {
                        steps.Add(new PuzzleSolveStep($"pipe:{key}"));
                    }
                }
                steps.Add(new PuzzleSolveStep("commit"));
                break;
            case SoloPanelMechanic.MemoryPair:
                var grouped = _memoryValues
                    .Select((value, index) => (value, index))
                    .GroupBy(entry => entry.value)
                    .Select(group => group.Select(entry => entry.index).ToArray())
                    .Where(group => group.Length >= 2)
                    .ToArray();
                foreach (var pair in grouped)
                {
                    steps.Add(new PuzzleSolveStep($"card:{pair[0]}"));
                    steps.Add(new PuzzleSolveStep($"card:{pair[1]}"));
                }
                break;
        }

        trace = new PuzzleSolveTrace(steps, "Doc panel preflight trace.");
        return steps.Count > 0;
    }

    private void ConfigureFamilyState()
    {
        _current.Clear();
        _initial.Clear();
        _target.Clear();
        _max.Clear();
        _orderedKeys.Clear();
        _memoryValues.Clear();
        _memoryMatched.Clear();
        _memoryOpen.Clear();
        _cellActionActive.Clear();
        _pipeMask.Clear();
        _pipeFlowActive.Clear();
        _pipeRows = 0;
        _pipeCols = 0;
        _pipeSourceKey = string.Empty;
        _pipeSinkKey = string.Empty;
        _signalComponents.Clear();
        _signalComponentLookup.Clear();
        _signalTargetSamples = [];
        _signalCurrentSamples = [];
        _signalTargetPath = string.Empty;
        _signalCurrentPath = string.Empty;
        _signalFftBins = string.Empty;
        _signalLevelDescription = string.Empty;
        _signalThreshold = 0d;
        _signalHoldRequiredSeconds = 0d;
        _signalHoldProgressSeconds = 0d;
        _signalPreviewSeconds = 0d;
        _signalPreviewRemainingSeconds = 0d;
        _signalPreviewVisible = true;
        _signalShowFft = false;
        _signalShowWaveLabels = true;
        _signalPhaseStepDegrees = 15;
        _signalNoiseStrength = 0d;
        _signalCrossChannelBleed = false;
        _chromaticRounds.Clear();
        _chromaticBaseTarget = default;
        _chromaticEffectiveTarget = default;
        _chromaticCurrentHue = 180;
        _chromaticCurrentSaturation = 50;
        _chromaticCurrentLightness = 50;
        _chromaticInitialHue = 180;
        _chromaticInitialSaturation = 50;
        _chromaticInitialLightness = 50;
        _chromaticHoldTicksProgress = 0d;
        _chromaticRoundElapsedSeconds = 0d;
        _chromaticDriftElapsedSeconds = 0d;
        _chromaticLockoutRemainingSeconds = 0d;
        _chromaticReadyToCommit = false;
        _chromaticTargetFrozen = false;
        _chromaticDriftStepIndex = 0;
        _stageIndex = 0;
        _mistakes = 0;
        _cellPageIndex = 0;
        _hintText = string.Empty;
        _showTarget = true;
        _hardOscillation = false;
        _hardDrift = false;
        _failureCode = string.Empty;
        _failureLabel = string.Empty;
        _recoveryText = string.Empty;
        _progressTrend = "steady";
        _lastProgressValue = 0d;
        _progressLabel = "System Progress";
        _hintsRemaining = _stageLevel == 1 ? 0 : _difficulty == MazeDifficulty.Medium ? 1 : 0;
        ResetDifficultyRebuildState();

        switch (char.ToLowerInvariant(PuzzleKey))
        {
            case 'p':
                _progressLabel = "Coherence / Synchronization";
                ConfigureChromaticLockFamily();
                break;
            case 'q':
                ConfigureSignalDecayFamily();
                break;
            case 'r':
                ConfigureDeadReckoningFamily();
                break;
            case 's':
                ConfigurePressureGridFamily();
                break;
            case 't':
                ConfigureCipherWheelFamily();
                break;
            case 'u':
                ConfigureGravityWellFamily();
                break;
            case 'v':
                ConfigureEchoChamberFamily();
                break;
            case 'w':
                ConfigureTokenFloodFamily();
                break;
            case 'x':
                ConfigureMemoryPalaceFamily();
                break;
            case 'y':
                ConfigureFaultLineFamily();
                break;
            case 'z':
                ConfigureTemporalGridFamily();
                break;
        }

        AssignDeterministicHardMutator();
        _timerRemaining = _timerSeconds;
        _status = PuzzleStatus.Active;
        _phase = PuzzlePhase.Configure;
        RewardPickupMultiplier = 1d;
        StatusText = _statusText;
    }

    private bool EnsurePreflight()
    {
        const int maxRetries = 4;
        for (var retry = 0; retry <= maxRetries; retry++)
        {
            if (TryBuildPanelSolveTrace(out var trace) && trace.Steps.Count > 0)
            {
                return true;
            }

            if (retry == maxRetries)
            {
                break;
            }

            _layoutSeed = PuzzleFactory.StableHash($"{_baseLayoutSeed}|tier|{TierLevel}|retry|{retry + 1}");
            _solutionSeed = PuzzleFactory.StableHash($"{_baseSolutionSeed}|tier|{TierLevel}|retry|{retry + 1}");
            ConfigureFamilyState();
        }

        return false;
    }

    private void ApplyDeterministicFallbackTemplate()
    {
        if (char.ToLowerInvariant(PuzzleKey) == 'p')
        {
            ApplyChromaticLockFallbackTemplate();
            return;
        }

        if (char.ToLowerInvariant(PuzzleKey) == 'q')
        {
            ApplySignalDecayFallbackTemplate();
            return;
        }

        _mechanic = SoloPanelMechanic.DialCommit;
        _current.Clear();
        _initial.Clear();
        _target.Clear();
        _max.Clear();
        _orderedKeys.Clear();
        _cellActionActive.Clear();
        _pipeMask.Clear();
        _pipeFlowActive.Clear();
        _memoryValues.Clear();
        _memoryMatched.Clear();
        _memoryOpen.Clear();

        var seed = PuzzleFactory.StableHash($"{_layoutSeed}|fallback");
        var max = 9;
        for (var index = 0; index < 3; index++)
        {
            var key = $"k{index + 1}";
            _orderedKeys.Add(key);
            _max[key] = max;
            _current[key] = 0;
            _initial[key] = 0;
            var target = Math.Abs(PuzzleFactory.StableHash($"{seed}|{key}") % (max + 1));
            _target[key] = target == 0 ? index + 1 : target;
        }

        _stagesRequired = 1;
        _stageIndex = 0;
        _attemptsRemaining = 4;
        _timerSeconds = 120d;
        _timerRemaining = _timerSeconds;
        _statusText = "Fallback template engaged. Stabilize channels and commit.";
        StatusText = _statusText;
    }

    private void ConfigureDialFamily(int keyCount, int maxValue, int stages, double timerSeconds, int attempts)
    {
        _mechanic = SoloPanelMechanic.DialCommit;
        _stagesRequired = 1;
        _timerSeconds = timerSeconds;
        _attemptsRemaining = attempts;
        for (var index = 0; index < keyCount; index++)
        {
            var key = $"k{index + 1}";
            _orderedKeys.Add(key);
            _max[key] = maxValue;
            var currentValue = Math.Abs(PuzzleFactory.StableHash($"{_solutionSeed}|current|{key}") % (maxValue + 1));
            _current[key] = currentValue;
            _initial[key] = currentValue;
            _target[key] = Math.Abs(PuzzleFactory.StableHash($"{_layoutSeed}|target|{key}") % (maxValue + 1));
        }
    }

    private void ConfigureCellFamily(int keyCount, int maxValue, int stages, double timerSeconds, int attempts)
    {
        _mechanic = SoloPanelMechanic.CellCommit;
        _stagesRequired = 1;
        _timerSeconds = timerSeconds;
        _attemptsRemaining = attempts;
        for (var index = 0; index < keyCount; index++)
        {
            var key = $"c{index + 1}";
            _orderedKeys.Add(key);
            _max[key] = maxValue;
            var currentNoise = Math.Abs(PuzzleFactory.StableHash($"{_solutionSeed}|cell-current|{key}") % 100);
            var currentValue = maxValue <= 1
                ? (currentNoise < 18 ? 1 : 0)
                : Math.Min(1, Math.Abs(PuzzleFactory.StableHash($"{_solutionSeed}|cell-current|{key}") % (maxValue + 1)));
            _current[key] = currentValue;
            _initial[key] = currentValue;

            if (maxValue <= 1)
            {
                var targetNoise = Math.Abs(PuzzleFactory.StableHash($"{_layoutSeed}|cell|{key}") % 100);
                _target[key] = targetNoise < 42 ? 1 : 0;
            }
            else
            {
                _target[key] = Math.Abs(PuzzleFactory.StableHash($"{_layoutSeed}|cell|{key}") % (maxValue + 1));
            }
            _cellActionActive[key] = _current[key] > 0;
        }
    }

    private void ConfigurePipeFamily(int rows, int cols, double timerSeconds, int attempts)
    {
        _mechanic = SoloPanelMechanic.PipeRotate;
        _stagesRequired = 1;
        _timerSeconds = timerSeconds;
        _attemptsRemaining = attempts;
        _pipeRows = Math.Max(3, rows);
        _pipeCols = Math.Max(3, cols);
        _pipeSourceKey = BuildPipeKey(0, 0);
        _pipeSinkKey = BuildPipeKey(_pipeRows - 1, _pipeCols - 1);

        const int north = 1;
        const int east = 2;
        const int south = 4;
        const int west = 8;

        var masks = new int[_pipeRows, _pipeCols];
        var path = BuildPipePath(_pipeRows, _pipeCols);
        for (var index = 0; index < path.Count - 1; index++)
        {
            var from = path[index];
            var to = path[index + 1];
            if (to.row < from.row)
            {
                masks[from.row, from.col] |= north;
                masks[to.row, to.col] |= south;
            }
            else if (to.row > from.row)
            {
                masks[from.row, from.col] |= south;
                masks[to.row, to.col] |= north;
            }
            else if (to.col > from.col)
            {
                masks[from.row, from.col] |= east;
                masks[to.row, to.col] |= west;
            }
            else
            {
                masks[from.row, from.col] |= west;
                masks[to.row, to.col] |= east;
            }
        }

        for (var row = 0; row < _pipeRows; row++)
        {
            for (var col = 0; col < _pipeCols; col++)
            {
                if (masks[row, col] != 0)
                {
                    continue;
                }

                var noise = Math.Abs(PuzzleFactory.StableHash($"{_layoutSeed}|pipe-noise|{row}|{col}") % 100);
                masks[row, col] = noise switch
                {
                    < 25 => 0,
                    < 55 => east | west,
                    < 80 => north | east,
                    _ => north | east | south,
                };
            }
        }

        for (var row = 0; row < _pipeRows; row++)
        {
            for (var col = 0; col < _pipeCols; col++)
            {
                var key = BuildPipeKey(row, col);
                _orderedKeys.Add(key);
                _pipeMask[key] = masks[row, col];
                _max[key] = 3;
                _target[key] = 0;
                var currentRotation = Math.Abs(PuzzleFactory.StableHash($"{_solutionSeed}|pipe-rot|{row}|{col}") % 4);
                _current[key] = currentRotation;
                _initial[key] = currentRotation;
            }
        }

        UpdatePipeFlowState();
    }

    private List<(int row, int col)> BuildPipePath(int rows, int cols)
    {
        var row = 0;
        var col = 0;
        var path = new List<(int row, int col)> { (row, col) };
        var step = 0;

        while (row < rows - 1 || col < cols - 1)
        {
            var canMoveRight = col < cols - 1;
            var canMoveDown = row < rows - 1;
            if (!canMoveRight && !canMoveDown)
            {
                break;
            }

            bool moveRight;
            if (canMoveRight && canMoveDown)
            {
                var bias = Math.Abs(PuzzleFactory.StableHash($"{_layoutSeed}|pipe-step|{row}|{col}|{step}") % 100);
                moveRight = bias >= 45;
            }
            else
            {
                moveRight = canMoveRight;
            }

            if (moveRight)
            {
                col++;
            }
            else
            {
                row++;
            }

            path.Add((row, col));
            step++;
        }

        return path;
    }

    private void ConfigureMemoryFamily(int cardCount, double timerSeconds, int attempts)
    {
        _mechanic = SoloPanelMechanic.MemoryPair;
        _stagesRequired = 1;
        _timerSeconds = timerSeconds;
        _attemptsRemaining = attempts;
        var pairCount = cardCount / 2;
        for (var pair = 0; pair < pairCount; pair++)
        {
            _memoryValues.Add(pair);
            _memoryValues.Add(pair);
        }

        var shuffled = ShuffleBySeed(_memoryValues.ToList(), _solutionSeed ^ 0x4f17);

        _memoryValues.Clear();
        _memoryValues.AddRange(shuffled);
        _memoryMatched.AddRange(Enumerable.Repeat(false, _memoryValues.Count));
    }

    private IReadOnlyList<SoloPanelHudItem> BuildHud()
    {
        if (_mechanic == SoloPanelMechanic.ChromaticLock)
        {
            return WithMutatorHud(BuildChromaticLockHud());
        }

        if (_mechanic == SoloPanelMechanic.SignalDecay)
        {
            return WithMutatorHud(BuildSignalDecayHud());
        }

        var progressValue = Math.Clamp(GetFamilyProgressAxisValue(), 0d, 1d);
        var progressPercent = $"{Math.Round(progressValue * 100d):0}%";
        return WithMutatorHud(
        [
            new SoloPanelHudItem(_progressLabel, progressPercent, _progressTrend == "up" ? "success" : _progressTrend == "down" ? "danger" : "shared"),
            new SoloPanelHudItem("Stage", $"{_stageLevel}/4", "info"),
            new SoloPanelHudItem("Attempts", _attemptsRemaining.ToString(CultureInfo.InvariantCulture), "warning"),
            new SoloPanelHudItem("Timer", $"{Math.Max(0, Math.Ceiling(_timerRemaining)):0}s", _timerRemaining <= 12d ? "danger" : "shared"),
            new SoloPanelHudItem("Hints", _hintsRemaining.ToString(CultureInfo.InvariantCulture), "info"),
        ]);
    }

    private IReadOnlyList<SoloPanelActionItem> BuildActions(double nowSeconds)
    {
        var enabled = _status != PuzzleStatus.Cooldown || nowSeconds >= _cooldownUntil;
        var actions = new List<SoloPanelActionItem>(64);
        if (_mechanic == SoloPanelMechanic.ChromaticLock)
        {
            return BuildChromaticLockActions(enabled);
        }

        if (_mechanic == SoloPanelMechanic.SignalDecay)
        {
            return BuildSignalDecayActions(enabled);
        }

        if (_mechanic == SoloPanelMechanic.CoupledSystem)
        {
            return BuildCoupledSystemActions(enabled);
        }

        if (_mechanic == SoloPanelMechanic.DialCommit)
        {
            foreach (var key in _orderedKeys)
            {
                actions.Add(new SoloPanelActionItem($"dial:{key}:down", $"{key} -", "dial", enabled));
                actions.Add(new SoloPanelActionItem($"dial:{key}:up", $"{key} +", "dial", enabled));
            }
        }
        else if (_mechanic == SoloPanelMechanic.CellCommit)
        {
            var pageSize = GetCellPageSize();
            var totalPages = Math.Max(1, (int)Math.Ceiling(_orderedKeys.Count / (double)pageSize));
            _cellPageIndex = Math.Clamp(_cellPageIndex, 0, totalPages - 1);
            var pageStart = _cellPageIndex * pageSize;

            foreach (var key in _orderedKeys.Skip(pageStart).Take(pageSize))
            {
                var active = _cellActionActive.TryGetValue(key, out var isActive) && isActive;
                actions.Add(new SoloPanelActionItem($"cell:{key}", key, "cell", enabled, active));
            }

            if (totalPages > 1)
            {
                actions.Add(new SoloPanelActionItem("page:prev", "Prev Page", "nav", enabled && _cellPageIndex > 0));
                actions.Add(new SoloPanelActionItem("page:next", "Next Page", "nav", enabled && _cellPageIndex < totalPages - 1));
            }
        }
        else if (_mechanic == SoloPanelMechanic.PipeRotate)
        {
            foreach (var key in _orderedKeys)
            {
                var active = _pipeFlowActive.TryGetValue(key, out var flowing) && flowing;
                actions.Add(new SoloPanelActionItem($"pipe:{key}", key, "pipe", enabled, active));
            }
        }
        else
        {
            for (var index = 0; index < _memoryValues.Count; index++)
            {
                if (_memoryMatched[index])
                {
                    continue;
                }

                var active = _memoryOpen.Contains(index);
                actions.Add(new SoloPanelActionItem($"card:{index}", $"Card {index + 1}", "memory", enabled, active));
            }
        }

        actions.Add(new SoloPanelActionItem("commit", "Commit", "commit", enabled));
        actions.Add(new SoloPanelActionItem("reset", "Reset", "reset", enabled));
        actions.Add(new SoloPanelActionItem("hint", "Hint", "hint", enabled && _hintsRemaining > 0));
        return actions;
    }

    private IReadOnlyDictionary<string, string> BuildBoardSnapshot()
    {
        var board = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["family"] = FamilyId,
            ["tier"] = TierLevel.ToString(CultureInfo.InvariantCulture),
            ["status"] = _status.ToString(),
            ["hint"] = _hintText,
            ["failure_code"] = _failureCode,
            ["failure_label"] = _failureLabel,
            ["recovery_text"] = _recoveryText,
            ["progress_label"] = _progressLabel,
            ["progress_value"] = GetFamilyProgressAxisValue().ToString("0.000", CultureInfo.InvariantCulture),
            ["progress_trend"] = _progressTrend,
            ["stage_visual_profile"] = _stageVisualProfile,
            ["stage_level"] = _stageLevel.ToString(CultureInfo.InvariantCulture),
        };
        AppendMutatorBoardMetadata(board);

        if (_mechanic == SoloPanelMechanic.ChromaticLock)
        {
            return BuildChromaticLockBoardSnapshot(board);
        }

        if (_mechanic == SoloPanelMechanic.SignalDecay)
        {
            return BuildSignalDecayBoardSnapshot(board);
        }

        if (_mechanic == SoloPanelMechanic.CoupledSystem)
        {
            return BuildCoupledSystemBoardSnapshot(board);
        }

        if (_mechanic == SoloPanelMechanic.MemoryPair)
        {
            board["matched"] = _memoryMatched.Count(matched => matched).ToString(CultureInfo.InvariantCulture);
            board["total"] = _memoryMatched.Count.ToString(CultureInfo.InvariantCulture);
            AppendMemoryVisualMetadata(board);
            return board;
        }

        if (_mechanic == SoloPanelMechanic.PipeRotate)
        {
            var (sinkReached, leakFree) = UpdatePipeFlowState();
            board["pipe_rows"] = _pipeRows.ToString(CultureInfo.InvariantCulture);
            board["pipe_cols"] = _pipeCols.ToString(CultureInfo.InvariantCulture);
            board["pipe_source"] = _pipeSourceKey;
            board["pipe_sink"] = _pipeSinkKey;
            board["pipe_linked"] = sinkReached ? "1" : "0";
            board["pipe_leak_free"] = leakFree ? "1" : "0";
            board["aligned"] = _orderedKeys.Count(key => _current.TryGetValue(key, out var current) &&
                                                       _target.TryGetValue(key, out var target) &&
                                                       current == target).ToString(CultureInfo.InvariantCulture);
            board["cells_total"] = _orderedKeys.Count.ToString(CultureInfo.InvariantCulture);

            foreach (var key in _orderedKeys)
            {
                board[$"cur:{key}"] = _current.TryGetValue(key, out var currentValue)
                    ? currentValue.ToString(CultureInfo.InvariantCulture)
                    : "0";
                board[$"max:{key}"] = "3";
                board[$"mask:{key}"] = _pipeMask.TryGetValue(key, out var maskValue)
                    ? maskValue.ToString(CultureInfo.InvariantCulture)
                    : "0";
                board[$"flow:{key}"] = _pipeFlowActive.TryGetValue(key, out var isFlowing) && isFlowing ? "1" : "0";
            }

            AppendFlowVisualMetadata(board, sinkReached, leakFree);
            return board;
        }

        if (_mechanic == SoloPanelMechanic.CellCommit)
        {
            var pageSize = GetCellPageSize();
            var totalPages = Math.Max(1, (int)Math.Ceiling(_orderedKeys.Count / (double)pageSize));
            _cellPageIndex = Math.Clamp(_cellPageIndex, 0, totalPages - 1);
            var pageStart = _cellPageIndex * pageSize;

            board["page"] = (_cellPageIndex + 1).ToString(CultureInfo.InvariantCulture);
            board["page_total"] = totalPages.ToString(CultureInfo.InvariantCulture);
            board["cells_total"] = _orderedKeys.Count.ToString(CultureInfo.InvariantCulture);
            board["aligned"] = CountAlignedCells().ToString(CultureInfo.InvariantCulture);

            var visibleKeys = _orderedKeys.Skip(pageStart).Take(pageSize).ToArray();
            foreach (var key in visibleKeys)
            {
                board[$"cur:{key}"] = _current.TryGetValue(key, out var currentValue)
                    ? currentValue.ToString(CultureInfo.InvariantCulture)
                    : "--";
                board[$"max:{key}"] = _max.TryGetValue(key, out var maxValue)
                    ? maxValue.ToString(CultureInfo.InvariantCulture)
                    : "1";
                var targetVisible = _gridVisibleTargets.Count == 0
                    ? _showTarget
                    : _gridVisibleTargets.Contains(key);
                if (targetVisible)
                {
                    board[$"tgt:{key}"] = _target.TryGetValue(key, out var targetValue)
                        ? targetValue.ToString(CultureInfo.InvariantCulture)
                        : "--";
                }
            }

            AppendGridVisualMetadata(board, visibleKeys);
            return board;
        }

        foreach (var key in _orderedKeys)
        {
            board[$"cur:{key}"] = _current.TryGetValue(key, out var value) ? value.ToString(CultureInfo.InvariantCulture) : "--";
            board[$"max:{key}"] = _max.TryGetValue(key, out var maxValue)
                ? maxValue.ToString(CultureInfo.InvariantCulture)
                : "1";
            if (_showTarget)
            {
                board[$"tgt:{key}"] = _target.TryGetValue(key, out var targetValue)
                    ? targetValue.ToString(CultureInfo.InvariantCulture)
                    : "--";
            }
        }

        if (char.ToLowerInvariant(PuzzleKey) == 'q')
        {
            var tolerance = GetSignalDecayTolerance();
            board["signal_tolerance"] = tolerance.ToString(CultureInfo.InvariantCulture);
            board["signal_coherence"] = GetSignalDecayCoherence().ToString("0.000", CultureInfo.InvariantCulture);
            board["signal_ready"] = IsCurrentConfigurationSolved() ? "1" : "0";
            board["signal_wave_noise"] = (1d - GetSignalDecayCoherence()).ToString("0.000", CultureInfo.InvariantCulture);
            foreach (var key in _orderedKeys)
            {
                var distance = GetSignalDecayDistance(key);
                board[$"dist:{key}"] = distance.ToString(CultureInfo.InvariantCulture);
                board[$"tol:{key}"] = tolerance.ToString(CultureInfo.InvariantCulture);
                board[$"aligned:{key}"] = distance <= tolerance ? "1" : "0";
            }
        }

        AppendDialVisualMetadata(board);
        return board;
    }

    private int CountAlignedCells() =>
        _orderedKeys.Count(key =>
            _current.TryGetValue(key, out var current) &&
            _target.TryGetValue(key, out var target) &&
            current == target);

    private bool HandleDialAction(string command)
    {
        if (command == "commit")
        {
            return true;
        }

        if (!command.StartsWith("dial:", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = command.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
        {
            return false;
        }

        var key = parts[1];
        var direction = parts[2];
        if (!_current.ContainsKey(key) || !_max.TryGetValue(key, out var max))
        {
            return false;
        }

        var delta = string.Equals(direction, "up", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
        var next = _current[key] + delta;
        if (next < 0)
        {
            next = max;
        }
        else if (next > max)
        {
            next = 0;
        }

        _current[key] = next;
        _phase = PuzzlePhase.Configure;
        if (char.ToLowerInvariant(PuzzleKey) == 'q')
        {
            var coherence = Math.Round(GetSignalDecayCoherence() * 100d);
            _statusText = IsCurrentConfigurationSolved()
                ? $"System Coherence: {coherence:0}% ready to stabilize."
                : $"System Coherence: {coherence:0}% (adjust channels into tolerance).";
        }
        else
        {
            _statusText = "Parameters updated.";
        }
        StatusText = _statusText;
        return true;
    }

    private bool HandleCellAction(string command)
    {
        if (command == "commit")
        {
            return true;
        }

        if (!command.StartsWith("cell:", StringComparison.Ordinal))
        {
            return false;
        }

        var key = command[5..];
        if (!_current.TryGetValue(key, out var current) || !_max.TryGetValue(key, out var max))
        {
            return false;
        }

        if (TryHandleAdvancedCellAction(key))
        {
            _phase = PuzzlePhase.Configure;
            var advancedAligned = CountAlignedCells();
            _statusText = BuildGridStatusText(advancedAligned);
            StatusText = _statusText;
            return true;
        }

        _current[key] = max == 0 ? 0 : (current + 1) % (max + 1);
        _cellActionActive[key] = _current[key] > 0;
        _phase = PuzzlePhase.Configure;
        var aligned = CountAlignedCells();
        _statusText = $"Cells aligned {aligned}/{_orderedKeys.Count}.";
        StatusText = _statusText;
        return true;
    }

    private bool HandlePipeAction(string command)
    {
        if (command == "commit")
        {
            return true;
        }

        if (!command.StartsWith("pipe:", StringComparison.Ordinal))
        {
            return false;
        }

        var key = command[5..];
        if (!_current.TryGetValue(key, out var current))
        {
            return false;
        }

        _current[key] = (current + 1) % 4;
        _phase = PuzzlePhase.Configure;
        var (sinkReached, leakFree) = UpdatePipeFlowState();
        _statusText = sinkReached && leakFree
            ? "Flow linked to receiver. Commit to lock."
            : BuildPipeProgressStatus();
        StatusText = _statusText;
        return true;
    }

    private bool HandleMemoryAction(string command)
    {
        if (!command.StartsWith("card:", StringComparison.Ordinal))
        {
            if (command == "commit")
            {
                return true;
            }

            return false;
        }

        if (!int.TryParse(command[5..], out var index) || index < 0 || index >= _memoryValues.Count || _memoryMatched[index])
        {
            return false;
        }

        if (_memoryOpen.Contains(index))
        {
            return true;
        }

        _memoryOpen.Add(index);
        if (_memoryOpen.Count < 2)
        {
            _statusText = "Select another card.";
            StatusText = _statusText;
            return true;
        }

        var first = _memoryOpen[0];
        var second = _memoryOpen[1];
        if (_memoryValues[first] == _memoryValues[second])
        {
            _memoryMatched[first] = true;
            _memoryMatched[second] = true;
            _memoryOpen.Clear();
            _statusText = "Match locked.";
            StatusText = _statusText;
            if (_memoryMatched.All(matched => matched))
            {
                ResolveCommit();
            }

            return true;
        }

        _memoryOpen.Clear();
        _mistakes++;
        _statusText = "Mismatch.";
        StatusText = _statusText;
        RegisterFailure(_difficulty == MazeDifficulty.Hard ? "sequence_corruption" : "archive_conflict", "Memory mismatch detected.");
        if (_difficulty == MazeDifficulty.Hard)
        {
            var reshuffleSeed = PuzzleFactory.StableHash($"{_solutionSeed}|reshuffle|{_mistakes}");
            var reshuffled = ShuffleBySeed(_memoryValues.ToList(), reshuffleSeed);
            _memoryValues.Clear();
            _memoryValues.AddRange(reshuffled);
        }

        return true;
    }

    private void ResolveCommit()
    {
        if (_mechanic == SoloPanelMechanic.ChromaticLock)
        {
            ResolveChromaticLockCommit();
            return;
        }

        if (_mechanic == SoloPanelMechanic.SignalDecay)
        {
            ResolveSignalDecayCommit();
            return;
        }

        if (_mechanic == SoloPanelMechanic.CoupledSystem && !IsCurrentConfigurationSolved())
        {
            RegisterFailure(GetDefaultFailureCode(), BuildCoupledSystemCommitFailureText());
            return;
        }

        if (_mechanic != SoloPanelMechanic.MemoryPair && !IsCurrentConfigurationSolved())
        {
            if (_mechanic == SoloPanelMechanic.CellCommit)
            {
                _statusText = $"Not stable yet: {CountAlignedCells()}/{_orderedKeys.Count} aligned.";
                StatusText = _statusText;
                return;
            }

            if (_mechanic == SoloPanelMechanic.PipeRotate)
            {
                var (sinkReached, leakFree) = UpdatePipeFlowState();
                if (sinkReached && leakFree && !AreAllOrderedTargetsMatched())
                {
                    RegisterFailure("routing_fault", "Receiver path is stable, but conduit orientations are not fully locked.");
                }
                else if (sinkReached && !leakFree)
                {
                    RegisterFailure("containment_leak", "Containment leak detected.");
                }
                else if (!sinkReached)
                {
                    RegisterFailure("routing_fault", "Receiver route is incomplete.");
                }
                else
                {
                    RegisterFailure("pressure_breach", "Pressure profile failed validation.");
                }
                return;
            }

            if (char.ToLowerInvariant(PuzzleKey) == 'q')
            {
                var worstChannel = GetSignalDecayWorstChannel();
                var tolerance = GetSignalDecayTolerance();
                RegisterFailure("phase_drift", $"Phase Drift Detected. Channel {worstChannel.ToUpperInvariant()} is outside ±{tolerance} tolerance.");
                return;
            }

            RegisterFailure(GetDefaultFailureCode(), "Commit rejected.");
            return;
        }

        _stageIndex++;
        if (_stageIndex < _stagesRequired)
        {
            AdvanceStageTargets();
            _phase = PuzzlePhase.Resolve;
            _statusText = $"Stage {_stageIndex}/{_stagesRequired} locked.";
            StatusText = _statusText;
            return;
        }

        var stars = ComputeStars();
        RewardPickupMultiplier = stars switch
        {
            >= 3 => 1.45d,
            2 => 1.25d,
            _ => 1.1d,
        };
        _status = PuzzleStatus.Solved;
        _phase = PuzzlePhase.Resolve;
        Complete($"{Title} solved. {stars}* rating.");
    }

    private bool IsCurrentConfigurationSolved()
    {
        if (_mechanic == SoloPanelMechanic.ChromaticLock)
        {
            return IsChromaticLockReadyToCommit();
        }

        if (_mechanic == SoloPanelMechanic.SignalDecay)
        {
            return IsSignalDecayReadyToCommit();
        }

        if (_mechanic == SoloPanelMechanic.CoupledSystem)
        {
            return IsCoupledSystemSolved();
        }

        if (_mechanic == SoloPanelMechanic.PipeRotate)
        {
            var (sinkReached, leakFree) = UpdatePipeFlowState();
            return sinkReached && leakFree && AreAllOrderedTargetsMatched();
        }

        foreach (var key in _orderedKeys)
        {
            if (!_current.TryGetValue(key, out var current) || !_target.TryGetValue(key, out var target))
            {
                return false;
            }

            if (char.ToLowerInvariant(PuzzleKey) == 'q')
            {
                if (GetSignalDecayDistance(key) > GetSignalDecayTolerance())
                {
                    return false;
                }
            }
            else if (current != target)
            {
                return false;
            }
        }

        return true;
    }

    private bool AreAllOrderedTargetsMatched() =>
        _orderedKeys.Count > 0 &&
        _orderedKeys.All(key =>
            _current.TryGetValue(key, out var current) &&
            _target.TryGetValue(key, out var target) &&
            current == target);

    private void AdvanceStageTargets()
    {
        foreach (var key in _orderedKeys)
        {
            if (!_max.TryGetValue(key, out var max))
            {
                continue;
            }

            var nextTarget = Math.Abs(PuzzleFactory.StableHash($"{_layoutSeed}|stage|{_stageIndex}|{key}") % (max + 1));
            _target[key] = nextTarget;
            if (_mechanic == SoloPanelMechanic.CellCommit)
            {
                _current[key] = 0;
                _cellActionActive[key] = false;
            }
        }
    }

    private void RegisterFailure(string message) => RegisterFailure(GetDefaultFailureCode(), message);

    private void RegisterFailure(string failureCode, string fallbackMessage)
    {
        _mistakes++;
        _attemptsRemaining = Math.Max(0, _attemptsRemaining - 1);
        ApplyFailureState(failureCode, fallbackMessage);
        var useFallbackStatus = char.ToLowerInvariant(PuzzleKey) == 'q' && !string.IsNullOrWhiteSpace(fallbackMessage);
        _statusText = useFallbackStatus
            ? fallbackMessage
            : string.IsNullOrWhiteSpace(_failureLabel)
                ? fallbackMessage
                : $"{_failureLabel}: {GetFailureVisualCue(failureCode)}";
        StatusText = _statusText;
        _status = PuzzleStatus.Cooldown;
        _phase = PuzzlePhase.Resolve;
        _cooldownUntil = _timerSeconds - _timerRemaining + (_difficulty == MazeDifficulty.Hard ? 1.0d : 0.5d);
        if (_attemptsRemaining == 0)
        {
            _attemptsRemaining = _difficulty == MazeDifficulty.Hard ? 2 : 3;
            _stageIndex = Math.Max(0, _stageIndex - 1);
            ResetStage();
        }
    }

    private void RestartPuzzleAfterTimeout()
    {
        if (_mechanic == SoloPanelMechanic.ChromaticLock)
        {
            RestartChromaticLockAfterTimeout();
            return;
        }

        if (_mechanic == SoloPanelMechanic.SignalDecay)
        {
            RestartSignalDecayAfterTimeout();
            return;
        }

        ConfigureFamilyState();
        ApplyFailureState(GetTimeoutFailureCode(), "Time window collapsed.");
        _status = PuzzleStatus.Active;
        _phase = PuzzlePhase.Configure;
        _statusText = string.IsNullOrWhiteSpace(_failureLabel)
            ? "Time expired. Puzzle restarted."
            : $"{_failureLabel}: {_recoveryText}";
        StatusText = _statusText;
    }

    private void ResetStage()
    {
        if (_mechanic == SoloPanelMechanic.ChromaticLock)
        {
            ResetChromaticLockStage(resetTimer: false, clearFailure: true, clearLockout: true);
            return;
        }

        if (_mechanic == SoloPanelMechanic.SignalDecay)
        {
            ResetSignalDecayStage(resetTimer: false, clearFailure: true, restorePreview: false);
            return;
        }

        if (_mechanic == SoloPanelMechanic.CoupledSystem)
        {
            ResetCoupledSystemStage();
            return;
        }

        if (_mechanic == SoloPanelMechanic.MemoryPair)
        {
            ResetMemoryPalaceStage();
            return;
        }

        foreach (var key in _orderedKeys)
        {
            _current[key] = _initial.TryGetValue(key, out var initialValue) ? initialValue : 0;
            _cellActionActive[key] = _current[key] > 0;
        }

        if (_mechanic == SoloPanelMechanic.PipeRotate)
        {
            UpdatePipeFlowState();
        }
    }

    private void ApplyOscillation(double nowSeconds)
    {
        foreach (var key in _orderedKeys.Take(6))
        {
            if (!_max.TryGetValue(key, out var max))
            {
                continue;
            }

            var baseTarget = Math.Abs(PuzzleFactory.StableHash($"{_layoutSeed}|osc|{key}") % (max + 1));
            var wave = Math.Sin((nowSeconds * 1.6d) + key.Length) * (max > 50 ? 8d : 2d);
            var value = (int)Math.Round(baseTarget + wave);
            if (value < 0)
            {
                value += max + 1;
            }
            _target[key] = value % (max + 1);
        }
    }

    private void ApplyDrift()
    {
        foreach (var key in _orderedKeys.Take(6))
        {
            if (!_max.TryGetValue(key, out var max))
            {
                continue;
            }

            var step = _rng.Next(0, 2) == 0 ? -1 : 1;
            var next = _target[key] + step;
            if (next < 0)
            {
                next = max;
            }
            else if (next > max)
            {
                next = 0;
            }

            _target[key] = next;
        }
    }

    private string BuildPipeProgressStatus()
    {
        var (sinkReached, leakFree) = UpdatePipeFlowState();
        var activeCount = _pipeFlowActive.Count(entry => entry.Value);
        if (sinkReached && !leakFree)
        {
            return $"Receiver linked, but leaks remain ({activeCount} flowing nodes).";
        }

        return $"Flow routed through {activeCount}/{Math.Max(1, _orderedKeys.Count)} nodes.";
    }

    private (bool sinkReached, bool leakFree) UpdatePipeFlowState()
    {
        _pipeFlowActive.Clear();
        _pipePressure.Clear();
        if (_mechanic != SoloPanelMechanic.PipeRotate || _pipeRows <= 0 || _pipeCols <= 0 || string.IsNullOrWhiteSpace(_pipeSourceKey))
        {
            return (false, false);
        }

        var queue = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        queue.Enqueue(_pipeSourceKey);
        visited.Add(_pipeSourceKey);
        _pipePressure[_pipeSourceKey] = 100d;
        var leakFree = true;

        while (queue.Count > 0)
        {
            var key = queue.Dequeue();
            var pressure = _pipePressure.TryGetValue(key, out var currentPressure) ? currentPressure : 0d;
            if (pressure <= 0d)
            {
                continue;
            }

            _pipeFlowActive[key] = true;
            if (!TryParsePipeKey(key, out var row, out var col))
            {
                continue;
            }

            var mask = GetPipeMaskForRuntime(key);
            var activeDirections = EnumeratePipeDirections()
                .Where(direction => (mask & direction.bit) != 0)
                .ToArray();
            var branchCount = Math.Max(1, activeDirections.Length);
            foreach (var (bit, deltaRow, deltaCol, oppositeBit) in activeDirections)
            {
                var neighborRow = row + deltaRow;
                var neighborCol = col + deltaCol;
                if (neighborRow < 0 || neighborRow >= _pipeRows || neighborCol < 0 || neighborCol >= _pipeCols)
                {
                    leakFree = false;
                    continue;
                }

                var neighborKey = BuildPipeKey(neighborRow, neighborCol);
                var neighborMask = GetPipeMaskForRuntime(neighborKey);
                if ((neighborMask & oppositeBit) == 0)
                {
                    leakFree = false;
                    continue;
                }

                var nextPressure = Math.Max(0d, pressure - (4d + ((branchCount - 1) * 2d)));
                if (_pipeGateThreshold.TryGetValue(neighborKey, out var gateThreshold) && gateThreshold > 0d && nextPressure < gateThreshold)
                {
                    continue;
                }

                if (nextPressure <= 0d)
                {
                    continue;
                }

                var hadPressure = _pipePressure.TryGetValue(neighborKey, out var existingPressure);
                if (!hadPressure || nextPressure > existingPressure + 0.01d)
                {
                    _pipePressure[neighborKey] = nextPressure;
                }

                if (visited.Add(neighborKey))
                {
                    queue.Enqueue(neighborKey);
                }
                else if (!hadPressure || nextPressure > existingPressure + 0.01d)
                {
                    queue.Enqueue(neighborKey);
                }
            }
        }

        var sinkReached = _pipePressure.TryGetValue(_pipeSinkKey, out var sinkPressure) && sinkPressure >= 8d;
        return (sinkReached, leakFree);
    }

    private int GetPipeMaskForRuntime(string key)
    {
        if (!_pipeMask.TryGetValue(key, out var baseMask))
        {
            return 0;
        }

        var turns = _current.TryGetValue(key, out var rotation) ? rotation : 0;
        return RotatePipeMask(baseMask, turns);
    }

    private static IEnumerable<(int bit, int deltaRow, int deltaCol, int oppositeBit)> EnumeratePipeDirections()
    {
        const int north = 1;
        const int east = 2;
        const int south = 4;
        const int west = 8;
        yield return (north, -1, 0, south);
        yield return (east, 0, 1, west);
        yield return (south, 1, 0, north);
        yield return (west, 0, -1, east);
    }

    private static int RotatePipeMask(int mask, int turnsClockwise)
    {
        var turns = ((turnsClockwise % 4) + 4) % 4;
        var rotated = mask;
        for (var turn = 0; turn < turns; turn++)
        {
            var next = 0;
            if ((rotated & 1) != 0) next |= 2;
            if ((rotated & 2) != 0) next |= 4;
            if ((rotated & 4) != 0) next |= 8;
            if ((rotated & 8) != 0) next |= 1;
            rotated = next;
        }

        return rotated;
    }

    private static string BuildPipeKey(int row, int col) => $"r{row}c{col}";

    private static bool TryParsePipeKey(string key, out int row, out int col)
    {
        row = -1;
        col = -1;
        if (string.IsNullOrWhiteSpace(key) || key[0] != 'r')
        {
            return false;
        }

        var cIndex = key.IndexOf('c');
        if (cIndex <= 1 || cIndex >= key.Length - 1)
        {
            return false;
        }

        if (!int.TryParse(key.AsSpan(1, cIndex - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out row))
        {
            return false;
        }

        return int.TryParse(key.AsSpan(cIndex + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out col);
    }

    private string GetDefaultFailureCode() =>
        char.ToLowerInvariant(PuzzleKey) switch
        {
            'p' => "sync_collapse",
            'q' => "phase_drift",
            'w' => "routing_fault",
            'x' => "reconstruction_fault",
            _ => "generic",
        };

    private string GetTimeoutFailureCode() =>
        char.ToLowerInvariant(PuzzleKey) switch
        {
            'p' => "stability_loss",
            'q' => "stability_loss",
            'w' => "pressure_breach",
            'x' => "sequence_corruption",
            _ => "generic",
        };

    private void ApplyFailureState(string failureCode, string fallbackMessage)
    {
        var key = char.ToLowerInvariant(PuzzleKey);
        if (!FailureLanguages.TryGetValue(key, out var familyFailures) ||
            !familyFailures.TryGetValue(failureCode, out var mapped))
        {
            _failureCode = string.IsNullOrWhiteSpace(failureCode) ? "generic" : failureCode;
            _failureLabel = fallbackMessage;
            _recoveryText = "Reset and retry.";
            return;
        }

        _failureCode = mapped.Code;
        _failureLabel = mapped.Label;
        _recoveryText = mapped.Recovery;
    }

    private string GetFailureVisualCue(string failureCode)
    {
        var key = char.ToLowerInvariant(PuzzleKey);
        if (FailureLanguages.TryGetValue(key, out var familyFailures) &&
            familyFailures.TryGetValue(failureCode, out var mapped))
        {
            return mapped.VisualCue;
        }

        return "System fault detected.";
    }

    private double GetFamilyProgressAxisValue()
    {
        var key = char.ToLowerInvariant(PuzzleKey);
        if (_mechanic == SoloPanelMechanic.ChromaticLock)
        {
            return GetChromaticLockAxisValue();
        }

        if (_mechanic == SoloPanelMechanic.SignalDecay)
        {
            return GetSignalDecayWaveformCoherence();
        }

        if (_mechanic == SoloPanelMechanic.CoupledSystem)
        {
            return GetCoupledSystemAxisValue();
        }

        if (key == 'p')
        {
            if (_orderedKeys.Count == 0)
            {
                return 0d;
            }

            double coherence = 0d;
            foreach (var channel in _orderedKeys)
            {
                if (!_current.TryGetValue(channel, out var current) ||
                    !_target.TryGetValue(channel, out var target) ||
                    !_max.TryGetValue(channel, out var max))
                {
                    continue;
                }

                var span = Math.Max(1d, max);
                coherence += 1d - (Math.Min(Math.Abs(current - target), span) / span);
            }

            var sync = coherence / _orderedKeys.Count;
            return Math.Clamp(sync, 0d, 1d);
        }

        if (key == 'w' && _mechanic == SoloPanelMechanic.PipeRotate)
        {
            return GetTokenFloodMechanicProgress();
        }

        if (key == 'x')
        {
            if (_memoryMatched.Count == 0)
            {
                return 0d;
            }

            var matched = _memoryMatched.Count(m => m) / (double)_memoryMatched.Count;
            var corruptionPenalty = Math.Min(0.35d, _mistakes * 0.05d);
            return Math.Clamp(matched - corruptionPenalty, 0d, 1d);
        }

        return GetMechanicProgress();
    }

    private int GetSignalDecayTolerance()
    {
        var baseTolerance = _stageLevel switch
        {
            1 => 7,
            2 => 5,
            3 => 4,
            _ => 3,
        };

        return _difficulty switch
        {
            MazeDifficulty.Easy => baseTolerance + 1,
            MazeDifficulty.Hard => Math.Max(2, baseTolerance - 1),
            _ => baseTolerance,
        };
    }

    private int GetSignalDecayDistance(string key)
    {
        if (!_current.TryGetValue(key, out var current) ||
            !_target.TryGetValue(key, out var target) ||
            !_max.TryGetValue(key, out var max))
        {
            return int.MaxValue;
        }

        var span = Math.Max(1, max + 1);
        var raw = Math.Abs(current - target);
        return Math.Min(raw, span - raw);
    }

    private double GetSignalDecayCoherence()
    {
        if (_orderedKeys.Count == 0)
        {
            return 0d;
        }

        var tolerance = GetSignalDecayTolerance();
        double total = 0d;
        foreach (var key in _orderedKeys)
        {
            if (!_max.TryGetValue(key, out var max))
            {
                continue;
            }

            var distance = GetSignalDecayDistance(key);
            var span = Math.Max(1d, max + 1d);
            var normalized = 1d - Math.Min(distance, span / 2d) / (span / 2d);
            if (distance <= tolerance)
            {
                normalized = Math.Min(1d, normalized + 0.22d);
            }

            total += Math.Clamp(normalized, 0d, 1d);
        }

        return Math.Clamp(total / _orderedKeys.Count, 0d, 1d);
    }

    private string GetSignalDecayWorstChannel()
    {
        var channel = _orderedKeys
            .OrderByDescending(GetSignalDecayDistance)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(channel) ? "k1" : channel;
    }

    private string BuildHintText()
    {
        if (_mechanic == SoloPanelMechanic.ChromaticLock)
        {
            return BuildChromaticLockHintText();
        }

        if (_mechanic == SoloPanelMechanic.SignalDecay)
        {
            return BuildSignalDecayHintText();
        }

        if (_mechanic == SoloPanelMechanic.CoupledSystem)
        {
            return BuildCoupledSystemHintText();
        }

        if (_mechanic == SoloPanelMechanic.MemoryPair)
        {
            return BuildMemoryPalaceHintText();
        }

        if (_mechanic == SoloPanelMechanic.PipeRotate)
        {
            return BuildTokenFloodHintText();
        }

        if (_mechanic == SoloPanelMechanic.CellCommit)
        {
            return BuildGridHintText();
        }

        var next = _orderedKeys.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(next) || !_target.TryGetValue(next, out var nextTarget))
        {
            return "Hint unavailable.";
        }

        return $"Hint: {next} should trend toward {nextTarget}.";
    }

    private double GetMechanicProgress()
    {
        if (_mechanic == SoloPanelMechanic.ChromaticLock)
        {
            return GetChromaticLockMechanicProgress();
        }

        if (_mechanic == SoloPanelMechanic.SignalDecay)
        {
            return GetSignalDecayWaveformCoherence();
        }

        if (_mechanic == SoloPanelMechanic.CoupledSystem)
        {
            return GetCoupledSystemAxisValue();
        }

        if (_mechanic == SoloPanelMechanic.MemoryPair)
        {
            return GetMemoryPalaceMechanicProgress();
        }

        if (_mechanic == SoloPanelMechanic.PipeRotate)
        {
            return GetTokenFloodMechanicProgress();
        }

        if (_orderedKeys.Count == 0)
        {
            return 0d;
        }

        var matches = _orderedKeys.Count(key => _current.TryGetValue(key, out var current) &&
                                             _target.TryGetValue(key, out var target) &&
                                             current == target);
        return matches / (double)_orderedKeys.Count;
    }

    private int ComputeStars()
    {
        var timeRatio = _timerSeconds <= 0d ? 0d : _timerRemaining / _timerSeconds;
        if (_mistakes <= 1 && timeRatio >= 0.45d)
        {
            return 3;
        }

        if (_mistakes <= 3 && timeRatio >= 0.2d)
        {
            return 2;
        }

        return 1;
    }

    private int GetCellPageSize() => _gridPageSize > 0 ? _gridPageSize : _difficulty == MazeDifficulty.Hard ? 20 : 16;

    private static List<int> ShuffleBySeed(List<int> values, int seed)
    {
        var state = seed;
        for (var index = values.Count - 1; index > 0; index--)
        {
            state = NextShuffleState(state + index);
            var swapIndex = state % (index + 1);
            (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
        }

        return values;
    }

    private static int NextShuffleState(int seed)
    {
        unchecked
        {
            return (int)(((uint)seed * 1103515245u + 12345u) & 0x7fffffff);
        }
    }
}

public static class AdvancedPuzzleFactory
{
    private static readonly string[] LogicSymbols = ["Lantern", "Raven", "Crown", "Anchor", "Thorn", "Mirror", "Bell", "Bloom", "Spire", "Mask"];
    private static readonly string[] MemoryRunes = ["A", "B", "C", "D", "E"];
    private static readonly (string Word, string Descriptor)[] WordDescriptorCorpus =
    [
        ("EMBER", "HOT"),
        ("MOSS", "GREEN"),
        ("GLASS", "FRAGILE"),
        ("THORN", "SHARP"),
        ("HONEY", "SWEET"),
        ("STONE", "HEAVY"),
        ("VELVET", "SOFT"),
        ("INK", "DARK"),
        ("SALT", "BRINY"),
        ("FROST", "COLD"),
        ("SPICE", "PUNGENT"),
        ("AMBER", "GOLDEN"),
        ("QUARTZ", "HARD"),
        ("VIOLET", "PURPLE"),
        ("IVORY", "PALE"),
        ("CEDAR", "WOODY"),
        ("THUNDER", "LOUD"),
        ("VELVET", "LUXURIOUS"),
        ("RUST", "CORRODED"),
        ("ONYX", "BLACK"),
        ("CINDER", "SMOKY"),
        ("HARBOR", "SAFE"),
    ];
    private static readonly (string Word, string Clue)[] CipherWords =
    [
        ("KEY", "What opens the hidden chamber?"),
        ("EYE", "What watches every corridor?"),
        ("ORB", "What hums at the heart of the maze?"),
        ("ARC", "What shape bridges the current?"),
        ("GEM", "What prize glows inside the vault?"),
        ("MAP", "What reveals a route without walking it?"),
        ("SUN", "What burns without a wick?"),
        ("OWL", "What sees in darkness?"),
    ];

    private static readonly (string[] Statements, int CorrectLeverIndex)[] ParadoxScenarios =
    [
        (["{0}: \"{1} lies.\"", "{1}: \"The left lever is false.\"", "{2}: \"Exactly one of us lies.\""], 1),
        (["{0}: \"{2} tells the truth.\"", "{1}: \"The right lever fails.\"", "{2}: \"{0} and I disagree.\""], 0),
        (["{0}: \"The center lever is safe.\"", "{1}: \"{0} lies.\"", "{2}: \"Only one of us tells the truth.\""], 2),
        (["{0}: \"Either {1} lies or the center lever is false.\"", "{1}: \"{2} tells the truth.\"", "{2}: \"The right lever is safe only if {0} lies.\""], 2),
        (["{0}: \"Exactly one of us speaks truly.\"", "{1}: \"The left lever fails.\"", "{2}: \"{1} lies.\""], 1),
        (["{0}: \"If I lie, the right lever is safe.\"", "{1}: \"{0} tells the truth.\"", "{2}: \"The center lever fails.\""], 0),
        (["{0}: \"If {1} speaks truly, the left lever fails.\"", "{1}: \"{2} lies exactly when the center lever is safe.\"", "{2}: \"The right lever is false and {0} is lying.\""], 1),
        (["{0}: \"The true lever is not right, and {2} lies.\"", "{1}: \"{0} and I cannot both be truthful.\"", "{2}: \"If the center lever is false, then {1} tells the truth.\""], 0),
    ];

    public static RoomPuzzle Create(string seed, string runNonce, MazeRoomDefinition room, MazeDifficulty difficulty)
    {
        return CreateDocPanelPuzzle(seed, runNonce, room, difficulty);
    }

    private static RoomPuzzle CreateDocPanelPuzzle(string seed, string runNonce, MazeRoomDefinition room, MazeDifficulty difficulty)
    {
        var key = char.ToLowerInvariant(room.PuzzleKey);
        var (title, familyId, instruction) = key switch
        {
            'p' => ("Chromatic Lock", "chromatic_lock", "Tune all channels inside tolerance, then commit."),
            'q' => ("Signal Decay", "signal_decay", "Stabilize waveform controls and hold lock through commit."),
            'r' => ("Dead Reckoning", "dead_reckoning", "Set route vectors, then simulate and verify destination."),
            's' => ("Pressure Grid", "pressure_grid", "Configure the pressure lattice to the target mask."),
            't' => ("Cipher Wheel", "cipher_wheel", "Align wheel discs to decode the target token stream."),
            'u' => ("Gravity Well", "gravity_well", "Tune attractor fields so the trajectory reaches the receiver."),
            'v' => ("Echo Chamber", "echo_chamber", "Configure reflectors so beam bounces satisfy receiver conditions."),
            'w' => ("Token Flood", "token_flood", "Route flow correctly to fill vessels without spill."),
            'x' => ("Memory Palace", "memory_palace", "Match all memory pairs before timer collapse."),
            'y' => ("Fault Line", "fault_line", "Shift strata controls until all fault seams align."),
            'z' => ("Temporal Grid", "temporal_grid", "Resolve the temporal grid across rows and columns."),
            _ => throw new MazeSeedParseException($"Unknown advanced puzzle type '{room.PuzzleKey}'."),
        };

        return new SoloPanelBiblePuzzle(
            key,
            title,
            familyId,
            instruction,
            seed,
            runNonce,
            room.Coordinates,
            difficulty);
    }

}
