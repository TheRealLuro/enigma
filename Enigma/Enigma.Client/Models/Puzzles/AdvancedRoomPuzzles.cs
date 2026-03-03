namespace Enigma.Client.Models.Gameplay;

public readonly record struct PlayAreaPoint(double X, double Y);

public interface IBlockPushPuzzle
{
    bool BlocksPlayer(PlayAreaRect playerBounds);
    bool TryPush(PlayerDirection direction, PlayAreaRect playerBounds);
}

public sealed class PhaseRelayPuzzle : RoomPuzzle
{
    private int? _currentTouchedNode;

    public PhaseRelayPuzzle(IReadOnlyList<PlayAreaRect> nodes, IReadOnlyList<int> sequence, double glowSeconds)
        : base('p', "Phase Relay Puzzle", "Trace the relay in order. The active node only glows briefly before the room goes dark.")
    {
        Nodes = nodes;
        Sequence = sequence;
        GlowSeconds = glowSeconds;
        GlowRemainingSeconds = glowSeconds;
        UpdateStatus();
    }

    public IReadOnlyList<PlayAreaRect> Nodes { get; }
    public IReadOnlyList<int> Sequence { get; }
    public double GlowSeconds { get; }
    public double GlowRemainingSeconds { get; private set; }
    public int CurrentStepIndex { get; private set; }
    public int ActiveNodeIndex => Sequence[Math.Min(CurrentStepIndex, Sequence.Count - 1)];
    public bool IsCurrentNodeVisible => GlowRemainingSeconds > 0d;

    public override void Update(PuzzleUpdateContext context)
    {
        if (IsCompleted)
        {
            return;
        }

        GlowRemainingSeconds = Math.Max(0d, GlowRemainingSeconds - context.DeltaTimeSeconds);
        var touchedNode = GetTouchedNode(context.PlayerBounds);
        if (touchedNode == _currentTouchedNode)
        {
            return;
        }

        _currentTouchedNode = touchedNode;
        if (touchedNode is null)
        {
            UpdateStatus();
            return;
        }

        if (touchedNode.Value == ActiveNodeIndex)
        {
            CurrentStepIndex++;
            if (CurrentStepIndex >= Sequence.Count)
            {
                Complete("The relay chain locks into phase.");
                return;
            }

            GlowRemainingSeconds = GlowSeconds;
            UpdateStatus();
            return;
        }

        CurrentStepIndex = 0;
        GlowRemainingSeconds = GlowSeconds;
        StatusText = "Wrong relay. The chain collapses back to phase one.";
    }

    private int? GetTouchedNode(PlayAreaRect playerBounds)
    {
        for (var index = 0; index < Nodes.Count; index++)
        {
            if (Nodes[index].Intersects(playerBounds))
            {
                return index;
            }
        }

        return null;
    }

    private void UpdateStatus()
    {
        StatusText = IsCurrentNodeVisible
            ? $"Relay {CurrentStepIndex + 1}/{Sequence.Count} is glowing. Memorize it before it fades."
            : $"Find relay {CurrentStepIndex + 1}/{Sequence.Count} from memory.";
    }
}

public sealed class DualPulseLockPuzzle : RoomPuzzle
{
    private int _directionA = 1;
    private int _directionB = -1;

    public DualPulseLockPuzzle(double targetStartA, double targetWidthA, double targetStartB, double targetWidthB, double speedA, double speedB)
        : base('q', "Dual Pulse Lock", "Anchor both pulses inside their target windows at the same time. You can stop each pulse independently.")
    {
        TargetStartA = targetStartA;
        TargetWidthA = targetWidthA;
        TargetStartB = targetStartB;
        TargetWidthB = targetWidthB;
        SpeedA = speedA;
        SpeedB = speedB;
        MeterA = 0.16d;
        MeterB = 0.82d;
        UpdateStatus();
    }

    public double MeterA { get; private set; }
    public double MeterB { get; private set; }
    public double TargetStartA { get; }
    public double TargetWidthA { get; }
    public double TargetStartB { get; }
    public double TargetWidthB { get; }
    public double SpeedA { get; }
    public double SpeedB { get; }
    public bool IsStoppedA { get; private set; }
    public bool IsStoppedB { get; private set; }
    public bool MeterAInWindow => MeterA >= TargetStartA && MeterA <= TargetStartA + TargetWidthA;
    public bool MeterBInWindow => MeterB >= TargetStartB && MeterB <= TargetStartB + TargetWidthB;

    public override void Update(PuzzleUpdateContext context)
    {
        if (IsCompleted)
        {
            return;
        }

        if (!IsStoppedA)
        {
            MeterA = AdvanceMeter(MeterA, SpeedA, ref _directionA, context.DeltaTimeSeconds);
        }

        if (!IsStoppedB)
        {
            MeterB = AdvanceMeter(MeterB, SpeedB, ref _directionB, context.DeltaTimeSeconds);
        }

        if (!IsStoppedA || !IsStoppedB)
        {
            UpdateStatus();
        }
    }

    public void ToggleMeterA() => ToggleMeter(isFirst: true);

    public void ToggleMeterB() => ToggleMeter(isFirst: false);

    private void ToggleMeter(bool isFirst)
    {
        if (IsCompleted)
        {
            return;
        }

        if (isFirst)
        {
            IsStoppedA = !IsStoppedA;
        }
        else
        {
            IsStoppedB = !IsStoppedB;
        }

        if (IsStoppedA && IsStoppedB)
        {
            if (MeterAInWindow && MeterBInWindow)
            {
                Complete("Both pulses lock in sync.");
                return;
            }

            IsStoppedA = false;
            IsStoppedB = false;
            StatusText = "The pulses fell out of sync. Both relays restart.";
            return;
        }

        UpdateStatus();
    }

    private static double AdvanceMeter(double value, double speed, ref int direction, double deltaSeconds)
    {
        value += direction * speed * deltaSeconds;
        if (value >= 1d)
        {
            value = 1d;
            direction = -1;
        }
        else if (value <= 0d)
        {
            value = 0d;
            direction = 1;
        }

        return value;
    }

    private void UpdateStatus()
    {
        if (IsStoppedA && MeterAInWindow)
        {
            StatusText = "Pulse A is anchored. Stop pulse B inside its window.";
            return;
        }

        if (IsStoppedB && MeterBInWindow)
        {
            StatusText = "Pulse B is anchored. Stop pulse A inside its window.";
            return;
        }

        StatusText = "Trap both moving pulses inside the bright windows.";
    }
}

public sealed class SymbolLogicPuzzle : RoomPuzzle
{
    private readonly List<string> _entered = [];

    public SymbolLogicPuzzle(IReadOnlyList<string> symbols, IReadOnlyList<string> statements, IReadOnlyList<string> solution)
        : base('r', "Symbol Logic Riddle", "Arrange the sigils into the only order that satisfies every statement.")
    {
        Symbols = symbols;
        Statements = statements;
        Solution = solution;
        UpdateStatus();
    }

    public IReadOnlyList<string> Symbols { get; }
    public IReadOnlyList<string> Statements { get; }
    public IReadOnlyList<string> Solution { get; }
    public IReadOnlyList<string> Entered => _entered;

    public void SelectSymbol(string symbol)
    {
        if (IsCompleted || _entered.Contains(symbol, StringComparer.Ordinal))
        {
            return;
        }

        _entered.Add(symbol);
        if (_entered.Count == Solution.Count)
        {
            if (_entered.SequenceEqual(Solution, StringComparer.Ordinal))
            {
                Complete("The sigils settle into the correct logic chain.");
                return;
            }

            _entered.Clear();
            StatusText = "That ordering breaks the logic. The sigils scatter and reset.";
            return;
        }

        UpdateStatus();
    }

    public void ClearSelection()
    {
        if (IsCompleted)
        {
            return;
        }

        _entered.Clear();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        StatusText = _entered.Count == 0
            ? "Build the order from left to right."
            : $"Current order: {string.Join(" ", _entered)}";
    }
}

public sealed class FadingPathMemoryPuzzle : RoomPuzzle
{
    private GridPoint? _lastVisitedCell;

    public FadingPathMemoryPuzzle(int gridSize, double boardOriginX, double boardOriginY, double cellSize, IReadOnlyList<GridPoint> path, double revealDurationSeconds)
        : base('s', "Fading Path Memory", "Watch the luminous path, then walk the exact route after it fades.")
    {
        GridSize = gridSize;
        BoardOriginX = boardOriginX;
        BoardOriginY = boardOriginY;
        CellSize = cellSize;
        Path = path;
        RevealDurationSeconds = revealDurationSeconds;
    }

    public int GridSize { get; }
    public double BoardOriginX { get; }
    public double BoardOriginY { get; }
    public double CellSize { get; }
    public IReadOnlyList<GridPoint> Path { get; }
    public double RevealDurationSeconds { get; }
    public double ElapsedSeconds { get; private set; }
    public int ProgressIndex { get; private set; }
    public bool IsPathVisible => IsCompleted || ElapsedSeconds < RevealDurationSeconds;

    public override void Update(PuzzleUpdateContext context)
    {
        if (IsCompleted)
        {
            return;
        }

        ElapsedSeconds += context.DeltaTimeSeconds;
        var currentCell = GetCellAt(context.PlayerBounds.CenterX, context.PlayerBounds.CenterY);
        if (currentCell is null || currentCell == _lastVisitedCell)
        {
            return;
        }

        _lastVisitedCell = currentCell;
        if (IsPathVisible)
        {
            return;
        }

        if (ProgressIndex == 0)
        {
            if (currentCell.Value == Path[0])
            {
                ProgressIndex = 1;
                StatusText = $"Path traced: {ProgressIndex}/{Path.Count}";
                if (ProgressIndex >= Path.Count)
                {
                    Complete("You retraced the hidden path.");
                }
            }

            return;
        }

        if (currentCell.Value == Path[ProgressIndex])
        {
            ProgressIndex++;
            StatusText = $"Path traced: {ProgressIndex}/{Path.Count}";
            if (ProgressIndex >= Path.Count)
            {
                Complete("You retraced the hidden path.");
            }

            return;
        }

        if (currentCell.Value != Path[ProgressIndex - 1])
        {
            ProgressIndex = 0;
            StatusText = "You stepped off the memory trail. The path resets.";
        }
    }

    public PlayAreaRect GetCellRect(GridPoint cell) => new(BoardOriginX + (cell.X * CellSize), BoardOriginY + (cell.Y * CellSize), CellSize, CellSize);

    private GridPoint? GetCellAt(double x, double y)
    {
        var localX = x - BoardOriginX;
        var localY = y - BoardOriginY;
        if (localX < 0d || localY < 0d)
        {
            return null;
        }

        var cellX = (int)(localX / CellSize);
        var cellY = (int)(localY / CellSize);
        if (cellX < 0 || cellX >= GridSize || cellY < 0 || cellY >= GridSize)
        {
            return null;
        }

        return new GridPoint(cellX, cellY);
    }
}

