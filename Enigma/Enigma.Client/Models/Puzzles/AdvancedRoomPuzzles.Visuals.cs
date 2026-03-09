namespace Enigma.Client.Models.Gameplay;

using System.Globalization;
using System.Text;

public sealed partial class SoloPanelBiblePuzzle
{
    private static readonly string[] DeadReckoningLabels = ["Bearing", "Drift", "Range", "Burn", "Vector", "Anchor", "Bias", "Delta"];
    private static readonly string[] CipherWheelLabels = ["Outer", "Middle", "Inner", "Latch", "Shift", "Gate", "Prime", "Echo"];
    private static readonly string[] GravityWellLabels = ["Well A", "Well B", "Well C", "Spine", "Lens", "Mass", "Wake", "Node"];
    private static readonly string[] EchoChamberLabels = ["Mirror A", "Mirror B", "Mirror C", "Gate", "Latch", "Focus", "Relay", "Prism"];
    private static readonly string[] FaultLineLabels = ["Seam A", "Seam B", "Seam C", "Shelf", "Plate", "Rift", "Span", "Torsion"];
    private static readonly string[] MemoryGlyphs = ["EMBER", "GLASS", "QUARTZ", "LANTERN", "RAVEN", "ORBIT", "CINDER", "THREAD", "AURORA", "HARBOR", "THORN", "IVORY"];
    private static readonly string[] TemporalEraLabels = ["PAST", "PRESENT", "FUTURE", "PARADOX"];

    private void AppendSharedVisualMetadata(Dictionary<string, string> board)
    {
        var dimensions = GetDifficultyDimensions();
        board["visual:difficulty"] = GetVisualDifficultyKey();
        board["visual:difficulty_label"] = _difficulty.ToString().ToUpperInvariant();
        board["visual:guidance"] = _difficulty switch
        {
            MazeDifficulty.Easy => "high",
            MazeDifficulty.Medium => "medium",
            MazeDifficulty.Hard => "low",
            _ => "medium",
        };
        board["visual:density"] = _difficulty switch
        {
            MazeDifficulty.Easy => "relaxed",
            MazeDifficulty.Medium => "balanced",
            MazeDifficulty.Hard => "dense",
            _ => "balanced",
        };
        board["visual:target_visibility"] = GetVisualTargetVisibilityKey();
        board["visual:timer_total"] = _timerSeconds.ToString("0.000", CultureInfo.InvariantCulture);
        board["visual:family_title"] = Title;
        board["visual:family_id"] = FamilyId;
        board["visual:accent"] = GetPrimaryVisualAccent();
        board["visual:accent_alt"] = GetSecondaryVisualAccent();
        board["visual:dimension:visibility"] = dimensions.InformationVisibility;
        board["visual:dimension:complexity"] = dimensions.SystemComplexity;
        board["visual:dimension:execution"] = dimensions.ExecutionDifficulty;
        board["visual:dimension:time"] = dimensions.TimePressure;
        board["visual:dimension:cognitive"] = dimensions.CognitiveLoad;
        board["visual:status_band"] = _status switch
        {
            PuzzleStatus.Solved => "solved",
            PuzzleStatus.Cooldown => "cooldown",
            PuzzleStatus.Resetting => "resetting",
            _ when !string.IsNullOrWhiteSpace(_failureCode) => "fault",
            _ => "active",
        };
    }

