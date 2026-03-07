using System.Globalization;

namespace Enigma.Client.Models.Gameplay;

public enum PuzzleStatus
{
    NotStarted,
    Active,
    Solved,
    FailedTemporary,
    Resetting,
    Cooldown,
    HintAvailable,
    HintConsumed,
}

public enum PuzzleInteractionSource
{
    Keyboard,
    Click,
    Proximity,
}

public readonly record struct PuzzleProgressState(PlayAreaRect AnchorRect, double Progress, string Label);

public sealed record PuzzleWorldInteractable(
    string Id,
    PlayAreaRect Bounds,
    string CssClass,
    string Label,
    bool Enabled = true,
    int Priority = 0,
    double InteractionRange = 170d,
    bool Clickable = true);

public interface IPuzzleLifecycle
{
    PuzzleStatus Status { get; }
    bool IsSolved { get; }
    bool IsFailed { get; }
    bool CanInteract { get; }
    string GetStatusText();
}

public interface ITimedPuzzleUpdate
{
    void Update(double nowSeconds, double deltaSeconds);
}

public interface IWorldInteractivePuzzle : IPuzzleLifecycle, ITimedPuzzleUpdate
{
    IReadOnlyList<PuzzleWorldInteractable> GetWorldInteractables();

    bool TryInteract(
        string interactableId,
        PuzzleInteractionSource source,
        PlayAreaRect playerBounds,
        PlayerDirection playerFacing,
        double nowSeconds);

    bool TryGetProgressState(out PuzzleProgressState progressState);
}

public interface IBehaviorAdaptiveWorldPuzzle
{
    void ApplyBehaviorProfile(double horizontalBias, double rushBias);
}

public abstract class ImmersiveEasyPuzzleBase : RoomPuzzle, IWorldInteractivePuzzle
{
    private readonly List<PuzzleWorldInteractable> _interactables = [];
    private double _failureVisibleUntilSeconds;
    private double _cooldownUntilSeconds;
    private string? _resumeStatusText;

    protected ImmersiveEasyPuzzleBase(char key, string title, string instruction)
        : base(key, title, instruction)
    {
    }

    public PuzzleStatus Status { get; protected set; } = PuzzleStatus.NotStarted;

    public bool IsSolved => IsCompleted || Status == PuzzleStatus.Solved;

    public bool IsFailed => Status == PuzzleStatus.FailedTemporary;

    public virtual bool CanInteract => !IsSolved &&
        Status is not PuzzleStatus.FailedTemporary &&
        Status is not PuzzleStatus.Cooldown &&
        Status is not PuzzleStatus.Resetting;

    public string GetStatusText() => StatusText;

    public IReadOnlyList<PuzzleWorldInteractable> GetWorldInteractables()
    {
        _interactables.Clear();
        BuildWorldInteractables(_interactables);
        return _interactables;
    }

    public virtual bool TryGetProgressState(out PuzzleProgressState progressState)
    {
        progressState = default;
        return false;
    }

    public override void Update(PuzzleUpdateContext context)
    {
        if (IsSolved)
        {
            return;
        }

        if (Status == PuzzleStatus.NotStarted)
        {
            Status = PuzzleStatus.Active;
            StatusText = Instruction;
        }

        TickCooldown(context.NowSeconds);
        if (Status == PuzzleStatus.Cooldown)
        {
            return;
        }

        ObservePlayer(context.PlayerBounds, context.PlayerFacing, context.NowSeconds, context.DeltaTimeSeconds);
        Update(context.NowSeconds, context.DeltaTimeSeconds);
    }

    public virtual void Update(double nowSeconds, double deltaSeconds)
    {
    }

    public bool TryInteract(
        string interactableId,
        PuzzleInteractionSource source,
        PlayAreaRect playerBounds,
        PlayerDirection playerFacing,
        double nowSeconds)
    {
        if (IsSolved)
        {
            return false;
        }

        TickCooldown(nowSeconds);
        if (!CanInteract)
        {
            return false;
        }

        if (Status == PuzzleStatus.NotStarted)
        {
            Status = PuzzleStatus.Active;
            StatusText = Instruction;
        }

        return HandleInteract(interactableId, source, playerBounds, playerFacing, nowSeconds);
    }

    protected abstract void BuildWorldInteractables(List<PuzzleWorldInteractable> interactables);

    protected virtual void ObservePlayer(PlayAreaRect playerBounds, PlayerDirection facing, double nowSeconds, double deltaSeconds)
    {
    }

    protected abstract bool HandleInteract(
        string interactableId,
        PuzzleInteractionSource source,
        PlayAreaRect playerBounds,
        PlayerDirection playerFacing,
        double nowSeconds);

    protected void MarkSolved(string solvedStatus)
    {
        Status = PuzzleStatus.Solved;
        Complete(solvedStatus);
    }

    protected void MarkHintAvailable(string status)
    {
        if (IsSolved)
        {
            return;
        }

        Status = PuzzleStatus.HintAvailable;
        StatusText = status;
    }

    protected void MarkHintConsumed(string status)
    {
        if (IsSolved)
        {
            return;
        }

        Status = PuzzleStatus.HintConsumed;
        StatusText = status;
    }

    protected void TriggerSoftFailure(string status, double nowSeconds, double cooldownSeconds, string resumeStatus)
    {
        if (IsSolved)
        {
            return;
        }

        Status = PuzzleStatus.FailedTemporary;
        StatusText = status;
        _resumeStatusText = resumeStatus;
        _failureVisibleUntilSeconds = Math.Max(_failureVisibleUntilSeconds, nowSeconds + 0.24d);
        _cooldownUntilSeconds = Math.Max(_cooldownUntilSeconds, _failureVisibleUntilSeconds + Math.Max(0.15d, cooldownSeconds));
    }

    private void TickCooldown(double nowSeconds)
    {
        if (Status == PuzzleStatus.FailedTemporary)
        {
            if (nowSeconds < _failureVisibleUntilSeconds)
            {
                return;
            }

            Status = PuzzleStatus.Resetting;
            StatusText = "Recalibrating...";
            return;
        }

        if (Status == PuzzleStatus.Resetting)
        {
            if (nowSeconds < _failureVisibleUntilSeconds + 0.1d)
            {
                return;
            }

            Status = PuzzleStatus.Cooldown;
        }

        if (Status != PuzzleStatus.Cooldown)
        {
            return;
        }

        if (nowSeconds < _cooldownUntilSeconds)
        {
            return;
        }

        if (!IsSolved)
        {
            Status = PuzzleStatus.Active;
            StatusText = string.IsNullOrWhiteSpace(_resumeStatusText) ? Instruction : _resumeStatusText;
        }

        _resumeStatusText = null;
    }

    protected static bool TryParseInteractableIndex(string id, string prefix, int maxExclusive, out int index)
    {
        index = -1;
        if (!id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = id[prefix.Length..];
        if (!int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedIndex))
        {
            return false;
        }

        if (parsedIndex < 0 || parsedIndex >= maxExclusive)
        {
            return false;
        }

        index = parsedIndex;
        return true;
    }
}

public sealed class SignalRoutingChamberPuzzle : ImmersiveEasyPuzzleBase
{
    private readonly IReadOnlyList<PlayAreaRect> _relays;
    private readonly bool[] _requiredRelay;
    private readonly bool[] _overloadRelay;
    private readonly bool[] _relayActive;
    private readonly int[] _relayLinks;

    public SignalRoutingChamberPuzzle(
        IReadOnlyList<PlayAreaRect> relays,
        IReadOnlySet<int> requiredRelays,
        IReadOnlySet<int> overloadRelays,
        IReadOnlyList<int> relayLinks)
        : base('p', "Signal Routing Chamber", "Press E to route the stable relay path.")
    {
        _relays = relays;
        _requiredRelay = new bool[relays.Count];
        _overloadRelay = new bool[relays.Count];
        _relayActive = new bool[relays.Count];
        _relayLinks = new int[relays.Count];

        for (var index = 0; index < _relayLinks.Length; index++)
        {
            var linkedIndex = index;
            if (index < relayLinks.Count)
            {
                linkedIndex = Math.Clamp(relayLinks[index], 0, relays.Count - 1);
            }
            else if (relays.Count > 1)
            {
                linkedIndex = (index + 1) % relays.Count;
            }

            if (linkedIndex == index && relays.Count > 1)
            {
                linkedIndex = (index + 1) % relays.Count;
            }

            _relayLinks[index] = linkedIndex;
        }

        foreach (var index in requiredRelays)
        {
            if (index >= 0 && index < _requiredRelay.Length)
            {
                _requiredRelay[index] = true;
            }
        }

        foreach (var index in overloadRelays)
        {
            if (index >= 0 && index < _overloadRelay.Length)
            {
                _overloadRelay[index] = true;
            }
        }

        StatusText = "Chamber unstable.";
    }

    public int RequiredRelayCount => _requiredRelay.Count(flag => flag);
    public int MatchedRelayCount => CountMatchedRelays();
    public int ActiveRelayCount => _relayActive.Count(flag => flag);
    public int OverloadRelayCount => _overloadRelay.Count(flag => flag);

    protected override void BuildWorldInteractables(List<PuzzleWorldInteractable> interactables)
    {
        for (var index = 0; index < _relays.Count; index++)
        {
            var classes = "signal-relay";
            if (_requiredRelay[index])
            {
                classes += " required";
            }

            if (_overloadRelay[index])
            {
                classes += " overload";
            }

            if (_relayActive[index])
            {
                classes += " active";
            }

            if (_relayLinks[index] != index)
            {
                classes += " linked";
            }

            interactables.Add(new PuzzleWorldInteractable(
                $"relay-{index}",
                _relays[index],
                classes,
                (index + 1).ToString(CultureInfo.InvariantCulture),
                Enabled: CanInteract,
                Priority: _requiredRelay[index] ? 2 : 1,
                InteractionRange: 184d));
        }
    }

