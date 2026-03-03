using System.Text.Json;
using Enigma.Client.Models.Gameplay;

namespace Enigma.Client.Pages;

public partial class Game
{
    private sealed record CoopStageElement(double X, double Y, double Width, double Height, string Label, string State, bool IsTarget);
    private sealed record CoopRotationTile(int Id, int Row, int Col, int Rotation, int Target, int DisplayRotation, bool Controllable);
    private sealed record CoopNamedControl(int Index, string Label);
    private sealed record CoopSignalNode(int Index, string Label, double X, double Y, bool Controllable, int? RouteTo);
    private sealed record CoopSignalLine(double StartX, double StartY, double EndX, double EndY, string CssClass);

    protected bool HasCoopPuzzle => IsCoopRun && CurrentCoopPuzzle is not null;
    protected bool HasCoopStageElements => GetCoopStageElements().Count > 0;

    private bool TryGetCoopViewProperty(string propertyName, out JsonElement value)
    {
        value = default;
        if (CurrentCoopPuzzle is null || CurrentCoopPuzzle.View.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return CurrentCoopPuzzle.View.TryGetProperty(propertyName, out value);
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

    private IReadOnlyList<CoopStageElement> GetCoopStageElements()
    {
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

    protected string GetCoopClueText() => GetCoopViewString("clue");

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

    protected string GetCoopMemoryNextRole() => GetCoopViewString("next_role");

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
            .Select((control, index) => new CoopNamedControl(index, control.TryGetProperty("label", out var label) ? label.GetString() ?? $"Valve {index + 1}" : $"Valve {index + 1}"))
            .ToArray();
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

        if (string.Equals(CurrentCoopPuzzle.ViewType, "opposing_pattern_input", StringComparison.OrdinalIgnoreCase) &&
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
            using var response = await Api.PostJsonAsync("api/auth/multiplayer/session/puzzle/action", new
            {
                sessionId = CoopSessionId,
                action,
                args = args ?? new { },
            });
            var payload = await Api.ReadJsonAsync<MultiplayerSessionEnvelope>(response);
            if (response.IsSuccessStatusCode && payload?.Session is not null)
            {
                ApplyCoopSession(payload.Session, forcePosition: true);
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
}
