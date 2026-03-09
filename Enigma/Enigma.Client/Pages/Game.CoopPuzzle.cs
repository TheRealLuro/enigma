using System.Text.Json;
using Enigma.Client.Models.Gameplay;

namespace Enigma.Client.Pages;

public partial class Game
{
    private sealed record CoopStageElement(double X, double Y, double Width, double Height, string Label, string State, bool IsTarget);
    private sealed record CoopRotationTile(int Id, int Row, int Col, int Rotation, int Target, int DisplayRotation, bool Controllable);
    private sealed record CoopNamedControl(int Index, string Label, string Detail);
    private sealed record CoopSignalNode(int Index, string Label, double X, double Y, bool Controllable, int? RouteTo);
    private sealed record CoopSignalLine(double StartX, double StartY, double EndX, double EndY, string CssClass);
    private sealed record CoopV2HudItem(string Label, string Value, string Icon, string Tone);
    private sealed record CoopV2ActionItem(string Command, string Label, string Icon, string Tone, bool Enabled, bool Active);

    protected bool HasCoopPuzzle => IsCoopRun && CurrentCoopPuzzle is not null;
    protected bool HasCoopStageElements => GetCoopStageElements().Count > 0;
    protected bool IsCoopPuzzleV2 => GetCoopViewInt("schema_version", 0) >= 2;
    protected bool HasCoopV2Board() =>
        TryGetCoopViewProperty("board", out var board) && board.ValueKind == JsonValueKind.Object;

    private bool IsGuestPerspective =>
        string.Equals(CurrentCoopPuzzle?.Role, "guest", StringComparison.OrdinalIgnoreCase);

    private string GetCoopRoleAccent(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return "Blue";
        }