    protected override bool HandleInteract(
        string interactableId,
        PuzzleInteractionSource source,
        PlayAreaRect playerBounds,
        PlayerDirection playerFacing,
        double nowSeconds)
    {
        if (!TryParseInteractableIndex(interactableId, "relay-", _relays.Count, out var relayIndex))
        {
            return false;
        }

        ToggleRelay(relayIndex);
        var linkedRelayIndex = _relayLinks[relayIndex];
        if (linkedRelayIndex >= 0 && linkedRelayIndex < _relayActive.Length && linkedRelayIndex != relayIndex)
        {
            ToggleRelay(linkedRelayIndex);
        }

        if (HasActiveOverloadRelay())
        {
            ClearActiveOverloadRelays();
            TriggerSoftFailure("Relay overload.", nowSeconds, 0.72d, "Route rewritten.");
            return true;
        }

        if (IsSolvedConfiguration())
        {
            MarkSolved("Signal stabilized.");
            return true;
        }

        var matched = CountMatchedRelays();
        var required = Math.Max(1, RequiredRelayCount);
        StatusText = $"{matched}/{required} links stable.";
        return true;
    }

    public override bool TryGetProgressState(out PuzzleProgressState progressState)
    {
        var required = Math.Max(1, RequiredRelayCount);
        var matched = CountMatchedRelays();
        var anchorIndex = Enumerable.Range(0, _requiredRelay.Length).FirstOrDefault(index => _requiredRelay[index]);
        progressState = new PuzzleProgressState(_relays[anchorIndex], matched / (double)required, "Route Stability");
        return true;
    }

    private bool IsSolvedConfiguration()
    {
        for (var index = 0; index < _relayActive.Length; index++)
        {
            if (_relayActive[index] != _requiredRelay[index])
            {
                return false;
            }
        }

        return true;
    }

    private int CountMatchedRelays()
    {
        var count = 0;
        for (var index = 0; index < _relayActive.Length; index++)
        {
            if (_requiredRelay[index] && _relayActive[index])
            {
                count++;
            }
        }

        return count;
    }

    private bool HasActiveOverloadRelay()
    {
        for (var index = 0; index < _relayActive.Length; index++)
        {
            if (_overloadRelay[index] && _relayActive[index])
            {
                return true;
            }
        }

        return false;
    }

    private void ClearActiveOverloadRelays()
    {
        for (var index = 0; index < _relayActive.Length; index++)
        {
            if (_overloadRelay[index])
            {
                _relayActive[index] = false;
            }
        }
    }

    private void ToggleRelay(int index)
    {
        _relayActive[index] = !_relayActive[index];
    }
}

public sealed class EchoMemoryChamberPuzzle : ImmersiveEasyPuzzleBase
{
    private readonly IReadOnlyList<PlayAreaRect> _pads;
    private readonly IReadOnlyList<int> _sequence;
    private readonly double _revealStepSeconds;
    private readonly PlayAreaRect _replayNode;
    private int _enteredCount;
    private double _revealElapsed;
    private bool _revealComplete;
    private int _lastTouchedPad = -1;
    private int _replayChargesRemaining = 1;
    private bool _isReplayCycle;

    public EchoMemoryChamberPuzzle(IReadOnlyList<PlayAreaRect> pads, IReadOnlyList<int> sequence, double revealStepSeconds)
        : base('q', "Echo Memory", "Press E to replay and reconstruct the echo pattern.")
    {
        _pads = pads;
        _sequence = sequence;
        _revealStepSeconds = Math.Max(0.3d, revealStepSeconds);
        var centerX = pads.Average(pad => pad.CenterX);
        var centerY = pads.Average(pad => pad.CenterY);
        _replayNode = new PlayAreaRect(centerX - 44d, centerY - 44d, 88d, 88d);
        StatusText = "Echo feed active.";
    }

    public int SequenceLength => _sequence.Count;
    public int EnteredCount => _enteredCount;
    public int ReplayChargesRemaining => _replayChargesRemaining;
    public double RevealProgress => _revealComplete
        ? 1d
        : Math.Clamp(_revealElapsed / Math.Max(_revealStepSeconds, _sequence.Count * _revealStepSeconds), 0d, 1d);

    protected override void BuildWorldInteractables(List<PuzzleWorldInteractable> interactables)
    {
        var revealIndex = GetRevealIndex();
        for (var index = 0; index < _pads.Count; index++)
        {
            var classes = "echo-pad";
            if (!_revealComplete && revealIndex >= 0 && _sequence[Math.Min(revealIndex, _sequence.Count - 1)] == index)
            {
                classes += " reveal";
            }

            if (_enteredCount > 0 && _sequence.Take(_enteredCount).Contains(index))
            {
                classes += " locked";
            }

            interactables.Add(new PuzzleWorldInteractable(
                $"echo-pad-{index}",
                _pads[index],
                classes,
                ((char)('A' + index)).ToString(),
                Enabled: _revealComplete && CanInteract,
                Priority: 1,
                InteractionRange: 190d));
        }

        interactables.Add(new PuzzleWorldInteractable(
            "echo-replay",
            _replayNode,
            "echo-pad echo-core",
            _replayChargesRemaining > 0 ? "R" : string.Empty,
            Enabled: _revealComplete && _replayChargesRemaining > 0 && CanInteract,
            Priority: 3,
            InteractionRange: 205d));
    }

    public override void Update(double nowSeconds, double deltaSeconds)
    {
        if (_revealComplete)
        {
            return;
        }

        _revealElapsed += deltaSeconds;
        var revealSlots = _sequence.Count + 1;
        var revealIndex = (int)Math.Floor(_revealElapsed / _revealStepSeconds);
        if (revealIndex >= revealSlots)
        {
            _revealComplete = true;
            StatusText = _isReplayCycle ? "Replay complete." : "Echo locked.";
            _isReplayCycle = false;
        }
    }

    protected override void ObservePlayer(PlayAreaRect playerBounds, PlayerDirection facing, double nowSeconds, double deltaSeconds)
    {
        var touchedPad = GetTouchedPadIndex(playerBounds);
        if (touchedPad == _lastTouchedPad)
        {
            return;
        }

        _lastTouchedPad = touchedPad;
        if (touchedPad >= 0)
        {
            _ = TryHandlePadTouch(touchedPad, nowSeconds);
        }
    }

    protected override bool HandleInteract(
        string interactableId,
        PuzzleInteractionSource source,
        PlayAreaRect playerBounds,
        PlayerDirection playerFacing,
        double nowSeconds)
    {
        if (string.Equals(interactableId, "echo-replay", StringComparison.OrdinalIgnoreCase))
        {
            if (_replayChargesRemaining <= 0 || !_revealComplete || !CanInteract)
            {
                return false;
            }

            _replayChargesRemaining--;
            _enteredCount = 0;
            _revealElapsed = 0d;
            _revealComplete = false;
            _isReplayCycle = true;
            StatusText = "Echo replaying.";
            return true;
        }

        if (!TryParseInteractableIndex(interactableId, "echo-pad-", _pads.Count, out var padIndex))
        {
            return false;
        }

        return TryHandlePadTouch(padIndex, nowSeconds);
    }

    public override bool TryGetProgressState(out PuzzleProgressState progressState)
    {
        var anchorPad = _pads[Math.Min(_sequence[_enteredCount < _sequence.Count ? _enteredCount : _sequence.Count - 1], _pads.Count - 1)];
        progressState = new PuzzleProgressState(anchorPad, _enteredCount / (double)_sequence.Count, "Echo Sequence");
        return true;
    }

    private bool TryHandlePadTouch(int padIndex, double nowSeconds)
    {
        if (!_revealComplete || !CanInteract)
        {
            return false;
        }

        var expectedPad = _sequence[_enteredCount];
        if (padIndex == expectedPad)
        {
            _enteredCount++;
            if (_enteredCount >= _sequence.Count)
            {
                MarkSolved("Echo reconstructed.");
            }
            else
            {
                StatusText = $"{_enteredCount}/{_sequence.Count} fragments locked.";
            }

            return true;
        }

        _enteredCount = Math.Max(0, _enteredCount - 2);
        TriggerSoftFailure("Echo corruption.", nowSeconds, 0.52d, "Signal unstable.");
        return true;
    }

    private int GetTouchedPadIndex(PlayAreaRect playerBounds)
    {
        var centerX = playerBounds.CenterX;
        var centerY = playerBounds.CenterY;

        for (var index = 0; index < _pads.Count; index++)
        {
            if (_pads[index].Contains(centerX, centerY))
            {
                return index;
            }
        }

        return -1;
    }

    private int GetRevealIndex()
    {
        if (_revealComplete)
        {
            return -1;
        }

        var revealIndex = (int)Math.Floor(_revealElapsed / _revealStepSeconds);
        return revealIndex >= 0 && revealIndex < _sequence.Count ? revealIndex : -1;
    }
}

public sealed class DualLayerRealityPuzzle : ImmersiveEasyPuzzleBase
{
    private readonly IReadOnlyList<PlayAreaRect> _alphaNodes;
    private readonly IReadOnlyList<PlayAreaRect> _betaNodes;
    private readonly IReadOnlyList<int> _pairMapping;
    private readonly IReadOnlyList<int> _alphaOrder;
    private readonly PlayAreaRect _layerSwitch;
    private bool _isAlphaLayer = true;
    private bool _expectAlpha = true;
    private int _pairStep;
    private double _switchCooldownUntilSeconds;
    private double _lastNowSeconds;

    public DualLayerRealityPuzzle(
        IReadOnlyList<PlayAreaRect> alphaNodes,
        IReadOnlyList<PlayAreaRect> betaNodes,
        IReadOnlyList<int> pairMapping,
        IReadOnlyList<int> alphaOrder,
        PlayAreaRect layerSwitch)
        : base('r', "Dual-Layer Reality", "Press E to bridge matching nodes across both layers.")
    {
        _alphaNodes = alphaNodes;
        _betaNodes = betaNodes;
        _pairMapping = pairMapping;
        _alphaOrder = alphaOrder;
        _layerSwitch = layerSwitch;
        StatusText = "Layer A active.";
    }

