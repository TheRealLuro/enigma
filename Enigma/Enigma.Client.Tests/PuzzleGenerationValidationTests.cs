using System.Globalization;
using System.Linq;
using System.Text;
using Enigma.Client.Models.Gameplay;
using Xunit;

namespace Enigma.Client.Tests;

public sealed class PuzzleGenerationValidationTests
{
    private static readonly char[] PuzzleKeys = ['p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z'];

    public static IEnumerable<object[]> PuzzleDifficultyCases()
    {
        foreach (var key in PuzzleKeys)
        {
            yield return new object[] { key, MazeDifficulty.Easy };
            yield return new object[] { key, MazeDifficulty.Medium };
            yield return new object[] { key, MazeDifficulty.Hard };
        }
    }

    [Theory]
    [MemberData(nameof(PuzzleDifficultyCases))]
    public void GeneratorProducesSolverReplayablePuzzle(char puzzleKey, MazeDifficulty difficulty)
    {
        for (var iteration = 0; iteration < 16; iteration++)
        {
            var seed = $"seed-{difficulty}-{puzzleKey}-{iteration}";
            var room = CreateRoom(puzzleKey);
            var puzzle = PuzzleFactory.Create(seed, room, difficulty, runNonce: $"nonce-{iteration}");
            var panelPuzzle = Assert.IsAssignableFrom<ISoloPanelPuzzle>(puzzle);
            panelPuzzle.EnsureTierLevel(solvedRoomsBeforeCurrent: 0, totalPuzzleRooms: PuzzleKeys.Length);
            var solverBacked = Assert.IsAssignableFrom<ISolverBackedPanelPuzzle>(puzzle);
            Assert.True(solverBacked.TryBuildPanelSolveTrace(out var trace), "Panel puzzle did not provide a solve trace.");
            Assert.True(trace.Steps.Count > 0, "Panel solve trace was empty.");

            var solveCount = ReplayPanelSolveTrace(puzzle, panelPuzzle, trace);
            Assert.True(puzzle.IsCompleted, "Panel puzzle did not complete after replaying solve trace.");
            Assert.Equal(1, solveCount);
        }
    }

    [Theory]
    [MemberData(nameof(PuzzleDifficultyCases))]
    public void SeedAndRunNonceProvideStableLayout(char puzzleKey, MazeDifficulty difficulty)
    {
        var room = CreateRoom(puzzleKey);
        var seed = $"seed-{difficulty}-{puzzleKey}-0";

        var a1 = PuzzleFactory.Create(seed, room, difficulty, runNonce: "run-a");
        var a2 = PuzzleFactory.Create(seed, room, difficulty, runNonce: "run-a");
        var b1 = PuzzleFactory.Create(seed, room, difficulty, runNonce: "run-b");

        Assert.Equal(a1.GetType(), a2.GetType());
        Assert.Equal(a1.GetType(), b1.GetType());

        string sigA1;
        string sigA2;
        string sigB1;

        var panelA1 = Assert.IsAssignableFrom<ISoloPanelPuzzle>(a1);
        var panelA2 = Assert.IsAssignableFrom<ISoloPanelPuzzle>(a2);
        var panelB1 = Assert.IsAssignableFrom<ISoloPanelPuzzle>(b1);

        panelA1.EnsureTierLevel(solvedRoomsBeforeCurrent: 0, totalPuzzleRooms: PuzzleKeys.Length);
        panelA2.EnsureTierLevel(solvedRoomsBeforeCurrent: 0, totalPuzzleRooms: PuzzleKeys.Length);
        panelB1.EnsureTierLevel(solvedRoomsBeforeCurrent: 0, totalPuzzleRooms: PuzzleKeys.Length);

        Assert.Equal(panelA1.FamilyId, panelA2.FamilyId);
        Assert.Equal(panelA1.FamilyId, panelB1.FamilyId);

        sigA1 = BuildPanelLayoutSignature(panelA1.BuildPanelView(0d));
        sigA2 = BuildPanelLayoutSignature(panelA2.BuildPanelView(0d));
        sigB1 = BuildPanelLayoutSignature(panelB1.BuildPanelView(0d));

        Assert.Equal(sigA1, sigA2);
        Assert.Equal(sigA1, sigB1);
    }

    [Theory]
    [MemberData(nameof(PuzzleDifficultyCases))]
    public void DifferentRunNonceChangesSolutionInstance(char puzzleKey, MazeDifficulty difficulty)
    {
        var room = CreateRoom(puzzleKey);
        var seed = $"solution-variance-seed-{difficulty}";
        var traceFingerprints = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < 8; index++)
        {
            var puzzle = PuzzleFactory.Create(seed, room, difficulty, runNonce: $"run-{index}");
            var panelPuzzle = Assert.IsAssignableFrom<ISoloPanelPuzzle>(puzzle);
            panelPuzzle.EnsureTierLevel(solvedRoomsBeforeCurrent: 0, totalPuzzleRooms: PuzzleKeys.Length);
            var solverBacked = Assert.IsAssignableFrom<ISolverBackedPanelPuzzle>(puzzle);
            Assert.True(solverBacked.TryBuildPanelSolveTrace(out var trace));

            traceFingerprints.Add(BuildTraceFingerprint(trace));
        }

