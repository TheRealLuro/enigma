using System.Diagnostics;
using System.Globalization;
using Enigma.Client.Models.Gameplay;
using Enigma.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;

namespace Enigma.Client.Pages;

public partial class Game : ComponentBase, IAsyncDisposable
{
    private sealed record PuzzleGuide(string Goal, string Controls, string Success);
    private sealed record RoomInteractionProgressState(PlayAreaRect AnchorRect, double Progress, string Label);
    private const double RoomSize = 1080d;
    private const double StageAspectRatio = 16d / 9d;
    private const double RenderWidth = RoomSize * StageAspectRatio;
    private const double PlayerSize = 60d;
    private const double PlayerSpeed = 380d;
    private const double DoorWidth = 240d;
    private const double WallThickness = 42d;
    private const double PuzzleConsoleSize = 116d;
    private static readonly PlayAreaRect[] PuzzleConsoleCandidates =
    [
        new(72d, 882d, PuzzleConsoleSize, PuzzleConsoleSize),
        new(72d, 482d, PuzzleConsoleSize, PuzzleConsoleSize),
        new(72d, 82d, PuzzleConsoleSize, PuzzleConsoleSize),
        new(892d, 82d, PuzzleConsoleSize, PuzzleConsoleSize),
        new(892d, 482d, PuzzleConsoleSize, PuzzleConsoleSize),
    ];
    private const string CompletionSummaryStorageKey = "enigma.game.summary";
    private static readonly TimeSpan PlayerStateSyncInterval = TimeSpan.FromMilliseconds(120);
    private static readonly string[] YarnStrandColors =
    [
        "#82c8ff",
        "#91f0d8",
        "#ffd173",
        "#ff9dbe",
        "#bca6ff",
        "#88e5ff",
        "#9cf28f",
        "#ffb58b",
    ];

    private readonly Stopwatch _sessionStopwatch = new();
    private readonly HashSet<string> _pressedKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<GridPoint, RoomRuntimeState> _roomStates = [];
    private DotNetObjectReference<Game>? _dotNetReference;
    private CancellationTokenSource? _loopCancellation;
    private Task? _loopTask;
    private DateTime _bannerExpiresAtUtc = DateTime.MinValue;
    private DateTime _lastPlayerStateSyncUtc = DateTime.MinValue;
    private string? _loadedSeed;
    private string _runNonce = Guid.NewGuid().ToString("N");
    private bool _completionTriggered;
    private bool _abandonTriggered;
    private bool _allowRouteExit;
    private bool _jsReady;
    private bool _playerStateDirty = true;
    private bool _roomInteractionWasActive;

