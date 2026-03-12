using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Enigma.Client.Models;
using Enigma.Client.Models.Gameplay;
using Enigma.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
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
    private const double RadarCoreRadius = 64d;
    private const double RadarDetailRadius = 138d;
    private const double RadarOuterRadius = 216d;
    private const double RadarPulseCycleSeconds = 5.6d;
    private const int RadarPulseEventsPerCycle = 2;
    private const double RadarPingLeadFraction = 0.04d;
    private const double RadarInterferenceDistance = 248d;
    private const double RadarWallCollisionPadding = 8d;
    private const double WorldInteractableMinimumSize = 44d;
    private const double EasyWorldInteractionRangeScale = 0.82d;
    private const double BehaviorAdaptationCap = 0.35d;
    private const string BehaviorStoragePrefix = "enigma.behavior";
    private const string DefaultRoomTintStart = "#293140";
    private const string DefaultRoomTintEnd = "#151b26";
    private const double PuzzleConsoleHorizontalMargin = 94d;
    private const double PuzzleConsoleTopMargin = 96d;
    private const double PuzzleConsoleBottomMargin = 124d;
    private const double PuzzleConsoleCenterExclusionRadius = 188d;
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
    private string? _dragDialKey;
    private double _dragDialLastClientX;
    private bool _behaviorProfileLoaded;
    private bool _behaviorProfileDirty;
    private DateTime _lastBehaviorProfilePersistUtc = DateTime.MinValue;
    private BehaviorProfileSnapshot _globalBehaviorProfile = new();
    private BehaviorProfileSnapshot _seedBehaviorProfile = new();
    private const double DialDragStepPixels = 14d;
    private SoloPanelView? _lastSoloPanelView;

    [Inject] protected NavigationManager NavigationManager { get; set; } = default!;
    [Inject] protected IJSRuntime JS { get; set; } = default!;
    [Inject] protected EnigmaApiClient Api { get; set; } = default!;
    [Inject] protected DeviceCompatibilityService DeviceCompatibility { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "seed")]
    public string? Seed { get; set; }

    [SupplyParameterFromQuery(Name = "mapName")]
    public string? MapName { get; set; }

    [SupplyParameterFromQuery(Name = "source")]
    public string? Source { get; set; }

    [SupplyParameterFromQuery(Name = "tutorial")]
    public string? Tutorial { get; set; }

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

    protected IReadOnlyDictionary<char, string> RoomArtImages { get; } = new Dictionary<char, string>
    {
        ['A'] = "/images/room-art/A.png",
        ['B'] = "/images/room-art/B.png",
        ['C'] = "/images/room-art/C.png",
        ['D'] = "/images/room-art/D.png",
        ['E'] = "/images/room-art/E.png",
        ['F'] = "/images/room-art/F.png",
        ['G'] = "/images/room-art/G.png",
        ['H'] = "/images/room-art/H.png",
        ['I'] = "/images/room-art/I.png",
        ['J'] = "/images/room-art/J.png",
        ['K'] = "/images/room-art/K.png",
        ['L'] = "/images/room-art/L.png",
        ['M'] = "/images/room-art/M.png",
        ['N'] = "/images/room-art/N.png",
        ['O'] = "/images/room-art/O.png",
    };

    protected IReadOnlyDictionary<char, (string Start, string End)> RoomArtTints { get; } = new Dictionary<char, (string Start, string End)>
    {
        ['A'] = ("#22344f", "#142033"),
        ['B'] = ("#27413a", "#15231f"),
        ['C'] = ("#3a2f4c", "#1a1628"),
        ['D'] = ("#49372b", "#1f1610"),
        ['E'] = ("#2f4547", "#182326"),
        ['F'] = ("#4b2f3c", "#20131a"),
        ['G'] = ("#405129", "#1a2110"),
        ['H'] = ("#1f4153", "#0f202a"),
        ['I'] = ("#3d304f", "#191424"),
        ['J'] = ("#26494a", "#132021"),
        ['K'] = ("#524a28", "#211d10"),
        ['L'] = ("#42313c", "#1d151a"),
        ['M'] = ("#2a4b36", "#142219"),
        ['N'] = ("#4a3526", "#1c140e"),
        ['O'] = ("#3d4f58", "#182026"),
    };

    protected IReadOnlyDictionary<PlayerDirection, string> PlayerAnimationDirections { get; } = new Dictionary<PlayerDirection, string>
    {
        [PlayerDirection.Up] = "up",
        [PlayerDirection.Right] = "right",
        [PlayerDirection.Down] = "down",
        [PlayerDirection.Left] = "left",
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
    protected bool UsesRoomNativePuzzleInteraction => false;
    protected bool CanInteractWithPuzzleConsole => CanShowRoom && HasPuzzleOverlayContent && !IsCurrentPuzzleSolved && !UsesRoomNativePuzzleInteraction;
    protected bool HasHotkeyUsableItems => GetActiveLoadoutItems().Count > 0;
    protected bool PortalReady => IsCoopRun
        ? CurrentRoomState?.Definition.Kind == MazeRoomKind.Finish && IsCurrentCoopRoomSolved
        : CurrentRoomState?.FinishPortalVisible == true;
    protected bool IsTutorialRun =>
        string.Equals(Tutorial, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Tutorial, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Tutorial, "yes", StringComparison.OrdinalIgnoreCase);
    protected bool IsCompatibilityResolved { get; private set; }
    protected GameplayCompatibilityDecision GameplayAccessDecision { get; private set; } = GameplayCompatibilityDecision.BlockedUnknown;
    protected string CurrentRequestedGameRoute => new Uri(NavigationManager.Uri).PathAndQuery;

    private string? _loadedRouteKey;
    private bool CanInitializeRuntime => IsCompatibilityResolved && GameplayAccessDecision == GameplayCompatibilityDecision.Allowed;

    protected string GetRoomArtStyle()
    {
        if (CurrentRoom is null || !RoomArtImages.TryGetValue(CurrentRoom.ConnectionKey, out var imageUrl))
        {
            return "background-image:none;";
        }

        return $"background-image:url('{imageUrl}');";
    }

    protected override void OnParametersSet()
    {
        if (!CanInitializeRuntime)
        {
            IsLoaded = false;
            LoadError = null;
            return;
        }

        ApplyRuntimeParameters();
    }

    private void ApplyRuntimeParameters()
    {
        var routeKey = $"{Seed}|{MapName}|{Source}|{Tutorial}|{Coop}|{CoopSessionId}";
        if (string.Equals(_loadedRouteKey, routeKey, StringComparison.Ordinal))
        {
            return;
        }

        _loadedRouteKey = routeKey;

        if (string.IsNullOrWhiteSpace(Seed))
        {
            LoadError = "Open a generated or loaded seed from the Play page before starting the game.";
            IsLoaded = false;
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
            EnsureCurrentSoloPanelTierAssigned();
            if (IsTutorialRun)
            {
                _tutorialStartRoomCoordinates = CurrentRoom.Coordinates;
            }
            CenterPlayer();
            _sessionStopwatch.Restart();
            IsLoaded = true;
            LoadError = null;
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
        if (!IsCompatibilityResolved)
        {
            GameplayAccessDecision = await DeviceCompatibility.EvaluateGameplayAccessAsync(JS, CurrentRequestedGameRoute);
            IsCompatibilityResolved = true;
            if (GameplayAccessDecision == GameplayCompatibilityDecision.Allowed)
            {
                ApplyRuntimeParameters();
            }

            await InvokeAsync(StateHasChanged);
            return;
        }

        if (!CanInitializeRuntime)
        {
            return;
        }

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

    protected Task HandleBlockedGameExitAsync()
    {
        NavigationManager.NavigateTo("/play", replace: true);
        return Task.CompletedTask;
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

        var interactables = worldPuzzle.GetWorldInteractables().ToList();
        _nearestWorldInteractableId = TryFindNearestWorldInteractableCandidate(worldPuzzle, null)?.Interactable.Id;
        return interactables;
    }

    protected string GetWorldInteractableStyle(PuzzleWorldInteractable interactable)
    {
        var renderBounds = interactable.Clickable
            ? ExpandWorldInteractableBounds(interactable.Bounds)
            : interactable.Bounds;
        return GetRectStyle(renderBounds);
    }

    protected string GetWorldInteractableClass(PuzzleWorldInteractable interactable)
    {
        var classes = $"enigma-world-interactable {interactable.CssClass}";
        if (ShouldUseColorCodedInteractables())
        {
            classes += $" color-coded color-slot-{GetWorldInteractableColorSlot(interactable.Id)}";
            classes += $" icon-{GetWorldInteractableIconToken(interactable.Id)}";
            if (CurrentRoomState?.Puzzle is RoomPuzzle puzzle)
            {
                classes += $" family-{char.ToLowerInvariant(puzzle.PuzzleKey)}";
            }
        }

        if (!interactable.Clickable)
        {
            classes += " decorative";
        }

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
        if (ShouldUseColorCodedInteractables())
        {
            return string.Empty;
        }

        if (!interactable.Clickable && string.IsNullOrWhiteSpace(interactable.Label))
        {
            return string.Empty;
        }

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

        if (interactable.Id.StartsWith("socket-", StringComparison.OrdinalIgnoreCase) ||
            interactable.Id.StartsWith("beta-socket-", StringComparison.OrdinalIgnoreCase) ||
            interactable.Id.StartsWith("route-link-", StringComparison.OrdinalIgnoreCase))
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

        if (string.Equals(id, "cargo-replay", StringComparison.OrdinalIgnoreCase))
        {
            return "replay the imprint";
        }

        return id switch
        {
            "heat-up" => "raise the fault axis",
            "heat-down" => "lower the fault axis",
            "pressure-up" => "raise stratum pressure",
            "pressure-down" => "lower stratum pressure",
            var value when value.StartsWith("cargo-", StringComparison.OrdinalIgnoreCase) => "acquire token",
            var value when value.StartsWith("socket-", StringComparison.OrdinalIgnoreCase) => "dock token",
            var value when value.StartsWith("alpha-cargo-", StringComparison.OrdinalIgnoreCase) => "acquire waypoint",
            var value when value.StartsWith("beta-socket-", StringComparison.OrdinalIgnoreCase) => "commit waypoint",
            var value when value.StartsWith("canister-", StringComparison.OrdinalIgnoreCase) => "acquire canister",
            "manifold-heat" => "inject heat manifold",
            "manifold-pressure" => "inject pressure manifold",
            var value when value.StartsWith("relay-", StringComparison.OrdinalIgnoreCase) => "tune lock channel",
            var value when value.StartsWith("echo-pad-", StringComparison.OrdinalIgnoreCase) => "sample node",
            var value when value.StartsWith("alpha-", StringComparison.OrdinalIgnoreCase) => "mark alpha anchor",
            var value when value.StartsWith("beta-", StringComparison.OrdinalIgnoreCase) => "mark beta anchor",
            var value when value.StartsWith("behavior-", StringComparison.OrdinalIgnoreCase) => "pulse pressure node",
            var value when value.StartsWith("recursive-", StringComparison.OrdinalIgnoreCase) => "rotate cipher anchor",
            var value when value.StartsWith("grid-", StringComparison.OrdinalIgnoreCase) => "tune gravity node",
            var value when value.StartsWith("symbol-", StringComparison.OrdinalIgnoreCase) => "rotate mirror",
            var value when value.StartsWith("time-gate-", StringComparison.OrdinalIgnoreCase) => "route flow gate",
            var value when value.StartsWith("false-", StringComparison.OrdinalIgnoreCase) => "reveal memory node",
            var value when value.StartsWith("hidden-", StringComparison.OrdinalIgnoreCase) => "lock temporal cell",
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
        var easyRangeScale = ShouldUseColorCodedInteractables() ? EasyWorldInteractionRangeScale : 1d;
        var range = Math.Max(56d, interactable.InteractionRange * rangeBoost * easyRangeScale);
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

    private bool ShouldUseColorCodedInteractables() =>
        !IsCoopRun &&
        CurrentRoomState?.Puzzle is IWorldInteractivePuzzle;

    private static int GetWorldInteractableColorSlot(string interactableId)
    {
        if (TryGetFirstNumericToken(interactableId, out var numericToken))
        {
            return Math.Abs(numericToken % 8);
        }

        unchecked
        {
            var hash = 19;
            foreach (var character in interactableId)
            {
                hash = (hash * 31) + char.ToUpperInvariant(character);
            }

            return (hash & int.MaxValue) % 8;
        }
    }

    private static bool TryGetFirstNumericToken(string value, out int token)
    {
        token = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var startIndex = -1;
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsDigit(value[index]))
            {
                startIndex = index;
                break;
            }
        }

        if (startIndex < 0)
        {
            return false;
        }

        var endIndex = startIndex;
        while (endIndex < value.Length && char.IsDigit(value[endIndex]))
        {
            endIndex++;
        }

        return int.TryParse(
            value[startIndex..endIndex],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out token);
    }

    private static string GetWorldInteractableIconToken(string interactableId)
    {
        var value = interactableId.ToLowerInvariant();
        if (value.Contains("relay") || value.Contains("false-"))
        {
            return "route";
        }

        if (value.Contains("echo"))
        {
            return "echo";
        }

        if (value.Contains("layer") || value.StartsWith("alpha-") || value.StartsWith("beta-"))
        {
            return "shift";
        }

        if (value.Contains("behavior"))
        {
            return "commit";
        }

        if (value.Contains("recursive"))
        {
            return "loop";
        }

        if (value.Contains("grid") || value.Contains("hidden"))
        {
            return "grid";
        }

        if (value.Contains("symbol"))
        {
            return "cipher";
        }

        if (value.Contains("time"))
        {
            return "time";
        }

        if (value.Contains("heat"))
        {
            return "heat";
        }

        if (value.Contains("pressure"))
        {
            return "pressure";
        }

        return "node";
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

        var puzzle = CurrentRoomState.Puzzle;
        var metaStats = BuildWorldPuzzleMetaStats();
        if (puzzle is IWorldPuzzleTelemetry telemetry)
        {
            var telemetryStats = telemetry.GetTelemetryStats();
            if (telemetryStats.Count > 0)
            {
                var normalizedStats = telemetryStats
                    .Select(stat => new WorldPuzzleStat(stat.Label, stat.Value))
                    .ToArray();
                return metaStats.Count == 0
                    ? normalizedStats
                    : [.. metaStats, .. normalizedStats];
            }
        }

        return metaStats;
    }

    private List<WorldPuzzleStat> BuildWorldPuzzleMetaStats()
    {
        var stats = new List<WorldPuzzleStat>(4);
        if (CurrentRoomState?.Puzzle is ISoloPanelPuzzle soloPanelPuzzle)
        {
            var view = GetSoloPanelView(soloPanelPuzzle);
            if (view is not null)
            {
                stats.Add(new WorldPuzzleStat("Family", view.FamilyId));
                stats.Add(new WorldPuzzleStat("Tier", $"L{view.TierLevel}"));
                stats.Add(new WorldPuzzleStat("Phase", view.Phase.ToString()));
                stats.Add(new WorldPuzzleStat("State", view.Status.ToString()));
            }
        }
        return stats;
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

        EnsureCurrentSoloPanelTierAssigned();

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
        var tint = CurrentRoom is not null && RoomArtTints.TryGetValue(CurrentRoom.ConnectionKey, out var palette)
            ? palette
            : (Start: DefaultRoomTintStart, End: DefaultRoomTintEnd);

        var playerCenterX = PlayerX + (PlayerSize / 2d);
        var playerCenterY = PlayerY + (PlayerSize / 2d);
        var radarCoreRadius = GetCurrentRadarCoreRadius();
        var radarDetailRadius = GetCurrentRadarDetailRadius();
        var radarOuterRadius = GetCurrentRadarOuterRadius();
        var radarPulseStrength = GetRadarPulseStrength();
        var radarWallProximity = GetRadarWallProximityScale(playerCenterX, playerCenterY);
        var proximityCoreScale = 0.88d + (radarWallProximity * 0.12d);
        var proximityDetailScale = 0.86d + (radarWallProximity * 0.14d);
        var proximityOuterScale = 0.84d + (radarWallProximity * 0.16d);
        var proximityPulseScale = 0.86d + (radarWallProximity * 0.14d);
        var openSpaceBoost = 1d + (Math.Max(0d, radarWallProximity - 0.72d) * 0.08d);
        var radarCycleSeconds = RadarPulseCycleSeconds;
        var radarPrimarySpacing = radarCycleSeconds / RadarPulseEventsPerCycle;
        var radarSecondarySpacing = radarPrimarySpacing / 3d;
        var radarCycleOffset = GetRadarVisualCycleOffsetSeconds();

        radarCoreRadius *= proximityCoreScale * openSpaceBoost;
        radarDetailRadius *= proximityDetailScale * openSpaceBoost;
        radarOuterRadius *= proximityOuterScale * openSpaceBoost;
        radarPulseStrength *= proximityPulseScale;

        radarDetailRadius = Math.Max(radarCoreRadius + 12d, radarDetailRadius);
        radarOuterRadius = Math.Max(radarDetailRadius + 26d, radarOuterRadius);
        var radarPulseScale = Math.Clamp((radarOuterRadius / Math.Max(radarCoreRadius, 1d)) * 0.86d, 2.45d, 3.3d);
        var radarInterference = IsRadarInterferenceActive() ? 1d : 0d;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"--room-stage-tint-start: {tint.Start}; " +
            $"--room-stage-tint-end: {tint.End}; " +
            $"--radar-x: {ToPositionPercentX(playerCenterX, 0d):0.###}%; " +
            $"--radar-y: {ToPercentY(playerCenterY):0.###}%; " +
            $"--radar-core-radius: {ToPercentY(radarCoreRadius):0.###}%; " +
            $"--radar-detail-radius: {ToPercentY(radarDetailRadius):0.###}%; " +
            $"--radar-outer-radius: {ToPercentY(radarOuterRadius):0.###}%; " +
            $"--radar-cycle: {radarCycleSeconds:0.###}s; " +
            $"--radar-primary-spacing: {radarPrimarySpacing:0.###}s; " +
            $"--radar-secondary-spacing: {radarSecondarySpacing:0.###}s; " +
            $"--radar-cycle-offset: {radarCycleOffset:0.###}s; " +
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
        var pulseExpansion = GetRadarPulseStrength() * 9d;
        return (RadarDetailRadius * GetCurrentRadarRangeMultiplier()) + pulseExpansion;
    }

    private double GetCurrentRadarOuterRadius()
    {
        var pulseExpansion = GetRadarPulseStrength() * 15d;
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

    private static double GetRadarPingAlignedCycleProgress()
    {
        var alignedProgress = GetRadarPulseCycleProgress() - (1d - RadarPingLeadFraction);
        if (alignedProgress < 0d)
        {
            alignedProgress += 1d;
        }

        return alignedProgress;
    }

    private static double GetRadarVisualCycleOffsetSeconds()
    {
        return -(GetRadarPingAlignedCycleProgress() * RadarPulseCycleSeconds);
    }

    private double GetRadarPulseStrength()
    {
        var localPulseProgress = (GetRadarPingAlignedCycleProgress() * RadarPulseEventsPerCycle) % 1d;
        var pulsePeak = Math.Exp(-Math.Pow((localPulseProgress - 0.08d) / 0.12d, 2d));
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
        $"facing-{PlayerAnimationDirections[PlayerFacing]} {(IsMoving ? "is-moving" : string.Empty)}";

    protected bool ShowCarriedPayloadIndicator => TryGetCarriedPayloadLabel(out _);

    protected string GetCarriedPayloadLabel() =>
        TryGetCarriedPayloadLabel(out var payloadLabel) ? payloadLabel : string.Empty;

    protected string GetCarriedPayloadStyle()
    {
        if (!TryGetCarriedPayloadLabel(out _))
        {
            return string.Empty;
        }

        const double badgeWidth = 88d;
        const double badgeHeight = 30d;
        var left = Math.Clamp(PlayerX + (PlayerSize / 2d) - (badgeWidth / 2d), 10d, RoomSize - badgeWidth - 10d);
        var top = Math.Clamp(PlayerY - badgeHeight - 12d, 8d, RoomSize - badgeHeight - 8d);
        return $"left: {ToPositionPercentX(left, badgeWidth)}%; top: {ToPercentY(top)}%; width: {ToLengthPercentX(badgeWidth)}%;";
    }

    private bool TryGetCarriedPayloadLabel(out string payloadLabel)
    {
        payloadLabel = string.Empty;
        return false;
    }

    protected string GetRectStyle(PlayAreaRect rect) =>
        AppendRadarSignalStyle(
            $"left: {ToPositionPercentX(rect.X, rect.Width)}%; top: {ToPercentY(rect.Y)}%; width: {ToLengthPercentX(rect.Width)}%; height: {ToPercentY(rect.Height)}%;",
            rect);

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


    protected SoloPanelView? GetSoloPanelView(ISoloPanelPuzzle puzzle)
    {
        EnsureCurrentSoloPanelTierAssigned();
        var view = puzzle.BuildPanelView(_sessionStopwatch.Elapsed.TotalSeconds);
        _lastSoloPanelView = view;
        return view;
    }

    protected void ApplySoloPanelAction(string command)
    {
        if (CurrentRoomState?.Puzzle is not ISoloPanelPuzzle puzzle)
        {
            return;
        }

        EnsureCurrentSoloPanelTierAssigned();
        if (puzzle.ApplyAction(command, _sessionStopwatch.Elapsed.TotalSeconds))
        {
            _playerStateDirty = true;
        }
    }

    protected string GetSoloPanelStatusClass(PuzzleStatus status) => status switch
    {
        PuzzleStatus.Solved => "solved",
        PuzzleStatus.Cooldown => "cooldown",
        PuzzleStatus.FailedTemporary => "failed",
        PuzzleStatus.Resetting => "resetting",
        _ => "active",
    };

    protected string GetSoloPanelActionClass(SoloPanelActionItem action)
    {
        var classes = new List<string> { "btn", "enigma-panel-action" };
        classes.Add(action.Active ? "is-active" : "is-idle");
        if (!action.Enabled)
        {
            classes.Add("is-disabled");
        }
        classes.Add($"tone-{action.Tone}");
        return string.Join(" ", classes);
    }

    protected string GetSoloPanelFamilyClass(string familyId) =>
        string.IsNullOrWhiteSpace(familyId)
            ? "family-unknown"
            : $"family-{familyId.Replace("_", "-", StringComparison.OrdinalIgnoreCase)}";

    protected string GetSoloPanelFamilyIcon(string familyId) => familyId switch
    {
        "chromatic_lock" => "CH",
        "signal_decay" => "SG",
        "dead_reckoning" => "DR",
        "pressure_grid" => "PG",
        "cipher_wheel" => "CW",
        "gravity_well" => "GW",
        "echo_chamber" => "EC",
        "token_flood" => "TF",
        "memory_palace" => "MP",
        "fault_line" => "FL",
        "temporal_grid" => "TG",
        _ => "PX",
    };

    protected IReadOnlyList<string> GetSoloPanelDialKeys(SoloPanelView view) =>
        view.Actions
            .Select(action => TryParseDialCommand(action.Command, out var key, out _) ? key : null)
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static key => ExtractNumericSuffix(key!), Comparer<int>.Default)
            .Cast<string>()
            .ToArray();

    protected IReadOnlyList<SoloPanelActionItem> GetSoloPanelCellActions(SoloPanelView view) =>
        view.Actions
            .Where(action => TryParseCellCommand(action.Command, out _))
            .OrderBy(action => TryParseCellCommand(action.Command, out var key) ? ExtractNumericSuffix(key) : int.MaxValue)
            .ToArray();

    protected IReadOnlyList<SoloPanelActionItem> GetSoloPanelCardActions(SoloPanelView view) =>
        view.Actions
            .Where(action => TryParseCardCommand(action.Command, out _))
            .OrderBy(action => TryParseCardCommand(action.Command, out var index) ? index : int.MaxValue)
            .ToArray();

    protected IReadOnlyList<SoloPanelActionItem> GetSoloPanelPipeActions(SoloPanelView view) =>
        view.Actions
            .Where(action => TryParsePipeCommand(action.Command, out _, out _))
            .OrderBy(action => TryParsePipeCommand(action.Command, out var row, out var col) ? (row * 100) + col : int.MaxValue)
            .ToArray();

    protected IReadOnlyList<SoloPanelActionItem> GetSoloPanelSystemActions(SoloPanelView view) =>
        view.Actions
            .Where(action =>
                !TryParseDialCommand(action.Command, out _, out _) &&
                !TryParseCellCommand(action.Command, out _) &&
                !TryParsePipeCommand(action.Command, out _, out _) &&
                !TryParseCardCommand(action.Command, out _))
            .ToArray();

    protected bool TryGetSoloPanelDialAction(
        SoloPanelView view,
        string key,
        string direction,
        out SoloPanelActionItem action)
    {
        foreach (var candidate in view.Actions)
        {
            if (!TryParseDialCommand(candidate.Command, out var candidateKey, out var candidateDirection))
            {
                continue;
            }

            if (string.Equals(candidateKey, key, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidateDirection, direction, StringComparison.OrdinalIgnoreCase))
            {
                action = candidate;
                return true;
            }
        }

        action = default;
        return false;
    }

    protected int GetSoloPanelBoardInt(SoloPanelView view, string key, int fallback = 0) =>
        view.Board.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    protected double GetSoloPanelBoardDouble(SoloPanelView view, string key, double fallback = 0d) =>
        view.Board.TryGetValue(key, out var value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    protected string GetSoloPanelBoardText(SoloPanelView view, string key, string fallback = "--") =>
        view.Board.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;

    protected string GetSoloPanelCellLabel(SoloPanelActionItem action) =>
        TryParseCellCommand(action.Command, out var key)
            ? ExtractNumericSuffix(key).ToString(CultureInfo.InvariantCulture)
            : action.Label;

    protected string GetSoloPanelCardLabel(SoloPanelActionItem action) =>
        TryParseCardCommand(action.Command, out var index)
            ? (index + 1).ToString(CultureInfo.InvariantCulture)
            : action.Label;

    protected string GetSoloPanelCellActionClass(SoloPanelActionItem action)
    {
        var classes = new List<string> { "enigma-solo-cell-action" };
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

    protected string GetSoloPanelDialRingStyle(SoloPanelView view, string key)
    {
        var current = GetSoloPanelBoardInt(view, $"cur:{key}");
        var target = GetSoloPanelBoardInt(view, $"tgt:{key}", -1);
        var max = Math.Max(1, GetSoloPanelBoardInt(view, $"max:{key}", 1));

        var currentAngle = Math.Round((current / (double)max) * 360d, 2, MidpointRounding.AwayFromZero);
        var targetAngle = target < 0
            ? -1d
            : Math.Round((target / (double)max) * 360d, 2, MidpointRounding.AwayFromZero);
        var targetVisibility = target < 0 ? "none" : "block";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"--dial-angle:{currentAngle:0.##}deg; --dial-target-angle:{targetAngle:0.##}deg; --dial-target-visible:{targetVisibility};");
    }

    protected string GetSoloPanelDialValueText(SoloPanelView view, string key)
    {
        var current = GetSoloPanelBoardInt(view, $"cur:{key}");
        if (string.Equals(view.FamilyId, "signal_decay", StringComparison.OrdinalIgnoreCase))
        {
            return current.ToString(CultureInfo.InvariantCulture);
        }

        var target = GetSoloPanelBoardInt(view, $"tgt:{key}", -1);
        if (target < 0)
        {
            return current.ToString(CultureInfo.InvariantCulture);
        }

        return $"{current} / {target}";
    }

    protected string GetSoloPanelCellGlyph(SoloPanelActionItem action, string familyId)
    {
        var index = TryParseCellCommand(action.Command, out var key)
            ? Math.Max(1, ExtractNumericSuffix(key))
            : 1;

        if (string.Equals(familyId, "token_flood", StringComparison.OrdinalIgnoreCase))
        {
            var pipeGlyphs = new[] { "┼", "┬", "┴", "├", "┤", "┌", "┐", "└", "┘", "─", "│" };
            return pipeGlyphs[index % pipeGlyphs.Length];
        }

        if (string.Equals(familyId, "pressure_grid", StringComparison.OrdinalIgnoreCase))
        {
            return index % 2 == 0 ? "▣" : "◫";
        }

        if (string.Equals(familyId, "temporal_grid", StringComparison.OrdinalIgnoreCase))
        {
            var temporalGlyphs = new[] { "◴", "◷", "◶", "◵" };
            return temporalGlyphs[index % temporalGlyphs.Length];
        }

        return action.Active ? "◉" : "○";
    }

    protected string GetSoloPanelCardGlyph(SoloPanelActionItem action, string familyId)
    {
        var index = TryParseCardCommand(action.Command, out var parsedIndex) ? parsedIndex : 0;
        if (string.Equals(familyId, "memory_palace", StringComparison.OrdinalIgnoreCase))
        {
            var glyphs = new[] { "◈", "◇", "◆", "◍", "◌", "◉", "⬢", "⬡" };
            return glyphs[index % glyphs.Length];
        }

        return "◈";
    }

    protected string GetSoloPanelActionGlyph(SoloPanelActionItem action)
    {
        if (action.Command.StartsWith("page:", StringComparison.OrdinalIgnoreCase))
        {
            return action.Command.EndsWith("prev", StringComparison.OrdinalIgnoreCase) ? "◀" : "▶";
        }

        return action.Command switch
        {
            "commit" => "⏎",
            "reset" => "↺",
            "hint" => "◔",
            _ => "•",
        };
    }

    protected string GetSoloPanelFailureClass(SoloPanelView view) =>
        string.IsNullOrWhiteSpace(view.FailureCode) ? "neutral" : "fault";

    protected string GetSoloPanelStageClass(SoloPanelView view) =>
        string.IsNullOrWhiteSpace(view.StageVisualProfile) ? "stage-intro" : $"stage-{view.StageVisualProfile}";

    protected string GetSoloPanelProgressLabel(SoloPanelView view) =>
        string.IsNullOrWhiteSpace(view.ProgressLabel) ? "System Progress" : view.ProgressLabel;

    protected int GetSoloPanelProgressPercent(SoloPanelView view) =>
        Math.Clamp((int)Math.Round(view.ProgressValue * 100d), 0, 100);

    protected string GetSoloPanelProgressTrendClass(SoloPanelView view) => view.ProgressTrend switch
    {
        "up" => "trend-up",
        "down" => "trend-down",
        _ => "trend-steady",
    };

    protected string GetSoloPanelProgressTrendLabel(SoloPanelView view) => view.ProgressTrend switch
    {
        "up" => "Improving",
        "down" => "Degrading",
        _ => "Stable",
    };

    protected bool IsSoloPanelChromaticLock(SoloPanelView view) =>
        string.Equals(view.FamilyId, "chromatic_lock", StringComparison.OrdinalIgnoreCase);

    protected bool IsSoloPanelSignalDecay(SoloPanelView view) =>
        string.Equals(view.FamilyId, "signal_decay", StringComparison.OrdinalIgnoreCase);

    protected bool IsSoloPanelSystemTemplate(SoloPanelView view) =>
        view.FamilyId is "dead_reckoning" or "gravity_well" or "echo_chamber" or "fault_line";

    protected bool IsSoloPanelDialTemplate(SoloPanelView view) =>
        string.Equals(view.FamilyId, "cipher_wheel", StringComparison.OrdinalIgnoreCase);

    protected bool IsSoloPanelGridTemplate(SoloPanelView view) =>
        view.FamilyId is "pressure_grid" or "temporal_grid";

    protected bool IsSoloPanelFlowTemplate(SoloPanelView view) =>
        string.Equals(view.FamilyId, "token_flood", StringComparison.OrdinalIgnoreCase);

    protected bool IsSoloPanelMemoryTemplate(SoloPanelView view) =>
        string.Equals(view.FamilyId, "memory_palace", StringComparison.OrdinalIgnoreCase);

    protected bool HasDedicatedSoloPanel(SoloPanelView view) =>
        IsSoloPanelChromaticLock(view) ||
        IsSoloPanelSignalDecay(view) ||
        IsSoloPanelSystemTemplate(view) ||
        IsSoloPanelDialTemplate(view) ||
        IsSoloPanelGridTemplate(view) ||
        IsSoloPanelFlowTemplate(view) ||
        IsSoloPanelMemoryTemplate(view);

    protected string GetSoloPanelSignalReadout(SoloPanelView view)
    {
        var coherence = GetSoloPanelBoardDouble(view, "signal_coherence", view.ProgressValue);
        var percent = Math.Clamp((int)Math.Round(coherence * 100d), 0, 100);
        var ready = string.Equals(GetSoloPanelBoardText(view, "signal_ready", "0"), "1", StringComparison.Ordinal);
        return ready
            ? $"System Coherence: {percent}% ready to commit."
            : coherence >= 0.9d
                ? $"System Coherence: {percent}% unstable."
                : $"System Coherence: {percent}%";
    }

    protected string GetSoloPanelSignalWaveStyle(SoloPanelView view)
    {
        var noise = Math.Clamp(GetSoloPanelBoardDouble(view, "signal_wave_noise", 1d - view.ProgressValue), 0d, 1d);
        var speed = Math.Clamp(1.2d - (view.ProgressValue * 0.7d), 0.45d, 1.2d);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"--signal-noise:{noise:0.000}; --signal-speed:{speed:0.000}s;");
    }

    protected string GetSoloPanelSignalAlignStyle(SoloPanelView view, string key)
    {
        var current = GetSoloPanelBoardInt(view, $"cur:{key}");
        var target = GetSoloPanelBoardInt(view, $"tgt:{key}");
        var max = Math.Max(1, GetSoloPanelBoardInt(view, $"max:{key}", 100));
        var tolerance = Math.Max(1, GetSoloPanelBoardInt(view, $"tol:{key}", GetSoloPanelBoardInt(view, "signal_tolerance", 3)));
        var span = max + 1d;
        var currentPercent = Math.Clamp((current / span) * 100d, 0d, 100d);
        var targetPercent = Math.Clamp((target / span) * 100d, 0d, 100d);
        var tolerancePercent = Math.Clamp((tolerance / span) * 100d, 1d, 48d);
        var windowPercent = Math.Clamp(tolerancePercent * 2d, 2d, 96d);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"--signal-current:{currentPercent:0.###}%; --signal-target:{targetPercent:0.###}%; --signal-window:{windowPercent:0.###}%;");
    }

    protected string GetSoloPanelSignalAlignClass(SoloPanelView view, string key)
    {
        var aligned = GetSoloPanelBoardInt(view, $"aligned:{key}", 0) == 1;
        if (aligned)
        {
            return "aligned";
        }

        var distance = GetSoloPanelBoardInt(view, $"dist:{key}", int.MaxValue);
        var tolerance = Math.Max(1, GetSoloPanelBoardInt(view, $"tol:{key}", GetSoloPanelBoardInt(view, "signal_tolerance", 3)));
        return distance <= tolerance * 2 ? "near" : "off";
    }

    protected string GetSoloPanelPipeActionClass(SoloPanelView view, SoloPanelActionItem action)
    {
        var classes = new List<string> { "enigma-solo-pipe-tile" };
        if (action.Active)
        {
            classes.Add("flowing");
        }

        if (!action.Enabled)
        {
            classes.Add("disabled");
        }

        if (TryParsePipeCommand(action.Command, out var row, out var col))
        {
            var key = $"r{row}c{col}";
            if (string.Equals(GetSoloPanelBoardText(view, "pipe_source", string.Empty), key, StringComparison.OrdinalIgnoreCase))
            {
                classes.Add("source");
            }
            else if (string.Equals(GetSoloPanelBoardText(view, "pipe_sink", string.Empty), key, StringComparison.OrdinalIgnoreCase))
            {
                classes.Add("sink");
            }

            if (GetSoloPanelBoardInt(view, $"flow:{key}", 0) == 1)
            {
                classes.Add("route-active");
            }
        }

        return string.Join(" ", classes);
    }

    protected string GetSoloPanelPipeGlyph(SoloPanelView view, SoloPanelActionItem action)
    {
        if (!TryParsePipeCommand(action.Command, out var row, out var col))
        {
            return ".";
        }

        var key = $"r{row}c{col}";
        var mask = GetSoloPanelBoardInt(view, $"mask:{key}", 0);
        var turns = GetSoloPanelBoardInt(view, $"cur:{key}", 0);
        var rotated = RotatePipeMask(mask, turns);
        return rotated switch
        {
            0 => ".",
            1 => "╹",
            2 => "╺",
            3 => "└",
            4 => "╻",
            5 => "│",
            6 => "┌",
            7 => "├",
            8 => "╸",
            9 => "┘",
            10 => "─",
            11 => "┴",
            12 => "┐",
            13 => "┤",
            14 => "┬",
            _ => "┼",
        };
    }

    protected void BeginSoloDialDrag(PointerEventArgs args, string dialKey)
    {
        _dragDialKey = dialKey;
        _dragDialLastClientX = args.ClientX;
    }

    protected void HandleSoloDialDragMove(PointerEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(_dragDialKey) || _lastSoloPanelView is null)
        {
            return;
        }

        var delta = args.ClientX - _dragDialLastClientX;
        if (Math.Abs(delta) < DialDragStepPixels)
        {
            return;
        }

        var direction = delta > 0 ? "up" : "down";
        var stepCount = Math.Max(1, (int)Math.Floor(Math.Abs(delta) / DialDragStepPixels));
        if (!TryGetSoloPanelDialAction(_lastSoloPanelView, _dragDialKey, direction, out var action) || !action.Enabled)
        {
            _dragDialLastClientX = args.ClientX;
            return;
        }

        for (var step = 0; step < stepCount; step++)
        {
            ApplySoloPanelAction(action.Command);
        }

        _dragDialLastClientX = args.ClientX;
    }

    protected void EndSoloDialDrag(PointerEventArgs _)
    {
        _dragDialKey = null;
    }

    private static int RotatePipeMask(int mask, int turnsClockwise)
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

    private static bool TryParseDialCommand(string command, out string key, out string direction)
    {
        key = string.Empty;
        direction = string.Empty;
        if (!command.StartsWith("dial:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = command.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
        {
            return false;
        }

        key = parts[1];
        direction = parts[2];
        return true;
    }

    private static bool TryParseCellCommand(string command, out string key)
    {
        key = string.Empty;
        if (!command.StartsWith("cell:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        key = command[5..];
        return !string.IsNullOrWhiteSpace(key);
    }

    private static bool TryParsePipeCommand(string command, out int row, out int col)
    {
        row = -1;
        col = -1;
        if (!command.StartsWith("pipe:r", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var payload = command[6..];
        var splitIndex = payload.IndexOf('c');
        if (splitIndex <= 0 || splitIndex >= payload.Length - 1)
        {
            return false;
        }

        return int.TryParse(payload.AsSpan(0, splitIndex), NumberStyles.Integer, CultureInfo.InvariantCulture, out row) &&
               int.TryParse(payload.AsSpan(splitIndex + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out col);
    }

    private static bool TryParseCardCommand(string command, out int index)
    {
        index = -1;
        return command.StartsWith("card:", StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(command[5..], NumberStyles.Integer, CultureInfo.InvariantCulture, out index);
    }

    private static int ExtractNumericSuffix(string value)
    {
        var digits = new StringBuilder();
        foreach (var character in value)
        {
            if (char.IsDigit(character))
            {
                digits.Append(character);
            }
        }

        return digits.Length == 0 || !int.TryParse(digits.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? int.MaxValue
            : parsed;
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
        UpdateCoopPeerInterpolation(deltaTime);

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
                var bonusMultiplier = CurrentRoomState.Puzzle is ISoloPanelPuzzle panelPuzzle &&
                                      ParsedSeed?.Difficulty is MazeDifficulty.Medium or MazeDifficulty.Hard
                    ? panelPuzzle.RewardPickupMultiplier
                    : 1d;
                var bonus = CurrentRoomState.CollectRewardPickup(bonusMultiplier);
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
        EnsureCurrentSoloPanelTierAssigned();
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

    private void EnsureCurrentSoloPanelTierAssigned()
    {
        if (IsCoopRun || CurrentRoomState?.Puzzle is not ISoloPanelPuzzle panelPuzzle || panelPuzzle.TierInitialized)
        {
            return;
        }

        var solvedRooms = _roomStates.Values.Count(state => state.Puzzle.IsCompleted);
        if (CurrentRoomState.Puzzle.IsCompleted)
        {
            solvedRooms = Math.Max(0, solvedRooms - 1);
        }

        var totalPuzzleRooms = Math.Max(1, _roomStates.Count);
        panelPuzzle.EnsureTierLevel(solvedRooms, totalPuzzleRooms);
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
        return false;
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
        return false;
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
            return CreateFallbackConsoleBounds();
        }

        var stableKey = $"{ParsedSeed.RawSeed}|{CurrentRoom.Coordinates.X}|{CurrentRoom.Coordinates.Y}|{CurrentRoom.ConnectionKey}|{CurrentRoom.PuzzleKey}";
        return CreateStableConsoleBounds(stableKey, 0);
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
        if (CurrentRoom is null || ParsedSeed is null)
        {
            var fallback = CreateFallbackConsoleBounds();
            return (fallback, new PlayAreaRect(fallback.X + 280d, fallback.Y, fallback.Width, fallback.Height));
        }

        var stableKey = $"{ParsedSeed.RawSeed}|{CurrentRoom.Coordinates.X}|{CurrentRoom.Coordinates.Y}|{CurrentRoom.ConnectionKey}|{CurrentRoom.PuzzleKey}|coop-console";
        var ownerConsole = CreateStableConsoleBounds(stableKey, 0);
        PlayAreaRect? guestConsole = null;
        for (var salt = 1; salt <= 24; salt++)
        {
            var candidate = CreateStableConsoleBounds(stableKey, salt);
            if (Distance(ownerConsole, candidate) >= CoopConsoleMinSeparation)
            {
                guestConsole = candidate;
                break;
            }
        }

        if (guestConsole is null)
        {
            guestConsole = CreateMirroredConsoleBounds(ownerConsole);
        }

        if (Distance(ownerConsole, guestConsole.Value) < CoopConsoleMinSeparation)
        {
            guestConsole = CreateFallbackSeparatedConsole(ownerConsole);
        }

        return (ownerConsole, guestConsole.Value);
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

    private static PlayAreaRect CreateFallbackConsoleBounds() => new(112d, 824d, PuzzleConsoleSize, PuzzleConsoleSize);

    private static PlayAreaRect CreateStableConsoleBounds(string stableKey, int salt)
    {
        var xMin = HorizontalWallCollisionInset + PuzzleConsoleHorizontalMargin;
        var xMax = RoomSize - HorizontalWallCollisionInset - PuzzleConsoleSize - PuzzleConsoleHorizontalMargin;
        var yMin = WallThickness + PuzzleConsoleTopMargin;
        var yMax = RoomSize - WallThickness - PuzzleConsoleSize - PuzzleConsoleBottomMargin;
        var centerX = (RoomSize - PuzzleConsoleSize) / 2d;
        var centerY = (RoomSize - PuzzleConsoleSize) / 2d;

        for (var attempt = 0; attempt < 24; attempt++)
        {
            var sampleSeed = $"{stableKey}|slot:{salt}|attempt:{attempt}";
            var xHash = GetStableHash($"{sampleSeed}|x");
            var yHash = GetStableHash($"{sampleSeed}|y");
            var x = Lerp(xMin, xMax, NormalizeHash(xHash));
            var y = Lerp(yMin, yMax, NormalizeHash(yHash));
            var candidate = new PlayAreaRect(x, y, PuzzleConsoleSize, PuzzleConsoleSize);

            if (Distance(candidate, new PlayAreaRect(centerX, centerY, PuzzleConsoleSize, PuzzleConsoleSize)) >= PuzzleConsoleCenterExclusionRadius)
            {
                return candidate;
            }
        }

        return CreateFallbackConsoleBounds();
    }

    private static PlayAreaRect CreateMirroredConsoleBounds(PlayAreaRect source)
    {
        var mirroredX = Math.Clamp(RoomSize - source.Right, HorizontalWallCollisionInset + PuzzleConsoleHorizontalMargin, RoomSize - HorizontalWallCollisionInset - PuzzleConsoleSize - PuzzleConsoleHorizontalMargin);
        var mirroredY = Math.Clamp(RoomSize - source.Bottom, WallThickness + PuzzleConsoleTopMargin, RoomSize - WallThickness - PuzzleConsoleSize - PuzzleConsoleBottomMargin);
        return new PlayAreaRect(mirroredX, mirroredY, PuzzleConsoleSize, PuzzleConsoleSize);
    }

    private static PlayAreaRect CreateFallbackSeparatedConsole(PlayAreaRect source)
    {
        var targetX = source.CenterX < (RoomSize / 2d)
            ? RoomSize - HorizontalWallCollisionInset - PuzzleConsoleSize - PuzzleConsoleHorizontalMargin
            : HorizontalWallCollisionInset + PuzzleConsoleHorizontalMargin;
        var targetY = source.CenterY < (RoomSize / 2d)
            ? RoomSize - WallThickness - PuzzleConsoleSize - PuzzleConsoleBottomMargin
            : WallThickness + PuzzleConsoleTopMargin;
        return new PlayAreaRect(targetX, targetY, PuzzleConsoleSize, PuzzleConsoleSize);
    }

    private static double NormalizeHash(int hash) => hash / (double)int.MaxValue;

    private static double Lerp(double min, double max, double t) => min + ((max - min) * Math.Clamp(t, 0d, 1d));

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

    private PuzzleGuide GetCurrentPuzzleGuide()
    {
        if (CurrentRoomState?.Puzzle is ISoloPanelPuzzle soloPanelPuzzle)
        {
            var view = GetSoloPanelView(soloPanelPuzzle);
            if (view is not null)
            {
                return new PuzzleGuide(
                    Goal: CurrentRoomState.Puzzle.Instruction,
                    Controls: "Use the console controls to tune, align, route, or stabilize the panel state until the room unlocks.",
                    Success: $"{view.FamilyId.Replace('_', ' ')} reaches a solved state and the room doors unlock.");
            }
        }

        return new PuzzleGuide(
            Goal: "Solve the current room puzzle.",
            Controls: "Use the controls shown in the panel and watch the status line for live feedback.",
            Success: "The room reports solved and the doors unlock.");
    }

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

        if (CurrentRoomState.Puzzle is ISoloPanelPuzzle soloPanelPuzzle)
        {
            var view = GetSoloPanelView(soloPanelPuzzle);
            if (view is not null)
            {
                if (view.Board.TryGetValue("hint", out var directHint) && !string.IsNullOrWhiteSpace(directHint))
                {
                    return directHint;
                }

                return $"{GetCurrentPuzzleGuide().Controls} Status: {view.StatusText}";
            }
        }

        return GetCurrentPuzzleGuide().Controls;
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


