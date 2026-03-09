namespace Enigma.Client.Models.Gameplay;

using System.Globalization;
using System.Text;

public sealed partial class SoloPanelBiblePuzzle
{
    private enum SignalWaveType
    {
        Sine,
        Square,
        Saw,
    }

    private sealed class SignalDecayComponentState
    {
        public required string Key { get; init; }
        public required SignalWaveType InitialWaveType { get; init; }
        public required SignalWaveType TargetWaveType { get; init; }
        public SignalWaveType CurrentWaveType { get; set; }
        public required double InitialAmplitude { get; init; }
        public required double TargetAmplitude { get; init; }
        public double CurrentAmplitude { get; set; }
        public required int InitialFrequency { get; init; }
        public required int TargetFrequency { get; init; }
        public int CurrentFrequency { get; set; }
        public required int InitialPhaseDegrees { get; init; }
        public required int TargetPhaseDegrees { get; init; }
        public int CurrentPhaseDegrees { get; set; }
    }

    private readonly record struct SignalDecayConfig(
        int ComponentCount,
        double TimerSeconds,
        int Attempts,
        double Threshold,
        double HoldSeconds,
        double PreviewSeconds,
        bool ShowFft,
        bool ShowWaveLabels,
        int PhaseStepDegrees,
        double NoiseStrength,
        bool CrossChannelBleed,
        string LevelDescription);

    private const int SignalDecayScopeWidth = 720;
    private const int SignalDecayScopeHeight = 240;
    private const int SignalDecaySampleCount = 96;

    private readonly List<SignalDecayComponentState> _signalComponents = [];
    private readonly Dictionary<string, SignalDecayComponentState> _signalComponentLookup = new(StringComparer.OrdinalIgnoreCase);
    private double _signalThreshold;
    private double _signalHoldRequiredSeconds;
    private double _signalHoldProgressSeconds;
    private double _signalPreviewSeconds;
    private double _signalPreviewRemainingSeconds;
    private bool _signalPreviewVisible = true;
    private bool _signalShowFft;
    private bool _signalShowWaveLabels = true;
    private int _signalPhaseStepDegrees = 15;
    private double _signalNoiseStrength;
    private bool _signalCrossChannelBleed;
    private double[] _signalTargetSamples = [];
    private double[] _signalCurrentSamples = [];
    private string _signalTargetPath = string.Empty;
    private string _signalCurrentPath = string.Empty;
    private string _signalFftBins = string.Empty;
    private string _signalLevelDescription = string.Empty;

    private void ConfigureSignalDecayFamily(bool simplifiedFallback = false)
    {
        _mechanic = SoloPanelMechanic.SignalDecay;
        _stagesRequired = 1;
        _progressLabel = "Signal Stability / Coherence";
        _signalComponents.Clear();
        _signalComponentLookup.Clear();

        var config = GetSignalDecayConfig(simplifiedFallback);
        _timerSeconds = config.TimerSeconds;
        _attemptsRemaining = config.Attempts;
        _signalThreshold = config.Threshold;
        _signalHoldRequiredSeconds = config.HoldSeconds;
        _signalPreviewSeconds = config.PreviewSeconds;
        _signalPreviewRemainingSeconds = config.PreviewSeconds;
        _signalPreviewVisible = config.PreviewSeconds <= 0d || config.PreviewSeconds > 0d;
        _signalShowFft = config.ShowFft;
        _signalShowWaveLabels = config.ShowWaveLabels;
        _signalPhaseStepDegrees = config.PhaseStepDegrees;
        _signalNoiseStrength = config.NoiseStrength;
        _signalCrossChannelBleed = config.CrossChannelBleed;
        _signalLevelDescription = config.LevelDescription;
        _signalHoldProgressSeconds = 0d;

        for (var index = 0; index < config.ComponentCount; index++)
        {
            var key = $"k{index + 1}";
            var targetWaveType = BuildSignalWaveType(index, useTargetSeed: true);
            var targetAmplitude = BuildSignalAmplitude(index, useTargetSeed: true);
            var targetFrequency = BuildSignalFrequency(index, useTargetSeed: true);
            var targetPhase = BuildSignalPhase(index, useTargetSeed: true);

            var initialWaveType = BuildSignalWaveType(index, useTargetSeed: false);
            var initialAmplitude = BuildSignalAmplitude(index, useTargetSeed: false);
            var initialFrequency = BuildSignalFrequency(index, useTargetSeed: false);
            var initialPhase = BuildSignalPhase(index, useTargetSeed: false);

            if (initialWaveType == targetWaveType)
            {
                initialWaveType = NextSignalWaveType(initialWaveType);
            }

            if (initialFrequency == targetFrequency)
            {
                initialFrequency = (initialFrequency % 8) + 1;
            }

            if (Math.Abs(initialAmplitude - targetAmplitude) < 0.001d)
            {
                initialAmplitude = QuantizeSignalAmplitude(initialAmplitude + 0.15d);
            }

            if (initialPhase == targetPhase)
            {
                initialPhase = NormalizeSignalPhase(initialPhase + (_signalPhaseStepDegrees * 2));
            }

            var component = new SignalDecayComponentState
            {
                Key = key,
                InitialWaveType = initialWaveType,
                TargetWaveType = targetWaveType,
                CurrentWaveType = initialWaveType,
                InitialAmplitude = initialAmplitude,
                TargetAmplitude = targetAmplitude,
                CurrentAmplitude = initialAmplitude,
                InitialFrequency = initialFrequency,
                TargetFrequency = targetFrequency,
                CurrentFrequency = initialFrequency,
                InitialPhaseDegrees = initialPhase,
                TargetPhaseDegrees = targetPhase,
                CurrentPhaseDegrees = initialPhase,
            };

            _signalComponents.Add(component);
            _signalComponentLookup[key] = component;
            _orderedKeys.Add(key);
        }

        RefreshSignalDecayTelemetry();
    }

