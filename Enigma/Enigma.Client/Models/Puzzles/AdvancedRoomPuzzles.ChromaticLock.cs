namespace Enigma.Client.Models.Gameplay;

using System.Globalization;

public sealed partial class SoloPanelBiblePuzzle
{
    private readonly record struct ChromaticLockTolerance(int Hue, int Saturation, int Lightness);

    private readonly record struct ChromaticLockColor(int Hue, int Saturation, int Lightness);

    private readonly record struct ChromaticLockConfig(
        double TimerSeconds,
        int Attempts,
        int RoundCount,
        ChromaticLockTolerance Tolerance,
        int HoldTicks,
        double LockoutSeconds,
        bool ShowDelta,
        bool DriftEnabled,
        double DriftIntervalSeconds,
        bool Oscillate,
        bool ResetMeterOnLockout,
        int HueStepDegrees,
        bool CoupleSaturationLightness,
        string DifficultyLabel);

    private readonly record struct ChromaticLockTelemetry(
        int DeltaHue,
        int DeltaSaturation,
        int DeltaLightness,
        bool InsideTolerance,
        int MatchPercent);

    private const double ChromaticLockTickRate = 60d;

    private readonly List<ChromaticLockColor> _chromaticRounds = [];
    private ChromaticLockConfig _chromaticConfig;
    private ChromaticLockColor _chromaticBaseTarget;
    private ChromaticLockColor _chromaticEffectiveTarget;
    private int _chromaticCurrentHue = 180;
    private int _chromaticCurrentSaturation = 50;
    private int _chromaticCurrentLightness = 50;
    private int _chromaticInitialHue = 180;
    private int _chromaticInitialSaturation = 50;
    private int _chromaticInitialLightness = 50;
    private double _chromaticHoldTicksProgress;
    private double _chromaticRoundElapsedSeconds;
    private double _chromaticDriftElapsedSeconds;
    private double _chromaticLockoutRemainingSeconds;
    private bool _chromaticReadyToCommit;
    private bool _chromaticTargetFrozen;
    private int _chromaticDriftStepIndex;

    private void ConfigureChromaticLockFamily(bool simplifiedFallback = false)
    {
        _mechanic = SoloPanelMechanic.ChromaticLock;
        _progressLabel = "Coherence / Synchronization";
        _chromaticRounds.Clear();

        var config = GetChromaticLockConfig(simplifiedFallback);
        _chromaticConfig = config;
        _timerSeconds = config.TimerSeconds;
        _attemptsRemaining = config.Attempts;
        _stagesRequired = config.RoundCount;

        for (var roundIndex = 0; roundIndex < config.RoundCount; roundIndex++)
        {
            _chromaticRounds.Add(BuildChromaticRound(roundIndex));
        }

        _stageIndex = Math.Clamp(_stageIndex, 0, Math.Max(0, config.RoundCount - 1));
        LoadChromaticLockRound(_stageIndex);
        _statusText = BuildChromaticLockStatusText();
        StatusText = _statusText;
    }