    public int PairStep => _pairStep;
    public int PairCount => _pairMapping.Count;
    public bool IsAlphaLayerActive => _isAlphaLayer;
    public double SwitchCooldownRemainingSeconds => Math.Max(0d, _switchCooldownUntilSeconds - _lastNowSeconds);

    protected override void BuildWorldInteractables(List<PuzzleWorldInteractable> interactables)
    {
        interactables.Add(new PuzzleWorldInteractable(
            "layer-toggle",
            _layerSwitch,
            "duallayer-toggle",
            _isAlphaLayer ? "A" : "B",
            Enabled: CanInteract,
            Priority: 3,
            InteractionRange: 196d));

        for (var index = 0; index < _alphaNodes.Count; index++)
        {
            var enabled = CanInteract && _isAlphaLayer && _expectAlpha;
            var classes = "duallayer-node alpha";
            if (_pairStep > 0 && _alphaOrder.Take(_pairStep).Contains(index))
            {
                classes += " solved";
            }
            if (_expectAlpha && _pairStep < _alphaOrder.Count && _alphaOrder[_pairStep] == index)
            {
                classes += " target";
            }

            interactables.Add(new PuzzleWorldInteractable(
                $"alpha-{index}",
                _alphaNodes[index],
                classes,
                (index + 1).ToString(CultureInfo.InvariantCulture),
                Enabled: enabled,
                Priority: 2,
                InteractionRange: 184d));
        }

        for (var index = 0; index < _betaNodes.Count; index++)
        {
            var enabled = CanInteract && !_isAlphaLayer && !_expectAlpha;
            var classes = "duallayer-node beta";
            if (_pairStep > 0 && _alphaOrder.Take(_pairStep).Select(alphaIndex => _pairMapping[alphaIndex]).Contains(index))
            {
                classes += " solved";
            }
            if (!_expectAlpha && _pairStep < _alphaOrder.Count && _pairMapping[_alphaOrder[_pairStep]] == index)
            {
                classes += " target";
            }

            interactables.Add(new PuzzleWorldInteractable(
                $"beta-{index}",
                _betaNodes[index],
                classes,
                (index + 1).ToString(CultureInfo.InvariantCulture),
                Enabled: enabled,
                Priority: 2,
                InteractionRange: 184d));
        }
    }

    public override void Update(double nowSeconds, double deltaSeconds)
    {
        _lastNowSeconds = nowSeconds;
        if (_switchCooldownUntilSeconds > 0d && nowSeconds >= _switchCooldownUntilSeconds)
        {
            _switchCooldownUntilSeconds = 0d;
        }
    }

    protected override bool HandleInteract(
        string interactableId,
        PuzzleInteractionSource source,
        PlayAreaRect playerBounds,
        PlayerDirection playerFacing,
        double nowSeconds)
    {
        if (string.Equals(interactableId, "layer-toggle", StringComparison.OrdinalIgnoreCase))
        {
            if (_switchCooldownUntilSeconds > nowSeconds)
            {
                return false;
            }

            _isAlphaLayer = !_isAlphaLayer;
            _switchCooldownUntilSeconds = nowSeconds + 1.05d;
            StatusText = _isAlphaLayer ? "Layer A active." : "Layer B active.";
            return true;
        }

        if (_expectAlpha)
        {
            if (!TryParseInteractableIndex(interactableId, "alpha-", _alphaNodes.Count, out var alphaIndex) || !_isAlphaLayer)
            {
                return false;
            }

            var expectedAlpha = _alphaOrder[_pairStep];
            if (alphaIndex != expectedAlpha)
            {
                _pairStep = Math.Max(0, _pairStep - 1);
                TriggerSoftFailure("Phase mismatch.", nowSeconds, 0.66d, "Bridge unstable.");
                return true;
            }

            _expectAlpha = false;
            StatusText = "Bridge half-locked.";
            return true;
        }

        if (!TryParseInteractableIndex(interactableId, "beta-", _betaNodes.Count, out var betaIndex) || _isAlphaLayer)
        {
            return false;
        }

        var expectedBeta = _pairMapping[_alphaOrder[_pairStep]];
        if (betaIndex != expectedBeta)
        {
            _expectAlpha = true;
            _pairStep = Math.Max(0, _pairStep - 1);
            TriggerSoftFailure("Layer desync.", nowSeconds, 0.7d, "Bridge unstable.");
            return true;
        }

        _pairStep++;
        _expectAlpha = true;

        if (_pairStep >= _pairMapping.Count)
        {
            MarkSolved("Layers synchronized.");
        }
        else
        {
            StatusText = $"{_pairStep}/{_pairMapping.Count} bridges locked.";
        }

        return true;
    }

    public override bool TryGetProgressState(out PuzzleProgressState progressState)
    {
        var anchor = _expectAlpha
            ? _alphaNodes[Math.Min(_alphaOrder[Math.Min(_pairStep, _alphaOrder.Count - 1)], _alphaNodes.Count - 1)]
            : _betaNodes[Math.Min(_pairMapping[_alphaOrder[Math.Min(_pairStep, _alphaOrder.Count - 1)]], _betaNodes.Count - 1)];

        progressState = new PuzzleProgressState(anchor, _pairStep / (double)_pairMapping.Count, "Layer Sync");
        return true;
    }
}

public sealed class BehaviorAdaptivePuzzle : ImmersiveEasyPuzzleBase, IBehaviorAdaptiveWorldPuzzle
{
    private readonly IReadOnlyList<PlayAreaRect> _terminals;
    private readonly int[] _baseSequence;
    private int[] _activeSequence;
    private int _sequenceStep;
    private double _horizontalBias;
    private double _rushBias;
    private bool _started;
    private double _startedAtSeconds;
    private double _lastInputAtSeconds = double.NegativeInfinity;

    public BehaviorAdaptivePuzzle(IReadOnlyList<PlayAreaRect> terminals, IReadOnlyList<int> baseSequence)
        : base('s', "Behavior Pattern", "Press E to commit terminals without repeating your habits.")
    {
        _terminals = terminals;
        _baseSequence = baseSequence.ToArray();
        _activeSequence = _baseSequence.ToArray();
        StatusText = "The chamber is observing.";
    }

    public int SequenceStep => _sequenceStep;
    public int SequenceLength => _activeSequence.Length;
    public double AdaptedRushBias => _rushBias;
    public double AdaptedHorizontalBias => _horizontalBias;

    public void ApplyBehaviorProfile(double horizontalBias, double rushBias)
    {
        _horizontalBias = Math.Clamp(horizontalBias, -0.35d, 0.35d);
        _rushBias = Math.Clamp(rushBias, 0d, 0.35d);
        _activeSequence = _baseSequence
            .Where(index => index >= 0 && index < _terminals.Count)
            .Distinct()
            .ToArray();

        if (_activeSequence.Length == 0)
        {
            _activeSequence = [0];
        }

        if (_activeSequence.Length < Math.Min(3, _terminals.Count))
        {
            var remaining = Enumerable.Range(0, _terminals.Count)
                .Where(index => !_activeSequence.Contains(index))
                .ToList();
            while (_activeSequence.Length < Math.Min(3, _terminals.Count) && remaining.Count > 0)
            {
                _activeSequence = [.. _activeSequence, remaining[0]];
                remaining.RemoveAt(0);
            }
        }

        if (_horizontalBias > 0.14d)
        {
            _activeSequence[0] = _terminals.Count - 1;
        }
        else if (_horizontalBias < -0.14d)
        {
            _activeSequence[0] = 0;
        }

        if (_rushBias > 0.18d && _activeSequence.Length > 2)
        {
            (_activeSequence[1], _activeSequence[^1]) = (_activeSequence[^1], _activeSequence[1]);
        }

        // Keep sequence deterministic and duplicate-free after adaptation.
        for (var index = 1; index < _activeSequence.Length; index++)
        {
            if (_activeSequence[index] == _activeSequence[index - 1])
            {
                _activeSequence[index] = (_activeSequence[index] + 1) % _terminals.Count;
            }
        }

        _sequenceStep = Math.Clamp(_sequenceStep, 0, Math.Max(0, _activeSequence.Length - 1));
    }

    protected override void BuildWorldInteractables(List<PuzzleWorldInteractable> interactables)
    {
        for (var index = 0; index < _terminals.Count; index++)
        {
            var classes = "behavior-terminal";
            classes += index switch
            {
                0 => " left",
                1 => " center",
                _ => " right",
            };

            if (_sequenceStep > index)
            {
                classes += " solved";
            }

            interactables.Add(new PuzzleWorldInteractable(
                $"behavior-{index}",
                _terminals[index],
                classes,
                string.Empty,
                Enabled: CanInteract,
                Priority: index == _activeSequence[Math.Min(_sequenceStep, _activeSequence.Length - 1)] ? 3 : 1,
                InteractionRange: 196d));
        }
    }

    protected override bool HandleInteract(
        string interactableId,
        PuzzleInteractionSource source,
        PlayAreaRect playerBounds,
        PlayerDirection playerFacing,
        double nowSeconds)
    {
        if (!TryParseInteractableIndex(interactableId, "behavior-", _terminals.Count, out var terminalIndex))
        {
            return false;
        }

        if (!_started)
        {
            _started = true;
            _startedAtSeconds = nowSeconds;
        }

        var rushLockSeconds = 0.18d + (_rushBias * 1.4d);
        if (nowSeconds - _startedAtSeconds < rushLockSeconds)
        {
            TriggerSoftFailure("Impulse pattern detected.", nowSeconds, 0.58d, "Pattern shifted.");
            return true;
        }

        var cadenceSeconds = 0.24d - (_rushBias * 0.1d);
        if (nowSeconds - _lastInputAtSeconds < Math.Max(0.12d, cadenceSeconds))
        {
            TriggerSoftFailure("Cadence anomaly.", nowSeconds, 0.55d, "Pattern shifted.");
            return true;
        }

        _lastInputAtSeconds = nowSeconds;
        var expectedIndex = _activeSequence[_sequenceStep];
        if (terminalIndex != expectedIndex)
        {
            _sequenceStep = Math.Max(0, _sequenceStep - 1);
            TriggerSoftFailure("Behavior mirror tripped.", nowSeconds, 0.64d, "Pattern shifted.");
            return true;
        }

        _sequenceStep++;
        if (_sequenceStep >= _activeSequence.Length)
        {
            MarkSolved("Pattern broken.");
        }
        else
        {
            StatusText = $"{_sequenceStep}/{_activeSequence.Length} pattern phases locked.";
        }

        return true;
    }

