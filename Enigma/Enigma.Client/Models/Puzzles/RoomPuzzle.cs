namespace Enigma.Client.Models.Gameplay;

public abstract class RoomPuzzle
{
    protected RoomPuzzle(char key, string title, string instruction)
    {
        PuzzleKey = key;
        Title = title;
        Instruction = instruction;
        StatusText = instruction;
    }

    public char PuzzleKey { get; }
    public string Title { get; }
    public string Instruction { get; }
    public string StatusText { get; protected set; }
    public bool IsCompleted { get; protected set; }

    public virtual void Update(PuzzleUpdateContext context)
    {
    }

    protected void Complete(string statusText)
    {
        IsCompleted = true;
        StatusText = statusText;
    }

    public void SyncCompleted(string? statusText = null)
    {
        if (IsCompleted)
        {
            return;
        }

        IsCompleted = true;
        if (!string.IsNullOrWhiteSpace(statusText))
        {
            StatusText = statusText;
        }
    }
}

public sealed class PressurePlatePuzzle : RoomPuzzle
{
    public PressurePlatePuzzle(PlayAreaRect plateBounds, double requiredHoldSeconds, double decayRate)
        : base('p', "Pressure Plate", "Stand on the plate until it locks in. Leaving early drains the charge.")
    {
        PlateBounds = plateBounds;
        RequiredHoldSeconds = requiredHoldSeconds;
        DecayRate = decayRate;
    }

    public PlayAreaRect PlateBounds { get; }
    public double RequiredHoldSeconds { get; }
    public double DecayRate { get; }
    public double HeldSeconds { get; private set; }

    public double Progress => Math.Clamp(HeldSeconds / RequiredHoldSeconds, 0d, 1d);

    public override void Update(PuzzleUpdateContext context)
    {
        if (IsCompleted)
        {
            return;
        }

        if (PlateBounds.Intersects(context.PlayerBounds))
        {
            HeldSeconds = Math.Min(RequiredHoldSeconds, HeldSeconds + context.DeltaTimeSeconds);
            StatusText = Progress >= 0.92d ? "Plate almost locked. Hold steady." : "Plate charging. Stay on it.";
        }
        else if (HeldSeconds > 0d)
        {
            HeldSeconds = Math.Max(0d, HeldSeconds - (context.DeltaTimeSeconds * DecayRate));
            StatusText = HeldSeconds <= 0.05d ? Instruction : "Plate charge slipping. Step back onto it.";
        }

        if (HeldSeconds >= RequiredHoldSeconds)
        {
            Complete("Pressure plate secured.");
        }
    }
}

public sealed class QuickTimePuzzle : RoomPuzzle
{
    private const double HitTolerance = 0.003d;
    private const double VisualMarkerHalfSpan = 0.011d;
    private int _direction = 1;

    public QuickTimePuzzle(double targetStart, double targetWidth, double pulseSpeed, int requiredHits)
        : base('q', "Quick Time Reaction", "Stop the pulse while it is inside the bright window. Harder locks demand multiple perfect hits in a row.")
    {
        TargetStart = targetStart;
        TargetWidth = targetWidth;
        PulseSpeed = pulseSpeed;
        RequiredHits = Math.Max(1, requiredHits);
        MeterValue = 0.12d;
    }

    public double MeterValue { get; private set; }
    public double TargetStart { get; }
    public double TargetWidth { get; }
    public double PulseSpeed { get; }
    public int RequiredHits { get; }
    public int SuccessfulHits { get; private set; }

    public override void Update(PuzzleUpdateContext context)
    {
        if (IsCompleted)
        {
            return;
        }

        MeterValue += _direction * context.DeltaTimeSeconds * PulseSpeed;
        if (MeterValue >= 1d)
        {
            MeterValue = 1d;
            _direction = -1;
        }
        else if (MeterValue <= 0d)
        {
            MeterValue = 0d;
            _direction = 1;
        }
    }

    public void AttemptStop()
    {
        if (IsCompleted)
        {
            return;
        }

        var targetEnd = TargetStart + TargetWidth;
        var adjustedMeter = Math.Clamp(MeterValue, 0d, 1d);
        var markerStart = Math.Max(0d, adjustedMeter - VisualMarkerHalfSpan);
        var markerEnd = Math.Min(1d, adjustedMeter + VisualMarkerHalfSpan);
        var overlapsTarget = markerEnd >= TargetStart - HitTolerance && markerStart <= targetEnd + HitTolerance;

        if (overlapsTarget)
        {
            SuccessfulHits++;
            if (SuccessfulHits >= RequiredHits)
            {
                Complete("Reaction puzzle solved.");
                return;
            }

            StatusText = $"Precision hit {SuccessfulHits}/{RequiredHits}. Hold steady.";
            return;
        }

        SuccessfulHits = 0;
        StatusText = "Missed the window. Precision streak reset.";
    }
}

public sealed class RiddlePuzzle : RoomPuzzle
{
    public RiddlePuzzle(string question, IReadOnlyList<string> options, int correctIndex)
        : base('r', "Riddle Lock", question)
    {
        Question = question;
        Options = options;
        CorrectIndex = correctIndex;
    }

    public string Question { get; }
    public IReadOnlyList<string> Options { get; }
    public int CorrectIndex { get; }