public sealed class SignalRotationNetworkPuzzle : RoomPuzzle
{
    public SignalRotationNetworkPuzzle(int gridSize, int[] baseMasks, int[] rotations, int startIndex, int endIndex)
        : base('t', "Signal Rotation Network", "Rotate the relay tiles until a live circuit connects the start node to the end node.")
    {
        GridSize = gridSize;
        BaseMasks = baseMasks;
        Rotations = rotations;
        StartIndex = startIndex;
        EndIndex = endIndex;
        UpdateStatus();
    }

    public int GridSize { get; }
    public int[] BaseMasks { get; }
    public int[] Rotations { get; }
    public int StartIndex { get; }
    public int EndIndex { get; }

    public int GetMask(int index) => RotateMask(BaseMasks[index], Rotations[index]);

    public void Rotate(int index)
    {
        if (IsCompleted || index < 0 || index >= Rotations.Length)
        {
            return;
        }

        Rotations[index] = (Rotations[index] + 1) % 4;
        if (HasLivePath())
        {
            Complete("The circuit hums end to end.");
            return;
        }

        UpdateStatus();
    }

    public bool HasLivePath()
    {
        var visited = new HashSet<int>();
        var queue = new Queue<int>();
        queue.Enqueue(StartIndex);
        visited.Add(StartIndex);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == EndIndex)
            {
                return true;
            }

            var mask = GetMask(current);
            foreach (var (neighbor, bit, oppositeBit) in GetNeighbors(current))
            {
                if ((mask & bit) == 0)
                {
                    continue;
                }

                if ((GetMask(neighbor) & oppositeBit) == 0 || !visited.Add(neighbor))
                {
                    continue;
                }

                queue.Enqueue(neighbor);
            }
        }

        return false;
    }

    private IEnumerable<(int Neighbor, int Bit, int OppositeBit)> GetNeighbors(int index)
    {
        var x = index % GridSize;
        var y = index / GridSize;
        if (y > 0)
        {
            yield return (index - GridSize, 1, 4);
        }

        if (x < GridSize - 1)
        {
            yield return (index + 1, 2, 8);
        }

        if (y < GridSize - 1)
        {
            yield return (index + GridSize, 4, 1);
        }

        if (x > 0)
        {
            yield return (index - 1, 8, 2);
        }
    }

    private static int RotateMask(int mask, int turns)
    {
        var current = mask;
        for (var step = 0; step < turns; step++)
        {
            current = ((current << 1) & 0b1111) | ((current >> 3) & 0b0001);
        }

        return current;
    }

    private void UpdateStatus()
    {
        var connected = CountReachableTiles();
        StatusText = $"Live tiles connected: {connected}/{BaseMasks.Length}";
    }

    private int CountReachableTiles()
    {
        var visited = new HashSet<int>();
        var queue = new Queue<int>();
        queue.Enqueue(StartIndex);
        visited.Add(StartIndex);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var mask = GetMask(current);
            foreach (var (neighbor, bit, oppositeBit) in GetNeighbors(current))
            {
                if ((mask & bit) == 0 || (GetMask(neighbor) & oppositeBit) == 0 || !visited.Add(neighbor))
                {
                    continue;
                }

                queue.Enqueue(neighbor);
            }
        }

        return visited.Count;
    }
}

public enum DirectionTransformKind
{
    Reverse,
    RotateClockwise,
    RotateCounterClockwise,
    MirrorHorizontal,
    RotateHalfTurn,
}

public sealed class DirectionalEchoPuzzle : RoomPuzzle
{
    private readonly List<PlayerDirection> _entered = [];

    public DirectionalEchoPuzzle(IReadOnlyList<PlayerDirection> pattern, IReadOnlyList<PlayerDirection> solution, DirectionTransformKind transformKind, double revealDurationSeconds)
        : base('u', "Directional Echo Puzzle", "The echo does not repeat what it hears. Apply the transformation rule before you answer.")
    {
        Pattern = pattern;
        Solution = solution;
        TransformKind = transformKind;
        RevealDurationSeconds = revealDurationSeconds;
    }

    public IReadOnlyList<PlayerDirection> Pattern { get; }
    public IReadOnlyList<PlayerDirection> Solution { get; }
    public DirectionTransformKind TransformKind { get; }
    public IReadOnlyList<PlayerDirection> Entered => _entered;
    public double RevealDurationSeconds { get; }
    public double ElapsedSeconds { get; private set; }
    public bool IsPatternVisible => IsCompleted || ElapsedSeconds < RevealDurationSeconds;
    public string RuleDescription => TransformKind switch
    {
        DirectionTransformKind.Reverse => "Answer the pattern in reverse.",
        DirectionTransformKind.RotateClockwise => "Rotate every direction 90 degrees clockwise.",
        DirectionTransformKind.RotateCounterClockwise => "Rotate every direction 90 degrees counterclockwise.",
        DirectionTransformKind.MirrorHorizontal => "Mirror the pattern across the vertical axis.",
        DirectionTransformKind.RotateHalfTurn => "Invert the pattern across a half turn.",
        _ => "Transform the pattern before you answer.",
    };

    public override void Update(PuzzleUpdateContext context)
    {
        if (IsCompleted || !IsPatternVisible)
        {
            return;
        }

        ElapsedSeconds += context.DeltaTimeSeconds;
        if (!IsPatternVisible)
        {
            StatusText = "The echo fades. Answer using the transformation rule.";
        }
    }

    public void Press(PlayerDirection direction)
    {
        if (IsCompleted)
        {
            return;
        }

        var expected = Solution[_entered.Count];
        if (expected != direction)
        {
            _entered.Clear();
            StatusText = "The echo rejects that input. Start the transformed pattern again.";
            return;
        }

        _entered.Add(direction);
        StatusText = $"Echo decoded: {_entered.Count}/{Solution.Count}";
        if (_entered.Count >= Solution.Count)
        {
            Complete("The transformed echo is accepted.");
        }
    }
}

public sealed class FlowRedistributionPuzzle : RoomPuzzle
{
    public FlowRedistributionPuzzle(int[] outputs, IReadOnlyList<int[]> valveAdjustments)
        : base('v', "Flow Redistribution", "Pulse the valves until every output carries the same pressure.")
    {
        Outputs = outputs;
        ValveAdjustments = valveAdjustments;
        TargetFlow = outputs.Sum() / outputs.Length;
        UpdateStatus();
    }

    public int[] Outputs { get; }
    public IReadOnlyList<int[]> ValveAdjustments { get; }
    public int TargetFlow { get; }
    public int MoveCount { get; private set; }

    public void Pulse(int index)
    {
        if (IsCompleted || index < 0 || index >= ValveAdjustments.Count)
        {
            return;
        }

        var delta = ValveAdjustments[index];
        var preview = new int[Outputs.Length];
        for (var outputIndex = 0; outputIndex < Outputs.Length; outputIndex++)
        {
            preview[outputIndex] = Outputs[outputIndex] + delta[outputIndex];
            if (preview[outputIndex] < 0)
            {
                StatusText = "That pulse would starve one channel. Try another valve.";
                return;
            }
        }

        for (var outputIndex = 0; outputIndex < Outputs.Length; outputIndex++)
        {
            Outputs[outputIndex] = preview[outputIndex];
        }

        MoveCount++;
        if (Outputs.All(value => value == TargetFlow))
        {
            Complete("The pressure equalizes across every output.");
            return;
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        StatusText = $"Outputs: {string.Join(" / ", Outputs)}. Equalize them at {TargetFlow}.";
    }
}

public sealed class WeightedBlockState
{
    public required int Id { get; init; }
    public required int Value { get; init; }
    public required int CellX { get; set; }
    public required int CellY { get; set; }
}

public sealed class WeightPadState
{
    public required int CellX { get; init; }
    public required int CellY { get; init; }
    public required int Multiplier { get; init; }
    public required string Label { get; init; }
}

public abstract class WeightGridPuzzleBase : RoomPuzzle, IBlockPushPuzzle
{
    private double _pushCooldownSeconds;

    protected WeightGridPuzzleBase(char key, string title, string instruction, int gridSize, double boardOriginX, double boardOriginY, double cellSize, IReadOnlyList<WeightedBlockState> blocks, IReadOnlyList<WeightPadState> pads)
        : base(key, title, instruction)
    {
        GridSize = gridSize;
        BoardOriginX = boardOriginX;
        BoardOriginY = boardOriginY;
        CellSize = cellSize;
        Blocks = blocks;
        Pads = pads;
    }

    public int GridSize { get; }
    public double BoardOriginX { get; }
    public double BoardOriginY { get; }
    public double CellSize { get; }
    public IReadOnlyList<WeightedBlockState> Blocks { get; }
    public IReadOnlyList<WeightPadState> Pads { get; }

    public override void Update(PuzzleUpdateContext context)
    {
        if (_pushCooldownSeconds > 0d)
        {
            _pushCooldownSeconds = Math.Max(0d, _pushCooldownSeconds - context.DeltaTimeSeconds);
        }
    }

    public bool BlocksPlayer(PlayAreaRect playerBounds) => Blocks.Any(block => GetExpandedBlockRect(block).Intersects(playerBounds));

    public bool TryPush(PlayerDirection direction, PlayAreaRect playerBounds)
    {
        if (IsCompleted || _pushCooldownSeconds > 0d)
        {
            return false;
        }

        var block = Blocks.FirstOrDefault(candidate => GetExpandedBlockRect(candidate).Intersects(playerBounds) && IsAligned(direction, playerBounds, candidate));
        if (block is null)
        {
            return false;
        }

        var (dx, dy) = direction switch
        {
            PlayerDirection.Up => (0, -1),
            PlayerDirection.Right => (1, 0),
            PlayerDirection.Down => (0, 1),
            PlayerDirection.Left => (-1, 0),
            _ => (0, 0),
        };

        var targetX = block.CellX + dx;
        var targetY = block.CellY + dy;
        if (targetX < 0 || targetY < 0 || targetX >= GridSize || targetY >= GridSize || Blocks.Any(other => other.Id != block.Id && other.CellX == targetX && other.CellY == targetY))
        {
            StatusText = "The weight catches against the floor grid.";
            _pushCooldownSeconds = 0.14d;
            return true;
        }

        block.CellX = targetX;
        block.CellY = targetY;
        _pushCooldownSeconds = 0.16d;
        Evaluate();
        return true;
    }

    public PlayAreaRect GetBlockRect(WeightedBlockState block) => new(BoardOriginX + (block.CellX * CellSize), BoardOriginY + (block.CellY * CellSize), CellSize, CellSize);

    public PlayAreaRect GetPadRect(WeightPadState pad) => new(BoardOriginX + (pad.CellX * CellSize), BoardOriginY + (pad.CellY * CellSize), CellSize, CellSize);

