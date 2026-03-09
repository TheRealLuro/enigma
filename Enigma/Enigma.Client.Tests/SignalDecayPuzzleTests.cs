using System.Globalization;
using System.Linq;
using Enigma.Client.Models.Gameplay;
using Xunit;

namespace Enigma.Client.Tests;

public sealed class SignalDecayPuzzleTests
{
    [Theory]
    [InlineData(MazeDifficulty.Easy, false, true)]
    [InlineData(MazeDifficulty.Medium, true, true)]
    [InlineData(MazeDifficulty.Hard, false, true)]
    public void SignalDecayViewIncludesExpectedBoardContract(MazeDifficulty difficulty, bool fftVisible, bool previewVisible)
    {
        var (puzzle, panelPuzzle) = CreateSignalDecayPuzzle(difficulty);
        var view = panelPuzzle.BuildPanelView(0d);

        Assert.Equal("signal_decay", view.FamilyId);
        Assert.Contains("signal:component_count", view.Board.Keys);
        Assert.Contains("signal:path_target", view.Board.Keys);
        Assert.Contains("signal:path_current", view.Board.Keys);
        Assert.Contains("signal:hold_required", view.Board.Keys);
        Assert.Contains("signal:hold_progress", view.Board.Keys);
        Assert.Contains("signal:coherence", view.Board.Keys);
        Assert.Contains("signal:timer_total", view.Board.Keys);
        Assert.Contains("signal:noise_level", view.Board.Keys);
        Assert.Contains("signal:cross_bleed_active", view.Board.Keys);
        Assert.Equal(fftVisible ? "1" : "0", view.Board["signal:fft_visible"]);
        Assert.Equal(previewVisible ? "1" : "0", view.Board["signal:preview_visible"]);
        Assert.Contains(view.Actions, action => action.Command == "signal:commit");
        Assert.Contains(view.Actions, action => action.Command == "signal:reset");
        Assert.Contains(view.Actions, action => action.Command == "signal:hint");
        Assert.NotEmpty(view.Board["signal:path_current"]);
        Assert.NotEmpty(view.Board["tgt:k1:type"]);
    }

    [Fact]
    public void SignalDecayTimeoutResetsToDeterministicBaselineAndRestartsTimer()
    {
        var (puzzle, panelPuzzle) = CreateSignalDecayPuzzle(MazeDifficulty.Easy);
        var initialView = panelPuzzle.BuildPanelView(0d);
        var initialAmplitude = initialView.Board["signal:k1:amp"];
        var updatedAmplitude = initialAmplitude == "0.90" ? "0.10" : "0.90";

        Assert.True(panelPuzzle.ApplyAction($"signal:set:k1:amp:{updatedAmplitude}", 0d));
        var mutatedView = panelPuzzle.BuildPanelView(0d);
        Assert.NotEqual(initialAmplitude, mutatedView.Board["signal:k1:amp"]);

        var timerTotal = double.Parse(initialView.Board["signal:timer_total"], CultureInfo.InvariantCulture);
        AdvanceTime(puzzle, timerTotal + 0.2d);

        var restartedView = panelPuzzle.BuildPanelView(timerTotal + 0.2d);
        Assert.Equal("sync_collapse", restartedView.FailureCode);
        Assert.Equal("Sync Collapse", restartedView.FailureLabel);
        Assert.Equal(initialAmplitude, restartedView.Board["signal:k1:amp"]);
        Assert.True(restartedView.TimerRemainingSeconds > timerTotal - 1.0d);
    }

    [Fact]
    public void MediumSignalDecayPreviewFadesAfterIntroWindow()
    {
        var (puzzle, panelPuzzle) = CreateSignalDecayPuzzle(MazeDifficulty.Medium);
        var initialView = panelPuzzle.BuildPanelView(0d);
        var previewSeconds = double.Parse(initialView.Board["signal:preview_seconds"], CultureInfo.InvariantCulture);

        Assert.True(previewSeconds > 0d);
        Assert.Equal("1", initialView.Board["signal:preview_visible"]);

        AdvanceTime(puzzle, previewSeconds + 0.2d);
        var fadedView = panelPuzzle.BuildPanelView(previewSeconds + 0.2d);
        Assert.Equal("0", fadedView.Board["signal:preview_visible"]);
    }

    [Fact]
    public void HardSignalDecayPreviewFadesAndReturnsAfterReset()
    {
        var (puzzle, panelPuzzle) = CreateSignalDecayPuzzle(MazeDifficulty.Hard);
        var initialView = panelPuzzle.BuildPanelView(0d);
        var previewSeconds = double.Parse(initialView.Board["signal:preview_seconds"], CultureInfo.InvariantCulture);

        Assert.Equal("1", initialView.Board["signal:preview_visible"]);
        AdvanceTime(puzzle, previewSeconds + 0.2d);
        var fadedView = panelPuzzle.BuildPanelView(previewSeconds + 0.2d);
        Assert.Equal("0", fadedView.Board["signal:preview_visible"]);

        Assert.True(panelPuzzle.ApplyAction("signal:reset", previewSeconds + 0.3d));
        var resetView = panelPuzzle.BuildPanelView(previewSeconds + 0.3d);
        Assert.Equal("1", resetView.Board["signal:preview_visible"]);
    }