    public void Choose(int index)
    {
        if (IsCompleted)
        {
            return;
        }

        if (index == CorrectIndex)
        {
            Complete("The riddle lock clicks open.");
            return;
        }

        StatusText = "Wrong answer. Read it again.";
    }
}

public sealed class SequenceMemoryPuzzle : RoomPuzzle
{
    private readonly List<string> _entered = [];
    private bool _sequenceHiddenNoticeShown;

    public SequenceMemoryPuzzle(IReadOnlyList<string> sequence, double revealDurationSeconds)
        : base('s', "Sequence Memory", "Memorize the rune order before it fades, then repeat it from memory.")
    {
        Sequence = sequence;
        RevealDurationSeconds = revealDurationSeconds;
    }

    public IReadOnlyList<string> Sequence { get; }
    public IReadOnlyList<string> Entered => _entered;
    public double RevealDurationSeconds { get; }
    public double ElapsedSeconds { get; private set; }
    public bool IsSequenceVisible => IsCompleted || ElapsedSeconds < RevealDurationSeconds;

    public override void Update(PuzzleUpdateContext context)
    {
        if (IsCompleted || IsSequenceVisible == false)
        {
            return;
        }

        ElapsedSeconds += context.DeltaTimeSeconds;
        if (!_sequenceHiddenNoticeShown && ElapsedSeconds >= RevealDurationSeconds)
        {
            _sequenceHiddenNoticeShown = true;
            StatusText = "The runes fade. Repeat the sequence from memory.";
        }
    }

    public void Press(string symbol)
    {
        if (IsCompleted)
        {
            return;
        }

        var expected = Sequence[_entered.Count];
        if (!string.Equals(expected, symbol, StringComparison.Ordinal))
        {
            _entered.Clear();
            StatusText = "Sequence broken. Start again.";
            return;
        }

        _entered.Add(symbol);
        StatusText = $"Runes matched: {_entered.Count}/{Sequence.Count}";

        if (_entered.Count == Sequence.Count)
        {
            Complete("Sequence remembered.");
        }
    }
}

public sealed class TileRotationPuzzle : RoomPuzzle
{
    public TileRotationPuzzle(int[] rotations)
        : base('t', "Tile Rotation", "Rotate every tile so the arrows point north.")
    {
        Rotations = rotations;
        UpdateStatus();
    }

    public int[] Rotations { get; }

    public void Rotate(int index)
    {
        if (IsCompleted || index < 0 || index >= Rotations.Length)
        {
            return;
        }

        Rotations[index] = (Rotations[index] + 1) % 4;
        if (Rotations.All(rotation => rotation == 0))
        {
            Complete("Tile array aligned.");
            return;
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var remaining = Rotations.Count(rotation => rotation != 0);
        StatusText = $"Tiles remaining: {remaining}";
    }
}

public sealed class UnlockPatternPuzzle : RoomPuzzle
{
    private readonly List<PlayerDirection> _entered = [];
    private bool _patternHiddenNoticeShown;

    public UnlockPatternPuzzle(IReadOnlyList<PlayerDirection> pattern, double revealDurationSeconds)
        : base('u', "Unlock Pattern", "Memorize the directional sequence, wait for it to disappear, then replay it with the arrow keys.")
    {
        Pattern = pattern;
        RevealDurationSeconds = revealDurationSeconds;
        StatusText = "Watch the pattern. Input stays locked until the arrows vanish.";
    }

    public IReadOnlyList<PlayerDirection> Pattern { get; }
    public IReadOnlyList<PlayerDirection> Entered => _entered;
    public double RevealDurationSeconds { get; }
    public double ElapsedSeconds { get; private set; }
    public bool IsPatternVisible => IsCompleted || ElapsedSeconds < RevealDurationSeconds;
    public bool AcceptsInput => !IsCompleted && !IsPatternVisible;

    public override void Update(PuzzleUpdateContext context)
    {
        if (IsCompleted || IsPatternVisible == false)
        {
            return;
        }

        ElapsedSeconds += context.DeltaTimeSeconds;
        if (!_patternHiddenNoticeShown && ElapsedSeconds >= RevealDurationSeconds)
        {
            _patternHiddenNoticeShown = true;
            StatusText = "The pattern vanishes. Use the arrow keys to replay it from memory.";
        }
    }

    public void Press(PlayerDirection direction)
    {
        if (IsCompleted)
        {
            return;
        }

        if (IsPatternVisible)
        {
            StatusText = "Hold still. The lock only listens after the pattern disappears.";
            return;
        }

        var expected = Pattern[_entered.Count];
        if (expected != direction)
        {
            _entered.Clear();
            StatusText = "Pattern rejected. Resetting lock.";
            return;
        }

        _entered.Add(direction);
        StatusText = $"Pattern progress: {_entered.Count}/{Pattern.Count}";

        if (_entered.Count == Pattern.Count)
        {
            Complete("Pattern accepted.");
        }
    }
}

public sealed class ValveFlowPuzzle : RoomPuzzle
{
    public ValveFlowPuzzle(int[] valveValues, int targetFlow, int requiredOpenValveCount)
        : base('v', "Valve Flow", "Route pressure through the right valves until the channel stabilizes at the target flow using the exact channel count.")
    {
        ValveValues = valveValues;
        TargetFlow = targetFlow;
        RequiredOpenValveCount = Math.Clamp(requiredOpenValveCount, 1, valveValues.Length);
        OpenStates = new bool[valveValues.Length];
        UpdateStatus();
    }

