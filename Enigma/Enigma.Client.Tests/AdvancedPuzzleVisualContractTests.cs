using System.Globalization;
using System.Linq;
using Enigma.Client.Models.Gameplay;
using Xunit;

namespace Enigma.Client.Tests;

public sealed class AdvancedPuzzleVisualContractTests
{
    public static IEnumerable<object[]> SystemFamilyCases()
    {
        foreach (var key in new[] { 'r', 'u', 'v', 'y' })
        {
            yield return new object[] { key, MazeDifficulty.Easy };
            yield return new object[] { key, MazeDifficulty.Medium };
            yield return new object[] { key, MazeDifficulty.Hard };
        }
    }

    public static IEnumerable<object[]> GridFamilyCases()
    {
        foreach (var key in new[] { 's', 'z' })
        {
            yield return new object[] { key, MazeDifficulty.Easy };
            yield return new object[] { key, MazeDifficulty.Medium };
            yield return new object[] { key, MazeDifficulty.Hard };
        }
    }

    [Theory]
    [InlineData(MazeDifficulty.Easy)]
    [InlineData(MazeDifficulty.Medium)]
    [InlineData(MazeDifficulty.Hard)]
    public void CipherWheelRemainsDialDrivenButDifficultyHintsScale(MazeDifficulty difficulty)
    {
        var (_, panelPuzzle) = CreatePuzzle('t', difficulty);
        var view = panelPuzzle.BuildPanelView(0d);

        Assert.Contains("dialviz:variant", view.Board.Keys);
        Assert.Contains("cipher_wheel:encoded_fragment", view.Board.Keys);
        Assert.Contains(view.Actions, action => action.Command.StartsWith("dial:", StringComparison.OrdinalIgnoreCase));

        if (difficulty == MazeDifficulty.Medium)
        {
            Assert.StartsWith("SHIFT +", view.Board["cipher_wheel:rule_hint"], StringComparison.Ordinal);
        }
        else if (difficulty == MazeDifficulty.Hard)
        {
            Assert.Equal("masked", view.Board["cipher_wheel:mask_state"]);
            Assert.DoesNotContain("tgt:k1", view.Board.Keys);
            Assert.False(string.IsNullOrWhiteSpace(view.Board["cipher_wheel:semantic_hint"]));
        }
    }

    [Theory]
    [MemberData(nameof(SystemFamilyCases))]
    public void CoupledSystemFamiliesExposeSystemMetadataAcrossDifficulties(char puzzleKey, MazeDifficulty difficulty)
    {
        var (_, panelPuzzle) = CreatePuzzle(puzzleKey, difficulty);
        var view = panelPuzzle.BuildPanelView(0d);
        var prefix = puzzleKey switch
        {
            'r' => "reckon",
            'u' => "gravity",
            'v' => "echo",
            'y' => "fault",
            _ => throw new ArgumentOutOfRangeException(nameof(puzzleKey)),
        };

        Assert.Contains("system:variant", view.Board.Keys);
        Assert.Contains("system:headline", view.Board.Keys);
        Assert.Contains("system:description", view.Board.Keys);
        Assert.Contains("system:control_count", view.Board.Keys);
        Assert.Contains("system:metric_count", view.Board.Keys);
        Assert.Contains("system:control:k1:label", view.Board.Keys);
        Assert.Contains("system:metric:", string.Join('|', view.Board.Keys.Where(key => key.StartsWith("system:metric:", StringComparison.OrdinalIgnoreCase))));
        Assert.Contains(view.Actions, action => action.Command == $"{prefix}:commit");
        Assert.Contains(view.Actions, action => action.Command == $"{prefix}:reset");
        Assert.Contains(view.Actions, action => action.Command == $"{prefix}:hint");

        var targetMode = view.Board["system:target_mode"];
        Assert.Equal(
            difficulty == MazeDifficulty.Easy ? "direct" :
            difficulty == MazeDifficulty.Medium ? "band" :
            "aggregate",
            targetMode);

        if (difficulty == MazeDifficulty.Hard)
        {
            Assert.Equal("concealed", view.Board["visual:target_visibility"]);
        }
    }

    [Theory]
    [MemberData(nameof(SystemFamilyCases))]
    public void CoupledSystemFamiliesRequireExactControlLock(char puzzleKey, MazeDifficulty difficulty)
    {
        var (puzzle, panelPuzzle) = CreatePuzzle(puzzleKey, difficulty);
        var solver = Assert.IsAssignableFrom<ISolverBackedPanelPuzzle>(puzzle);
        Assert.True(solver.TryBuildPanelSolveTrace(out var trace));

        var commands = trace.Steps
            .Where(step => !string.IsNullOrWhiteSpace(step.InteractableId) &&
                           !step.InteractableId!.EndsWith(":commit", StringComparison.OrdinalIgnoreCase))
            .Select(step => step.InteractableId!)
            .ToArray();

        foreach (var command in commands)
        {
            Assert.True(panelPuzzle.ApplyAction(command, 0d));
        }

        var readyView = panelPuzzle.BuildPanelView(0d);
        Assert.Equal("1", readyView.Board["system:ready"]);

        var prefix = readyView.Board["system:command_prefix"];
        var step = int.Parse(readyView.Board["system:input_step"], CultureInfo.InvariantCulture);
        var disturbed = Math.Clamp(int.Parse(readyView.Board["system:control:k1:value"], CultureInfo.InvariantCulture) - step, 0, 100);
        if (disturbed == int.Parse(readyView.Board["system:control:k1:value"], CultureInfo.InvariantCulture))
        {
            disturbed = Math.Clamp(disturbed + step, 0, 100);
        }

        Assert.True(panelPuzzle.ApplyAction($"{prefix}:set:k1:{disturbed.ToString(CultureInfo.InvariantCulture)}", 0d));
        var disturbedView = panelPuzzle.BuildPanelView(0d);
        Assert.Equal("0", disturbedView.Board["system:ready"]);
    }