    protected int GetPadContribution(WeightPadState pad)
    {
        var block = Blocks.FirstOrDefault(candidate => candidate.CellX == pad.CellX && candidate.CellY == pad.CellY);
        return block is null ? 0 : block.Value * pad.Multiplier;
    }

    protected abstract void Evaluate();

    private PlayAreaRect GetExpandedBlockRect(WeightedBlockState block)
    {
        var rect = GetBlockRect(block);
        return new PlayAreaRect(rect.X - 18d, rect.Y - 18d, rect.Width + 36d, rect.Height + 36d);
    }

    private bool IsAligned(PlayerDirection direction, PlayAreaRect playerBounds, WeightedBlockState block)
    {
        var rect = GetBlockRect(block);
        return direction switch
        {
            PlayerDirection.Left or PlayerDirection.Right => Math.Abs(playerBounds.CenterY - rect.CenterY) <= CellSize * 0.45d,
            PlayerDirection.Up or PlayerDirection.Down => Math.Abs(playerBounds.CenterX - rect.CenterX) <= CellSize * 0.45d,
            _ => false,
        };
    }
}

public sealed class WeightedTriggerZonesPuzzle : WeightGridPuzzleBase
{
    public WeightedTriggerZonesPuzzle(int gridSize, double boardOriginX, double boardOriginY, double cellSize, IReadOnlyList<WeightedBlockState> blocks, IReadOnlyList<WeightPadState> pads, int targetTotal)
        : base('w', "Weighted Trigger Zones", "Push the weight blocks onto the pressure pads until the weighted total matches the target.", gridSize, boardOriginX, boardOriginY, cellSize, blocks, pads)
    {
        TargetTotal = targetTotal;
        Evaluate();
    }

    public int TargetTotal { get; }
    public int CurrentTotal => Pads.Sum(GetPadContribution);

    protected override void Evaluate()
    {
        if (CurrentTotal == TargetTotal)
        {
            Complete("The weighted pads settle at the exact target.");
            return;
        }

        StatusText = $"Weighted total: {CurrentTotal} / {TargetTotal}";
    }
}

public sealed class BinaryShiftPuzzle : RoomPuzzle
{
    public BinaryShiftPuzzle(bool[] startBits, bool[] targetBits)
        : base('x', "Binary Shift Puzzle", "Rotate the bit row and flip the center bit until the pattern matches the target.")
    {
        Bits = startBits;
        TargetBits = targetBits;
        UpdateStatus();
    }

    public bool[] Bits { get; }
    public bool[] TargetBits { get; }
    public int MoveCount { get; private set; }

    public void RotateLeft()
    {
        if (IsCompleted)
        {
            return;
        }

        var first = Bits[0];
        for (var index = 0; index < Bits.Length - 1; index++)
        {
            Bits[index] = Bits[index + 1];
        }

        Bits[^1] = first;
        CompleteIfSolved();
    }

    public void RotateRight()
    {
        if (IsCompleted)
        {
            return;
        }

        var last = Bits[^1];
        for (var index = Bits.Length - 1; index > 0; index--)
        {
            Bits[index] = Bits[index - 1];
        }

        Bits[0] = last;
        CompleteIfSolved();
    }

    public void FlipCenter()
    {
        if (IsCompleted)
        {
            return;
        }

        Bits[Bits.Length / 2] = !Bits[Bits.Length / 2];
        CompleteIfSolved();
    }

    private void CompleteIfSolved()
    {
        MoveCount++;
        if (Bits.SequenceEqual(TargetBits))
        {
            Complete("The binary row shifts into the correct state.");
            return;
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        StatusText = $"Current: {ToBitString(Bits)} | Target: {ToBitString(TargetBits)}";
    }

    private static string ToBitString(IEnumerable<bool> bits) => string.Concat(bits.Select(bit => bit ? '1' : '0'));
}

public sealed class CrossingPathPuzzle : RoomPuzzle
{
    private int? _selectedLeftIndex;

    public CrossingPathPuzzle(IReadOnlyList<string> labels)
        : base('y', "Crossing Path Puzzle", "Connect each left node to its matching right node. Any crossing line invalidates the network.")
    {
        Labels = labels;
        Connections = new int?[labels.Count];
        LeftPoints = Enumerable.Range(0, labels.Count).Select(index => new PlayAreaPoint(220d, 260d + (index * 140d))).ToArray();
        RightPoints = Enumerable.Range(0, labels.Count).Select(index => new PlayAreaPoint(860d, 260d + (index * 140d))).ToArray();
    }

    public IReadOnlyList<string> Labels { get; }
    public PlayAreaPoint[] LeftPoints { get; }
    public PlayAreaPoint[] RightPoints { get; }
    public int?[] Connections { get; }
    public int? SelectedLeftIndex => _selectedLeftIndex;

    public void SelectLeft(int index)
    {
        if (IsCompleted || index < 0 || index >= Labels.Count)
        {
            return;
        }

        _selectedLeftIndex = index;
        StatusText = $"Route {Labels[index]} to the correct receiver.";
    }

    public void ConnectRight(int rightIndex)
    {
        if (IsCompleted || _selectedLeftIndex is null || rightIndex < 0 || rightIndex >= Labels.Count)
        {
            return;
        }

        var leftIndex = _selectedLeftIndex.Value;
        _selectedLeftIndex = null;
        if (rightIndex != leftIndex)
        {
            StatusText = "That receiver carries the wrong sigil.";
            return;
        }

        Connections[leftIndex] = rightIndex;
        if (HasCrossing(leftIndex))
        {
            Connections[leftIndex] = null;
            StatusText = "That path crosses another route. Rewire it differently.";
            return;
        }

        if (Connections.All(connection => connection.HasValue))
        {
            Complete("All routes lock into a clean, non-crossing path.");
            return;
        }

        StatusText = $"Routes stable: {Connections.Count(connection => connection.HasValue)}/{Connections.Length}";
    }

    public IEnumerable<(PlayAreaPoint Start, PlayAreaPoint End)> GetLines()
    {
        for (var index = 0; index < Connections.Length; index++)
        {
            if (Connections[index] is int rightIndex)
            {
                yield return (LeftPoints[index], RightPoints[rightIndex]);
            }
        }
    }

    private bool HasCrossing(int latestLeft)
    {
        var latestRight = Connections[latestLeft]!.Value;
        for (var index = 0; index < Connections.Length; index++)
        {
            if (index == latestLeft || Connections[index] is not int otherRight)
            {
                continue;
            }

            if ((index < latestLeft && otherRight > latestRight) || (index > latestLeft && otherRight < latestRight))
            {
                return true;
            }
        }

        return false;
    }
}

public sealed class DelayedActivationSequencePuzzle : RoomPuzzle
{
    private double _holdSeconds;
    private double _delayRemainingSeconds;

    public DelayedActivationSequencePuzzle(IReadOnlyList<PlayAreaRect> zones, IReadOnlyList<int> order, double holdSeconds, double delaySeconds)
        : base('z', "Delayed Activation Sequence", "Stand in each zone in order. After each lock, wait for the next zone to wake before you move.")
    {
        Zones = zones;
        Order = order;
        RequiredHoldSeconds = holdSeconds;
        DelaySeconds = delaySeconds;
        UpdateStatus();
    }

    public IReadOnlyList<PlayAreaRect> Zones { get; }
    public IReadOnlyList<int> Order { get; }
    public double RequiredHoldSeconds { get; }
    public double DelaySeconds { get; }
    public int CurrentStepIndex { get; private set; }
    public bool IsWaitingForNextZone => _delayRemainingSeconds > 0d;
    public double HoldProgress => Math.Clamp(_holdSeconds / RequiredHoldSeconds, 0d, 1d);
    public int ActiveZoneIndex => Order[Math.Min(CurrentStepIndex, Order.Count - 1)];

    public override void Update(PuzzleUpdateContext context)
    {
        if (IsCompleted)
        {
            return;
        }

        var playerZone = GetTouchedZone(context.PlayerBounds);
        if (IsWaitingForNextZone)
        {
            if (playerZone != Order[CurrentStepIndex - 1])
            {
                Reset("You moved before the next zone stabilized. Sequence reset.");
                return;
            }

            _delayRemainingSeconds = Math.Max(0d, _delayRemainingSeconds - context.DeltaTimeSeconds);
            if (_delayRemainingSeconds <= 0d)
            {
                UpdateStatus();
            }

            return;
        }

        if (playerZone == ActiveZoneIndex)
        {
            _holdSeconds += context.DeltaTimeSeconds;
            StatusText = $"Charging zone {CurrentStepIndex + 1}/{Order.Count}: {Math.Round(HoldProgress * 100d)}%";
            if (_holdSeconds >= RequiredHoldSeconds)
            {
                _holdSeconds = 0d;
                CurrentStepIndex++;
                if (CurrentStepIndex >= Order.Count)
                {
                    Complete("The delayed sequence resolves cleanly.");
                    return;
                }

                _delayRemainingSeconds = DelaySeconds;
                StatusText = $"Zone {CurrentStepIndex} locked. Hold position until zone {CurrentStepIndex + 1} wakes.";
            }

            return;
        }

        if (playerZone is not null)
        {
            Reset("That zone woke out of order. Sequence reset.");
        }
        else if (_holdSeconds > 0d)
        {
            _holdSeconds = Math.Max(0d, _holdSeconds - (context.DeltaTimeSeconds * 1.4d));
        }
    }

    private int? GetTouchedZone(PlayAreaRect playerBounds)
    {
        for (var index = 0; index < Zones.Count; index++)
        {
            if (Zones[index].Intersects(playerBounds))
            {
                return index;
            }
        }

        return null;
    }

    private void Reset(string message)
    {
        CurrentStepIndex = 0;
        _holdSeconds = 0d;
        _delayRemainingSeconds = 0d;
        StatusText = message;
    }

    private void UpdateStatus()
    {
        StatusText = $"Step into zone {CurrentStepIndex + 1} and hold until it locks.";
    }
}

public sealed class HarmonicPhasePuzzle : RoomPuzzle
{
    private int? _lastTouchedNode;

    public HarmonicPhasePuzzle(IReadOnlyList<PlayAreaRect> nodes, int[] frequencies, int modulus)
        : base('p', "Harmonic Phase Plates", "Step on the plates until every frequency resonates together. Each plate shifts itself and its neighbors.")
    {
        Nodes = nodes;
        Frequencies = frequencies;
        Modulus = modulus;
        UpdateStatus();
    }

    public IReadOnlyList<PlayAreaRect> Nodes { get; }
    public int[] Frequencies { get; }
    public int Modulus { get; }

    public override void Update(PuzzleUpdateContext context)
    {
        if (IsCompleted)
        {
            return;
        }

        var touched = GetTouchedNode(context.PlayerBounds);
        if (touched == _lastTouchedNode)
        {
            return;
        }

        _lastTouchedNode = touched;
        if (touched is null)
        {
            return;
        }

        ApplyPlateShift(touched.Value);
        if (Frequencies.All(value => value == Frequencies[0]))
        {
            Complete("Every plate resonates in the same frequency.");
            return;
        }

        UpdateStatus();
    }