    public int[] ValveValues { get; }
    public bool[] OpenStates { get; }
    public int TargetFlow { get; }
    public int RequiredOpenValveCount { get; }
    public int CurrentFlow => ValveValues.Where((value, index) => OpenStates[index]).Sum();
    public int OpenValveCount => OpenStates.Count(isOpen => isOpen);

    public void Toggle(int index)
    {
        if (IsCompleted || index < 0 || index >= OpenStates.Length)
        {
            return;
        }

        OpenStates[index] = !OpenStates[index];
        if (CurrentFlow == TargetFlow && OpenValveCount == RequiredOpenValveCount)
        {
            Complete("Flow stabilized.");
            return;
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var delta = TargetFlow - CurrentFlow;
        StatusText = delta switch
        {
            0 when OpenValveCount == RequiredOpenValveCount => "Pressure stabilized.",
            0 => $"Exact flow reached, but you need exactly {RequiredOpenValveCount} open channels.",
            > 0 => $"Flow is short by {delta}. Open exactly {RequiredOpenValveCount} channels.",
            _ => $"Flow is high by {Math.Abs(delta)}. Keep the total on exactly {RequiredOpenValveCount} open channels.",
        };
    }
}

public sealed class WeightBalancePuzzle : RoomPuzzle
{
    public WeightBalancePuzzle(int[] weights, int targetWeight, int requiredSelectionCount)
        : base('w', "Weight Balance", "Load the platform until the total matches the target using the exact number of weights.")
    {
        Weights = weights;
        TargetWeight = targetWeight;
        RequiredSelectionCount = Math.Clamp(requiredSelectionCount, 1, weights.Length);
        Selected = new bool[weights.Length];
        UpdateStatus();
    }

    public int[] Weights { get; }
    public bool[] Selected { get; }
    public int TargetWeight { get; }
    public int RequiredSelectionCount { get; }
    public int CurrentWeight => Weights.Where((value, index) => Selected[index]).Sum();
    public int SelectedCount => Selected.Count(selected => selected);

    public void Toggle(int index)
    {
        if (IsCompleted || index < 0 || index >= Selected.Length)
        {
            return;
        }

        Selected[index] = !Selected[index];
        if (CurrentWeight == TargetWeight && SelectedCount == RequiredSelectionCount)
        {
            Complete("Balance restored.");
            return;
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        StatusText = CurrentWeight == TargetWeight
            ? $"Exact load reached. Now match it with exactly {RequiredSelectionCount} weights."
            : $"Current load: {CurrentWeight} / {TargetWeight} using exactly {RequiredSelectionCount} weights.";
    }
}

public sealed class XorLogicPuzzle : RoomPuzzle
{
    public XorLogicPuzzle(bool targetOutput, int inputCount, int targetEnabledCount)
        : base('x', "XOR Logic", "Toggle the signal bits until the circuit matches both the required parity and the required number of live inputs.")
    {
        TargetOutput = targetOutput;
        Inputs = new bool[Math.Max(3, inputCount)];
        TargetEnabledCount = Math.Clamp(targetEnabledCount, 1, Inputs.Length);
        UpdateStatus();
    }

    public bool[] Inputs { get; }
    public bool TargetOutput { get; }
    public int TargetEnabledCount { get; }
    public bool CurrentOutput => Inputs.Aggregate(false, (state, input) => state ^ input);
    public int EnabledInputCount => Inputs.Count(input => input);

    public void Toggle(int index)
    {
        if (IsCompleted || index < 0 || index >= Inputs.Length)
        {
            return;
        }

        Inputs[index] = !Inputs[index];
        if (CurrentOutput == TargetOutput && EnabledInputCount == TargetEnabledCount)
        {
            Complete("Circuit output matches target.");
            return;
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        StatusText = $"Current parity is {(CurrentOutput ? "odd" : "even")} with {EnabledInputCount} live bits. Target parity is {(TargetOutput ? "odd" : "even")} with exactly {TargetEnabledCount} live bits.";
    }
}

public sealed class YarnUntanglePuzzle : RoomPuzzle
{
    private int? _selectedIndex;

    public YarnUntanglePuzzle(int[] strandOrder)
        : base('y', "Path Untangle", "Swap the strands until they run from 1 to 4 without crossing.")
    {
        StrandOrder = strandOrder;
        UpdateStatus();
    }

    public int[] StrandOrder { get; }
    public int? SelectedIndex => _selectedIndex;

    public void Select(int index)
    {
        if (IsCompleted || index < 0 || index >= StrandOrder.Length)
        {
            return;
        }

        if (_selectedIndex is null)
        {
            _selectedIndex = index;
            StatusText = "Choose a second strand to swap.";
            return;
        }

        var otherIndex = _selectedIndex.Value;
        _selectedIndex = null;
        (StrandOrder[otherIndex], StrandOrder[index]) = (StrandOrder[index], StrandOrder[otherIndex]);

        if (StrandOrder.SequenceEqual([1, 2, 3, 4]))
        {
            Complete("Path untangled.");
            return;
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        StatusText = $"Current order: {string.Join(" - ", StrandOrder)}";
    }
}

public sealed class ZoneActivationPuzzle : RoomPuzzle
{
    private double _holdSeconds;

