namespace Enigma.Client.Models.Gameplay;

using System.Globalization;

public sealed partial class SoloPanelBiblePuzzle
{
    private readonly record struct DifficultyDimensions(
        string InformationVisibility,
        string SystemComplexity,
        string ExecutionDifficulty,
        string TimePressure,
        string CognitiveLoad);

    private readonly record struct CoupledSystemPairWeight(int LeftIndex, int RightIndex, double Weight);

    private sealed record CoupledSystemMetricDefinition(
        string Id,
        string Label,
        double[] Weights,
        CoupledSystemPairWeight[] PairWeights,
        double BaseBand,
        string Unit = "");

    private readonly record struct MutatorDefinition(string Id, string Label, string Description);

    private readonly List<CoupledSystemMetricDefinition> _systemMetricDefinitions = [];
    private readonly Dictionary<string, string> _systemControlLabels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _systemControlTargetMin = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _systemControlTargetMax = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _systemMetricCurrentValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _systemMetricTargetValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _systemMetricBandMin = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _systemMetricBandMax = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _systemClues = [];
    private readonly Dictionary<string, double> _pipePressure = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _pipeGateThreshold = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<int> _memoryInitialLayout = [];
    private readonly HashSet<string> _gridVisibleTargets = new(StringComparer.OrdinalIgnoreCase);

    private string _systemCommandPrefix = string.Empty;
    private string _systemHeadline = string.Empty;
    private string _systemDescription = string.Empty;
    private string _systemTargetMode = "direct";
    private int _systemInputStep = 1;

    private int _gridRows;
    private int _gridCols;
    private int _gridPageSize;
    private bool _gridUseKernel;
    private bool _gridPageLink;
    private double _memoryRotateElapsedSeconds;

    private string _mutatorId = string.Empty;
    private string _mutatorLabel = string.Empty;
    private string _mutatorDescription = string.Empty;

    private void ResetDifficultyRebuildState()
    {
        _systemMetricDefinitions.Clear();
        _systemControlLabels.Clear();
        _systemControlTargetMin.Clear();
        _systemControlTargetMax.Clear();
        _systemMetricCurrentValues.Clear();
        _systemMetricTargetValues.Clear();
        _systemMetricBandMin.Clear();
        _systemMetricBandMax.Clear();
        _systemClues.Clear();
        _pipePressure.Clear();
        _pipeGateThreshold.Clear();
        _memoryInitialLayout.Clear();
        _gridVisibleTargets.Clear();

        _systemCommandPrefix = string.Empty;
        _systemHeadline = string.Empty;
        _systemDescription = string.Empty;
        _systemTargetMode = "direct";
        _systemInputStep = 1;

        _gridRows = 0;
        _gridCols = 0;
        _gridPageSize = 0;
        _gridUseKernel = false;
        _gridPageLink = false;
        _memoryRotateElapsedSeconds = 0d;

        _mutatorId = string.Empty;
        _mutatorLabel = string.Empty;
        _mutatorDescription = string.Empty;
    }

    private void AssignDeterministicHardMutator()
    {
        _mutatorId = string.Empty;
        _mutatorLabel = string.Empty;
        _mutatorDescription = string.Empty;

        if (_difficulty != MazeDifficulty.Hard)
        {
            return;
        }

        var roll = Math.Abs(PuzzleFactory.StableHash($"mutator|{_solutionSeed}|{FamilyId}|{TierLevel}")) % 10;
        if (roll >= 4)
        {
            return;
        }

        var definition = GetFamilyMutatorDefinition();
        _mutatorId = definition.Id;
        _mutatorLabel = definition.Label;
        _mutatorDescription = definition.Description;
    }

    private MutatorDefinition GetFamilyMutatorDefinition() => FamilyId switch
    {
        "chromatic_lock" => new MutatorDefinition("lighting_shift", "Lighting Shift", "Ambient wash alters colour perception while true values remain unchanged."),
        "signal_decay" => new MutatorDefinition("signal_noise", "Signal Noise", "Readouts carry deterministic interference and weaker visual certainty."),
        "dead_reckoning" or "gravity_well" or "echo_chamber" or "fault_line" => new MutatorDefinition("instrument_drift", "Instrument Drift", "Adjusting one control nudges the next control in sequence."),
        "pressure_grid" or "temporal_grid" or "token_flood" => new MutatorDefinition("power_instability", "Power Instability", "Telemetry flickers while the system remains deterministic underneath."),
        "memory_palace" => new MutatorDefinition("time_echo", "Time Echo", "Unmatched fragments continue to migrate through the archive."),
        _ => new MutatorDefinition(string.Empty, string.Empty, string.Empty),
    };

    private IReadOnlyList<SoloPanelHudItem> WithMutatorHud(IReadOnlyList<SoloPanelHudItem> items)
    {
        if (string.IsNullOrWhiteSpace(_mutatorId))
        {
            return items;
        }

        var list = items.ToList();
        list.Add(new SoloPanelHudItem("Mutator", _mutatorLabel, "warning"));
        return list;
    }

    private void AppendMutatorBoardMetadata(Dictionary<string, string> board)
    {
        board["mutator:active"] = string.IsNullOrWhiteSpace(_mutatorId) ? "0" : "1";
        board["mutator:id"] = _mutatorId;
        board["mutator:label"] = _mutatorLabel;
        board["mutator:desc"] = _mutatorDescription;
    }

    private DifficultyDimensions GetDifficultyDimensions() => _difficulty switch
    {
        MazeDifficulty.Easy => new DifficultyDimensions("low", "low", "low", "low", "low"),
        MazeDifficulty.Medium => new DifficultyDimensions("moderate", "moderate", "moderate", "moderate", "moderate"),
        MazeDifficulty.Hard => new DifficultyDimensions("high", "high", "high", "high", "high"),
        _ => new DifficultyDimensions("moderate", "moderate", "moderate", "moderate", "moderate"),
    };

    private string GetVisualTargetVisibilityKey()
    {
        if (_mechanic == SoloPanelMechanic.CoupledSystem)
        {
            return _systemTargetMode switch
            {
                "band" => "partial",
                "aggregate" => "concealed",
                _ => "visible",
            };
        }

        if (_mechanic == SoloPanelMechanic.CellCommit && _gridVisibleTargets.Count > 0 && _gridVisibleTargets.Count < _orderedKeys.Count)
        {
            return "partial";
        }

        return _showTarget ? "visible" : "concealed";
    }