    private void AppendDialVisualMetadata(Dictionary<string, string> board)
    {
        AppendSharedVisualMetadata(board);

        var alignedCount = _orderedKeys.Count(key =>
            _current.TryGetValue(key, out var current) &&
            _target.TryGetValue(key, out var target) &&
            current == target);

        board["dialviz:variant"] = FamilyId;
        board["dialviz:key_count"] = _orderedKeys.Count.ToString(CultureInfo.InvariantCulture);
        board["dialviz:aligned_count"] = alignedCount.ToString(CultureInfo.InvariantCulture);
        board["dialviz:show_target"] = _showTarget ? "1" : "0";
        board["dialviz:subtitle"] = GetDialVisualSubtitle();
        board["dialviz:summary_token"] = BuildDialSummaryToken();

        for (var index = 0; index < _orderedKeys.Count; index++)
        {
            var key = _orderedKeys[index];
            var current = _current.TryGetValue(key, out var currentValue) ? currentValue : 0;
            var target = _target.TryGetValue(key, out var targetValue) ? targetValue : 0;
            var max = Math.Max(1, _max.TryGetValue(key, out var maxValue) ? maxValue : 1);
            var currentPercent = (current / (double)max) * 100d;
            var targetPercent = (target / (double)max) * 100d;
            var distance = GetCircularDialDistance(current, target, max);

            board[$"dialviz:{key}:index"] = (index + 1).ToString(CultureInfo.InvariantCulture);
            board[$"dialviz:{key}:label"] = GetDialVisualLabel(index);
            board[$"dialviz:{key}:current_pct"] = currentPercent.ToString("0.000", CultureInfo.InvariantCulture);
            board[$"dialviz:{key}:distance"] = distance.ToString(CultureInfo.InvariantCulture);
            board[$"dialviz:{key}:aligned"] = current == target ? "1" : "0";
            if (_showTarget)
            {
                board[$"dialviz:{key}:target_pct"] = targetPercent.ToString("0.000", CultureInfo.InvariantCulture);
            }
        }

        switch (FamilyId)
        {
            case "dead_reckoning":
                board["dead_reckoning:route_code"] = BuildDialSummaryToken();
                board["dead_reckoning:waypoints"] = string.Join(",", _orderedKeys.Select((_, index) => $"WP{index + 1:00}"));
                board["dead_reckoning:destination"] = $"{(SumTargetValues() % 360):000} deg";
                break;
            case "cipher_wheel":
                var targetFragment = _showTarget
                    ? BuildGlyphStreamFromKeys(_orderedKeys, _target)
                    : BuildGlyphMask(_orderedKeys.Count);
                var currentFragment = BuildGlyphStreamFromKeys(_orderedKeys, _current);
                board["dialviz:summary_token"] = BuildCipherWheelSummaryToken();
                board["cipher_wheel:token_stream"] = targetFragment;
                board["cipher_wheel:target_fragment"] = targetFragment;
                board["cipher_wheel:mask_state"] = _showTarget ? "revealed" : "masked";
                board["cipher_wheel:rule_hint"] = _difficulty == MazeDifficulty.Medium
                    ? ExtractCipherWheelRuleHint()
                    : string.Empty;
                board["cipher_wheel:encoded_fragment"] = currentFragment;
                board["cipher_wheel:current_fragment"] = currentFragment;
                board["cipher_wheel:semantic_hint"] = _difficulty == MazeDifficulty.Hard
                    ? BuildCipherWheelSemanticHint()
                    : "Direct decode";
                break;
            case "gravity_well":
                board["gravity_well:receiver_arc"] = $"{(SumTargetValues() % 360):000} deg";
                board["gravity_well:well_count"] = _orderedKeys.Count.ToString(CultureInfo.InvariantCulture);
                break;
            case "echo_chamber":
                board["echo_chamber:bounce_window"] = (2 + (SumTargetValues() % 5)).ToString(CultureInfo.InvariantCulture);
                board["echo_chamber:receiver_band"] = $"{(SumCurrentValues() % 100):00}%";
                break;
            case "fault_line":
                board["fault_line:strata_count"] = _orderedKeys.Count.ToString(CultureInfo.InvariantCulture);
                board["fault_line:stress_band"] = $"{Math.Clamp(alignedCount * 100 / Math.Max(1, _orderedKeys.Count), 0, 100):00}%";
                break;
        }
    }