    public ZoneActivationPuzzle(IReadOnlyList<PlayAreaRect> zones, double requiredHoldSeconds)
        : base('z', "Zone Activation", "Walk through each beacon in order.")
    {
        Zones = zones;
        RequiredHoldSeconds = requiredHoldSeconds;
    }

    public IReadOnlyList<PlayAreaRect> Zones { get; }
    public double RequiredHoldSeconds { get; }
    public int CurrentZoneIndex { get; private set; }
    public double HoldProgress => Math.Clamp(_holdSeconds / RequiredHoldSeconds, 0d, 1d);

    public override void Update(PuzzleUpdateContext context)
    {
        if (IsCompleted)
        {
            return;
        }

        var zone = Zones[CurrentZoneIndex];
        if (zone.Intersects(context.PlayerBounds))
        {
            _holdSeconds += context.DeltaTimeSeconds;
            StatusText = $"Charging beacon {CurrentZoneIndex + 1}/{Zones.Count}: {Math.Round(HoldProgress * 100d)}%";

            if (_holdSeconds >= RequiredHoldSeconds)
            {
                _holdSeconds = 0d;
                CurrentZoneIndex++;
                if (CurrentZoneIndex >= Zones.Count)
                {
                    Complete("All zones activated.");
                }
                else
                {
                    StatusText = $"Beacon {CurrentZoneIndex} locked. Move to beacon {CurrentZoneIndex + 1}.";
                }
            }
        }
        else if (_holdSeconds > 0d)
        {
            _holdSeconds = Math.Max(0d, _holdSeconds - (context.DeltaTimeSeconds * 0.8d));
        }
    }
}

public static class PuzzleFactory
{
    private sealed record PuzzleDifficultyProfile(
        double PressurePlateMinHoldSeconds,
        double PressurePlateHoldVarianceSeconds,
        double PressurePlateDecayRate,
        double QuickTimeTargetWidth,
        double QuickTimePulseSpeed,
        int QuickTimeRequiredHits,
        int SequenceLengthMin,
        int SequenceLengthVariance,
        double SequenceRevealSeconds,
        int TileCount,
        int PatternLength,
        double PatternRevealSeconds,
        int ValveCount,
        int ValveTargetCount,
        int WeightCount,
        int WeightTargetCount,
        int XorInputCount,
        int YarnCount,
        int ZoneCount,
        double ZoneHoldSeconds,
        double PlateSize);

    private static readonly (string Question, string[] Options, int CorrectIndex)[] EasyRiddles =
    [
        ("What fills a room but takes up no space?", ["Light", "Fog", "Dust", "A shadow"], 0),
        ("What gets sharper the more you use it?", ["A blade", "Your mind", "A key", "Your voice"], 1),
        ("What has many locks but no doors?", ["A keyboard", "A prison", "A vault", "A corridor"], 0),
        ("What can travel the world while staying in one corner?", ["A stamp", "A coin", "A nail", "A compass"], 0),
        ("What has hands but can never clap?", ["A clock", "A mannequin", "A glove", "A puppet"], 0),
        ("What goes up but never comes down?", ["Your age", "A lantern", "Smoke", "A kite"], 0),
        ("What can you catch but not throw?", ["A cold", "A rope", "A stone", "A key"], 0),
        ("The more you take away, the larger I become. What am I?", ["A hole", "A crater", "A tunnel", "A shadow"], 0),
        ("What begins with T, ends with T, and has T in it?", ["A teapot", "A trumpet", "A tunnel", "A tablet"], 0),
        ("What has one eye but cannot see?", ["A needle", "A storm", "A raven", "A knot"], 0),
        ("What has a neck but no head?", ["A bottle", "A bell", "A river", "A violin"], 0),
        ("What belongs to you but is used more by others?", ["Your name", "Your coat", "Your room", "Your key"], 0),
        ("What has cities but no houses, forests but no trees, and water but no fish?", ["A map", "A mirror", "A globe", "A mural"], 0),
        ("What kind of coat is always wet when you put it on?", ["Paint", "Rain", "Ice", "Mist"], 0),
        ("What has to be broken before you can use it?", ["An egg", "A seal", "A chain", "A vow"], 0),
        ("What runs but never walks?", ["Water", "A fox", "A rumor", "A clock"], 0),
        ("What gets wetter the more it dries?", ["A towel", "A sponge", "A river", "A cloak"], 0),
        ("What has many holes but still holds water?", ["A sponge", "A bucket", "A net", "A shell"], 0),
        ("What has thirteen hearts but no lungs?", ["A deck of cards", "A dragon", "A crown", "A pipe organ"], 0),
        ("What kind of room has no doors or windows?", ["A mushroom", "A cellar", "A dream", "A wardrobe"], 0),
        ("What has words but never speaks?", ["A book", "A bell", "A painting", "A road sign"], 0),
        ("What can you hold in your right hand but never in your left?", ["Your left elbow", "Your right wrist", "Your shadow", "Your breath"], 0),
        ("What kind of tree can fit in your hand?", ["A palm", "A cedar", "A pine", "A willow"], 0),
        ("What has legs but never walks?", ["A table", "A horse", "A spider", "A compass"], 0),
        ("What has teeth but cannot bite?", ["A comb", "A saw", "A gear", "A zipper"], 0),
        ("What has a thumb and four fingers but is not alive?", ["A glove", "A branch", "A statue", "A ring"], 0),
        ("What comes down but never goes up?", ["Rain", "Ash", "Snow", "Dust"], 0),
        ("What has pages but no voice?", ["A book", "A fan", "A ledger", "A map"], 0),
        ("What has a ring but no finger?", ["A telephone", "A coin", "A lantern", "A tower"], 0),
        ("What can be cracked, made, told, and played?", ["A joke", "A bell", "A lock", "A code"], 0),
        ("What has many needles but never sews?", ["A pine tree", "A compass", "A hedgehog", "A porcupine"], 0),
        ("What goes through towns and over hills but never moves?", ["A road", "A river", "The wind", "A cloud"], 0),
        ("What can answer you without ever speaking?", ["An echo", "A mirror", "A map", "A flame"], 0),
        ("What gets larger the more truth you remove from it?", ["A lie", "A rumor", "A whisper", "A mask"], 0),
        ("What is easy to lift but hard to throw far?", ["A feather", "A coin", "A book", "A stone"], 0),
    ];