    private void ConfigureDeadReckoningFamily()
    {
        var controlCount = _difficulty switch
        {
            MazeDifficulty.Easy => 5,
            MazeDifficulty.Medium => _stageLevel >= 3 ? 7 : 6,
            MazeDifficulty.Hard => 8,
            _ => 6,
        };

        ConfigureCoupledSystemFamily(
            commandPrefix: "reckon",
            headline: "Navigation Solution",
            description: _difficulty switch
            {
                MazeDifficulty.Easy => "Route vectors are visible. Bring the live plot into the destination corridor.",
                MazeDifficulty.Medium => "Some vector targets are hidden. Use route metrics and partial control bands together.",
                MazeDifficulty.Hard => "Per-control targets are gone. Deduce the route from aggregate navigation metrics and clue cards.",
                _ => "Solve the navigation stack."
            },
            controlLabels: DeadReckoningLabels,
            controlCount: controlCount,
            inputStep: _difficulty == MazeDifficulty.Easy ? 5 : _difficulty == MazeDifficulty.Medium ? 2 : 1,
            timerSeconds: _difficulty == MazeDifficulty.Easy ? 80d : _difficulty == MazeDifficulty.Medium ? 120d : 210d,
            attempts: _difficulty == MazeDifficulty.Hard ? 3 : 4,
            metrics:
            [
                new CoupledSystemMetricDefinition("destination_error", "Destination Error", [1.6, 0.7, 1.1, 0.4, 1.2, 0.6, 0.8, 0.3], [new CoupledSystemPairWeight(0, 4, 0.8), new CoupledSystemPairWeight(1, 2, 0.6)], 5d),
                new CoupledSystemMetricDefinition("total_distance", "Total Distance", [0.5, 1.4, 1.2, 0.7, 0.6, 0.9, 0.4, 0.3], [new CoupledSystemPairWeight(2, 4, 0.7)], 5d),
                new CoupledSystemMetricDefinition("average_bearing", "Average Bearing", [1.1, 0.4, 0.5, 0.3, 1.3, 0.7, 0.6, 0.8], [new CoupledSystemPairWeight(0, 7, 0.9)], 5d, "deg"),
                new CoupledSystemMetricDefinition("fuel_burn", "Fuel Burn", [0.6, 0.9, 0.8, 1.2, 0.5, 1.1, 0.7, 0.4], [new CoupledSystemPairWeight(3, 5, 0.8)], 5d),
                new CoupledSystemMetricDefinition("drift", "Drift", [0.7, 1.0, 0.5, 0.9, 0.8, 0.4, 1.2, 0.6], [new CoupledSystemPairWeight(1, 6, 0.8)], 5d),
            ]);
    }

    private void ConfigureGravityWellFamily()
    {
        var controlCount = _difficulty switch
        {
            MazeDifficulty.Easy => 3,
            MazeDifficulty.Medium => 4,
            MazeDifficulty.Hard => 5,
            _ => 4,
        };

        ConfigureCoupledSystemFamily(
            commandPrefix: "gravity",
            headline: "Orbital Equilibrium",
            description: _difficulty switch
            {
                MazeDifficulty.Easy => "Independent wells feed a visible receiver cone. Tune each orbit directly.",
                MazeDifficulty.Medium => "Nearby wells influence the path. Use lock and stability bands together.",
                MazeDifficulty.Hard => "The full orbital system is coupled. Solve to the receiver lock, stability, and escape-energy bands.",
                _ => "Solve the orbital stack."
            },
            controlLabels: GravityWellLabels,
            controlCount: controlCount,
            inputStep: _difficulty == MazeDifficulty.Easy ? 5 : _difficulty == MazeDifficulty.Medium ? 2 : 1,
            timerSeconds: _difficulty == MazeDifficulty.Easy ? 95d : _difficulty == MazeDifficulty.Medium ? 145d : 225d,
            attempts: _difficulty == MazeDifficulty.Hard ? 3 : 4,
            metrics:
            [
                new CoupledSystemMetricDefinition("receiver_lock", "Receiver Lock", [1.5, 0.9, 1.2, 0.7, 0.8, 0.0, 0.0, 0.0], [new CoupledSystemPairWeight(0, 2, 0.8)], 4d),
                new CoupledSystemMetricDefinition("orbital_stability", "Orbital Stability", [0.8, 1.3, 0.9, 1.1, 0.7, 0.0, 0.0, 0.0], [new CoupledSystemPairWeight(1, 3, 0.9)], 4d),
                new CoupledSystemMetricDefinition("escape_energy", "Escape Energy", [1.0, 0.6, 0.8, 1.4, 1.0, 0.0, 0.0, 0.0], [new CoupledSystemPairWeight(2, 4, 0.7)], 4d),
                new CoupledSystemMetricDefinition("lens_focus", "Lens Focus", [0.6, 1.1, 0.7, 0.9, 1.3, 0.0, 0.0, 0.0], [new CoupledSystemPairWeight(0, 4, 0.8)], 4d),
            ]);
    }

    private void ConfigureEchoChamberFamily()
    {
        var controlCount = _difficulty switch
        {
            MazeDifficulty.Easy => 3,
            MazeDifficulty.Medium => 4,
            MazeDifficulty.Hard => 5,
            _ => 4,
        };

        ConfigureCoupledSystemFamily(
            commandPrefix: "echo",
            headline: "Interference Solver",
            description: _difficulty switch
            {
                MazeDifficulty.Easy => "Direct reflector paths are visible. Bring the return into the receiver window.",
                MazeDifficulty.Medium => "Reflections combine across banks. Tune for gain and reduced echo together.",
                MazeDifficulty.Hard => "Phase interference dominates. Minimize residual echo while holding gain and phase error inside band.",
                _ => "Solve the chamber."
            },
            controlLabels: EchoChamberLabels,
            controlCount: controlCount,
            inputStep: _difficulty == MazeDifficulty.Easy ? 5 : _difficulty == MazeDifficulty.Medium ? 2 : 1,
            timerSeconds: _difficulty == MazeDifficulty.Easy ? 80d : _difficulty == MazeDifficulty.Medium ? 125d : 200d,
            attempts: _difficulty == MazeDifficulty.Hard ? 3 : 4,
            metrics:
            [
                new CoupledSystemMetricDefinition("receiver_gain", "Receiver Gain", [1.3, 0.8, 1.0, 0.6, 0.9, 0.0, 0.0, 0.0], [new CoupledSystemPairWeight(0, 1, 0.7)], 4d),
                new CoupledSystemMetricDefinition("residual_echo", "Residual Echo", [0.7, 1.4, 0.6, 1.1, 0.9, 0.0, 0.0, 0.0], [new CoupledSystemPairWeight(1, 3, 0.8)], 4d),
                new CoupledSystemMetricDefinition("phase_error", "Phase Error", [1.0, 0.6, 1.2, 0.9, 0.8, 0.0, 0.0, 0.0], [new CoupledSystemPairWeight(2, 4, 0.9)], 4d),
                new CoupledSystemMetricDefinition("bounce_density", "Bounce Density", [0.8, 1.0, 0.7, 1.2, 0.6, 0.0, 0.0, 0.0], [new CoupledSystemPairWeight(0, 4, 0.7)], 4d),
            ]);
    }