        return string.Equals(role, IsGuestPerspective ? "guest" : "owner", StringComparison.OrdinalIgnoreCase) ? "Blue" : "Red";
    }

    private PuzzleGuide GetCurrentCoopPuzzleGuide()
    {
        return CurrentCoopPuzzle?.ViewType switch
        {
            "pressure_systems" => new(
                "Lock the shared plate pattern together.",
                "Move both explorers onto the correct plates and keep them there for the full hold. Blue marks your side and red marks your partner.",
                "Every required plate phase locks in sequence."),
            "sync_reaction" => new(
                "Lock both explorers under the timing rule for this room.",
                "Use the timing information on the blue side and lock only when your lane is valid. The red lane belongs to your partner.",
                "Both explorers are locked in a valid state at the same time."),
            "deduction_riddle" => new(
                "Combine both explorers' clues into one consistent answer.",
                "Share the prompt, clue fragments, and options with your partner, then commit to the same answer. Blue marks your side and red marks theirs.",
                "The selected answer matches the combined clue set."),
            "split_memory" => new(
                "Rebuild one full sequence from split information.",
                "Memorize the blue-side symbols, wait for the next turn callout, and enter one symbol at a time. The red side belongs to your partner.",
                "The team enters the full shared sequence without a mistake."),
            "dual_rotation" => new(
                "Align every shared tile to its target orientation.",
                "Rotate only the blue tiles. Red tiles are controlled by your partner.",
                "Every tile arrow matches its target arrow."),
            "opposing_pattern_input" => new(
                "Apply the hidden transformation to the shared pattern.",
                "Use the blue-side arrow controls and enter the transformed pattern, not the literal preview. Red belongs to your partner.",
                "Both explorers finish the correct transformed pattern."),
            "flow_transfer" => new(
                "Move shared flow into the exact target distribution.",
                "Pulse the blue controls. Every pulse changes the whole system, including the red side your partner is reading.",
                "Every output reaches its target value simultaneously."),
            "distributed_weight" => new(
                "Reach the combined weighted total together.",
                "Adjust only the blue pads and account for each multiplier before you add or remove weight. Red pads belong to your partner.",
                "The shared weighted total equals the target exactly."),
            "binary_echo" => new(
                "Transform the shared bit state into the target state.",
                "Use only the blue-side operations. Your partner sees a different red-side piece of the binary system.",
                "Current bits match target bits before the move budget or rule set rejects the attempt."),
            "signal_lines" => new(
                "Route every signal to the correct destination without violating blocked paths.",
                "Assign routes only for the blue source nodes. Compare notes with your partner because the red-side information is asymmetric.",
                "All signals form a valid non-conflicting network."),
            "spatial_sync" => new(
                "Stand in the correct zones together and hold them in sync.",
                "Move both explorers into the active zones at the same time and stay there for the required hold. Blue is your side and red is your partner.",
                "All sync steps lock in order."),
            _ => new(
                "Solve the co-op room together.",
                "Use the blue controls shown on your side of the board and communicate with your partner on the red side.",
                "The shared puzzle reports complete and the room unlocks.")
        };
    }

    private bool TryGetCoopViewProperty(string propertyName, out JsonElement value)
    {
        value = default;
        if (CurrentCoopPuzzle is null || CurrentCoopPuzzle.View.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return CurrentCoopPuzzle.View.TryGetProperty(propertyName, out value);
    }

    private bool TryGetCoopNestedViewProperty(string parentPropertyName, string childPropertyName, out JsonElement value)
    {
        value = default;
        if (!TryGetCoopViewProperty(parentPropertyName, out var parent) || parent.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return parent.TryGetProperty(childPropertyName, out value);
    }

    private string GetCoopViewString(string propertyName, string fallback = "")
    {
        return TryGetCoopViewProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    private int GetCoopViewInt(string propertyName, int fallback = 0)
    {
        return TryGetCoopViewProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed)
            ? parsed
            : fallback;
    }

    private double GetCoopViewDouble(string propertyName, double fallback = 0)
    {
        return TryGetCoopViewProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var parsed)
            ? parsed
            : fallback;
    }

    private bool GetCoopViewBool(string propertyName, bool fallback = false)
    {
        return TryGetCoopViewProperty(propertyName, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            ? value.GetBoolean()
            : fallback;
    }

    private IReadOnlyList<JsonElement> GetCoopViewArray(string propertyName)
    {
        if (!TryGetCoopViewProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray().ToArray();
    }

    protected string GetCoopV2FamilyId() => GetCoopViewString("family_id", "coop_v2");
    protected string GetCoopV2ShellClass()
    {
        var profile = GetCoopV2StageVisualProfile();
        return $"enigma-coop-v2-shell stage-{profile}";
    }

    protected string GetCoopV2StageVisualProfile()
    {
        if (TryGetCoopNestedViewProperty("panel", "stage_visual_profile", out var panelProfile) && panelProfile.ValueKind == JsonValueKind.String)
        {
            return panelProfile.GetString() ?? "intro";
        }

        return GetCoopViewString("stage_visual_profile", "intro");
    }

    protected string GetCoopV2FailureLabel()
    {
        if (TryGetCoopNestedViewProperty("panel", "failure_label", out var panelFailure) && panelFailure.ValueKind == JsonValueKind.String)
        {
            return panelFailure.GetString() ?? string.Empty;
        }

        return GetCoopViewString("failure_label");
    }

    protected string GetCoopV2RecoveryText()
    {
        if (TryGetCoopNestedViewProperty("panel", "recovery_text", out var panelRecovery) && panelRecovery.ValueKind == JsonValueKind.String)
        {
            return panelRecovery.GetString() ?? string.Empty;
        }

        return GetCoopViewString("recovery_text");
    }

    protected string GetCoopV2FailureVisualCue()
    {
        if (TryGetCoopNestedViewProperty("panel", "failure_visual_cue", out var panelCue) && panelCue.ValueKind == JsonValueKind.String)
        {
            return panelCue.GetString() ?? string.Empty;
        }

        return GetCoopViewString("failure_visual_cue");
    }

    protected string GetCoopV2ProgressLabel()
    {
        if (TryGetCoopNestedViewProperty("panel", "progress_label", out var panelLabel) && panelLabel.ValueKind == JsonValueKind.String)
        {
            return panelLabel.GetString() ?? "System Stability";
        }

        return GetCoopViewString("progress_label", "System Stability");
    }

    protected string GetCoopV2ProgressTrend()
    {
        if (TryGetCoopNestedViewProperty("panel", "progress_trend", out var panelTrend) && panelTrend.ValueKind == JsonValueKind.String)
        {
            return panelTrend.GetString() ?? "steady";
        }

        return GetCoopViewString("progress_trend", "steady");
    }

    protected string GetCoopV2ProgressTrendClass() => GetCoopV2ProgressTrend() switch
    {
        "up" => "trend-up",
        "down" => "trend-down",
        _ => "trend-steady",
    };

    protected string GetCoopV2StatusText()
    {
        if (TryGetCoopNestedViewProperty("panel", "status_text", out var panelStatus) && panelStatus.ValueKind == JsonValueKind.String)
        {
            return panelStatus.GetString() ?? string.Empty;
        }

        return GetCoopViewString("status_text", CurrentCoopPuzzle?.Status ?? string.Empty);
    }

    protected string GetCoopV2PhaseText() => GetCoopViewString("phase", "configure");
    protected string GetCoopV2PromptText()
    {
        if (TryGetCoopNestedViewProperty("panel", "prompt", out var panelPrompt) && panelPrompt.ValueKind == JsonValueKind.String)
        {
            return panelPrompt.GetString() ?? "Use your assigned controls.";
        }

        return GetCoopViewString("prompt", "Use your assigned controls.");
    }

    protected int GetCoopV2ProgressPercent()
    {
        double progressValue;
        if (TryGetCoopNestedViewProperty("panel", "progress_value", out var panelValue) && panelValue.ValueKind == JsonValueKind.Number && panelValue.TryGetDouble(out var parsedPanel))
        {
            progressValue = parsedPanel;
        }
        else
        {
            progressValue = GetCoopViewDouble("progress_value", GetCoopViewDouble("progress", 0d));
        }

        return Math.Clamp((int)Math.Round(progressValue * 100d), 0, 100);
    }

    private IReadOnlyList<CoopV2HudItem> GetCoopV2HudItems()
    {
        if (TryGetCoopNestedViewProperty("panel", "hud", out var panelHud) && panelHud.ValueKind == JsonValueKind.Array)
        {
            return panelHud
                .EnumerateArray()
                .Where(entry => entry.ValueKind == JsonValueKind.Object)
                .Select(entry => new CoopV2HudItem(
                    entry.TryGetProperty("label", out var label) ? label.GetString() ?? string.Empty : string.Empty,
                    entry.TryGetProperty("value", out var value) ? value.GetString() ?? string.Empty : string.Empty,
                    entry.TryGetProperty("icon", out var icon) ? icon.GetString() ?? "node" : "node",
                    entry.TryGetProperty("tone", out var tone) ? tone.GetString() ?? "shared" : "shared"))
                .ToArray();
        }

        return GetCoopViewArray("hud")
                .Where(entry => entry.ValueKind == JsonValueKind.Object)
                .Select(entry => new CoopV2HudItem(
                    entry.TryGetProperty("label", out var label) ? label.GetString() ?? string.Empty : string.Empty,
                    entry.TryGetProperty("value", out var value) ? value.GetString() ?? string.Empty : string.Empty,
                    entry.TryGetProperty("icon", out var icon) ? icon.GetString() ?? "node" : "node",
                    entry.TryGetProperty("tone", out var tone) ? tone.GetString() ?? "shared" : "shared"))
                .ToArray();
    }

    private IReadOnlyList<CoopV2ActionItem> GetCoopV2Actions()
    {
        if (TryGetCoopNestedViewProperty("panel", "actions", out var panelActions) && panelActions.ValueKind == JsonValueKind.Array)
        {
            return panelActions
                .EnumerateArray()
                .Where(entry => entry.ValueKind == JsonValueKind.Object)
                .Select(entry => new CoopV2ActionItem(
                    entry.TryGetProperty("cmd", out var command) ? command.GetString() ?? string.Empty : string.Empty,
                    entry.TryGetProperty("label", out var label) ? label.GetString() ?? string.Empty : string.Empty,
                    entry.TryGetProperty("icon", out var icon) ? icon.GetString() ?? "node" : "node",
                    entry.TryGetProperty("tone", out var tone) ? tone.GetString() ?? "shared" : "shared",
                    !entry.TryGetProperty("enabled", out var enabled) || enabled.ValueKind != JsonValueKind.False,
                    entry.TryGetProperty("active", out var active) && active.ValueKind == JsonValueKind.True))
                .Where(item => !string.IsNullOrWhiteSpace(item.Command))
                .ToArray();
        }

        return GetCoopViewArray("actions")
                .Where(entry => entry.ValueKind == JsonValueKind.Object)
                .Select(entry => new CoopV2ActionItem(
                    entry.TryGetProperty("cmd", out var command) ? command.GetString() ?? string.Empty : string.Empty,
                    entry.TryGetProperty("label", out var label) ? label.GetString() ?? string.Empty : string.Empty,
                    entry.TryGetProperty("icon", out var icon) ? icon.GetString() ?? "node" : "node",
                    entry.TryGetProperty("tone", out var tone) ? tone.GetString() ?? "shared" : "shared",
                    !entry.TryGetProperty("enabled", out var enabled) || enabled.ValueKind != JsonValueKind.False,
                    entry.TryGetProperty("active", out var active) && active.ValueKind == JsonValueKind.True))
                .Where(item => !string.IsNullOrWhiteSpace(item.Command))
                .ToArray();
    }

    protected string GetCoopV2IconGlyph(string icon) => icon.ToLowerInvariant() switch
    {
        "dial" => "DL",
        "valve" => "VL",
        "vent" => "VT",
        "bridge" => "BR",
        "mirror" => "MR",
        "gate" => "GT",
        "pump" => "PM",
        "freq" => "FQ",
        "time" => "TM",
        "echo" => "EC",
        "strata" => "ST",
        "launch" => "LN",
        "lock" => "LK",
        "commit" => "CM",
        _ => "ND",
    };

    private string GetCoopV2ActionClass(CoopV2ActionItem action)
    {
        var classes = new List<string> { "enigma-coop-v2-action", $"tone-{action.Tone.ToLowerInvariant()}" };
        if (action.Active)
        {
            classes.Add("active");
        }

        if (!action.Enabled)
        {
            classes.Add("disabled");
        }

        return string.Join(" ", classes);
    }

    private IReadOnlyList<CoopStageElement> GetCoopStageElements()
    {
        if (TryGetCoopNestedViewProperty("stage", "elements", out var stageElements) && stageElements.ValueKind == JsonValueKind.Array)
        {
            return stageElements
                .EnumerateArray()
                .Select(element => new CoopStageElement(
                    element.GetProperty("x").GetDouble(),
                    element.GetProperty("y").GetDouble(),
                    element.GetProperty("width").GetDouble(),
                    element.GetProperty("height").GetDouble(),
                    element.TryGetProperty("label", out var label) ? label.GetString() ?? string.Empty : string.Empty,
                    element.TryGetProperty("state", out var state) ? state.GetString() ?? "idle" : "idle",
                    element.TryGetProperty("is_target", out var target) && target.ValueKind is JsonValueKind.True or JsonValueKind.False && target.GetBoolean()))
                .ToArray();
        }

        return GetCoopViewArray("stage_elements")
            .Select(element => new CoopStageElement(
                element.GetProperty("x").GetDouble(),
                element.GetProperty("y").GetDouble(),
                element.GetProperty("width").GetDouble(),
                element.GetProperty("height").GetDouble(),
                element.TryGetProperty("label", out var label) ? label.GetString() ?? string.Empty : string.Empty,
                element.TryGetProperty("state", out var state) ? state.GetString() ?? "idle" : "idle",
                element.TryGetProperty("is_target", out var target) && target.ValueKind is JsonValueKind.True or JsonValueKind.False && target.GetBoolean()))
            .ToArray();
    }

    private string GetCoopStageElementStyle(CoopStageElement element) =>
        GetRectStyle(new PlayAreaRect(element.X, element.Y, element.Width, element.Height));

    private string GetCoopStageElementClass(CoopStageElement element) =>
        $"enigma-coop-stage-target {element.State} {(element.IsTarget ? "target" : string.Empty)}";

    protected string GetCoopClueText() => IsCoopPuzzleV2 ? string.Empty : GetCoopViewString("clue");

    protected bool CoopShowPulse => GetCoopViewBool("show_pulse", true);
    protected bool CoopShowTarget => GetCoopViewBool("show_target", true);
    protected double CoopTargetStart => GetCoopViewDouble("target_start", 0.3d);
    protected double CoopTargetWidth => GetCoopViewDouble("target_width", 0.15d);
    protected bool CoopSelfLocked => GetCoopViewBool("locked_self");
    protected bool CoopPartnerLocked => GetCoopViewBool("locked_partner");

    protected double GetCoopPulseMeter()
    {
        if (!TryGetCoopViewProperty("pulse_started_at", out var startedAt) || startedAt.ValueKind != JsonValueKind.String)
        {
            return 0d;
        }

        var startedAtValue = startedAt.GetString();
        if (!DateTimeOffset.TryParse(startedAtValue, out var started))
        {
            return 0d;
        }

        var speed = GetCoopViewDouble("pulse_speed", 0.75d);
        var offset = GetCoopViewDouble("pulse_offset", 0.15d);
        var elapsed = Math.Max(0d, (DateTimeOffset.UtcNow - started).TotalSeconds);
        var value = (offset + (elapsed * speed)) % 2d;
        return value <= 1d ? value : 2d - value;
    }

    protected IReadOnlyList<string> GetCoopRiddleOptions() =>
        GetCoopViewArray("options")
            .Where(option => option.ValueKind == JsonValueKind.String)
            .Select(option => option.GetString() ?? string.Empty)
            .ToArray();

    protected string GetCoopRiddlePrompt() => GetCoopViewString("prompt");

    protected IReadOnlyList<string> GetCoopRiddleClues() =>
        GetCoopViewArray("clues")
            .Where(option => option.ValueKind == JsonValueKind.String)
            .Select(option => option.GetString() ?? string.Empty)
            .ToArray();

    protected int? GetCoopSelectedOption(string key)
    {
        if (!TryGetCoopViewProperty(key, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var parsed))
        {
            return null;
        }

        return parsed;
    }

    protected IReadOnlyList<string> GetCoopMemorySymbols() =>
        GetCoopViewArray("symbols")
            .Where(option => option.ValueKind == JsonValueKind.String)
            .Select(option => option.GetString() ?? string.Empty)
            .ToArray();

    protected IReadOnlyList<string> GetCoopMemoryVisibleSequence() =>
        GetCoopViewArray("visible_sequence")
            .Where(option => option.ValueKind == JsonValueKind.String)
            .Select(option => option.GetString() ?? string.Empty)
            .ToArray();

    protected IReadOnlyList<string> GetCoopMemoryInput() =>
        GetCoopViewArray("input")
            .Where(option => option.ValueKind == JsonValueKind.String)
            .Select(option => option.GetString() ?? string.Empty)
            .ToArray();

    protected string GetCoopMemoryNextRole() => GetCoopRoleAccent(GetCoopViewString("next_role"));

    private IReadOnlyList<CoopRotationTile> GetCoopRotationTiles()
    {
        return GetCoopViewArray("tiles")
            .Where(tile => tile.ValueKind == JsonValueKind.Object)
            .Select(tile => new CoopRotationTile(
                tile.GetProperty("id").GetInt32(),
                tile.GetProperty("row").GetInt32(),
                tile.GetProperty("col").GetInt32(),
                tile.GetProperty("rotation").GetInt32(),
                tile.GetProperty("target").GetInt32(),
                tile.GetProperty("display_rotation").GetInt32(),
                tile.GetProperty("controllable").GetBoolean()))
            .ToArray();
    }

    protected int GetCoopRotationRows() => GetCoopViewInt("rows", 2);
    protected int GetCoopRotationCols() => GetCoopViewInt("cols", 2);
    protected int GetCoopBoardRotation() => GetCoopViewInt("board_rotation", 0);

    private string GetCoopRotationTileClass(CoopRotationTile tile)
    {
        var classes = new List<string> { "enigma-tile-button", "enigma-rotation-tile" };
        classes.Add(tile.Controllable ? "co-op-tile-active" : "co-op-tile-locked");
        if (tile.Rotation == tile.Target)
        {
            classes.Add("aligned");
        }

        return string.Join(" ", classes);
    }

    protected string GetCoopPatternClue()
    {
        if (!TryGetCoopViewProperty("clue", out var clue))
        {
            return string.Empty;
        }

        if (clue.ValueKind == JsonValueKind.String)
        {
            return clue.GetString() ?? string.Empty;
        }

        if (clue.ValueKind == JsonValueKind.Array)
        {
            return string.Join(" -> ", clue.EnumerateArray().Select(item => item.GetString() ?? string.Empty));
        }

        return string.Empty;
    }

    protected IReadOnlyList<string> GetCoopPatternInput() =>
        GetCoopViewArray("input")
            .Where(option => option.ValueKind == JsonValueKind.String)
            .Select(option => option.GetString() ?? string.Empty)
            .ToArray();

    private IReadOnlyList<CoopNamedControl> GetCoopFlowControls()
    {
        return GetCoopViewArray("controls")
            .Select((control, index) => new CoopNamedControl(
                index,
                control.TryGetProperty("label", out var label) ? label.GetString() ?? $"Valve {index + 1}" : $"Valve {index + 1}",
                GetCoopFlowControlDetail(control)))
            .ToArray();
    }

    private string GetCoopFlowControlDetail(JsonElement control)
    {
        var role = IsGuestPerspective ? "guest" : "owner";
        var selfKey = role == "owner" ? "owner_delta" : "guest_delta";
        var partnerKey = role == "owner" ? "guest_delta" : "owner_delta";
        var selfEffects = FormatCoopDeltaList(control, selfKey);
        var partnerEffects = FormatCoopDeltaList(control, partnerKey);
        return $"Blue: {selfEffects} | Red: {partnerEffects}";
    }

    private static string FormatCoopDeltaList(JsonElement control, string propertyName)
    {
        if (!control.TryGetProperty(propertyName, out var deltaArray) || deltaArray.ValueKind != JsonValueKind.Array)
        {
            return "no change";
        }

        var segments = deltaArray
            .EnumerateArray()
            .Select((value, index) => new { Index = index + 1, Delta = value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed) ? parsed : 0 })
            .Where(entry => entry.Delta != 0)
            .Select(entry => $"O{entry.Index} {(entry.Delta > 0 ? "+" : string.Empty)}{entry.Delta}")
            .ToArray();

        return segments.Length == 0 ? "no change" : string.Join(", ", segments);
    }

    protected string GetCoopWeightRuleText()
    {
        if (GetCoopIntList("allocations").Count < 4)
        {
            return "Only the blue pads can be changed from this panel.";
        }

        return IsGuestPerspective
            ? "Cross-link rule: blue pads 1 and 3 each add +1 to the shared total, while red pads 2 and 4 each subtract 1."
            : "Cross-link rule: red pads 1 and 3 each add +1 to the shared total, while blue pads 2 and 4 each subtract 1.";
    }

    protected IReadOnlyList<int> GetCoopIntList(string propertyName) =>
        GetCoopViewArray(propertyName)
            .Where(value => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _))
            .Select(value => value.GetInt32())
            .ToArray();

    protected IReadOnlyList<int?> GetCoopNullableIntList(string propertyName) =>
        GetCoopViewArray(propertyName)
            .Select(value => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed) ? parsed : (int?)null)
            .ToArray();

    protected IReadOnlyList<bool> GetCoopBoolList(string propertyName) =>
        GetCoopViewArray(propertyName)
            .Where(value => value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            .Select(value => value.GetBoolean())
            .ToArray();

    protected IReadOnlyList<string> GetCoopStringList(string propertyName) =>
        GetCoopViewArray(propertyName)
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString() ?? string.Empty)
            .ToArray();

    private IReadOnlyList<CoopSignalNode> GetCoopSignalNodes(bool isLeft)
    {
        var labels = isLeft ? GetCoopStringList("left_nodes") : GetCoopStringList("right_nodes");
        var routes = GetCoopNullableIntList("routes");
        var controls = GetCoopIntList("controls").ToHashSet();
        if (labels.Count == 0)
        {
            return [];
        }

        var spacing = labels.Count == 1 ? 0d : 80d / (labels.Count - 1);
        return labels
            .Select((label, index) => new CoopSignalNode(
                index,
                label,
                isLeft ? 12d : 88d,
                10d + (index * spacing),
                isLeft && controls.Contains(index),
                isLeft && index < routes.Count ? routes[index] : null))
            .ToArray();
    }

    private IReadOnlyList<CoopSignalLine> GetCoopSignalLines()
    {
        var leftNodes = GetCoopSignalNodes(isLeft: true);
        var rightNodes = GetCoopSignalNodes(isLeft: false);
        var lines = new List<CoopSignalLine>();

        foreach (var pair in GetCoopViewArray("blocked_routes")
                     .Where(value => value.ValueKind == JsonValueKind.Array)
                     .Select(value => value.EnumerateArray().Select(entry => entry.GetInt32()).ToArray())
                     .Where(pair => pair.Length == 2))
        {
            if (pair[0] >= leftNodes.Count || pair[1] >= rightNodes.Count)
            {
                continue;
            }

            lines.Add(new CoopSignalLine(
                leftNodes[pair[0]].X,
                leftNodes[pair[0]].Y,
                rightNodes[pair[1]].X,
                rightNodes[pair[1]].Y,
                "blocked"));
        }

        foreach (var leftNode in leftNodes)
        {
            if (leftNode.RouteTo is null || leftNode.RouteTo.Value < 0 || leftNode.RouteTo.Value >= rightNodes.Count)
            {
                continue;
            }

            lines.Add(new CoopSignalLine(
                leftNode.X,
                leftNode.Y,
                rightNodes[leftNode.RouteTo.Value].X,
                rightNodes[leftNode.RouteTo.Value].Y,
                leftNode.Controllable ? "active controllable" : "active partner"));
        }

        return lines;
    }

    protected int? GetCoopSignalSelectedRoute(int leftIndex)
    {
        var routes = GetCoopNullableIntList("routes");
        if (leftIndex < 0 || leftIndex >= routes.Count)
        {
            return null;
        }

        return routes[leftIndex];
    }

    protected string GetRotationGlyph(int rotation) => rotation switch
    {
        0 => "Up",
        1 => "Right",
        2 => "Down",
        3 => "Left",
        _ => "Open",
    };

    protected async Task HandleCoopPuzzleKeyChangeAsync(string keyCode, bool isPressed)
    {
        if (!isPressed || CurrentCoopPuzzle is null)
        {
            if (!isPressed)
            {
                _pressedKeys.Remove(keyCode);
            }

            return;
        }

        if (IsPuzzleOverlayOpen &&
            string.Equals(CurrentCoopPuzzle.ViewType, "opposing_pattern_input", StringComparison.OrdinalIgnoreCase) &&
            TryMapDirectionKey(keyCode, out var direction))
        {
            await SubmitCoopPuzzleActionAsync("press_direction", new { direction = direction.ToString() });
            return;
        }

        _pressedKeys.Add(keyCode);
    }

    private async Task SubmitCoopPuzzleActionAsync(string action, object? args = null)
    {
        if (_coopPuzzleActionInFlight || string.IsNullOrWhiteSpace(CoopSessionId))
        {
            return;
        }

        if (IsCoopSocketOpen)
        {
            _coopPuzzleActionInFlight = true;
            var sent = await JS.InvokeAsync<bool>("enigmaGame.sendCoopSocketMessage", new object?[]
            {
                new
                {
                    type = "puzzle_action",
                    action,
                    args = args ?? new { },
                }
            });

            if (sent)
            {
                return;
            }

            _coopPuzzleActionInFlight = false;
        }

        _coopPuzzleActionInFlight = true;
        try
        {
            using var response = await Api.PostJsonAsync("api/auth/multiplayer/session/puzzle_action", new
            {
                sessionId = CoopSessionId,
                action,
                args = args ?? new { },
            });
            var payload = await Api.ReadJsonAsync<MultiplayerSessionEnvelope>(response);
            if (response.IsSuccessStatusCode && payload?.Session is not null)
            {
                ApplyCoopSession(payload.Session);
                _playerStateDirty = true;
                return;
            }

            var raw = await response.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                ShowBanner(raw, 1.2d);
            }
        }
        finally
        {
            _coopPuzzleActionInFlight = false;
        }
    }

    protected Task LockCoopReactionAsync() => SubmitCoopPuzzleActionAsync("lock");
    protected Task SelectCoopRiddleOptionAsync(int index) => SubmitCoopPuzzleActionAsync("select_option", new { index });
    protected Task PressCoopMemorySymbolAsync(string symbol) => SubmitCoopPuzzleActionAsync("press_symbol", new { symbol });
    protected Task RotateCoopTileAsync(int tileId) => SubmitCoopPuzzleActionAsync("rotate_tile", new { tileId });
    protected Task PressCoopDirectionAsync(PlayerDirection direction) => SubmitCoopPuzzleActionAsync("press_direction", new { direction = direction.ToString() });
    protected Task PulseCoopFlowAsync(int index) => SubmitCoopPuzzleActionAsync("pulse_flow", new { index });
    protected Task AdjustCoopWeightAsync(int index, int delta) => SubmitCoopPuzzleActionAsync("adjust_weight", new { index, delta });
    protected Task ToggleCoopBitAsync(int index) => SubmitCoopPuzzleActionAsync("toggle_bit", new { index });
    protected Task ApplyCoopBinaryOperationAsync(string operation) => SubmitCoopPuzzleActionAsync("binary_operation", new { operation });
    protected Task RouteCoopSignalAsync(int leftIndex, int rightIndex) => SubmitCoopPuzzleActionAsync("route_signal", new { leftIndex, rightIndex });
    protected Task TriggerCoopV2ActionAsync(string command) => SubmitCoopPuzzleActionAsync("v2_action", new { cmd = command });
}