    private static readonly (string Question, string[] Options, int CorrectIndex)[] MediumRiddles =
    [
        ("I speak without a mouth and hear without ears. What am I?", ["An echo", "The wind", "A drum", "A bell"], 0),
        ("The more of me you take, the more you leave behind. What am I?", ["Footsteps", "Ash", "Dust", "Coins"], 0),
        ("I have keys but no locks, space but no rooms, and you can enter but not go inside. What am I?", ["A keyboard", "A vault", "A codex", "A lantern"], 0),
        ("What disappears the moment you say its name?", ["Silence", "Fog", "A secret", "Hope"], 0),
        ("I am always in front of you but can never be seen. What am I?", ["The future", "The horizon", "Your breath", "A shadow"], 0),
        ("The more you remove from me, the bigger I get. What am I?", ["A hole", "A canyon", "A debt", "A tunnel"], 0),
        ("What can run but never tires, has a bed but never sleeps, and a mouth but never speaks?", ["A river", "A wolf", "A fire", "A clock"], 0),
        ("What is so fragile that saying its name breaks it?", ["Silence", "Glass", "Trust", "A promise"], 0),
        ("I can be long or short. I can be grown or bought. I can be painted or left bare. What am I?", ["Nails", "Hair", "Candles", "Walls"], 0),
        ("What can fill a hall with sound yet never speak a word?", ["Music", "A bell", "A trumpet", "Thunder"], 0),
        ("What has one head, one foot, and four legs?", ["A bed", "A table", "A chair", "A spider"], 0),
        ("What can you keep after giving it away?", ["Your word", "Your coin", "Your map", "Your shadow"], 0),
    ];

    private static readonly (string Question, string[] Options, int CorrectIndex)[] HardRiddles =
    [
        ("What occurs once in every minute, twice in every moment, yet never in a thousand years?", ["The letter M", "A heartbeat", "A blink", "A footstep"], 0),
        ("Feed me and I live, yet give me a drink and I die. What am I?", ["Fire", "Rust", "A battery", "A shadow"], 0),
        ("I am not alive, but I grow. I do not have lungs, but I need air. What am I?", ["Fire", "Sound", "A storm", "Ice"], 0),
        ("The person who makes it does not need it. The person who buys it does not use it. The person who uses it does not know it. What is it?", ["A coffin", "A disguise", "A crown", "A lockpick"], 0),
        ("What can be measured but has no weight, seen but no shape, and spent but never touched?", ["Time", "Light", "Noise", "Heat"], 0),
        ("I vanish when light shines on me, yet I am born because of light. What am I?", ["A shadow", "Mist", "Ash", "Smoke"], 0),
        ("The more truthful I am, the more deceptive I seem. What am I?", ["A paradox", "A rumor", "A mirror", "A riddle"], 3),
        ("What can lock a door without a key, close a mouth without a hand, and silence a room without a sound?", ["Fear", "Night", "Doubt", "Snow"], 1),
        ("What can hold meaning for centuries yet weigh less than a feather?", ["A word", "A memory", "A secret", "An echo"], 0),
        ("I can be stolen, broken, shared, and buried, yet I am never seen. What am I?", ["Trust", "A promise", "A lie", "Hope"], 1),
        ("What grows shorter as it saves you from the dark?", ["A candle", "A fuse", "A match", "A torch"], 0),
        ("What moves only because it is watched by numbers?", ["A clock", "A lock", "A cipher", "A scale"], 0),
    ];

    private static readonly string[] SequenceRunes = ["A", "B", "C", "D"];
    private static readonly PlayerDirection[] Directions = [PlayerDirection.Up, PlayerDirection.Right, PlayerDirection.Down, PlayerDirection.Left];