    private void ConfigureFaultLineFamily()
    {
        var controlCount = _difficulty switch
        {
            MazeDifficulty.Easy => 4,
            MazeDifficulty.Medium => 5,
            MazeDifficulty.Hard => 6,
            _ => 5,
        };

        ConfigureCoupledSystemFamily(
            commandPrefix: "fault",
            headline: "Geological Balance",
            description: _difficulty switch
            {
                MazeDifficulty.Easy => "Visible seam targets reveal the stable strata alignment directly.",
                MazeDifficulty.Medium => "Controls align into safe ranges rather than exact marks.",
                MazeDifficulty.Hard => "Only pressure, tension, and balance bands are surfaced. Tune the fault stack to equilibrium.",
                _ => "Solve the fault stack."
            },
            controlLabels: FaultLineLabels,
            controlCount: controlCount,
            inputStep: _difficulty == MazeDifficulty.Easy ? 5 : _difficulty == MazeDifficulty.Medium ? 2 : 1,
            timerSeconds: _difficulty == MazeDifficulty.Easy ? 90d : _difficulty == MazeDifficulty.Medium ? 135d : 210d,
            attempts: _difficulty == MazeDifficulty.Hard ? 3 : 4,
            metrics:
            [
                new CoupledSystemMetricDefinition("seam_alignment", "Seam Alignment", [1.3, 1.1, 0.9, 0.6, 0.8, 0.7, 0.0, 0.0], [new CoupledSystemPairWeight(0, 2, 0.8)], 4d),
                new CoupledSystemMetricDefinition("strata_pressure", "Strata Pressure", [0.8, 1.4, 0.6, 1.2, 0.7, 0.9, 0.0, 0.0], [new CoupledSystemPairWeight(1, 4, 0.7)], 4d),
                new CoupledSystemMetricDefinition("fault_tension", "Fault Tension", [1.0, 0.7, 1.2, 0.8, 1.1, 0.6, 0.0, 0.0], [new CoupledSystemPairWeight(2, 5, 0.9)], 4d),
                new CoupledSystemMetricDefinition("seismic_balance", "Seismic Balance", [0.9, 0.8, 0.7, 1.1, 1.0, 1.2, 0.0, 0.0], [new CoupledSystemPairWeight(0, 5, 0.8)], 4d),
            ]);
    }

    private void ConfigureCoupledSystemFamily(
        string commandPrefix,
        string headline,
        string description,
        string[] controlLabels,
        int controlCount,
        int inputStep,
        double timerSeconds,
        int attempts,
        IReadOnlyList<CoupledSystemMetricDefinition> metrics)
    {
        _mechanic = SoloPanelMechanic.CoupledSystem;
        _stagesRequired = 1;
        _timerSeconds = timerSeconds;
        _attemptsRemaining = attempts;
        _systemCommandPrefix = commandPrefix;
        _systemHeadline = headline;
        _systemDescription = description;
        _systemInputStep = Math.Max(1, inputStep);
        _systemTargetMode = _difficulty switch
        {
            MazeDifficulty.Easy => "direct",
            MazeDifficulty.Medium => "band",
            MazeDifficulty.Hard => "aggregate",
            _ => "direct",
        };
        _showTarget = _difficulty != MazeDifficulty.Hard;

        foreach (var metric in metrics)
        {
            _systemMetricDefinitions.Add(metric);
        }

        for (var index = 0; index < controlCount; index++)
        {
            var key = $"k{index + 1}";
            var target = QuantizeSystemValue(
                18 + (Math.Abs(PuzzleFactory.StableHash($"{_solutionSeed}|system|target|{FamilyId}|{index}|{TierLevel}")) % 65),
                _systemInputStep);
            var current = QuantizeSystemValue(
                14 + (Math.Abs(PuzzleFactory.StableHash($"{_layoutSeed}|system|current|{FamilyId}|{index}|{TierLevel}")) % 73),
                _systemInputStep);
            if (Math.Abs(current - target) <= Math.Max(4, _systemInputStep * 2))
            {
                current = QuantizeSystemValue((target + 27 + (index * 9)) % 101, _systemInputStep);
            }

            _orderedKeys.Add(key);
            _max[key] = 100;
            _target[key] = target;
            _current[key] = current;
            _initial[key] = current;
            _systemControlLabels[key] = controlLabels[index % controlLabels.Length];

            if (_difficulty == MazeDifficulty.Easy)
            {
                _systemControlTargetMin[key] = target;
                _systemControlTargetMax[key] = target;
            }
            else if (_difficulty == MazeDifficulty.Medium && index < Math.Max(2, controlCount / 2))
            {
                _systemControlTargetMin[key] = Math.Max(0, QuantizeSystemValue(target - 6, _systemInputStep));
                _systemControlTargetMax[key] = Math.Min(100, QuantizeSystemValue(target + 6, _systemInputStep));
            }
        }

        RefreshCoupledSystemTelemetry();
        BuildCoupledSystemClues();
        _statusText = BuildCoupledSystemStatusText();
        StatusText = _statusText;
    }

    private void ConfigurePressureGridFamily()
    {
        _progressLabel = "Lattice Alignment / Pressure Balance";
        _gridRows = _difficulty switch
        {
            MazeDifficulty.Easy => 3,
            _ => 4,
        };
        _gridCols = _gridRows;
        _gridPageSize = _gridRows * _gridCols;
        _gridUseKernel = _difficulty == MazeDifficulty.Hard;
        _gridPageLink = false;

        ConfigureCellFamily(
            keyCount: _gridRows * _gridCols,
            maxValue: 1,
            stages: 1,
            timerSeconds: _difficulty == MazeDifficulty.Easy ? 80d : _difficulty == MazeDifficulty.Medium ? 110d : 190d,
            attempts: _difficulty == MazeDifficulty.Hard ? 3 : 4);
        BuildGridVisibleTargets();
        _statusText = BuildGridStatusText(CountAlignedCells());
        StatusText = _statusText;
    }

    private void ConfigureTemporalGridFamily()
    {
        _progressLabel = "Phase Alignment / Temporal Stability";
        _gridRows = 3;
        _gridCols = 3;
        _gridPageSize = _gridRows * _gridCols;
        _gridUseKernel = false;
        _gridPageLink = _difficulty == MazeDifficulty.Hard;

        var pages = _difficulty == MazeDifficulty.Easy ? 1 : 2;
        ConfigureCellFamily(
            keyCount: _gridPageSize * pages,
            maxValue: 1,
            stages: 1,
            timerSeconds: _difficulty == MazeDifficulty.Easy ? 85d : _difficulty == MazeDifficulty.Medium ? 135d : 220d,
            attempts: _difficulty == MazeDifficulty.Hard ? 3 : 4);
        BuildGridVisibleTargets();
        _showTarget = _difficulty == MazeDifficulty.Easy;
        _statusText = BuildGridStatusText(CountAlignedCells());
        StatusText = _statusText;
    }

