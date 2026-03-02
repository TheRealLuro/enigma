using System.Diagnostics;
using Enigma.Client.Models.Gameplay;
using Microsoft.AspNetCore.Components;
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
    private bool _completionTriggered;
    private bool _jsReady;
    private bool _playerStateDirty = true;
    private bool _resumeAttempted;

    [Inject] protected NavigationManager NavigationManager { get; set; } = default!;
    [Inject] protected IJSRuntime JS { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "seed")]
    public string? Seed { get; set; }

    [SupplyParameterFromQuery(Name = "mapName")]
    public string? MapName { get; set; }

    [SupplyParameterFromQuery(Name = "source")]
    public string? Source { get; set; }

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

    protected int SolvedRoomCount => _roomStates.Values.Count(state => state.Puzzle.IsCompleted);
    protected int TotalRoomCount => _roomStates.Count;
    protected string DifficultyLabel => ParsedSeed?.Difficulty.ToString() ?? "Unknown";
    protected string RoomCoordinateLabel => CurrentRoom is null ? "(0, 0)" : CurrentRoom.Coordinates.ToString();
    protected string CurrentMapSourceLabel => string.Equals(NormalizedSource, "load", StringComparison.Ordinal) ? "Loaded map" : "Generated map";
    protected string CurrentMapLabel => string.IsNullOrWhiteSpace(MapName) ? "Unsaved seed" : MapName!;
    protected string NormalizedSource => string.Equals(Source, "load", StringComparison.OrdinalIgnoreCase) ? "load" : "new";
    protected string ElapsedTimeLabel => FormatElapsed(_sessionStopwatch.Elapsed);
    protected bool CanShowRoom => ParsedSeed is not null && CurrentRoom is not null && CurrentRoomState is not null;
    protected bool PortalReady => CurrentRoomState?.FinishPortalVisible == true;

    protected override void OnParametersSet()
    {
        if (string.IsNullOrWhiteSpace(Seed))
        {
            LoadError = _resumeAttempted
                ? "Open a generated or loaded seed from the Play page before starting the game."
                : null;
            IsLoaded = false;
            return;
        }

        if (string.Equals(_loadedSeed, Seed, StringComparison.Ordinal))
        {
            return;
        }

        ResetRuntimeState();

        try
        {
            ParsedSeed = MazeSeedParser.Parse(Seed);
            foreach (var room in ParsedSeed.Rooms.Values)
            {
                _roomStates[room.Coordinates] = new RoomRuntimeState
                {
                    Definition = room,
                    Puzzle = PuzzleFactory.Create(ParsedSeed.RawSeed, room, ParsedSeed.Difficulty),
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
        if (!_resumeAttempted && string.IsNullOrWhiteSpace(Seed))
        {
            _resumeAttempted = true;
            var activeRun = await JS.InvokeAsync<ActiveGameSession?>("enigmaGame.getActiveGameSession");
            if (!string.IsNullOrWhiteSpace(activeRun?.Seed))
            {
                var url = $"/game?seed={Uri.EscapeDataString(activeRun.Seed)}&source={Uri.EscapeDataString(activeRun.Source)}";
                if (!string.IsNullOrWhiteSpace(activeRun.MapName))
                {
                    url += $"&mapName={Uri.EscapeDataString(activeRun.MapName)}";
                }

                NavigationManager.NavigateTo(url, replace: true);
                return;
            }

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

            var identity = await JS.InvokeAsync<PlayerIdentity?>("enigmaGame.getPlayerIdentity");
            if (!string.IsNullOrWhiteSpace(identity?.Username))
            {
                Username = identity!.Username;
            }

            _jsReady = true;
            await PersistActiveGameSessionAsync();
            await SyncLivePlayerStateAsync(force: true);
            StartGameLoop();
            await InvokeAsync(StateHasChanged);
            return;
        }

        if (_jsReady && IsLoaded)
        {
            await PersistActiveGameSessionAsync();
        }
    }

    [JSInvokable]
    public Task HandleKeyChange(string keyCode, bool isPressed)
    {
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

        CurrentRoomState.Puzzle.Update(new PuzzleUpdateContext
        {
            PlayerBounds = new PlayAreaRect(PlayerX, PlayerY, PlayerSize, PlayerSize),
            DeltaTimeSeconds = deltaTime,
        });

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

        if (CurrentRoomState.RewardPickupVisible &&
            CurrentRoomState.RewardPickupBounds.Intersects(new PlayAreaRect(PlayerX, PlayerY, PlayerSize, PlayerSize)))
        {
            var bonus = CurrentRoomState.CollectRewardPickup();
            if (bonus > 0)
            {
                TotalGold += bonus;
                _playerStateDirty = true;
                ShowBanner($"Reward cache collected. +{bonus} gold", 1.5d);
            }
        }

        if (!_completionTriggered &&
            CurrentRoomState.FinishPortalVisible &&
            CurrentRoomState.FinishPortalBounds.Intersects(new PlayAreaRect(PlayerX, PlayerY, PlayerSize, PlayerSize)))
        {
            _completionTriggered = true;
            _sessionStopwatch.Stop();
            _ = CompleteRunAsync();
        }

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

        await JS.InvokeVoidAsync("enigmaGame.sessionSetJson", CompletionSummaryStorageKey, summary);
        await JS.InvokeVoidAsync("enigmaGame.clearActiveGameSession");
        await JS.InvokeVoidAsync("enigmaGame.clearLivePlayerState");
        await JS.InvokeVoidAsync("enigmaGame.disposeInput");
        await InvokeAsync(() => NavigationManager.NavigateTo("/gameend/win"));
    }

    private void UpdateMovement(double deltaTime)
    {
        var previousX = PlayerX;
        var previousY = PlayerY;
        var previousFacing = PlayerFacing;
        var previousMoving = IsMoving;
        var previousRoom = CurrentRoom?.Coordinates;

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

        if (!CurrentRoomState.Puzzle.IsCompleted)
        {
            ClampToBoundary(direction);
            ShowBanner("Solve the room puzzle before leaving.", 1.0d);
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
        _pressedKeys.Clear();
        _lastPlayerStateSyncUtc = DateTime.MinValue;
        _playerStateDirty = true;
    }

    private static string FormatElapsed(TimeSpan elapsed) =>
        $"{elapsed.Hours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}:{elapsed.Milliseconds:000}";

    private static double ToPercent(double value) => Math.Round((value / RoomSize) * 100d, 4);

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
        await PersistActiveGameSessionAsync();
        _lastPlayerStateSyncUtc = nowUtc;
        _playerStateDirty = false;
    }

    private async Task PersistActiveGameSessionAsync()
    {
        if (!_jsReady || !IsLoaded || ParsedSeed is null || CurrentRoom is null)
        {
            return;
        }

        var activeRun = new ActiveGameSession
        {
            Seed = ParsedSeed.RawSeed,
            MapName = MapName,
            Source = NormalizedSource,
            Username = Username,
            Difficulty = ParsedSeed.Difficulty.ToString(),
            Size = ParsedSeed.Size,
            CurrentRoomLabel = CurrentRoom.Coordinates.ToString(),
            SavedAtUtc = DateTime.UtcNow.ToString("O"),
        };

        await JS.InvokeVoidAsync("enigmaGame.setActiveGameSession", activeRun);
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