    public static RoomPuzzle Create(string seed, MazeRoomDefinition room, MazeDifficulty difficulty, string? runNonce = null)
    {
        if (difficulty != MazeDifficulty.Easy)
        {
            return AdvancedPuzzleFactory.Create(seed, runNonce ?? Guid.NewGuid().ToString("N"), room, difficulty);
        }

        var profile = GetProfile(difficulty);
        var hashSeed = string.IsNullOrWhiteSpace(runNonce) ? seed : $"{seed}|{runNonce}";
        var hash = StableHash($"{hashSeed}|{room.Coordinates.X}|{room.Coordinates.Y}|{room.PuzzleKey}");
        return room.PuzzleKey switch
        {
            'p' => new PressurePlatePuzzle(
                CreateRect(hash, 240d, 240d, profile.PlateSize, profile.PlateSize),
                profile.PressurePlateMinHoldSeconds + ((hash % 7) * profile.PressurePlateHoldVarianceSeconds),
                profile.PressurePlateDecayRate),
            'q' => new QuickTimePuzzle(
                0.12d + ((hash % 55) / 100d),
                profile.QuickTimeTargetWidth,
                profile.QuickTimePulseSpeed,
                profile.QuickTimeRequiredHits),
            'r' => CreateRiddle(hash, difficulty),
            's' => new SequenceMemoryPuzzle(CreateSequence(hash, profile), profile.SequenceRevealSeconds),
            't' => new TileRotationPuzzle(CreateRotations(hash, profile)),
            'u' => new UnlockPatternPuzzle(CreatePattern(hash, profile), profile.PatternRevealSeconds),
            'v' => CreateValveFlow(hash, profile),
            'w' => CreateWeightBalance(hash, profile),
            'x' => new XorLogicPuzzle((hash & 1) == 1, profile.XorInputCount, CreateXorTargetEnabledCount(hash, profile.XorInputCount, (hash & 1) == 1)),
            'y' => new YarnUntanglePuzzle(CreateUntangledSeed(hash, profile)),
            'z' => new ZoneActivationPuzzle(CreateZones(hash, profile), profile.ZoneHoldSeconds),
            _ => throw new MazeSeedParseException($"Unknown puzzle type '{room.PuzzleKey}'."),
        };
    }

    public static int StableHash(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in value)
            {
                hash ^= character;
                hash *= 16777619u;
            }

            return (int)(hash & 0x7fffffff);
        }
    }

    public static int GetPuzzleReward(string seed, MazeRoomDefinition room, MazeDifficulty difficulty)
    {
        var baseReward = 18 + (StableHash($"reward|{seed}|{room.Coordinates.X}|{room.Coordinates.Y}") % 11);
        return ApplyDifficulty(baseReward, difficulty);
    }

    public static int GetRewardPickupBonus(string seed, MazeRoomDefinition room, MazeDifficulty difficulty)
    {
        var bonus = 24 + (StableHash($"bonus|{seed}|{room.Coordinates.X}|{room.Coordinates.Y}") % 19);
        return ApplyDifficulty(bonus, difficulty);
    }

    public static PlayAreaRect CreateRewardPickupBounds(string seed, MazeRoomDefinition room)
    {
        var hash = StableHash($"reward-pickup|{seed}|{room.Coordinates.X}|{room.Coordinates.Y}");
        var x = 420d + (hash % 180);
        var y = 350d + ((hash / 13) % 240);
        return new PlayAreaRect(x, y, 88d, 88d);
    }

    public static PlayAreaRect CreateFinishPortalBounds() => new(452d, 452d, 176d, 176d);

    private static int ApplyDifficulty(int value, MazeDifficulty difficulty) => difficulty switch
    {
        MazeDifficulty.Medium => (int)Math.Round(value * 1.25d),
        MazeDifficulty.Hard => value * 2,
        _ => value,
    };

    private static PuzzleDifficultyProfile GetProfile(MazeDifficulty difficulty) => difficulty switch
    {
        MazeDifficulty.Medium => new PuzzleDifficultyProfile(
            PressurePlateMinHoldSeconds: 3.2d,
            PressurePlateHoldVarianceSeconds: 0.4d,
            PressurePlateDecayRate: 1.05d,
            QuickTimeTargetWidth: 0.082d,
            QuickTimePulseSpeed: 1.48d,
            QuickTimeRequiredHits: 3,
            SequenceLengthMin: 7,
            SequenceLengthVariance: 3,
            SequenceRevealSeconds: 2.0d,
            TileCount: 7,
            PatternLength: 7,
            PatternRevealSeconds: 1.95d,
            ValveCount: 5,
            ValveTargetCount: 3,
            WeightCount: 6,
            WeightTargetCount: 3,
            XorInputCount: 5,
            YarnCount: 5,
            ZoneCount: 4,
            ZoneHoldSeconds: 0.95d,
            PlateSize: 128d),
        MazeDifficulty.Hard => new PuzzleDifficultyProfile(
            PressurePlateMinHoldSeconds: 5.1d,
            PressurePlateHoldVarianceSeconds: 0.55d,
            PressurePlateDecayRate: 1.65d,
            QuickTimeTargetWidth: 0.048d,
            QuickTimePulseSpeed: 2.1d,
            QuickTimeRequiredHits: 4,
            SequenceLengthMin: 9,
            SequenceLengthVariance: 4,
            SequenceRevealSeconds: 0.95d,
            TileCount: 9,
            PatternLength: 9,
            PatternRevealSeconds: 0.95d,
            ValveCount: 6,
            ValveTargetCount: 4,
            WeightCount: 7,
            WeightTargetCount: 4,
            XorInputCount: 6,
            YarnCount: 7,
            ZoneCount: 5,
            ZoneHoldSeconds: 1.25d,
            PlateSize: 108d),
        _ => new PuzzleDifficultyProfile(
            PressurePlateMinHoldSeconds: 2.25d,
            PressurePlateHoldVarianceSeconds: 0.3d,
            PressurePlateDecayRate: 0.55d,
            QuickTimeTargetWidth: 0.112d,
            QuickTimePulseSpeed: 1.1d,
            QuickTimeRequiredHits: 2,
            SequenceLengthMin: 5,
            SequenceLengthVariance: 3,
            SequenceRevealSeconds: 3.0d,
            TileCount: 5,
            PatternLength: 5,
            PatternRevealSeconds: 3.0d,
            ValveCount: 4,
            ValveTargetCount: 2,
            WeightCount: 5,
            WeightTargetCount: 3,
            XorInputCount: 5,
            YarnCount: 5,
            ZoneCount: 4,
            ZoneHoldSeconds: 0.65d,
            PlateSize: 148d),
    };