    private void BuildGridVisibleTargets()
    {
        _gridVisibleTargets.Clear();
        if (_difficulty == MazeDifficulty.Easy)
        {
            foreach (var key in _orderedKeys)
            {
                _gridVisibleTargets.Add(key);
            }

            _showTarget = true;
            return;
        }

        if (_difficulty == MazeDifficulty.Hard)
        {
            _showTarget = false;
            return;
        }

        var visibleCount = Math.Max(2, _orderedKeys.Count / (FamilyId == "temporal_grid" ? 3 : 2));
        foreach (var key in _orderedKeys
                     .OrderBy(key => Math.Abs(PuzzleFactory.StableHash($"{_layoutSeed}|grid-visible|{FamilyId}|{key}")))
                     .Take(visibleCount))
        {
            _gridVisibleTargets.Add(key);
        }

        _showTarget = _gridVisibleTargets.Count > 0;
    }

    private bool TryHandleAdvancedCellAction(string key)
    {
        if (_mechanic != SoloPanelMechanic.CellCommit)
        {
            return false;
        }

        if (_gridUseKernel)
        {
            ToggleCellByKey(key);
            var (_, pageIndex, localIndex) = GetCellAddress(key);
            var row = localIndex / Math.Max(1, _gridCols);
            var col = localIndex % Math.Max(1, _gridCols);
            if (row > 0)
            {
                ToggleCellByOrdinal((pageIndex * GetCellPageSize()) + localIndex - _gridCols);
            }

            if (col > 0)
            {
                ToggleCellByOrdinal((pageIndex * GetCellPageSize()) + localIndex - 1);
            }

            return true;
        }

        if (_gridPageLink)
        {
            ToggleCellByKey(key);
            var (_, pageIndex, localIndex) = GetCellAddress(key);
            if (pageIndex == 1)
            {
                ToggleCellByOrdinal(localIndex);
            }

            return true;
        }

        return false;
    }

    private bool TryBuildAdvancedCellSolveTrace(out PuzzleSolveTrace trace)
    {
        if (_mechanic != SoloPanelMechanic.CellCommit || (!_gridUseKernel && !_gridPageLink))
        {
            trace = new PuzzleSolveTrace(Array.Empty<PuzzleSolveStep>(), "Advanced cell trace unavailable.");
            return false;
        }

        var current = _orderedKeys
            .Select(key => _current.TryGetValue(key, out var value) ? value : 0)
            .ToArray();
        var target = _orderedKeys
            .Select(key => _target.TryGetValue(key, out var value) ? value : 0)
            .ToArray();
        var steps = new List<PuzzleSolveStep>();

        if (_gridUseKernel)
        {
            for (var ordinal = current.Length - 1; ordinal >= 0; ordinal--)
            {
                if (current[ordinal] == target[ordinal])
                {
                    continue;
                }

                steps.Add(new PuzzleSolveStep($"cell:{_orderedKeys[ordinal]}"));
                current[ordinal] ^= 1;

                var localIndex = ordinal % Math.Max(1, GetCellPageSize());
                var row = localIndex / Math.Max(1, _gridCols);
                var col = localIndex % Math.Max(1, _gridCols);
                if (row > 0)
                {
                    current[ordinal - _gridCols] ^= 1;
                }

                if (col > 0)
                {
                    current[ordinal - 1] ^= 1;
                }
            }
        }
        else if (_gridPageLink)
        {
            var pageSize = Math.Max(1, GetCellPageSize());
            for (var ordinal = pageSize; ordinal < current.Length; ordinal++)
            {
                if (current[ordinal] == target[ordinal])
                {
                    continue;
                }

                steps.Add(new PuzzleSolveStep($"cell:{_orderedKeys[ordinal]}"));
                current[ordinal] ^= 1;
                current[ordinal - pageSize] ^= 1;
            }

            for (var ordinal = 0; ordinal < Math.Min(pageSize, current.Length); ordinal++)
            {
                if (current[ordinal] == target[ordinal])
                {
                    continue;
                }

                steps.Add(new PuzzleSolveStep($"cell:{_orderedKeys[ordinal]}"));
                current[ordinal] ^= 1;
            }
        }

        if (current.Where((value, index) => value != target[index]).Any())
        {
            trace = new PuzzleSolveTrace(Array.Empty<PuzzleSolveStep>(), "Advanced cell trace did not converge.");
            return false;
        }

        steps.Add(new PuzzleSolveStep("commit"));
        trace = new PuzzleSolveTrace(steps, $"{FamilyId} advanced cell trace.");
        return true;
    }

    private bool TryBuildCoupledSystemSolveTrace(out PuzzleSolveTrace trace)
    {
        var steps = new List<PuzzleSolveStep>(_orderedKeys.Count + 1);
        foreach (var key in _orderedKeys)
        {
            if (!_target.TryGetValue(key, out var target))
            {
                continue;
            }

            steps.Add(new PuzzleSolveStep($"{_systemCommandPrefix}:set:{key}:{target.ToString(CultureInfo.InvariantCulture)}"));
        }

        steps.Add(new PuzzleSolveStep($"{_systemCommandPrefix}:commit"));
        trace = new PuzzleSolveTrace(steps, $"{FamilyId} coupled-system trace.");
        return steps.Count > 0;
    }

    private bool AreCoupledSystemControlsLocked() =>
        _orderedKeys.Count > 0 &&
        _orderedKeys.All(key =>
            _current.TryGetValue(key, out var current) &&
            _target.TryGetValue(key, out var target) &&
            current == target);

    private IReadOnlyList<SoloPanelActionItem> BuildCoupledSystemActions(bool enabled) =>
    [
        new SoloPanelActionItem($"{_systemCommandPrefix}:commit", "Commit", "commit", enabled),
        new SoloPanelActionItem($"{_systemCommandPrefix}:reset", "Reset", "reset", enabled),
        new SoloPanelActionItem($"{_systemCommandPrefix}:hint", "Hint", "hint", enabled && _hintsRemaining > 0),
    ];