    public override bool TryGetProgressState(out PuzzleProgressState progressState)
    {
        var anchor = _terminals[Math.Min(_activeSequence[Math.Min(_sequenceStep, _activeSequence.Length - 1)], _terminals.Count - 1)];
        progressState = new PuzzleProgressState(anchor, _sequenceStep / (double)_activeSequence.Length, "Pattern Depth");
        return true;
    }
}

public sealed class RecursiveRoomMutationPuzzle : ImmersiveEasyPuzzleBase
{
    private readonly IReadOnlyList<PlayAreaRect> _anchors;
    private readonly IReadOnlyList<int> _changes;
    private int _loopIndex;
    private double _loopRevealUntilSeconds;
    private bool _revealVisible;

    public RecursiveRoomMutationPuzzle(IReadOnlyList<PlayAreaRect> anchors, IReadOnlyList<int> changes)
        : base('t', "Recursive Room", "Press E to lock the meaningful change each loop.")
    {
        _anchors = anchors;
        _changes = changes;
        StatusText = "Loop initialized.";
    }

    public int LoopIndex => _loopIndex;
    public int LoopCount => _changes.Count;
    public bool IsRevealVisible => _revealVisible;

    protected override void BuildWorldInteractables(List<PuzzleWorldInteractable> interactables)
    {
        var expected = _changes[Math.Min(_loopIndex, _changes.Count - 1)];
        for (var index = 0; index < _anchors.Count; index++)
        {
            var glyph = ((char)('A' + ((index + _loopIndex) % _anchors.Count))).ToString();
            var classes = "recursive-anchor";
            if (_loopIndex > 0 && _changes.Take(_loopIndex).Contains(index))
            {
                classes += " locked";
            }
            if (_revealVisible && index == expected)
            {
                classes += " target";
            }

            interactables.Add(new PuzzleWorldInteractable(
                $"recursive-{index}",
                _anchors[index],
                classes,
                glyph,
                Enabled: CanInteract,
                Priority: 1,
                InteractionRange: 180d));
        }
    }

    public override void Update(double nowSeconds, double deltaSeconds)
    {
        if (IsSolved || _loopIndex >= _changes.Count)
        {
            return;
        }

        if (_loopRevealUntilSeconds <= 0d)
        {
            _loopRevealUntilSeconds = nowSeconds + 1.18d;
        }

        _revealVisible = nowSeconds <= _loopRevealUntilSeconds;
    }

    protected override bool HandleInteract(
        string interactableId,
        PuzzleInteractionSource source,
        PlayAreaRect playerBounds,
        PlayerDirection playerFacing,
        double nowSeconds)
    {
        if (!TryParseInteractableIndex(interactableId, "recursive-", _anchors.Count, out var anchorIndex))
        {
            return false;
        }

        var expected = _changes[_loopIndex];
        if (anchorIndex != expected)
        {
            _loopIndex = Math.Max(0, _loopIndex - 1);
            _loopRevealUntilSeconds = nowSeconds + 0.96d;
            TriggerSoftFailure("Loop rejection.", nowSeconds, 0.56d, "Loop shifted.");
            return true;
        }

        _loopIndex++;
        _loopRevealUntilSeconds = nowSeconds + 0.96d;
        if (_loopIndex >= _changes.Count)
        {
            MarkSolved("Recursive pattern resolved.");
        }
        else
        {
            StatusText = $"Loop {_loopIndex + 1}/{_changes.Count}";
        }

        return true;
    }

    public override bool TryGetProgressState(out PuzzleProgressState progressState)
    {
        var anchor = _anchors[_changes[Math.Min(_loopIndex, _changes.Count - 1)]];
        progressState = new PuzzleProgressState(anchor, _loopIndex / (double)_changes.Count, "Loop Integrity");
        return true;
    }
}

public sealed class LivingGridPuzzle : ImmersiveEasyPuzzleBase
{
    private readonly int _size;
    private readonly double _originX;
    private readonly double _originY;
    private readonly double _cellSize;
    private readonly bool[] _target;
    private readonly bool[] _cells;
    private readonly bool[] _checkpoint;
    private int _moveCount;
    private int _bestMatchedCount;

    public LivingGridPuzzle(int size, double originX, double originY, double cellSize, bool[] initialCells, bool[] target)
        : base('u', "Living Grid", "Press E to pulse cells until the grid aligns.")
    {
        _size = size;
        _originX = originX;
        _originY = originY;
        _cellSize = cellSize;
        _target = target.ToArray();
        _cells = initialCells.ToArray();
        _checkpoint = _cells.ToArray();
        _bestMatchedCount = CountMatchedCells();
        StatusText = "Grid active.";
    }

    public int MatchedCells => CountMatchedCells();
    public int CellCount => _cells.Length;
    public int MoveCount => _moveCount;

    protected override void BuildWorldInteractables(List<PuzzleWorldInteractable> interactables)
    {
        for (var index = 0; index < _cells.Length; index++)
        {
            var bounds = InsetRect(GetCellBounds(index), 10d);
            var classes = "living-grid-cell";
            if (_target[index])
            {
                classes += " target";
            }

            if (_cells[index])
            {
                classes += " active";
            }

            if (_cells[index] == _target[index])
            {
                classes += " matched";
            }
            else if (_target[index])
            {
                classes += " needed";
            }
            else if (_cells[index])
            {
                classes += " excess";
            }

            interactables.Add(new PuzzleWorldInteractable(
                $"grid-{index}",
                bounds,
                classes,
                string.Empty,
                Enabled: CanInteract,
                Priority: _cells[index] == _target[index] ? 1 : 2,
                InteractionRange: 124d));
        }
    }

    protected override void ObservePlayer(PlayAreaRect playerBounds, PlayerDirection facing, double nowSeconds, double deltaSeconds)
    {
        // Living grid uses explicit input only (E/click) to avoid accidental pulses while moving.
    }

    protected override bool HandleInteract(
        string interactableId,
        PuzzleInteractionSource source,
        PlayAreaRect playerBounds,
        PlayerDirection playerFacing,
        double nowSeconds)
    {
        if (!TryParseInteractableIndex(interactableId, "grid-", _cells.Length, out var cellIndex))
        {
            return false;
        }

        ActivateCell(cellIndex, nowSeconds);
        return true;
    }

    public override bool TryGetProgressState(out PuzzleProgressState progressState)
    {
        var matched = CountMatchedCells();
        progressState = new PuzzleProgressState(
            new PlayAreaRect(_originX, _originY, _size * _cellSize, _size * _cellSize),
            matched / (double)_cells.Length,
            "Grid Match");
        return true;
    }

    private void ActivateCell(int cellIndex, double nowSeconds)
    {
        ToggleCell(cellIndex);

        var x = cellIndex % _size;
        var y = cellIndex / _size;
        if (x > 0)
        {
            ToggleCell(cellIndex - 1);
        }

        if (x < _size - 1)
        {
            ToggleCell(cellIndex + 1);
        }

        if (y > 0)
        {
            ToggleCell(cellIndex - _size);
        }

        if (y < _size - 1)
        {
            ToggleCell(cellIndex + _size);
        }

        _moveCount++;
        var matched = CountMatchedCells();
        if (matched > _bestMatchedCount)
        {
            _bestMatchedCount = matched;
            Array.Copy(_cells, _checkpoint, _cells.Length);
        }

        if (IsTargetReached())
        {
            MarkSolved("Grid stabilized.");
            return;
        }

        StatusText = $"{matched}/{_cells.Length} cells aligned.";
    }

    private void ToggleCell(int index)
    {
        _cells[index] = !_cells[index];
    }

    private bool IsTargetReached()
    {
        for (var index = 0; index < _cells.Length; index++)
        {
            if (_cells[index] != _target[index])
            {
                return false;
            }
        }

        return true;
    }

    private int CountMatchedCells()
    {
        var count = 0;
        for (var index = 0; index < _cells.Length; index++)
        {
            if (_cells[index] == _target[index])
            {
                count++;
            }
        }

        return count;
    }

    private PlayAreaRect GetCellBounds(int index)
    {
        var x = index % _size;
        var y = index / _size;
        return new PlayAreaRect(_originX + (x * _cellSize), _originY + (y * _cellSize), _cellSize, _cellSize);
    }

    private static PlayAreaRect InsetRect(PlayAreaRect rect, double inset)
    {
        var clampedInset = Math.Clamp(inset, 0d, Math.Min(rect.Width, rect.Height) * 0.45d);
        return new PlayAreaRect(
            rect.X + clampedInset,
            rect.Y + clampedInset,
            rect.Width - (clampedInset * 2d),
            rect.Height - (clampedInset * 2d));
    }

    private int GetCellIndex(double x, double y)
    {
        var localX = x - _originX;
        var localY = y - _originY;
        if (localX < 0d || localY < 0d)
        {
            return -1;
        }

        var cellX = (int)(localX / _cellSize);
        var cellY = (int)(localY / _cellSize);
        if (cellX < 0 || cellX >= _size || cellY < 0 || cellY >= _size)
        {
            return -1;
        }

        return (cellY * _size) + cellX;
    }
}

public sealed class SymbolDecoderPuzzle : ImmersiveEasyPuzzleBase
{
    private static readonly string[] SymbolOrder = ["A", "B", "C", "D"];
    private const int RequiredCoherence = 2;