    private static RiddlePuzzle CreateRiddle(int hash, MazeDifficulty difficulty)
    {
        var source = difficulty switch
        {
            MazeDifficulty.Medium => MediumRiddles,
            MazeDifficulty.Hard => HardRiddles,
            _ => EasyRiddles,
        };

        var selectionState = NextState(hash ^ 0x45f21);
        var item = source[selectionState % source.Length];
        var shuffledOptions = item.Options.ToArray();
        var correctedIndex = ShuffleOptions(shuffledOptions, item.CorrectIndex, selectionState ^ 0x2d4f13);
        return new RiddlePuzzle(item.Question, shuffledOptions, correctedIndex);
    }

    private static int ShuffleOptions(string[] options, int correctIndex, int hash)
    {
        var workingCorrectIndex = correctIndex;
        var state = hash;
        for (var index = options.Length - 1; index > 0; index--)
        {
            state = NextState(state + index);
            var swapIndex = state % (index + 1);
            (options[index], options[swapIndex]) = (options[swapIndex], options[index]);

            if (workingCorrectIndex == index)
            {
                workingCorrectIndex = swapIndex;
            }
            else if (workingCorrectIndex == swapIndex)
            {
                workingCorrectIndex = index;
            }
        }

        return workingCorrectIndex;
    }

    private static IReadOnlyList<string> CreateSequence(int hash, PuzzleDifficultyProfile profile)
    {
        var length = profile.SequenceLengthMin + (hash % Math.Max(1, profile.SequenceLengthVariance));
        var runes = new List<string>(length);
        var state = hash;
        string? previous = null;
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        var minimumDistinct = Math.Min(SequenceRunes.Length, length >= 7 ? 4 : 3);
        for (var index = 0; index < length; index++)
        {
            state = NextState(state + index + distinct.Count);
            var candidates = SequenceRunes.Where(rune => !string.Equals(rune, previous, StringComparison.Ordinal)).ToArray();
            if ((length - index) <= (minimumDistinct - distinct.Count))
            {
                var unseen = candidates.Where(rune => !distinct.Contains(rune)).ToArray();
                if (unseen.Length > 0)
                {
                    candidates = unseen;
                }
            }

            var selected = candidates[state % candidates.Length];
            runes.Add(selected);
            distinct.Add(selected);
            previous = selected;
        }

        return runes;
    }

    private static int[] CreateRotations(int hash, PuzzleDifficultyProfile profile)
    {
        var rotations = new int[profile.TileCount];
        var state = hash;
        for (var index = 0; index < rotations.Length; index++)
        {
            state = NextState(state + index);
            rotations[index] = (state % 3) + 1;
        }

        if (rotations.All(rotation => rotation == 0))
        {
            rotations[0] = 1;
        }

        return rotations;
    }

    private static IReadOnlyList<PlayerDirection> CreatePattern(int hash, PuzzleDifficultyProfile profile)
    {
        var pattern = new List<PlayerDirection>(profile.PatternLength);
        var state = hash;
        PlayerDirection? previous = null;
        var distinct = new HashSet<PlayerDirection>();
        var minimumDistinct = Math.Min(Directions.Length, profile.PatternLength >= 7 ? 4 : 3);
        for (var index = 0; index < profile.PatternLength; index++)
        {
            state = NextState(state + (index * 5) + distinct.Count);
            var candidates = Directions.Where(direction => previous is null || direction != previous.Value).ToArray();
            if ((profile.PatternLength - index) <= (minimumDistinct - distinct.Count))
            {
                var unseen = candidates.Where(direction => !distinct.Contains(direction)).ToArray();
                if (unseen.Length > 0)
                {
                    candidates = unseen;
                }
            }

            var selected = candidates[state % candidates.Length];
            pattern.Add(selected);
            distinct.Add(selected);
            previous = selected;
        }

        return pattern;
    }

    private static ValveFlowPuzzle CreateValveFlow(int hash, PuzzleDifficultyProfile profile)
    {
        var (valves, target) = CreateUniqueSubsetChallenge(hash, profile.ValveCount, 14, 52, profile.ValveTargetCount);
        return new ValveFlowPuzzle(valves, target, profile.ValveTargetCount);
    }

    private static WeightBalancePuzzle CreateWeightBalance(int hash, PuzzleDifficultyProfile profile)
    {
        var (weights, target) = CreateUniqueSubsetChallenge(hash ^ 0x55aa55, profile.WeightCount, 2, 21, profile.WeightTargetCount);
        return new WeightBalancePuzzle(weights, target, profile.WeightTargetCount);
    }

