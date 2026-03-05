using Enigma.Client.Models.Gameplay;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Text.Json;

namespace Enigma.Client.Pages;

public partial class Game
{
    private static readonly TimeSpan CoopStateSyncInterval = TimeSpan.FromMilliseconds(33);
    private static readonly TimeSpan CoopPollInterval = TimeSpan.FromMilliseconds(150);

    private DateTime _lastCoopStateSyncUtc = DateTime.MinValue;
    private DateTime _lastCoopPollUtc = DateTime.MinValue;
    private MultiplayerSessionState? _coopSession;
    private bool _coopInitialized;
    private bool _coopStateSyncInFlight;
    private bool _coopPollInFlight;
    private bool _coopMoveRequestInFlight;
    private bool _coopPuzzleActionInFlight;
    private bool _coopFinishRequested;
    private bool _isOnBlackHole;
    private bool _coopSocketOpen;

    [SupplyParameterFromQuery(Name = "coop")]
    public string? Coop { get; set; }

    [SupplyParameterFromQuery(Name = "coopSessionId")]
    public string? CoopSessionId { get; set; }

    protected bool IsCoopRun =>
        string.Equals(Coop, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Coop, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Coop, "yes", StringComparison.OrdinalIgnoreCase);

    protected MultiplayerSessionState? CoopSession => _coopSession;
    protected MultiplayerRoomPuzzleState? CurrentCoopPuzzle => _coopSession?.CurrentRoomPuzzle;
    protected MultiplayerPlayerState? CoopOtherPlayer => _coopSession?.OtherPlayerVisible == true ? _coopSession.OtherPlayer : null;
    protected string CoopPartnerName => CoopOtherPlayer?.Username ?? _coopSession?.GuestUsername ?? "Waiting";
    protected string CoopStatusLabel => _coopSession is null ? "Not linked" : _coopSession.Status.Replace('_', ' ');
    protected bool IsCurrentCoopRoomSolved => _coopSession?.CurrentRoomProgress?.PuzzleSolved == true || CurrentCoopPuzzle?.Completed == true;
    protected bool IsCurrentCoopRewardCollected => _coopSession?.CurrentRoomProgress?.RewardPickupCollected == true;
    protected bool IsCoopSocketOpen => _coopSocketOpen;
    protected bool RequiresContinuousCoopSync =>
        string.Equals(CurrentCoopPuzzle?.ViewType, "pressure_systems", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(CurrentCoopPuzzle?.ViewType, "spatial_sync", StringComparison.OrdinalIgnoreCase);

    protected string GetOtherPlayerStyle()
    {
        if (CoopOtherPlayer is null)
        {
            return string.Empty;
        }

        var (x, y) = NormalizeCoopPosition(CoopOtherPlayer.Position);
        return $"left: {ToPositionPercentX(x, PlayerSize)}%; top: {ToPercentY(y)}%; width: {ToLengthPercentX(PlayerSize)}%; height: {ToPercentY(PlayerSize)}%;";
    }

    protected string GetOtherPlayerTagStyle()
    {
        if (CoopOtherPlayer is null)
        {
            return string.Empty;
        }

        var (x, normalizedY) = NormalizeCoopPosition(CoopOtherPlayer.Position);
        var y = Math.Clamp(normalizedY - 42d, 0d, RoomSize - PlayerSize);
        return $"left: {ToPositionPercentX(x, PlayerSize)}%; top: {ToPercentY(y)}%;";
    }

    protected string GetOtherPlayerClass()
    {
        var direction = ParseDirection(CoopOtherPlayer?.Facing);
        return $"co-op-peer facing-{PlayerAnimationDirections[direction]} {PlayerSpriteStates[direction]}";
    }

    protected string GetCoopVoteLabel()
    {
        if (_coopSession?.MoveVote is null)
        {
            return "No shared room vote in progress.";
        }

        return $"Moving to {_coopSession.MoveVote.Target} when both players confirm.";
    }

    private void SyncCurrentRoomProgressFromSession()
    {
        if (!IsCoopRun || CurrentRoomState is null || _coopSession is null)
        {
            return;
        }

        CurrentRoomState.SyncCoopProgress(
            _coopSession.CurrentRoomProgress?.PuzzleSolved == true,
            _coopSession.CurrentRoomProgress?.RewardPickupCollected == true);
        if (_coopSession.CurrentRoomProgress?.PuzzleSolved == true)
        {
            CurrentRoomState.Puzzle.SyncCompleted(_coopSession.CurrentRoomPuzzle?.Status);
        }
        TotalGold = _coopSession.TeamGold;
    }

    private void ResetCoopRuntimeState()
    {
        _coopSession = null;
        _coopInitialized = false;
        _lastCoopStateSyncUtc = DateTime.MinValue;
        _lastCoopPollUtc = DateTime.MinValue;
        _coopStateSyncInFlight = false;
        _coopPollInFlight = false;
        _coopMoveRequestInFlight = false;
        _coopPuzzleActionInFlight = false;
        _coopFinishRequested = false;
        _isOnBlackHole = false;
        _coopSocketOpen = false;
    }

    private async Task InitializeCoopAsync()
    {
        if (!IsCoopRun)
        {
            _coopInitialized = true;
            return;
        }

        if (string.IsNullOrWhiteSpace(CoopSessionId))
        {
            throw new InvalidOperationException("Co-op mode requires a valid session id.");
        }

        using var response = await Api.GetAsync($"api/auth/multiplayer/session?sessionId={Uri.EscapeDataString(CoopSessionId)}");
        var payload = await Api.ReadJsonAsync<MultiplayerSessionEnvelope>(response);
        if (!response.IsSuccessStatusCode || payload?.Session is null)
        {
            var raw = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(raw) ? "Unable to load the co-op session." : raw);
        }

        if (!string.Equals(payload.Session.Status, "active", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(payload.Session.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("This co-op session is not active yet. Return to the lobby and wait for both players to ready up.");
        }

        ApplyCoopSession(payload.Session, forcePosition: true);
        _coopInitialized = true;
    }

    private async Task ConnectCoopSocketAsync()
    {
        if (!IsCoopRun || !_coopInitialized || _dotNetReference is null || string.IsNullOrWhiteSpace(CoopSessionId))
        {
            return;
        }

        await JS.InvokeVoidAsync("enigmaGame.connectCoopSocket", CoopSessionId, _dotNetReference);
    }

    private async Task PumpCoopAsync()
    {
        if (!IsCoopRun || !_coopInitialized || _completionTriggered || _abandonTriggered || string.IsNullOrWhiteSpace(CoopSessionId))
        {
            return;
        }

        var onBlackHole = IsOnBlackHoleNow();
        if (onBlackHole != _isOnBlackHole)
        {
            _isOnBlackHole = onBlackHole;
            _playerStateDirty = true;
        }

        await SubmitCoopStateAsync();
        await PollCoopSessionAsync();

        if (_coopSession is null)
        {
            return;
        }

        if (string.Equals(_coopSession.Status, "completed", StringComparison.OrdinalIgnoreCase) && _coopSession.Completion is not null)
        {
            await CompleteCoopRunAsync(_coopSession.Completion);
            return;
        }

        if (string.Equals(_coopSession.Status, "abandoned", StringComparison.OrdinalIgnoreCase))
        {
            await RedirectToCoopLossAsync("partner_left", submitLeave: false);
            return;
        }

        if (_isOnBlackHole && CurrentRoom?.Coordinates == ParsedSeed?.FinishRoom.Coordinates)
        {
            ShowBanner("Hold on the black hole until your partner arrives.", 0.6d);
        }

        if (!_coopFinishRequested &&
            _coopSession.You?.IsOnBlackHole == true &&
            _coopSession.OtherPlayer?.IsOnBlackHole == true &&
            _coopSession.CurrentRoom.X == _coopSession.FinishRoom.X &&
            _coopSession.CurrentRoom.Y == _coopSession.FinishRoom.Y)
        {
            _coopFinishRequested = true;
            if (_coopSocketOpen)
            {
                var sent = await JS.InvokeAsync<bool>("enigmaGame.sendCoopSocketMessage", new object?[] { new { type = "finish" } });
                if (sent)
                {
                    return;
                }

                _coopFinishRequested = false;
            }

            using var response = await Api.PostJsonAsync("api/auth/multiplayer/session/finish", new { sessionId = CoopSessionId });
            var payload = await Api.ReadJsonAsync<MultiplayerSessionEnvelope>(response);
            if (response.IsSuccessStatusCode && payload?.Completion is not null)
            {
                _coopSession = payload.Session;
                await CompleteCoopRunAsync(payload.Completion);
                return;
            }

            _coopFinishRequested = false;
        }
    }

    private async Task SubmitCoopStateAsync(bool force = false)
    {
        if (_coopSession is null || CurrentRoom is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (!force && !_playerStateDirty && !RequiresContinuousCoopSync)
        {
            return;
        }

        if (!force && now - _lastCoopStateSyncUtc < CoopStateSyncInterval)
        {
            return;
        }

        var payload = new
        {
            type = "state",
            room_x = CurrentRoom.Coordinates.X,
            room_y = CurrentRoom.Coordinates.Y,
            position = new
            {
                x = Math.Round(PlayerX, 3),
                y = Math.Round(PlayerY, 3),
                width = Math.Round(PlayerSize, 3),
                height = Math.Round(PlayerSize, 3),
                x_percent = ToPositionPercentX(PlayerX, PlayerSize),
                y_percent = ToPercentY(PlayerY),
            },
            facing = PlayerFacing.ToString(),
            is_on_black_hole = _isOnBlackHole,
            gold_collected = TotalGold,
            puzzle_solved = IsCurrentCoopRoomSolved,
            reward_pickup_collected = CurrentRoomState?.RewardPickupCollected == true,
        };

        if (_coopSocketOpen)
        {
            var sent = await JS.InvokeAsync<bool>("enigmaGame.sendCoopSocketMessage", new object?[] { payload });
            if (sent)
            {
                _lastCoopStateSyncUtc = now;
                return;
            }
        }

        if (_coopStateSyncInFlight)
        {
            return;
        }

        _coopStateSyncInFlight = true;
        try
        {
            using var response = await Api.PutJsonAsync("api/auth/multiplayer/session/state", new
            {
                sessionId = CoopSessionId,
                roomX = CurrentRoom.Coordinates.X,
                roomY = CurrentRoom.Coordinates.Y,
                position = new
                {
                    x = Math.Round(PlayerX, 3),
                    y = Math.Round(PlayerY, 3),
                    width = Math.Round(PlayerSize, 3),
                    height = Math.Round(PlayerSize, 3),
                    xPercent = ToPositionPercentX(PlayerX, PlayerSize),
                    yPercent = ToPercentY(PlayerY),
                },
                facing = PlayerFacing.ToString(),
                isOnBlackHole = _isOnBlackHole,
                goldCollected = TotalGold,
                puzzleSolved = IsCurrentCoopRoomSolved,
                rewardPickupCollected = CurrentRoomState?.RewardPickupCollected == true,
            });

            var envelope = await Api.ReadJsonAsync<MultiplayerSessionEnvelope>(response);
            if (response.IsSuccessStatusCode && envelope?.Session is not null)
            {
                ApplyCoopSession(envelope.Session);
                _lastCoopStateSyncUtc = now;
            }
        }
        finally
        {
            _coopStateSyncInFlight = false;
        }
    }

    private async Task PollCoopSessionAsync(bool force = false)
    {
        if (_coopSocketOpen && !force)
        {
            return;
        }

        if (_coopPollInFlight || string.IsNullOrWhiteSpace(CoopSessionId))
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (!force && now - _lastCoopPollUtc < CoopPollInterval)
        {
            return;
        }

        _coopPollInFlight = true;
        try
        {
            using var response = await Api.GetAsync($"api/auth/multiplayer/session?sessionId={Uri.EscapeDataString(CoopSessionId)}");
            var payload = await Api.ReadJsonAsync<MultiplayerSessionEnvelope>(response);
            if (response.IsSuccessStatusCode && payload?.Session is not null)
            {
                ApplyCoopSession(payload.Session);
                _lastCoopPollUtc = now;
            }
        }
        finally
        {
            _coopPollInFlight = false;
        }
    }

    private void ApplyCoopSession(MultiplayerSessionState session, bool forcePosition = false)
    {
        _coopSession = session;
        _coopMoveRequestInFlight = false;
        _coopPuzzleActionInFlight = false;

        if (ParsedSeed is null)
        {
            return;
        }

        var nextPoint = new GridPoint(session.CurrentRoom.X, session.CurrentRoom.Y);
        var roomChanged = CurrentRoom?.Coordinates != nextPoint;
        if (roomChanged && ParsedSeed.TryGetRoom(nextPoint, out var nextRoom))
        {
            CurrentRoom = nextRoom;
            CurrentRoomState = _roomStates[nextPoint];
            ClosePuzzleOverlay();
            var (nextX, nextY) = NormalizeCoopPosition(session.You?.Position);
            PlayerX = nextX;
            PlayerY = nextY;
            ShowBanner($"Both players entered room {nextPoint}", 0.8d);
        }
        else if (forcePosition && session.You is not null)
        {
            var (nextX, nextY) = NormalizeCoopPosition(session.You.Position);
            PlayerX = nextX;
            PlayerY = nextY;
        }

        if (session.You is not null)
        {
            PlayerFacing = ParseDirection(session.You.Facing);
            _isOnBlackHole = session.You.IsOnBlackHole;
        }

        SyncCurrentRoomProgressFromSession();
    }

    private static (double X, double Y) NormalizeCoopPosition(MultiplayerPlayerPosition? position)
    {
        var fallback = (RoomSize - PlayerSize) / 2d;
        if (position is null)
        {
            return (fallback, fallback);
        }

        var x = position.X;
        var y = position.Y;
        var width = position.Width;
        var height = position.Height;
        var xPercent = position.XPercent;
        var yPercent = position.YPercent;
        var maxCoordinate = RoomSize - PlayerSize;
        var hasPercentHint = xPercent >= 0d && xPercent <= 100d && yPercent >= 0d && yPercent <= 100d;

        // Legacy sessions used 0-100-ish coordinates with 8x8 avatars.
        if (width <= 8.1d && height <= 8.1d && x <= 100d && y <= 100d)
        {
            // Some legacy payloads carried 0/0 coords but valid percent hints.
            if (x <= 0.001d && y <= 0.001d && hasPercentHint && (xPercent > 1d || yPercent > 1d))
            {
                return (
                    Math.Clamp((xPercent / 100d) * maxCoordinate, 0d, maxCoordinate),
                    Math.Clamp((yPercent / 100d) * maxCoordinate, 0d, maxCoordinate)
                );
            }

            var scaledX = (Math.Clamp(x, 0d, 100d) / 100d) * (RoomSize - PlayerSize);
            var scaledY = (Math.Clamp(y, 0d, 100d) / 100d) * (RoomSize - PlayerSize);
            return (scaledX, scaledY);
        }

        // Guard against partial payloads that zero out coords but keep percent hints.
        if (x <= 0.001d && y <= 0.001d && hasPercentHint && (xPercent > 1d || yPercent > 1d))
        {
            return (
                Math.Clamp((xPercent / 100d) * maxCoordinate, 0d, maxCoordinate),
                Math.Clamp((yPercent / 100d) * maxCoordinate, 0d, maxCoordinate)
            );
        }

        return (
            Math.Clamp(x, 0d, maxCoordinate),
            Math.Clamp(y, 0d, maxCoordinate)
        );
    }

    private async Task RequestCoopRoomMoveAsync(GridPoint nextPoint)
    {
        if (_coopMoveRequestInFlight || string.IsNullOrWhiteSpace(CoopSessionId))
        {
            return;
        }

        if (_coopSocketOpen)
        {
            _coopMoveRequestInFlight = true;
            var sent = await JS.InvokeAsync<bool>("enigmaGame.sendCoopSocketMessage", new object?[]
            {
                new
                {
                    type = "room_move",
                    target_room_x = nextPoint.X,
                    target_room_y = nextPoint.Y,
                }
            });

            if (sent)
            {
                ShowBanner("Room vote sent. Waiting for your partner.", 0.8d);
                return;
            }

            _coopMoveRequestInFlight = false;
        }

        _coopMoveRequestInFlight = true;
        try
        {
            using var response = await Api.PostJsonAsync("api/auth/multiplayer/session/room/move", new
            {
                sessionId = CoopSessionId,
                targetRoomX = nextPoint.X,
                targetRoomY = nextPoint.Y,
            });

            var payload = await Api.ReadJsonAsync<MultiplayerSessionEnvelope>(response);
            if (response.IsSuccessStatusCode && payload?.Session is not null)
            {
                ApplyCoopSession(payload.Session, forcePosition: true);
                _playerStateDirty = true;
                ShowBanner(payload.RoomMoved ? $"Both players moved to room {nextPoint}" : "Waiting for your partner to choose the same doorway.", 1.0d);
                return;
            }

            var raw = await response.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                ShowBanner("Your partner requested a different room.", 1.0d);
            }
        }
        finally
        {
            _coopMoveRequestInFlight = false;
        }
    }

    [JSInvokable]
    public async Task HandleCoopSocketMessage(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return;
        }

        MultiplayerSocketEnvelope? message;
        try
        {
            message = JsonSerializer.Deserialize<MultiplayerSocketEnvelope>(rawJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return;
        }

        if (message is null)
        {
            return;
        }

        if (string.Equals(message.Type, "error", StringComparison.OrdinalIgnoreCase))
        {
            _coopMoveRequestInFlight = false;
            _coopPuzzleActionInFlight = false;
            _coopFinishRequested = false;
            if (!string.IsNullOrWhiteSpace(message.Detail))
            {
                ShowBanner(message.Detail, 1.2d);
                await InvokeAsync(StateHasChanged);
            }

            return;
        }

        if (!string.Equals(message.Type, "session", StringComparison.OrdinalIgnoreCase) || message.Session is null)
        {
            return;
        }

        if (message.Completion is not null && message.Session.Completion is null)
        {
            message.Session.Completion = message.Completion;
        }

        ApplyCoopSession(message.Session);

        if (string.Equals(message.Session.Status, "completed", StringComparison.OrdinalIgnoreCase) && message.Session.Completion is not null)
        {
            await CompleteCoopRunAsync(message.Session.Completion);
            return;
        }

        if (string.Equals(message.Session.Status, "abandoned", StringComparison.OrdinalIgnoreCase))
        {
            await RedirectToCoopLossAsync("partner_left", submitLeave: false);
            return;
        }

        await InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public Task HandleCoopSocketStatusChanged(bool isOpen)
    {
        _coopSocketOpen = isOpen;
        if (!isOpen)
        {
            _lastCoopPollUtc = DateTime.MinValue;
            _coopMoveRequestInFlight = false;
            _coopPuzzleActionInFlight = false;
            _coopFinishRequested = false;
        }

        return InvokeAsync(StateHasChanged);
    }

    private async Task CompleteCoopRunAsync(MultiplayerCompletionData completion)
    {
        if (_completionTriggered)
        {
            return;
        }

        _completionTriggered = true;
        _allowRouteExit = true;
        _sessionStopwatch.Stop();

        var isOwner = string.Equals(Username, completion.OwnerUsername, StringComparison.OrdinalIgnoreCase);
        var rewardAwarded = isOwner ? completion.OwnerReward : completion.GuestReward;
        var partner = completion.Discoverers.FirstOrDefault(user => !string.Equals(user, Username, StringComparison.OrdinalIgnoreCase));
        var summary = new GameCompletionSummary
        {
            Seed = ParsedSeed?.RawSeed ?? string.Empty,
            Username = Username,
            CompletionTime = FormatElapsed(_sessionStopwatch.Elapsed),
            GoldCollected = completion.TotalRewards,
            RewardAwarded = rewardAwarded,
            BankDividend = completion.BankDividend,
            Source = NormalizedSource,
            LoadedMapName = MapName ?? _coopSession?.MapName,
            Difficulty = ParsedSeed?.Difficulty.ToString() ?? string.Empty,
            Size = ParsedSeed?.Size ?? 0,
            SeedExisted = completion.SeedExisted,
            IsMultiplayer = true,
            MultiplayerSessionId = CoopSessionId,
            PartnerUsername = partner,
            IsSessionOwner = isOwner,
            RequiresOwnerSave = completion.RequiresOwnerSave,
            DiscoverersSynced = completion.DiscoverersSynced,
            CanSubmitMapRecord = isOwner,
        };

        await JS.InvokeVoidAsync("enigmaGame.clearPendingLossDraft");
        await JS.InvokeVoidAsync("enigmaGame.clearPendingLossSummary");
        await JS.InvokeVoidAsync("enigmaGame.clearActiveGameSession");
        await JS.InvokeVoidAsync("enigmaGame.clearLivePlayerState");
        await JS.InvokeVoidAsync("enigmaGame.clearCoopLeaveUnload");
        await JS.InvokeVoidAsync("enigmaGame.disposeCoopSocket");
        await JS.InvokeVoidAsync("enigmaGame.disposeInput");
        await JS.InvokeVoidAsync("enigmaGame.sessionSetJson", CompletionSummaryStorageKey, summary);
        await InvokeAsync(() => NavigationManager.NavigateTo("/gameend/win", replace: true));
    }

    private async Task RedirectToCoopLossAsync(string reason, bool submitLeave)
    {
        if (_abandonTriggered)
        {
            return;
        }

        _abandonTriggered = true;
        _allowRouteExit = true;
        _sessionStopwatch.Stop();

        var summary = BuildPendingLossSummary(reason);
        summary.IsMultiplayer = true;
        summary.MultiplayerSessionId = CoopSessionId;
        summary.PartnerUsername = CoopPartnerName;

        await JS.InvokeVoidAsync("enigmaGame.setPendingLossSummary", summary);
        await JS.InvokeVoidAsync("enigmaGame.clearPendingLossDraft");
        await JS.InvokeVoidAsync("enigmaGame.clearCoopLeaveUnload");

        if (submitLeave && !string.IsNullOrWhiteSpace(CoopSessionId))
        {
            try
            {
                if (_coopSocketOpen)
                {
                    var sent = await JS.InvokeAsync<bool>("enigmaGame.sendCoopSocketMessage", new object?[]
                    {
                        new
                        {
                            type = "leave",
                            reason,
                        }
                    });
                    if (!sent)
                    {
                        using var socketFallback = await Api.PostJsonAsync("api/auth/multiplayer/session/leave", new
                        {
                            sessionId = CoopSessionId,
                            reason,
                        });
                        _ = await Api.ReadJsonAsync<MultiplayerSessionEnvelope>(socketFallback);
                    }
                }
                else
                {
                    using var response = await Api.PostJsonAsync("api/auth/multiplayer/session/leave", new
                    {
                        sessionId = CoopSessionId,
                        reason,
                    });
                    _ = await Api.ReadJsonAsync<MultiplayerSessionEnvelope>(response);
                }
            }
            catch
            {
            }
        }

        await RefreshClientSessionAsync();

        await JS.InvokeVoidAsync("enigmaGame.clearLivePlayerState");
        await JS.InvokeVoidAsync("enigmaGame.disposeCoopSocket");
        await JS.InvokeVoidAsync("enigmaGame.disposeInput");
        await InvokeAsync(() => NavigationManager.NavigateTo("/lose", replace: true));
    }

    private bool IsOnBlackHoleNow()
    {
        return CurrentRoomState is not null &&
            CurrentRoomState.FinishPortalVisible &&
            CurrentRoomState.FinishPortalBounds.Intersects(new PlayAreaRect(PlayerX, PlayerY, PlayerSize, PlayerSize));
    }

    private static PlayerDirection ParseDirection(string? value)
    {
        return Enum.TryParse<PlayerDirection>(value, true, out var direction)
            ? direction
            : PlayerDirection.Down;
    }
}