    private readonly IReadOnlyList<PlayAreaRect> _symbolNodes;
    private readonly int _targetValue;
    private readonly int _startValue;
    private int _currentValue;
    private int _moves;
    private int _coherence;

    public SymbolDecoderPuzzle(IReadOnlyList<PlayAreaRect> symbolNodes, int startValue, int targetValue)
        : base('v', "Symbol Decoder", "Press E to apply symbols and align the decoder.")
    {
        _symbolNodes = symbolNodes;
        _startValue = startValue;
        _currentValue = startValue;
        _targetValue = targetValue;
        StatusText = "Decoder active.";
    }

    public int CurrentValue => _currentValue;
    public int TargetValue => _targetValue;
    public int MoveCount => _moves;
    public int Coherence => _coherence;

    protected override void BuildWorldInteractables(List<PuzzleWorldInteractable> interactables)
    {
        for (var index = 0; index < _symbolNodes.Count; index++)
        {
            var symbol = SymbolOrder[index % SymbolOrder.Length];
            var classes = "symbol-node";
            classes += symbol switch
            {
                "A" => " sigma",
                "B" => " delta",
                "C" => " lambda",
                _ => " omega",
            };

            interactables.Add(new PuzzleWorldInteractable(
                $"symbol-{symbol}",
                _symbolNodes[index],
                classes,
                symbol,
                Enabled: CanInteract,
                Priority: 2,
                InteractionRange: 182d));
        }
    }

