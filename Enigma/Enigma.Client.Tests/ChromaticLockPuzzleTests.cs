using System.Globalization;
using System.Linq;
using Enigma.Client.Models.Gameplay;
using Xunit;

namespace Enigma.Client.Tests;

public sealed class ChromaticLockPuzzleTests
{
    [Theory]
    [InlineData(MazeDifficulty.Easy, 2, 0, 1)]
    [InlineData(MazeDifficulty.Medium, 3, 150, 6)]
    [InlineData(MazeDifficulty.Hard, 5, 300, 1)]
    public void ChromaticLockViewIncludesExpectedBoardContractAndSetCommands(MazeDifficulty difficulty, int expectedRounds, int expectedHoldTicks, int expectedHueStep)
    {
        var (_, panelPuzzle) = CreateChromaticLockPuzzle(difficulty);
        var view = panelPuzzle.BuildPanelView(0d);

        Assert.Equal("chromatic_lock", view.FamilyId);
        Assert.Contains("chromatic:channel:h:current", view.Board.Keys);
        Assert.Contains("chromatic:channel:s:current", view.Board.Keys);
        Assert.Contains("chromatic:channel:l:current", view.Board.Keys);
        Assert.Contains("chromatic:channel:h:target", view.Board.Keys);
        Assert.Contains("chromatic:match_percent", view.Board.Keys);
        Assert.Contains("chromatic:hold_required", view.Board.Keys);
        Assert.Contains("chromatic:lockout_active", view.Board.Keys);
        Assert.Equal(expectedRounds.ToString(CultureInfo.InvariantCulture), view.Board["chromatic:round_total"]);
        Assert.Equal(expectedHoldTicks.ToString(CultureInfo.InvariantCulture), view.Board["chromatic:hold_required"]);
        Assert.Equal(expectedHueStep.ToString(CultureInfo.InvariantCulture), view.Board["chromatic:channel:h:step"]);
        Assert.DoesNotContain(view.Actions, action => action.Command.StartsWith("dial:", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(view.Actions, action => action.Command == "chroma:commit");
        Assert.Contains(view.Actions, action => action.Command == "chroma:reset");
        Assert.Contains(view.Actions, action => action.Command == "chroma:hint");

        Assert.True(panelPuzzle.ApplyAction("chroma:set:h:120", 0d));
        Assert.True(panelPuzzle.ApplyAction("chroma:set:s:64", 0d));
        Assert.True(panelPuzzle.ApplyAction("chroma:set:l:38", 0d));

        var mutated = panelPuzzle.BuildPanelView(0d);
        Assert.Equal("120", mutated.Board["chromatic:channel:h:current"]);
        Assert.Equal("64", mutated.Board["chromatic:channel:s:current"]);
        Assert.Equal("38", mutated.Board["chromatic:channel:l:current"]);
    }

    [Fact]
    public void ChromaticLockSameSeedAndRunNonceProduceStableTargets()
    {
        var (_, first) = CreateChromaticLockPuzzle(MazeDifficulty.Medium, "stable-seed", "stable-run");
        var (_, second) = CreateChromaticLockPuzzle(MazeDifficulty.Medium, "stable-seed", "stable-run");

        var firstView = first.BuildPanelView(0d);
        var secondView = second.BuildPanelView(0d);

        Assert.Equal(firstView.Board["chromatic:round_total"], secondView.Board["chromatic:round_total"]);
        Assert.Equal(firstView.Board["chromatic:round:1:h"], secondView.Board["chromatic:round:1:h"]);
        Assert.Equal(firstView.Board["chromatic:round:1:s"], secondView.Board["chromatic:round:1:s"]);
        Assert.Equal(firstView.Board["chromatic:round:1:l"], secondView.Board["chromatic:round:1:l"]);
        Assert.Equal(firstView.Board["chromatic:channel:h:target"], secondView.Board["chromatic:channel:h:target"]);
        Assert.Equal(firstView.Board["chromatic:channel:s:target"], secondView.Board["chromatic:channel:s:target"]);
        Assert.Equal(firstView.Board["chromatic:channel:l:target"], secondView.Board["chromatic:channel:l:target"]);
    }

    [Fact]
    public void ChromaticLockRoundsRequireHoldAndCommitToSolve()
    {
        var (puzzle, panelPuzzle) = CreateChromaticLockPuzzle(MazeDifficulty.Medium);
        var solverBacked = Assert.IsAssignableFrom<ISolverBackedPanelPuzzle>(puzzle);
        Assert.True(solverBacked.TryBuildPanelSolveTrace(out var trace));

        var nowSeconds = 0d;
        var firstCommitIndex = trace.Steps
            .Select((step, index) => new { step, index })
            .First(entry => string.Equals(entry.step.InteractableId, "chroma:commit", StringComparison.Ordinal))
            .index;
        Assert.True(firstCommitIndex > 0);

        foreach (var step in trace.Steps.Take(firstCommitIndex - 1))
        {
            Assert.True(panelPuzzle.ApplyAction(step.InteractableId!, nowSeconds));
        }

        var preHoldView = panelPuzzle.BuildPanelView(nowSeconds);
        Assert.Equal("0", preHoldView.Board["chromatic:ready"]);

        var holdStep = trace.Steps[firstCommitIndex - 1];
        Assert.Null(holdStep.InteractableId);
        AdvanceTime(puzzle, holdStep.AdvanceSeconds, ref nowSeconds);

        var readyView = panelPuzzle.BuildPanelView(nowSeconds);
        Assert.Equal("1", readyView.Board["chromatic:ready"]);

        Assert.True(panelPuzzle.ApplyAction("chroma:commit", nowSeconds));
        Assert.False(puzzle.IsCompleted);

        foreach (var step in trace.Steps.Skip(firstCommitIndex + 1))
        {
            var advance = Math.Max(0d, step.AdvanceSeconds);
            if (advance > 0d)
            {
                AdvanceTime(puzzle, advance, ref nowSeconds);
            }

            if (!string.IsNullOrWhiteSpace(step.InteractableId))
            {
                Assert.True(panelPuzzle.ApplyAction(step.InteractableId!, nowSeconds));
            }
        }

        Assert.True(puzzle.IsCompleted);
    }

    [Fact]
    public void EasyChromaticLockCanCommitWithoutHoldDelay()
    {
        var (puzzle, panelPuzzle) = CreateChromaticLockPuzzle(MazeDifficulty.Easy);
        var targetView = panelPuzzle.BuildPanelView(0d);

        Assert.True(panelPuzzle.ApplyAction($"chroma:set:h:{targetView.Board["chromatic:channel:h:target"]}", 0d));
        Assert.True(panelPuzzle.ApplyAction($"chroma:set:s:{targetView.Board["chromatic:channel:s:target"]}", 0d));
        Assert.True(panelPuzzle.ApplyAction($"chroma:set:l:{targetView.Board["chromatic:channel:l:target"]}", 0d));

        var alignedView = panelPuzzle.BuildPanelView(0d);
        Assert.Equal("1", alignedView.Board["chromatic:ready"]);

        Assert.True(panelPuzzle.ApplyAction("chroma:commit", 0d));
        Assert.False(puzzle.IsCompleted);
    }

    [Fact]
    public void EasyChromaticLockRejectsNearMatchInsideTolerance()
    {
        var (_, panelPuzzle) = CreateChromaticLockPuzzle(MazeDifficulty.Easy);
        var targetView = panelPuzzle.BuildPanelView(0d);
        var nearHue = (int.Parse(targetView.Board["chromatic:channel:h:target"], CultureInfo.InvariantCulture) + 1) % 360;

        Assert.True(panelPuzzle.ApplyAction($"chroma:set:h:{nearHue.ToString(CultureInfo.InvariantCulture)}", 0d));
        Assert.True(panelPuzzle.ApplyAction($"chroma:set:s:{targetView.Board["chromatic:channel:s:target"]}", 0d));
        Assert.True(panelPuzzle.ApplyAction($"chroma:set:l:{targetView.Board["chromatic:channel:l:target"]}", 0d));

        var alignedView = panelPuzzle.BuildPanelView(0d);
        Assert.Equal("0", alignedView.Board["chromatic:ready"]);
    }

    [Fact]
    public void HardChromaticLockSaturationAdjustmentPerturbsLightness()
    {
        var (_, panelPuzzle) = CreateChromaticLockPuzzle(MazeDifficulty.Hard);
        var initialView = panelPuzzle.BuildPanelView(0d);
        var initialLightness = initialView.Board["chromatic:channel:l:current"];
        var nextSaturation = (int.Parse(initialView.Board["chromatic:channel:s:current"], CultureInfo.InvariantCulture) + 20).ToString(CultureInfo.InvariantCulture);

        Assert.True(panelPuzzle.ApplyAction($"chroma:set:s:{nextSaturation}", 0d));
        var updatedView = panelPuzzle.BuildPanelView(0d);

        Assert.NotEqual(initialLightness, updatedView.Board["chromatic:channel:l:current"]);
    }

    [Fact]
    public void HardChromaticLockTargetIsStaticAndSolverTraceReplays()
    {
        var (puzzle, panelPuzzle) = CreateChromaticLockPuzzle(MazeDifficulty.Hard);
        var nowSeconds = 0d;
        var initialView = panelPuzzle.BuildPanelView(nowSeconds);
        var initialTargetHue = initialView.Board["chromatic:channel:h:target"];

        AdvanceTime(puzzle, 0.6d, ref nowSeconds);
        var movedView = panelPuzzle.BuildPanelView(nowSeconds);
        Assert.Equal(initialTargetHue, movedView.Board["chromatic:channel:h:target"]);

        var (replayPuzzle, replayPanel) = CreateChromaticLockPuzzle(MazeDifficulty.Hard, "hard-replay-seed", "hard-replay-run");
        var replaySolver = Assert.IsAssignableFrom<ISolverBackedPanelPuzzle>(replayPuzzle);
        Assert.True(replaySolver.TryBuildPanelSolveTrace(out var trace));

        var replayNow = 0d;
        foreach (var step in trace.Steps)
        {
            if (step.AdvanceSeconds > 0d)
            {
                AdvanceTime(replayPuzzle, step.AdvanceSeconds, ref replayNow);
            }

            if (!string.IsNullOrWhiteSpace(step.InteractableId))
            {
                Assert.True(replayPanel.ApplyAction(step.InteractableId!, replayNow));
            }
        }

        Assert.True(replayPuzzle.IsCompleted);
    }

    private static (RoomPuzzle puzzle, ISoloPanelPuzzle panelPuzzle) CreateChromaticLockPuzzle(
        MazeDifficulty difficulty,
        string seed = "",
        string runNonce = "")
    {
        var room = new MazeRoomDefinition
        {
            Coordinates = new GridPoint(2, 2),
            ConnectionKey = 'O',
            PuzzleKey = 'p',
            Kind = MazeRoomKind.Normal,
            Connections = new RoomConnections(true, true, true, true),
        };

        var actualSeed = string.IsNullOrWhiteSpace(seed) ? $"chromatic-seed-{difficulty}" : seed;
        var actualRunNonce = string.IsNullOrWhiteSpace(runNonce) ? $"chromatic-run-{difficulty}" : runNonce;
        var puzzle = PuzzleFactory.Create(actualSeed, room, difficulty, actualRunNonce);
        var panelPuzzle = Assert.IsAssignableFrom<ISoloPanelPuzzle>(puzzle);
        panelPuzzle.EnsureTierLevel(0, 11);
        return (puzzle, panelPuzzle);
    }

    private static void AdvanceTime(RoomPuzzle puzzle, double seconds, ref double nowSeconds)
    {
        var remaining = seconds;
        while (remaining > 0.0001d)
        {
            var delta = Math.Min(0.05d, remaining);
            nowSeconds += delta;
            remaining -= delta;
            puzzle.Update(new PuzzleUpdateContext
            {
                PlayerBounds = new PlayAreaRect(510d, 510d, 60d, 60d),
                DeltaTimeSeconds = delta,
                NowSeconds = nowSeconds,
                PlayerFacing = PlayerDirection.Down,
            });
        }
    }
}
