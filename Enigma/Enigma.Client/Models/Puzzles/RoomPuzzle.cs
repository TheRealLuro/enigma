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
            StatusText = $"Plate charge: {Math.Round(Progress * 100d)}%";
        }
        else if (HeldSeconds > 0d)
        {
            HeldSeconds = Math.Max(0d, HeldSeconds - (context.DeltaTimeSeconds * DecayRate));
            StatusText = $"Plate charge: {Math.Round(Progress * 100d)}%";
        }

        if (HeldSeconds >= RequiredHoldSeconds)
        {
            Complete("Pressure plate secured.");
        }
    }
}

public sealed class QuickTimePuzzle : RoomPuzzle
{
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
        if (MeterValue >= TargetStart && MeterValue <= targetEnd)
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
        : base('u', "Unlock Pattern", "Memorize the directional sequence before it disappears, then tap it back exactly.")
    {
        Pattern = pattern;
        RevealDurationSeconds = revealDurationSeconds;
    }

    public IReadOnlyList<PlayerDirection> Pattern { get; }
    public IReadOnlyList<PlayerDirection> Entered => _entered;
    public double RevealDurationSeconds { get; }
    public double ElapsedSeconds { get; private set; }
    public bool IsPatternVisible => IsCompleted || ElapsedSeconds < RevealDurationSeconds;

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
            StatusText = "The pattern vanishes. Repeat it from memory.";
        }
    }

    public void Press(PlayerDirection direction)
    {
        if (IsCompleted)
        {
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
    public ValveFlowPuzzle(int[] valveValues, int targetFlow)
        : base('v', "Valve Flow", "Open the correct valves to hit the target flow exactly.")
    {
        ValveValues = valveValues;
        TargetFlow = targetFlow;
        OpenStates = new bool[valveValues.Length];
        UpdateStatus();
    }

    public int[] ValveValues { get; }
    public bool[] OpenStates { get; }
    public int TargetFlow { get; }
    public int CurrentFlow => ValveValues.Where((value, index) => OpenStates[index]).Sum();

    public void Toggle(int index)
    {
        if (IsCompleted || index < 0 || index >= OpenStates.Length)
        {
            return;
        }

        OpenStates[index] = !OpenStates[index];
        if (CurrentFlow == TargetFlow)
        {
            Complete("Flow stabilized.");
            return;
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        StatusText = $"Current flow: {CurrentFlow} / {TargetFlow}";
    }
}

public sealed class WeightBalancePuzzle : RoomPuzzle
{
    public WeightBalancePuzzle(int[] weights, int targetWeight)
        : base('w', "Weight Balance", "Load the platform until the total matches the target.")
    {
        Weights = weights;
        TargetWeight = targetWeight;
        Selected = new bool[weights.Length];
        UpdateStatus();
    }

    public int[] Weights { get; }
    public bool[] Selected { get; }
    public int TargetWeight { get; }
    public int CurrentWeight => Weights.Where((value, index) => Selected[index]).Sum();

    public void Toggle(int index)
    {
        if (IsCompleted || index < 0 || index >= Selected.Length)
        {
            return;
        }

        Selected[index] = !Selected[index];
        if (CurrentWeight == TargetWeight)
        {
            Complete("Balance restored.");
            return;
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        StatusText = $"Current load: {CurrentWeight} / {TargetWeight}";
    }
}

public sealed class XorLogicPuzzle : RoomPuzzle
{
    public XorLogicPuzzle(bool targetOutput, int inputCount)
        : base('x', "XOR Logic", "Toggle the inputs until the parity matches the target output.")
    {
        TargetOutput = targetOutput;
        Inputs = new bool[Math.Max(3, inputCount)];
        UpdateStatus();
    }

    public bool[] Inputs { get; }
    public bool TargetOutput { get; }
    public bool CurrentOutput => Inputs.Aggregate(false, (state, input) => state ^ input);

    public void Toggle(int index)
    {
        if (IsCompleted || index < 0 || index >= Inputs.Length)
        {
            return;
        }

        Inputs[index] = !Inputs[index];
        if (CurrentOutput == TargetOutput)
        {
            Complete("Circuit output matches target.");
            return;
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        StatusText = $"Output: {(CurrentOutput ? 1 : 0)} / {(TargetOutput ? 1 : 0)}";
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
        ("What fills a room but takes no space?", ["Light", "Stone", "Water"], 0),
        ("What gets sharper the more you use it?", ["A key", "Your mind", "A shadow"], 1),
        ("What has many locks but no doors?", ["A keyboard", "A prison", "A vault"], 0),
        ("What can travel the world while staying in one corner?", ["A stamp", "A map", "A coin"], 0),
    ];

    private static readonly (string Question, string[] Options, int CorrectIndex)[] MediumRiddles =
    [
        ("I speak without a mouth and hear without ears. What am I?", ["Echo", "Wind", "A radio", "A drum"], 0),
        ("The more of me you take, the more you leave behind. What am I?", ["Footsteps", "Smoke", "Dust", "Coins"], 0),
        ("I have keys but no locks, space but no rooms, and you can enter but not go inside. What am I?", ["Keyboard", "Vault", "Map", "Lantern"], 0),
        ("What disappears the moment you say its name?", ["Silence", "Fog", "A secret", "Hope"], 0),
    ];

    private static readonly (string Question, string[] Options, int CorrectIndex)[] HardRiddles =
    [
        ("What occurs once in every minute, twice in every moment, yet never in a thousand years?", ["The letter M", "A bell", "A breath", "A blink"], 0),
        ("Feed me and I live, yet give me a drink and I die. What am I?", ["Fire", "Rust", "A battery", "A shadow"], 0),
        ("I am not alive, but I grow. I do not have lungs, but I need air. What am I?", ["Fire", "Sound", "A storm", "Ice"], 0),
        ("The person who makes it does not need it. The person who buys it does not use it. The person who uses it does not know it. What is it?", ["A coffin", "A lockpick", "A disguise", "A crown"], 0),
    ];

    private static readonly string[] SequenceRunes = ["A", "B", "C", "D"];
    private static readonly PlayerDirection[] Directions = [PlayerDirection.Up, PlayerDirection.Right, PlayerDirection.Down, PlayerDirection.Left];

    public static RoomPuzzle Create(string seed, MazeRoomDefinition room, MazeDifficulty difficulty)
    {
        var profile = GetProfile(difficulty);
        var hash = StableHash($"{seed}|{room.Coordinates.X}|{room.Coordinates.Y}|{room.PuzzleKey}");
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
            'x' => new XorLogicPuzzle((hash & 1) == 1, profile.XorInputCount),
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
            PressurePlateMinHoldSeconds: 2.8d,
            PressurePlateHoldVarianceSeconds: 0.35d,
            PressurePlateDecayRate: 0.85d,
            QuickTimeTargetWidth: 0.09d,
            QuickTimePulseSpeed: 1.35d,
            QuickTimeRequiredHits: 2,
            SequenceLengthMin: 6,
            SequenceLengthVariance: 2,
            SequenceRevealSeconds: 2.2d,
            TileCount: 6,
            PatternLength: 6,
            PatternRevealSeconds: 2.2d,
            ValveCount: 4,
            ValveTargetCount: 3,
            WeightCount: 5,
            WeightTargetCount: 3,
            XorInputCount: 4,
            YarnCount: 5,
            ZoneCount: 4,
            ZoneHoldSeconds: 0.8d,
            PlateSize: 136d),
        MazeDifficulty.Hard => new PuzzleDifficultyProfile(
            PressurePlateMinHoldSeconds: 4.5d,
            PressurePlateHoldVarianceSeconds: 0.45d,
            PressurePlateDecayRate: 1.4d,
            QuickTimeTargetWidth: 0.055d,
            QuickTimePulseSpeed: 1.95d,
            QuickTimeRequiredHits: 3,
            SequenceLengthMin: 8,
            SequenceLengthVariance: 3,
            SequenceRevealSeconds: 1.15d,
            TileCount: 8,
            PatternLength: 8,
            PatternRevealSeconds: 1.15d,
            ValveCount: 5,
            ValveTargetCount: 3,
            WeightCount: 6,
            WeightTargetCount: 4,
            XorInputCount: 5,
            YarnCount: 6,
            ZoneCount: 5,
            ZoneHoldSeconds: 1.1d,
            PlateSize: 118d),
        _ => new PuzzleDifficultyProfile(
            PressurePlateMinHoldSeconds: 1.8d,
            PressurePlateHoldVarianceSeconds: 0.25d,
            PressurePlateDecayRate: 0.35d,
            QuickTimeTargetWidth: 0.13d,
            QuickTimePulseSpeed: 0.95d,
            QuickTimeRequiredHits: 1,
            SequenceLengthMin: 4,
            SequenceLengthVariance: 2,
            SequenceRevealSeconds: 3.5d,
            TileCount: 4,
            PatternLength: 4,
            PatternRevealSeconds: 3.5d,
            ValveCount: 3,
            ValveTargetCount: 2,
            WeightCount: 4,
            WeightTargetCount: 2,
            XorInputCount: 3,
            YarnCount: 4,
            ZoneCount: 3,
            ZoneHoldSeconds: 0.5d,
            PlateSize: 160d),
    };

    private static RiddlePuzzle CreateRiddle(int hash, MazeDifficulty difficulty)
    {
        var source = difficulty switch
        {
            MazeDifficulty.Medium => MediumRiddles,
            MazeDifficulty.Hard => HardRiddles,
            _ => EasyRiddles,
        };

        var item = source[hash % source.Length];
        return new RiddlePuzzle(item.Question, item.Options, item.CorrectIndex);
    }

    private static IReadOnlyList<string> CreateSequence(int hash, PuzzleDifficultyProfile profile)
    {
        var length = profile.SequenceLengthMin + (hash % profile.SequenceLengthVariance);
        var runes = new List<string>(length);
        var state = hash;
        for (var index = 0; index < length; index++)
        {
            state = NextState(state + index);
            runes.Add(SequenceRunes[state % SequenceRunes.Length]);
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
        for (var index = 0; index < profile.PatternLength; index++)
        {
            state = NextState(state + (index * 5));
            pattern.Add(Directions[state % Directions.Length]);
        }

        return pattern;
    }

    private static ValveFlowPuzzle CreateValveFlow(int hash, PuzzleDifficultyProfile profile)
    {
        var valves = CreateValueSeries(hash, profile.ValveCount, 14, 48);
        var target = CreateTargetValue(valves, hash, profile.ValveTargetCount);
        return new ValveFlowPuzzle(valves, target);
    }

    private static WeightBalancePuzzle CreateWeightBalance(int hash, PuzzleDifficultyProfile profile)
    {
        var weights = CreateValueSeries(hash, profile.WeightCount, 2, 18);
        var target = CreateTargetValue(weights, hash ^ 0x55aa55, profile.WeightTargetCount);
        return new WeightBalancePuzzle(weights, target);
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

        for (var index = 0; index < count; index++)
        {
            state = NextState(state + (index * 13));
            values[index] = minValue + (state % range);
        }

        return values;
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