    [Theory]
    [MemberData(nameof(GridFamilyCases))]
    public void GridFamiliesExposeDifficultySpecificVisibilityAndRules(char puzzleKey, MazeDifficulty difficulty)
    {
        var (_, panelPuzzle) = CreatePuzzle(puzzleKey, difficulty);
        var view = panelPuzzle.BuildPanelView(0d);
        var firstCellAction = Assert.Single(view.Actions.Where(action => action.Command.StartsWith("cell:", StringComparison.OrdinalIgnoreCase)).Take(1));
        var key = firstCellAction.Command[5..];

        Assert.Contains("gridviz:variant", view.Board.Keys);
        Assert.Contains("gridviz:page_rows", view.Board.Keys);
        Assert.Contains("gridviz:page_cols", view.Board.Keys);
        Assert.Contains($"gridviz:{key}:row", view.Board.Keys);
        Assert.Contains($"gridviz:{key}:col", view.Board.Keys);

        if (puzzleKey == 's')
        {
            Assert.Equal(
                difficulty == MazeDifficulty.Easy ? "visible" :
                difficulty == MazeDifficulty.Medium ? "partial" :
                "concealed",
                view.Board["visual:target_visibility"]);
            if (difficulty == MazeDifficulty.Hard)
            {
                Assert.Contains("self", view.Board["gridviz:directive"], StringComparison.OrdinalIgnoreCase);
            }
        }
        else
        {
            Assert.Equal(
                difficulty == MazeDifficulty.Easy ? "visible" :
                difficulty == MazeDifficulty.Medium ? "partial" :
                "concealed",
                view.Board["visual:target_visibility"]);
            if (difficulty == MazeDifficulty.Hard)
            {
                Assert.Equal("linked", view.Board["temporal_grid:sync_mode"]);
            }
        }
    }

    [Fact]
    public void HardPressureGridUsesSelfUpLeftKernel()
    {
        var (_, panelPuzzle) = CreatePuzzle('s', MazeDifficulty.Hard);
        var before = panelPuzzle.BuildPanelView(0d);

        var keys = new[] { "c6", "c2", "c5" };
        var previous = keys.ToDictionary(key => key, key => before.Board[$"cur:{key}"], StringComparer.Ordinal);

        Assert.True(panelPuzzle.ApplyAction("cell:c6", 0d));
        var after = panelPuzzle.BuildPanelView(0d);

        foreach (var key in keys)
        {
            Assert.NotEqual(previous[key], after.Board[$"cur:{key}"]);
        }
    }

    [Fact]
    public void HardTemporalGridPageTwoToggleAlsoFlipsPageOne()
    {
        var (_, panelPuzzle) = CreatePuzzle('z', MazeDifficulty.Hard);
        var before = panelPuzzle.BuildPanelView(0d);
        var keyPageTwo = "c10";
        var keyPageOne = "c1";

        Assert.True(panelPuzzle.ApplyAction("page:next", 0d));
        var pageTwoBefore = panelPuzzle.BuildPanelView(0d);

        Assert.True(panelPuzzle.ApplyAction($"cell:{keyPageTwo}", 0d));
        var pageTwoAfter = panelPuzzle.BuildPanelView(0d);
        Assert.True(panelPuzzle.ApplyAction("page:prev", 0d));
        var pageOneAfter = panelPuzzle.BuildPanelView(0d);

        Assert.NotEqual(pageTwoBefore.Board[$"cur:{keyPageTwo}"], pageTwoAfter.Board[$"cur:{keyPageTwo}"]);
        Assert.NotEqual(before.Board[$"cur:{keyPageOne}"], pageOneAfter.Board[$"cur:{keyPageOne}"]);
    }