    private int? GetTouchedNode(PlayAreaRect playerBounds)
    {
        for (var index = 0; index < Nodes.Count; index++)
        {
            if (Nodes[index].Intersects(playerBounds))
            {
                return index;
            }
        }

        return null;
    }

    private void ApplyPlateShift(int index)
    {
        Frequencies[index] = Wrap(Frequencies[index] + 2);
        Frequencies[(index - 1 + Frequencies.Length) % Frequencies.Length] = Wrap(Frequencies[(index - 1 + Frequencies.Length) % Frequencies.Length] + 1);
        Frequencies[(index + 1) % Frequencies.Length] = Wrap(Frequencies[(index + 1) % Frequencies.Length] + 1);
    }

    private int Wrap(int value)
    {
        var wrapped = value % Modulus;
        return wrapped == 0 ? Modulus : wrapped;
    }

    private void UpdateStatus()
    {
        StatusText = $"Frequencies: {string.Join(" | ", Frequencies)}";
    }
}

public sealed class TemporalLockPuzzle : RoomPuzzle
{
    internal static readonly string[][] RingSymbolSets =
    [
        ["Moon", "Star", "Sun", "Eye", "Key", "Wave", "Flame", "Crown"],
        ["Star", "Wave", "Key", "Moon", "Crown", "Eye", "Sun", "Flame"],
        ["Eye", "Key", "Moon", "Flame", "Sun", "Wave", "Crown", "Star"],
    ];

    public TemporalLockPuzzle(double[] speeds, int[] targetIndices, IReadOnlyList<string> clues)
        : base('q', "Multi-Layer Temporal Lock", "Stop each temporal ring on the sigil implied by the cryptic clues. No target window is shown.")
    {
        Speeds = speeds;
        TargetIndices = targetIndices;
        Clues = clues;
        Positions = [0.4d, 2.1d, 5.3d];
        IsStopped = new bool[3];
        Directions = [1, -1, 1];
        UpdateStatus();
    }

    public double[] Speeds { get; }
    public int[] TargetIndices { get; }
    public IReadOnlyList<string> Clues { get; }
    public double[] Positions { get; }
    public bool[] IsStopped { get; }
    public int[] Directions { get; }
    public IReadOnlyList<IReadOnlyList<string>> RingSymbols => RingSymbolSets;

    public override void Update(PuzzleUpdateContext context)
    {
        if (IsCompleted)
        {
            return;
        }

        for (var ringIndex = 0; ringIndex < Positions.Length; ringIndex++)
        {
            if (IsStopped[ringIndex])
            {
                continue;
            }

            Positions[ringIndex] += Directions[ringIndex] * Speeds[ringIndex] * context.DeltaTimeSeconds;
            if (Positions[ringIndex] >= RingSymbolSets[ringIndex].Length)
            {
                Positions[ringIndex] -= RingSymbolSets[ringIndex].Length;
            }
            else if (Positions[ringIndex] < 0d)
            {
                Positions[ringIndex] += RingSymbolSets[ringIndex].Length;
            }
        }
    }

    public void ToggleRing(int ringIndex)
    {
        if (IsCompleted || ringIndex < 0 || ringIndex >= IsStopped.Length)
        {
            return;
        }

        IsStopped[ringIndex] = !IsStopped[ringIndex];
        if (IsStopped.All(value => value))
        {
            if (Enumerable.Range(0, IsStopped.Length).All(index => GetCurrentIndex(index) == TargetIndices[index]))
            {
                Complete("The temporal rings freeze in the correct relationship.");
                return;
            }

            StatusText = "The rings froze on the wrong constellation. Release one and try again.";
            return;
        }

        UpdateStatus();
    }

    public int GetCurrentIndex(int ringIndex) => ((int)Math.Round(Positions[ringIndex])) % RingSymbolSets[ringIndex].Length;

    public string GetCurrentSymbol(int ringIndex) => RingSymbolSets[ringIndex][GetCurrentIndex(ringIndex)];

    private void UpdateStatus()
    {
        StatusText = $"Outer {GetCurrentSymbol(0)} | Middle {GetCurrentSymbol(1)} | Inner {GetCurrentSymbol(2)}";
    }
}

public sealed class LogicalParadoxPuzzle : RoomPuzzle
{
    public LogicalParadoxPuzzle(IReadOnlyList<string> statueNames, IReadOnlyList<string> statements, IReadOnlyList<string> levers, int correctLeverIndex)
        : base('r', "Logical Paradox Chamber", "One voice lies, one tells truth, and one alternates. Pull the only lever consistent with every statement.")
    {
        StatueNames = statueNames;
        Statements = statements;
        Levers = levers;
        CorrectLeverIndex = correctLeverIndex;
    }

    public IReadOnlyList<string> StatueNames { get; }
    public IReadOnlyList<string> Statements { get; }
    public IReadOnlyList<string> Levers { get; }
    public int CorrectLeverIndex { get; }

    public void PullLever(int index)
    {
        if (IsCompleted)
        {
            return;
        }

        if (index == CorrectLeverIndex)
        {
            Complete("The paradox resolves and the true lever yields.");
            return;
        }

        StatusText = "The chamber rejects that deduction. Re-evaluate the contradictions.";
    }
}

public sealed class MemoryInterferencePuzzle : RoomPuzzle
{
    private readonly List<string> _entered = [];

    public MemoryInterferencePuzzle(IReadOnlyList<string> primarySequence, IReadOnlyList<string> interferenceSequence, IReadOnlyList<string> answerSequence, bool reversePrimary, double primaryRevealSeconds, double interferenceRevealSeconds)
        : base('s', "Memory Interference Matrix", "Two echoes will overlap. Trust the first pattern and ignore the interference.")
    {
        PrimarySequence = primarySequence;
        InterferenceSequence = interferenceSequence;
        AnswerSequence = answerSequence;
        ReversePrimary = reversePrimary;
        PrimaryRevealSeconds = primaryRevealSeconds;
        InterferenceRevealSeconds = interferenceRevealSeconds;
    }

    public IReadOnlyList<string> PrimarySequence { get; }
    public IReadOnlyList<string> InterferenceSequence { get; }
    public IReadOnlyList<string> AnswerSequence { get; }
    public IReadOnlyList<string> Entered => _entered;
    public bool ReversePrimary { get; }
    public double PrimaryRevealSeconds { get; }
    public double InterferenceRevealSeconds { get; }
    public double ElapsedSeconds { get; private set; }
    public bool ShowingPrimary => ElapsedSeconds < PrimaryRevealSeconds;
    public bool ShowingInterference => !ShowingPrimary && ElapsedSeconds < PrimaryRevealSeconds + InterferenceRevealSeconds;
    public IReadOnlyList<string> DisplayedSequence => ShowingPrimary ? PrimarySequence : InterferenceSequence;

    public override void Update(PuzzleUpdateContext context)
    {
        if (IsCompleted || (!ShowingPrimary && !ShowingInterference))
        {
            return;
        }

        ElapsedSeconds += context.DeltaTimeSeconds;
        if (!ShowingPrimary && !ShowingInterference)
        {
            StatusText = ReversePrimary
                ? "The real echo fades. Enter the first sequence in reverse."
                : "The real echo fades. Ignore the second pattern and enter the first.";
        }
    }

    public void Press(string rune)
    {
        if (IsCompleted || ShowingPrimary || ShowingInterference)
        {
            return;
        }

        var expected = AnswerSequence[_entered.Count];
        if (!string.Equals(expected, rune, StringComparison.Ordinal))
        {
            _entered.Clear();
            StatusText = "Interference won that round. The true pattern resets.";
            return;
        }

        _entered.Add(rune);
        StatusText = $"Recovered memory: {_entered.Count}/{AnswerSequence.Count}";
        if (_entered.Count >= AnswerSequence.Count)
        {
            Complete("You isolated the true memory trace.");
        }
    }
}

public sealed class RotationalCipherGridPuzzle : RoomPuzzle
{
    public RotationalCipherGridPuzzle(char[] grid, string targetWord, string clue)
        : base('t', "Rotational Cipher Grid", "Rotate rows and columns until the encrypted center row reveals the word hidden in the clue.")
    {
        Grid = grid;
        TargetWord = targetWord;
        Clue = clue;
        UpdateStatus();
    }

    public char[] Grid { get; }
    public string TargetWord { get; }
    public string Clue { get; }

    public char GetCell(int row, int column) => Grid[(row * 3) + column];

    public void RotateRow(int row)
    {
        if (IsCompleted || row < 0 || row >= 3)
        {
            return;
        }

        var offset = row * 3;
        (Grid[offset], Grid[offset + 1], Grid[offset + 2]) = (Grid[offset + 2], Grid[offset], Grid[offset + 1]);
        CompleteIfSolved();
    }

    public void RotateColumn(int column)
    {
        if (IsCompleted || column < 0 || column >= 3)
        {
            return;
        }

        (Grid[column], Grid[column + 3], Grid[column + 6]) = (Grid[column + 6], Grid[column], Grid[column + 3]);
        CompleteIfSolved();
    }

    private void CompleteIfSolved()
    {
        if (new string([Grid[3], Grid[4], Grid[5]]) == TargetWord)
        {
            Complete("The cipher row resolves into the hidden word.");
            return;
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        StatusText = $"Center row: {new string([Grid[3], Grid[4], Grid[5]])}";
    }
}

public sealed class DimensionalPatternShiftPuzzle : RoomPuzzle
{
    private readonly List<PlayerDirection> _entered = [];

    public DimensionalPatternShiftPuzzle(IReadOnlyList<PlayerDirection> pattern, IReadOnlyList<PlayerDirection> answer, string ruleDescription, double revealDurationSeconds)
        : base('u', "Dimensional Pattern Shift", "Interpret the transformation rule, then translate the pattern into its new dimension.")
    {
        Pattern = pattern;
        Answer = answer;
        RuleDescription = ruleDescription;
        RevealDurationSeconds = revealDurationSeconds;
    }

    public IReadOnlyList<PlayerDirection> Pattern { get; }
    public IReadOnlyList<PlayerDirection> Answer { get; }
    public IReadOnlyList<PlayerDirection> Entered => _entered;
    public string RuleDescription { get; }
    public double RevealDurationSeconds { get; }
    public double ElapsedSeconds { get; private set; }
    public bool IsPatternVisible => IsCompleted || ElapsedSeconds < RevealDurationSeconds;

    public override void Update(PuzzleUpdateContext context)
    {
        if (IsCompleted || !IsPatternVisible)
        {
            return;
        }

        ElapsedSeconds += context.DeltaTimeSeconds;
        if (!IsPatternVisible)
        {
            StatusText = "The original pattern collapses. Enter the transformed version.";
        }
    }