    private SignalDecayConfig GetSignalDecayConfig(bool simplifiedFallback)
    {
        if (simplifiedFallback)
        {
            return new SignalDecayConfig(
                ComponentCount: 2,
                TimerSeconds: 120d,
                Attempts: 4,
                Threshold: 0.90d,
                HoldSeconds: 1.2d,
                PreviewSeconds: 0d,
                ShowFft: false,
                ShowWaveLabels: true,
                PhaseStepDegrees: 15,
                NoiseStrength: 0d,
                CrossChannelBleed: false,
                LevelDescription: "Fallback diagnostics: reconstruct the composite waveform with two labeled channels.");
        }

        var stageIndex = Math.Clamp(_stageLevel, 1, 4);
        return _difficulty switch
        {
            MazeDifficulty.Easy => new SignalDecayConfig(
                ComponentCount: stageIndex switch { 1 => 2, 2 => 3, 3 => 3, _ => 4 },
                TimerSeconds: stageIndex switch { 1 => 120d, 2 => 110d, 3 => 95d, _ => 85d },
                Attempts: stageIndex <= 2 ? 5 : 4,
                Threshold: stageIndex switch { 1 => 0.90d, 2 => 0.92d, 3 => 0.93d, _ => 0.94d },
                HoldSeconds: stageIndex switch { 1 => 1.2d, 2 => 1.5d, 3 => 1.8d, _ => 2.0d },
                PreviewSeconds: 0d,
                ShowFft: false,
                ShowWaveLabels: true,
                PhaseStepDegrees: 30,
                NoiseStrength: 0d,
                CrossChannelBleed: false,
                LevelDescription: stageIndex switch
                {
                    1 => "Stage 1: tune two labeled channels until the composite trace settles into the ghost waveform.",
                    2 => "Stage 2: a third component broadens the waveform. Maintain coherence instead of chasing a single dial.",
                    3 => "Stage 3: overlap denser components without losing phase discipline.",
                    _ => "Stage 4: full signal reconstruction. Balance four channels and hold the lock window long enough to stabilize."
                }),
            MazeDifficulty.Medium => new SignalDecayConfig(
                ComponentCount: stageIndex switch { 1 => 2, 2 => 3, 3 => 4, _ => 4 },
                TimerSeconds: stageIndex switch { 1 => 100d, 2 => 90d, 3 => 80d, _ => 70d },
                Attempts: stageIndex <= 2 ? 4 : 3,
                Threshold: stageIndex switch { 1 => 0.93d, 2 => 0.94d, 3 => 0.95d, _ => 0.96d },
                HoldSeconds: stageIndex switch { 1 => 1.5d, 2 => 1.8d, 3 => 2.0d, _ => 2.2d },
                PreviewSeconds: stageIndex switch { 1 => 10d, 2 => 9d, 3 => 8d, _ => 7d },
                ShowFft: true,
                ShowWaveLabels: true,
                PhaseStepDegrees: 15,
                NoiseStrength: 0.03d,
                CrossChannelBleed: false,
                LevelDescription: stageIndex switch
                {
                    1 => "Stage 1: use the temporary ghost trace and clear channel labels to learn the reconstruction console.",
                    2 => "Stage 2: the spectrum panel reveals dominant bins while a third channel broadens the lock window.",
                    3 => "Stage 3: four channels interact. Use scope shape and spectrum bias together.",
                    _ => "Stage 4: the full medium reconstruction stack. Lock coherence, then stabilize before the window collapses."
                }),
            MazeDifficulty.Hard => new SignalDecayConfig(
                ComponentCount: stageIndex switch { 1 => 3, 2 => 4, 3 => 5, _ => 5 },
                TimerSeconds: stageIndex switch { 1 => 80d, 2 => 72d, 3 => 64d, _ => 58d },
                Attempts: stageIndex == 1 ? 4 : stageIndex == 2 ? 3 : 2,
                Threshold: stageIndex switch { 1 => 0.95d, 2 => 0.96d, 3 => 0.97d, _ => 0.975d },
                HoldSeconds: stageIndex switch { 1 => 2.0d, 2 => 2.2d, 3 => 2.5d, _ => 2.8d },
                PreviewSeconds: stageIndex switch { 1 => 6d, 2 => 5d, 3 => 4d, _ => 3.5d },
                ShowFft: false,
                ShowWaveLabels: stageIndex == 1,
                PhaseStepDegrees: 5,
                NoiseStrength: 0.06d,
                CrossChannelBleed: true,
                LevelDescription: stageIndex switch
                {
                    1 => "Stage 1: the ghost trace will fade. Memorize the waveform silhouette before reconstructing it.",
                    2 => "Stage 2: labels vanish after calibration. Trust the scope and your own channel reads.",
                    3 => "Stage 3: five channels, no spectrum view, and a short preview window.",
                    _ => "Stage 4: full anomaly reconstruction. Hidden labels, maximum density, and the narrowest stability window."
                }),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    private SignalWaveType BuildSignalWaveType(int index, bool useTargetSeed)
    {
        var seed = useTargetSeed ? _layoutSeed : _solutionSeed;
        var noise = Math.Abs(PuzzleFactory.StableHash($"signal|wave|{seed}|{index}|{TierLevel}"));
        return (noise % 3) switch
        {
            0 => SignalWaveType.Sine,
            1 => SignalWaveType.Square,
            _ => SignalWaveType.Saw,
        };
    }

    private double BuildSignalAmplitude(int index, bool useTargetSeed)
    {
        var seed = useTargetSeed ? _layoutSeed : _solutionSeed;
        var noise = Math.Abs(PuzzleFactory.StableHash($"signal|amp|{seed}|{index}|{TierLevel}"));
        return 0.10d + ((noise % 17) * 0.05d);
    }

    private int BuildSignalFrequency(int index, bool useTargetSeed)
    {
        var seed = useTargetSeed ? _layoutSeed : _solutionSeed;
        var noise = Math.Abs(PuzzleFactory.StableHash($"signal|freq|{seed}|{index}|{TierLevel}"));
        return 1 + (noise % 8);
    }

    private int BuildSignalPhase(int index, bool useTargetSeed)
    {
        var seed = useTargetSeed ? _layoutSeed : _solutionSeed;
        var noise = Math.Abs(PuzzleFactory.StableHash($"signal|phase|{seed}|{index}|{TierLevel}"));
        var step = Math.Max(5, _signalPhaseStepDegrees);
        var slots = 360 / step;
        return (noise % slots) * step;
    }

    private static SignalWaveType NextSignalWaveType(SignalWaveType value) => value switch
    {
        SignalWaveType.Sine => SignalWaveType.Square,
        SignalWaveType.Square => SignalWaveType.Saw,
        _ => SignalWaveType.Sine,
    };
    private void UpdateSignalDecay(PuzzleUpdateContext context)
    {
        if (_signalPreviewSeconds > 0d && _signalPreviewVisible)
        {
            _signalPreviewRemainingSeconds = Math.Max(0d, _signalPreviewRemainingSeconds - context.DeltaTimeSeconds);
            if (_signalPreviewRemainingSeconds <= 0d)
            {
                _signalPreviewVisible = false;
            }
        }

        RefreshSignalDecayTelemetry();

        var coherence = GetSignalDecayWaveformCoherence();
        if (AreAllSignalDecayComponentsAligned())
        {
            _signalHoldProgressSeconds = Math.Min(_signalHoldRequiredSeconds, _signalHoldProgressSeconds + context.DeltaTimeSeconds);
            if (_signalHoldProgressSeconds >= _signalHoldRequiredSeconds)
            {
                _phase = PuzzlePhase.Commit;
                _statusText = $"Signal lock ready. Stabilize at {Math.Round(coherence * 100d):0}% coherence.";
                StatusText = _statusText;
            }
        }
        else
        {
            var hadMeaningfulHold = _signalHoldProgressSeconds >= (_signalHoldRequiredSeconds * 0.45d);
            _signalHoldProgressSeconds = 0d;
            if (hadMeaningfulHold)
            {
                ApplyFailureState("stability_loss", "Stability Loss. The lock window slipped before stabilization.");
                _statusText = $"{_failureLabel}: {GetFailureVisualCue(_failureCode)}";
                StatusText = _statusText;
            }
        }
    }

    private IReadOnlyList<SoloPanelHudItem> BuildSignalDecayHud()
    {
        var coherence = GetSignalDecayWaveformCoherence();
        var thresholdPercent = Math.Round(_signalThreshold * 100d);
        return
        [
            new SoloPanelHudItem("Coherence", $"{Math.Round(coherence * 100d):0}%", coherence >= _signalThreshold ? "success" : coherence >= _signalThreshold - 0.04d ? "warning" : "shared"),
            new SoloPanelHudItem("Threshold", $"{thresholdPercent:0}%", "info"),
            new SoloPanelHudItem("Hold", $"{_signalHoldProgressSeconds:0.0}/{_signalHoldRequiredSeconds:0.0}s", _signalHoldProgressSeconds >= _signalHoldRequiredSeconds ? "success" : "warning"),
            new SoloPanelHudItem("Timer", $"{Math.Max(0, Math.Ceiling(_timerRemaining)):0}s", _timerRemaining <= 12d ? "danger" : "shared"),
            new SoloPanelHudItem("Hints", _hintsRemaining.ToString(CultureInfo.InvariantCulture), "info"),
        ];
    }

    private IReadOnlyList<SoloPanelActionItem> BuildSignalDecayActions(bool enabled)
    {
        return
        [
            new SoloPanelActionItem("signal:commit", "Stabilize Signal", "commit", enabled),
            new SoloPanelActionItem("signal:reset", "Recalibrate System", "reset", enabled),
            new SoloPanelActionItem("signal:hint", "Scan Diagnostic", "hint", enabled && _hintsRemaining > 0),
        ];
    }

    private IReadOnlyDictionary<string, string> BuildSignalDecayBoardSnapshot(Dictionary<string, string> board)
    {
        RefreshSignalDecayTelemetry();

        board["signal:component_count"] = _signalComponents.Count.ToString(CultureInfo.InvariantCulture);
        board["signal:threshold"] = _signalThreshold.ToString("0.000", CultureInfo.InvariantCulture);
        board["signal:hold_required"] = _signalHoldRequiredSeconds.ToString("0.000", CultureInfo.InvariantCulture);
        board["signal:hold_progress"] = _signalHoldProgressSeconds.ToString("0.000", CultureInfo.InvariantCulture);
        board["signal:coherence"] = GetSignalDecayWaveformCoherence().ToString("0.000", CultureInfo.InvariantCulture);
        board["signal:preview_visible"] = _signalPreviewVisible ? "1" : "0";
        board["signal:fft_visible"] = _signalShowFft ? "1" : "0";
        board["signal:wave_labels_visible"] = _signalShowWaveLabels ? "1" : "0";
        board["signal:path_target"] = _signalTargetPath;
        board["signal:path_current"] = _signalCurrentPath;
        board["signal:fft_bins"] = _signalFftBins;
        board["signal:phase_step"] = _signalPhaseStepDegrees.ToString(CultureInfo.InvariantCulture);
        board["signal:ready"] = IsSignalDecayReadyToCommit() ? "1" : "0";
        board["signal:scope_width"] = SignalDecayScopeWidth.ToString(CultureInfo.InvariantCulture);
        board["signal:scope_height"] = SignalDecayScopeHeight.ToString(CultureInfo.InvariantCulture);
        board["signal:timer_total"] = _timerSeconds.ToString("0.000", CultureInfo.InvariantCulture);
        board["signal:preview_seconds"] = _signalPreviewSeconds.ToString("0.0", CultureInfo.InvariantCulture);
        board["signal:preview_remaining"] = Math.Max(0d, _signalPreviewRemainingSeconds).ToString("0.000", CultureInfo.InvariantCulture);
        board["signal:level_title"] = $"LEVEL 0{_stageLevel}";
        board["signal:level_desc"] = _signalLevelDescription;
        board["signal:ghost_label"] = _signalPreviewVisible ? "Ghost Signal Visible" : "Ghost Signal Faded";
        board["signal:match_percent"] = Math.Round(GetSignalDecayWaveformCoherence() * 100d).ToString("0", CultureInfo.InvariantCulture);
        board["signal:noise_level"] = _signalNoiseStrength.ToString("0.000", CultureInfo.InvariantCulture);
        board["signal:cross_bleed_active"] = _signalCrossChannelBleed ? "1" : "0";

        foreach (var component in _signalComponents)
        {
            var key = component.Key;
            board[$"signal:{key}:type"] = FormatSignalWaveType(component.CurrentWaveType);
            board[$"signal:{key}:amp"] = component.CurrentAmplitude.ToString("0.00", CultureInfo.InvariantCulture);
            board[$"signal:{key}:freq"] = component.CurrentFrequency.ToString(CultureInfo.InvariantCulture);
            board[$"signal:{key}:phase"] = component.CurrentPhaseDegrees.ToString(CultureInfo.InvariantCulture);
            board[$"signal:{key}:delta"] = GetSignalDecayComponentDelta(component).ToString("0.000", CultureInfo.InvariantCulture);
            board[$"signal:{key}:aligned"] = IsSignalDecayComponentAligned(component) ? "1" : "0";

            board[$"tgt:{key}:type"] = FormatSignalWaveType(component.TargetWaveType);
            board[$"tgt:{key}:amp"] = component.TargetAmplitude.ToString("0.00", CultureInfo.InvariantCulture);
            board[$"tgt:{key}:freq"] = component.TargetFrequency.ToString(CultureInfo.InvariantCulture);
            board[$"tgt:{key}:phase"] = component.TargetPhaseDegrees.ToString(CultureInfo.InvariantCulture);
        }

        return board;
    }

    private bool HandleSignalDecayAction(string command, double nowSeconds)
    {
        if (string.Equals(command, "signal:hint", StringComparison.OrdinalIgnoreCase))
        {
            if (_hintsRemaining <= 0)
            {
                _statusText = "No diagnostics remaining.";
                StatusText = _statusText;
                return true;
            }

            _hintsRemaining--;
            _hintText = BuildSignalDecayHintText();
            _statusText = _hintText;
            StatusText = _statusText;
            return true;
        }

        if (string.Equals(command, "signal:reset", StringComparison.OrdinalIgnoreCase))
        {
            ResetSignalDecayStage(resetTimer: true, clearFailure: true, restorePreview: true);
            _statusText = "Signal matrix recalibrated to baseline.";
            StatusText = _statusText;
            return true;
        }

        if (string.Equals(command, "signal:commit", StringComparison.OrdinalIgnoreCase))
        {
            ResolveSignalDecayCommit();
            return true;
        }

        if (!command.StartsWith("signal:set:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = command.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 5)
        {
            return false;
        }

        var key = parts[2];
        var field = parts[3];
        var rawValue = parts[4];
        if (!_signalComponentLookup.TryGetValue(key, out var component))
        {
            return false;
        }

        switch (field)
        {
            case "type":
                if (!TryParseSignalWaveType(rawValue, out var waveType))
                {
                    return false;
                }
                component.CurrentWaveType = waveType;
                break;
            case "amp":
                if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var amplitude))
                {
                    return false;
                }
                component.CurrentAmplitude = QuantizeSignalAmplitude(amplitude);
                break;
            case "freq":
                if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var frequency))
                {
                    return false;
                }
                component.CurrentFrequency = Math.Clamp(frequency, 1, 8);
                break;
            case "phase":
                if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var phase))
                {
                    return false;
                }
                component.CurrentPhaseDegrees = NormalizeSignalPhase(phase);
                break;
            default:
                return false;
        }

        ApplySignalCrossChannelBleed(component, field);
        _phase = PuzzlePhase.Configure;
        ClearSignalDecayFailure();
        RefreshSignalDecayTelemetry();
        _statusText = BuildSignalDecayStatusText();
        StatusText = _statusText;
        return true;
    }

    private bool TryBuildSignalDecaySolveTrace(out PuzzleSolveTrace trace)
    {
        var steps = new List<PuzzleSolveStep>(_signalComponents.Count * 4 + 4);
        foreach (var component in _signalComponents)
        {
            if (component.CurrentWaveType != component.TargetWaveType)
            {
                steps.Add(new PuzzleSolveStep($"signal:set:{component.Key}:type:{FormatSignalWaveType(component.TargetWaveType)}:{FormatSignalWaveType(component.CurrentWaveType)}"));
            }

            if (Math.Abs(component.CurrentAmplitude - component.TargetAmplitude) > 0.001d)
            {
                steps.Add(new PuzzleSolveStep($"signal:set:{component.Key}:amp:{component.TargetAmplitude.ToString("0.00", CultureInfo.InvariantCulture)}:{component.CurrentAmplitude.ToString("0.00", CultureInfo.InvariantCulture)}"));
            }

            if (component.CurrentFrequency != component.TargetFrequency)
            {
                steps.Add(new PuzzleSolveStep($"signal:set:{component.Key}:freq:{component.TargetFrequency.ToString(CultureInfo.InvariantCulture)}:{component.CurrentFrequency.ToString(CultureInfo.InvariantCulture)}"));
            }

            if (component.CurrentPhaseDegrees != component.TargetPhaseDegrees)
            {
                steps.Add(new PuzzleSolveStep($"signal:set:{component.Key}:phase:{component.TargetPhaseDegrees.ToString(CultureInfo.InvariantCulture)}:{component.CurrentPhaseDegrees.ToString(CultureInfo.InvariantCulture)}"));
            }
        }

        var remainingHold = Math.Max(0.1d, _signalHoldRequiredSeconds - _signalHoldProgressSeconds + 0.1d);
        steps.Add(new PuzzleSolveStep(null, remainingHold));
        steps.Add(new PuzzleSolveStep("signal:commit"));

        trace = new PuzzleSolveTrace(steps, "Signal decay waveform reconstruction trace.");
        return steps.Count > 0;
    }

    private void ResolveSignalDecayCommit()
    {
        RefreshSignalDecayTelemetry();
        if (!IsSignalDecayReadyToCommit())
        {
            var worstChannel = GetSignalDecayWorstChannelKey();
            var worstParameter = GetSignalDecayWorstParameterLabel(worstChannel);
            RegisterFailure("phase_drift", $"Phase Drift. {worstChannel.ToUpperInvariant()} {worstParameter} is outside lock tolerance.");
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
    private void RestartSignalDecayAfterTimeout()
    {
        ResetSignalDecayStage(resetTimer: true, clearFailure: false, restorePreview: true);
        ApplyFailureState("sync_collapse", "Sync Collapse. Time window collapsed before stabilization.");
        _status = PuzzleStatus.Active;
        _phase = PuzzlePhase.Configure;
        _statusText = $"{_failureLabel}: {_recoveryText}";
        StatusText = _statusText;
    }

    private void ResetSignalDecayStage(bool resetTimer, bool clearFailure, bool restorePreview)
    {
        foreach (var component in _signalComponents)
        {
            component.CurrentWaveType = component.InitialWaveType;
            component.CurrentAmplitude = component.InitialAmplitude;
            component.CurrentFrequency = component.InitialFrequency;
            component.CurrentPhaseDegrees = component.InitialPhaseDegrees;
        }

        _signalHoldProgressSeconds = 0d;
        if (resetTimer)
        {
            _timerRemaining = _timerSeconds;
        }

        if (restorePreview)
        {
            _signalPreviewRemainingSeconds = _signalPreviewSeconds;
            _signalPreviewVisible = _signalPreviewSeconds <= 0d || _signalPreviewRemainingSeconds > 0d;
        }

        if (clearFailure)
        {
            ClearSignalDecayFailure();
        }

        RefreshSignalDecayTelemetry();
    }

    private void ApplySignalDecayFallbackTemplate()
    {
        ConfigureSignalDecayFamily(simplifiedFallback: true);
        _statusText = "Fallback diagnostics engaged. Reconstruct the waveform and stabilize the lock window.";
        StatusText = _statusText;
    }

    private double GetSignalDecayWaveformCoherence()
    {
        RefreshSignalDecayTelemetry();
        if (_signalTargetSamples.Length == 0 || _signalCurrentSamples.Length == 0)
        {
            return 0d;
        }

        var totalDelta = 0d;
        for (var index = 0; index < _signalTargetSamples.Length; index++)
        {
            totalDelta += Math.Abs(_signalTargetSamples[index] - _signalCurrentSamples[index]);
        }

        var meanAbsoluteError = totalDelta / _signalTargetSamples.Length;
        return Math.Clamp(1d - (meanAbsoluteError / 2d), 0d, 1d);
    }

    private string GetSignalDecayWorstChannelKey()
    {
        var component = _signalComponents
            .OrderBy(component => GetSignalDecayComponentDelta(component))
            .FirstOrDefault();
        return component?.Key ?? "k1";
    }

    private string GetSignalDecayWorstParameterLabel(string key)
    {
        if (!_signalComponentLookup.TryGetValue(key, out var component))
        {
            return "alignment";
        }

        var candidates = new List<(string label, double delta)>
        {
            ("wave type", component.CurrentWaveType == component.TargetWaveType ? 1d : 0d),
            ("amplitude", 1d - Math.Min(1d, Math.Abs(component.CurrentAmplitude - component.TargetAmplitude) / 0.8d)),
            ("frequency", 1d - (Math.Abs(component.CurrentFrequency - component.TargetFrequency) / 7d)),
            ("phase", 1d - (GetCircularPhaseDistance(component.CurrentPhaseDegrees, component.TargetPhaseDegrees) / 180d)),
        };

        return candidates.OrderBy(candidate => candidate.delta).First().label;
    }

    private string BuildSignalDecayHintText()
    {
        var key = GetSignalDecayWorstChannelKey();
        if (!_signalComponentLookup.TryGetValue(key, out var component))
        {
            return "Diagnostic unavailable.";
        }

        var parameter = GetSignalDecayWorstParameterLabel(key);
        return parameter switch
        {
            "wave type" => $"Diagnostic: {key.ToUpperInvariant()} should use a {FormatSignalWaveType(component.TargetWaveType)} profile.",
            "amplitude" => $"Diagnostic: {key.ToUpperInvariant()} amplitude should trend toward {component.TargetAmplitude:0.00}.",
            "frequency" => $"Diagnostic: {key.ToUpperInvariant()} frequency should trend toward {component.TargetFrequency}Hz.",
            "phase" => $"Diagnostic: {key.ToUpperInvariant()} phase should trend toward {component.TargetPhaseDegrees}°.",
            _ => $"Diagnostic: {key.ToUpperInvariant()} remains the weakest channel.",
        };
    }

    private bool IsSignalDecayReadyToCommit() =>
        AreAllSignalDecayComponentsAligned() &&
        _signalHoldProgressSeconds >= _signalHoldRequiredSeconds;

    private bool AreAllSignalDecayComponentsAligned() =>
        _signalComponents.Count > 0 &&
        _signalComponents.All(IsSignalDecayComponentAligned);

    private void ClearSignalDecayFailure()
    {
        _failureCode = string.Empty;
        _failureLabel = string.Empty;
        _recoveryText = string.Empty;
    }

    private string BuildSignalDecayStatusText()
    {
        var coherence = Math.Round(GetSignalDecayWaveformCoherence() * 100d);
        if (IsSignalDecayReadyToCommit())
        {
            return $"Signal lock ready. Stabilize at {coherence:0}% coherence.";
        }

        if (GetSignalDecayWaveformCoherence() >= _signalThreshold)
        {
            return $"Coherence is high, but the exact waveform lock is incomplete at {coherence:0}%.";
        }

        return $"Signal Coherence: {coherence:0}% — continue tuning the composite waveform.";
    }

    private void ApplySignalCrossChannelBleed(SignalDecayComponentState component, string field)
    {
        if (!_signalCrossChannelBleed)
        {
            return;
        }

        var index = _signalComponents.IndexOf(component);
        if (index < 0 || index >= _signalComponents.Count - 1)
        {
            return;
        }

        var neighbor = _signalComponents[index + 1];
        switch (field)
        {
            case "amp":
                neighbor.CurrentAmplitude = QuantizeSignalAmplitude(neighbor.CurrentAmplitude + 0.05d);
                break;
            case "freq":
                neighbor.CurrentFrequency = Math.Clamp(neighbor.CurrentFrequency + 1, 1, 8);
                break;
            case "phase":
                neighbor.CurrentPhaseDegrees = NormalizeSignalPhase(neighbor.CurrentPhaseDegrees + _signalPhaseStepDegrees);
                break;
        }
    }

    private void RefreshSignalDecayTelemetry()
    {
        if (_signalComponents.Count == 0)
        {
            _signalTargetSamples = [];
            _signalCurrentSamples = [];
            _signalTargetPath = string.Empty;
            _signalCurrentPath = string.Empty;
            _signalFftBins = string.Empty;
            return;
        }

        _signalTargetSamples = BuildSignalDecaySamples(target: true);
        _signalCurrentSamples = BuildSignalDecaySamples(target: false);
        _signalTargetPath = BuildSignalDecayPath(_signalTargetSamples);
        _signalCurrentPath = BuildSignalDecayPath(ApplySignalDisplayNoise(_signalCurrentSamples));
        _signalFftBins = BuildSignalDecayFftBins(_signalTargetSamples);
    }

    private double[] BuildSignalDecaySamples(bool target)
    {
        var samples = new double[SignalDecaySampleCount];
        for (var index = 0; index < SignalDecaySampleCount; index++)
        {
            var t = index / (double)(SignalDecaySampleCount - 1);
            var value = 0d;
            foreach (var component in _signalComponents)
            {
                var waveType = target ? component.TargetWaveType : component.CurrentWaveType;
                var amplitude = target ? component.TargetAmplitude : component.CurrentAmplitude;
                var frequency = target ? component.TargetFrequency : component.CurrentFrequency;
                var phaseDegrees = target ? component.TargetPhaseDegrees : component.CurrentPhaseDegrees;
                value += amplitude * SampleSignalWave(waveType, t, frequency, phaseDegrees);
            }

            samples[index] = Math.Clamp(value / Math.Max(1, _signalComponents.Count), -1d, 1d);
        }

        return samples;
    }

    private string BuildSignalDecayPath(IReadOnlyList<double> samples)
    {
        if (samples.Count == 0)
        {
            return string.Empty;
        }

        const double leftPadding = 14d;
        const double topPadding = 16d;
        var width = SignalDecayScopeWidth - (leftPadding * 2d);
        var height = SignalDecayScopeHeight - (topPadding * 2d);
        var mid = topPadding + (height / 2d);
        var builder = new StringBuilder(samples.Count * 18);
        for (var index = 0; index < samples.Count; index++)
        {
            var x = leftPadding + ((width * index) / Math.Max(1, samples.Count - 1));
            var y = mid - (samples[index] * (height * 0.42d));
            builder.Append(index == 0 ? "M" : " L")
                .Append(x.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(' ')
                .Append(y.ToString("0.###", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private IReadOnlyList<double> ApplySignalDisplayNoise(IReadOnlyList<double> samples)
    {
        if (_signalNoiseStrength <= 0d)
        {
            return samples;
        }

        var noisy = new double[samples.Count];
        for (var index = 0; index < samples.Count; index++)
        {
            var wave = Math.Sin((index * 0.73d) + (_stageLevel * 0.6d));
            noisy[index] = Math.Clamp(samples[index] + (wave * _signalNoiseStrength), -1d, 1d);
        }

        return noisy;
    }

    private string BuildSignalDecayFftBins(IReadOnlyList<double> samples)
    {
        if (!_signalShowFft || samples.Count == 0)
        {
            return string.Empty;
        }

        const int binCount = 8;
        var magnitudes = new double[binCount];
        for (var bin = 0; bin < binCount; bin++)
        {
            double real = 0d;
            double imaginary = 0d;
            for (var n = 0; n < samples.Count; n++)
            {
                var angle = (2d * Math.PI * (bin + 1) * n) / samples.Count;
                real += samples[n] * Math.Cos(angle);
                imaginary -= samples[n] * Math.Sin(angle);
            }

            magnitudes[bin] = Math.Sqrt((real * real) + (imaginary * imaginary)) / samples.Count;
        }

        var maxMagnitude = Math.Max(0.0001d, magnitudes.Max());
        return string.Join(',', magnitudes.Select(value => (value / maxMagnitude).ToString("0.000", CultureInfo.InvariantCulture)));
    }
    private double GetSignalDecayComponentDelta(SignalDecayComponentState component)
    {
        var typeScore = component.CurrentWaveType == component.TargetWaveType ? 1d : 0d;
        var amplitudeScore = 1d - Math.Min(1d, Math.Abs(component.CurrentAmplitude - component.TargetAmplitude) / 0.8d);
        var frequencyScore = 1d - (Math.Abs(component.CurrentFrequency - component.TargetFrequency) / 7d);
        var phaseScore = 1d - (GetCircularPhaseDistance(component.CurrentPhaseDegrees, component.TargetPhaseDegrees) / 180d);
        return Math.Clamp((typeScore * 0.25d) + (amplitudeScore * 0.25d) + (frequencyScore * 0.30d) + (phaseScore * 0.20d), 0d, 1d);
    }

    private bool IsSignalDecayComponentAligned(SignalDecayComponentState component) =>
        component.CurrentWaveType == component.TargetWaveType &&
        Math.Abs(component.CurrentAmplitude - component.TargetAmplitude) <= 0.001d &&
        component.CurrentFrequency == component.TargetFrequency &&
        component.CurrentPhaseDegrees == component.TargetPhaseDegrees;

    private static double SampleSignalWave(SignalWaveType waveType, double t, int frequency, int phaseDegrees)
    {
        var phase = phaseDegrees * (Math.PI / 180d);
        var angle = (2d * Math.PI * frequency * t) + phase;
        return waveType switch
        {
            SignalWaveType.Sine => Math.Sin(angle),
            SignalWaveType.Square => Math.Sign(Math.Sin(angle)),
            SignalWaveType.Saw => (2d * ((((frequency * t) + (phase / (2d * Math.PI))) % 1d + 1d) % 1d)) - 1d,
            _ => 0d,
        };
    }

    private static bool TryParseSignalWaveType(string value, out SignalWaveType waveType)
    {
        switch ((value ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "sine":
                waveType = SignalWaveType.Sine;
                return true;
            case "square":
                waveType = SignalWaveType.Square;
                return true;
            case "saw":
                waveType = SignalWaveType.Saw;
                return true;
            default:
                waveType = SignalWaveType.Sine;
                return false;
        }
    }

    private static string FormatSignalWaveType(SignalWaveType waveType) => waveType switch
    {
        SignalWaveType.Sine => "sine",
        SignalWaveType.Square => "square",
        _ => "saw",
    };

    private int NormalizeSignalPhase(int degrees)
    {
        var normalized = ((degrees % 360) + 360) % 360;
        var step = Math.Max(5, _signalPhaseStepDegrees);
        var snapped = (int)Math.Round(normalized / (double)step, MidpointRounding.AwayFromZero) * step;
        return snapped % 360;
    }

    private static double QuantizeSignalAmplitude(double value)
    {
        var clamped = Math.Clamp(value, 0.10d, 0.90d);
        var snapped = Math.Round((clamped - 0.10d) / 0.05d, MidpointRounding.AwayFromZero) * 0.05d + 0.10d;
        return Math.Clamp(Math.Round(snapped, 2, MidpointRounding.AwayFromZero), 0.10d, 0.90d);
    }

    private static double GetCircularPhaseDistance(int currentPhase, int targetPhase)
    {
        var raw = Math.Abs(currentPhase - targetPhase) % 360;
        return Math.Min(raw, 360 - raw);
    }
}