    [Fact]
    public void FlowFamilyExposesPressureAndGateMetadata()
    {
        var (_, panelPuzzle) = CreatePuzzle('w', MazeDifficulty.Hard);
        var initialView = panelPuzzle.BuildPanelView(0d);

        Assert.Contains("flowviz:pressure_pct", initialView.Board.Keys);
        Assert.Contains("flowviz:linked", initialView.Board.Keys);
        Assert.Contains("flowviz:r0c0:pressure", initialView.Board.Keys);
        Assert.Contains("flowviz:r0c0:gate", initialView.Board.Keys);

        var candidateAction = initialView.Actions
            .Where(action => action.Command.StartsWith("pipe:", StringComparison.OrdinalIgnoreCase))
            .Select(action => new
            {
                action,
                key = action.Command[5..],
                mask = int.Parse(initialView.Board[$"mask:{action.Command[5..]}"], CultureInfo.InvariantCulture),
            })
            .First(entry => entry.mask is not 0 and not 15);

        var initialRotatedMask = initialView.Board[$"flowviz:{candidateAction.key}:rotated_mask"];
        Assert.True(panelPuzzle.ApplyAction(candidateAction.action.Command, 0d));

        var updatedView = panelPuzzle.BuildPanelView(0d);
        Assert.NotEqual(initialRotatedMask, updatedView.Board[$"flowviz:{candidateAction.key}:rotated_mask"]);
    }

    [Fact]
    public void HardMemoryPalaceMasksFragmentsUntilCardsOpen()
    {
        var (_, panelPuzzle) = CreatePuzzle('x', MazeDifficulty.Hard);
        var initialView = panelPuzzle.BuildPanelView(0d);
        var cardCount = int.Parse(initialView.Board["memoryviz:card_count"], CultureInfo.InvariantCulture);
        var firstHidden = Enumerable.Range(0, cardCount)
            .First(index => initialView.Board[$"memoryviz:card:{index}:state"] == "hidden");

        Assert.Contains("??", initialView.Board[$"memoryviz:card:{firstHidden}:fragment"], StringComparison.Ordinal);

        Assert.True(panelPuzzle.ApplyAction($"card:{firstHidden}", 0d));
        var openView = panelPuzzle.BuildPanelView(0d);
        Assert.Equal("open", openView.Board[$"memoryviz:card:{firstHidden}:state"]);
        Assert.DoesNotContain("??", openView.Board[$"memoryviz:card:{firstHidden}:fragment"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData('r', MazeDifficulty.Medium, "system:headline")]
    [InlineData('s', MazeDifficulty.Medium, "gridviz:page_label")]
    [InlineData('w', MazeDifficulty.Medium, "flowviz:pressure_pct")]
    [InlineData('x', MazeDifficulty.Medium, "memory_palace:archive_sector")]
    public void VisualMetadataIsStableForSameSeedAndRunNonce(char puzzleKey, MazeDifficulty difficulty, string metadataKey)
    {
        var (_, first) = CreatePuzzle(puzzleKey, difficulty, "visual-stable-seed", "visual-stable-run");
        var (_, second) = CreatePuzzle(puzzleKey, difficulty, "visual-stable-seed", "visual-stable-run");

        var firstView = first.BuildPanelView(0d);
        var secondView = second.BuildPanelView(0d);

        Assert.Equal(firstView.Board[metadataKey], secondView.Board[metadataKey]);
        Assert.Equal(firstView.Board["visual:difficulty"], secondView.Board["visual:difficulty"]);
    }

    [Theory]
    [InlineData('p')]
    [InlineData('q')]
    [InlineData('r')]
    [InlineData('s')]
    [InlineData('w')]
    [InlineData('x')]
    public void HardMutatorMetadataIsDeterministicForSeedAndRunNonce(char puzzleKey)
    {
        var (_, first) = CreatePuzzle(puzzleKey, MazeDifficulty.Hard, "mutator-seed", "mutator-run");
        var (_, second) = CreatePuzzle(puzzleKey, MazeDifficulty.Hard, "mutator-seed", "mutator-run");

        var firstView = first.BuildPanelView(0d);
        var secondView = second.BuildPanelView(0d);

        Assert.Equal(firstView.Board["mutator:active"], secondView.Board["mutator:active"]);
        Assert.Equal(firstView.Board["mutator:id"], secondView.Board["mutator:id"]);
        Assert.Equal(firstView.Board["mutator:label"], secondView.Board["mutator:label"]);
    }

    private static (RoomPuzzle puzzle, ISoloPanelPuzzle panelPuzzle) CreatePuzzle(
        char puzzleKey,
        MazeDifficulty difficulty,
        string seed = "",
        string runNonce = "")
    {
        var room = new MazeRoomDefinition
        {
            Coordinates = new GridPoint(3, 3),
            ConnectionKey = 'O',
            PuzzleKey = puzzleKey,
            Kind = MazeRoomKind.Normal,
            Connections = new RoomConnections(true, true, true, true),
        };

        var actualSeed = string.IsNullOrWhiteSpace(seed) ? $"visual-seed-{difficulty}-{puzzleKey}" : seed;
        var actualRunNonce = string.IsNullOrWhiteSpace(runNonce) ? $"visual-run-{difficulty}-{puzzleKey}" : runNonce;
        var puzzle = PuzzleFactory.Create(actualSeed, room, difficulty, actualRunNonce);
        var panelPuzzle = Assert.IsAssignableFrom<ISoloPanelPuzzle>(puzzle);
        panelPuzzle.EnsureTierLevel(0, 11);
        return (puzzle, panelPuzzle);
    }
}