        var requiredDistinctFingerprints = 2;
        Assert.True(traceFingerprints.Count >= requiredDistinctFingerprints, "Run nonce did not produce solution variation.");
    }

    [Fact]
    public void PuzzleFamilyRegistryContainsDirectPToZMapping()
    {
        var expectedFamilies = new Dictionary<char, string>
        {
            ['p'] = "chromatic_lock",
            ['q'] = "signal_decay",
            ['r'] = "dead_reckoning",
            ['s'] = "pressure_grid",
            ['t'] = "cipher_wheel",
            ['u'] = "gravity_well",
            ['v'] = "echo_chamber",
            ['w'] = "token_flood",
            ['x'] = "memory_palace",
            ['y'] = "fault_line",
            ['z'] = "temporal_grid",
        };

        foreach (var (key, familyId) in expectedFamilies)
        {
            var puzzle = PuzzleFactory.Create("registry-seed", CreateRoom(key), MazeDifficulty.Easy, runNonce: "registry-run");
            var panelPuzzle = Assert.IsAssignableFrom<ISoloPanelPuzzle>(puzzle);
            panelPuzzle.EnsureTierLevel(solvedRoomsBeforeCurrent: 0, totalPuzzleRooms: PuzzleKeys.Length);
            Assert.Equal(familyId, panelPuzzle.FamilyId);
        }
    }

    private static MazeRoomDefinition CreateRoom(char puzzleKey) => new()
    {
        Coordinates = new GridPoint(1, 1),
        ConnectionKey = 'O',
        PuzzleKey = puzzleKey,
        Kind = MazeRoomKind.Normal,
        Connections = new RoomConnections(true, true, true, true),
    };

    private static int ReplayPanelSolveTrace(RoomPuzzle puzzle, ISoloPanelPuzzle panelPuzzle, PuzzleSolveTrace trace)
    {
        var solvedTransitions = 0;
        var wasSolved = puzzle.IsCompleted;
        var nowSeconds = 0d;
        var playerBounds = new PlayAreaRect(510d, 510d, 60d, 60d);

        foreach (var step in trace.Steps)
        {
            var advance = Math.Max(0d, step.AdvanceSeconds);
            if (advance > 0d)
            {
                AdvanceTime(puzzle, ref nowSeconds, advance, playerBounds);
            }

            if (!string.IsNullOrWhiteSpace(step.InteractableId))
            {
                _ = panelPuzzle.ApplyAction(step.InteractableId!, nowSeconds);
            }

            if (puzzle.IsCompleted && !wasSolved)
            {
                solvedTransitions++;
                wasSolved = true;
            }
        }

        AdvanceTime(puzzle, ref nowSeconds, 0.5d, playerBounds);
        if (puzzle.IsCompleted && !wasSolved)
        {
            solvedTransitions++;
        }

        return solvedTransitions;
    }

    private static void AdvanceTime(RoomPuzzle puzzle, ref double nowSeconds, double seconds, PlayAreaRect playerBounds)
    {
        var remaining = Math.Max(0d, seconds);
        while (remaining > 0.0001d)
        {
            var delta = Math.Min(0.05d, remaining);
            nowSeconds += delta;
            remaining -= delta;
            puzzle.Update(new PuzzleUpdateContext
            {
                PlayerBounds = playerBounds,
                DeltaTimeSeconds = delta,
                NowSeconds = nowSeconds,
                PlayerFacing = PlayerDirection.Down,
            });
        }
    }

    private static string BuildTraceFingerprint(PuzzleSolveTrace trace)
    {
        var builder = new StringBuilder(trace.Steps.Count * 24);
        foreach (var step in trace.Steps)
        {
            builder
                .Append(step.InteractableId ?? "_")
                .Append('#')
                .Append(Math.Round(step.AdvanceSeconds, 3).ToString("0.000", CultureInfo.InvariantCulture))
                .Append('#')
                .Append((int)step.Source)
                .Append('|');
        }

        return builder.ToString();
    }

    private static string BuildPanelLayoutSignature(SoloPanelView view)
    {
        var builder = new StringBuilder(512);
        builder.Append(view.FamilyId)
            .Append('|')
            .Append(view.TierLevel)
            .Append('|');

        foreach (var action in view.Actions
                     .OrderBy(action => action.Command, StringComparer.Ordinal))
        {
            builder.Append(action.Command).Append(';');
        }

        builder.Append('|');
        foreach (var entry in view.Board
                     .Where(entry => entry.Key.StartsWith("tgt:", StringComparison.Ordinal) ||
                                     string.Equals(entry.Key, "family", StringComparison.Ordinal) ||
                                     string.Equals(entry.Key, "tier", StringComparison.Ordinal))
                     .OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            builder.Append(entry.Key).Append('=').Append(entry.Value).Append(';');
        }

        return builder.ToString();
    }
}
