using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Enigma.Client.Models;
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
    private sealed record ActivePuzzleConsole(PlayAreaRect Bounds, bool IsLocal);
    private sealed record WorldInteractableCandidate(PuzzleWorldInteractable Interactable, double Distance, double FacingAlignment);
    private sealed record WorldActionPromptState(PlayAreaRect AnchorRect, string Text, bool InRange);
    protected sealed record WorldPuzzleStat(string Label, string Value);
    private sealed class BehaviorProfileSnapshot
    {
        public int LeftInteractions { get; set; }
        public int RightInteractions { get; set; }
        public int DoorRushAttempts { get; set; }
        public int InteractionSamples { get; set; }
    }

    private const double RoomSize = 1080d;
    private const double StageAspectRatio = 16d / 9d;
    private const double RenderWidth = RoomSize * StageAspectRatio;
    private const double PlayerSize = 60d;
    private const double PlayerSpeed = 380d;
    private const double DoorWidth = 240d;
    private const double WallThickness = 42d;
    private const double HorizontalWallCollisionInset = WallThickness * ((RoomSize - PlayerSize) / (RenderWidth - PlayerSize));
    private const double PuzzleConsoleSize = 116d;
    private const double RadarCoreRadius = 60d;
    private const double RadarDetailRadius = 138d;
    private const double RadarOuterRadius = 220d;
    private const double RadarPulseCycleSeconds = 5.6d;
    private const int RadarPulseEventsPerCycle = 2;
    private const double RadarPingLeadFraction = 0.04d;
    private const double RadarInterferenceDistance = 248d;
    private const double RadarWallCollisionPadding = 8d;
    private const double WorldInteractableMinimumSize = 56d;
    private const double BehaviorAdaptationCap = 0.35d;
    private const string BehaviorStoragePrefix = "enigma.behavior";
    private static readonly PlayAreaRect[] PuzzleConsoleCandidates =
    [
        new(72d, 882d, PuzzleConsoleSize, PuzzleConsoleSize),
        new(72d, 482d, PuzzleConsoleSize, PuzzleConsoleSize),
        new(72d, 82d, PuzzleConsoleSize, PuzzleConsoleSize),
        new(892d, 82d, PuzzleConsoleSize, PuzzleConsoleSize),
        new(892d, 482d, PuzzleConsoleSize, PuzzleConsoleSize),
    ];
    private const double CoopConsoleMinSeparation = 300d;
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
    private readonly List<string> _consumedItemIds = [];
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
    private bool _loadoutConsumptionPrimed;
    private int _lastRadarPulsePhaseIndex = -1;
    private DateTime _timerPauseUntilUtc = DateTime.MinValue;
    private DateTime _visionBoostUntilUtc = DateTime.MinValue;
    private DateTime _pathRevealUntilUtc = DateTime.MinValue;
    private GridPoint? _tutorialStartRoomCoordinates;
    private bool _tutorialRoomTransitionReported;
    private string? _nearestWorldInteractableId;
    private bool _behaviorProfileLoaded;
    private bool _behaviorProfileDirty;
    private DateTime _lastBehaviorProfilePersistUtc = DateTime.MinValue;
    private BehaviorProfileSnapshot _globalBehaviorProfile = new();
    private BehaviorProfileSnapshot _seedBehaviorProfile = new();

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
    protected bool HasPuzzleOverlayContent => IsCoopRun
        ? HasCoopPuzzle
        : CurrentRoomState?.Puzzle is not null && CurrentRoomState.Puzzle is not IWorldInteractivePuzzle;
    protected bool IsCurrentPuzzleSolved => IsCoopRun ? IsCurrentCoopRoomSolved : CurrentRoomState?.Puzzle.IsCompleted == true;
    protected bool UsesRoomNativePuzzleInteraction => !IsCoopRun &&
        CurrentRoomState?.Puzzle is
            (IWorldInteractivePuzzle
            or PressurePlatePuzzle
            or PhaseRelayPuzzle
            or HarmonicPhasePuzzle
            or WeightGridPuzzleBase
            or ZoneActivationPuzzle
            or DelayedActivationSequencePuzzle
            or RecursiveActivationSequencePuzzle
            or CrossingPathPuzzle);
    protected bool CanInteractWithPuzzleConsole => CanShowRoom && HasPuzzleOverlayContent && !IsCurrentPuzzleSolved && !UsesRoomNativePuzzleInteraction;
    protected bool HasHotkeyUsableItems => GetActiveLoadoutItems().Count > 0;
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
        _tutorialStartRoomCoordinates = null;
        _tutorialRoomTransitionReported = false;

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
            if (IsTutorialRun)
            {
                _tutorialStartRoomCoordinates = CurrentRoom.Coordinates;
            }
            CenterPlayer();
            _sessionStopwatch.Restart();
            IsLoaded = true;
            LoadError = null;
            _loadedSeed = Seed;
            ShowBanner("Every room is locked until its puzzle is solved.", 2.8d);
            _playerStateDirty = true;
            ApplyBehaviorProfileToEasyPuzzles();
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

            await LoadBehaviorProfilesAsync();
            ApplyBehaviorProfileToEasyPuzzles();

            var storedLoadout = await JS.InvokeAsync<List<RunLoadoutSelection>?>("enigmaGame.getRunLoadout");
            EquippedLoadout = (storedLoadout ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item.ItemId) && item.Quantity > 0)
                .ToList();
            PrimePassiveLoadoutConsumption();

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
            await JS.InvokeVoidAsync("enigmaGame.startRunAmbiance", _runNonce);
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
            if (isPressed && TryInteractNearestWorldInteractable(PuzzleInteractionSource.Keyboard))
            {
                return Task.CompletedTask;
            }

            if (isPressed && IsNearPuzzleConsole())
            {
                TogglePuzzleOverlay();
            }

            return Task.CompletedTask;
        }

        if (isPressed && TryGetLoadoutItemIdForHotkey(keyCode, out var loadoutItemId))
        {
            _ = UseLoadoutItemFromUiAsync(loadoutItemId);
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

    protected string GetPuzzleConsoleStyle() => GetRectStyle(GetLocalPuzzleConsoleBounds());

    protected string GetPuzzleConsolePromptStyle()
    {
        var consoleBounds = GetLocalPuzzleConsoleBounds();
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
        var consoleBounds = GetLocalPuzzleConsoleBounds();
        var consoleCenterX = consoleBounds.CenterX;
        var consoleCenterY = consoleBounds.CenterY;
        var distance = Math.Sqrt(Math.Pow(playerCenterX - consoleCenterX, 2d) + Math.Pow(playerCenterY - consoleCenterY, 2d));
        var interactionDistance = DateTime.UtcNow < _visionBoostUntilUtc ? 235d : 165d;
        return distance <= interactionDistance;
    }

    protected IEnumerable<PuzzleWorldInteractable> GetWorldInteractablesForRender()
    {
        if (IsCoopRun || CurrentRoomState?.Puzzle is not IWorldInteractivePuzzle worldPuzzle || worldPuzzle.IsSolved)
        {
            _nearestWorldInteractableId = null;
            return [];
        }

        var interactables = worldPuzzle.GetWorldInteractables()
            .Where(interactable => interactable.Clickable)
            .ToList();
        _nearestWorldInteractableId = TryFindNearestWorldInteractableCandidate(worldPuzzle, null)?.Interactable.Id;
        return interactables;
    }

    protected string GetWorldInteractableStyle(PuzzleWorldInteractable interactable)
    {
        var expandedBounds = ExpandWorldInteractableBounds(interactable.Bounds);
        return GetRectStyle(expandedBounds);
    }

    protected string GetWorldInteractableClass(PuzzleWorldInteractable interactable)
    {
        var classes = $"enigma-world-interactable {interactable.CssClass}";
        if (!interactable.Enabled)
        {
            classes += " disabled";
        }
        else if (CurrentRoomState?.Puzzle is IWorldInteractivePuzzle worldPuzzle)
        {
            classes += worldPuzzle.Status switch
            {
                PuzzleStatus.Active => " state-active",
                PuzzleStatus.FailedTemporary => " state-failed",
                PuzzleStatus.Resetting => " state-resetting",
                PuzzleStatus.Cooldown => " state-cooldown",
                _ => string.Empty,
            };
        }

        if (!string.IsNullOrWhiteSpace(_nearestWorldInteractableId) &&
            string.Equals(_nearestWorldInteractableId, interactable.Id, StringComparison.OrdinalIgnoreCase))
        {
            classes += " nearest";
        }

        return classes;
    }

    protected string GetWorldInteractableTooltip(PuzzleWorldInteractable interactable)
    {
        var action = GetWorldInteractableActionVerb(interactable);
        var prompt = RequiresFullStepForInteraction(interactable)
            ? $"Step onto node, then press E to {action}"
            : $"Press E to {action}";

        if (!interactable.Enabled)
        {
            return $"{prompt} (unavailable)";
        }

        return IsWithinWorldInteractionRange(interactable)
            ? prompt
            : $"Move closer to {prompt.ToLowerInvariant()}";
    }

    protected string GetWorldInteractableRenderLabel(PuzzleWorldInteractable interactable)
    {
        if (!string.IsNullOrWhiteSpace(interactable.Label))
        {
            return interactable.Label;
        }

        if (TryParseInteractableIndex(interactable.Id, "behavior-", out var behaviorIndex))
        {
            return behaviorIndex switch
            {
                0 => "L",
                1 => "C",
                2 => "R",
                _ => $"T{behaviorIndex + 1}",
            };
        }

        if (TryParseInteractableIndex(interactable.Id, "false-", out var falseIndex))
        {
            return (falseIndex + 1).ToString(CultureInfo.InvariantCulture);
        }

        if (TryParseInteractableIndex(interactable.Id, "grid-", out var gridIndex))
        {
            return string.Empty;
        }

        if (TryParseInteractableIndex(interactable.Id, "hidden-", out _))
        {
            return string.Empty;
        }

        return "\u2022";
    }

    protected string GetWorldPuzzlePressEInstruction()
    {
        if (!TryGetWorldActionPromptState(out var promptState))
        {
            return "Press E near an active node.";
        }

        return promptState.Text;
    }

    protected bool ShowWorldActionPrompt => TryGetWorldActionPromptState(out _);

    protected string GetWorldActionPromptText() =>
        TryGetWorldActionPromptState(out var promptState) ? promptState.Text : "Press E to interact.";

    protected string GetWorldActionPromptStyle()
    {
        if (!TryGetWorldActionPromptState(out var promptState))
        {
            return string.Empty;
        }

        var prompt = promptState.Text;
        var width = Math.Clamp(186d + (prompt.Length * 3.4d), 196d, 388d);
        var height = 42d;
        var left = Math.Clamp(promptState.AnchorRect.CenterX - (width / 2d), 18d, RoomSize - width - 18d);
        var top = promptState.AnchorRect.Y >= RoomSize / 2d
            ? promptState.AnchorRect.Y - height - 14d
            : promptState.AnchorRect.Bottom + 14d;
        top = Math.Clamp(top, 18d, RoomSize - height - 18d);
        return $"left: {ToPositionPercentX(left, width)}%; top: {ToPercentY(top)}%; width: {ToLengthPercentX(width)}%;";
    }

    protected string GetWorldActionPromptClass() =>
        TryGetWorldActionPromptState(out var promptState) && !promptState.InRange
            ? "enigma-world-action-prompt out-of-range"
            : "enigma-world-action-prompt";

    protected bool CanClickWorldInteractable(PuzzleWorldInteractable interactable) =>
        interactable.Clickable &&
        interactable.Enabled &&
        !IsCoopRun &&
        CurrentRoomState?.Puzzle is IWorldInteractivePuzzle &&
        IsWithinWorldInteractionRange(interactable);

    protected void InteractWithWorldInteractable(string interactableId)
    {
        _ = TryInteractWorldInteractableById(interactableId, PuzzleInteractionSource.Click);
    }

    private bool TryInteractNearestWorldInteractable(PuzzleInteractionSource source)
    {
        if (IsCoopRun || CurrentRoomState?.Puzzle is not IWorldInteractivePuzzle worldPuzzle || worldPuzzle.IsSolved)
        {
            return false;
        }

        var candidate = TryFindNearestWorldInteractableCandidate(worldPuzzle, null);
        if (candidate is null)
        {
            return false;
        }

        return TryInteractWorldInteractable(worldPuzzle, candidate.Interactable, source);
    }

    private bool TryInteractWorldInteractableById(string interactableId, PuzzleInteractionSource source)
    {
        if (IsCoopRun || CurrentRoomState?.Puzzle is not IWorldInteractivePuzzle worldPuzzle || worldPuzzle.IsSolved)
        {
            return false;
        }

        var candidate = TryFindNearestWorldInteractableCandidate(worldPuzzle, interactableId);
        if (candidate is null)
        {
            return false;
        }

        return TryInteractWorldInteractable(worldPuzzle, candidate.Interactable, source);
    }

    private bool TryInteractWorldInteractable(IWorldInteractivePuzzle worldPuzzle, PuzzleWorldInteractable interactable, PuzzleInteractionSource source)
    {
        if (!interactable.Enabled || !IsWithinWorldInteractionRange(interactable))
        {
            ShowBanner(
                RequiresFullStepForInteraction(interactable)
                    ? "Step fully onto the node to interact."
                    : "Move closer to interact.",
                0.65d);
            return false;
        }

        var interacted = worldPuzzle.TryInteract(
            interactable.Id,
            source,
            new PlayAreaRect(PlayerX, PlayerY, PlayerSize, PlayerSize),
            PlayerFacing,
            _sessionStopwatch.Elapsed.TotalSeconds);

        if (!interacted)
        {
            return false;
        }

        RecordBehaviorInteraction(interactable.Bounds);
        _playerStateDirty = true;
        return true;
    }

    private WorldInteractableCandidate? TryFindNearestWorldInteractableCandidate(IWorldInteractivePuzzle worldPuzzle, string? forcedId, bool requireInRange = true)
    {
        var playerCenterX = PlayerX + (PlayerSize / 2d);
        var playerCenterY = PlayerY + (PlayerSize / 2d);
        var candidates = new List<WorldInteractableCandidate>();
        foreach (var interactable in worldPuzzle.GetWorldInteractables())
        {
            if (!interactable.Clickable || !interactable.Enabled)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(forcedId) &&
                !string.Equals(interactable.Id, forcedId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var distance = GetWorldInteractableDistance(interactable);
            if (requireInRange && !IsWithinWorldInteractionRange(interactable, distance))
            {
                continue;
            }

            var facingAlignment = GetFacingAlignmentForPoint(playerCenterX, playerCenterY, interactable.Bounds.CenterX, interactable.Bounds.CenterY);
            candidates.Add(new WorldInteractableCandidate(interactable, distance, facingAlignment));
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates
            .OrderBy(candidate => candidate.Distance)
            .ThenByDescending(candidate => candidate.FacingAlignment)
            .ThenByDescending(candidate => candidate.Interactable.Priority)
            .ThenBy(candidate => candidate.Interactable.Id, StringComparer.Ordinal)
            .First();
    }

    private bool TryGetWorldActionPromptState(out WorldActionPromptState promptState)
    {
        promptState = default!;
        if (IsCoopRun || CurrentRoomState?.Puzzle is not IWorldInteractivePuzzle worldPuzzle || worldPuzzle.IsSolved)
        {
            return false;
        }

        var nearest = TryFindNearestWorldInteractableCandidate(worldPuzzle, null, requireInRange: false);
        if (nearest is null)
        {
            return false;
        }

        var radarSignal = GetRadarSignalForPoint(nearest.Interactable.Bounds.CenterX, nearest.Interactable.Bounds.CenterY);
        if (radarSignal <= 0.11d)
        {
            return false;
        }

        var action = GetWorldInteractableActionVerb(nearest.Interactable);
        var inRange = IsWithinWorldInteractionRange(nearest.Interactable, nearest.Distance);
        if (RequiresFullStepForInteraction(nearest.Interactable) && !inRange)
        {
            return false;
        }

        var text = inRange
            ? $"Press E to {action}."
            : $"Move closer. Press E to {action}.";

        promptState = new WorldActionPromptState(nearest.Interactable.Bounds, text, inRange);
        return true;
    }

    private static string GetWorldInteractableActionVerb(PuzzleWorldInteractable interactable)
    {
        var id = interactable.Id;
        if (string.Equals(id, "layer-toggle", StringComparison.OrdinalIgnoreCase))
        {
            return "shift layers";
        }

        if (string.Equals(id, "echo-replay", StringComparison.OrdinalIgnoreCase))
        {
            return "replay the echo";
        }

        if (string.Equals(id, "heat-up", StringComparison.OrdinalIgnoreCase))
        {
            return "increase heat";
        }

        if (string.Equals(id, "heat-down", StringComparison.OrdinalIgnoreCase))
        {
            return "decrease heat";
        }

        if (string.Equals(id, "pressure-up", StringComparison.OrdinalIgnoreCase))
        {
            return "increase pressure";
        }

        if (string.Equals(id, "pressure-down", StringComparison.OrdinalIgnoreCase))
        {
            return "decrease pressure";
        }

        return id switch
        {
            var value when value.StartsWith("relay-", StringComparison.OrdinalIgnoreCase) => "toggle relay",
            var value when value.StartsWith("echo-pad-", StringComparison.OrdinalIgnoreCase) => "imprint this pad",
            var value when value.StartsWith("alpha-", StringComparison.OrdinalIgnoreCase) => "lock alpha node",
            var value when value.StartsWith("beta-", StringComparison.OrdinalIgnoreCase) => "lock beta node",
            var value when value.StartsWith("behavior-", StringComparison.OrdinalIgnoreCase) => "commit terminal",
            var value when value.StartsWith("recursive-", StringComparison.OrdinalIgnoreCase) => "confirm anchor",
            var value when value.StartsWith("grid-", StringComparison.OrdinalIgnoreCase) => "pulse cell",
            var value when value.StartsWith("symbol-", StringComparison.OrdinalIgnoreCase) => "invoke symbol",
            var value when value.StartsWith("time-gate-", StringComparison.OrdinalIgnoreCase) => "capture gate window",
            var value when value.StartsWith("false-", StringComparison.OrdinalIgnoreCase) => "probe terminal",
            var value when value.StartsWith("hidden-", StringComparison.OrdinalIgnoreCase) => "test tile",
            _ => "interact",
        };
    }

    private PlayAreaRect ExpandWorldInteractableBounds(PlayAreaRect bounds)
    {
        var width = Math.Max(bounds.Width, WorldInteractableMinimumSize);
        var height = Math.Max(bounds.Height, WorldInteractableMinimumSize);
        var x = bounds.CenterX - (width / 2d);
        var y = bounds.CenterY - (height / 2d);
        return new PlayAreaRect(x, y, width, height);
    }

    private bool IsWithinWorldInteractionRange(PuzzleWorldInteractable interactable) =>
        IsWithinWorldInteractionRange(interactable, GetWorldInteractableDistance(interactable));

    private bool IsWithinWorldInteractionRange(PuzzleWorldInteractable interactable, double distance)
    {
        if (RequiresFullStepForInteraction(interactable))
        {
            return IsPlayerFullyOnInteractable(interactable);
        }

        var rangeBoost = DateTime.UtcNow < _visionBoostUntilUtc ? 1.25d : 1d;
        var range = Math.Max(64d, interactable.InteractionRange * rangeBoost);
        return distance <= range;
    }

    private double GetFacingAlignmentForPoint(double playerX, double playerY, double targetX, double targetY)
    {
        var deltaX = targetX - playerX;
        var deltaY = targetY - playerY;
        var magnitude = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (magnitude <= 0.001d || !TryGetFacingUnitVector(PlayerFacing, out var facingX, out var facingY))
        {
            return 0d;
        }

        var normalizedX = deltaX / magnitude;
        var normalizedY = deltaY / magnitude;
        return (normalizedX * facingX) + (normalizedY * facingY);
    }

    private static bool TryGetFacingUnitVector(PlayerDirection facing, out double x, out double y)
    {
        (x, y) = facing switch
        {
            PlayerDirection.Up => (0d, -1d),
            PlayerDirection.Right => (1d, 0d),
            PlayerDirection.Down => (0d, 1d),
            PlayerDirection.Left => (-1d, 0d),
            _ => (0d, 0d),
        };

        return !(Math.Abs(x) < 0.001d && Math.Abs(y) < 0.001d);
    }

    private static bool TryParseInteractableIndex(string interactableId, string prefix, out int index)
    {
        index = -1;
        if (!interactableId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(interactableId[prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out index);
    }

    protected bool ShowRoomInteractionProgress => TryGetRoomInteractionProgressState(out _);

    protected string GetRoomInteractionProgressStyle()
    {
        if (!TryGetRoomInteractionProgressState(out var progressState))
        {
            return string.Empty;
        }

        var labelLength = Math.Max(10, progressState.Label.Length);
        var width = Math.Clamp(220d + (labelLength * 3.2d), 250d, 380d);
        var left = Math.Clamp((RoomSize - width) / 2d, 18d, RoomSize - width - 18d);
        return $"left: {ToPositionPercentX(left, width)}%; bottom: 2.4%; width: {ToLengthPercentX(width)}%;";
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

    protected string GetWorldPuzzleStatusLabel()
    {
        if (CurrentRoomState?.Puzzle is not IWorldInteractivePuzzle worldPuzzle)
        {
            return "Unknown";
        }

        return worldPuzzle.Status switch
        {
            PuzzleStatus.NotStarted => "Idle",
            PuzzleStatus.Active => "Active",
            PuzzleStatus.Solved => "Solved",
            PuzzleStatus.FailedTemporary => "Rejected",
            PuzzleStatus.Resetting => "Resetting",
            PuzzleStatus.Cooldown => "Cooldown",
            PuzzleStatus.HintAvailable => "Hint Ready",
            PuzzleStatus.HintConsumed => "Hint Used",
            _ => worldPuzzle.Status.ToString(),
        };
    }

    protected string GetWorldPuzzleStatusCssClass()
    {
        if (CurrentRoomState?.Puzzle is not IWorldInteractivePuzzle worldPuzzle)
        {
            return "state-idle";
        }

        return worldPuzzle.Status switch
        {
            PuzzleStatus.Active => "state-active",
            PuzzleStatus.Solved => "state-solved",
            PuzzleStatus.FailedTemporary => "state-failed",
            PuzzleStatus.Resetting => "state-resetting",
            PuzzleStatus.Cooldown => "state-cooldown",
            PuzzleStatus.HintAvailable => "state-hint",
            PuzzleStatus.HintConsumed => "state-hint",
            _ => "state-idle",
        };
    }

    protected bool TryGetWorldPuzzleCardProgress(out double progress, out string label)
    {
        progress = 0d;
        label = string.Empty;

        if (CurrentRoomState?.Puzzle is not IWorldInteractivePuzzle worldPuzzle ||
            !worldPuzzle.TryGetProgressState(out var progressState))
        {
            return false;
        }

        progress = Math.Clamp(progressState.Progress, 0d, 1d);
        label = progressState.Label;
        return true;
    }

    protected IReadOnlyList<WorldPuzzleStat> GetWorldPuzzleStats()
    {
        if (CurrentRoomState?.Puzzle is null)
        {
            return [];
        }

        return CurrentRoomState.Puzzle switch
        {
            SignalRoutingChamberPuzzle signal => [
                new WorldPuzzleStat("Stable", $"{signal.MatchedRelayCount}/{Math.Max(1, signal.RequiredRelayCount)}"),
                new WorldPuzzleStat("Active", signal.ActiveRelayCount.ToString(CultureInfo.InvariantCulture)),
                new WorldPuzzleStat("Overload", signal.OverloadRelayCount.ToString(CultureInfo.InvariantCulture)),
            ],
            EchoMemoryChamberPuzzle echo => [
                new WorldPuzzleStat("Sequence", $"{echo.EnteredCount}/{echo.SequenceLength}"),
                new WorldPuzzleStat("Replay", echo.ReplayChargesRemaining.ToString(CultureInfo.InvariantCulture)),
            ],
            DualLayerRealityPuzzle dualLayer => [
                new WorldPuzzleStat("Sync", $"{dualLayer.PairStep}/{Math.Max(1, dualLayer.PairCount)}"),
                new WorldPuzzleStat("Layer", dualLayer.IsAlphaLayerActive ? "A" : "B"),
            ],
            BehaviorAdaptivePuzzle behavior => [
                new WorldPuzzleStat("Depth", $"{behavior.SequenceStep}/{Math.Max(1, behavior.SequenceLength)}"),
                new WorldPuzzleStat("Bias", $"{behavior.AdaptedHorizontalBias:+0.00;-0.00;0.00}"),
            ],
            RecursiveRoomMutationPuzzle recursive => [
                new WorldPuzzleStat("Loop", $"{recursive.LoopIndex}/{Math.Max(1, recursive.LoopCount)}"),
                new WorldPuzzleStat("Signal", recursive.IsRevealVisible ? "Visible" : "Hidden"),
            ],
            LivingGridPuzzle grid => [
                new WorldPuzzleStat("Aligned", $"{grid.MatchedCells}/{Math.Max(1, grid.CellCount)}"),
                new WorldPuzzleStat("Moves", grid.MoveCount.ToString(CultureInfo.InvariantCulture)),
            ],
            SymbolDecoderPuzzle symbol => [
                new WorldPuzzleStat("Phase", symbol.CurrentValue.ToString("00", CultureInfo.InvariantCulture)),
                new WorldPuzzleStat("Target", symbol.TargetValue.ToString("00", CultureInfo.InvariantCulture)),
                new WorldPuzzleStat("Coherence", symbol.Coherence.ToString(CultureInfo.InvariantCulture)),
            ],
            TimeWindowPuzzle timeWindow => [
                new WorldPuzzleStat("Window", $"{timeWindow.Step}/{Math.Max(1, timeWindow.StepCount)}"),
                new WorldPuzzleStat("Gate", (timeWindow.OpenGateIndex + 1).ToString(CultureInfo.InvariantCulture)),
            ],
            FalseSolutionPuzzle falseRoute => [
                new WorldPuzzleStat("Trusted", $"{falseRoute.RealStep}/{Math.Max(1, falseRoute.SequenceLength)}"),
                new WorldPuzzleStat("Decoy", falseRoute.DecoyPressure.ToString(CultureInfo.InvariantCulture)),
            ],
            HeatPressureBalancePuzzle heatPressure => [
                new WorldPuzzleStat("Heat", Math.Round(heatPressure.Heat).ToString("0", CultureInfo.InvariantCulture)),
                new WorldPuzzleStat("Pressure", Math.Round(heatPressure.Pressure).ToString("0", CultureInfo.InvariantCulture)),
                new WorldPuzzleStat("Band", heatPressure.IsInStableBand ? "Stable" : "Adjust"),
            ],
            HiddenRulePrimePuzzle hiddenRule => [
                new WorldPuzzleStat("Confirmed", $"{hiddenRule.Progress}/{Math.Max(1, hiddenRule.RequiredCount)}"),
            ],
            _ => [],
        };
    }

    protected string MovementHintText => CanInteractWithPuzzleConsole
        ? IsCoopRun
            ? "Move with WASD or the arrow keys. Walk to your blue console and press E to work the room puzzle."
            : "Move with WASD or the arrow keys. Walk to the console and press E to work the room puzzle."
        : CurrentRoomState?.Puzzle is IWorldInteractivePuzzle
            ? "Move with WASD or the arrow keys. Press E to interact with nearby puzzle nodes."
        : UsesRoomNativePuzzleInteraction
            ? "Move with WASD or the arrow keys. Solve this room by stepping onto or moving the active room elements."
            : IsCoopRun
                ? "Move with WASD or the arrow keys. Both explorers must vote the same doorway to change rooms."
                : "Move with WASD or the arrow keys.";
    protected string PuzzleConsolePromptText => IsCoopRun
        ? "Press E at your blue console to access the puzzle interface."
        : "Press E to access the puzzle interface.";

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

        var playerCenterX = PlayerX + (PlayerSize / 2d);
        var playerCenterY = PlayerY + (PlayerSize / 2d);
        var radarCoreRadius = GetCurrentRadarCoreRadius();
        var radarDetailRadius = GetCurrentRadarDetailRadius();
        var radarOuterRadius = GetCurrentRadarOuterRadius();
        var radarPulseStrength = GetRadarPulseStrength();
        var radarWallProximity = GetRadarWallProximityScale(playerCenterX, playerCenterY);
        var proximityCoreScale = 0.7d + (radarWallProximity * 0.3d);
        var proximityDetailScale = 0.62d + (radarWallProximity * 0.38d);
        var proximityOuterScale = 0.56d + (radarWallProximity * 0.44d);
        var proximityPulseScale = 0.54d + (radarWallProximity * 0.46d);

        radarCoreRadius *= proximityCoreScale;
        radarDetailRadius *= proximityDetailScale;
        radarOuterRadius *= proximityOuterScale;
        radarPulseStrength *= proximityPulseScale;

        radarDetailRadius = Math.Max(radarCoreRadius + 8d, radarDetailRadius);
        radarOuterRadius = Math.Max(radarDetailRadius + 20d, radarOuterRadius);
        var radarPulseScale = Math.Clamp((radarOuterRadius / 176d), 2.1d, 4.4d);
        var radarInterference = IsRadarInterferenceActive() ? 1d : 0d;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"background: {background}; " +
            $"--radar-x: {ToPositionPercentX(playerCenterX, 0d):0.###}%; " +
            $"--radar-y: {ToPercentY(playerCenterY):0.###}%; " +
            $"--radar-core-radius: {ToPercentY(radarCoreRadius):0.###}%; " +
            $"--radar-detail-radius: {ToPercentY(radarDetailRadius):0.###}%; " +
            $"--radar-outer-radius: {ToPercentY(radarOuterRadius):0.###}%; " +
            $"--radar-pulse-scale: {radarPulseScale:0.###}; " +
            $"--radar-pulse-strength: {radarPulseStrength:0.###}; " +
            $"--radar-wall-proximity: {radarWallProximity:0.###}; " +
            $"--radar-interference: {radarInterference:0.###};");
    }

    protected bool IsRadarInterferenceVisible => IsRadarInterferenceActive();

    protected string GetRadarBandClass(PlayAreaRect rect, bool anomalySensitive = false)
    {
        var signal = GetRadarSignalForPoint(rect.CenterX, rect.CenterY, anomalySensitive);
        return signal switch
        {
            >= 0.86d => "radar-core",
            >= 0.42d => "radar-reconstructed",
            _ => "radar-silhouette",
        };
    }

    private string GetRadarSignalStyleForRect(PlayAreaRect rect, bool anomalySensitive = false)
    {
        var signal = GetRadarSignalForPoint(rect.CenterX, rect.CenterY, anomalySensitive);
        return string.Create(CultureInfo.InvariantCulture, $"--radar-signal: {signal:0.###};");
    }

    private string AppendRadarSignalStyle(string style, PlayAreaRect rect, bool anomalySensitive = false)
    {
        var normalizedStyle = style.EndsWith(';') ? style : $"{style};";
        return $"{normalizedStyle} {GetRadarSignalStyleForRect(rect, anomalySensitive)}";
    }

    private double GetCurrentRadarRangeMultiplier()
    {
        var now = DateTime.UtcNow;
        return now < _visionBoostUntilUtc ? 1.2d : 1d;
    }

    private double GetCurrentRadarCoreRadius() => RadarCoreRadius * GetCurrentRadarRangeMultiplier();

    private double GetCurrentRadarDetailRadius()
    {
        var pulseExpansion = GetRadarPulseStrength() * 20d;
        return (RadarDetailRadius * GetCurrentRadarRangeMultiplier()) + pulseExpansion;
    }

    private double GetCurrentRadarOuterRadius()
    {
        var pulseExpansion = GetRadarPulseStrength() * 32d;
        return (RadarOuterRadius * GetCurrentRadarRangeMultiplier()) + pulseExpansion;
    }

    private static double GetRadarWallProximityScale(double playerCenterX, double playerCenterY)
    {
        var distanceToLeftWall = playerCenterX - HorizontalWallCollisionInset;
        var distanceToRightWall = (RoomSize - HorizontalWallCollisionInset) - playerCenterX;
        var distanceToTopWall = playerCenterY - WallThickness;
        var distanceToBottomWall = (RoomSize - WallThickness) - playerCenterY;
        var nearestWallDistance = Math.Min(
            Math.Min(distanceToLeftWall, distanceToRightWall),
            Math.Min(distanceToTopWall, distanceToBottomWall));
        var safeDistance = Math.Max(0d, nearestWallDistance);
        return Math.Clamp(safeDistance / Math.Max(1d, RadarOuterRadius), 0d, 1d);
    }

    private static double GetRadarPulseCycleProgress()
    {
        var nowSeconds = DateTime.UtcNow.TimeOfDay.TotalSeconds;
        return (nowSeconds % RadarPulseCycleSeconds) / RadarPulseCycleSeconds;
    }

    private double GetRadarPulseStrength()
    {
        var cycleProgress = GetRadarPulseCycleProgress();
        var pulsePeak = Math.Exp(-Math.Pow((cycleProgress - 0.16d) / 0.13d, 2d));
        return Math.Clamp(pulsePeak, 0d, 1d);
    }

    private double GetRadarSignalForPoint(double x, double y, bool anomalySensitive = false)
    {
        var playerCenterX = PlayerX + (PlayerSize / 2d);
        var playerCenterY = PlayerY + (PlayerSize / 2d);
        var deltaX = x - playerCenterX;
        var deltaY = y - playerCenterY;
        var distance = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (distance <= 0.0001d)
        {
            return 1d;
        }

        var directionX = deltaX / distance;
        var directionY = deltaY / distance;
        var collisionDistance = GetRadarCollisionDistance(playerCenterX, playerCenterY, directionX, directionY);

        var coreRadius = GetCurrentRadarCoreRadius();
        var detailRadius = GetCurrentRadarDetailRadius();
        var outerRadius = Math.Min(GetCurrentRadarOuterRadius(), collisionDistance);
        if (outerRadius <= 0.01d)
        {
            return 0.06d;
        }

        detailRadius = Math.Min(detailRadius, Math.Max(coreRadius + 1d, outerRadius * 0.72d));
        coreRadius = Math.Min(coreRadius, Math.Max(20d, detailRadius * 0.62d));

        var wallCollisionDampen = collisionDistance < GetCurrentRadarOuterRadius()
            ? Math.Clamp(collisionDistance / Math.Max(1d, GetCurrentRadarOuterRadius()), 0.58d, 1d)
            : 1d;

        double signal;
        if (distance <= coreRadius)
        {
            signal = 1d;
        }
        else if (distance <= detailRadius)
        {
            var t = (distance - coreRadius) / Math.Max(1d, detailRadius - coreRadius);
            signal = 1d - (0.3d * t);
        }
        else if (distance <= outerRadius)
        {
            var t = (distance - detailRadius) / Math.Max(1d, outerRadius - detailRadius);
            signal = 0.7d - (0.54d * t);
        }
        else
        {
            signal = 0.08d;
        }

        signal *= wallCollisionDampen;

        if (anomalySensitive && IsRadarInterferenceActive())
        {
            var flicker = (Math.Sin(DateTime.UtcNow.TimeOfDay.TotalMilliseconds / 140d) + 1d) * 0.07d;
            signal = Math.Max(0.06d, signal - flicker);
        }

        return Math.Clamp(signal, 0.06d, 1d);
    }

    private double GetRadarCollisionDistance(double originX, double originY, double directionX, double directionY)
    {
        const double epsilon = 0.0001d;
        var clampedOriginX = Math.Clamp(originX, HorizontalWallCollisionInset + 1d, RoomSize - HorizontalWallCollisionInset - 1d);
        var clampedOriginY = Math.Clamp(originY, WallThickness + 1d, RoomSize - WallThickness - 1d);

        var tX = double.PositiveInfinity;
        if (directionX > epsilon)
        {
            tX = ((RoomSize - HorizontalWallCollisionInset) - clampedOriginX) / directionX;
        }
        else if (directionX < -epsilon)
        {
            tX = (HorizontalWallCollisionInset - clampedOriginX) / directionX;
        }

        var tY = double.PositiveInfinity;
        if (directionY > epsilon)
        {
            tY = ((RoomSize - WallThickness) - clampedOriginY) / directionY;
        }
        else if (directionY < -epsilon)
        {
            tY = (WallThickness - clampedOriginY) / directionY;
        }

        if (tX <= 0d)
        {
            tX = double.PositiveInfinity;
        }

        if (tY <= 0d)
        {
            tY = double.PositiveInfinity;
        }

        var hitDistance = Math.Min(tX, tY);
        if (double.IsInfinity(hitDistance))
        {
            return GetCurrentRadarOuterRadius();
        }

        return Math.Max(16d, hitDistance - RadarWallCollisionPadding);
    }

    private double GetDistanceToRectCenter(PlayAreaRect rect)
    {
        var playerCenterX = PlayerX + (PlayerSize / 2d);
        var playerCenterY = PlayerY + (PlayerSize / 2d);
        var deltaX = rect.CenterX - playerCenterX;
        var deltaY = rect.CenterY - playerCenterY;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }

    private double GetWorldInteractableDistance(PuzzleWorldInteractable interactable)
    {
        if (UsesSurfaceDistance(interactable))
        {
            return GetDistanceToRectSurface(interactable.Bounds);
        }

        return GetDistanceToRectCenter(interactable.Bounds);
    }

    private bool UsesSurfaceDistance(PuzzleWorldInteractable interactable) =>
        interactable.CssClass.Contains("living-grid-cell", StringComparison.OrdinalIgnoreCase) ||
        interactable.CssClass.Contains("hidden-rule-tile", StringComparison.OrdinalIgnoreCase);

    private bool RequiresFullStepForInteraction(PuzzleWorldInteractable interactable) =>
        interactable.CssClass.Contains("echo-pad", StringComparison.OrdinalIgnoreCase) ||
        interactable.CssClass.Contains("living-grid-cell", StringComparison.OrdinalIgnoreCase) ||
        interactable.CssClass.Contains("hidden-rule-tile", StringComparison.OrdinalIgnoreCase);

    private bool IsPlayerFullyOnInteractable(PuzzleWorldInteractable interactable)
    {
        var playerBounds = new PlayAreaRect(PlayerX, PlayerY, PlayerSize, PlayerSize);
        return playerBounds.Left >= interactable.Bounds.Left &&
               playerBounds.Right <= interactable.Bounds.Right &&
               playerBounds.Top >= interactable.Bounds.Top &&
               playerBounds.Bottom <= interactable.Bounds.Bottom;
    }

    private double GetDistanceToRectSurface(PlayAreaRect rect)
    {
        var playerCenterX = PlayerX + (PlayerSize / 2d);
        var playerCenterY = PlayerY + (PlayerSize / 2d);

        var dx = Math.Max(Math.Abs(playerCenterX - rect.CenterX) - (rect.Width / 2d), 0d);
        var dy = Math.Max(Math.Abs(playerCenterY - rect.CenterY) - (rect.Height / 2d), 0d);
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private bool IsRadarInterferenceActive()
    {
        if (CurrentRoomState?.Puzzle is null || IsCurrentPuzzleSolved)
        {
            return false;
        }

        if (CanInteractWithPuzzleConsole &&
            GetDistanceToRectCenter(GetLocalPuzzleConsoleBounds()) <= RadarInterferenceDistance)
        {
            return true;
        }

        if (TryGetRoomInteractionProgressState(out var progressState) &&
            GetDistanceToRectCenter(progressState.AnchorRect) <= RadarInterferenceDistance)
        {
            return true;
        }

        return false;
    }

    protected string GetPlayerStyle() =>
        $"left: {ToPositionPercentX(PlayerX, PlayerSize)}%; top: {ToPercentY(PlayerY)}%; width: {ToLengthPercentX(PlayerSize)}%; height: {ToPercentY(PlayerSize)}%;";

    protected string GetPlayerClass() =>
        $"facing-{PlayerAnimationDirections[PlayerFacing]} {PlayerSpriteStates[PlayerFacing]} {(IsMoving ? "is-moving" : string.Empty)}";

    protected string GetRectStyle(PlayAreaRect rect) =>
        AppendRadarSignalStyle(
            $"left: {ToPositionPercentX(rect.X, rect.Width)}%; top: {ToPercentY(rect.Y)}%; width: {ToLengthPercentX(rect.Width)}%; height: {ToPercentY(rect.Height)}%;",
            rect);

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
                await PersistBehaviorProfilesAsync(force: true);
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

        if (_jsReady)
        {
            await TryPlayRadarPingAsync();
        }

        UpdateTimedItemEffects();

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
                NowSeconds = _sessionStopwatch.Elapsed.TotalSeconds,
                PlayerFacing = PlayerFacing,
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
        await PersistBehaviorProfilesAsync();
        StateHasChanged();
    }

    private async ValueTask TryPlayRadarPingAsync()
    {
        var cycleProgress = GetRadarPulseCycleProgress();
        var shiftedProgress = cycleProgress + RadarPingLeadFraction;
        if (shiftedProgress >= 1d)
        {
            shiftedProgress -= 1d;
        }

        var phaseIndex = Math.Clamp((int)Math.Floor(shiftedProgress * RadarPulseEventsPerCycle), 0, RadarPulseEventsPerCycle - 1);
        if (_lastRadarPulsePhaseIndex < 0)
        {
            _lastRadarPulsePhaseIndex = phaseIndex;
            return;
        }

        if (phaseIndex == _lastRadarPulsePhaseIndex)
        {
            return;
        }

        _lastRadarPulsePhaseIndex = phaseIndex;
        await JS.InvokeVoidAsync("enigmaGame.playRadarPing");
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

        await PersistBehaviorProfilesAsync(force: true);
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

        if (CanInteractWithPuzzleConsole &&
            IntersectsAnyPuzzleConsoleBase(GetPuzzleConsoleCollisionBounds(new PlayAreaRect(PlayerX, PlayerY, PlayerSize, PlayerSize))))
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
            RecordDoorRushAttempt();
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
        _nearestWorldInteractableId = null;
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
        _ = ReportTutorialRoomTransitionAsync(CurrentRoom.Coordinates);
        return true;
    }

    private async Task ReportTutorialRoomTransitionAsync(GridPoint currentRoomCoordinates)
    {
        if (!IsTutorialRun || _tutorialRoomTransitionReported || !_jsReady || _tutorialStartRoomCoordinates is null)
        {
            return;
        }

        if (currentRoomCoordinates == _tutorialStartRoomCoordinates.Value)
        {
            return;
        }

        _tutorialRoomTransitionReported = true;
        try
        {
            await JS.InvokeVoidAsync("enigmaGame.reportTutorialObjective", "tutorial-demo-room-transition");
        }
        catch
        {
        }
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
        var doorRect = isHorizontal
            ? new PlayAreaRect(doorStart, edge, DoorWidth, WallThickness)
            : new PlayAreaRect(edge, doorStart, WallThickness, DoorWidth);
        var doorStyle = isHorizontal
            ? $"left: {ToMappedPercentX(doorStart)}%; top: {ToPercentY(edge)}%; width: {ToMappedSpanPercentX(DoorWidth)}%; height: {ToPercentY(WallThickness)}%;"
            : $"left: {ToPositionPercentX(edge, WallThickness)}%; top: {ToPercentY(doorStart)}%; width: {ToLengthPercentX(WallThickness)}%; height: {ToPercentY(DoorWidth)}%;";
        yield return new WallSegment(
            $"door-glow {side} {GetRadarBandClass(doorRect, anomalySensitive: true)}",
            AppendRadarSignalStyle(doorStyle, doorRect, anomalySensitive: true),
            doorRect);
    }

    private WallSegment CreateWallSegment(bool isHorizontal, double fixedAxisValue, double start, double length, string side)
    {
        var segmentRect = isHorizontal
            ? new PlayAreaRect(start, fixedAxisValue, length, WallThickness)
            : new PlayAreaRect(fixedAxisValue, start, WallThickness, length);
        var segmentStyle = isHorizontal
            ? $"left: {ToMappedPercentX(start)}%; top: {ToPercentY(fixedAxisValue)}%; width: {ToMappedSpanPercentX(length)}%; height: {ToPercentY(WallThickness)}%;"
            : $"left: {ToPositionPercentX(fixedAxisValue, WallThickness)}%; top: {ToPercentY(start)}%; width: {ToLengthPercentX(WallThickness)}%; height: {ToPercentY(length)}%;";

        return new WallSegment(
            $"{side} {GetRadarBandClass(segmentRect)}",
            AppendRadarSignalStyle(segmentStyle, segmentRect),
            segmentRect);
    }

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
                PlayerX = HorizontalWallCollisionInset;
                break;
            case PlayerDirection.Right:
                PlayerX = RoomSize - PlayerSize - HorizontalWallCollisionInset;
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
        var minX = CanUseDoorBand(PlayerDirection.Left) ? 0d : HorizontalWallCollisionInset;
        var maxX = CanUseDoorBand(PlayerDirection.Right) ? RoomSize - PlayerSize : RoomSize - PlayerSize - HorizontalWallCollisionInset;
        var minY = CanUseDoorBand(PlayerDirection.Up) ? 0d : WallThickness;
        var maxY = CanUseDoorBand(PlayerDirection.Down) ? RoomSize - PlayerSize : RoomSize - PlayerSize - WallThickness;

        PlayerX = Math.Clamp(PlayerX, minX, maxX);
        PlayerY = Math.Clamp(PlayerY, minY, maxY);
    }

    private bool CanUseDoorBand(PlayerDirection direction) =>
        CurrentRoom is not null &&
        CurrentRoom.Connections.HasDoor(direction) &&
        IsWithinDoorway(direction) &&
        (IsCoopRun ? IsCurrentCoopRoomSolved : CurrentRoomState?.Puzzle.IsCompleted == true);

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
        _consumedItemIds.Clear();
        _loadoutConsumptionPrimed = false;
        _timerPauseUntilUtc = DateTime.MinValue;
        _visionBoostUntilUtc = DateTime.MinValue;
        _pathRevealUntilUtc = DateTime.MinValue;
        _lastRadarPulsePhaseIndex = -1;
        _lastPlayerStateSyncUtc = DateTime.MinValue;
        _playerStateDirty = true;
        _roomInteractionWasActive = false;
        _nearestWorldInteractableId = null;
        _behaviorProfileLoaded = false;
        _behaviorProfileDirty = false;
        _lastBehaviorProfilePersistUtc = DateTime.MinValue;
        _globalBehaviorProfile = new BehaviorProfileSnapshot();
        _seedBehaviorProfile = new BehaviorProfileSnapshot();
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
        if (CurrentRoomState?.Puzzle is null || CurrentRoomState.Puzzle.IsCompleted)
        {
            return false;
        }

        if (CurrentRoomState.Puzzle is IWorldInteractivePuzzle worldPuzzle &&
            worldPuzzle.TryGetProgressState(out var worldProgress))
        {
            var normalizedProgress = Math.Clamp(worldProgress.Progress, 0d, 1d);
            if (normalizedProgress >= 0.999d)
            {
                return false;
            }

            progressState = new RoomInteractionProgressState(
                worldProgress.AnchorRect,
                normalizedProgress,
                worldProgress.Label);
            return true;
        }

        switch (CurrentRoomState.Puzzle)
        {
            case PressurePlatePuzzle pressurePlate when pressurePlate.Progress > 0d:
                if (pressurePlate.Progress >= 0.999d)
                {
                    return false;
                }

                progressState = new(pressurePlate.PlateBounds, pressurePlate.Progress, "Plate Charge");
                return true;
            case ZoneActivationPuzzle zoneActivation when zoneActivation.CurrentZoneIndex < zoneActivation.Zones.Count && zoneActivation.HoldProgress > 0d:
                if (zoneActivation.HoldProgress >= 0.999d)
                {
                    return false;
                }

                progressState = new(
                    zoneActivation.Zones[zoneActivation.CurrentZoneIndex],
                    zoneActivation.HoldProgress,
                    $"Beacon {zoneActivation.CurrentZoneIndex + 1}/{zoneActivation.Zones.Count}");
                return true;
            case DelayedActivationSequencePuzzle delayed when !delayed.IsWaitingForNextZone && delayed.HoldProgress > 0d:
                if (delayed.HoldProgress >= 0.999d)
                {
                    return false;
                }

                progressState = new(
                    delayed.Zones[delayed.ActiveZoneIndex],
                    delayed.HoldProgress,
                    $"Zone {delayed.CurrentStepIndex + 1}/{delayed.Order.Count}");
                return true;
            case RecursiveActivationSequencePuzzle recursive when recursive.CurrentZoneIndex >= 0 && recursive.HoldProgress > 0d:
                if (recursive.HoldProgress >= 0.999d)
                {
                    return false;
                }

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

        if (CurrentRoomState.Puzzle is IWorldInteractivePuzzle worldPuzzle)
        {
            if (TryFindNearestWorldInteractableCandidate(worldPuzzle, null) is not null)
            {
                return true;
            }

            return worldPuzzle.TryGetProgressState(out var worldProgress) && worldProgress.Progress > 0.001d;
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

    private IEnumerable<ActivePuzzleConsole> GetActivePuzzleConsoles()
    {
        if (!CanInteractWithPuzzleConsole)
        {
            yield break;
        }

        if (!IsCoopRun)
        {
            yield return new ActivePuzzleConsole(GetCurrentPuzzleConsoleBounds(), IsLocal: true);
            yield break;
        }

        var (ownerConsole, guestConsole) = GetCoopConsolePairBounds();
        if (IsLocalCoopOwner())
        {
            yield return new ActivePuzzleConsole(ownerConsole, IsLocal: true);
            yield return new ActivePuzzleConsole(guestConsole, IsLocal: false);
        }
        else
        {
            yield return new ActivePuzzleConsole(guestConsole, IsLocal: true);
            yield return new ActivePuzzleConsole(ownerConsole, IsLocal: false);
        }
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

    private PlayAreaRect GetLocalPuzzleConsoleBounds()
    {
        if (!IsCoopRun)
        {
            return GetCurrentPuzzleConsoleBounds();
        }

        var (ownerConsole, guestConsole) = GetCoopConsolePairBounds();
        return IsLocalCoopOwner() ? ownerConsole : guestConsole;
    }

    private (PlayAreaRect OwnerConsole, PlayAreaRect GuestConsole) GetCoopConsolePairBounds()
    {
        if (CurrentRoom is null || ParsedSeed is null || PuzzleConsoleCandidates.Length < 2)
        {
            return (PuzzleConsoleCandidates[0], PuzzleConsoleCandidates[Math.Min(1, PuzzleConsoleCandidates.Length - 1)]);
        }

        var stableKey = $"{ParsedSeed.RawSeed}|{CurrentRoom.Coordinates.X}|{CurrentRoom.Coordinates.Y}|{CurrentRoom.ConnectionKey}|{CurrentRoom.PuzzleKey}|coop-console";
        var ownerIndex = GetStableHash(stableKey) % PuzzleConsoleCandidates.Length;
        var ownerConsole = PuzzleConsoleCandidates[ownerIndex];
        var guestIndex = -1;

        var scanSeed = GetStableHash($"{stableKey}|guest-scan");
        for (var offset = 1; offset < PuzzleConsoleCandidates.Length; offset++)
        {
            var candidateIndex = (ownerIndex + offset + scanSeed) % PuzzleConsoleCandidates.Length;
            if (candidateIndex == ownerIndex)
            {
                continue;
            }

            var candidate = PuzzleConsoleCandidates[candidateIndex];
            if (Distance(ownerConsole, candidate) >= CoopConsoleMinSeparation)
            {
                guestIndex = candidateIndex;
                break;
            }
        }

        if (guestIndex < 0)
        {
            var farthestDistance = double.MinValue;
            for (var index = 0; index < PuzzleConsoleCandidates.Length; index++)
            {
                if (index == ownerIndex)
                {
                    continue;
                }

                var candidate = PuzzleConsoleCandidates[index];
                var candidateDistance = Distance(ownerConsole, candidate);
                if (candidateDistance > farthestDistance)
                {
                    farthestDistance = candidateDistance;
                    guestIndex = index;
                }
            }
        }

        if (guestIndex < 0)
        {
            guestIndex = (ownerIndex + 1) % PuzzleConsoleCandidates.Length;
        }

        return (ownerConsole, PuzzleConsoleCandidates[guestIndex]);
    }

    private bool IsLocalCoopOwner()
    {
        if (_coopSession?.You is not null)
        {
            return string.Equals(_coopSession.You.Role, "owner", StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(_coopSession?.OwnerUsername, Username, StringComparison.OrdinalIgnoreCase);
    }

    private static double Distance(PlayAreaRect a, PlayAreaRect b)
    {
        var dx = a.CenterX - b.CenterX;
        var dy = a.CenterY - b.CenterY;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static IEnumerable<PlayAreaRect> GetPuzzleConsoleBarrierBounds(PlayAreaRect consoleBounds)
    {
        // Keep side blocking tight so players can approach close to the console edges.
        yield return new PlayAreaRect(
            consoleBounds.X + (consoleBounds.Width * 0.29d),
            consoleBounds.Y + (consoleBounds.Height * 0.74d),
            consoleBounds.Width * 0.42d,
            consoleBounds.Height * 0.16d);

        // Add a lower lip barrier so the console base blocks from underneath.
        yield return new PlayAreaRect(
            consoleBounds.X + (consoleBounds.Width * 0.33d),
            consoleBounds.Y + (consoleBounds.Height * 0.88d),
            consoleBounds.Width * 0.34d,
            consoleBounds.Height * 0.16d);
    }

    private static PlayAreaRect GetPuzzleConsoleCollisionBounds(PlayAreaRect playerBounds) =>
        new(
            playerBounds.X + (playerBounds.Width * 0.22d),
            playerBounds.Y + (playerBounds.Height * 0.5d),
            playerBounds.Width * 0.56d,
            playerBounds.Height * 0.46d);

    private bool IntersectsAnyPuzzleConsoleBase(PlayAreaRect playerBounds)
    {
        foreach (var puzzleConsole in GetActivePuzzleConsoles())
        {
            foreach (var barrierBounds in GetPuzzleConsoleBarrierBounds(puzzleConsole.Bounds))
            {
                if (barrierBounds.Intersects(playerBounds))
                {
                    return true;
                }
            }
        }

        return false;
    }

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
        var midpoint = new PlayAreaRect(
            (start.X + end.X) / 2d,
            (start.Y + end.Y) / 2d,
            1d,
            1d);
        return AppendRadarSignalStyle(
            $"left: {Math.Round((scaledStartX / RenderWidth) * 100d, 4)}%; top: {ToPercentY(start.Y)}%; width: {ToLengthPercentX(length)}%; transform: rotate({Math.Round(angle, 2)}deg);",
            midpoint);
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
        SignalRoutingChamberPuzzle => new(
            "Route power through stable relays.",
            "Use E or click on relay nodes. Overload relays vent and briefly lock input.",
            "Only the correct stable relay set remains active."),
        EchoMemoryChamberPuzzle => new(
            "Rebuild the echo sequence.",
            "Watch the pad reveal, then repeat the pad order by stepping or interacting in sequence.",
            "The full sequence is repeated without errors."),
        DualLayerRealityPuzzle => new(
            "Synchronize both room layers.",
            "Use the layer toggle, then activate the correct node in each layer pair.",
            "Every alpha/beta pair is matched."),
        BehaviorAdaptivePuzzle => new(
            "Break your habitual route.",
            "The chamber adapts to your tendencies. Choose deliberately instead of repeating instincts.",
            "All behavior phases are cleared."),
        RecursiveRoomMutationPuzzle => new(
            "Identify the meaningful mutation each loop.",
            "Interact with the single meaningful anchor each loop. Wrong picks roll one loop back.",
            "All loops resolve."),
        LivingGridPuzzle => new(
            "Stabilize the living grid.",
            "Stepping or interacting with a tile flips nearby states. Plan ahead and align the board.",
            "Current grid matches target state."),
        SymbolDecoderPuzzle => new(
            "Decode with fixed symbol semantics.",
            "A, B, C, and D always apply the same operations every run.",
            "Decoder value matches target."),
        TimeWindowPuzzle => new(
            "Capture the cycle windows.",
            "Interact only when the expected gate is open in the active cycle.",
            "All cycle locks complete."),
        FalseSolutionPuzzle => new(
            "Ignore deceptive progress.",
            "Decoy terminals can look correct briefly. Confirm the true route chain.",
            "True route is fully confirmed."),
        HeatPressureBalancePuzzle => new(
            "Hold both systems in equilibrium.",
            "Use heat/pressure controls and account for delayed effects to keep both systems centered.",
            "Equilibrium stays stable long enough to lock."),
        HiddenRulePrimePuzzle => new(
            "Infer the hidden acceptance rule.",
            "Test tiles and observe accepted order. There is no explicit rule text.",
            "The full hidden order is entered."),
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

    private async Task LoadBehaviorProfilesAsync()
    {
        if (ParsedSeed is null)
        {
            return;
        }

        var globalKey = GetBehaviorStorageKey(seedSpecific: false);
        var seedKey = GetBehaviorStorageKey(seedSpecific: true);
        try
        {
            _globalBehaviorProfile = await JS.InvokeAsync<BehaviorProfileSnapshot?>("enigmaGame.localGetJson", globalKey) ?? new BehaviorProfileSnapshot();
            _seedBehaviorProfile = await JS.InvokeAsync<BehaviorProfileSnapshot?>("enigmaGame.localGetJson", seedKey) ?? new BehaviorProfileSnapshot();
        }
        catch
        {
            _globalBehaviorProfile = new BehaviorProfileSnapshot();
            _seedBehaviorProfile = new BehaviorProfileSnapshot();
        }

        _behaviorProfileLoaded = true;
    }

    private async Task PersistBehaviorProfilesAsync(bool force = false)
    {
        if (!_jsReady || ParsedSeed is null || !_behaviorProfileLoaded || !_behaviorProfileDirty)
        {
            return;
        }

        if (!force && DateTime.UtcNow - _lastBehaviorProfilePersistUtc < TimeSpan.FromMilliseconds(550))
        {
            return;
        }

        var globalKey = GetBehaviorStorageKey(seedSpecific: false);
        var seedKey = GetBehaviorStorageKey(seedSpecific: true);
        try
        {
            await JS.InvokeVoidAsync("enigmaGame.localSetJson", globalKey, _globalBehaviorProfile);
            await JS.InvokeVoidAsync("enigmaGame.localSetJson", seedKey, _seedBehaviorProfile);
            _behaviorProfileDirty = false;
            _lastBehaviorProfilePersistUtc = DateTime.UtcNow;
        }
        catch
        {
        }
    }

    private string GetBehaviorStorageKey(bool seedSpecific)
    {
        var normalizedUser = string.IsNullOrWhiteSpace(Username)
            ? "guest"
            : Username.Trim().ToLowerInvariant();

        if (!seedSpecific || ParsedSeed is null)
        {
            return $"{BehaviorStoragePrefix}.{normalizedUser}.global";
        }

        return $"{BehaviorStoragePrefix}.{normalizedUser}.{ParsedSeed.RawSeed}";
    }

    private void RecordBehaviorInteraction(PlayAreaRect interactableBounds)
    {
        if (!_behaviorProfileLoaded)
        {
            return;
        }

        var playerCenterX = PlayerX + (PlayerSize / 2d);
        if (interactableBounds.CenterX < playerCenterX)
        {
            _globalBehaviorProfile.LeftInteractions++;
            _seedBehaviorProfile.LeftInteractions++;
        }
        else
        {
            _globalBehaviorProfile.RightInteractions++;
            _seedBehaviorProfile.RightInteractions++;
        }

        _globalBehaviorProfile.InteractionSamples++;
        _seedBehaviorProfile.InteractionSamples++;
        _behaviorProfileDirty = true;
        ApplyBehaviorProfileToEasyPuzzles();
    }

    private void RecordDoorRushAttempt()
    {
        if (!_behaviorProfileLoaded)
        {
            return;
        }

        _globalBehaviorProfile.DoorRushAttempts++;
        _seedBehaviorProfile.DoorRushAttempts++;
        _behaviorProfileDirty = true;
        ApplyBehaviorProfileToEasyPuzzles();
    }

    private void ApplyBehaviorProfileToEasyPuzzles()
    {
        var (horizontalBias, rushBias) = GetBehaviorBias();
        foreach (var roomState in _roomStates.Values)
        {
            if (roomState.Puzzle is IBehaviorAdaptiveWorldPuzzle adaptivePuzzle)
            {
                adaptivePuzzle.ApplyBehaviorProfile(horizontalBias, rushBias);
            }
        }
    }

    private (double HorizontalBias, double RushBias) GetBehaviorBias()
    {
        var globalHorizontal = ComputeHorizontalBias(_globalBehaviorProfile);
        var seedHorizontal = ComputeHorizontalBias(_seedBehaviorProfile);
        var horizontal = Math.Clamp((globalHorizontal * 0.65d) + (seedHorizontal * 0.35d), -BehaviorAdaptationCap, BehaviorAdaptationCap);

        var globalRush = ComputeRushBias(_globalBehaviorProfile);
        var seedRush = ComputeRushBias(_seedBehaviorProfile);
        var rush = Math.Clamp((globalRush * 0.55d) + (seedRush * 0.45d), 0d, BehaviorAdaptationCap);

        return (horizontal, rush);
    }

    private static double ComputeHorizontalBias(BehaviorProfileSnapshot profile)
    {
        var total = profile.LeftInteractions + profile.RightInteractions;
        if (total <= 0)
        {
            return 0d;
        }

        return (profile.LeftInteractions - profile.RightInteractions) / (double)total;
    }

    private static double ComputeRushBias(BehaviorProfileSnapshot profile)
    {
        var denominator = Math.Max(1, profile.InteractionSamples + profile.DoorRushAttempts);
        return profile.DoorRushAttempts / (double)denominator;
    }

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

    protected IEnumerable<RunLoadoutSelection> GetVisibleLoadoutItems() =>
        EquippedLoadout.Where(item => !string.IsNullOrWhiteSpace(item.ItemId) && item.Quantity > 0);

    private List<RunLoadoutSelection> GetActiveLoadoutItems() =>
        GetVisibleLoadoutItems()
            .Where(CanActivateLoadoutItem)
            .Take(3)
            .ToList();

    protected bool CanActivateLoadoutItem(RunLoadoutSelection item)
    {
        if (item is null || item.Quantity <= 0)
        {
            return false;
        }

        var slotKind = (item.SlotKind ?? string.Empty).Trim().ToLowerInvariant();
        if (slotKind is "passive" or "perk")
        {
            return false;
        }

        var effectType = GetLoadoutEffectType(item);
        return effectType switch
        {
            "trap_block" => false,
            "reward_multiplier" => false,
            "permanent_founder_bonus" => false,
            _ => true,
        };
    }

    protected bool CanUseLoadoutItem(RunLoadoutSelection item)
    {
        if (!CanActivateLoadoutItem(item))
        {
            return false;
        }

        return EquippedLoadout.Any(entry =>
            string.Equals(entry.ItemId, item.ItemId, StringComparison.OrdinalIgnoreCase) &&
            entry.Quantity > 0);
    }

    private static bool TryMapLoadoutHotkey(string keyCode, out int slotIndex)
    {
        slotIndex = keyCode switch
        {
            "Digit1" or "Numpad1" => 0,
            "Digit2" or "Numpad2" => 1,
            "Digit3" or "Numpad3" => 2,
            _ => -1,
        };

        return slotIndex >= 0;
    }

    private bool TryGetLoadoutItemIdForHotkey(string keyCode, out string itemId)
    {
        itemId = string.Empty;
        if (!TryMapLoadoutHotkey(keyCode, out var slotIndex))
        {
            return false;
        }

        var activeItems = GetActiveLoadoutItems();
        if (slotIndex < 0 || slotIndex >= activeItems.Count)
        {
            return false;
        }

        itemId = activeItems[slotIndex].ItemId;
        return !string.IsNullOrWhiteSpace(itemId);
    }

    protected async Task UseLoadoutItemFromUiAsync(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return;
        }

        var item = EquippedLoadout.FirstOrDefault(entry =>
            string.Equals(entry.ItemId, itemId, StringComparison.OrdinalIgnoreCase));

        if (item is null || !CanUseLoadoutItem(item))
        {
            return;
        }

        if (!ApplyLoadoutItemEffect(item))
        {
            return;
        }

        item.Quantity = Math.Max(0, item.Quantity - 1);
        if (item.Quantity <= 0)
        {
            EquippedLoadout.Remove(item);
        }

        RecordConsumedLoadoutItem(item.ItemId);
        _playerStateDirty = true;

        try
        {
            await JS.InvokeVoidAsync("enigmaGame.setRunLoadout", EquippedLoadout);
        }
        catch
        {
        }

        await InvokeAsync(StateHasChanged);
    }

    protected string GetLoadoutHotkeyLabel(RunLoadoutSelection item)
    {
        if (item is null)
        {
            return "item";
        }

        var activeItems = GetActiveLoadoutItems();

        var index = activeItems.FindIndex(candidate =>
            string.Equals(candidate.ItemId, item.ItemId, StringComparison.OrdinalIgnoreCase));

        return index >= 0 ? (index + 1).ToString(CultureInfo.InvariantCulture) : "item";
    }

    private void PrimePassiveLoadoutConsumption()
    {
        if (_loadoutConsumptionPrimed)
        {
            return;
        }

        _consumedItemIds.Clear();
        foreach (var item in EquippedLoadout)
        {
            if (string.IsNullOrWhiteSpace(item.ItemId) || item.Quantity <= 0)
            {
                continue;
            }

            if (CanActivateLoadoutItem(item))
            {
                continue;
            }

            for (var index = 0; index < item.Quantity; index++)
            {
                _consumedItemIds.Add(item.ItemId);
            }
        }

        _loadoutConsumptionPrimed = true;
    }

    private void RecordConsumedLoadoutItem(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return;
        }

        _consumedItemIds.Add(itemId);
    }

    private static string GetLoadoutEffectType(RunLoadoutSelection item)
    {
        if (item.EffectConfig is null || !item.EffectConfig.TryGetValue("type", out var rawType) || rawType is null)
        {
            return string.Empty;
        }

        if (rawType is JsonElement jsonElement)
        {
            return jsonElement.ValueKind == JsonValueKind.String
                ? jsonElement.GetString()?.Trim().ToLowerInvariant() ?? string.Empty
                : string.Empty;
        }

        return rawType.ToString()?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    private static double GetLoadoutEffectNumber(RunLoadoutSelection item, string key, double fallback)
    {
        if (item.EffectConfig is null || !item.EffectConfig.TryGetValue(key, out var rawValue) || rawValue is null)
        {
            return fallback;
        }

        if (rawValue is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.Number && jsonElement.TryGetDouble(out var jsonNumber))
            {
                return jsonNumber;
            }

            if (jsonElement.ValueKind == JsonValueKind.String
                && double.TryParse(jsonElement.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedText))
            {
                return parsedText;
            }

            return fallback;
        }

        try
        {
            return Convert.ToDouble(rawValue, CultureInfo.InvariantCulture);
        }
        catch
        {
            return fallback;
        }
    }

    private bool ApplyLoadoutItemEffect(RunLoadoutSelection item)
    {
        var effectType = GetLoadoutEffectType(item);
        return effectType switch
        {
            "skip_puzzle" => TryApplyPuzzleSkipEffect(item),
            "timer_pause_seconds" => TryApplyTimerPauseEffect(item),
            "direction_hint" => TryApplyDirectionHintEffect(item),
            "puzzle_hint" => TryApplyPuzzleHintEffect(item),
            "vision_boost" => TryApplyVisionBoostEffect(item),
            "path_reveal" => TryApplyPathRevealEffect(item),
            _ => TryApplyDefaultLoadoutEffect(item),
        };
    }

    private bool TryApplyPuzzleSkipEffect(RunLoadoutSelection item)
    {
        if (IsCoopRun)
        {
            ShowBanner("Puzzle Skip is unavailable in co-op runs.", 1.1d);
            return false;
        }

        if (CurrentRoomState is null || CurrentRoomState.Puzzle.IsCompleted)
        {
            ShowBanner("This room is already unlocked.", 1.0d);
            return false;
        }

        CurrentRoomState.Puzzle.SyncCompleted("Puzzle bypassed.");
        var reward = CurrentRoomState.GrantPuzzleReward();
        if (reward > 0)
        {
            TotalGold += reward;
        }

        ClosePuzzleOverlay();
        ShowBanner(
            reward > 0
                ? $"{item.Name} bypassed the puzzle. +{reward} MN"
                : $"{item.Name} bypassed the puzzle.",
            1.5d);
        return true;
    }

    private bool TryApplyTimerPauseEffect(RunLoadoutSelection item)
    {
        var pauseSeconds = GetLoadoutEffectNumber(item, "value", 20d);
        if (pauseSeconds <= 0d)
        {
            pauseSeconds = GetLoadoutEffectNumber(item, "duration_seconds", 20d);
        }

        pauseSeconds = Math.Clamp(pauseSeconds, 1d, 120d);
        var now = DateTime.UtcNow;
        var pauseStart = _timerPauseUntilUtc > now ? _timerPauseUntilUtc : now;
        _timerPauseUntilUtc = pauseStart.AddSeconds(pauseSeconds);
        if (_sessionStopwatch.IsRunning)
        {
            _sessionStopwatch.Stop();
        }

        ShowBanner($"{item.Name} froze the run timer for {pauseSeconds:0.#}s.", 1.4d);
        return true;
    }

    private bool TryApplyDirectionHintEffect(RunLoadoutSelection item)
    {
        if (ParsedSeed is null || CurrentRoom is null)
        {
            ShowBanner("Compass data unavailable in this room.", 1.1d);
            return false;
        }

        var duration = Math.Clamp(GetLoadoutEffectNumber(item, "duration_seconds", 10d), 2d, 12d);
        var path = TryFindRoute(CurrentRoom.Coordinates, ParsedSeed.FinishRoom.Coordinates);
        if (path is null)
        {
            ShowBanner("Compass cannot resolve a route right now.", 1.1d);
            return false;
        }

        if (path.Count == 0)
        {
            ShowBanner($"{item.Name}: you are already in the finish room.", duration);
            return true;
        }

        var firstDirection = DirectionToWord(path[0]);
        ShowBanner($"{item.Name}: move {firstDirection}. {FormatDirectionPath(path)}", duration);
        return true;
    }

    private bool TryApplyPuzzleHintEffect(RunLoadoutSelection item)
    {
        if (CurrentRoomState?.Puzzle is null)
        {
            ShowBanner("No puzzle is active in this room.", 1.1d);
            return false;
        }

        var hintText = BuildRuntimePuzzleHint();
        ShowBanner($"{item.Name}: {hintText}", 5.5d);
        return true;
    }

    private bool TryApplyVisionBoostEffect(RunLoadoutSelection item)
    {
        var duration = Math.Clamp(GetLoadoutEffectNumber(item, "duration_seconds", 12d), 2d, 60d);
        var now = DateTime.UtcNow;
        var boostStart = _visionBoostUntilUtc > now ? _visionBoostUntilUtc : now;
        _visionBoostUntilUtc = boostStart.AddSeconds(duration);
        ShowBanner($"{item.Name} active for {duration:0.#}s. Interaction range increased.", 2.2d);
        return true;
    }

    private bool TryApplyPathRevealEffect(RunLoadoutSelection item)
    {
        if (CurrentRoom is null)
        {
            ShowBanner("Pathfinder route unavailable.", 1.1d);
            return false;
        }

        var duration = Math.Clamp(GetLoadoutEffectNumber(item, "duration_seconds", 10d), 2d, 20d);
        var now = DateTime.UtcNow;
        var revealStart = _pathRevealUntilUtc > now ? _pathRevealUntilUtc : now;
        _pathRevealUntilUtc = revealStart.AddSeconds(duration);

        var route = FindNearestObjectiveRoute();
        if (route.Path is null)
        {
            ShowBanner("Pathfinder cannot resolve a route right now.", 1.1d);
            return false;
        }

        if (route.Path.Count == 0)
        {
            ShowBanner($"{item.Name}: objective already reached in this room.", duration);
            return true;
        }

        ShowBanner($"{item.Name}: {route.TargetLabel} -> {FormatDirectionPath(route.Path)}", duration);
        return true;
    }

    private bool TryApplyDefaultLoadoutEffect(RunLoadoutSelection item)
    {
        ShowBanner($"{item.Name} has no active runtime effect in this room.", 1.1d);
        return false;
    }

    private string BuildRuntimePuzzleHint()
    {
        if (CurrentRoomState?.Puzzle is null)
        {
            return "No puzzle is currently active.";
        }

        return CurrentRoomState.Puzzle switch
        {
            SignalRoutingChamberPuzzle routing =>
                $"Set relays to the exact stable configuration. Current match {Math.Round((routing.TryGetProgressState(out var progress) ? progress.Progress : 0d) * 100d)}%.",
            EchoMemoryChamberPuzzle =>
                "Wait for the echo to finish, then reproduce the pad order without repeating the wrong pad.",
            DualLayerRealityPuzzle =>
                "Toggle layers and match the linked node in each pair before moving to the next pair.",
            BehaviorAdaptivePuzzle =>
                "Break your instinct. If you always choose the same side first, choose differently.",
            RecursiveRoomMutationPuzzle =>
                "Across each loop, choose only the meaningful change. Wrong picks roll back one loop.",
            LivingGridPuzzle =>
                "Each activation toggles neighbors. Think several moves ahead and match the grid state.",
            SymbolDecoderPuzzle =>
                $"Symbol language is fixed: A={SymbolDecoderPuzzle.DescribeSymbol("A")}, B={SymbolDecoderPuzzle.DescribeSymbol("B")}, C={SymbolDecoderPuzzle.DescribeSymbol("C")}, D={SymbolDecoderPuzzle.DescribeSymbol("D")}.",
            TimeWindowPuzzle =>
                "Act only while the correct gate is open. Wrong timing drops your progress.",
            FalseSolutionPuzzle =>
                "Some routes pretend to progress. Confirm the quiet route twice to finish.",
            HeatPressureBalancePuzzle heatPressure =>
                $"{heatPressure.BuildTelemetry()} - keep both in the center band long enough to lock equilibrium.",
            HiddenRulePrimePuzzle =>
                "The rule is never stated. Test tiles, watch what the room accepts, and infer the sequence.",
            QuickTimePuzzle quickTime =>
                $"Stop the pulse inside the bright window. Clean hits: {quickTime.SuccessfulHits}/{quickTime.RequiredHits}.",
            SequenceMemoryPuzzle sequenceMemory =>
                sequenceMemory.IsSequenceVisible
                    ? "Memorize now. Input starts after the rune sequence disappears."
                    : $"Enter the rune order from memory. Progress {sequenceMemory.Entered.Count}/{sequenceMemory.Sequence.Count}.",
            UnlockPatternPuzzle unlockPattern =>
                unlockPattern.IsPatternVisible
                    ? "Wait for the preview to vanish, then replay the pattern."
                    : $"Replay the pattern exactly. Progress {unlockPattern.Entered.Count}/{unlockPattern.Pattern.Count}.",
            YarnUntanglePuzzle untangle =>
                $"Swap right endpoints until crossings are zero. Current crossings: {untangle.CrossingCount}.",
            _ => GetCurrentPuzzleGuide().Controls,
        };
    }

    private (List<PlayerDirection>? Path, string TargetLabel) FindNearestObjectiveRoute()
    {
        if (ParsedSeed is null || CurrentRoom is null)
        {
            return (null, "Objective");
        }

        List<PlayerDirection>? bestPath = null;
        var bestLabel = "finish room";

        foreach (var room in ParsedSeed.Rooms.Values.Where(room => room.Kind == MazeRoomKind.Reward))
        {
            if (!_roomStates.TryGetValue(room.Coordinates, out var state) || state.RewardPickupCollected)
            {
                continue;
            }

            var rewardPath = TryFindRoute(CurrentRoom.Coordinates, room.Coordinates);
            if (rewardPath is null)
            {
                continue;
            }

            if (bestPath is null || rewardPath.Count < bestPath.Count)
            {
                bestPath = rewardPath;
                bestLabel = "nearest reward";
            }
        }

        if (bestPath is not null)
        {
            return (bestPath, bestLabel);
        }

        var finishPath = TryFindRoute(CurrentRoom.Coordinates, ParsedSeed.FinishRoom.Coordinates);
        return (finishPath, "finish room");
    }

    private List<PlayerDirection>? TryFindRoute(GridPoint start, GridPoint target)
    {
        if (ParsedSeed is null || !ParsedSeed.TryGetRoom(start, out _))
        {
            return null;
        }

        if (start == target)
        {
            return [];
        }

        var queue = new Queue<GridPoint>();
        var visited = new HashSet<GridPoint> { start };
        var previous = new Dictionary<GridPoint, (GridPoint Parent, PlayerDirection Direction)>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var currentPoint = queue.Dequeue();
            if (!ParsedSeed.TryGetRoom(currentPoint, out var currentRoom))
            {
                continue;
            }

            foreach (var (nextPoint, direction) in EnumerateConnectedNeighbors(currentRoom))
            {
                if (!visited.Add(nextPoint))
                {
                    continue;
                }

                previous[nextPoint] = (currentPoint, direction);
                if (nextPoint == target)
                {
                    return BuildDirectionPath(previous, start, target);
                }

                queue.Enqueue(nextPoint);
            }
        }

        return null;
    }

    private IEnumerable<(GridPoint Point, PlayerDirection Direction)> EnumerateConnectedNeighbors(MazeRoomDefinition room)
    {
        if (ParsedSeed is null)
        {
            yield break;
        }

        if (room.Connections.North)
        {
            var next = new GridPoint(room.Coordinates.X, room.Coordinates.Y - 1);
            if (ParsedSeed.TryGetRoom(next, out _))
            {
                yield return (next, PlayerDirection.Up);
            }
        }

        if (room.Connections.East)
        {
            var next = new GridPoint(room.Coordinates.X + 1, room.Coordinates.Y);
            if (ParsedSeed.TryGetRoom(next, out _))
            {
                yield return (next, PlayerDirection.Right);
            }
        }

        if (room.Connections.South)
        {
            var next = new GridPoint(room.Coordinates.X, room.Coordinates.Y + 1);
            if (ParsedSeed.TryGetRoom(next, out _))
            {
                yield return (next, PlayerDirection.Down);
            }
        }

        if (room.Connections.West)
        {
            var next = new GridPoint(room.Coordinates.X - 1, room.Coordinates.Y);
            if (ParsedSeed.TryGetRoom(next, out _))
            {
                yield return (next, PlayerDirection.Left);
            }
        }
    }

    private static List<PlayerDirection> BuildDirectionPath(
        IReadOnlyDictionary<GridPoint, (GridPoint Parent, PlayerDirection Direction)> previous,
        GridPoint start,
        GridPoint target)
    {
        var path = new List<PlayerDirection>();
        var cursor = target;
        while (cursor != start && previous.TryGetValue(cursor, out var previousStep))
        {
            path.Add(previousStep.Direction);
            cursor = previousStep.Parent;
        }

        path.Reverse();
        return path;
    }

    private static string FormatDirectionPath(IReadOnlyList<PlayerDirection> path)
    {
        if (path.Count == 0)
        {
            return "Already at target.";
        }

        var previewSteps = path.Take(6).Select(DirectionToWord).ToList();
        var extraSteps = path.Count - previewSteps.Count;
        var suffix = extraSteps > 0 ? $" (+{extraSteps} more)" : string.Empty;
        return $"Route: {string.Join(" -> ", previewSteps)}{suffix}.";
    }

    private static string DirectionToWord(PlayerDirection direction) => direction switch
    {
        PlayerDirection.Up => "up",
        PlayerDirection.Right => "right",
        PlayerDirection.Down => "down",
        PlayerDirection.Left => "left",
        _ => "forward",
    };

    private void UpdateTimedItemEffects()
    {
        var now = DateTime.UtcNow;
        if (_timerPauseUntilUtc != DateTime.MinValue)
        {
            if (now < _timerPauseUntilUtc)
            {
                if (_sessionStopwatch.IsRunning)
                {
                    _sessionStopwatch.Stop();
                }
            }
            else
            {
                _timerPauseUntilUtc = DateTime.MinValue;
                if (!_completionTriggered && !_abandonTriggered && !_sessionStopwatch.IsRunning)
                {
                    _sessionStopwatch.Start();
                    ShowBanner("Timer resumed.", 0.8d);
                }
            }
        }
        else if (!_completionTriggered && !_abandonTriggered && !_sessionStopwatch.IsRunning)
        {
            _sessionStopwatch.Start();
        }

        if (_visionBoostUntilUtc != DateTime.MinValue && now >= _visionBoostUntilUtc)
        {
            _visionBoostUntilUtc = DateTime.MinValue;
        }

        if (_pathRevealUntilUtc != DateTime.MinValue && now >= _pathRevealUntilUtc)
        {
            _pathRevealUntilUtc = DateTime.MinValue;
        }
    }

    private List<string> BuildSelectedItemIds()
    {
        return _consumedItemIds
            .Where(itemId => !string.IsNullOrWhiteSpace(itemId))
            .ToList();
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

        await PersistBehaviorProfilesAsync(force: true);
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

            var payload = await Api.ReadJsonAsync<LoginResponse>(response);
            if (response.IsSuccessStatusCode && payload?.User is not null)
            {
                await RefreshClientSessionAsync(payload.User);
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                _ = await response.Content.ReadAsStringAsync();
            }
            else
            {
                await RefreshClientSessionAsync();
            }
        }
        catch
        {
        }
    }

    private async Task RefreshClientSessionAsync(LoginUserSummary? session = null)
    {
        try
        {
            var nextSession = session;
            if (nextSession is null || string.IsNullOrWhiteSpace(nextSession.Username))
            {
                nextSession = await Api.GetSessionAsync();
            }

            if (nextSession is null || string.IsNullOrWhiteSpace(nextSession.Username))
            {
                return;
            }

            Username = nextSession.Username;
            await JS.InvokeVoidAsync("enigmaGame.refreshUserSession", nextSession);
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

    protected sealed record WallSegment(string CssClass, string Style, PlayAreaRect Bounds);
}