    private void AppendGridVisualMetadata(Dictionary<string, string> board, IReadOnlyList<string> visibleKeys)
    {
        AppendSharedVisualMetadata(board);

        var (rows, cols) = _gridRows > 0 && _gridCols > 0
            ? (_gridRows, _gridCols)
            : GetPagedGridDimensions(visibleKeys.Count);
        var visibleGrid = visibleKeys
            .Select((key, index) => new
            {
                key,
                row = index / cols,
                col = index % cols,
                current = _current.TryGetValue(key, out var currentValue) ? currentValue : 0,
                target = _target.TryGetValue(key, out var targetValue) ? targetValue : 0,
            })
            .ToArray();

        board["gridviz:variant"] = FamilyId;
        board["gridviz:visible_count"] = visibleKeys.Count.ToString(CultureInfo.InvariantCulture);
        board["gridviz:page_rows"] = rows.ToString(CultureInfo.InvariantCulture);
        board["gridviz:page_cols"] = cols.ToString(CultureInfo.InvariantCulture);
        board["gridviz:show_target"] = visibleGrid.Any(cell => _gridVisibleTargets.Contains(cell.key) || (_showTarget && _gridVisibleTargets.Count == 0)) ? "1" : "0";
        board["gridviz:aligned_count"] = CountAlignedCells().ToString(CultureInfo.InvariantCulture);
        board["gridviz:page_label"] = FamilyId == "temporal_grid"
            ? $"Epoch {_cellPageIndex + 1}"
            : $"Zone {_cellPageIndex + 1}";
        board["gridviz:directive"] = FamilyId switch
        {
            "pressure_grid" when _gridUseKernel => "Kernel rule active: each toggle flips self, up, and left.",
            "temporal_grid" when _gridPageLink => "Page 2 toggles also flip their mirrored cell on page 1.",
            _ when _showTarget => "Match each live cell to the visible target markers and guide totals.",
            _ => "Use the row and column phase totals to infer the hidden pattern.",
        };

        foreach (var cell in visibleGrid)
        {
            var key = cell.key;
            var max = Math.Max(1, _max.TryGetValue(key, out var maxValue) ? maxValue : 1);
            var aligned = cell.current == cell.target;
            var intensity = Math.Clamp(((cell.current + 1d) / (max + 1d)) * 100d, 0d, 100d);
            var targetVisible = _gridVisibleTargets.Count == 0
                ? _showTarget
                : _gridVisibleTargets.Contains(key);

            board[$"gridviz:{key}:row"] = cell.row.ToString(CultureInfo.InvariantCulture);
            board[$"gridviz:{key}:col"] = cell.col.ToString(CultureInfo.InvariantCulture);
            board[$"gridviz:{key}:aligned"] = aligned ? "1" : "0";
            board[$"gridviz:{key}:intensity"] = intensity.ToString("0.000", CultureInfo.InvariantCulture);
            board[$"gridviz:{key}:target_visible"] = targetVisible ? "1" : "0";
            board[$"gridviz:{key}:live_on"] = cell.current > 0 ? "1" : "0";
            board[$"gridviz:{key}:target_on"] = cell.target > 0 ? "1" : "0";
            if (targetVisible)
            {
                board[$"gridviz:{key}:target"] = cell.target.ToString(CultureInfo.InvariantCulture);
            }
        }

        for (var row = 0; row < rows; row++)
        {
            var rowCells = visibleGrid.Where(cell => cell.row == row).ToArray();
            board[$"gridviz:row:{row}:current_active"] = rowCells.Count(cell => cell.current > 0).ToString(CultureInfo.InvariantCulture);
            board[$"gridviz:row:{row}:target_active"] = rowCells.Count(cell => cell.target > 0).ToString(CultureInfo.InvariantCulture);
            board[$"gridviz:row:{row}:aligned"] = rowCells.All(cell => cell.current == cell.target) ? "1" : "0";
        }

        for (var col = 0; col < cols; col++)
        {
            var colCells = visibleGrid.Where(cell => cell.col == col).ToArray();
            board[$"gridviz:col:{col}:current_active"] = colCells.Count(cell => cell.current > 0).ToString(CultureInfo.InvariantCulture);
            board[$"gridviz:col:{col}:target_active"] = colCells.Count(cell => cell.target > 0).ToString(CultureInfo.InvariantCulture);
            board[$"gridviz:col:{col}:aligned"] = colCells.All(cell => cell.current == cell.target) ? "1" : "0";
        }

        if (visibleGrid.Length > 0)
        {
            var firstMismatch = visibleGrid.FirstOrDefault(cell => cell.current != cell.target);
            if (firstMismatch is not null)
            {
                board["gridviz:mismatch_focus"] = firstMismatch.key;
            }
        }

        switch (FamilyId)
        {
            case "pressure_grid":
                board["pressure_grid:hotspot_count"] = visibleKeys.Count(key => _target.TryGetValue(key, out var target) && target > 0).ToString(CultureInfo.InvariantCulture);
                board["pressure_grid:mode"] = _difficulty == MazeDifficulty.Easy ? "guided" : _difficulty == MazeDifficulty.Hard ? "kernel" : "balanced";
                break;
            case "temporal_grid":
                board["temporal_grid:era"] = TemporalEraLabels[_cellPageIndex % TemporalEraLabels.Length];
                board["temporal_grid:sync_mode"] = _gridPageLink ? "linked" : _showTarget ? "tracked" : "blind";
                break;
        }
    }