    private IReadOnlyDictionary<string, string> BuildCoupledSystemBoardSnapshot(Dictionary<string, string> board)
    {
        RefreshCoupledSystemTelemetry();
        AppendSharedVisualMetadata(board);
        board["system:variant"] = FamilyId;
        board["system:command_prefix"] = _systemCommandPrefix;
        board["system:headline"] = _systemHeadline;
        board["system:description"] = _systemDescription;
        board["system:control_count"] = _orderedKeys.Count.ToString(CultureInfo.InvariantCulture);
        board["system:metric_count"] = _systemMetricDefinitions.Count.ToString(CultureInfo.InvariantCulture);
        board["system:input_step"] = _systemInputStep.ToString(CultureInfo.InvariantCulture);
        board["system:target_mode"] = _systemTargetMode;
        board["system:ready"] = IsCoupledSystemSolved() ? "1" : "0";
        board["system:locked_count"] = _orderedKeys.Count(key =>
            _current.TryGetValue(key, out var current) &&
            _target.TryGetValue(key, out var target) &&
            current == target).ToString(CultureInfo.InvariantCulture);
        board["system:clue_count"] = _systemClues.Count.ToString(CultureInfo.InvariantCulture);
        board["system:aggregate_progress"] = GetCoupledSystemAxisValue().ToString("0.000", CultureInfo.InvariantCulture);

        foreach (var key in _orderedKeys)
        {
            var current = _current.TryGetValue(key, out var currentValue) ? currentValue : 0;
            var target = _target.TryGetValue(key, out var targetValue) ? targetValue : 0;
            var targetVisible = _systemControlTargetMin.ContainsKey(key) && _systemControlTargetMax.ContainsKey(key);
            var exactVisible = targetVisible && _systemControlTargetMin[key] == _systemControlTargetMax[key];

            board[$"system:control:{key}:label"] = _systemControlLabels.TryGetValue(key, out var label) ? label : key.ToUpperInvariant();
            board[$"system:control:{key}:value"] = current.ToString(CultureInfo.InvariantCulture);
            board[$"system:control:{key}:current_pct"] = current.ToString(CultureInfo.InvariantCulture);
            board[$"system:control:{key}:aligned"] = current == target ? "1" : "0";
            board[$"system:control:{key}:target_visible"] = targetVisible ? "1" : "0";
            board[$"system:control:{key}:target_exact"] = exactVisible ? "1" : "0";
            if (targetVisible)
            {
                board[$"system:control:{key}:target_min"] = _systemControlTargetMin[key].ToString(CultureInfo.InvariantCulture);
                board[$"system:control:{key}:target_max"] = _systemControlTargetMax[key].ToString(CultureInfo.InvariantCulture);
                if (exactVisible)
                {
                    board[$"system:control:{key}:target"] = target.ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        foreach (var metric in _systemMetricDefinitions)
        {
            var current = _systemMetricCurrentValues.TryGetValue(metric.Id, out var currentValue) ? currentValue : 0d;
            var target = _systemMetricTargetValues.TryGetValue(metric.Id, out var targetValue) ? targetValue : 0d;
            var min = _systemMetricBandMin.TryGetValue(metric.Id, out var minValue) ? minValue : target;
            var max = _systemMetricBandMax.TryGetValue(metric.Id, out var maxValue) ? maxValue : target;
            var aligned = current >= min && current <= max;

            board[$"system:metric:{metric.Id}:label"] = metric.Label;
            board[$"system:metric:{metric.Id}:value"] = current.ToString("0.0", CultureInfo.InvariantCulture);
            board[$"system:metric:{metric.Id}:target"] = target.ToString("0.0", CultureInfo.InvariantCulture);
            board[$"system:metric:{metric.Id}:min"] = min.ToString("0.0", CultureInfo.InvariantCulture);
            board[$"system:metric:{metric.Id}:max"] = max.ToString("0.0", CultureInfo.InvariantCulture);
            board[$"system:metric:{metric.Id}:delta"] = Math.Abs(current - target).ToString("0.0", CultureInfo.InvariantCulture);
            board[$"system:metric:{metric.Id}:aligned"] = aligned ? "1" : "0";
            board[$"system:metric:{metric.Id}:unit"] = metric.Unit;
        }

        for (var index = 0; index < _systemClues.Count; index++)
        {
            board[$"system:clue:{index + 1}"] = _systemClues[index];
        }

        switch (FamilyId)
        {
            case "dead_reckoning":
                board["dead_reckoning:destination_preview"] = _systemMetricTargetValues["destination_error"].ToString("0.0", CultureInfo.InvariantCulture);
                board["dead_reckoning:route_band"] = _systemMetricTargetValues["average_bearing"].ToString("0.0", CultureInfo.InvariantCulture);
                break;
            case "gravity_well":
                board["gravity_well:receiver_lock"] = _systemMetricCurrentValues["receiver_lock"].ToString("0.0", CultureInfo.InvariantCulture);
                board["gravity_well:stability_band"] = _systemMetricTargetValues["orbital_stability"].ToString("0.0", CultureInfo.InvariantCulture);
                break;
            case "echo_chamber":
                board["echo_chamber:receiver_gain"] = _systemMetricCurrentValues["receiver_gain"].ToString("0.0", CultureInfo.InvariantCulture);
                board["echo_chamber:phase_window"] = _systemMetricTargetValues["phase_error"].ToString("0.0", CultureInfo.InvariantCulture);
                break;
            case "fault_line":
                board["fault_line:stress_band"] = _systemMetricCurrentValues["fault_tension"].ToString("0.0", CultureInfo.InvariantCulture);
                board["fault_line:seismic_balance"] = _systemMetricCurrentValues["seismic_balance"].ToString("0.0", CultureInfo.InvariantCulture);
                break;
        }

        return board;
    }

    private bool HandleCoupledSystemAction(string command, double nowSeconds)
    {
        var prefix = _systemCommandPrefix;
        if (string.Equals(command, $"{prefix}:hint", StringComparison.OrdinalIgnoreCase))
        {
            if (_hintsRemaining <= 0)
            {
                _statusText = "No hints remaining.";
                StatusText = _statusText;
                return true;
            }

            _hintsRemaining--;
            _hintText = BuildCoupledSystemHintText();
            _statusText = _hintText;
            StatusText = _statusText;
            return true;
        }

        if (string.Equals(command, $"{prefix}:reset", StringComparison.OrdinalIgnoreCase))
        {
            ResetCoupledSystemStage();
            _statusText = $"{_systemHeadline} reset.";
            StatusText = _statusText;
            return true;
        }

        if (string.Equals(command, $"{prefix}:commit", StringComparison.OrdinalIgnoreCase))
        {
            ResolveCommit();
            return true;
        }

        var parts = command.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 4 ||
            !string.Equals(parts[0], prefix, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parts[1], "set", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var key = parts[2];
        if (!_current.ContainsKey(key) || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawValue))
        {
            return false;
        }

        _current[key] = QuantizeSystemValue(Math.Clamp(rawValue, 0, 100), _systemInputStep);
        ApplyInstrumentDriftFromControl(key);
        _phase = PuzzlePhase.Configure;
        _status = PuzzleStatus.Active;
        RefreshCoupledSystemTelemetry();
        _statusText = BuildCoupledSystemStatusText();
        StatusText = _statusText;
        return true;
    }

    private void ApplyInstrumentDriftFromControl(string key)
    {
        if (!string.Equals(_mutatorId, "instrument_drift", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var currentIndex = _orderedKeys.IndexOf(key);
        if (currentIndex < 0 || currentIndex >= _orderedKeys.Count - 1)
        {
            return;
        }

        var neighborKey = _orderedKeys[currentIndex + 1];
        if (!_current.TryGetValue(neighborKey, out var neighbor))
        {
            return;
        }

        var direction = (Math.Abs(PuzzleFactory.StableHash($"{_solutionSeed}|drift|{key}|{neighborKey}")) % 2) == 0 ? -1 : 1;
        _current[neighborKey] = QuantizeSystemValue(Math.Clamp(neighbor + (direction * _systemInputStep), 0, 100), _systemInputStep);
    }

    private void ResetCoupledSystemStage()
    {
        foreach (var key in _orderedKeys)
        {
            _current[key] = _initial.TryGetValue(key, out var initialValue) ? initialValue : 0;
        }

        _failureCode = string.Empty;
        _failureLabel = string.Empty;
        _recoveryText = string.Empty;
        _phase = PuzzlePhase.Configure;
        _status = PuzzleStatus.Active;
        RefreshCoupledSystemTelemetry();
        _statusText = BuildCoupledSystemStatusText();
        StatusText = _statusText;
    }

    private bool IsCoupledSystemSolved()
    {
        RefreshCoupledSystemTelemetry();
        return AreCoupledSystemControlsLocked();
    }

    private double GetCoupledSystemAxisValue()
    {
        RefreshCoupledSystemTelemetry();
        if (_systemMetricDefinitions.Count == 0)
        {
            return 0d;
        }

        double total = 0d;
        foreach (var metric in _systemMetricDefinitions)
        {
            var current = _systemMetricCurrentValues.TryGetValue(metric.Id, out var currentValue) ? currentValue : 0d;
            var target = _systemMetricTargetValues.TryGetValue(metric.Id, out var targetValue) ? targetValue : 0d;
            var band = GetMetricBand(metric);
            var delta = Math.Abs(current - target);
            var overrun = Math.Max(0d, delta - band);
            total += Math.Clamp(1d - (overrun / 24d), 0d, 1d);
        }

        return Math.Clamp(total / _systemMetricDefinitions.Count, 0d, 1d);
    }

    private string BuildCoupledSystemHintText()
    {
        RefreshCoupledSystemTelemetry();
        var weakestMetric = _systemMetricDefinitions
            .OrderByDescending(metric =>
            {
                var current = _systemMetricCurrentValues.TryGetValue(metric.Id, out var currentValue) ? currentValue : 0d;
                var target = _systemMetricTargetValues.TryGetValue(metric.Id, out var targetValue) ? targetValue : 0d;
                return Math.Abs(current - target);
            })
            .FirstOrDefault();

        if (weakestMetric is null)
        {
            return "Hint unavailable.";
        }

        var dominantControlIndex = weakestMetric.Weights
            .Select((weight, index) => new { weight, index })
            .OrderByDescending(entry => entry.weight)
            .First().index;
        var key = dominantControlIndex < _orderedKeys.Count ? _orderedKeys[dominantControlIndex] : _orderedKeys.First();
        var label = _systemControlLabels.TryGetValue(key, out var controlLabel) ? controlLabel : key.ToUpperInvariant();
        var currentMetric = _systemMetricCurrentValues.TryGetValue(weakestMetric.Id, out var currentValue) ? currentValue : 0d;
        var targetMetric = _systemMetricTargetValues.TryGetValue(weakestMetric.Id, out var targetValue) ? targetValue : 0d;
        var direction = currentMetric < targetMetric ? "raise" : "reduce";
        return $"Hint: {direction} {label} to pull {weakestMetric.Label.ToUpperInvariant()} toward its band.";
    }

    private string BuildCoupledSystemStatusText()
    {
        RefreshCoupledSystemTelemetry();
        var readyCount = _systemMetricDefinitions.Count(metric =>
            _systemMetricCurrentValues.TryGetValue(metric.Id, out var current) &&
            _systemMetricBandMin.TryGetValue(metric.Id, out var min) &&
            _systemMetricBandMax.TryGetValue(metric.Id, out var max) &&
            current >= min &&
            current <= max);
        var lockedCount = _orderedKeys.Count(key =>
            _current.TryGetValue(key, out var current) &&
            _target.TryGetValue(key, out var target) &&
            current == target);
        if (AreCoupledSystemControlsLocked())
        {
            return $"{_systemHeadline} ready. Commit the stabilized solution.";
        }

        if (readyCount == _systemMetricDefinitions.Count && readyCount > 0)
        {
            return $"{_systemHeadline}: telemetry stable, refine exact locks {lockedCount}/{_orderedKeys.Count}.";
        }

        return $"{_systemHeadline}: {readyCount}/{_systemMetricDefinitions.Count} metrics inside band, {lockedCount}/{_orderedKeys.Count} controls locked.";
    }

    private string BuildCoupledSystemCommitFailureText()
    {
        RefreshCoupledSystemTelemetry();
        if (!AreCoupledSystemControlsLocked() &&
            _systemMetricDefinitions.All(metric =>
                _systemMetricCurrentValues.TryGetValue(metric.Id, out var current) &&
                _systemMetricBandMin.TryGetValue(metric.Id, out var min) &&
                _systemMetricBandMax.TryGetValue(metric.Id, out var max) &&
                current >= min &&
                current <= max))
        {
            return "Telemetry is stable, but the exact control lock is incomplete.";
        }

        var weakestMetric = _systemMetricDefinitions
            .OrderByDescending(metric =>
            {
                var current = _systemMetricCurrentValues.TryGetValue(metric.Id, out var currentValue) ? currentValue : 0d;
                var target = _systemMetricTargetValues.TryGetValue(metric.Id, out var targetValue) ? targetValue : 0d;
                return Math.Abs(current - target);
            })
            .FirstOrDefault();

        return weakestMetric is null
            ? "Commit rejected."
            : $"{weakestMetric.Label} remains outside the safe band.";
    }

    private void RefreshCoupledSystemTelemetry()
    {
        _systemMetricCurrentValues.Clear();
        _systemMetricTargetValues.Clear();
        _systemMetricBandMin.Clear();
        _systemMetricBandMax.Clear();

        foreach (var metric in _systemMetricDefinitions)
        {
            var current = ComputeCoupledSystemMetric(metric, target: false);
            var targetValue = ComputeCoupledSystemMetric(metric, target: true);
            var band = GetMetricBand(metric);
            _systemMetricCurrentValues[metric.Id] = current;
            _systemMetricTargetValues[metric.Id] = targetValue;
            _systemMetricBandMin[metric.Id] = Math.Max(0d, targetValue - band);
            _systemMetricBandMax[metric.Id] = Math.Min(100d, targetValue + band);
        }
    }

    private void BuildCoupledSystemClues()
    {
        _systemClues.Clear();
        if (_orderedKeys.Count == 0)
        {
            return;
        }

        var ordered = _orderedKeys
            .Select(key => new
            {
                key,
                label = _systemControlLabels.TryGetValue(key, out var label) ? label : key.ToUpperInvariant(),
                target = _target.TryGetValue(key, out var target) ? target : 0,
            })
            .OrderByDescending(entry => entry.target)
            .ToArray();

        _systemClues.Add($"{ordered[0].label} carries the dominant load.");
        if (ordered.Length >= 2)
        {
            _systemClues.Add($"{ordered[0].label} should stay above {ordered[^1].label}.");
        }

        if (ordered.Length >= 3)
        {
            var pairSum = ordered[1].target + ordered[2].target;
            _systemClues.Add($"{ordered[1].label} + {ordered[2].label} settles near {pairSum.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (_difficulty == MazeDifficulty.Medium)
        {
            foreach (var entry in ordered.Where(entry =>
                         !_systemControlTargetMin.ContainsKey(entry.key) ||
                         !_systemControlTargetMax.ContainsKey(entry.key) ||
                         _systemControlTargetMin[entry.key] != _systemControlTargetMax[entry.key]))
            {
                _systemClues.Add($"{entry.label} locks at {entry.target.ToString(CultureInfo.InvariantCulture)}.");
            }
        }
        else if (_difficulty == MazeDifficulty.Hard)
        {
            foreach (var entry in ordered)
            {
                _systemClues.Add($"{entry.label} locks at {entry.target.ToString(CultureInfo.InvariantCulture)}.");
            }
        }

        if (_difficulty == MazeDifficulty.Easy && _systemClues.Count > 1)
        {
            _systemClues.RemoveRange(1, _systemClues.Count - 1);
        }
    }

    private double ComputeCoupledSystemMetric(CoupledSystemMetricDefinition metric, bool target)
    {
        if (_orderedKeys.Count == 0)
        {
            return 0d;
        }

        double total = 0d;
        double weightTotal = 0d;
        for (var index = 0; index < metric.Weights.Length && index < _orderedKeys.Count; index++)
        {
            var key = _orderedKeys[index];
            var value = target
                ? (_target.TryGetValue(key, out var targetValue) ? targetValue : 0)
                : (_current.TryGetValue(key, out var currentValue) ? currentValue : 0);
            total += (value / 100d) * metric.Weights[index];
            weightTotal += metric.Weights[index];
        }

        foreach (var pair in metric.PairWeights)
        {
            if (pair.LeftIndex >= _orderedKeys.Count || pair.RightIndex >= _orderedKeys.Count)
            {
                continue;
            }

            var leftKey = _orderedKeys[pair.LeftIndex];
            var rightKey = _orderedKeys[pair.RightIndex];
            var left = target
                ? (_target.TryGetValue(leftKey, out var targetLeft) ? targetLeft : 0)
                : (_current.TryGetValue(leftKey, out var currentLeft) ? currentLeft : 0);
            var right = target
                ? (_target.TryGetValue(rightKey, out var targetRight) ? targetRight : 0)
                : (_current.TryGetValue(rightKey, out var currentRight) ? currentRight : 0);
            total += (((left + right) / 2d) / 100d) * pair.Weight;
            weightTotal += pair.Weight;
        }

        if (weightTotal <= 0d)
        {
            return 0d;
        }

        return Math.Clamp((total / weightTotal) * 100d, 0d, 100d);
    }

    private double GetMetricBand(CoupledSystemMetricDefinition metric) => _difficulty switch
    {
        MazeDifficulty.Easy => metric.BaseBand + 2d,
        MazeDifficulty.Medium => metric.BaseBand,
        MazeDifficulty.Hard => Math.Max(2d, metric.BaseBand - 1d),
        _ => metric.BaseBand,
    };

    private int QuantizeSystemValue(int value, int step)
    {
        if (step <= 1)
        {
            return Math.Clamp(value, 0, 100);
        }

        var quantized = (int)Math.Round(value / (double)step, MidpointRounding.AwayFromZero) * step;
        return Math.Clamp(quantized, 0, 100);
    }

    private (int ordinal, int pageIndex, int localIndex) GetCellAddress(string key)
    {
        var ordinal = ExtractNumericSuffix(key) - 1;
        ordinal = Math.Max(0, ordinal);
        var pageSize = Math.Max(1, GetCellPageSize());
        return (ordinal, ordinal / pageSize, ordinal % pageSize);
    }

    private void ToggleCellByKey(string key)
    {
        if (!_current.TryGetValue(key, out var current))
        {
            return;
        }

        _current[key] = current == 0 ? 1 : 0;
        _cellActionActive[key] = _current[key] > 0;
    }

    private void ToggleCellByOrdinal(int ordinal)
    {
        if (ordinal < 0 || ordinal >= _orderedKeys.Count)
        {
            return;
        }

        ToggleCellByKey(_orderedKeys[ordinal]);
    }

    private string BuildGridStatusText(int aligned)
    {
        if (FamilyId == "temporal_grid" && _gridPageLink)
        {
            return $"Temporal states aligned {aligned}/{_orderedKeys.Count}. Page 2 echoes into page 1.";
        }

        if (FamilyId == "pressure_grid" && _gridUseKernel)
        {
            return $"Pressure nodes aligned {aligned}/{_orderedKeys.Count}. Kernel flips self + up + left.";
        }

        return $"Cells aligned {aligned}/{_orderedKeys.Count}.";
    }

    private string BuildGridHintText()
    {
        var mismatch = _orderedKeys.FirstOrDefault(key =>
            _current.TryGetValue(key, out var current) &&
            _target.TryGetValue(key, out var target) &&
            current != target);
        if (string.IsNullOrWhiteSpace(mismatch))
        {
            return "Grid is already aligned.";
        }

        if (FamilyId == "pressure_grid" && _gridUseKernel)
        {
            return $"Hint: {mismatch.ToUpperInvariant()} is wrong. Each toggle flips itself, the node above, and the node to the left.";
        }

        if (FamilyId == "temporal_grid" && _gridPageLink)
        {
            return $"Hint: {mismatch.ToUpperInvariant()} is off. Page 2 cells also flip their matching cell on page 1.";
        }

        return _gridVisibleTargets.Contains(mismatch)
            ? $"Hint: {mismatch.ToUpperInvariant()} should match its visible target marker."
            : $"Hint: inspect the row and column guides around {mismatch.ToUpperInvariant()}.";
    }

    private void ConfigureCipherWheelFamily()
    {
        _progressLabel = "Cipher Stability / Decode Confidence";
        var keyCount = _difficulty == MazeDifficulty.Easy ? 3 : 4;
        var maxValue = _difficulty == MazeDifficulty.Easy ? 11 : 25;
        ConfigureDialFamily(
            keyCount: keyCount,
            maxValue: maxValue,
            stages: 1,
            timerSeconds: _difficulty == MazeDifficulty.Easy ? 70d : _difficulty == MazeDifficulty.Medium ? 110d : 170d,
            attempts: _difficulty == MazeDifficulty.Hard ? 4 : 5);

        var shift = 2 + (Math.Abs(PuzzleFactory.StableHash($"{_solutionSeed}|cipher-shift|{TierLevel}")) % Math.Min(7, maxValue));
        foreach (var key in _orderedKeys)
        {
            if (!_target.TryGetValue(key, out var target))
            {
                continue;
            }

            var span = maxValue + 1;
            var encoded = (target + shift) % span;
            _current[key] = encoded;
            _initial[key] = encoded;
        }

        _showTarget = _difficulty != MazeDifficulty.Hard;
        _statusText = _difficulty switch
        {
            MazeDifficulty.Easy => "Visible decode rings online. Match the wheel targets directly.",
            MazeDifficulty.Medium => $"Cipher hint: shift {shift:+#;-#;0}. Reconstruct the plaintext rings.",
            MazeDifficulty.Hard => "Encoded fragment loaded. Deduce the wheel positions from the clue fragment.",
            _ => "Align the cipher wheels.",
        };
        StatusText = _statusText;
    }

    private void ConfigureTokenFloodFamily()
    {
        _progressLabel = "Route Integrity / Pressure Stability";
        ConfigurePipeFamily(
            _difficulty == MazeDifficulty.Easy ? 3 : _difficulty == MazeDifficulty.Medium ? 4 : 5,
            _difficulty == MazeDifficulty.Easy ? 3 : _difficulty == MazeDifficulty.Medium ? 4 : 5,
            _difficulty == MazeDifficulty.Easy ? 120d : _difficulty == MazeDifficulty.Medium ? 150d : 220d,
            _difficulty == MazeDifficulty.Hard ? 3 : 4);
        ConfigureTokenFloodPressureModel();
        _statusText = BuildPipeProgressStatus();
        StatusText = _statusText;
    }

    private void ConfigureTokenFloodPressureModel()
    {
        _pipePressure.Clear();
        _pipeGateThreshold.Clear();
        if (_difficulty != MazeDifficulty.Hard)
        {
            return;
        }

        foreach (var key in _orderedKeys)
        {
            var noise = Math.Abs(PuzzleFactory.StableHash($"{_layoutSeed}|gate|{key}|{TierLevel}")) % 100;
            if (key == _pipeSourceKey || key == _pipeSinkKey || noise < 62)
            {
                continue;
            }

            _pipeGateThreshold[key] = 10d + (noise % 10);
        }
    }

    private string BuildTokenFloodHintText()
    {
        var gate = _pipeGateThreshold
            .Where(entry => entry.Value > 0d)
            .OrderByDescending(entry => entry.Value)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(gate.Key))
        {
            return $"Hint: {gate.Key.ToUpperInvariant()} only opens above {gate.Value.ToString("0", CultureInfo.InvariantCulture)} pressure.";
        }

        return "Hint: trace the main trunk first, then remove branches that vent pressure.";
    }

    private double GetTokenFloodMechanicProgress()
    {
        var (sinkReached, leakFree) = UpdatePipeFlowState();
        if (_orderedKeys.Count == 0)
        {
            return 0d;
        }

        var routeIntegrity = _pipeFlowActive.Count(entry => entry.Value) / (double)_orderedKeys.Count;
        var sinkPressure = _pipePressure.TryGetValue(_pipeSinkKey, out var sink) ? sink / 100d : 0d;
        var stability = leakFree ? 1d : 0.3d;
        if (sinkReached && leakFree)
        {
            return 1d;
        }

        return Math.Clamp((routeIntegrity * 0.35d) + (sinkPressure * 0.4d) + (stability * 0.25d), 0d, 0.98d);
    }

    private void ConfigureMemoryPalaceFamily()
    {
        _progressLabel = "Reconstruction Completeness / Archive Restoration";
        var cardCount = _difficulty switch
        {
            MazeDifficulty.Easy => _stageLevel <= 2 ? 8 : 12,
            MazeDifficulty.Medium => _stageLevel <= 2 ? 12 : _stageLevel == 3 ? 16 : 20,
            MazeDifficulty.Hard => _stageLevel <= 2 ? 16 : _stageLevel == 3 ? 20 : 24,
            _ => 12,
        };
        ConfigureMemoryFamily(
            cardCount: cardCount,
            timerSeconds: _difficulty == MazeDifficulty.Easy ? 150d : _difficulty == MazeDifficulty.Medium ? 120d : 100d,
            attempts: _difficulty == MazeDifficulty.Hard ? 4 : 5);
        _memoryInitialLayout.AddRange(_memoryValues);
        _statusText = "Recover matching fragments before the archive destabilizes.";
        StatusText = _statusText;
    }

    private void UpdateMemoryPalace(PuzzleUpdateContext context)
    {
        if (_difficulty != MazeDifficulty.Hard || _memoryMatched.Count == 0 || _memoryOpen.Count > 0 || _memoryMatched.All(matched => matched))
        {
            return;
        }

        _memoryRotateElapsedSeconds += context.DeltaTimeSeconds;
        if (_memoryRotateElapsedSeconds < 3.5d)
        {
            return;
        }

        _memoryRotateElapsedSeconds = 0d;
        RotateUnmatchedMemorySlots();
    }

    private void RotateUnmatchedMemorySlots()
    {
        var unmatched = Enumerable.Range(0, _memoryValues.Count)
            .Where(index => !_memoryMatched[index])
            .ToArray();
        if (unmatched.Length < 3)
        {
            return;
        }

        var lastValue = _memoryValues[unmatched[^1]];
        for (var index = unmatched.Length - 1; index > 0; index--)
        {
            _memoryValues[unmatched[index]] = _memoryValues[unmatched[index - 1]];
        }

        _memoryValues[unmatched[0]] = lastValue;
    }

    private void ResetMemoryPalaceStage()
    {
        for (var index = 0; index < _memoryMatched.Count; index++)
        {
            _memoryMatched[index] = false;
        }

        _memoryOpen.Clear();
        _memoryRotateElapsedSeconds = 0d;
        if (_memoryInitialLayout.Count == _memoryValues.Count)
        {
            _memoryValues.Clear();
            _memoryValues.AddRange(_memoryInitialLayout);
        }
    }

    private double GetMemoryPalaceMechanicProgress()
    {
        if (_memoryMatched.Count == 0)
        {
            return 0d;
        }

        var matched = _memoryMatched.Count(matched => matched) / (double)_memoryMatched.Count;
        var mistakePenalty = Math.Min(0.25d, _mistakes * 0.04d);
        return Math.Clamp(matched - mistakePenalty, 0d, 1d);
    }

    private string BuildMemoryPalaceHintText()
    {
        if (_difficulty == MazeDifficulty.Hard)
        {
            return "Hint: watch how unmatched fragments migrate before committing to a guess.";
        }

        return "Hint: lock in obvious pairs before exploring unknown cards.";
    }

    private string BuildVisibleMemoryFragment(int index, int pairValue, string state)
    {
        var full = BuildMemoryFragmentLabel(index, pairValue);
        if (_difficulty != MazeDifficulty.Hard || !string.Equals(state, "hidden", StringComparison.OrdinalIgnoreCase))
        {
            return full;
        }

        var prefixLength = Math.Min(3, full.Length);
        return $"{full[..prefixLength]}-??";
    }

    private static int ExtractNumericSuffix(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }
}