    [Fact]
    public void HardSignalDecayCrossChannelBleedPerturbsNeighbor()
    {
        var (_, panelPuzzle) = CreateSignalDecayPuzzle(MazeDifficulty.Hard);
        var initialView = panelPuzzle.BuildPanelView(0d);
        var initialNeighborPhase = initialView.Board["signal:k2:phase"];

        Assert.True(panelPuzzle.ApplyAction("signal:set:k1:phase:45", 0d));
        var updatedView = panelPuzzle.BuildPanelView(0d);

        Assert.Equal("1", updatedView.Board["signal:cross_bleed_active"]);
        Assert.NotEqual(initialNeighborPhase, updatedView.Board["signal:k2:phase"]);
    }

    [Fact]
    public void SignalDecayRequiresHoldBeforeCommit()
    {
        var (puzzle, panelPuzzle) = CreateSignalDecayPuzzle(MazeDifficulty.Medium);
        var solverBacked = Assert.IsAssignableFrom<ISolverBackedPanelPuzzle>(puzzle);
        Assert.True(solverBacked.TryBuildPanelSolveTrace(out var trace));

        var initialCoherence = panelPuzzle.BuildPanelView(0d).ProgressValue;
        var nowSeconds = 0d;

        foreach (var step in trace.Steps.Where(step => !string.IsNullOrWhiteSpace(step.InteractableId) && step.InteractableId != "signal:commit"))
        {
            Assert.True(panelPuzzle.ApplyAction(step.InteractableId!, nowSeconds));
        }

        var alignedView = panelPuzzle.BuildPanelView(nowSeconds);
        Assert.True(alignedView.ProgressValue > initialCoherence);
        Assert.Equal("0", alignedView.Board["signal:ready"]);

        var holdStep = Assert.Single(trace.Steps, step => string.IsNullOrWhiteSpace(step.InteractableId));
        AdvanceTime(puzzle, holdStep.AdvanceSeconds, ref nowSeconds);
        var readyView = panelPuzzle.BuildPanelView(nowSeconds);
        Assert.Equal("1", readyView.Board["signal:ready"]);

        Assert.True(panelPuzzle.ApplyAction("signal:commit", nowSeconds));
        Assert.True(puzzle.IsCompleted);
    }

    [Fact]
    public void SignalDecayRejectsNearWaveformUntilExactTargetsMatch()
    {
        var (puzzle, panelPuzzle) = CreateSignalDecayPuzzle(MazeDifficulty.Medium);
        var initialView = panelPuzzle.BuildPanelView(0d);
        var componentCount = int.Parse(initialView.Board["signal:component_count"], CultureInfo.InvariantCulture);

        for (var index = 1; index <= componentCount; index++)
        {
            var key = $"k{index}";
            Assert.True(panelPuzzle.ApplyAction($"signal:set:{key}:type:{initialView.Board[$"tgt:{key}:type"]}", 0d));
            Assert.True(panelPuzzle.ApplyAction($"signal:set:{key}:freq:{initialView.Board[$"tgt:{key}:freq"]}", 0d));
            Assert.True(panelPuzzle.ApplyAction($"signal:set:{key}:phase:{initialView.Board[$"tgt:{key}:phase"]}", 0d));

            var targetAmplitude = decimal.Parse(initialView.Board[$"tgt:{key}:amp"], CultureInfo.InvariantCulture);
            if (index == 1)
            {
                targetAmplitude = targetAmplitude >= 0.90m ? targetAmplitude - 0.05m : targetAmplitude + 0.05m;
            }

            Assert.True(panelPuzzle.ApplyAction(
                $"signal:set:{key}:amp:{targetAmplitude.ToString("0.00", CultureInfo.InvariantCulture)}",
                0d));
        }

        AdvanceTime(puzzle, 3.0d);
        var nearView = panelPuzzle.BuildPanelView(3.0d);
        Assert.Equal("0", nearView.Board["signal:ready"]);
    }

    private static (RoomPuzzle puzzle, ISoloPanelPuzzle panelPuzzle) CreateSignalDecayPuzzle(MazeDifficulty difficulty)
    {
        var room = new MazeRoomDefinition
        {
            Coordinates = new GridPoint(2, 2),
            ConnectionKey = 'O',
            PuzzleKey = 'q',
            Kind = MazeRoomKind.Normal,
            Connections = new RoomConnections(true, true, true, true),
        };

        var puzzle = PuzzleFactory.Create($"signal-seed-{difficulty}", room, difficulty, runNonce: $"signal-run-{difficulty}");
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

    private static void AdvanceTime(RoomPuzzle puzzle, double seconds)
    {
        var nowSeconds = 0d;
        AdvanceTime(puzzle, seconds, ref nowSeconds);
    }
}