    [Inject] protected NavigationManager NavigationManager { get; set; } = default!;
    [Inject] protected IJSRuntime JS { get; set; } = default!;
    [Inject] protected EnigmaApiClient Api { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "seed")]
    public string? Seed { get; set; }

    [SupplyParameterFromQuery(Name = "mapName")]
    public string? MapName { get; set; }

    [SupplyParameterFromQuery(Name = "source")]
    public string? Source { get; set; }

    [SupplyParameterFromQuery(Name = "tutorial")]
    public string? Tutorial { get; set; }

    protected bool UsePlaceholderGraphics { get; } = true;
    protected string GameSurfaceId { get; } = $"enigma-game-{Guid.NewGuid():N}";
    protected MazeSeedDefinition? ParsedSeed { get; private set; }
    protected MazeRoomDefinition? CurrentRoom { get; private set; }
    protected RoomRuntimeState? CurrentRoomState { get; private set; }
    protected bool IsLoaded { get; private set; }
    protected string? LoadError { get; private set; }
    protected string Username { get; private set; } = "Guest";
    protected string StatusBanner { get; private set; } = "Use WASD or the arrow keys to move between rooms.";
    protected double PlayerX { get; private set; }
    protected double PlayerY { get; private set; }
    protected PlayerDirection PlayerFacing { get; private set; } = PlayerDirection.Down;
    protected bool IsMoving { get; private set; }
    protected int TotalGold { get; private set; }
    protected List<RunLoadoutSelection> EquippedLoadout { get; private set; } = [];
    protected bool IsPuzzleOverlayOpen { get; private set; }

    protected IReadOnlyDictionary<char, string> RoomBackgrounds { get; } = new Dictionary<char, string>
    {
        ['A'] = "linear-gradient(145deg, #22344f 0%, #142033 100%)",
        ['B'] = "linear-gradient(145deg, #27413a 0%, #15231f 100%)",
        ['C'] = "linear-gradient(145deg, #3a2f4c 0%, #1a1628 100%)",
        ['D'] = "linear-gradient(145deg, #49372b 0%, #1f1610 100%)",
        ['E'] = "linear-gradient(145deg, #2f4547 0%, #182326 100%)",
        ['F'] = "linear-gradient(145deg, #4b2f3c 0%, #20131a 100%)",
        ['G'] = "linear-gradient(145deg, #405129 0%, #1a2110 100%)",
        ['H'] = "linear-gradient(145deg, #1f4153 0%, #0f202a 100%)",
        ['I'] = "linear-gradient(145deg, #3d304f 0%, #191424 100%)",
        ['J'] = "linear-gradient(145deg, #26494a 0%, #132021 100%)",
        ['K'] = "linear-gradient(145deg, #524a28 0%, #211d10 100%)",
        ['L'] = "linear-gradient(145deg, #42313c 0%, #1d151a 100%)",
        ['M'] = "linear-gradient(145deg, #2a4b36 0%, #142219 100%)",
        ['N'] = "linear-gradient(145deg, #4a3526 0%, #1c140e 100%)",
        ['O'] = "linear-gradient(145deg, #3d4f58 0%, #182026 100%)",
    };

    protected IReadOnlyDictionary<PlayerDirection, string> PlayerAnimationDirections { get; } = new Dictionary<PlayerDirection, string>
    {
        [PlayerDirection.Up] = "up",
        [PlayerDirection.Right] = "right",
        [PlayerDirection.Down] = "down",
        [PlayerDirection.Left] = "left",
    };

    protected IReadOnlyDictionary<PlayerDirection, string> PlayerSpriteStates { get; } = new Dictionary<PlayerDirection, string>
    {
        [PlayerDirection.Up] = "placeholder-up",
        [PlayerDirection.Right] = "placeholder-right",
        [PlayerDirection.Down] = "placeholder-down",
        [PlayerDirection.Left] = "placeholder-left",
    };

    protected int SolvedRoomCount => IsCoopRun
        ? _coopSession?.SolvedRoomCount ?? _roomStates.Values.Count(state => state.Puzzle.IsCompleted)
        : _roomStates.Values.Count(state => state.Puzzle.IsCompleted);
    protected int TotalRoomCount => _roomStates.Count;
    protected string DifficultyLabel => ParsedSeed?.Difficulty.ToString() ?? "Unknown";
    protected string RoomCoordinateLabel => CurrentRoom is null ? "(0, 0)" : CurrentRoom.Coordinates.ToString();
    protected string CurrentMapLabel => string.IsNullOrWhiteSpace(MapName) ? "UNKNOWN" : MapName!;
    protected string NormalizedSource => string.Equals(Source, "load", StringComparison.OrdinalIgnoreCase) ? "load" : "new";
    protected string ElapsedTimeLabel => FormatElapsed(_sessionStopwatch.Elapsed);
    protected bool CanShowRoom => ParsedSeed is not null && CurrentRoom is not null && CurrentRoomState is not null;
    protected bool HasPuzzleOverlayContent => IsCoopRun ? HasCoopPuzzle : CurrentRoomState?.Puzzle is not null;
    protected bool IsCurrentPuzzleSolved => IsCoopRun ? IsCurrentCoopRoomSolved : CurrentRoomState?.Puzzle.IsCompleted == true;
    protected bool UsesRoomNativePuzzleInteraction => !IsCoopRun && CurrentRoomState?.Puzzle is PressurePlatePuzzle
        or PhaseRelayPuzzle
        or HarmonicPhasePuzzle
        or WeightGridPuzzleBase
        or ZoneActivationPuzzle
        or DelayedActivationSequencePuzzle
        or RecursiveActivationSequencePuzzle
        or CrossingPathPuzzle;
    protected bool CanInteractWithPuzzleConsole => CanShowRoom && HasPuzzleOverlayContent && !IsCurrentPuzzleSolved && !UsesRoomNativePuzzleInteraction;
    protected bool PortalReady => IsCoopRun
        ? CurrentRoomState?.Definition.Kind == MazeRoomKind.Finish && IsCurrentCoopRoomSolved
        : CurrentRoomState?.FinishPortalVisible == true;
    protected bool IsTutorialRun =>
        string.Equals(Tutorial, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Tutorial, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Tutorial, "yes", StringComparison.OrdinalIgnoreCase);

    protected override void OnParametersSet()
    {
        if (string.IsNullOrWhiteSpace(Seed))
        {
            LoadError = "Open a generated or loaded seed from the Play page before starting the game.";
            IsLoaded = false;
            return;
        }

        if (string.Equals(_loadedSeed, Seed, StringComparison.Ordinal))
        {
            return;
        }

        ResetRuntimeState();
        ResetCoopRuntimeState();
        _runNonce = Guid.NewGuid().ToString("N");
        _abandonTriggered = false;
        _allowRouteExit = false;

        try
        {
            ParsedSeed = MazeSeedParser.Parse(Seed);
            foreach (var room in ParsedSeed.Rooms.Values)
            {
                _roomStates[room.Coordinates] = new RoomRuntimeState
                {
                    Definition = room,
                    Puzzle = PuzzleFactory.Create(ParsedSeed.RawSeed, room, ParsedSeed.Difficulty, _runNonce),
                    PuzzleGoldReward = PuzzleFactory.GetPuzzleReward(ParsedSeed.RawSeed, room, ParsedSeed.Difficulty),
                    RewardPickupGold = PuzzleFactory.GetRewardPickupBonus(ParsedSeed.RawSeed, room, ParsedSeed.Difficulty),
                    RewardPickupBounds = PuzzleFactory.CreateRewardPickupBounds(ParsedSeed.RawSeed, room),
                    FinishPortalBounds = PuzzleFactory.CreateFinishPortalBounds(),
                };
            }

            CurrentRoom = ParsedSeed.StartRoom;
            CurrentRoomState = _roomStates[CurrentRoom.Coordinates];
            CenterPlayer();
            _sessionStopwatch.Restart();
            IsLoaded = true;
            LoadError = null;
            _loadedSeed = Seed;
            StatusBanner = "Every room is locked until its puzzle is solved.";
            _playerStateDirty = true;
        }
        catch (Exception exception)
        {
            ParsedSeed = null;
            CurrentRoom = null;
            CurrentRoomState = null;
            IsLoaded = false;
            LoadError = exception.Message;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (string.IsNullOrWhiteSpace(Seed))
        {
            LoadError = "Open a generated or loaded seed from the Play page before starting the game.";
            await InvokeAsync(StateHasChanged);
            return;
        }

        if (_dotNetReference is null && IsLoaded)
        {
            _dotNetReference = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("enigmaGame.registerInput", _dotNetReference);
            await JS.InvokeVoidAsync("enigmaGame.focusElement", GameSurfaceId);
            await JS.InvokeVoidAsync("enigmaGame.sessionRemove", CompletionSummaryStorageKey);
            await JS.InvokeVoidAsync("enigmaGame.clearPendingLossSummary");

            var session = await Api.GetSessionAsync();
            if (!string.IsNullOrWhiteSpace(session?.Username))
            {
                Username = session!.Username;
            }

            var storedLoadout = await JS.InvokeAsync<List<RunLoadoutSelection>?>("enigmaGame.getRunLoadout");
            EquippedLoadout = (storedLoadout ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item.ItemId) && item.Quantity > 0)
                .ToList();

            try
            {
                await InitializeCoopAsync();
            }
            catch (Exception ex)
            {
                LoadError = ex.Message;
                IsLoaded = false;
                await InvokeAsync(StateHasChanged);
                return;
            }
            _jsReady = true;
            await ConnectCoopSocketAsync();
            if (!IsTutorialRun && !IsCoopRun)
            {
                await JS.InvokeVoidAsync("enigmaGame.registerLossUnload", Api.BuildUrl("api/auth/game/abandon"));
            }
            else if (!IsTutorialRun && IsCoopRun)
            {
                await JS.InvokeVoidAsync("enigmaGame.registerCoopLeaveUnload", Api.BuildUrl("api/auth/multiplayer/session/leave"), CoopSessionId, "page_unload");
            }

            if (!IsTutorialRun)
            {
                await UpdatePendingLossDraftAsync(force: true);
            }
            await SyncLivePlayerStateAsync(force: true);
            StartGameLoop();
            await InvokeAsync(StateHasChanged);
            return;
        }
    }

    [JSInvokable]
    public Task HandleKeyChange(string keyCode, bool isPressed)
    {
        if (isPressed && string.Equals(keyCode, "Escape", StringComparison.OrdinalIgnoreCase))
        {
            if (IsPuzzleOverlayOpen)
            {
                ClosePuzzleOverlay();
            }

            return Task.CompletedTask;
        }

        if (string.Equals(keyCode, "KeyE", StringComparison.OrdinalIgnoreCase))
        {
            if (isPressed && IsNearPuzzleConsole())
            {
                TogglePuzzleOverlay();
            }

            return Task.CompletedTask;
        }

        if (IsCoopRun)
        {
            return HandleCoopPuzzleKeyChangeAsync(keyCode, isPressed);
        }

        if (IsPuzzleOverlayOpen && CurrentRoomState?.Puzzle is UnlockPatternPuzzle unlockPattern && !unlockPattern.IsCompleted)
        {
            _pressedKeys.Clear();

            if (isPressed && TryMapDirectionKey(keyCode, out var patternDirection))
            {
                unlockPattern.Press(patternDirection);
            }

            return Task.CompletedTask;
        }

        if (IsPuzzleOverlayOpen && CurrentRoomState?.Puzzle is DirectionalEchoPuzzle directionalEcho && !directionalEcho.IsCompleted)
        {
            _pressedKeys.Clear();

            if (isPressed && TryMapDirectionKey(keyCode, out var echoDirection))
            {
                directionalEcho.Press(echoDirection);
            }

            return Task.CompletedTask;
        }

        if (IsPuzzleOverlayOpen && CurrentRoomState?.Puzzle is DimensionalPatternShiftPuzzle dimensionalShift && !dimensionalShift.IsCompleted)
        {
            _pressedKeys.Clear();

            if (isPressed && TryMapDirectionKey(keyCode, out var shiftDirection))
            {
                dimensionalShift.Press(shiftDirection);
            }

            return Task.CompletedTask;
        }

        if (isPressed)
        {
            _pressedKeys.Add(keyCode);
        }
        else
        {
            _pressedKeys.Remove(keyCode);
        }

        return Task.CompletedTask;
    }

    protected string GetPuzzleConsoleStyle() => GetRectStyle(GetCurrentPuzzleConsoleBounds());

    protected string GetPuzzleConsolePromptStyle()
    {
        var consoleBounds = GetCurrentPuzzleConsoleBounds();
        const double promptWidth = 344d;
        const double promptHeight = 42d;
        var left = Math.Clamp(consoleBounds.CenterX - (promptWidth / 2d), 22d, RoomSize - promptWidth - 22d);
        var top = consoleBounds.Y >= RoomSize / 2d
            ? consoleBounds.Y - promptHeight - 16d
            : consoleBounds.Bottom + 16d;
        top = Math.Clamp(top, 22d, RoomSize - promptHeight - 22d);
        return $"left: {ToPositionPercentX(left, promptWidth)}%; top: {ToPercentY(top)}%;";
    }

    protected bool IsNearPuzzleConsole()
    {
        if (!CanInteractWithPuzzleConsole)
        {
            return false;
        }

        var playerCenterX = PlayerX + (PlayerSize / 2d);
        var playerCenterY = PlayerY + (PlayerSize / 2d);
        var consoleBounds = GetCurrentPuzzleConsoleBounds();
        var consoleCenterX = consoleBounds.CenterX;
        var consoleCenterY = consoleBounds.CenterY;
        var distance = Math.Sqrt(Math.Pow(playerCenterX - consoleCenterX, 2d) + Math.Pow(playerCenterY - consoleCenterY, 2d));
        return distance <= 165d;
    }

    protected bool ShowRoomInteractionProgress => TryGetRoomInteractionProgressState(out _);

    protected string GetRoomInteractionProgressStyle()
    {
        if (!TryGetRoomInteractionProgressState(out var progressState))
        {
            return string.Empty;
        }

        var width = Math.Clamp(Math.Max(progressState.AnchorRect.Width + 48d, 180d), 180d, 260d);
        var left = Math.Clamp(progressState.AnchorRect.CenterX - (width / 2d), 22d, RoomSize - width - 22d);
        var height = 56d;
        var top = progressState.AnchorRect.Y >= RoomSize / 2d
            ? progressState.AnchorRect.Y - height - 18d
            : progressState.AnchorRect.Bottom + 18d;
        top = Math.Clamp(top, 22d, RoomSize - height - 22d);
        return $"left: {ToPositionPercentX(left, width)}%; top: {ToPercentY(top)}%; width: {ToLengthPercentX(width)}%;";
    }

    protected string GetRoomInteractionProgressFillStyle()
    {
        if (!TryGetRoomInteractionProgressState(out var progressState))
        {
            return "width: 0%;";
        }

        return $"width: {Math.Round(Math.Clamp(progressState.Progress, 0d, 1d) * 100d, 2)}%;";
    }

    protected string GetRoomInteractionProgressLabel() =>
        TryGetRoomInteractionProgressState(out var progressState) ? progressState.Label : string.Empty;

    protected string MovementHintText => CanInteractWithPuzzleConsole
        ? "Move with WASD or the arrow keys. Walk to the console and press E to work the room puzzle."
        : UsesRoomNativePuzzleInteraction
            ? "Move with WASD or the arrow keys. Solve this room by stepping onto or moving the active room elements."
            : IsCoopRun
                ? "Move with WASD or the arrow keys. Both players must vote the same doorway to change rooms."
                : "Move with WASD or the arrow keys.";

    protected void TogglePuzzleOverlay()
    {
        if (IsPuzzleOverlayOpen)
        {
            ClosePuzzleOverlay();
        }
        else
        {
            OpenPuzzleOverlay();
        }
    }

    protected void OpenPuzzleOverlay()
    {
        if (!CanInteractWithPuzzleConsole)
        {
            return;
        }

        if (CurrentRoomState?.Puzzle is IRevealOnOpenPuzzle revealPuzzle)
        {
            revealPuzzle.BeginReveal();
        }

        IsPuzzleOverlayOpen = true;
        IsMoving = false;
        _pressedKeys.Clear();
        ShowBanner("Puzzle interface active. Press E or Escape to close.", 1.0d);
    }

    protected void ClosePuzzleOverlay()
    {
        if (!IsPuzzleOverlayOpen)
        {
            return;
        }

        IsPuzzleOverlayOpen = false;
        IsMoving = false;
        _pressedKeys.Clear();
    }

    protected string GetRoomStageStyle()
    {
        var background = CurrentRoom is not null && RoomBackgrounds.TryGetValue(CurrentRoom.ConnectionKey, out var style)
            ? style
            : "linear-gradient(145deg, #293140 0%, #151b26 100%)";

        return $"background: {background};";
    }

    protected string GetPlayerStyle() =>
        $"left: {ToPositionPercentX(PlayerX, PlayerSize)}%; top: {ToPercentY(PlayerY)}%; width: {ToLengthPercentX(PlayerSize)}%; height: {ToPercentY(PlayerSize)}%;";

    protected string GetPlayerClass() =>
        $"facing-{PlayerAnimationDirections[PlayerFacing]} {PlayerSpriteStates[PlayerFacing]} {(IsMoving ? "is-moving" : string.Empty)}";

    protected string GetRectStyle(PlayAreaRect rect) =>
        $"left: {ToPositionPercentX(rect.X, rect.Width)}%; top: {ToPercentY(rect.Y)}%; width: {ToLengthPercentX(rect.Width)}%; height: {ToPercentY(rect.Height)}%;";

    protected IEnumerable<(double X1, double Y1, double X2, double Y2)> GetYarnLineSegments(YarnUntanglePuzzle puzzle)
    {
        if (puzzle.StrandOrder.Length == 0)
        {
            yield break;
        }

        for (var index = 0; index < puzzle.StrandOrder.Length; index++)
        {
            yield return (14d, GetYarnNodeCenter(index, puzzle.StrandOrder.Length), 86d, GetYarnNodeCenter(puzzle.GetRightSlotForLeftIndex(index), puzzle.StrandOrder.Length));
        }
    }

    protected string GetYarnPathData(int leftIndex, int totalCount, int rightSlotIndex)
    {
        var startX = 18d;
        var endX = 82d;
        var startY = GetYarnNodeCenter(leftIndex, totalCount);
        var endY = GetYarnNodeCenter(rightSlotIndex, totalCount);
        var controlLeftX = 38d;
        var controlRightX = 62d;

        return FormattableString.Invariant(
            $"M {startX:F2} {startY:F2} C {controlLeftX:F2} {startY:F2}, {controlRightX:F2} {endY:F2}, {endX:F2} {endY:F2}");
    }

    protected string GetYarnLineClass(YarnUntanglePuzzle puzzle, int leftIndex)
    {
        var classes = new List<string> { "enigma-yarn-line" };
        if (HasYarnCrossing(puzzle, leftIndex))
        {
            classes.Add("crossed");
        }

        if (puzzle.SelectedIndex == leftIndex)
        {
            classes.Add("selected");
        }

        return string.Join(" ", classes);
    }

    protected string GetYarnLineStyle(YarnUntanglePuzzle puzzle, int leftIndex)
    {
        var color = YarnStrandColors[leftIndex % YarnStrandColors.Length];
        var opacity = puzzle.SelectedIndex == leftIndex ? 1d : HasYarnCrossing(puzzle, leftIndex) ? 0.96d : 0.82d;
        return FormattableString.Invariant($"stroke: {color}; opacity: {opacity:F2};");
    }

    protected int GetLeftIndexForRightSlot(YarnUntanglePuzzle puzzle, int rightSlotIndex)
    {
        for (var leftIndex = 0; leftIndex < puzzle.StrandOrder.Length; leftIndex++)
        {
            if (puzzle.GetRightSlotForLeftIndex(leftIndex) == rightSlotIndex)
            {
                return leftIndex;
            }
        }

        return rightSlotIndex;
    }

    protected bool HasYarnCrossing(YarnUntanglePuzzle puzzle, int leftIndex)
    {
        if (leftIndex < 0 || leftIndex >= puzzle.StrandOrder.Length)
        {
            return false;
        }

        var targetOrder = puzzle.StrandOrder[leftIndex];
        for (var otherIndex = 0; otherIndex < puzzle.StrandOrder.Length; otherIndex++)
        {
            if (otherIndex == leftIndex)
            {
                continue;
            }

            var otherOrder = puzzle.StrandOrder[otherIndex];
            if ((leftIndex < otherIndex && targetOrder > otherOrder) ||
                (leftIndex > otherIndex && targetOrder < otherOrder))
            {
                return true;
            }
        }

        return false;
    }

    protected double GetYarnNodeTopPercent(int index, int totalCount)
    {
        var center = GetYarnNodeCenter(index, totalCount);
        return Math.Clamp(center - 6d, 0d, 94d);
    }

    private static double GetYarnNodeCenter(int index, int totalCount)
    {
        if (totalCount <= 1)
        {
            return 50d;
        }

        return 12d + ((76d / (totalCount - 1)) * index);
    }

    protected IEnumerable<WallSegment> GetWallSegments()
    {
        if (CurrentRoom is null)
        {
            yield break;
        }

        foreach (var segment in BuildWallSegments(CurrentRoom.Connections.North, isHorizontal: true, edge: 0d, before: 0d, after: RoomSize - WallThickness, fixedAxisValue: 0d, side: "north"))
        {
            yield return segment;
        }

        foreach (var segment in BuildWallSegments(CurrentRoom.Connections.South, isHorizontal: true, edge: RoomSize - WallThickness, before: 0d, after: RoomSize - WallThickness, fixedAxisValue: RoomSize - WallThickness, side: "south"))
        {
            yield return segment;
        }

        foreach (var segment in BuildWallSegments(CurrentRoom.Connections.West, isHorizontal: false, edge: 0d, before: 0d, after: RoomSize - WallThickness, fixedAxisValue: 0d, side: "west"))
        {
            yield return segment;
        }

        foreach (var segment in BuildWallSegments(CurrentRoom.Connections.East, isHorizontal: false, edge: RoomSize - WallThickness, before: 0d, after: RoomSize - WallThickness, fixedAxisValue: RoomSize - WallThickness, side: "east"))
        {
            yield return segment;
        }
    }

    protected void AttemptQuickTimeStop()
    {
        if (CurrentRoomState?.Puzzle is QuickTimePuzzle puzzle)
        {
            puzzle.AttemptStop();
        }
    }

    protected void ChooseRiddleAnswer(int index)
    {
        if (CurrentRoomState?.Puzzle is RiddlePuzzle puzzle)
        {
            puzzle.Choose(index);
        }
    }

    protected void PressSequenceRune(string symbol)
    {
        if (CurrentRoomState?.Puzzle is SequenceMemoryPuzzle puzzle)
        {
            puzzle.Press(symbol);
        }
    }

    protected void RotateTile(int index)
    {
        if (CurrentRoomState?.Puzzle is TileRotationPuzzle puzzle)
        {
            puzzle.Rotate(index);
        }
    }

    protected void RotateSignalTile(int index)
    {
        if (CurrentRoomState?.Puzzle is SignalRotationNetworkPuzzle puzzle)
        {
            puzzle.Rotate(index);
        }
    }

    protected void PressPatternDirection(PlayerDirection direction)
    {
        if (CurrentRoomState?.Puzzle is UnlockPatternPuzzle puzzle)
        {
            puzzle.Press(direction);
        }
    }

    protected void ToggleValve(int index)
    {
        if (CurrentRoomState?.Puzzle is ValveFlowPuzzle puzzle)
        {
            puzzle.Toggle(index);
        }
        else if (CurrentRoomState?.Puzzle is FlowRedistributionPuzzle redistribution)
        {
            redistribution.Pulse(index);
        }
        else if (CurrentRoomState?.Puzzle is ConservationNetworkPuzzle conservation)
        {
            conservation.Pulse(index);
        }
    }

    protected void ToggleWeight(int index)
    {
        if (CurrentRoomState?.Puzzle is WeightBalancePuzzle puzzle)
        {
            puzzle.Toggle(index);
        }
    }

    protected void ToggleXorInput(int index)
    {
        if (CurrentRoomState?.Puzzle is XorLogicPuzzle puzzle)
        {
            puzzle.Toggle(index);
        }
    }

    protected void SelectYarnStrand(int index)
    {
        if (CurrentRoomState?.Puzzle is YarnUntanglePuzzle puzzle)
        {
            puzzle.Select(index);
        }
        else if (CurrentRoomState?.Puzzle is KnotTopologyPuzzle knot)
        {
            knot.Select(index);
        }
    }

    protected void ToggleDualPulseA()
    {
        if (CurrentRoomState?.Puzzle is DualPulseLockPuzzle puzzle)
        {
            puzzle.ToggleMeterA();
        }
    }

    protected void ToggleDualPulseB()
    {
        if (CurrentRoomState?.Puzzle is DualPulseLockPuzzle puzzle)
        {
            puzzle.ToggleMeterB();
        }
    }

    protected void SelectSymbolLogicSymbol(string symbol)
    {
        if (CurrentRoomState?.Puzzle is SymbolLogicPuzzle puzzle)
        {
            puzzle.SelectSymbol(symbol);
        }
    }

    protected void ClearSymbolLogicSelection()
    {
        if (CurrentRoomState?.Puzzle is SymbolLogicPuzzle puzzle)
        {
            puzzle.ClearSelection();
        }
    }

    protected void PressEchoDirection(PlayerDirection direction)
    {
        if (CurrentRoomState?.Puzzle is DirectionalEchoPuzzle puzzle)
        {
            puzzle.Press(direction);
        }
        else if (CurrentRoomState?.Puzzle is DimensionalPatternShiftPuzzle dimensional)
        {
            dimensional.Press(direction);
        }
    }

    protected void RotateBinaryLeft()
    {
        if (CurrentRoomState?.Puzzle is BinaryShiftPuzzle puzzle)
        {
            puzzle.RotateLeft();
        }
        else if (CurrentRoomState?.Puzzle is BinaryTransformationPuzzle transformation)
        {
            transformation.RotateLeft();
        }
    }

    protected void RotateBinaryRight()
    {
        if (CurrentRoomState?.Puzzle is BinaryShiftPuzzle puzzle)
        {
            puzzle.RotateRight();
        }
        else if (CurrentRoomState?.Puzzle is BinaryTransformationPuzzle transformation)
        {
            transformation.RotateRight();
        }
    }

    protected void FlipBinaryCenter()
    {
        if (CurrentRoomState?.Puzzle is BinaryShiftPuzzle puzzle)
        {
            puzzle.FlipCenter();
        }
    }

    protected void SelectCrossingLeft(int index)
    {
        if (CurrentRoomState?.Puzzle is CrossingPathPuzzle puzzle)
        {
            puzzle.SelectLeft(index);
        }
    }

    protected void ConnectCrossingRight(int index)
    {
        if (CurrentRoomState?.Puzzle is CrossingPathPuzzle puzzle)
        {
            puzzle.ConnectRight(index);
        }
    }

    protected void ToggleTemporalRing(int index)
    {
        if (CurrentRoomState?.Puzzle is TemporalLockPuzzle puzzle)
        {
            puzzle.ToggleRing(index);
        }
    }

    protected void PullParadoxLever(int index)
    {
        if (CurrentRoomState?.Puzzle is LogicalParadoxPuzzle puzzle)
        {
            puzzle.PullLever(index);
        }
    }

    protected void PressInterferenceRune(string rune)
    {
        if (CurrentRoomState?.Puzzle is MemoryInterferencePuzzle puzzle)
        {
            puzzle.Press(rune);
        }
    }

    protected void RotateCipherRow(int row)
    {
        if (CurrentRoomState?.Puzzle is RotationalCipherGridPuzzle puzzle)
        {
            puzzle.RotateRow(row);
        }
    }

    protected void RotateCipherColumn(int column)
    {
        if (CurrentRoomState?.Puzzle is RotationalCipherGridPuzzle puzzle)
        {
            puzzle.RotateColumn(column);
        }
    }

    protected void InvertBinaryAll()
    {
        if (CurrentRoomState?.Puzzle is BinaryTransformationPuzzle puzzle)
        {
            puzzle.InvertAll();
        }
    }

    protected void FlipBinaryAlternate()
    {
        if (CurrentRoomState?.Puzzle is BinaryTransformationPuzzle puzzle)
        {
            puzzle.FlipAlternate();
        }
    }

    protected void ApplyBinaryXorMask()
    {
        if (CurrentRoomState?.Puzzle is BinaryTransformationPuzzle puzzle)
        {
            puzzle.ApplyXorMask();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _loopCancellation?.Cancel();
        if (_loopTask is not null)
        {
            try
            {
                await _loopTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_dotNetReference is not null)
        {
            try
            {
                await JS.InvokeVoidAsync("enigmaGame.disposeInput");
                await JS.InvokeVoidAsync("enigmaGame.disposeCoopSocket");
                await JS.InvokeVoidAsync("enigmaGame.clearLivePlayerState");
            }
            catch (JSDisconnectedException)
            {
            }

            _dotNetReference.Dispose();
        }

        _loopCancellation?.Dispose();
    }

    private void StartGameLoop()
    {
        if (_loopTask is not null || !IsLoaded)
        {
            return;
        }

        _loopCancellation = new CancellationTokenSource();
        _loopTask = RunGameLoopAsync(_loopCancellation.Token);
    }

    private async Task RunGameLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(16));
        var frameTimer = Stopwatch.StartNew();
        var last = frameTimer.Elapsed;

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var now = frameTimer.Elapsed;
            var deltaTime = (now - last).TotalSeconds;
            last = now;
            await InvokeAsync(() => AdvanceFrameAsync(deltaTime));
        }
    }

    private async Task AdvanceFrameAsync(double deltaTime)
    {
        if (!IsLoaded || ParsedSeed is null || CurrentRoom is null || CurrentRoomState is null)
        {
            return;
        }

        if (_bannerExpiresAtUtc != DateTime.MinValue && DateTime.UtcNow >= _bannerExpiresAtUtc)
        {
            StatusBanner = string.Empty;
            _bannerExpiresAtUtc = DateTime.MinValue;
        }

        UpdateMovement(deltaTime);

        if (!IsCoopRun)
        {
            var wasCurrentPuzzleCompleted = CurrentRoomState.Puzzle.IsCompleted;
            CurrentRoomState.Puzzle.Update(new PuzzleUpdateContext
            {
                PlayerBounds = new PlayAreaRect(PlayerX, PlayerY, PlayerSize, PlayerSize),
                DeltaTimeSeconds = deltaTime,
            });

            if (_jsReady && CurrentRoomState.Puzzle is HarmonicPhasePuzzle harmonicPhase)
            {
                var tone = harmonicPhase.ConsumePendingTone();
                if (tone.HasValue)
                {
                    await JS.InvokeVoidAsync("enigmaGame.playTone", tone.Value);
                }
            }

            if (CurrentRoomState.Puzzle.IsCompleted)
            {
                var reward = CurrentRoomState.GrantPuzzleReward();
                if (reward > 0)
                {
                    TotalGold += reward;
                    _playerStateDirty = true;
                    ShowBanner($"Puzzle solved. +{reward} MN", 1.5d);
                }
            }

            if (_jsReady && TryIsRoomInteractionActive())
            {
                if (!_roomInteractionWasActive)
                {
                    await JS.InvokeVoidAsync("enigmaGame.playDing", "engage");
                    _roomInteractionWasActive = true;
                }
            }
            else
            {
                _roomInteractionWasActive = false;
            }

            if (_jsReady && UsesRoomNativePuzzleInteraction && !wasCurrentPuzzleCompleted && CurrentRoomState.Puzzle.IsCompleted)
            {
                await JS.InvokeVoidAsync("enigmaGame.playDing", "success");
            }
        }
        else if (CurrentCoopPuzzle?.Completed == true && !IsCurrentCoopRoomSolved)
        {
            _playerStateDirty = true;
        }

        if (IsPuzzleOverlayOpen && IsCurrentPuzzleSolved)
        {
            ClosePuzzleOverlay();
        }

        if (CurrentRoomState.RewardPickupVisible &&
            CurrentRoomState.RewardPickupBounds.Intersects(new PlayAreaRect(PlayerX, PlayerY, PlayerSize, PlayerSize)))
        {
            if (IsCoopRun)
            {
                if (!IsCurrentCoopRewardCollected)
                {
                    CurrentRoomState.MarkRewardPickupCollectedForSync();
                    _playerStateDirty = true;
                    ShowBanner("Reward cache secured for the team.", 0.9d);
                }
            }
            else
            {
                var bonus = CurrentRoomState.CollectRewardPickup();
                if (bonus > 0)
                {
                    TotalGold += bonus;
                    _playerStateDirty = true;
                    ShowBanner($"Reward cache collected. +{bonus} MN", 1.5d);
                }
            }
        }

        if (!_completionTriggered &&
            CurrentRoomState.FinishPortalVisible &&
            CurrentRoomState.FinishPortalBounds.Intersects(new PlayAreaRect(PlayerX, PlayerY, PlayerSize, PlayerSize)))
        {
            if (!IsCoopRun)
            {
                _completionTriggered = true;
                _sessionStopwatch.Stop();
                _ = CompleteRunAsync();
            }
        }

        await PumpCoopAsync();
        await SyncLivePlayerStateAsync();
        StateHasChanged();
    }

    private async Task CompleteRunAsync()
    {
        if (ParsedSeed is null)
        {
            return;
        }

        var summary = new GameCompletionSummary
        {
            Seed = ParsedSeed.RawSeed,
            Username = Username,
            CompletionTime = FormatElapsed(_sessionStopwatch.Elapsed),
            GoldCollected = TotalGold,
            Source = NormalizedSource,
            LoadedMapName = MapName,
            Difficulty = ParsedSeed.Difficulty.ToString(),
            Size = ParsedSeed.Size,
            UsedItems = BuildSelectedItemIds(),
        };

        await JS.InvokeVoidAsync("enigmaGame.clearActiveGameSession");
        await JS.InvokeVoidAsync("enigmaGame.clearLivePlayerState");
        await JS.InvokeVoidAsync("enigmaGame.disposeInput");
        if (IsTutorialRun)
        {
            await InvokeAsync(() => NavigationManager.NavigateTo("/leaderboard", replace: true));
            return;
        }

        await JS.InvokeVoidAsync("enigmaGame.sessionSetJson", CompletionSummaryStorageKey, summary);
        await InvokeAsync(() => NavigationManager.NavigateTo("/gameend/win"));
    }

    private void UpdateMovement(double deltaTime)
    {
        var previousX = PlayerX;
        var previousY = PlayerY;
        var previousFacing = PlayerFacing;
        var previousMoving = IsMoving;
        var previousRoom = CurrentRoom?.Coordinates;

        if (IsPuzzleOverlayOpen)
        {
            IsMoving = false;
            if (previousMoving)
            {
                _playerStateDirty = true;
            }

            return;
        }

        var horizontal = (IsPressed("ArrowRight", "KeyD") ? 1 : 0) - (IsPressed("ArrowLeft", "KeyA") ? 1 : 0);
        var vertical = (IsPressed("ArrowDown", "KeyS") ? 1 : 0) - (IsPressed("ArrowUp", "KeyW") ? 1 : 0);

        if (horizontal == 0 && vertical == 0)
        {
            IsMoving = false;
            if (previousMoving)
            {
                _playerStateDirty = true;
            }
            return;
        }

        var vectorLength = Math.Sqrt((horizontal * horizontal) + (vertical * vertical));
        var deltaX = horizontal / vectorLength;
        var deltaY = vertical / vectorLength;

        if (Math.Abs(deltaX) >= Math.Abs(deltaY))
        {
            PlayerFacing = deltaX >= 0 ? PlayerDirection.Right : PlayerDirection.Left;
        }
        else
        {
            PlayerFacing = deltaY >= 0 ? PlayerDirection.Down : PlayerDirection.Up;
        }

        IsMoving = true;
        PlayerX += deltaX * PlayerSpeed * deltaTime;
        PlayerY += deltaY * PlayerSpeed * deltaTime;

        if (!IsCoopRun && CurrentRoomState?.Puzzle is IBlockPushPuzzle blockPushPuzzle)
        {
            var playerRect = new PlayAreaRect(PlayerX, PlayerY, PlayerSize, PlayerSize);
            if (blockPushPuzzle.BlocksPlayer(playerRect))
            {
                _ = blockPushPuzzle.TryPush(PlayerFacing, playerRect);
                playerRect = new PlayAreaRect(PlayerX, PlayerY, PlayerSize, PlayerSize);
                if (blockPushPuzzle.BlocksPlayer(playerRect))
                {
                    PlayerX = previousX;
                    PlayerY = previousY;
                }
            }
        }

        if (CanInteractWithPuzzleConsole && IntersectsPuzzleConsoleBase(new PlayAreaRect(PlayerX, PlayerY, PlayerSize, PlayerSize)))
        {
            PlayerX = previousX;
            PlayerY = previousY;
        }

        if (TryTransition(PlayerDirection.Left) ||
            TryTransition(PlayerDirection.Right) ||
            TryTransition(PlayerDirection.Up) ||
            TryTransition(PlayerDirection.Down))
        {
            MarkPlayerStateDirty(previousX, previousY, previousFacing, previousMoving, previousRoom);
            return;
        }

        ClampInsideRoomWalls();
        MarkPlayerStateDirty(previousX, previousY, previousFacing, previousMoving, previousRoom);
    }

    private bool TryTransition(PlayerDirection direction)
    {
        var crossing = direction switch
        {
            PlayerDirection.Left => PlayerX < 0d,
            PlayerDirection.Right => PlayerX > RoomSize - PlayerSize,
            PlayerDirection.Up => PlayerY < 0d,
            PlayerDirection.Down => PlayerY > RoomSize - PlayerSize,
            _ => false,
        };

        if (!crossing || CurrentRoom is null || CurrentRoomState is null || ParsedSeed is null)
        {
            return false;
        }

        if (!IsWithinDoorway(direction))
        {
            ClampToBoundary(direction);
            return true;
        }

        if (!CurrentRoom.Connections.HasDoor(direction))
        {
            ClampToBoundary(direction);
            ShowBanner("That wall is sealed.", 1.0d);
            return true;
        }

        var roomSolvedForExit = IsCoopRun ? IsCurrentCoopRoomSolved : CurrentRoomState.Puzzle.IsCompleted;
        if (!roomSolvedForExit)
        {
            ClampToBoundary(direction);
            ShowBanner(IsCoopRun ? "This room stays sealed until the shared solve syncs." : "Solve the room puzzle before leaving.", 1.0d);
            return true;
        }

        var nextPoint = direction switch
        {
            PlayerDirection.Up => new GridPoint(CurrentRoom.Coordinates.X, CurrentRoom.Coordinates.Y - 1),
            PlayerDirection.Right => new GridPoint(CurrentRoom.Coordinates.X + 1, CurrentRoom.Coordinates.Y),
            PlayerDirection.Down => new GridPoint(CurrentRoom.Coordinates.X, CurrentRoom.Coordinates.Y + 1),
            PlayerDirection.Left => new GridPoint(CurrentRoom.Coordinates.X - 1, CurrentRoom.Coordinates.Y),
            _ => CurrentRoom.Coordinates,
        };

        if (!ParsedSeed.TryGetRoom(nextPoint, out var nextRoom))
        {
            ClampToBoundary(direction);
            return true;
        }

        if (IsCoopRun)
        {
            ClampToBoundary(direction);
            _ = RequestCoopRoomMoveAsync(nextPoint);
            return true;
        }

        CurrentRoom = nextRoom;
        CurrentRoomState = _roomStates[nextRoom.Coordinates];
        _roomInteractionWasActive = false;
        ClosePuzzleOverlay();
        switch (direction)
        {
            case PlayerDirection.Up:
                PlayerY = RoomSize - PlayerSize - 6d;
                break;
            case PlayerDirection.Right:
                PlayerX = 6d;
                break;
            case PlayerDirection.Down:
                PlayerY = 6d;
                break;
            case PlayerDirection.Left:
                PlayerX = RoomSize - PlayerSize - 6d;
                break;
        }

        ClampInsideRoomWalls();
        ShowBanner($"Entered room {CurrentRoom.Coordinates}", 0.8d);
        return true;
    }

    private IEnumerable<WallSegment> BuildWallSegments(bool hasDoor, bool isHorizontal, double edge, double before, double after, double fixedAxisValue, string side)
    {
        if (!hasDoor)
        {
            yield return CreateWallSegment(isHorizontal, fixedAxisValue, 0d, RoomSize, side);
            yield break;
        }

        var doorStart = (RoomSize - DoorWidth) / 2d;
        var doorEnd = doorStart + DoorWidth;
        yield return CreateWallSegment(isHorizontal, fixedAxisValue, 0d, doorStart, side);
        yield return CreateWallSegment(isHorizontal, fixedAxisValue, doorEnd, RoomSize - doorEnd, side);
        yield return new WallSegment($"door-glow {side}", isHorizontal
            ? $"left: {ToMappedPercentX(doorStart)}%; top: {ToPercentY(edge)}%; width: {ToMappedSpanPercentX(DoorWidth)}%; height: {ToPercentY(WallThickness)}%;"
            : $"left: {ToPositionPercentX(edge, WallThickness)}%; top: {ToPercentY(doorStart)}%; width: {ToLengthPercentX(WallThickness)}%; height: {ToPercentY(DoorWidth)}%;");
    }

    private static WallSegment CreateWallSegment(bool isHorizontal, double fixedAxisValue, double start, double length, string side) =>
        isHorizontal
            ? new WallSegment(side, $"left: {ToMappedPercentX(start)}%; top: {ToPercentY(fixedAxisValue)}%; width: {ToMappedSpanPercentX(length)}%; height: {ToPercentY(WallThickness)}%;")
            : new WallSegment(side, $"left: {ToPositionPercentX(fixedAxisValue, WallThickness)}%; top: {ToPercentY(start)}%; width: {ToLengthPercentX(WallThickness)}%; height: {ToPercentY(length)}%;");

    private bool IsWithinDoorway(PlayerDirection direction)
    {
        var doorMin = (RoomSize - DoorWidth) / 2d;
        var doorMax = doorMin + DoorWidth;
        var centerX = PlayerX + (PlayerSize / 2d);
        var centerY = PlayerY + (PlayerSize / 2d);

        return direction switch
        {
            PlayerDirection.Up or PlayerDirection.Down => centerX >= doorMin && centerX <= doorMax,
            PlayerDirection.Left or PlayerDirection.Right => centerY >= doorMin && centerY <= doorMax,
            _ => false,
        };
    }

    private void ClampToBoundary(PlayerDirection direction)
    {
        switch (direction)
        {
            case PlayerDirection.Left:
                PlayerX = WallThickness;
                break;
            case PlayerDirection.Right:
                PlayerX = RoomSize - PlayerSize - WallThickness;
                break;
            case PlayerDirection.Up:
                PlayerY = WallThickness;
                break;
            case PlayerDirection.Down:
                PlayerY = RoomSize - PlayerSize - WallThickness;
                break;
        }
    }

    private void ClampInsideRoomWalls()
    {
        var minX = CanUseDoorBand(PlayerDirection.Left) ? 0d : WallThickness;
        var maxX = CanUseDoorBand(PlayerDirection.Right) ? RoomSize - PlayerSize : RoomSize - PlayerSize - WallThickness;
        var minY = CanUseDoorBand(PlayerDirection.Up) ? 0d : WallThickness;
        var maxY = CanUseDoorBand(PlayerDirection.Down) ? RoomSize - PlayerSize : RoomSize - PlayerSize - WallThickness;

        PlayerX = Math.Clamp(PlayerX, minX, maxX);
        PlayerY = Math.Clamp(PlayerY, minY, maxY);
    }

    private bool CanUseDoorBand(PlayerDirection direction) =>
        CurrentRoom is not null &&
        CurrentRoom.Connections.HasDoor(direction) &&
        IsWithinDoorway(direction);

    private void CenterPlayer()
    {
        PlayerX = (RoomSize - PlayerSize) / 2d;
        PlayerY = (RoomSize - PlayerSize) / 2d;
        PlayerFacing = PlayerDirection.Down;
        IsMoving = false;
        _playerStateDirty = true;
    }

    private bool IsPressed(params string[] keys) => keys.Any(_pressedKeys.Contains);

    private void ShowBanner(string message, double seconds)
    {
        StatusBanner = message;
        _bannerExpiresAtUtc = DateTime.UtcNow.AddSeconds(seconds);
    }

    private void ResetRuntimeState()
    {
        _roomStates.Clear();
        ParsedSeed = null;
        CurrentRoom = null;
        CurrentRoomState = null;
        LoadError = null;
        IsLoaded = false;
        TotalGold = 0;
        IsPuzzleOverlayOpen = false;
        _completionTriggered = false;
        _abandonTriggered = false;
        _allowRouteExit = false;
        _pressedKeys.Clear();
        _lastPlayerStateSyncUtc = DateTime.MinValue;
        _playerStateDirty = true;
        _roomInteractionWasActive = false;
    }

    private static string FormatElapsed(TimeSpan elapsed) =>
        $"{elapsed.Hours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}:{elapsed.Milliseconds:000}";

    private static bool TryMapDirectionKey(string keyCode, out PlayerDirection direction)
    {
        direction = keyCode switch
        {
            "ArrowUp" => PlayerDirection.Up,
            "ArrowRight" => PlayerDirection.Right,
            "ArrowDown" => PlayerDirection.Down,
            "ArrowLeft" => PlayerDirection.Left,
            _ => default,
        };

        return keyCode is "ArrowUp" or "ArrowRight" or "ArrowDown" or "ArrowLeft";
    }

    protected static double ToPointPercentX(double value) => Math.Round((Math.Clamp(value, 0d, RoomSize) / RoomSize) * 100d, 4);

    protected static double ToMappedPercentX(double value) => Math.Round((Math.Clamp(value, 0d, RoomSize) / RoomSize) * 100d, 4);

    protected static double ToMappedSpanPercentX(double value) => Math.Round((Math.Clamp(value, 0d, RoomSize) / RoomSize) * 100d, 4);

    protected static double ToPositionPercentX(double value, double width)
    {
        var clampedWidth = Math.Clamp(width, 0d, RenderWidth);
        var sourceRange = Math.Max(1d, RoomSize - clampedWidth);
        var targetRange = Math.Max(0d, RenderWidth - clampedWidth);
        var scaled = (Math.Clamp(value, 0d, Math.Max(0d, RoomSize - clampedWidth)) / sourceRange) * targetRange;
        return Math.Round((scaled / RenderWidth) * 100d, 4);
    }

    protected static double ToLengthPercentX(double value) => Math.Round((value / RenderWidth) * 100d, 4);

    protected static double ToPercentY(double value) => Math.Round((value / RoomSize) * 100d, 4);

    private bool TryGetRoomInteractionProgressState(out RoomInteractionProgressState progressState)
    {
        progressState = default!;
        if (CurrentRoomState?.Puzzle is null)
        {
            return false;
        }

        switch (CurrentRoomState.Puzzle)
        {
            case PressurePlatePuzzle pressurePlate when pressurePlate.Progress > 0d:
                progressState = new(pressurePlate.PlateBounds, pressurePlate.Progress, "Plate Charge");
                return true;
            case ZoneActivationPuzzle zoneActivation when zoneActivation.CurrentZoneIndex < zoneActivation.Zones.Count && zoneActivation.HoldProgress > 0d:
                progressState = new(
                    zoneActivation.Zones[zoneActivation.CurrentZoneIndex],
                    zoneActivation.HoldProgress,
                    $"Beacon {zoneActivation.CurrentZoneIndex + 1}/{zoneActivation.Zones.Count}");
                return true;
            case DelayedActivationSequencePuzzle delayed when !delayed.IsWaitingForNextZone && delayed.HoldProgress > 0d:
                progressState = new(
                    delayed.Zones[delayed.ActiveZoneIndex],
                    delayed.HoldProgress,
                    $"Zone {delayed.CurrentStepIndex + 1}/{delayed.Order.Count}");
                return true;
            case RecursiveActivationSequencePuzzle recursive when recursive.CurrentZoneIndex >= 0 && recursive.HoldProgress > 0d:
                progressState = new(
                    recursive.Zones[recursive.CurrentZoneIndex],
                    recursive.HoldProgress,
                    $"Zone {recursive.CompletedOrder.Count + 1}");
                return true;
            default:
                return false;
        }
    }

    private bool TryIsRoomInteractionActive()
    {
        if (CurrentRoomState?.Puzzle is null)
        {
            return false;
        }

        var playerBounds = new PlayAreaRect(PlayerX, PlayerY, PlayerSize, PlayerSize);
        return CurrentRoomState.Puzzle switch
        {
            PressurePlatePuzzle pressurePlate => pressurePlate.PlateBounds.Intersects(playerBounds),
            ZoneActivationPuzzle zoneActivation when zoneActivation.CurrentZoneIndex < zoneActivation.Zones.Count =>
                zoneActivation.Zones[zoneActivation.CurrentZoneIndex].Intersects(playerBounds),
            DelayedActivationSequencePuzzle delayed when !delayed.IsWaitingForNextZone =>
                delayed.Zones[delayed.ActiveZoneIndex].Intersects(playerBounds),
            RecursiveActivationSequencePuzzle recursive when recursive.CurrentZoneIndex >= 0 =>
                recursive.Zones[recursive.CurrentZoneIndex].Intersects(playerBounds),
            _ => false,
        };
    }

    private PlayAreaRect GetCurrentPuzzleConsoleBounds()
    {
        if (CurrentRoom is null || ParsedSeed is null)
        {
            return PuzzleConsoleCandidates[0];
        }

        var stableKey = $"{ParsedSeed.RawSeed}|{CurrentRoom.Coordinates.X}|{CurrentRoom.Coordinates.Y}|{CurrentRoom.ConnectionKey}|{CurrentRoom.PuzzleKey}";
        var candidateIndex = GetStableHash(stableKey) % PuzzleConsoleCandidates.Length;
        return PuzzleConsoleCandidates[candidateIndex];
    }

    private static PlayAreaRect GetPuzzleConsoleBaseBounds(PlayAreaRect consoleBounds) =>
        new(
            consoleBounds.X + (consoleBounds.Width * 0.2d),
            consoleBounds.Y + (consoleBounds.Height * 0.72d),
            consoleBounds.Width * 0.6d,
            consoleBounds.Height * 0.2d);

    private bool IntersectsPuzzleConsoleBase(PlayAreaRect playerBounds) =>
        GetPuzzleConsoleBaseBounds(GetCurrentPuzzleConsoleBounds()).Intersects(playerBounds);

    private static int GetStableHash(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in value)
            {
                hash ^= character;
                hash *= 16777619u;
            }

            return (int)(hash & 0x7FFFFFFF);
        }
    }

    protected static string GetDirectionArrow(PlayerDirection direction) => direction switch
    {
        PlayerDirection.Up => "\u2191",
        PlayerDirection.Right => "\u2192",
        PlayerDirection.Down => "\u2193",
        PlayerDirection.Left => "\u2190",
        _ => "?",
    };