    private void AppendFlowVisualMetadata(Dictionary<string, string> board, bool sinkReached, bool leakFree)
    {
        AppendSharedVisualMetadata(board);

        var routeCells = _pipeFlowActive.Count(entry => entry.Value);
        var pressurePercent = Math.Clamp((routeCells / (double)Math.Max(1, _orderedKeys.Count)) * 100d, 0d, 100d);

        board["flowviz:variant"] = FamilyId;
        board["flowviz:route_cells"] = routeCells.ToString(CultureInfo.InvariantCulture);
        board["flowviz:pressure_pct"] = pressurePercent.ToString("0.000", CultureInfo.InvariantCulture);
        board["flowviz:linked"] = sinkReached ? "1" : "0";
        board["flowviz:leak_free"] = leakFree ? "1" : "0";
        board["token_flood:reservoir_state"] = sinkReached && leakFree
            ? "stable"
            : sinkReached
                ? "overflow"
                : "seeking";

        foreach (var key in _orderedKeys)
        {
            var (row, col) = ParsePipeKey(key);
            var mask = _pipeMask.TryGetValue(key, out var maskValue) ? maskValue : 0;
            var turns = _current.TryGetValue(key, out var turnValue) ? turnValue : 0;
            board[$"flowviz:{key}:row"] = row.ToString(CultureInfo.InvariantCulture);
            board[$"flowviz:{key}:col"] = col.ToString(CultureInfo.InvariantCulture);
            board[$"flowviz:{key}:rotated_mask"] = RotatePipeMaskForVisuals(mask, turns).ToString(CultureInfo.InvariantCulture);
            board[$"flowviz:{key}:state"] = _pipeFlowActive.TryGetValue(key, out var active) && active ? "flowing" : "idle";
            board[$"flowviz:{key}:pressure"] = (_pipePressure.TryGetValue(key, out var pressure) ? pressure : 0d).ToString("0.0", CultureInfo.InvariantCulture);
            board[$"flowviz:{key}:gate"] = (_pipeGateThreshold.TryGetValue(key, out var threshold) && threshold > 0d) ? "1" : "0";
            board[$"flowviz:{key}:gate_threshold"] = (_pipeGateThreshold.TryGetValue(key, out var gateThreshold) ? gateThreshold : 0d).ToString("0.0", CultureInfo.InvariantCulture);
        }
    }