    protected override bool HandleInteract(
        string interactableId,
        PuzzleInteractionSource source,
        PlayAreaRect playerBounds,
        PlayerDirection playerFacing,
        double nowSeconds)
    {
        if (!interactableId.StartsWith("symbol-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var previousDistance = GetWrappedDistance(_targetValue, _currentValue);
        var symbol = interactableId[7..].ToUpperInvariant();
        _currentValue = ApplySymbol(symbol, _currentValue);
        _moves++;
        var newDistance = GetWrappedDistance(_targetValue, _currentValue);

        if (newDistance < previousDistance)
        {
            _coherence = Math.Min(RequiredCoherence + 1, _coherence + 1);
        }
        else if (newDistance > previousDistance)
        {
            _coherence = Math.Max(0, _coherence - 1);
        }

        if (_currentValue == _targetValue && _coherence >= RequiredCoherence)
        {
            MarkSolved("Decoder aligned.");
            return true;
        }

        if (_currentValue == _targetValue)
        {
            StatusText = "Signal at target but unstable.";
            return true;
        }

        if (_moves >= 12)
        {
            _moves = 6;
            _currentValue = (_currentValue + _startValue) / 2;
            _coherence = Math.Max(0, _coherence - 1);
            TriggerSoftFailure("Decoder drift.", nowSeconds, 0.66d, "Decoder recalibrated.");
            return true;
        }

        StatusText = $"Phase {_currentValue:00}";
        return true;
    }

    public override bool TryGetProgressState(out PuzzleProgressState progressState)
    {
        var distance = Math.Abs(_targetValue - _currentValue);
        distance = Math.Min(distance, 12 - distance);
        var proximity = 1d - (distance / 6d);
        progressState = new PuzzleProgressState(_symbolNodes[0], Math.Clamp(proximity, 0d, 1d), "Decoder Proximity");
        return true;
    }

    public static string DescribeSymbol(string symbol) => symbol switch
    {
        "A" => "advance by three",
        "B" => "fold across midpoint",
        "C" => "retreat by three",
        "D" => "mirror bit mask",
        _ => "unknown",
    };

    private static int ApplySymbol(string symbol, int value)
    {
        var wrapped = value % 12;
        if (wrapped < 0)
        {
            wrapped += 12;
        }

        return symbol switch
        {
            "A" => (wrapped + 3) % 12,
            "B" => (11 - wrapped) % 12,
            "C" => (wrapped + 9) % 12,
            "D" => (wrapped ^ 0b0011) % 12,
            _ => wrapped,
        };
    }

    private static int GetWrappedDistance(int a, int b)
    {
        var distance = Math.Abs(a - b) % 12;
        return Math.Min(distance, 12 - distance);
    }
}

public sealed class TimeWindowPuzzle : ImmersiveEasyPuzzleBase
{
    private readonly IReadOnlyList<PlayAreaRect> _gates;
    private readonly IReadOnlyList<int> _order;
    private readonly double _cycleSeconds;
    private readonly double _windowLeadFraction;
    private readonly double _windowSpanFraction;
    private int _step;
    private double _cycleElapsedSeconds;

    public TimeWindowPuzzle(
        IReadOnlyList<PlayAreaRect> gates,
        IReadOnlyList<int> order,
        double cycleSeconds,
        double windowLeadFraction,
        double windowSpanFraction)
        : base('w', "Time Window", "Press E during the stable gate windows.")
    {
        _gates = gates;
        _order = order;
        _cycleSeconds = Math.Max(3d, cycleSeconds);
        _windowLeadFraction = Math.Clamp(windowLeadFraction, 0.04d, 0.46d);
        _windowSpanFraction = Math.Clamp(windowSpanFraction, 0.22d, 0.64d);
        StatusText = "Cycle scanning.";
    }

    public int Step => _step;
    public int StepCount => _order.Count;
    public int OpenGateIndex => GetOpenGateIndex();
    public double CycleProgress => GetNormalizedCycle();

    protected override void BuildWorldInteractables(List<PuzzleWorldInteractable> interactables)
    {
        var openGateIndex = GetOpenGateIndex();
        for (var index = 0; index < _gates.Count; index++)
        {
            var classes = "time-gate";
            if (index == openGateIndex && IsGateOpen(index))
            {
                classes += " open";
            }

            if (_step > 0 && _order.Take(_step).Contains(index))
            {
                classes += " locked";
            }

            interactables.Add(new PuzzleWorldInteractable(
                $"time-gate-{index}",
                _gates[index],
                classes,
                (index + 1).ToString(CultureInfo.InvariantCulture),
                Enabled: CanInteract,
                Priority: index == _order[Math.Min(_step, _order.Count - 1)] ? 3 : 1,
                InteractionRange: 184d));
        }
    }

    public override void Update(double nowSeconds, double deltaSeconds)
    {
        _cycleElapsedSeconds += Math.Max(0d, deltaSeconds);
        if (_cycleElapsedSeconds >= _cycleSeconds)
        {
            _cycleElapsedSeconds %= _cycleSeconds;
        }
    }

    protected override bool HandleInteract(
        string interactableId,
        PuzzleInteractionSource source,
        PlayAreaRect playerBounds,
        PlayerDirection playerFacing,
        double nowSeconds)
    {
        if (!TryParseInteractableIndex(interactableId, "time-gate-", _gates.Count, out var gateIndex))
        {
            return false;
        }

        var expectedGate = _order[_step];
        if (gateIndex == expectedGate && IsGateOpen(gateIndex))
        {
            _step++;
            if (_step >= _order.Count)
            {
                MarkSolved("Cycle captured.");
            }
            else
            {
                StatusText = $"{_step}/{_order.Count} windows locked.";
            }

            return true;
        }

        _step = Math.Max(0, _step - 1);
        TriggerSoftFailure("Out-of-phase input.", nowSeconds, 0.58d, "Cycle slipping.");
        return true;
    }

    public override bool TryGetProgressState(out PuzzleProgressState progressState)
    {
        var anchor = _gates[_order[Math.Min(_step, _order.Count - 1)]];
        progressState = new PuzzleProgressState(anchor, _step / (double)_order.Count, "Cycle Lock");
        return true;
    }

    private int GetOpenGateIndex()
    {
        var normalized = GetNormalizedCycle();
        var segment = 1d / _gates.Count;
        var index = (int)Math.Floor(normalized / segment);
        return Math.Clamp(index, 0, _gates.Count - 1);
    }

    private double GetNormalizedCycle()
    {
        if (_cycleSeconds <= 0.0001d)
        {
            return 0d;
        }

        var normalized = _cycleElapsedSeconds % _cycleSeconds;
        if (normalized < 0d)
        {
            normalized += _cycleSeconds;
        }

        return normalized / _cycleSeconds;
    }

    private bool IsGateOpen(int gateIndex)
    {
        if (gateIndex < 0 || gateIndex >= _gates.Count)
        {
            return false;
        }

        var normalized = GetNormalizedCycle();
        var segment = 1d / _gates.Count;
        var gateStart = gateIndex * segment;
        var local = normalized - gateStart;
        if (local < 0d)
        {
            local += 1d;
        }

        if (local > segment)
        {
            return false;
        }

        var openStart = segment * _windowLeadFraction;
        var openEnd = openStart + (segment * _windowSpanFraction);
        return local >= openStart && local <= openEnd;
    }
}

public sealed class FalseSolutionPuzzle : ImmersiveEasyPuzzleBase
{
    private readonly IReadOnlyList<PlayAreaRect> _terminals;
    private readonly int[] _realSequence;
    private readonly int[] _decoySequence;
    private int _realStep;
    private int _decoyPressure;

    public FalseSolutionPuzzle(IReadOnlyList<PlayAreaRect> terminals, IReadOnlyList<int> realSequence, IReadOnlyList<int> decoySequence)
        : base('x', "False Solution", "Press E to test routes and expose the true path.")
    {
        _terminals = terminals;
        _realSequence = realSequence.ToArray();
        _decoySequence = decoySequence.ToArray();
        StatusText = "Route appears stable.";
    }

    public int RealStep => _realStep;
    public int SequenceLength => _realSequence.Length;
    public int DecoyPressure => _decoyPressure;

    protected override void BuildWorldInteractables(List<PuzzleWorldInteractable> interactables)
    {
        var decoyTerminal = _decoySequence[Math.Min(_realStep, _decoySequence.Length - 1)];
        for (var index = 0; index < _terminals.Count; index++)
        {
            var classes = "false-terminal";
            if (_realStep > 0 && index == _realSequence[0])
            {
                classes += " primed";
            }
            if (index == decoyTerminal)
            {
                classes += " decoy";
            }

            interactables.Add(new PuzzleWorldInteractable(
                $"false-{index}",
                _terminals[index],
                classes,
                string.Empty,
                Enabled: CanInteract,
                Priority: index == _realSequence[Math.Min(_realStep, _realSequence.Length - 1)] ? 3 : 1,
                InteractionRange: 188d));
        }
    }

    protected override bool HandleInteract(
        string interactableId,
        PuzzleInteractionSource source,
        PlayAreaRect playerBounds,
        PlayerDirection playerFacing,
        double nowSeconds)
    {
        if (!TryParseInteractableIndex(interactableId, "false-", _terminals.Count, out var terminalIndex))
        {
            return false;
        }

        var expected = _realSequence[_realStep];
        var decoy = _decoySequence[_realStep];
        if (terminalIndex == expected)
        {
            _realStep++;
            _decoyPressure = Math.Max(0, _decoyPressure - 1);
            if (_realStep >= _realSequence.Length)
            {
                MarkSolved("True route confirmed.");
            }
            else
            {
                StatusText = $"{_realStep}/{_realSequence.Length} segments trusted.";
            }

            return true;
        }

        if (terminalIndex == decoy)
        {
            _decoyPressure++;
            if (_decoyPressure >= 2)
            {
                _realStep = Math.Max(0, _realStep - 1);
                _decoyPressure = 0;
                TriggerSoftFailure("False progress collapsed.", nowSeconds, 0.72d, "Route rewritten.");
            }
            else
            {
                StatusText = "Route seems to open...";
            }
        }
        else
        {
            _realStep = Math.Max(0, _realStep - 1);
            _decoyPressure = Math.Max(0, _decoyPressure - 1);
            TriggerSoftFailure("Route contradiction.", nowSeconds, 0.56d, "Topology shifted.");
        }

        return true;
    }

    public override bool TryGetProgressState(out PuzzleProgressState progressState)
    {
        var anchor = _terminals[_realSequence[Math.Min(_realStep, _realSequence.Length - 1)]];
        progressState = new PuzzleProgressState(anchor, _realStep / (double)_realSequence.Length, "Route Certainty");
        return true;
    }
}

public sealed class HeatPressureBalancePuzzle : ImmersiveEasyPuzzleBase
{
    private const double StableMin = 46d;
    private const double StableMax = 54d;

    private readonly IReadOnlyList<PuzzleWorldInteractable> _controls;
    private readonly double _driftHeat;
    private readonly double _driftPressure;
    private readonly double _requiredStabilitySeconds;
    private double _heat;
    private double _pressure;
    private double _stabilitySeconds;

    public HeatPressureBalancePuzzle(
        IReadOnlyList<PuzzleWorldInteractable> controls,
        double startHeat,
        double startPressure,
        double driftHeat,
        double driftPressure,
        double requiredStabilitySeconds)
        : base('y', "Heat / Pressure Balance", "Press E to tune heat and pressure into equilibrium.")
    {
        _controls = controls;
        _heat = startHeat;
        _pressure = startPressure;
        _driftHeat = driftHeat;
        _driftPressure = driftPressure;
        _requiredStabilitySeconds = Math.Max(1.2d, requiredStabilitySeconds);
        StatusText = BuildStatusText("Tune both systems into the safe band.");
    }

    public double Heat => _heat;
    public double Pressure => _pressure;
    public bool IsInStableBand => IsStable();
    public double StabilityProgress => Math.Clamp(_stabilitySeconds / _requiredStabilitySeconds, 0d, 1d);

    protected override void BuildWorldInteractables(List<PuzzleWorldInteractable> interactables)
    {
        foreach (var control in _controls)
        {
            interactables.Add(control with { Enabled = CanInteract });
        }
    }

    public override void Update(double nowSeconds, double deltaSeconds)
    {
        // Drift values are kept tiny and deterministic so puzzle behavior stays predictable.
        _heat = Math.Clamp(_heat + (_driftHeat * deltaSeconds), 0d, 100d);
        _pressure = Math.Clamp(_pressure + (_driftPressure * deltaSeconds), 0d, 100d);

        var inRange = IsStable();
        if (inRange)
        {
            _stabilitySeconds = Math.Min(_requiredStabilitySeconds, _stabilitySeconds + deltaSeconds);
        }
        else
        {
            _stabilitySeconds = Math.Max(0d, _stabilitySeconds - (deltaSeconds * 0.55d));
        }

        if (_stabilitySeconds >= _requiredStabilitySeconds)
        {
            MarkSolved("System equilibrium achieved.");
            return;
        }

        if (_heat is < 4d or > 96d || _pressure is < 4d or > 96d)
        {
            _heat = Math.Clamp(_heat, 12d, 88d);
            _pressure = Math.Clamp(_pressure, 12d, 88d);
            _stabilitySeconds = Math.Max(0d, _stabilitySeconds - 0.8d);
            StatusText = BuildStatusText("Safety vent engaged.");
            return;
        }

        StatusText = BuildStatusText(inRange ? "Band held." : "Adjust controls.");
    }

    protected override bool HandleInteract(
        string interactableId,
        PuzzleInteractionSource source,
        PlayAreaRect playerBounds,
        PlayerDirection playerFacing,
        double nowSeconds)
    {
        if (string.Equals(interactableId, "heat-up", StringComparison.OrdinalIgnoreCase))
        {
            ApplyAdjustment(8.2d, 0d);
        }
        else if (string.Equals(interactableId, "heat-down", StringComparison.OrdinalIgnoreCase))
        {
            ApplyAdjustment(-8.2d, 0d);
        }
        else if (string.Equals(interactableId, "pressure-up", StringComparison.OrdinalIgnoreCase))
        {
            ApplyAdjustment(0d, 8.2d);
        }
        else if (string.Equals(interactableId, "pressure-down", StringComparison.OrdinalIgnoreCase))
        {
            ApplyAdjustment(0d, -8.2d);
        }
        else
        {
            return false;
        }

        StatusText = BuildStatusText("Input accepted.");
        return true;
    }

    public override bool TryGetProgressState(out PuzzleProgressState progressState)
    {
        var anchor = new PlayAreaRect(470d, 470d, 140d, 140d);
        progressState = new PuzzleProgressState(anchor, _stabilitySeconds / _requiredStabilitySeconds, "Equilibrium Lock");
        return true;
    }

    public string BuildTelemetry() => $"Heat {Math.Round(_heat):0} | Pressure {Math.Round(_pressure):0} | Band {StableMin:0}-{StableMax:0}";

    private bool IsStable() => _heat is >= StableMin and <= StableMax && _pressure is >= StableMin and <= StableMax;

    private string BuildStatusText(string prefix) =>
        $"{prefix} {BuildTelemetry()} | Stable {Math.Round(StabilityProgress * 100d):0}%";

    private void ApplyAdjustment(double heatDelta, double pressureDelta)
    {
        _heat = Math.Clamp(_heat + heatDelta, 0d, 100d);
        _pressure = Math.Clamp(_pressure + pressureDelta, 0d, 100d);
    }
}

public sealed class HiddenRulePrimePuzzle : ImmersiveEasyPuzzleBase
{
    private readonly int _size;
    private readonly double _originX;
    private readonly double _originY;
    private readonly double _cellSize;
    private readonly int[] _safeOrder;
    private int _progress;

    public HiddenRulePrimePuzzle(int size, double originX, double originY, double cellSize, IReadOnlyList<int> safeOrder)
        : base('z', "Hidden Rule", "Press E to test tiles and infer the hidden rule.")
    {
        _size = size;
        _originX = originX;
        _originY = originY;
        _cellSize = cellSize;
        _safeOrder = safeOrder.ToArray();
        StatusText = "No explicit rule detected.";
    }

    public int Progress => _progress;
    public int RequiredCount => _safeOrder.Length;

    protected override void BuildWorldInteractables(List<PuzzleWorldInteractable> interactables)
    {
        for (var index = 0; index < _size * _size; index++)
        {
            var classes = "hidden-rule-tile";
            if (_progress > 0 && _safeOrder.Take(_progress).Contains(index))
            {
                classes += " confirmed";
            }

            interactables.Add(new PuzzleWorldInteractable(
                $"hidden-{index}",
                GetCellBounds(index),
                classes,
                (index + 1).ToString(CultureInfo.InvariantCulture),
                Enabled: CanInteract,
                Priority: 1,
                InteractionRange: 168d));
        }
    }

    protected override void ObservePlayer(PlayAreaRect playerBounds, PlayerDirection facing, double nowSeconds, double deltaSeconds)
    {
        // Hidden rule uses explicit interaction only (E/click) to prevent accidental triggers while moving.
    }

    protected override bool HandleInteract(
        string interactableId,
        PuzzleInteractionSource source,
        PlayAreaRect playerBounds,
        PlayerDirection playerFacing,
        double nowSeconds)
    {
        if (!TryParseInteractableIndex(interactableId, "hidden-", _size * _size, out var tileIndex))
        {
            return false;
        }

        return ApplySelection(tileIndex, nowSeconds);
    }

    public override bool TryGetProgressState(out PuzzleProgressState progressState)
    {
        var anchor = GetCellBounds(_safeOrder[Math.Min(_progress, _safeOrder.Length - 1)]);
        progressState = new PuzzleProgressState(anchor, _progress / (double)_safeOrder.Length, "Rule Depth");
        return true;
    }

    private bool ApplySelection(int tileIndex, double nowSeconds)
    {
        var expected = _safeOrder[_progress];
        if (tileIndex == expected)
        {
            _progress++;
            if (_progress >= _safeOrder.Length)
            {
                MarkSolved("Rule inferred.");
            }
            else
            {
                StatusText = $"{_progress}/{_safeOrder.Length} rule anchors confirmed.";
            }

            return true;
        }

        _progress = Math.Max(0, _progress - 1);
        TriggerSoftFailure("Rule conflict.", nowSeconds, 0.5d, "Pattern shifted.");
        return true;
    }

    private PlayAreaRect GetCellBounds(int index)
    {
        var x = index % _size;
        var y = index / _size;
        return new PlayAreaRect(_originX + (x * _cellSize), _originY + (y * _cellSize), _cellSize, _cellSize);
    }

}

public static class ImmersiveEasyPuzzleFactory
{
    private const double RoomSize = 1080d;

    public static RoomPuzzle Create(string seed, string runNonce, MazeRoomDefinition room)
    {
        var layoutSeed = PuzzleFactory.StableHash($"layout|{seed}|{room.Coordinates.X}|{room.Coordinates.Y}|{room.PuzzleKey}");
        var solutionSeed = PuzzleFactory.StableHash($"solution|{seed}|{runNonce}|{room.Coordinates.X}|{room.Coordinates.Y}|{room.PuzzleKey}");

        return room.PuzzleKey switch
        {
            'p' => BuildSignalRouting(layoutSeed, solutionSeed),
            'q' => BuildEchoMemory(layoutSeed, solutionSeed),
            'r' => BuildDualLayer(layoutSeed, solutionSeed),
            's' => BuildBehaviorPuzzle(layoutSeed, solutionSeed),
            't' => BuildRecursivePuzzle(layoutSeed, solutionSeed),
            'u' => BuildLivingGrid(layoutSeed, solutionSeed),
            'v' => BuildSymbolDecoder(layoutSeed, solutionSeed),
            'w' => BuildTimeWindow(layoutSeed, solutionSeed),
            'x' => BuildFalseSolution(layoutSeed, solutionSeed),
            'y' => BuildHeatPressure(layoutSeed, solutionSeed),
            'z' => BuildHiddenRule(layoutSeed, solutionSeed),
            _ => throw new MazeSeedParseException($"Unknown immersive easy puzzle key '{room.PuzzleKey}'."),
        };
    }

    private static RoomPuzzle BuildSignalRouting(int layoutSeed, int solutionSeed)
    {
        const int relayCount = 6;
        var relays = SpreadOutRects(CreateRing(layoutSeed, relayCount, 90d, 220d, 302d), 168d);
        var links = BuildRelayLinks(layoutSeed ^ 0x51b9, relayCount);
        var requiredPattern = BuildSignalTargetPattern(solutionSeed, links, relayCount);
        var required = Enumerable.Range(0, relayCount).Where(index => requiredPattern[index]).ToHashSet();
        if (required.Count < 2)
        {
            required = [0, 2];
        }

        var overload = PickDistinct(solutionSeed ^ 0x2a17, relayCount, 2)
            .Where(index => !required.Contains(index))
            .ToHashSet();
        if (overload.Count == 0)
        {
            overload.Add((required.First() + 1) % relayCount);
        }

        return new SignalRoutingChamberPuzzle(relays, required, overload, links);
    }

    private static RoomPuzzle BuildEchoMemory(int layoutSeed, int solutionSeed)
    {
        var pads = SpreadOutRects(CreateSquareNodes(layoutSeed, 4, 132d, 286d, 258d, 508d, 482d), 212d);
        var sequence = BuildSequence(solutionSeed, 4, 6);
        return new EchoMemoryChamberPuzzle(pads, sequence, 0.56d);
    }

    private static RoomPuzzle BuildDualLayer(int layoutSeed, int solutionSeed)
    {
        const int nodeCount = 4;
        var alpha = SpreadOutRects(CreateLaneNodes(layoutSeed, nodeCount, 130d, 304d, 128d, 102d, 9d), 146d);
        var beta = SpreadOutRects(CreateLaneNodes(layoutSeed ^ 0x22bb, nodeCount, 580d, 304d, 106d, 102d, 9d), 132d);
        var map = PickDistinct(solutionSeed, nodeCount, nodeCount);
        var alphaOrder = PickDistinct(solutionSeed ^ 0x61cf, nodeCount, nodeCount);
        var layerSwitch = new PlayAreaRect(488d, 472d, 104d, 104d);
        return new DualLayerRealityPuzzle(alpha, beta, map, alphaOrder, layerSwitch);
    }

    private static RoomPuzzle BuildBehaviorPuzzle(int layoutSeed, int solutionSeed)
    {
        var terminals = SpreadOutRects(CreateLineNodes(layoutSeed, 4, 156d, 382d, 218d, 122d), 176d);
        var sequence = PickDistinct(solutionSeed, terminals.Count, 3);
        return new BehaviorAdaptivePuzzle(terminals, sequence);
    }

    private static RoomPuzzle BuildRecursivePuzzle(int layoutSeed, int solutionSeed)
    {
        var anchors = SpreadOutRects(CreateRing(layoutSeed, 5, 118d, 184d, 240d), 168d);
        var changes = PickDistinct(solutionSeed, anchors.Count, 4);
        return new RecursiveRoomMutationPuzzle(anchors, changes);
    }

    private static RoomPuzzle BuildLivingGrid(int layoutSeed, int solutionSeed)
    {
        var size = 3;
        var target = BuildBitPattern(solutionSeed, size * size, requiredOnCount: 4);
        var initial = BuildSolvableLivingGridInitial(layoutSeed ^ 0x6149, target, size, scrambleMoves: 6);

        return new LivingGridPuzzle(size, 270d, 270d, 180d, initial, target);
    }

    private static RoomPuzzle BuildSymbolDecoder(int layoutSeed, int solutionSeed)
    {
        var symbolNodes = SpreadOutRects(CreateRing(layoutSeed, 4, 108d, 170d, 240d), 186d);
        var startValue = Math.Abs(layoutSeed % 12);
        var targetValue = Math.Abs(solutionSeed % 12);
        var distance = Math.Min(Math.Abs(targetValue - startValue), 12 - Math.Abs(targetValue - startValue));
        if (distance < 2)
        {
            targetValue = (targetValue + 5) % 12;
        }

        return new SymbolDecoderPuzzle(symbolNodes, startValue, targetValue);
    }

    private static RoomPuzzle BuildTimeWindow(int layoutSeed, int solutionSeed)
    {
        const int gateCount = 4;
        var gates = SpreadOutRects(CreateRing(layoutSeed, gateCount, 118d, 184d, 246d), 172d);
        var order = BuildSequence(solutionSeed, gateCount, 5);
        var lead = 0.14d + (Math.Abs((solutionSeed / 13) % 10) * 0.01d);
        var span = 0.34d + (Math.Abs((solutionSeed / 29) % 8) * 0.01d);
        return new TimeWindowPuzzle(gates, order, 7.2d, lead, span);
    }

    private static RoomPuzzle BuildFalseSolution(int layoutSeed, int solutionSeed)
    {
        var terminals = SpreadOutRects(CreateLineNodes(layoutSeed, 4, 162d, 404d, 214d, 122d), 174d);
        var sequence = PickDistinct(solutionSeed, terminals.Count, 3);
        var decoys = BuildDecoySequence(solutionSeed ^ 0x3471, terminals.Count, sequence);
        return new FalseSolutionPuzzle(terminals, sequence, decoys);
    }

    private static RoomPuzzle BuildHeatPressure(int layoutSeed, int solutionSeed)
    {
        var controls = new List<PuzzleWorldInteractable>
        {
            new("heat-up", new PlayAreaRect(336d, 314d, 114d, 114d), "heat-control heat-up", "+H", Priority: 2, InteractionRange: 188d),
            new("heat-down", new PlayAreaRect(336d, 652d, 114d, 114d), "heat-control heat-down", "-H", Priority: 2, InteractionRange: 188d),
            new("pressure-up", new PlayAreaRect(628d, 314d, 114d, 114d), "pressure-control pressure-up", "+P", Priority: 2, InteractionRange: 188d),
            new("pressure-down", new PlayAreaRect(628d, 652d, 114d, 114d), "pressure-control pressure-down", "-P", Priority: 2, InteractionRange: 188d),
        };

        var heat = 36d + Math.Abs(layoutSeed % 29);
        var pressure = 33d + Math.Abs((layoutSeed / 13) % 31);
        var driftHeat = 0d;
        var driftPressure = 0d;
        return new HeatPressureBalancePuzzle(controls, heat, pressure, driftHeat, driftPressure, 3.3d);
    }

    private static RoomPuzzle BuildHiddenRule(int layoutSeed, int solutionSeed)
    {
        var safeOrder = BuildHiddenRuleOrder(solutionSeed);
        return new HiddenRulePrimePuzzle(3, 328d, 236d, 138d, safeOrder);
    }

    private static IReadOnlyList<PlayAreaRect> CreateRing(int seed, int count, double size, double minRadius, double maxRadius)
    {
        var state = seed;
        var nodes = new List<PlayAreaRect>(count);
        for (var index = 0; index < count; index++)
        {
            state = NextState(state + index + 17);
            var angle = ((Math.PI * 2d) / count) * index;
            var radius = minRadius + (state % (int)Math.Max(1d, maxRadius - minRadius));
            var x = 540d + (Math.Cos(angle) * radius) - (size / 2d) + ((state % 34) - 17);
            var y = 540d + (Math.Sin(angle) * radius) - (size / 2d) + (((state / 11) % 34) - 17);
            nodes.Add(ClampRect(new PlayAreaRect(x, y, size, size)));
        }

        return nodes;
    }

    private static IReadOnlyList<PlayAreaRect> CreateSquareNodes(int seed, int count, double size, double x1, double y1, double x2, double y2)
    {
        var points = new[]
        {
            new PlayAreaRect(x1, y1, size, size),
            new PlayAreaRect(x2, y1, size, size),
            new PlayAreaRect(x1, y2, size, size),
            new PlayAreaRect(x2, y2, size, size),
        };

        var state = seed;
        var result = new List<PlayAreaRect>(count);
        for (var index = 0; index < count; index++)
        {
            state = NextState(state + index + 19);
            var template = points[index % points.Length];
            var jitterX = (state % 28) - 14;
            var jitterY = ((state / 9) % 28) - 14;
            result.Add(ClampRect(template with { X = template.X + jitterX, Y = template.Y + jitterY }));
        }

        return result;
    }

    private static IReadOnlyList<PlayAreaRect> CreateLineNodes(int seed, int count, double startX, double startY, double stepX, double size)
    {
        var state = seed;
        var nodes = new List<PlayAreaRect>(count);
        for (var index = 0; index < count; index++)
        {
            state = NextState(state + index + 23);
            var x = startX + (stepX * index) + ((state % 24) - 12);
            var y = startY + (((state / 7) % 20) - 10);
            nodes.Add(ClampRect(new PlayAreaRect(x, y, size, size)));
        }

        return nodes;
    }

    private static IReadOnlyList<PlayAreaRect> CreateLaneNodes(
        int seed,
        int count,
        double startX,
        double startY,
        double stepX,
        double size,
        double yJitter)
    {
        var state = seed;
        var nodes = new List<PlayAreaRect>(count);
        for (var index = 0; index < count; index++)
        {
            state = NextState(state + index + 37);
            var x = startX + (stepX * index);
            var y = startY + (((state / 9) % (int)Math.Max(1d, (yJitter * 2d) + 1d)) - yJitter);
            nodes.Add(ClampRect(new PlayAreaRect(x, y, size, size)));
        }

        return nodes;
    }

    private static IReadOnlyList<PlayAreaRect> SpreadOutRects(IReadOnlyList<PlayAreaRect> source, double minimumCenterDistance, int maxIterations = 20)
    {
        if (source.Count < 2 || minimumCenterDistance <= 0d)
        {
            return source;
        }

        var rects = source.ToArray();
        var minDistance = Math.Max(1d, minimumCenterDistance);
        var minDistanceSquared = minDistance * minDistance;

        for (var iteration = 0; iteration < Math.Max(1, maxIterations); iteration++)
        {
            var moved = false;
            for (var leftIndex = 0; leftIndex < rects.Length - 1; leftIndex++)
            {
                for (var rightIndex = leftIndex + 1; rightIndex < rects.Length; rightIndex++)
                {
                    var leftRect = rects[leftIndex];
                    var rightRect = rects[rightIndex];
                    var deltaX = rightRect.CenterX - leftRect.CenterX;
                    var deltaY = rightRect.CenterY - leftRect.CenterY;
                    var distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
                    if (distanceSquared >= minDistanceSquared)
                    {
                        continue;
                    }

                    var distance = Math.Sqrt(distanceSquared);
                    if (distance < 0.001d)
                    {
                        deltaX = 1d;
                        deltaY = 0d;
                        distance = 1d;
                    }

                    var overlap = minDistance - distance;
                    if (overlap <= 0d)
                    {
                        continue;
                    }

                    var adjustX = (deltaX / distance) * (overlap * 0.5d);
                    var adjustY = (deltaY / distance) * (overlap * 0.5d);
                    rects[leftIndex] = ClampRect(leftRect with { X = leftRect.X - adjustX, Y = leftRect.Y - adjustY });
                    rects[rightIndex] = ClampRect(rightRect with { X = rightRect.X + adjustX, Y = rightRect.Y + adjustY });
                    moved = true;
                }
            }

            if (!moved)
            {
                break;
            }
        }

        return rects;
    }

    private static IReadOnlyList<int> PickDistinct(int seed, int upperExclusive, int count)
    {
        var state = seed;
        var pool = Enumerable.Range(0, upperExclusive).ToList();
        var result = new List<int>(Math.Min(count, upperExclusive));
        for (var index = 0; index < count && pool.Count > 0; index++)
        {
            state = NextState(state + index + 29);
            var pick = state % pool.Count;
            result.Add(pool[pick]);
            pool.RemoveAt(pick);
        }

        return result;
    }

    private static IReadOnlyList<int> BuildSequence(int seed, int symbolCount, int length)
    {
        var state = seed;
        var sequence = new List<int>(length);
        var previous = -1;

        while (sequence.Count < length)
        {
            state = NextState(state + sequence.Count + 31);
            var candidate = state % symbolCount;
            if (candidate == previous)
            {
                candidate = (candidate + 1) % symbolCount;
            }

            sequence.Add(candidate);
            previous = candidate;
        }

        return sequence;
    }

    private static bool[] BuildBitPattern(int seed, int length, int requiredOnCount)
    {
        var pattern = new bool[length];
        var picks = PickDistinct(seed, length, Math.Clamp(requiredOnCount, 1, length));
        foreach (var pick in picks)
        {
            pattern[pick] = true;
        }

        return pattern;
    }

    private static IReadOnlyList<int> BuildRelayLinks(int seed, int count)
    {
        var links = new int[count];
        var permutation = PickDistinct(seed, count, count).ToArray();
        for (var index = 0; index < count; index++)
        {
            var candidate = permutation[index];
            if (candidate == index)
            {
                candidate = (index + 1) % count;
            }

            links[index] = candidate;
        }

        return links;
    }

    private static bool[] BuildSignalTargetPattern(int seed, IReadOnlyList<int> links, int count)
    {
        var state = seed;
        var pattern = new bool[count];
        var attempt = 0;
        while (attempt < 8)
        {
            Array.Fill(pattern, false);
            var pulseCount = 5 + (Math.Abs((state / 7) % 4));
            for (var pulseIndex = 0; pulseIndex < pulseCount; pulseIndex++)
            {
                state = NextState(state + pulseIndex + 37 + (attempt * 17));
                var relayIndex = state % count;
                ToggleSignalNode(pattern, relayIndex);
                if (relayIndex >= 0 && relayIndex < links.Count)
                {
                    var linkedIndex = links[relayIndex];
                    if (linkedIndex >= 0 && linkedIndex < count && linkedIndex != relayIndex)
                    {
                        ToggleSignalNode(pattern, linkedIndex);
                    }
                }
            }

            var active = pattern.Count(flag => flag);
            if (active >= 2 && active <= count - 1)
            {
                return pattern;
            }

            attempt++;
            state = NextState(state + 71 + attempt);
        }

        return pattern;
    }

    private static void ToggleSignalNode(bool[] pattern, int index)
    {
        if (index < 0 || index >= pattern.Length)
        {
            return;
        }

        pattern[index] = !pattern[index];
    }

    private static bool[] BuildSolvableLivingGridInitial(int seed, bool[] target, int size, int scrambleMoves)
    {
        var state = seed;
        var current = target.ToArray();
        var totalCells = size * size;
        var moves = Math.Max(3, scrambleMoves);
        var previous = -1;
        for (var move = 0; move < moves; move++)
        {
            state = NextState(state + move + 41);
            var index = state % totalCells;
            if (index == previous)
            {
                index = (index + 1) % totalCells;
            }

            ApplyLivingGridMove(current, size, index);
            previous = index;
        }

        if (current.SequenceEqual(target))
        {
            ApplyLivingGridMove(current, size, Math.Abs(state % totalCells));
        }

        return current;
    }

    private static void ApplyLivingGridMove(bool[] cells, int size, int index)
    {
        var x = index % size;
        var y = index / size;
        ToggleLivingGridCell(cells, index);
        if (x > 0)
        {
            ToggleLivingGridCell(cells, index - 1);
        }

        if (x < size - 1)
        {
            ToggleLivingGridCell(cells, index + 1);
        }

        if (y > 0)
        {
            ToggleLivingGridCell(cells, index - size);
        }

        if (y < size - 1)
        {
            ToggleLivingGridCell(cells, index + size);
        }
    }

    private static void ToggleLivingGridCell(bool[] cells, int index)
    {
        if (index < 0 || index >= cells.Length)
        {
            return;
        }

        cells[index] = !cells[index];
    }

    private static IReadOnlyList<int> BuildDecoySequence(int seed, int terminalCount, IReadOnlyList<int> realSequence)
    {
        var state = seed;
        var decoys = new int[realSequence.Count];
        for (var index = 0; index < realSequence.Count; index++)
        {
            state = NextState(state + index + 43);
            var decoy = state % terminalCount;
            if (decoy == realSequence[index])
            {
                decoy = (decoy + 1) % terminalCount;
            }

            decoys[index] = decoy;
        }

        return decoys;
    }

    private static int[] BuildHiddenRuleOrder(int seed)
    {
        var variant = Math.Abs(seed % 5);
        return variant switch
        {
            // 1-based prime positions: 2, 3, 5, 7.
            0 => [1, 2, 4, 6],
            // Corners (clockwise) then center.
            1 => RotateOrder([0, 2, 8, 6, 4], Math.Abs((seed / 7) % 4)),
            // Checkerboard parity.
            2 => (seed & 1) == 0 ? [0, 2, 4, 6, 8] : [1, 3, 5, 7],
            // Cross arms then center.
            3 => RotateOrder([1, 5, 7, 3, 4], Math.Abs((seed / 11) % 4)),
            // Spiral edge segment.
            _ => RotateOrder([0, 1, 2, 5, 8], Math.Abs((seed / 13) % 5)),
        };
    }

    private static int[] RotateOrder(int[] values, int shift)
    {
        if (values.Length == 0)
        {
            return values;
        }

        var normalizedShift = shift % values.Length;
        if (normalizedShift < 0)
        {
            normalizedShift += values.Length;
        }

        var rotated = new int[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            rotated[index] = values[(index + normalizedShift) % values.Length];
        }

        return rotated;
    }

    private static PlayAreaRect ClampRect(PlayAreaRect rect)
    {
        var x = Math.Clamp(rect.X, 78d, RoomSize - rect.Width - 78d);
        var y = Math.Clamp(rect.Y, 78d, RoomSize - rect.Height - 78d);
        return new PlayAreaRect(x, y, rect.Width, rect.Height);
    }

    private static int NextState(int seed)
    {
        unchecked
        {
            return (int)(((uint)seed * 1103515245u + 12345u) & 0x7fffffff);
        }
    }
}