protected static string GetBinaryString(IEnumerable<bool> bits) => string.Concat(bits.Select(bit => bit ? '1' : '0'));

    protected static string GetPipeGlyph(int mask) => mask switch
    {
        0b0101 => "\u2502",
        0b1010 => "\u2500",
        0b0011 => "\u2514",
        0b0110 => "\u250C",
        0b1100 => "\u2510",
        0b1001 => "\u2518",
        0b0111 => "\u251C",
        0b1110 => "\u252C",
        0b1101 => "\u2524",
        0b1011 => "\u2534",
        0b1111 => "\u253C",
        0b0001 => "\u2575",
        0b0010 => "\u2576",
        0b0100 => "\u2577",
        0b1000 => "\u2574",
        _ => "\u2022",
    };

protected static string GetTemporalRingStyle(TemporalLockPuzzle puzzle, int ringIndex)
    {
        var rotation = -(puzzle.Positions[ringIndex] * 45d);
        return $"transform: translate(-50%, -50%) rotate({Math.Round(rotation, 2)}deg);";
    }

    protected static string GetTemporalSigilStyle(int ringIndex, int symbolIndex, int symbolCount)
    {
        var radius = ringIndex switch
        {
            0 => 118d,
            1 => 84d,
            _ => 52d,
        };

        var angle = ((Math.PI * 2d) / symbolCount) * symbolIndex - (Math.PI / 2d);
        var x = Math.Cos(angle) * radius;
        var y = Math.Sin(angle) * radius;
        return $"left: calc(50% + {Math.Round(x, 2)}px); top: calc(50% + {Math.Round(y, 2)}px);";
    }

    protected string GetConnectionLineStyle(PlayAreaPoint start, PlayAreaPoint end)
    {
        var scaledStartX = (Math.Clamp(start.X, 0d, RoomSize) / RoomSize) * RenderWidth;
        var scaledEndX = (Math.Clamp(end.X, 0d, RoomSize) / RoomSize) * RenderWidth;
        var deltaX = scaledEndX - scaledStartX;
        var deltaY = end.Y - start.Y;
        var length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        var angle = Math.Atan2(deltaY, deltaX) * (180d / Math.PI);
        return $"left: {Math.Round((scaledStartX / RenderWidth) * 100d, 4)}%; top: {ToPercentY(start.Y)}%; width: {ToLengthPercentX(length)}%; transform: rotate({Math.Round(angle, 2)}deg);";
    }

    protected static string GetModifierLabel(RecursiveZoneModifier modifier) => modifier switch
    {
        RecursiveZoneModifier.ReverseRemaining => "Reverse",
        RecursiveZoneModifier.RotateRemaining => "Rotate",
        RecursiveZoneModifier.SwapExtremes => "Swap Ends",
        RecursiveZoneModifier.ExtendNextHold => "Long Hold",
        _ => "Shift",
    };

    private PuzzleGuide GetCurrentPuzzleGuide() => CurrentRoomState?.Puzzle switch
    {
        PressurePlatePuzzle => new(
            "Charge the plate until it fully locks.",
            "Stand on the glowing plate inside the room. Leaving early drains its progress.",
            "The plate reaches full charge and the progress bar fills completely."),
        PhaseRelayPuzzle => new(
            "Trigger the phase nodes in the hidden order.",
            "Watch the active node while it is visible, then walk to the nodes in that same sequence.",
            "Every node is stepped on in the correct order without touching a wrong one."),
        HarmonicPhasePuzzle => new(
            "Make every plate resonate at the same value.",
            "Stepping on a plate adds +2 to that plate and +1 to each adjacent plate.",
            "All displayed plate values are equal at the same time."),
        QuickTimePuzzle quickTime => new(
            "Stop the moving pulse inside the bright target window.",
            "Press Stop Pulse when the white marker overlaps the green band. Higher difficulties require a clean streak.",
            $"Land {quickTime.RequiredHits} clean hit{(quickTime.RequiredHits == 1 ? string.Empty : "s")} in a row."),
        DualPulseLockPuzzle => new(
            "Freeze both pulses inside their target windows at the same time.",
            "Use Pulse A and Pulse B to lock or release each track separately until both markers sit inside their green windows together.",
            "Both tracks are locked while each marker is inside its own target window."),
        TemporalLockPuzzle => new(
            "Freeze the three rings in a constellation that satisfies every clue.",
            "Let the rings spin, then lock or release each ring individually while reading the clue list as symbol relationships.",
            "All clue statements are true for the symbols currently shown on the outer, middle, and inner rings."),
        RiddlePuzzle => new(
            "Choose the only answer that fits the riddle.",
            "Read the question carefully and select one answer from the option list.",
            "The selected answer is the correct solution to the riddle."),
        SymbolLogicPuzzle => new(
            "Build the only symbol order that satisfies every logic rule.",
            "Read every statement, then place symbols left-to-right in the order strip. Clear Order resets the attempt.",
            "The final left-to-right order keeps every listed statement true."),
        LogicalParadoxPuzzle => new(
            "Pick the lever that matches the only consistent interpretation of the statue statements.",
            "Treat the statements as one logic set. Compare them before choosing a single lever.",
            "The chosen lever is the only one that does not create a contradiction."),
        SequenceMemoryPuzzle => new(
            "Repeat the rune sequence exactly from memory.",
            "Watch the runes first, then press the runes back in the same order after they disappear.",
            "Every rune is entered in the original sequence with no mistakes."),
        FadingPathMemoryPuzzle => new(
            "Walk the exact floor path after it fades.",
            "Watch the glowing cells on the room floor, then move your character along the same route from memory.",
            "You trace the full path without stepping off the correct route."),
        MemoryInterferencePuzzle => new(
            "Recover the true sequence and ignore the interference.",
            "Watch the original pattern, ignore the fake overlay, then enter the real answer once the display ends.",
            "The full correct sequence is entered from start to finish without an error."),
        TileRotationPuzzle => new(
            "Rotate every tile so all arrows point north.",
            "Click a tile to rotate its arrow by 90 degrees clockwise.",
            "Every tile arrow points up at the same time."),
        SignalRotationNetworkPuzzle => new(
            "Create one continuous live circuit from the start node to the end node.",
            "Rotate tiles until the pipe glyphs connect the upper-left start to the lower-right end.",
            "There is a single continuous path linking start and end through connected tiles."),
        RotationalCipherGridPuzzle => new(
            "Transform the grid until the center row reveals the clue answer.",
            "Use the row and column buttons to rotate letters around the board while watching the middle row.",
            "The center row spells the word implied by the clue."),
        UnlockPatternPuzzle => new(
            "Replay the hidden arrow pattern exactly.",
            "Wait until the preview disappears, then use the keyboard arrow keys or the on-screen arrow buttons to enter the same sequence.",
            "Every direction matches the original pattern in the same order."),
        DirectionalEchoPuzzle => new(
            "Enter the transformed version of the shown direction pattern.",
            "Watch the preview, then enter the answer using the transformation rule instead of copying it directly.",
            "The full entered sequence matches the transformed target pattern."),
        DimensionalPatternShiftPuzzle => new(
            "Translate the shown pattern using the puzzle's transformation rule.",
            "Use the arrow controls to enter the transformed path, not the raw preview.",
            "The entered pattern matches the required dimensional transform exactly."),
        ValveFlowPuzzle => new(
            "Open the exact set of valves that produces the target flow.",
            "Each valve adds its displayed amount to the live flow. You must hit the total using the required number of open valves.",
            "Current Flow equals Target Flow and the exact required count of valves is open."),
        FlowRedistributionPuzzle => new(
            "Equalize all outputs to the same pressure.",
            "Each valve button shows the change it applies to every output. Pulse valves until every output matches the shared target.",
            "All output values are identical and equal to the displayed target flow."),
        ConservationNetworkPuzzle => new(
            "Reach the exact target distribution without changing the total flow.",
            "Each valve moves pressure between outputs. Use the displayed valve deltas to turn the current distribution into the target one.",
            "Every current output value matches the corresponding target value exactly."),
        WeightBalancePuzzle => new(
            "Select the exact combination of weights that hits the target load.",
            "Toggle weights on or off. Both the total weight and the required number of selected weights must match.",
            "Current Load equals Target Load and the selected count matches the exact requirement."),
        WeightedTriggerZonesPuzzle => new(
            "Push the weight blocks until the weighted pad total matches the target.",
            "Move blocks around the room onto multiplier pads. Each pad changes a block's contribution.",
            "The displayed weighted total equals the target."),
        WeightedEquilibriumPuzzle => new(
            "Balance the full pad equation at zero.",
            "Move blocks onto positive and negative multiplier pads. Zero only counts if enough pads are occupied.",
            "Equation total is exactly 0 and the occupied-pad count meets the minimum."),
        XorLogicPuzzle => new(
            "Match the circuit's target parity and exact live-bit count.",
            "Toggle bits on and off. Every active bit flips the parity, so both the parity and number of lit bits matter.",
            "Live Parity matches Target Parity and the live-bit count matches the target count."),
        BinaryShiftPuzzle => new(
            "Transform the current bit string into the target bit string.",
            "Use Rotate Left, Rotate Right, and Flip Center to change the current pattern.",
            "The current bit string matches the target bit string exactly."),
        BinaryTransformationPuzzle binaryTransformation => new(
            "Chain the machine operations until the current state matches the target.",
            "Use the listed binary operations in a deliberate order. The hint points toward a useful sequence and the move limit is strict.",
            $"Current bits match target bits before exceeding {binaryTransformation.MoveLimit} moves."),
        YarnUntanglePuzzle => new(
            "Untangle the strands so no lines cross.",
            "The left anchors stay fixed. Click right-side endpoints to swap them until every strand runs cleanly.",
            "Crossings drop to zero."),
        CrossingPathPuzzle => new(
            "Match each word tile to its correct descriptor tile.",
            "Step on a word tile in the room to carry it, then walk to the descriptor tile that matches it.",
            "Every word has been matched to its correct descriptor."),
        KnotTopologyPuzzle => new(
            "Remove every real crossing and leave only fake illusion crossings.",
            "Swap strand endpoints until the solid crossings disappear. Dotted illusion crossings do not count.",
            "Real crossings reach zero."),
        ZoneActivationPuzzle => new(
            "Charge the active beacon one zone at a time.",
            "Walk into the highlighted zone in the room and stay there until it locks.",
            "Each zone locks in order until the full sequence is complete."),
        DelayedActivationSequencePuzzle => new(
            "Lock each zone in order while respecting the wake delay.",
            "Hold the live zone until it locks, then stay planted on the locked zone until the next zone activates.",
            "Every zone locks in order without leaving the sequence."),
        RecursiveActivationSequencePuzzle => new(
            "Adapt to the changing zone order and modifiers until the full sequence is complete.",
            "Charge the current zone, then read the modifier list because each completed zone can change the order or next hold time.",
            "All recursive zone steps complete in the resulting final order."),
        _ => new(
            "Solve the current room puzzle.",
            "Use the controls shown in the panel and watch the status line for live feedback.",
            "The room reports solved and the doors unlock.")
    };

    protected static string FormatSignedDelta(int value) => value > 0 ? $"+{value}" : value.ToString();

    protected static string FormatAdjustmentSummary(IEnumerable<int> adjustments) =>
        string.Join(" / ", adjustments.Select(FormatSignedDelta));

    private async Task SyncLivePlayerStateAsync(bool force = false)
    {
        if (!_jsReady || !IsLoaded || ParsedSeed is null || CurrentRoom is null || CurrentRoomState is null)
        {
            return;
        }

        var nowUtc = DateTime.UtcNow;
        if (!force && !_playerStateDirty)
        {
            return;
        }

        if (!force && nowUtc - _lastPlayerStateSyncUtc < PlayerStateSyncInterval)
        {
            return;
        }

        var payload = new LivePlayerSessionState
        {
            Username = Username,
            Seed = ParsedSeed.RawSeed,
            MapName = MapName,
            Source = NormalizedSource,
            Difficulty = ParsedSeed.Difficulty.ToString(),
            MapSize = ParsedSeed.Size,
            CurrentRoomLabel = CurrentRoom.Coordinates.ToString(),
            Room = new LiveRoomState
            {
                X = CurrentRoom.Coordinates.X,
                Y = CurrentRoom.Coordinates.Y,
                Kind = CurrentRoom.Kind.ToString(),
            },
            Position = new LivePositionState
            {
                X = Math.Round(PlayerX, 3),
                Y = Math.Round(PlayerY, 3),
                XPercent = ToPositionPercentX(PlayerX, PlayerSize),
                YPercent = ToPercentY(PlayerY),
                Width = Math.Round(PlayerSize, 3),
                Height = Math.Round(PlayerSize, 3),
            },
            Facing = PlayerFacing.ToString(),
            IsMoving = IsMoving,
            GoldCollected = TotalGold,
            ElapsedTime = FormatElapsed(_sessionStopwatch.Elapsed),
            UpdatedAtUtc = nowUtc.ToString("O"),
        };

        await JS.InvokeVoidAsync("enigmaGame.setLivePlayerState", payload);
        await UpdatePendingLossDraftAsync(force: force);
        _lastPlayerStateSyncUtc = nowUtc;
        _playerStateDirty = false;
    }

    private PendingLossSummary BuildPendingLossSummary(string reason = "abandoned")
    {
        var mapValue = CalculateMapValue();
        var projectedCompletionPayout = CalculateProjectedCompletionPayout();
        return new PendingLossSummary
        {
            RunNonce = _runNonce,
            Username = Username,
            Seed = ParsedSeed?.RawSeed ?? string.Empty,
            MapName = MapName,
            Source = NormalizedSource,
            Difficulty = ParsedSeed?.Difficulty.ToString() ?? string.Empty,
            ThemeLabel = "Unknown",
            MapValue = mapValue,
            ForfeitedRunPayout = TotalGold,
            ProjectedCompletionPayout = projectedCompletionPayout,
            UsedItems = BuildSelectedItemIds(),
            Reason = reason,
            AbandonedAtUtc = DateTime.UtcNow.ToString("O"),
            IsMultiplayer = IsCoopRun,
            MultiplayerSessionId = CoopSessionId,
            PartnerUsername = CoopPartnerName,
        };
    }

    protected static string FormatLoadoutSlot(string slotKind)
    {
        if (string.IsNullOrWhiteSpace(slotKind))
        {
            return "Support";
        }

        return slotKind.Trim().Replace('_', ' ');
    }

    private List<string> BuildSelectedItemIds()
    {
        var usedItems = new List<string>();
        foreach (var item in EquippedLoadout)
        {
            if (string.IsNullOrWhiteSpace(item.ItemId) || item.Quantity <= 0)
            {
                continue;
            }

            for (var index = 0; index < item.Quantity; index++)
            {
                usedItems.Add(item.ItemId);
            }
        }

        return usedItems;
    }

    private async Task UpdatePendingLossDraftAsync(bool force = false)
    {
        if (IsTutorialRun || !_jsReady || !IsLoaded || ParsedSeed is null || _completionTriggered || _abandonTriggered)
        {
            return;
        }

        if (!force && !_playerStateDirty)
        {
            return;
        }

        await JS.InvokeVoidAsync("enigmaGame.setPendingLossDraft", BuildPendingLossSummary());
    }

    private int CalculateProjectedCompletionPayout()
    {
        var total = TotalGold;
        foreach (var roomState in _roomStates.Values)
        {
            if (!roomState.PuzzleRewardGranted)
            {
                total += roomState.PuzzleGoldReward;
            }

            if (roomState.RewardPickupVisible)
            {
                total += roomState.RewardPickupGold;
            }
        }

        return total;
    }

    private int CalculateMapValue()
    {
        if (ParsedSeed is null)
        {
            return 0;
        }

        var roomCount = ParsedSeed.Rooms.Count;
        var rewardCount = ParsedSeed.Rooms.Values.Count(room => room.Kind == MazeRoomKind.Reward);
        var difficultyMultiplier = ParsedSeed.Difficulty switch
        {
            MazeDifficulty.Medium => 1.25d,
            MazeDifficulty.Hard => 1.5d,
            _ => 1d,
        };

        var rewardMultiplier = rewardCount / 1.5d;
        var roomMultiplier = roomCount / 16d;
        return (int)Math.Round(((100d * difficultyMultiplier) * rewardMultiplier) * roomMultiplier);
    }

    public async Task HandleBeforeInternalNavigation(LocationChangingContext context)
    {
        if (IsTutorialRun || _allowRouteExit || _completionTriggered || _abandonTriggered || !IsLoaded || ParsedSeed is null)
        {
            return;
        }

        if (string.Equals(context.TargetLocation, NavigationManager.Uri, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        context.PreventNavigation();
        await AbandonAndRedirectAsync("navigation");
    }

    private async Task AbandonAndRedirectAsync(string reason)
    {
        if (_abandonTriggered || ParsedSeed is null)
        {
            return;
        }

        if (IsCoopRun)
        {
            await RedirectToCoopLossAsync(reason, submitLeave: true);
            return;
        }

        _abandonTriggered = true;
        _allowRouteExit = true;
        _sessionStopwatch.Stop();

        var summary = BuildPendingLossSummary(reason);
        await JS.InvokeVoidAsync("enigmaGame.setPendingLossSummary", summary);
        await JS.InvokeVoidAsync("enigmaGame.clearPendingLossDraft");
        await SubmitAbandonSummaryAsync(summary);
        await JS.InvokeVoidAsync("enigmaGame.clearLossUnload");
        await JS.InvokeVoidAsync("enigmaGame.clearLivePlayerState");
        await JS.InvokeVoidAsync("enigmaGame.disposeInput");
        await InvokeAsync(() => NavigationManager.NavigateTo("/lose", replace: true));
    }

    private async Task SubmitAbandonSummaryAsync(PendingLossSummary summary)
    {
        try
        {
            using var response = await Api.PostJsonAsync("api/auth/game/abandon", new
            {
                runNonce = summary.RunNonce,
                seed = summary.Seed,
                mapName = summary.MapName,
                source = summary.Source,
                usedItems = summary.UsedItems,
                forfeitedRunPayout = summary.ForfeitedRunPayout,
                projectedCompletionPayout = summary.ProjectedCompletionPayout,
                mapValue = summary.MapValue,
                reason = summary.Reason,
            });

            if (!response.IsSuccessStatusCode)
            {
                _ = await response.Content.ReadAsStringAsync();
            }
        }
        catch
        {
        }
    }
    private void MarkPlayerStateDirty(
        double previousX,
        double previousY,
        PlayerDirection previousFacing,
        bool previousMoving,
        GridPoint? previousRoom)
    {
        if (Math.Abs(PlayerX - previousX) > 0.001d ||
            Math.Abs(PlayerY - previousY) > 0.001d ||
            PlayerFacing != previousFacing ||
            IsMoving != previousMoving ||
            CurrentRoom?.Coordinates != previousRoom)
        {
            _playerStateDirty = true;
        }
    }

    protected sealed record WallSegment(string CssClass, string Style);
}