    private void AppendMemoryVisualMetadata(Dictionary<string, string> board)
    {
        AppendSharedVisualMetadata(board);

        board["memoryviz:variant"] = FamilyId;
        board["memoryviz:card_count"] = _memoryValues.Count.ToString(CultureInfo.InvariantCulture);
        board["memoryviz:matched_count"] = _memoryMatched.Count(matched => matched).ToString(CultureInfo.InvariantCulture);
        board["memoryviz:open_count"] = _memoryOpen.Count.ToString(CultureInfo.InvariantCulture);
        board["memoryviz:columns"] = GetMemoryColumnCount().ToString(CultureInfo.InvariantCulture);
        board["memory_palace:archive_sector"] = $"Sector {_stageLevel}";
        board["memory_palace:fragment_mode"] = _difficulty == MazeDifficulty.Hard ? "volatile" : _difficulty == MazeDifficulty.Medium ? "indexed" : "guided";

        for (var index = 0; index < _memoryValues.Count; index++)
        {
            var pairValue = _memoryValues[index];
            var state = _memoryMatched[index]
                ? "matched"
                : _memoryOpen.Contains(index)
                    ? "open"
                    : "hidden";

            board[$"memoryviz:card:{index}:state"] = state;
            board[$"memoryviz:card:{index}:pair"] = pairValue.ToString(CultureInfo.InvariantCulture);
            board[$"memoryviz:card:{index}:glyph"] = GetMemoryGlyph(pairValue);
            board[$"memoryviz:card:{index}:fragment"] = BuildVisibleMemoryFragment(index, pairValue, state);
            board[$"memoryviz:card:{index}:rotating"] = _difficulty == MazeDifficulty.Hard && state == "hidden" ? "1" : "0";
        }
    }

    private string ExtractCipherWheelRuleHint()
    {
        if (_orderedKeys.Count == 0)
        {
            return "SHIFT +0";
        }

        var key = _orderedKeys[0];
        if (!_current.TryGetValue(key, out var encoded) || !_target.TryGetValue(key, out var target))
        {
            return "SHIFT +0";
        }

        var span = Math.Max(1, (_max.TryGetValue(key, out var max) ? max : 25) + 1);
        var shift = (encoded - target + span) % span;
        return $"SHIFT +{shift.ToString(CultureInfo.InvariantCulture)}";
    }

    private string BuildCipherWheelSemanticHint() =>
        FamilyId == "cipher_wheel"
            ? "Common greeting fragment"
            : string.Empty;

    private string GetVisualDifficultyKey() => _difficulty.ToString().ToLowerInvariant();

    private string GetPrimaryVisualAccent() => FamilyId switch
    {
        "dead_reckoning" => "#57d2ff",
        "pressure_grid" => "#ff8b5d",
        "cipher_wheel" => "#f5c84c",
        "gravity_well" => "#73f0c2",
        "echo_chamber" => "#ff6f91",
        "token_flood" => "#57c6ff",
        "memory_palace" => "#ffd479",
        "fault_line" => "#ff7e66",
        "temporal_grid" => "#84f0ff",
        _ => "#8fb0ff",
    };

    private string GetSecondaryVisualAccent() => FamilyId switch
    {
        "dead_reckoning" => "#1f6d8f",
        "pressure_grid" => "#6a2d1d",
        "cipher_wheel" => "#7a5d1b",
        "gravity_well" => "#1d5d52",
        "echo_chamber" => "#5f2644",
        "token_flood" => "#194f6a",
        "memory_palace" => "#6b5630",
        "fault_line" => "#5c2d27",
        "temporal_grid" => "#1f4f67",
        _ => "#324969",
    };

    private string GetDialVisualSubtitle() => FamilyId switch
    {
        "dead_reckoning" => "Route vectors and simulation drift",
        "cipher_wheel" => "Decoder drums and token lattice",
        "gravity_well" => "Orbital wells and receiver lock",
        "echo_chamber" => "Reflector banks and beam return",
        "fault_line" => "Strata offsets and seam stress",
        _ => "Channel telemetry",
    };

