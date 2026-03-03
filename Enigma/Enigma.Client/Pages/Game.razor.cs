using System.Diagnostics;
using Enigma.Client.Models.Gameplay;
using Enigma.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;

namespace Enigma.Client.Pages;

public partial class Game : ComponentBase, IAsyncDisposable
{
    private const double RoomSize = 1080d;
    private const double PlayerSize = 60d;
    private const double PlayerSpeed = 380d;
    private const double DoorWidth = 240d;
    private const double WallThickness = 42d;
    private const string CompletionSummaryStorageKey = "enigma.game.summary";
    private static readonly TimeSpan PlayerStateSyncInterval = TimeSpan.FromMilliseconds(120);

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
        if (IsCoopRun)
        {
            return HandleCoopPuzzleKeyChangeAsync(keyCode, isPressed);
        }

        if (CurrentRoomState?.Puzzle is UnlockPatternPuzzle unlockPattern && !unlockPattern.IsCompleted)
        {
            _pressedKeys.Clear();

            if (isPressed && TryMapDirectionKey(keyCode, out var patternDirection))
            {
                unlockPattern.Press(patternDirection);
            }

            return Task.CompletedTask;
        }

        if (CurrentRoomState?.Puzzle is DirectionalEchoPuzzle directionalEcho && !directionalEcho.IsCompleted)
        {
            _pressedKeys.Clear();

            if (isPressed && TryMapDirectionKey(keyCode, out var echoDirection))
            {
                directionalEcho.Press(echoDirection);
            }

            return Task.CompletedTask;
        }

        if (CurrentRoomState?.Puzzle is DimensionalPatternShiftPuzzle dimensionalShift && !dimensionalShift.IsCompleted)
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

    protected string GetRoomStageStyle()
    {
        var background = CurrentRoom is not null && RoomBackgrounds.TryGetValue(CurrentRoom.ConnectionKey, out var style)
            ? style
            : "linear-gradient(145deg, #293140 0%, #151b26 100%)";

        return $"background: {background};";
    }

    protected string GetPlayerStyle() =>
        $"left: {ToPercent(PlayerX)}%; top: {ToPercent(PlayerY)}%; width: {ToPercent(PlayerSize)}%; height: {ToPercent(PlayerSize)}%;";

    protected string GetPlayerClass() =>
        $"facing-{PlayerAnimationDirections[PlayerFacing]} {PlayerSpriteStates[PlayerFacing]} {(IsMoving ? "is-moving" : string.Empty)}";

    protected string GetRectStyle(PlayAreaRect rect) =>
        $"left: {ToPercent(rect.X)}%; top: {ToPercent(rect.Y)}%; width: {ToPercent(rect.Width)}%; height: {ToPercent(rect.Height)}%;";

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
                    ShowBanner($"Puzzle solved. +{reward} gold", 1.5d);
                }
            }
        }
        else if (CurrentCoopPuzzle?.Completed == true && !IsCurrentCoopRoomSolved)
        {
            _playerStateDirty = true;
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
                    ShowBanner($"Reward cache collected. +{bonus} gold", 1.5d);
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

        if (!IsCoopRun &&
            ((CurrentRoomState?.Puzzle is UnlockPatternPuzzle unlockPattern && !unlockPattern.IsCompleted) ||
             (CurrentRoomState?.Puzzle is DirectionalEchoPuzzle directionalEcho && !directionalEcho.IsCompleted) ||
             (CurrentRoomState?.Puzzle is DimensionalPatternShiftPuzzle dimensionalShift && !dimensionalShift.IsCompleted)))
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

        if (TryTransition(PlayerDirection.Left) ||
            TryTransition(PlayerDirection.Right) ||
            TryTransition(PlayerDirection.Up) ||
            TryTransition(PlayerDirection.Down))
        {
            MarkPlayerStateDirty(previousX, previousY, previousFacing, previousMoving, previousRoom);
            return;
        }

        PlayerX = Math.Clamp(PlayerX, 0d, RoomSize - PlayerSize);
        PlayerY = Math.Clamp(PlayerY, 0d, RoomSize - PlayerSize);
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

        PlayerX = Math.Clamp(PlayerX, 0d, RoomSize - PlayerSize);
        PlayerY = Math.Clamp(PlayerY, 0d, RoomSize - PlayerSize);
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
            ? $"left: {ToPercent(doorStart)}%; top: {ToPercent(edge)}%; width: {ToPercent(DoorWidth)}%; height: {ToPercent(WallThickness)}%;"
            : $"left: {ToPercent(edge)}%; top: {ToPercent(doorStart)}%; width: {ToPercent(WallThickness)}%; height: {ToPercent(DoorWidth)}%;");
    }

    private static WallSegment CreateWallSegment(bool isHorizontal, double fixedAxisValue, double start, double length, string side) =>
        isHorizontal
            ? new WallSegment(side, $"left: {ToPercent(start)}%; top: {ToPercent(fixedAxisValue)}%; width: {ToPercent(length)}%; height: {ToPercent(WallThickness)}%;")
            : new WallSegment(side, $"left: {ToPercent(fixedAxisValue)}%; top: {ToPercent(start)}%; width: {ToPercent(WallThickness)}%; height: {ToPercent(length)}%;");

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
                PlayerX = 0d;
                break;
            case PlayerDirection.Right:
                PlayerX = RoomSize - PlayerSize;
                break;
            case PlayerDirection.Up:
                PlayerY = 0d;
                break;
            case PlayerDirection.Down:
                PlayerY = RoomSize - PlayerSize;
                break;
        }
    }

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
        _completionTriggered = false;
        _abandonTriggered = false;
        _allowRouteExit = false;
        _pressedKeys.Clear();
        _lastPlayerStateSyncUtc = DateTime.MinValue;
        _playerStateDirty = true;
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

    private static double ToPercent(double value) => Math.Round((value / RoomSize) * 100d, 4);

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
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        var angle = Math.Atan2(deltaY, deltaX) * (180d / Math.PI);
        return $"left: {ToPercent(start.X)}%; top: {ToPercent(start.Y)}%; width: {ToPercent(length)}%; transform: rotate({Math.Round(angle, 2)}deg);";
    }

    protected static string GetModifierLabel(RecursiveZoneModifier modifier) => modifier switch
    {
        RecursiveZoneModifier.ReverseRemaining => "Reverse",
        RecursiveZoneModifier.RotateRemaining => "Rotate",
        RecursiveZoneModifier.SwapExtremes => "Swap Ends",
        RecursiveZoneModifier.ExtendNextHold => "Long Hold",
        _ => "Shift",
    };

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
                XPercent = ToPercent(PlayerX),
                YPercent = ToPercent(PlayerY),
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
            UsedItems = [],
            Reason = reason,
            AbandonedAtUtc = DateTime.UtcNow.ToString("O"),
            IsMultiplayer = IsCoopRun,
            MultiplayerSessionId = CoopSessionId,
            PartnerUsername = CoopPartnerName,
        };
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