    public void Press(PlayerDirection direction)
    {
        if (IsCompleted)
        {
            return;
        }

        var expected = Answer[_entered.Count];
        if (expected != direction)
        {
            _entered.Clear();
            StatusText = "The dimensional map rejects that step. Start over.";
            return;
        }

        _entered.Add(direction);
        StatusText = $"Dimensional translation: {_entered.Count}/{Answer.Count}";
        if (_entered.Count >= Answer.Count)
        {
            Complete("The dimensional shift resolves.");
        }
    }
}

public sealed class ConservationNetworkPuzzle : RoomPuzzle
{
    public ConservationNetworkPuzzle(int[] outputs, int[] targetOutputs, IReadOnlyList<int[]> valveAdjustments)
        : base('v', "Conservation Network Puzzle", "Total flow never changes. Read the system, then bend it into the target distribution.")
    {
        Outputs = outputs;
        TargetOutputs = targetOutputs;
        ValveAdjustments = valveAdjustments;
        UpdateStatus();
    }

    public int[] Outputs { get; }
    public int[] TargetOutputs { get; }
    public IReadOnlyList<int[]> ValveAdjustments { get; }
    public int MoveCount { get; private set; }

    public void Pulse(int index)
    {
        if (IsCompleted || index < 0 || index >= ValveAdjustments.Count)
        {
            return;
        }

        var delta = ValveAdjustments[index];
        var preview = new int[Outputs.Length];
        for (var outputIndex = 0; outputIndex < Outputs.Length; outputIndex++)
        {
            preview[outputIndex] = Outputs[outputIndex] + delta[outputIndex];
            if (preview[outputIndex] < 0)
            {
                StatusText = "That valve would violate the conservation law.";
                return;
            }
        }

        for (var outputIndex = 0; outputIndex < Outputs.Length; outputIndex++)
        {
            Outputs[outputIndex] = preview[outputIndex];
        }

        MoveCount++;
        if (Outputs.SequenceEqual(TargetOutputs))
        {
            Complete("The network settles into the exact conserved distribution.");
            return;
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        StatusText = $"Current: {string.Join(" / ", Outputs)} | Target: {string.Join(" / ", TargetOutputs)}";
    }
}

public sealed class WeightedEquilibriumPuzzle : WeightGridPuzzleBase
{
    public WeightedEquilibriumPuzzle(int gridSize, double boardOriginX, double boardOriginY, double cellSize, IReadOnlyList<WeightedBlockState> blocks, IReadOnlyList<WeightPadState> pads)
        : base('w', "Weighted Equilibrium System", "Push the weights onto multiplier pads until the total equation balances at zero.", gridSize, boardOriginX, boardOriginY, cellSize, blocks, pads)
    {
        Evaluate();
    }

    public int CurrentEquation => Pads.Sum(GetPadContribution);
    public int OccupiedPads => Pads.Count(pad => Blocks.Any(block => block.CellX == pad.CellX && block.CellY == pad.CellY));

    protected override void Evaluate()
    {
        if (CurrentEquation == 0 && OccupiedPads >= 2)
        {
            Complete("The equation balances in perfect equilibrium.");
            return;
        }

        StatusText = $"Equation total: {CurrentEquation} (need 0)";
    }
}

public sealed class BinaryTransformationPuzzle : RoomPuzzle
{
    private readonly bool[] _startBits;

    public BinaryTransformationPuzzle(bool[] startBits, bool[] targetBits, int moveLimit, int xorMask, string hint)
        : base('x', "Binary Transformation Machine", "Chain the machine operations in the right order. Exceeding the move limit resets the state.")
    {
        Bits = startBits;
        _startBits = startBits.ToArray();
        TargetBits = targetBits;
        MoveLimit = moveLimit;
        XorMask = xorMask;
        Hint = hint;
        UpdateStatus();
    }

    public bool[] Bits { get; }
    public bool[] TargetBits { get; }
    public int MoveLimit { get; }
    public int XorMask { get; }
    public string Hint { get; }
    public int MovesUsed { get; private set; }

    public void RotateLeft()
    {
        if (IsCompleted)
        {
            return;
        }

        var first = Bits[0];
        for (var index = 0; index < Bits.Length - 1; index++)
        {
            Bits[index] = Bits[index + 1];
        }

        Bits[^1] = first;
        CompleteIfSolved();
    }

    public void RotateRight()
    {
        if (IsCompleted)
        {
            return;
        }

        var last = Bits[^1];
        for (var index = Bits.Length - 1; index > 0; index--)
        {
            Bits[index] = Bits[index - 1];
        }

        Bits[0] = last;
        CompleteIfSolved();
    }

    public void InvertAll()
    {
        if (IsCompleted)
        {
            return;
        }

        for (var index = 0; index < Bits.Length; index++)
        {
            Bits[index] = !Bits[index];
        }

        CompleteIfSolved();
    }

    public void FlipAlternate()
    {
        if (IsCompleted)
        {
            return;
        }

        for (var index = 0; index < Bits.Length; index += 2)
        {
            Bits[index] = !Bits[index];
        }

        CompleteIfSolved();
    }

    public void ApplyXorMask()
    {
        if (IsCompleted)
        {
            return;
        }

        for (var index = 0; index < Bits.Length; index++)
        {
            if (((XorMask >> (Bits.Length - 1 - index)) & 1) == 1)
            {
                Bits[index] = !Bits[index];
            }
        }

        CompleteIfSolved();
    }

    private void CompleteIfSolved()
    {
        MovesUsed++;
        if (Bits.SequenceEqual(TargetBits))
        {
            Complete("The binary machine resolves into the target state.");
            return;
        }

        if (MovesUsed >= MoveLimit)
        {
            Array.Copy(_startBits, Bits, Bits.Length);
            MovesUsed = 0;
            StatusText = "The machine overflowed. State reset to the starting pattern.";
            return;
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        StatusText = $"Current {ToBitString(Bits)} | Target {ToBitString(TargetBits)} | Moves {MovesUsed}/{MoveLimit}";
    }

    private static string ToBitString(IEnumerable<bool> bits) => string.Concat(bits.Select(bit => bit ? '1' : '0'));
}

public sealed class KnotTopologyPuzzle : RoomPuzzle
{
    private int? _selectedIndex;

    public KnotTopologyPuzzle(IReadOnlyList<string> strands, int[] order, IReadOnlyList<(int A, int B)> illusionPairs)
        : base('y', "Knot Topology Puzzle", "Swap strand endpoints until only illusion crossings remain.")
    {
        Strands = strands;
        Order = order;
        IllusionPairs = illusionPairs;
        UpdateStatus();
    }

    public IReadOnlyList<string> Strands { get; }
    public int[] Order { get; }
    public IReadOnlyList<(int A, int B)> IllusionPairs { get; }
    public int? SelectedIndex => _selectedIndex;
    public int RealCrossings => CountRealCrossings();

    public void Select(int index)
    {
        if (IsCompleted || index < 0 || index >= Order.Length)
        {
            return;
        }

        if (_selectedIndex is null)
        {
            _selectedIndex = index;
            StatusText = "Choose another strand to swap endpoints.";
            return;
        }

        var otherIndex = _selectedIndex.Value;
        _selectedIndex = null;
        (Order[otherIndex], Order[index]) = (Order[index], Order[otherIndex]);
        if (RealCrossings == 0)
        {
            Complete("Only illusion crossings remain. The knot is truly untangled.");
            return;
        }

        UpdateStatus();
    }

    public IEnumerable<(PlayAreaPoint Start, PlayAreaPoint End, bool IsIllusion)> GetLines()
    {
        for (var index = 0; index < Order.Length; index++)
        {
            var start = new PlayAreaPoint(220d, 220d + (index * 130d));
            var end = new PlayAreaPoint(860d, 220d + (Order[index] * 130d));
            yield return (start, end, false);
        }
    }