    private string GetDialVisualLabel(int index)
    {
        var labels = FamilyId switch
        {
            "dead_reckoning" => DeadReckoningLabels,
            "cipher_wheel" => CipherWheelLabels,
            "gravity_well" => GravityWellLabels,
            "echo_chamber" => EchoChamberLabels,
            "fault_line" => FaultLineLabels,
            _ => DeadReckoningLabels,
        };

        return labels[index % labels.Length];
    }

    private string BuildDialSummaryToken()
    {
        var builder = new StringBuilder();
        foreach (var value in (_showTarget ? _target.Values : _current.Values).Take(4))
        {
            var normalized = Math.Abs(value) % 26;
            builder.Append((char)('A' + normalized));
        }

        return builder.Length == 0 ? "VOID" : builder.ToString();
    }

    private string BuildCipherWheelSummaryToken() => _difficulty switch
    {
        MazeDifficulty.Easy => "DIRECT",
        MazeDifficulty.Medium => ExtractCipherWheelRuleHint(),
        MazeDifficulty.Hard => "MASKED",
        _ => "DIRECT",
    };

    private static string BuildGlyphStream(IEnumerable<int> values)
    {
        var builder = new StringBuilder();
        foreach (var value in values.Take(6))
        {
            builder.Append((char)('A' + (Math.Abs(value) % 26)));
        }

        return builder.Length == 0 ? "LOCK" : builder.ToString();
    }

    private static string BuildGlyphStreamFromKeys(IEnumerable<string> keys, IReadOnlyDictionary<string, int> values)
    {
        var builder = new StringBuilder();
        foreach (var key in keys.Take(6))
        {
            if (!values.TryGetValue(key, out var value))
            {
                continue;
            }

            builder.Append((char)('A' + (Math.Abs(value) % 26)));
        }

        return builder.Length == 0 ? "LOCK" : builder.ToString();
    }

    private static string BuildGlyphMask(int count) => new string('*', Math.Max(3, Math.Min(8, count)));

    private int SumCurrentValues() => _current.Values.Sum();

    private int SumTargetValues() => _target.Values.Sum();

    private static int GetCircularDialDistance(int current, int target, int max)
    {
        var span = Math.Max(1, max + 1);
        var raw = Math.Abs(current - target);
        return Math.Min(raw, span - raw);
    }

    private static (int rows, int cols) GetPagedGridDimensions(int visibleCount) => visibleCount switch
    {
        <= 4 => (2, 2),
        <= 6 => (2, 3),
        <= 9 => (3, 3),
        <= 12 => (3, 4),
        <= 16 => (4, 4),
        <= 20 => (4, 5),
        _ => ((int)Math.Ceiling(Math.Sqrt(visibleCount)), (int)Math.Ceiling(Math.Sqrt(visibleCount))),
    };

    private int GetMemoryColumnCount() => _memoryValues.Count switch
    {
        <= 8 => 4,
        <= 12 => 4,
        <= 16 => 4,
        <= 20 => 5,
        _ => 6,
    };

    private static string GetMemoryGlyph(int pairValue) =>
        MemoryGlyphs[Math.Abs(pairValue) % MemoryGlyphs.Length];

    private static string BuildMemoryFragmentLabel(int index, int pairValue) =>
        $"{GetMemoryGlyph(pairValue)}-{(index + 1).ToString("00", CultureInfo.InvariantCulture)}";

    private static (int row, int col) ParsePipeKey(string key)
    {
        if (!key.StartsWith("r", StringComparison.OrdinalIgnoreCase))
        {
            return (0, 0);
        }

        var separatorIndex = key.IndexOf('c');
        if (separatorIndex <= 1 || separatorIndex >= key.Length - 1)
        {
            return (0, 0);
        }

        return int.TryParse(key.AsSpan(1, separatorIndex - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var row) &&
               int.TryParse(key.AsSpan(separatorIndex + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var col)
            ? (row, col)
            : (0, 0);
    }

    private static int RotatePipeMaskForVisuals(int mask, int turnsClockwise)
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
}