    private static int[] CreateUntangledSeed(int hash, PuzzleDifficultyProfile profile)
    {
        var result = Enumerable.Range(1, profile.YarnCount).ToArray();
        var state = hash;
        for (var index = 0; index < result.Length; index++)
        {
            state = NextState(state + (index * 7));
            var swapIndex = state % result.Length;
            (result[index], result[swapIndex]) = (result[swapIndex], result[index]);
        }

        if (result.SequenceEqual(Enumerable.Range(1, profile.YarnCount)))
        {
            (result[0], result[1]) = (result[1], result[0]);
        }

        return result;
    }

    private static IReadOnlyList<PlayAreaRect> CreateZones(int hash, PuzzleDifficultyProfile profile)
    {
        var zones = new List<PlayAreaRect>(profile.ZoneCount);
        var state = hash;
        var zoneSize = profile switch
        {
            { ZoneCount: >= 5 } => 102d,
            { ZoneCount: 4 } => 112d,
            _ => 120d,
        };

        for (var index = 0; index < profile.ZoneCount; index++)
        {
            state = NextState(state + index);
            var angle = ((Math.PI * 2d) / profile.ZoneCount) * index;
            var radius = 270d + (state % 120);
            var x = 420d + (Math.Cos(angle) * radius) + ((state % 45) - 22);
            var y = 420d + (Math.Sin(angle) * radius) + (((state / 7) % 45) - 22);
            x = Math.Clamp(x, 90d, 1080d - zoneSize - 90d);
            y = Math.Clamp(y, 90d, 1080d - zoneSize - 90d);
            zones.Add(new PlayAreaRect(x, y, zoneSize, zoneSize));
        }

        return zones;
    }

    private static int[] CreateValueSeries(int hash, int count, int minValue, int maxValue)
    {
        var values = new int[count];
        var state = hash;
        var range = maxValue - minValue + 1;
        var used = new HashSet<int>();

        for (var index = 0; index < count; index++)
        {
            state = NextState(state + (index * 13));
            var value = minValue + (state % range);
            while (!used.Add(value))
            {
                value++;
                if (value > maxValue)
                {
                    value = minValue;
                }
            }

            values[index] = value;
        }

        return values;
    }

    private static (int[] Values, int Target) CreateUniqueSubsetChallenge(int hash, int count, int minValue, int maxValue, int requiredCount)
    {
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var attemptHash = hash + (attempt * 977);
            var values = CreateValueSeries(attemptHash, count, minValue, maxValue);
            var target = CreateTargetValue(values, attemptHash ^ 0x19af31, requiredCount);
            if (CountMatchingSubsets(values, target, requiredCount) == 1)
            {
                return (ShuffleCopy(values, attemptHash ^ 0x4ac2d1), target);
            }
        }

        var fallbackValues = ShuffleCopy(CreateValueSeries(hash, count, minValue, maxValue), hash ^ 0x1133aa);
        return (fallbackValues, CreateTargetValue(fallbackValues, hash ^ 0x19af31, requiredCount));
    }

    private static int CountMatchingSubsets(int[] values, int target, int requiredCount)
    {
        var matches = 0;
        var limit = 1 << values.Length;
        for (var mask = 0; mask < limit; mask++)
        {
            if (CountBits(mask) != requiredCount)
            {
                continue;
            }

            var sum = 0;
            for (var index = 0; index < values.Length; index++)
            {
                if (((mask >> index) & 1) == 1)
                {
                    sum += values[index];
                }
            }

            if (sum == target)
            {
                matches++;
                if (matches > 1)
                {
                    break;
                }
            }
        }

        return matches;
    }

    private static int[] ShuffleCopy(int[] values, int hash)
    {
        var copy = values.ToArray();
        var state = hash;
        for (var index = copy.Length - 1; index > 0; index--)
        {
            state = NextState(state + index);
            var swapIndex = state % (index + 1);
            (copy[index], copy[swapIndex]) = (copy[swapIndex], copy[index]);
        }

        return copy;
    }

    private static int CountBits(int value)
    {
        var count = 0;
        var working = value;
        while (working != 0)
        {
            count += working & 1;
            working >>= 1;
        }

        return count;
    }

    private static int CreateTargetValue(int[] values, int hash, int picks)
    {
        var target = 0;
        var chosen = new HashSet<int>();
        var state = hash;

        while (chosen.Count < Math.Min(picks, values.Length))
        {
            state = NextState(state + chosen.Count);
            chosen.Add(state % values.Length);
        }

        foreach (var index in chosen)
        {
            target += values[index];
        }

        return target;
    }

    private static int CreateXorTargetEnabledCount(int hash, int inputCount, bool targetOutput)
    {
        var minimum = targetOutput ? 1 : 2;
        var candidates = Enumerable.Range(minimum, Math.Max(1, inputCount - minimum + 1))
            .Where(count => (count % 2 == 1) == targetOutput)
            .ToArray();

        return candidates.Length == 0 ? minimum : candidates[(hash / 31) % candidates.Length];
    }

    private static int NextState(int seed)
    {
        unchecked
        {
            return (int)(((uint)seed * 1103515245u + 12345u) & 0x7fffffff);
        }
    }

    private static PlayAreaRect CreateRect(int hash, double minX, double minY, double width, double height)
    {
        var x = minX + (hash % 480);
        var y = minY + ((hash / 17) % 420);
        return new PlayAreaRect(x, y, width, height);
    }
}