    private ChromaticLockConfig GetChromaticLockConfig(bool simplifiedFallback)
    {
        if (simplifiedFallback)
        {
            return new ChromaticLockConfig(
                TimerSeconds: 150d,
                Attempts: 4,
                RoundCount: 2,
                Tolerance: new ChromaticLockTolerance(12, 10, 10),
                HoldTicks: 0,
                LockoutSeconds: 0.35d,
                ShowDelta: true,
                DriftEnabled: false,
                DriftIntervalSeconds: 0d,
                Oscillate: false,
                ResetMeterOnLockout: false,
                HueStepDegrees: 1,
                CoupleSaturationLightness: false,
                DifficultyLabel: "EASY");
        }

        return _difficulty switch
        {
            MazeDifficulty.Easy => new ChromaticLockConfig(
                TimerSeconds: 150d,
                Attempts: 4,
                RoundCount: 2,
                Tolerance: new ChromaticLockTolerance(12, 10, 10),
                HoldTicks: 0,
                LockoutSeconds: 0.35d,
                ShowDelta: true,
                DriftEnabled: false,
                DriftIntervalSeconds: 0d,
                Oscillate: false,
                ResetMeterOnLockout: false,
                HueStepDegrees: 1,
                CoupleSaturationLightness: false,
                DifficultyLabel: "EASY"),
            MazeDifficulty.Medium => new ChromaticLockConfig(
                TimerSeconds: 105d,
                Attempts: 4,
                RoundCount: 3,
                Tolerance: new ChromaticLockTolerance(8, 6, 6),
                HoldTicks: 150,
                LockoutSeconds: 0.5d,
                ShowDelta: true,
                DriftEnabled: false,
                DriftIntervalSeconds: 0d,
                Oscillate: false,
                ResetMeterOnLockout: false,
                HueStepDegrees: 6,
                CoupleSaturationLightness: false,
                DifficultyLabel: "MEDIUM"),
            MazeDifficulty.Hard => new ChromaticLockConfig(
                TimerSeconds: 75d,
                Attempts: 3,
                RoundCount: 5,
                Tolerance: new ChromaticLockTolerance(3, 2, 2),
                HoldTicks: 300,
                LockoutSeconds: 1d,
                ShowDelta: false,
                DriftEnabled: false,
                DriftIntervalSeconds: 0d,
                Oscillate: false,
                ResetMeterOnLockout: true,
                HueStepDegrees: 1,
                CoupleSaturationLightness: true,
                DifficultyLabel: "HARD"),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    private bool IsChromaticLockExactMatch() =>
        _chromaticCurrentHue == _chromaticBaseTarget.Hue &&
        _chromaticCurrentSaturation == _chromaticBaseTarget.Saturation &&
        _chromaticCurrentLightness == _chromaticBaseTarget.Lightness;

    private ChromaticLockColor BuildChromaticRound(int roundIndex)
    {
        var hue = QuantizeChromaticHue(Math.Abs(PuzzleFactory.StableHash($"chromatic|round|h|{_solutionSeed}|{roundIndex}|{TierLevel}")) % 360);
        var saturation = 42 + (Math.Abs(PuzzleFactory.StableHash($"chromatic|round|s|{_solutionSeed}|{roundIndex}|{TierLevel}")) % 49);
        var lightness = 28 + (Math.Abs(PuzzleFactory.StableHash($"chromatic|round|l|{_solutionSeed}|{roundIndex}|{TierLevel}")) % 45);

        var candidate = new ChromaticLockColor(hue, saturation, lightness);
        for (var guard = 0; guard < 12; guard++)
        {
            if (_chromaticRounds.All(existing => IsChromaticRoundDistinct(existing, candidate)))
            {
                return candidate;
            }

            candidate = new ChromaticLockColor(
                NormalizeHue(candidate.Hue + 47),
                Math.Clamp(candidate.Saturation + 11, 35, 96),
                Math.Clamp(candidate.Lightness + ((guard & 1) == 0 ? 8 : -6), 18, 84));
        }

        return candidate;
    }

    private static bool IsChromaticRoundDistinct(ChromaticLockColor left, ChromaticLockColor right) =>
        GetHueDistance(left.Hue, right.Hue) >= 28 ||
        Math.Abs(left.Saturation - right.Saturation) >= 10 ||
        Math.Abs(left.Lightness - right.Lightness) >= 10;

    private void LoadChromaticLockRound(int roundIndex)
    {
        if (_chromaticRounds.Count == 0)
        {
            _chromaticRounds.Add(new ChromaticLockColor(320, 78, 52));
        }

        roundIndex = Math.Clamp(roundIndex, 0, _chromaticRounds.Count - 1);
        _chromaticBaseTarget = _chromaticRounds[roundIndex];
        _chromaticEffectiveTarget = _chromaticBaseTarget;
        _chromaticHoldTicksProgress = 0d;
        _chromaticRoundElapsedSeconds = 0d;
        _chromaticDriftElapsedSeconds = 0d;
        _chromaticLockoutRemainingSeconds = 0d;
        _chromaticReadyToCommit = false;
        _chromaticTargetFrozen = false;
        _chromaticDriftStepIndex = 0;

        var initial = BuildChromaticInitialState(roundIndex, _chromaticBaseTarget, _chromaticConfig.Tolerance);
        _chromaticInitialHue = initial.Hue;
        _chromaticInitialSaturation = initial.Saturation;
        _chromaticInitialLightness = initial.Lightness;
        _chromaticCurrentHue = initial.Hue;
        _chromaticCurrentSaturation = initial.Saturation;
        _chromaticCurrentLightness = initial.Lightness;
    }

    private ChromaticLockColor BuildChromaticInitialState(int roundIndex, ChromaticLockColor target, ChromaticLockTolerance tolerance)
    {
        var hue = NormalizeHue(Math.Abs(PuzzleFactory.StableHash($"chromatic|init|h|{_solutionSeed}|{roundIndex}|{TierLevel}")) % 360);
        var saturation = 14 + (Math.Abs(PuzzleFactory.StableHash($"chromatic|init|s|{_solutionSeed}|{roundIndex}|{TierLevel}")) % 73);
        var lightness = 14 + (Math.Abs(PuzzleFactory.StableHash($"chromatic|init|l|{_solutionSeed}|{roundIndex}|{TierLevel}")) % 73);

        var initial = new ChromaticLockColor(hue, saturation, lightness);
        if (GetHueDistance(initial.Hue, target.Hue) <= tolerance.Hue &&
            Math.Abs(initial.Saturation - target.Saturation) <= tolerance.Saturation &&
            Math.Abs(initial.Lightness - target.Lightness) <= tolerance.Lightness)
        {
            initial = new ChromaticLockColor(
                NormalizeHue(target.Hue + 96),
                Math.Clamp(target.Saturation + 24, 4, 100),
                Math.Clamp(target.Lightness + 18, 4, 100));
        }

        return initial;
    }

    private void UpdateChromaticLock(PuzzleUpdateContext context)
    {
        if (_chromaticConfig.DriftEnabled && !_chromaticTargetFrozen)
        {
            _chromaticDriftElapsedSeconds += context.DeltaTimeSeconds;
            while (_chromaticDriftElapsedSeconds >= _chromaticConfig.DriftIntervalSeconds)
            {
                _chromaticDriftElapsedSeconds -= _chromaticConfig.DriftIntervalSeconds;
                ApplyChromaticLockDrift();
            }
        }

        _chromaticRoundElapsedSeconds += context.DeltaTimeSeconds;

        if (_chromaticLockoutRemainingSeconds > 0d)
        {
            _chromaticLockoutRemainingSeconds = Math.Max(0d, _chromaticLockoutRemainingSeconds - context.DeltaTimeSeconds);
        }

        RefreshChromaticEffectiveTarget();
        var telemetry = GetChromaticLockTelemetry();
        if (IsChromaticLockExactMatch())
        {
            if (!_chromaticTargetFrozen)
            {
                _chromaticTargetFrozen = true;
                _chromaticEffectiveTarget = _chromaticBaseTarget;
                telemetry = GetChromaticLockTelemetry();
            }

            if (!IsChromaticLockLockedOut())
            {
                if (_chromaticConfig.HoldTicks <= 0)
                {
                    _chromaticHoldTicksProgress = 0d;
                    _chromaticReadyToCommit = true;
                }
                else
                {
                    _chromaticHoldTicksProgress = Math.Min(_chromaticConfig.HoldTicks, _chromaticHoldTicksProgress + (context.DeltaTimeSeconds * ChromaticLockTickRate));
                    _chromaticReadyToCommit = _chromaticHoldTicksProgress >= _chromaticConfig.HoldTicks;
                }
            }
        }
        else if (_chromaticTargetFrozen && _chromaticHoldTicksProgress > 0d)
        {
            TriggerChromaticLockLockout();
            telemetry = GetChromaticLockTelemetry();
        }
        else
        {
            _chromaticHoldTicksProgress = 0d;
            _chromaticReadyToCommit = false;
            _chromaticTargetFrozen = false;
        }

        _statusText = BuildChromaticLockStatusText(telemetry);
        StatusText = _statusText;
    }

    private void ApplyChromaticLockDrift()
    {
        var roundIndex = Math.Clamp(_stageIndex, 0, Math.Max(0, _chromaticRounds.Count - 1));
        var hueShift = (Math.Abs(PuzzleFactory.StableHash($"chromatic|drift|h|{_layoutSeed}|{roundIndex}|{_chromaticDriftStepIndex}")) % 11) - 5;
        var saturationShift = (Math.Abs(PuzzleFactory.StableHash($"chromatic|drift|s|{_layoutSeed}|{roundIndex}|{_chromaticDriftStepIndex}")) % 9) - 4;
        var lightnessShift = (Math.Abs(PuzzleFactory.StableHash($"chromatic|drift|l|{_layoutSeed}|{roundIndex}|{_chromaticDriftStepIndex}")) % 9) - 4;
        _chromaticDriftStepIndex++;

        _chromaticBaseTarget = new ChromaticLockColor(
            QuantizeChromaticHue(_chromaticBaseTarget.Hue + hueShift),
            Math.Clamp(_chromaticBaseTarget.Saturation + saturationShift, 8, 100),
            Math.Clamp(_chromaticBaseTarget.Lightness + lightnessShift, 8, 92));
    }

    private void RefreshChromaticEffectiveTarget()
    {
        _chromaticEffectiveTarget = _chromaticTargetFrozen
            ? _chromaticEffectiveTarget
            : GetChromaticLiveTarget();
    }

    private ChromaticLockColor GetChromaticLiveTarget()
    {
        if (!_chromaticConfig.Oscillate)
        {
            return _chromaticBaseTarget;
        }

        var t = _chromaticRoundElapsedSeconds;
        return new ChromaticLockColor(
            QuantizeChromaticHue(_chromaticBaseTarget.Hue + (int)Math.Round(15d * Math.Sin((2d * Math.PI * t) / 3d))),
            Math.Clamp(_chromaticBaseTarget.Saturation + (int)Math.Round(12d * Math.Sin((2d * Math.PI * t * 1.3d) / 3d)), 0, 100),
            Math.Clamp(_chromaticBaseTarget.Lightness + (int)Math.Round(12d * Math.Sin((2d * Math.PI * t * 0.7d) / 3d)), 0, 100));
    }

    private ChromaticLockTelemetry GetChromaticLockTelemetry()
    {
        var target = _chromaticEffectiveTarget;
        var deltaHue = GetHueDistance(_chromaticCurrentHue, target.Hue);
        var deltaSaturation = Math.Abs(_chromaticCurrentSaturation - target.Saturation);
        var deltaLightness = Math.Abs(_chromaticCurrentLightness - target.Lightness);
        var insideTolerance = deltaHue <= _chromaticConfig.Tolerance.Hue &&
                              deltaSaturation <= _chromaticConfig.Tolerance.Saturation &&
                              deltaLightness <= _chromaticConfig.Tolerance.Lightness;

        var match = Math.Max(
            0d,
            100d - (((deltaHue / 180d) + (deltaSaturation / 100d) + (deltaLightness / 100d)) / 3d * 100d));

        var matchPercent = IsChromaticLockLockedOut() && _chromaticConfig.ResetMeterOnLockout
            ? 0
            : Math.Clamp((int)Math.Round(match), 0, 100);

        return new ChromaticLockTelemetry(deltaHue, deltaSaturation, deltaLightness, insideTolerance, matchPercent);
    }

    private bool HandleChromaticLockAction(string command, double nowSeconds)
    {
        if (command == "chroma:hint")
        {
            if (_hintsRemaining <= 0)
            {
                _statusText = "No hints remaining.";
                StatusText = _statusText;
                return true;
            }

            _hintsRemaining--;
            _hintText = BuildChromaticLockHintText();
            _statusText = _hintText;
            StatusText = _statusText;
            return true;
        }

        if (command == "chroma:reset")
        {
            ResetChromaticLockStage(resetTimer: false, clearFailure: true, clearLockout: true);
            _statusText = "Chromatic lock reset.";
            StatusText = _statusText;
            return true;
        }

        if (command == "chroma:commit")
        {
            ResolveChromaticLockCommit();
            return true;
        }

        if (IsChromaticLockLockedOut())
        {
            _statusText = "Lockout active. Reacquire the colour window when the dampers clear.";
            StatusText = _statusText;
            return true;
        }

        var parts = command.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 4 || !string.Equals(parts[0], "chroma", StringComparison.OrdinalIgnoreCase) || !string.Equals(parts[1], "set", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawValue))
        {
            return false;
        }

        switch (parts[2])
        {
            case "h":
                _chromaticCurrentHue = QuantizeChromaticHue(rawValue);
                break;
            case "s":
                var priorSaturation = _chromaticCurrentSaturation;
                _chromaticCurrentSaturation = Math.Clamp(rawValue, 0, 100);
                if (_chromaticConfig.CoupleSaturationLightness)
                {
                    var delta = _chromaticCurrentSaturation - priorSaturation;
                    _chromaticCurrentLightness = Math.Clamp(_chromaticCurrentLightness + (int)Math.Round(delta / 10d), 0, 100);
                }
                break;
            case "l":
                _chromaticCurrentLightness = Math.Clamp(rawValue, 0, 100);
                break;
            default:
                return false;
        }

        _phase = PuzzlePhase.Configure;
        _status = PuzzleStatus.Active;
        RefreshChromaticEffectiveTarget();
        var telemetry = GetChromaticLockTelemetry();
        if (_chromaticConfig.HoldTicks <= 0)
        {
            _chromaticReadyToCommit = IsChromaticLockExactMatch();
        }
        _statusText = BuildChromaticLockStatusText();
        StatusText = _statusText;
        return true;
    }

    private IReadOnlyList<SoloPanelHudItem> BuildChromaticLockHud()
    {
        var telemetry = GetChromaticLockTelemetry();
        var holdPercent = Math.Round(GetChromaticLockHoldRatio() * 100d);
        return
        [
            new SoloPanelHudItem("Match", $"{telemetry.MatchPercent}%", _chromaticReadyToCommit ? "success" : telemetry.MatchPercent >= 70 ? "info" : "shared"),
            new SoloPanelHudItem("Round", $"{Math.Min(_stageIndex + 1, _stagesRequired)}/{Math.Max(1, _stagesRequired)}", "info"),
            new SoloPanelHudItem("Timer", $"{Math.Max(0, Math.Ceiling(_timerRemaining)):0}s", _timerRemaining <= 10d ? "danger" : _timerRemaining <= 20d ? "warning" : "shared"),
            new SoloPanelHudItem("Hold", $"{holdPercent:0}%", _chromaticReadyToCommit ? "success" : "info"),
            new SoloPanelHudItem("Hints", _hintsRemaining.ToString(CultureInfo.InvariantCulture), "info"),
        ];
    }

    private IReadOnlyList<SoloPanelActionItem> BuildChromaticLockActions(bool enabled)
    {
        var interactive = enabled && !IsChromaticLockLockedOut();
        return
        [
            new SoloPanelActionItem("chroma:commit", "Stabilize", "commit", interactive),
            new SoloPanelActionItem("chroma:reset", "Reset", "reset", enabled),
            new SoloPanelActionItem("chroma:hint", "Hint", "hint", enabled && _hintsRemaining > 0),
        ];
    }

    private IReadOnlyDictionary<string, string> BuildChromaticLockBoardSnapshot(Dictionary<string, string> board)
    {
        var telemetry = GetChromaticLockTelemetry();
        board["chromatic:difficulty"] = _chromaticConfig.DifficultyLabel;
        board["chromatic:timer_total"] = _timerSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        board["chromatic:round_index"] = Math.Min(_stageIndex + 1, Math.Max(1, _stagesRequired)).ToString(CultureInfo.InvariantCulture);
        board["chromatic:round_total"] = Math.Max(1, _stagesRequired).ToString(CultureInfo.InvariantCulture);
        board["chromatic:match_percent"] = telemetry.MatchPercent.ToString(CultureInfo.InvariantCulture);
        board["chromatic:hold_ticks"] = Math.Round(_chromaticHoldTicksProgress, 3).ToString("0.###", CultureInfo.InvariantCulture);
        board["chromatic:hold_required"] = _chromaticConfig.HoldTicks.ToString(CultureInfo.InvariantCulture);
        board["chromatic:hold_percent"] = Math.Round(GetChromaticLockHoldRatio() * 100d, 3).ToString("0.###", CultureInfo.InvariantCulture);
        board["chromatic:ready"] = _chromaticReadyToCommit ? "1" : "0";
        board["chromatic:lockout_active"] = IsChromaticLockLockedOut() ? "1" : "0";
        board["chromatic:target_frozen"] = _chromaticTargetFrozen ? "1" : "0";
        board["chromatic:delta_visible"] = _chromaticConfig.ShowDelta ? "1" : "0";
        board["chromatic:drift_active"] = _chromaticConfig.DriftEnabled ? "1" : "0";
        board["chromatic:oscillate_active"] = _chromaticConfig.Oscillate ? "1" : "0";
        board["chromatic:channel:h:step"] = _chromaticConfig.HueStepDegrees.ToString(CultureInfo.InvariantCulture);
        board["chromatic:lighting_shift_active"] = string.Equals(_mutatorId, "lighting_shift", StringComparison.OrdinalIgnoreCase) ? "1" : "0";

        board["chromatic:channel:h:current"] = _chromaticCurrentHue.ToString(CultureInfo.InvariantCulture);
        board["chromatic:channel:h:target"] = _chromaticEffectiveTarget.Hue.ToString(CultureInfo.InvariantCulture);
        board["chromatic:channel:h:delta"] = telemetry.DeltaHue.ToString(CultureInfo.InvariantCulture);
        board["chromatic:channel:h:tolerance"] = _chromaticConfig.Tolerance.Hue.ToString(CultureInfo.InvariantCulture);
        board["chromatic:channel:h:aligned"] = telemetry.DeltaHue <= _chromaticConfig.Tolerance.Hue ? "1" : "0";

        board["chromatic:channel:s:current"] = _chromaticCurrentSaturation.ToString(CultureInfo.InvariantCulture);
        board["chromatic:channel:s:target"] = _chromaticEffectiveTarget.Saturation.ToString(CultureInfo.InvariantCulture);
        board["chromatic:channel:s:delta"] = telemetry.DeltaSaturation.ToString(CultureInfo.InvariantCulture);
        board["chromatic:channel:s:tolerance"] = _chromaticConfig.Tolerance.Saturation.ToString(CultureInfo.InvariantCulture);
        board["chromatic:channel:s:aligned"] = telemetry.DeltaSaturation <= _chromaticConfig.Tolerance.Saturation ? "1" : "0";

        board["chromatic:channel:l:current"] = _chromaticCurrentLightness.ToString(CultureInfo.InvariantCulture);
        board["chromatic:channel:l:target"] = _chromaticEffectiveTarget.Lightness.ToString(CultureInfo.InvariantCulture);
        board["chromatic:channel:l:delta"] = telemetry.DeltaLightness.ToString(CultureInfo.InvariantCulture);
        board["chromatic:channel:l:tolerance"] = _chromaticConfig.Tolerance.Lightness.ToString(CultureInfo.InvariantCulture);
        board["chromatic:channel:l:aligned"] = telemetry.DeltaLightness <= _chromaticConfig.Tolerance.Lightness ? "1" : "0";

        for (var index = 0; index < _chromaticRounds.Count; index++)
        {
            var round = _chromaticRounds[index];
            var keyIndex = index + 1;
            board[$"chromatic:round:{keyIndex}:h"] = round.Hue.ToString(CultureInfo.InvariantCulture);
            board[$"chromatic:round:{keyIndex}:s"] = round.Saturation.ToString(CultureInfo.InvariantCulture);
            board[$"chromatic:round:{keyIndex}:l"] = round.Lightness.ToString(CultureInfo.InvariantCulture);
            board[$"chromatic:round:{keyIndex}:solved"] = index < _stageIndex ? "1" : "0";
        }

        return board;
    }

    private void ResolveChromaticLockCommit()
    {
        if (IsChromaticLockLockedOut())
        {
            _statusText = "Lockout active. Wait for the dampers to clear.";
            StatusText = _statusText;
            return;
        }

        if (!IsChromaticLockReadyToCommit())
        {
            RegisterFailure("phase_drift", "Phase Drift. Hold the colour inside tolerance before stabilizing.");
            return;
        }

        _stageIndex++;
        if (_stageIndex < _stagesRequired)
        {
            LoadChromaticLockRound(_stageIndex);
            _status = PuzzleStatus.Active;
            _phase = PuzzlePhase.Configure;
            _statusText = $"Colour {_stageIndex}/{_stagesRequired} stabilized. Acquire the next lock.";
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

    private bool IsChromaticLockReadyToCommit() =>
        _chromaticReadyToCommit &&
        !IsChromaticLockLockedOut() &&
        IsChromaticLockExactMatch();

    private void RestartChromaticLockAfterTimeout()
    {
        ConfigureFamilyState();
        ApplyFailureState(GetTimeoutFailureCode(), "Time window collapsed.");
        _status = PuzzleStatus.Active;
        _phase = PuzzlePhase.Configure;
        _statusText = string.IsNullOrWhiteSpace(_failureLabel)
            ? "Time expired. Chromatic lock restarted."
            : $"{_failureLabel}: {_recoveryText}";
        StatusText = _statusText;
    }

    private void ResetChromaticLockStage(bool resetTimer, bool clearFailure, bool clearLockout)
    {
        if (resetTimer)
        {
            _timerRemaining = _timerSeconds;
        }

        if (clearFailure)
        {
            _failureCode = string.Empty;
            _failureLabel = string.Empty;
            _recoveryText = string.Empty;
        }

        if (clearLockout)
        {
            _chromaticLockoutRemainingSeconds = 0d;
        }

        LoadChromaticLockRound(Math.Clamp(_stageIndex, 0, Math.Max(0, _chromaticRounds.Count - 1)));
        _status = PuzzleStatus.Active;
        _phase = PuzzlePhase.Configure;
        _statusText = BuildChromaticLockStatusText();
        StatusText = _statusText;
    }

    private void TriggerChromaticLockLockout()
    {
        _chromaticHoldTicksProgress = 0d;
        _chromaticReadyToCommit = false;
        _chromaticTargetFrozen = false;
        _chromaticLockoutRemainingSeconds = Math.Max(_chromaticLockoutRemainingSeconds, _chromaticConfig.LockoutSeconds);
        RefreshChromaticEffectiveTarget();
    }

    private string BuildChromaticLockHintText()
    {
        var telemetry = GetChromaticLockTelemetry();
        if (telemetry.DeltaHue >= telemetry.DeltaSaturation && telemetry.DeltaHue >= telemetry.DeltaLightness)
        {
            var clockwise = NormalizeHue(_chromaticEffectiveTarget.Hue - _chromaticCurrentHue);
            var counterClockwise = 360 - clockwise;
            var direction = clockwise <= counterClockwise ? "clockwise" : "counter-clockwise";
            var delta = Math.Min(clockwise, counterClockwise);
            return $"Hint: shift hue {direction} by roughly {delta} degrees.";
        }

        if (telemetry.DeltaSaturation >= telemetry.DeltaLightness)
        {
            var direction = _chromaticCurrentSaturation < _chromaticEffectiveTarget.Saturation ? "increase" : "reduce";
            return $"Hint: {direction} saturation toward {_chromaticEffectiveTarget.Saturation}%";
        }

        var lightDirection = _chromaticCurrentLightness < _chromaticEffectiveTarget.Lightness ? "raise" : "lower";
        return $"Hint: {lightDirection} lightness toward {_chromaticEffectiveTarget.Lightness}%";
    }

    private string BuildChromaticLockStatusText() => BuildChromaticLockStatusText(GetChromaticLockTelemetry());

    private string BuildChromaticLockStatusText(ChromaticLockTelemetry telemetry)
    {
        if (IsChromaticLockLockedOut())
        {
            return "Lockout clearing. Reacquire the colour window when the dampers reset.";
        }

        if (_chromaticReadyToCommit)
        {
            return $"Signal lock ready. Stabilize colour {Math.Min(_stageIndex + 1, _stagesRequired)}/{_stagesRequired}.";
        }

        if (_chromaticConfig.HoldTicks <= 0)
        {
            return $"Match the vault colour. {telemetry.MatchPercent}% synchronized.";
        }

        if (_chromaticTargetFrozen && _chromaticHoldTicksProgress > 0d)
        {
            return $"Hold coherence. {telemetry.MatchPercent}% synchronized.";
        }

        return $"Tune the vault channels. Match {telemetry.MatchPercent}% and hold inside tolerance.";
    }

    private double GetChromaticLockAxisValue() => GetChromaticLockTelemetry().MatchPercent / 100d;

    private double GetChromaticLockMechanicProgress()
    {
        var match = GetChromaticLockAxisValue();
        var hold = GetChromaticLockHoldRatio();
        return Math.Clamp((match * 0.7d) + (hold * 0.3d), 0d, 1d);
    }

    private double GetChromaticLockHoldRatio() =>
        _chromaticConfig.HoldTicks <= 0
            ? (_chromaticReadyToCommit ? 1d : 0d)
            : Math.Clamp(_chromaticHoldTicksProgress / _chromaticConfig.HoldTicks, 0d, 1d);

    private bool TryBuildChromaticLockSolveTrace(out PuzzleSolveTrace trace)
    {
        if (_chromaticRounds.Count == 0)
        {
            trace = new PuzzleSolveTrace(Array.Empty<PuzzleSolveStep>(), "Chromatic lock trace unavailable.");
            return false;
        }

        var steps = new List<PuzzleSolveStep>(_chromaticRounds.Count * 5);
        var holdSeconds = _chromaticConfig.HoldTicks <= 0
            ? 0d
            : (_chromaticConfig.HoldTicks / ChromaticLockTickRate) + (1d / ChromaticLockTickRate);
        foreach (var entry in _chromaticRounds.Skip(_stageIndex).Select((round, index) => new { round, roundIndex = _stageIndex + index }))
        {
            foreach (var channel in BuildChromaticSolveChannelOrder(entry.roundIndex))
            {
                var value = channel switch
                {
                    "h" => entry.round.Hue,
                    "s" => entry.round.Saturation,
                    _ => entry.round.Lightness,
                };
                steps.Add(new PuzzleSolveStep($"chroma:set:{channel}:{value.ToString(CultureInfo.InvariantCulture)}"));
            }

            if (holdSeconds > 0d)
            {
                steps.Add(new PuzzleSolveStep((string?)null, holdSeconds));
            }
            steps.Add(new PuzzleSolveStep("chroma:commit"));
        }

        trace = new PuzzleSolveTrace(steps, "Chromatic lock preflight trace.");
        return steps.Count > 0;
    }

    private IReadOnlyList<string> BuildChromaticSolveChannelOrder(int roundIndex)
    {
        if (_chromaticConfig.CoupleSaturationLightness)
        {
            return ["h", "s", "l"];
        }

        var order = new List<string> { "h", "s", "l" };
        var state = Math.Abs(PuzzleFactory.StableHash($"chromatic|solve|{_solutionSeed}|{roundIndex}|{TierLevel}"));
        for (var index = order.Count - 1; index > 0; index--)
        {
            state = (state * 1103515245 + 12345) & 0x7fffffff;
            var swapIndex = state % (index + 1);
            (order[index], order[swapIndex]) = (order[swapIndex], order[index]);
        }

        return order;
    }

    private void ApplyChromaticLockFallbackTemplate()
    {
        _mechanic = SoloPanelMechanic.ChromaticLock;
        _progressLabel = "Coherence / Synchronization";
        _chromaticConfig = GetChromaticLockConfig(simplifiedFallback: true);
        _chromaticRounds.Clear();
        _chromaticRounds.AddRange(
        [
            new ChromaticLockColor(340, 82, 55),
            new ChromaticLockColor(198, 88, 46),
        ]);

        _stagesRequired = _chromaticRounds.Count;
        _stageIndex = 0;
        _timerSeconds = _chromaticConfig.TimerSeconds;
        _attemptsRemaining = _chromaticConfig.Attempts;
        LoadChromaticLockRound(0);
        _statusText = "Fallback template engaged. Stabilize the chromatic vault.";
        StatusText = _statusText;
    }

    private bool IsChromaticLockLockedOut() => _chromaticLockoutRemainingSeconds > 0.0001d;

    private static int GetHueDistance(int left, int right)
    {
        var delta = Math.Abs(NormalizeHue(left) - NormalizeHue(right));
        return Math.Min(delta, 360 - delta);
    }

    private static int NormalizeHue(int value)
    {
        var normalized = value % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private int QuantizeChromaticHue(int value)
    {
        var normalized = NormalizeHue(value);
        if (_chromaticConfig.HueStepDegrees <= 1)
        {
            return normalized;
        }

        var quantized = (int)Math.Round(normalized / (double)_chromaticConfig.HueStepDegrees, MidpointRounding.AwayFromZero) * _chromaticConfig.HueStepDegrees;
        return NormalizeHue(quantized);
    }
}