    private int CountRealCrossings()
    {
        var count = 0;
        for (var left = 0; left < Order.Length; left++)
        {
            for (var right = left + 1; right < Order.Length; right++)
            {
                if ((Order[left] > Order[right]) && !IsIllusionPair(left, right))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private bool IsIllusionPair(int a, int b) => IllusionPairs.Any(pair => (pair.A == a && pair.B == b) || (pair.A == b && pair.B == a));

    private void UpdateStatus()
    {
        StatusText = $"Real crossings remaining: {RealCrossings}";
    }
}

public enum RecursiveZoneModifier
{
    ReverseRemaining,
    RotateRemaining,
    SwapExtremes,
    ExtendNextHold,
}

public sealed class RecursiveActivationSequencePuzzle : RoomPuzzle
{
    private readonly List<int> _originalOrder;
    private readonly Queue<int> _remainingOrder;
    private readonly Dictionary<int, RecursiveZoneModifier> _modifiers;
    private readonly Dictionary<int, double> _zoneHoldDurations;
    private readonly double _baseHoldSeconds;
    private double _currentHoldSeconds;

    public RecursiveActivationSequencePuzzle(IReadOnlyList<PlayAreaRect> zones, IReadOnlyList<int> order, IReadOnlyDictionary<int, RecursiveZoneModifier> modifiers, double baseHoldSeconds)
        : base('z', "Recursive Activation Sequence", "Each zone mutates the sequence that follows it. Adapt after every activation.")
    {
        Zones = zones;
        _baseHoldSeconds = baseHoldSeconds;
        _originalOrder = order.ToList();
        _remainingOrder = new Queue<int>(order);
        _modifiers = modifiers.ToDictionary(entry => entry.Key, entry => entry.Value);
        _zoneHoldDurations = Enumerable.Range(0, zones.Count).ToDictionary(index => index, _ => baseHoldSeconds);
        CompletedOrder = [];
        UpdateStatus();
    }

    public IReadOnlyList<PlayAreaRect> Zones { get; }
    public List<int> CompletedOrder { get; }
    public int CurrentZoneIndex => _remainingOrder.Peek();
    public double CurrentRequiredHold => _zoneHoldDurations[CurrentZoneIndex];
    public double HoldProgress => Math.Clamp(_currentHoldSeconds / CurrentRequiredHold, 0d, 1d);

    public RecursiveZoneModifier GetModifierForZone(int zoneIndex) => _modifiers[zoneIndex];

    public override void Update(PuzzleUpdateContext context)
    {
        if (IsCompleted)
        {
            return;
        }

        var zoneIndex = GetTouchedZone(context.PlayerBounds);
        if (zoneIndex == CurrentZoneIndex)
        {
            _currentHoldSeconds += context.DeltaTimeSeconds;
            StatusText = $"Zone {CurrentZoneIndex + 1} charging: {Math.Round(HoldProgress * 100d)}%";
            if (_currentHoldSeconds >= CurrentRequiredHold)
            {
                _currentHoldSeconds = 0d;
                var completed = _remainingOrder.Dequeue();
                CompletedOrder.Add(completed);
                ApplyModifier(completed);
                if (_remainingOrder.Count == 0)
                {
                    Complete("The recursive sequence stabilizes.");
                    return;
                }

                UpdateStatus();
            }

            return;
        }

        if (zoneIndex is not null)
        {
            Reset("The sequence mutated past your prediction. Resetting recursion.");
        }
    }

    private int? GetTouchedZone(PlayAreaRect playerBounds)
    {
        for (var index = 0; index < Zones.Count; index++)
        {
            if (Zones[index].Intersects(playerBounds))
            {
                return index;
            }
        }

        return null;
    }

    private void ApplyModifier(int completedZone)
    {
        var remaining = _remainingOrder.ToList();
        switch (_modifiers[completedZone])
        {
            case RecursiveZoneModifier.ReverseRemaining:
                remaining.Reverse();
                break;
            case RecursiveZoneModifier.RotateRemaining:
                if (remaining.Count > 1)
                {
                    remaining.Add(remaining[0]);
                    remaining.RemoveAt(0);
                }
                break;
            case RecursiveZoneModifier.SwapExtremes:
                if (remaining.Count > 1)
                {
                    (remaining[0], remaining[^1]) = (remaining[^1], remaining[0]);
                }
                break;
            case RecursiveZoneModifier.ExtendNextHold:
                if (remaining.Count > 0)
                {
                    _zoneHoldDurations[remaining[0]] += 0.45d;
                }
                break;
        }

        _remainingOrder.Clear();
        foreach (var zone in remaining)
        {
            _remainingOrder.Enqueue(zone);
        }
    }

    private void Reset(string message)
    {
        CompletedOrder.Clear();
        _remainingOrder.Clear();
        foreach (var zone in _originalOrder)
        {
            _remainingOrder.Enqueue(zone);
            _zoneHoldDurations[zone] = _baseHoldSeconds;
        }

        _currentHoldSeconds = 0d;
        StatusText = message;
    }

    private void UpdateStatus()
    {
        StatusText = $"Step into zone {CurrentZoneIndex + 1}. Its modifier will alter the remaining sequence.";
    }
}

public static class AdvancedPuzzleFactory
{
    private static readonly string[] LogicSymbols = ["Triangle", "Circle", "Square", "Spiral", "Crown", "Key", "Eye"];
    private static readonly string[] MemoryRunes = ["A", "B", "C", "D", "E"];
    private static readonly string[] CrossingLabels = ["Sun", "Moon", "Key", "Eye"];
    private static readonly (string Word, string Clue)[] CipherWords =
    [
        ("KEY", "What opens the hidden chamber?"),
        ("EYE", "What watches every corridor?"),
        ("ORB", "What hums at the heart of the maze?"),
        ("ARC", "What shape bridges the current?"),
    ];

    private static readonly (string[] Statements, int CorrectLeverIndex)[] ParadoxScenarios =
    [
        (["{0}: \"{1} lies.\"", "{1}: \"The left lever is false.\"", "{2}: \"Exactly one of us lies.\""], 1),
        (["{0}: \"{2} tells the truth.\"", "{1}: \"The right lever fails.\"", "{2}: \"{0} and I disagree.\""], 0),
        (["{0}: \"The center lever is safe.\"", "{1}: \"{0} lies.\"", "{2}: \"Only one of us tells the truth.\""], 2),
    ];

    public static RoomPuzzle Create(string seed, string runNonce, MazeRoomDefinition room, MazeDifficulty difficulty)
    {
        if (difficulty == MazeDifficulty.Easy)
        {
            throw new InvalidOperationException("Advanced puzzles are only used for medium and hard runs.");
        }

        var hash = PuzzleFactory.StableHash($"{seed}|{runNonce}|{room.Coordinates.X}|{room.Coordinates.Y}|{room.PuzzleKey}|{difficulty}");
        return difficulty switch
        {
            MazeDifficulty.Medium => CreateMedium(room.PuzzleKey, hash),
            MazeDifficulty.Hard => CreateHard(room.PuzzleKey, hash),
            _ => throw new ArgumentOutOfRangeException(nameof(difficulty)),
        };
    }

    private static RoomPuzzle CreateMedium(char key, int hash) => key switch
    {
        'p' => CreatePhaseRelay(hash),
        'q' => new DualPulseLockPuzzle(
            0.18d + ((hash % 18) / 100d),
            0.12d,
            0.58d - (((hash / 7) % 18) / 100d),
            0.1d,
            1.05d + ((hash % 7) * 0.08d),
            1.22d + (((hash / 11) % 7) * 0.07d)),
        'r' => CreateSymbolLogic(hash),
        's' => CreateFadingPath(hash),
        't' => CreateSignalNetwork(hash),
        'u' => CreateDirectionalEcho(hash),
        'v' => CreateFlowRedistribution(hash),
        'w' => CreateWeightedTrigger(hash),
        'x' => CreateBinaryShift(hash),
        'y' => new CrossingPathPuzzle(CrossingLabels),
        'z' => CreateDelayedActivation(hash),
        _ => throw new MazeSeedParseException($"Unknown medium puzzle type '{key}'."),
    };

    private static RoomPuzzle CreateHard(char key, int hash) => key switch
    {
        'p' => CreateHarmonicPhase(hash),
        'q' => CreateTemporalLock(hash),
        'r' => CreateLogicalParadox(hash),
        's' => CreateMemoryInterference(hash),
        't' => CreateCipherGrid(hash),
        'u' => CreateDimensionalShift(hash),
        'v' => CreateConservationNetwork(hash),
        'w' => CreateWeightedEquilibrium(hash),
        'x' => CreateBinaryTransformation(hash),
        'y' => CreateKnotTopology(hash),
        'z' => CreateRecursiveSequence(hash),
        _ => throw new MazeSeedParseException($"Unknown hard puzzle type '{key}'."),
    };

    private static PhaseRelayPuzzle CreatePhaseRelay(int hash)
    {
        var count = 3 + (hash % 3);
        var nodes = CreateRingNodes(hash, count, 112d, 250d, 320d);
        var sequence = Shuffle(Enumerable.Range(0, count).ToList(), hash);
        return new PhaseRelayPuzzle(nodes, sequence, 2d);
    }

    private static SymbolLogicPuzzle CreateSymbolLogic(int hash)
    {
        var symbols = Shuffle(LogicSymbols.Take(5).ToList(), hash).Take(3).ToArray();
        var solution = Shuffle(symbols.ToList(), hash ^ 0x3311).ToArray();
        var statements = BuildUniqueLogicClues(solution);
        return new SymbolLogicPuzzle(symbols, statements, solution);
    }

    private static FadingPathMemoryPuzzle CreateFadingPath(int hash)
    {
        var path = CreateSelfAvoidingPath(hash, 6, 7 + (hash % 2));
        return new FadingPathMemoryPuzzle(6, 210d, 210d, 110d, path, 2.4d);
    }

    private static SignalRotationNetworkPuzzle CreateSignalNetwork(int hash)
    {
        const int gridSize = 4;
        var masks = new int[gridSize * gridSize];
        var path = CreateMonotonicTilePath(hash, gridSize);
        for (var index = 0; index < path.Count - 1; index++)
        {
            AddPipeConnection(masks, path[index], path[index + 1], gridSize);
        }

        var state = hash;
        for (var edge = 0; edge < 4; edge++)
        {
            state = NextState(state + edge);
            var from = state % masks.Length;
            var x = from % gridSize;
            var y = from / gridSize;
            var neighbors = new List<int>();
            if (x > 0) neighbors.Add(from - 1);
            if (x < gridSize - 1) neighbors.Add(from + 1);
            if (y > 0) neighbors.Add(from - gridSize);
            if (y < gridSize - 1) neighbors.Add(from + gridSize);
            var to = neighbors[state % neighbors.Count];
            AddPipeConnection(masks, from, to, gridSize);
        }

        var rotations = new int[masks.Length];
        for (var index = 0; index < rotations.Length; index++)
        {
            state = NextState(state + index);
            rotations[index] = 1 + (state % 3);
        }

        return new SignalRotationNetworkPuzzle(gridSize, masks, rotations, 0, masks.Length - 1);
    }

    private static DirectionalEchoPuzzle CreateDirectionalEcho(int hash)
    {
        var pattern = CreateDirectionPattern(hash, 6);
        var kinds = new[]
        {
            DirectionTransformKind.Reverse,
            DirectionTransformKind.RotateClockwise,
            DirectionTransformKind.MirrorHorizontal,
        };
        var kind = kinds[hash % kinds.Length];
        var solution = ApplyDirectionTransform(pattern, kind);
        return new DirectionalEchoPuzzle(pattern, solution, kind, 2.25d);
    }

    private static FlowRedistributionPuzzle CreateFlowRedistribution(int hash)
    {
        var outputs = new[] { 8, 8, 8 };
        var valves = new[]
        {
            new[] { 2, -1, -1 },
            new[] { -1, 2, -1 },
            new[] { -1, -1, 2 },
        };

        var state = hash;
        for (var step = 0; step < 6; step++)
        {
            state = NextState(state + step);
            _ = TryApplyDelta(outputs, valves[state % valves.Length]);
        }

        if (outputs.All(value => value == outputs[0]))
        {
            ApplyDelta(outputs, valves[0]);
        }

        return new FlowRedistributionPuzzle(outputs, valves);
    }

    private static WeightedTriggerZonesPuzzle CreateWeightedTrigger(int hash)
    {
        var blocks = new[]
        {
            new WeightedBlockState { Id = 0, Value = 2, CellX = 0, CellY = 4 },
            new WeightedBlockState { Id = 1, Value = 3, CellX = 2, CellY = 4 },
            new WeightedBlockState { Id = 2, Value = 4, CellX = 4, CellY = 4 },
        };

        var pads = new[]
        {
            new WeightPadState { CellX = 0, CellY = 1, Multiplier = 1, Label = "x1" },
            new WeightPadState { CellX = 2, CellY = 1, Multiplier = 2, Label = "x2" },
            new WeightPadState { CellX = 4, CellY = 1, Multiplier = 3, Label = "x3" },
        };

        var ordering = Shuffle(new List<int> { 0, 1, 2 }, hash);
        var target = (blocks[ordering[0]].Value * pads[0].Multiplier) +
                     (blocks[ordering[1]].Value * pads[1].Multiplier) +
                     (blocks[ordering[2]].Value * pads[2].Multiplier);

        return new WeightedTriggerZonesPuzzle(5, 240d, 240d, 120d, blocks, pads, target);
    }

    private static BinaryShiftPuzzle CreateBinaryShift(int hash)
    {
        var target = CreateBitPattern(hash, 5);
        var current = target.ToArray();
        var state = hash;
        for (var step = 0; step < 4; step++)
        {
            state = NextState(state + step);
            ApplyBinaryShiftScramble(current, state % 3);
        }

        if (current.SequenceEqual(target))
        {
            ApplyBinaryShiftScramble(current, 1);
        }

        return new BinaryShiftPuzzle(current, target);
    }

    private static DelayedActivationSequencePuzzle CreateDelayedActivation(int hash)
    {
        var zones = CreateZoneRing(hash, 4, 120d, 280d);
        var order = Shuffle(Enumerable.Range(0, 4).ToList(), hash ^ 0x9f11);
        return new DelayedActivationSequencePuzzle(zones, order, 0.72d, 0.95d);
    }

    private static HarmonicPhasePuzzle CreateHarmonicPhase(int hash)
    {
        var count = 3 + (hash % 2);
        var modulus = 7;
        var nodes = CreateRingNodes(hash, count, 128d, 240d, 300d);
        var targetFrequency = 1 + (hash % modulus);
        var frequencies = Enumerable.Repeat(targetFrequency, count).ToArray();
        var state = hash;
        for (var step = 0; step < 5; step++)
        {
            state = NextState(state + step);
            ApplyHarmonicShift(frequencies, state % count, modulus);
        }

        if (frequencies.All(value => value == frequencies[0]))
        {
            ApplyHarmonicShift(frequencies, 0, modulus);
        }

        return new HarmonicPhasePuzzle(nodes, frequencies, modulus);
    }

    private static TemporalLockPuzzle CreateTemporalLock(int hash)
    {
        var targetIndices = new[]
        {
            hash % 8,
            (hash / 7) % 8,
            (hash / 19) % 8,
        };

        var clues = new[]
        {
            $"Outer ring: stop on the sigil after {TemporalLockPuzzle.RingSymbolSets[0][(targetIndices[0] - 1 + 8) % 8]}.",
            $"Middle ring: stop on the sigil opposite {TemporalLockPuzzle.RingSymbolSets[1][(targetIndices[1] + 4) % 8]}.",
            $"Inner ring: stop on the sigil before {TemporalLockPuzzle.RingSymbolSets[2][(targetIndices[2] + 1) % 8]}.",
        };

        var speeds = new[]
        {
            0.95d + ((hash % 6) * 0.08d),
            1.2d + (((hash / 11) % 6) * 0.09d),
            1.45d + (((hash / 23) % 6) * 0.09d),
        };

        return new TemporalLockPuzzle(speeds, targetIndices, clues);
    }

    private static LogicalParadoxPuzzle CreateLogicalParadox(int hash)
    {
        var scenario = ParadoxScenarios[hash % ParadoxScenarios.Length];
        var names = Shuffle(new List<string> { "Astra", "Cinder", "Vale", "Morrow", "Nyx" }, hash).Take(3).ToArray();
        var statements = scenario.Statements.Select(statement => string.Format(statement, names[0], names[1], names[2])).ToArray();
        var levers = new[] { "Left Lever", "Center Lever", "Right Lever" };
        return new LogicalParadoxPuzzle(names, statements, levers, scenario.CorrectLeverIndex);
    }

    private static MemoryInterferencePuzzle CreateMemoryInterference(int hash)
    {
        var primary = CreateRuneSequence(hash, 7);
        var secondary = primary.ToArray();
        secondary[(hash / 5) % secondary.Length] = MemoryRunes[(hash / 13) % MemoryRunes.Length];
        secondary[(hash / 17) % secondary.Length] = MemoryRunes[(hash / 29) % MemoryRunes.Length];
        var reverse = (hash & 1) == 0;
        var answer = reverse ? primary.Reverse().ToArray() : primary.ToArray();
        return new MemoryInterferencePuzzle(primary, secondary, answer, reverse, 1.45d, 1.1d);
    }

    private static RotationalCipherGridPuzzle CreateCipherGrid(int hash)
    {
        var choice = CipherWords[hash % CipherWords.Length];
        var state = hash;
        var grid = new char[9];
        for (var index = 0; index < grid.Length; index++)
        {
            state = NextState(state + index);
            grid[index] = (char)('A' + (state % 26));
        }

        grid[3] = choice.Word[0];
        grid[4] = choice.Word[1];
        grid[5] = choice.Word[2];

        for (var step = 0; step < 6; step++)
        {
            state = NextState(state + step);
            if ((state & 1) == 0)
            {
                RotateCipherRow(grid, state % 3);
            }
            else
            {
                RotateCipherColumn(grid, state % 3);
            }
        }

        if (new string([grid[3], grid[4], grid[5]]) == choice.Word)
        {
            RotateCipherRow(grid, 1);
        }

        return new RotationalCipherGridPuzzle(grid, choice.Word, choice.Clue);
    }

    private static DimensionalPatternShiftPuzzle CreateDimensionalShift(int hash)
    {
        var pattern = CreateDirectionPattern(hash, 7);
        var ruleIndex = hash % 4;
        var (answer, description) = ruleIndex switch
        {
            0 => (ApplyDirectionTransform(pattern, DirectionTransformKind.RotateHalfTurn), "Invert the pattern across the axis."),
            1 => (ApplyDirectionTransform(pattern, DirectionTransformKind.MirrorHorizontal), "Reflect the pattern through the mirror axis."),
            2 => (ApplyDirectionTransform(pattern, DirectionTransformKind.RotateCounterClockwise), "Rotate the pattern 90 degrees counterclockwise."),
            _ => (ApplyDirectionTransform(pattern, DirectionTransformKind.Reverse), "The first becomes the last; answer in reverse."),
        };

        return new DimensionalPatternShiftPuzzle(pattern, answer, description, 1.55d);
    }

    private static ConservationNetworkPuzzle CreateConservationNetwork(int hash)
    {
        var target = new[] { 5, 7, 9 };
        var outputs = target.ToArray();
        var valves = new[]
        {
            new[] { 3, -2, -1 },
            new[] { -2, 3, -1 },
            new[] { -1, -2, 3 },
            new[] { 2, -3, 1 },
        };

        var state = hash;
        for (var step = 0; step < 6; step++)
        {
            state = NextState(state + step);
            _ = TryApplyDelta(outputs, valves[state % valves.Length]);
        }

        if (outputs.SequenceEqual(target))
        {
            ApplyDelta(outputs, valves[0]);
        }

        return new ConservationNetworkPuzzle(outputs, target, valves);
    }

    private static WeightedEquilibriumPuzzle CreateWeightedEquilibrium(int hash)
    {
        var blocks = new[]
        {
            new WeightedBlockState { Id = 0, Value = 2, CellX = 0, CellY = 4 },
            new WeightedBlockState { Id = 1, Value = 4, CellX = 2, CellY = 4 },
            new WeightedBlockState { Id = 2, Value = 6, CellX = 4, CellY = 4 },
        };

        var pads = new[]
        {
            new WeightPadState { CellX = 0, CellY = 1, Multiplier = 2, Label = "x2" },
            new WeightPadState { CellX = 2, CellY = 1, Multiplier = -1, Label = "x-1" },
            new WeightPadState { CellX = 4, CellY = 1, Multiplier = -1, Label = "x-1" },
            new WeightPadState { CellX = 2, CellY = 2, Multiplier = 1, Label = "x1" },
        };

        return new WeightedEquilibriumPuzzle(5, 240d, 240d, 120d, blocks, pads);
    }

    private static BinaryTransformationPuzzle CreateBinaryTransformation(int hash)
    {
        var target = CreateBitPattern(hash, 5);
        var current = target.ToArray();
        var xorMask = 0b10101 ^ ((hash >> 3) & 0b11111);
        var state = hash;
        for (var step = 0; step < 5; step++)
        {
            state = NextState(state + step);
            ApplyBinaryTransformationScramble(current, state % 5, xorMask);
        }

        if (current.SequenceEqual(target))
        {
            ApplyBinaryTransformationScramble(current, 3, xorMask);
        }

        return new BinaryTransformationPuzzle(current, target, 6, xorMask, "Mirror before you invert.");
    }

    private static KnotTopologyPuzzle CreateKnotTopology(int hash)
    {
        var strands = new[] { "Moon", "Key", "Eye", "Wave", "Flame" };
        var target = new[] { 0, 2, 1, 3, 4 };
        var illusionPairs = new List<(int, int)> { (1, 2) };
        var order = target.ToArray();
        var state = hash;
        for (var step = 0; step < 5; step++)
        {
            state = NextState(state + step);
            var a = state % order.Length;
            var b = (state / 7) % order.Length;
            (order[a], order[b]) = (order[b], order[a]);
        }

        return new KnotTopologyPuzzle(strands, order, illusionPairs);
    }

    private static RecursiveActivationSequencePuzzle CreateRecursiveSequence(int hash)
    {
        var zones = CreateZoneRing(hash, 4, 120d, 300d);
        var order = Shuffle(Enumerable.Range(0, 4).ToList(), hash);
        var modifierValues = Enum.GetValues<RecursiveZoneModifier>().ToArray();
        var modifiers = new Dictionary<int, RecursiveZoneModifier>();
        var state = hash;
        for (var zone = 0; zone < 4; zone++)
        {
            state = NextState(state + zone);
            modifiers[zone] = modifierValues[state % modifierValues.Length];
        }

        return new RecursiveActivationSequencePuzzle(zones, order, modifiers, 0.9d);
    }

    private static IReadOnlyList<string> BuildUniqueLogicClues(IReadOnlyList<string> solution)
    {
        var permutations = GetPermutations(solution.ToArray(), 0).Select(items => items.ToArray()).ToArray();
        var candidates = new List<(string Text, Func<string[], bool> Matches)>();

        foreach (var symbol in solution)
        {
            candidates.Add(($"{symbol} is not first.", order => order[0] != symbol));
            candidates.Add(($"{symbol} is not last.", order => order[^1] != symbol));
            candidates.Add(($"{symbol} stands in the middle.", order => order[1] == symbol));
        }

        for (var left = 0; left < solution.Count; left++)
        {
            for (var right = 0; right < solution.Count; right++)
            {
                if (left == right)
                {
                    continue;
                }

                var first = solution[left];
                var second = solution[right];
                candidates.Add(($"{first} comes before {second}.", order => Array.IndexOf(order, first) < Array.IndexOf(order, second)));
                candidates.Add(($"{first} is not adjacent to {second}.", order => Math.Abs(Array.IndexOf(order, first) - Array.IndexOf(order, second)) > 1));
            }
        }

        for (var first = 0; first < candidates.Count - 2; first++)
        {
            for (var second = first + 1; second < candidates.Count - 1; second++)
            {
                for (var third = second + 1; third < candidates.Count; third++)
                {
                    var trio = new[] { candidates[first], candidates[second], candidates[third] };
                    if (trio.Any(clue => !clue.Matches(solution.ToArray())))
                    {
                        continue;
                    }

                    var matchingPermutations = permutations.Count(order => trio.All(clue => clue.Matches(order)));
                    if (matchingPermutations == 1)
                    {
                        return trio.Select(clue => clue.Text).ToArray();
                    }
                }
            }
        }

        return [$"{solution[0]} comes before {solution[1]}.", $"{solution[1]} comes before {solution[2]}.", $"{solution[2]} is not first."];
    }

    private static IEnumerable<T[]> GetPermutations<T>(T[] items, int index)
    {
        if (index == items.Length - 1)
        {
            yield return items.ToArray();
            yield break;
        }

        for (var swapIndex = index; swapIndex < items.Length; swapIndex++)
        {
            (items[index], items[swapIndex]) = (items[swapIndex], items[index]);
            foreach (var permutation in GetPermutations(items, index + 1))
            {
                yield return permutation;
            }

            (items[index], items[swapIndex]) = (items[swapIndex], items[index]);
        }
    }

    private static IReadOnlyList<PlayAreaRect> CreateRingNodes(int hash, int count, double nodeSize, double minRadius, double maxRadius)
    {
        var nodes = new List<PlayAreaRect>(count);
        var state = hash;
        for (var index = 0; index < count; index++)
        {
            state = NextState(state + index);
            var angle = ((Math.PI * 2d) / count) * index;
            var radius = minRadius + (state % (int)(maxRadius - minRadius));
            var x = 540d + (Math.Cos(angle) * radius) - (nodeSize / 2d);
            var y = 540d + (Math.Sin(angle) * radius) - (nodeSize / 2d);
            nodes.Add(new PlayAreaRect(x, y, nodeSize, nodeSize));
        }

        return nodes;
    }

    private static IReadOnlyList<PlayAreaRect> CreateZoneRing(int hash, int count, double size, double radius)
    {
        var zones = new List<PlayAreaRect>(count);
        var state = hash;
        for (var index = 0; index < count; index++)
        {
            state = NextState(state + index);
            var angle = ((Math.PI * 2d) / count) * index;
            var x = 540d + (Math.Cos(angle) * radius) - (size / 2d) + ((state % 30) - 15);
            var y = 540d + (Math.Sin(angle) * radius) - (size / 2d) + (((state / 5) % 30) - 15);
            zones.Add(new PlayAreaRect(x, y, size, size));
        }

        return zones;
    }

    private static IReadOnlyList<GridPoint> CreateSelfAvoidingPath(int hash, int gridSize, int length)
    {
        var state = hash;
        var current = new GridPoint(state % gridSize, (state / 7) % gridSize);
        var path = new List<GridPoint> { current };
        var visited = new HashSet<GridPoint> { current };

        while (path.Count < length)
        {
            state = NextState(state + path.Count);
            var candidates = new List<GridPoint>();
            foreach (var next in new[]
                     {
                         new GridPoint(current.X, current.Y - 1),
                         new GridPoint(current.X + 1, current.Y),
                         new GridPoint(current.X, current.Y + 1),
                         new GridPoint(current.X - 1, current.Y),
                     })
            {
                if (next.X >= 0 && next.Y >= 0 && next.X < gridSize && next.Y < gridSize && visited.Add(next))
                {
                    candidates.Add(next);
                }
            }

            if (candidates.Count == 0)
            {
                break;
            }

            current = candidates[state % candidates.Count];
            path.Add(current);
        }

        return path;
    }

    private static IReadOnlyList<int> CreateMonotonicTilePath(int hash, int gridSize)
    {
        var path = new List<int> { 0 };
        var x = 0;
        var y = 0;
        var state = hash;
        while (x < gridSize - 1 || y < gridSize - 1)
        {
            state = NextState(state + x + y);
            var moveRight = x < gridSize - 1 && (y == gridSize - 1 || (state & 1) == 0);
            if (moveRight)
            {
                x++;
            }
            else
            {
                y++;
            }

            path.Add((y * gridSize) + x);
        }

        return path;
    }

    private static void AddPipeConnection(int[] masks, int from, int to, int gridSize)
    {
        var difference = to - from;
        if (difference == 1)
        {
            masks[from] |= 2;
            masks[to] |= 8;
        }
        else if (difference == -1)
        {
            masks[from] |= 8;
            masks[to] |= 2;
        }
        else if (difference == gridSize)
        {
            masks[from] |= 4;
            masks[to] |= 1;
        }
        else if (difference == -gridSize)
        {
            masks[from] |= 1;
            masks[to] |= 4;
        }
    }

    private static IReadOnlyList<PlayerDirection> CreateDirectionPattern(int hash, int length)
    {
        var directions = new List<PlayerDirection>(length);
        var state = hash;
        var available = Enum.GetValues<PlayerDirection>();
        for (var index = 0; index < length; index++)
        {
            state = NextState(state + index);
            directions.Add(available[state % available.Length]);
        }

        return directions;
    }

    private static IReadOnlyList<PlayerDirection> ApplyDirectionTransform(IReadOnlyList<PlayerDirection> pattern, DirectionTransformKind kind)
    {
        IEnumerable<PlayerDirection> transformed = kind switch
        {
            DirectionTransformKind.Reverse => pattern.Reverse(),
            DirectionTransformKind.RotateClockwise => pattern.Select(direction => direction switch
            {
                PlayerDirection.Up => PlayerDirection.Right,
                PlayerDirection.Right => PlayerDirection.Down,
                PlayerDirection.Down => PlayerDirection.Left,
                PlayerDirection.Left => PlayerDirection.Up,
                _ => direction,
            }),
            DirectionTransformKind.RotateCounterClockwise => pattern.Select(direction => direction switch
            {
                PlayerDirection.Up => PlayerDirection.Left,
                PlayerDirection.Right => PlayerDirection.Up,
                PlayerDirection.Down => PlayerDirection.Right,
                PlayerDirection.Left => PlayerDirection.Down,
                _ => direction,
            }),
            DirectionTransformKind.MirrorHorizontal => pattern.Select(direction => direction switch
            {
                PlayerDirection.Left => PlayerDirection.Right,
                PlayerDirection.Right => PlayerDirection.Left,
                _ => direction,
            }),
            DirectionTransformKind.RotateHalfTurn => pattern.Select(direction => direction switch
            {
                PlayerDirection.Up => PlayerDirection.Down,
                PlayerDirection.Right => PlayerDirection.Left,
                PlayerDirection.Down => PlayerDirection.Up,
                PlayerDirection.Left => PlayerDirection.Right,
                _ => direction,
            }),
            _ => pattern,
        };

        return transformed.ToArray();
    }

    private static bool TryApplyDelta(int[] outputs, int[] delta)
    {
        var preview = new int[outputs.Length];
        for (var index = 0; index < outputs.Length; index++)
        {
            preview[index] = outputs[index] + delta[index];
            if (preview[index] < 0)
            {
                return false;
            }
        }

        Array.Copy(preview, outputs, outputs.Length);
        return true;
    }

    private static void ApplyDelta(int[] outputs, int[] delta)
    {
        if (!TryApplyDelta(outputs, delta))
        {
            throw new InvalidOperationException("Attempted to apply an invalid conservation delta.");
        }
    }

    private static bool[] CreateBitPattern(int hash, int length)
    {
        var bits = new bool[length];
        var state = hash;
        for (var index = 0; index < length; index++)
        {
            state = NextState(state + index);
            bits[index] = (state & 1) == 1;
        }

        if (bits.All(bit => bit == bits[0]))
        {
            bits[^1] = !bits[^1];
        }

        return bits;
    }

    private static void ApplyBinaryShiftScramble(bool[] bits, int operation)
    {
        switch (operation)
        {
            case 0:
                var first = bits[0];
                for (var index = 0; index < bits.Length - 1; index++)
                {
                    bits[index] = bits[index + 1];
                }

                bits[^1] = first;
                break;
            case 1:
                var last = bits[^1];
                for (var index = bits.Length - 1; index > 0; index--)
                {
                    bits[index] = bits[index - 1];
                }

                bits[0] = last;
                break;
            default:
                bits[bits.Length / 2] = !bits[bits.Length / 2];
                break;
        }
    }

    private static void ApplyBinaryTransformationScramble(bool[] bits, int operation, int xorMask)
    {
        switch (operation)
        {
            case 0:
                ApplyBinaryShiftScramble(bits, 0);
                break;
            case 1:
                ApplyBinaryShiftScramble(bits, 1);
                break;
            case 2:
                for (var index = 0; index < bits.Length; index++)
                {
                    bits[index] = !bits[index];
                }

                break;
            case 3:
                for (var index = 0; index < bits.Length; index += 2)
                {
                    bits[index] = !bits[index];
                }

                break;
            default:
                for (var index = 0; index < bits.Length; index++)
                {
                    if (((xorMask >> (bits.Length - 1 - index)) & 1) == 1)
                    {
                        bits[index] = !bits[index];
                    }
                }

                break;
        }
    }

    private static void RotateCipherRow(char[] grid, int row)
    {
        var offset = row * 3;
        (grid[offset], grid[offset + 1], grid[offset + 2]) = (grid[offset + 2], grid[offset], grid[offset + 1]);
    }

    private static void RotateCipherColumn(char[] grid, int column)
    {
        (grid[column], grid[column + 3], grid[column + 6]) = (grid[column + 6], grid[column], grid[column + 3]);
    }

    private static IReadOnlyList<string> CreateRuneSequence(int hash, int length)
    {
        var sequence = new List<string>(length);
        var state = hash;
        for (var index = 0; index < length; index++)
        {
            state = NextState(state + index);
            sequence.Add(MemoryRunes[state % MemoryRunes.Length]);
        }

        return sequence;
    }

    private static void ApplyHarmonicShift(int[] frequencies, int index, int modulus)
    {
        frequencies[index] = Wrap(frequencies[index] + 2, modulus);
        frequencies[(index - 1 + frequencies.Length) % frequencies.Length] = Wrap(frequencies[(index - 1 + frequencies.Length) % frequencies.Length] + 1, modulus);
        frequencies[(index + 1) % frequencies.Length] = Wrap(frequencies[(index + 1) % frequencies.Length] + 1, modulus);
    }

    private static int Wrap(int value, int modulus)
    {
        var wrapped = value % modulus;
        return wrapped == 0 ? modulus : wrapped;
    }

    private static List<int> Shuffle(List<int> values, int hash)
    {
        var state = hash;
        for (var index = values.Count - 1; index > 0; index--)
        {
            state = NextState(state + index);
            var swapIndex = state % (index + 1);
            (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
        }

        return values;
    }

    private static List<T> Shuffle<T>(List<T> values, int hash)
    {
        var state = hash;
        for (var index = values.Count - 1; index > 0; index--)
        {
            state = NextState(state + index);
            var swapIndex = state % (index + 1);
            (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
        }

        return values;
    }

    private static int NextState(int seed)
    {
        unchecked
        {
            return (int)(((uint)seed * 1103515245u + 12345u) & 0x7fffffff);
        }
    }
}
