using Enigma.Client.Models.Gameplay;
using Xunit;

namespace Enigma.Client.Tests;

public sealed class RewardMultiplierTests
{
    [Fact]
    public void RewardPickupMultiplierAffectsBonusOnly()
    {
        var room = new MazeRoomDefinition
        {
            Coordinates = new GridPoint(1, 1),
            ConnectionKey = 'O',
            PuzzleKey = 'p',
            Kind = MazeRoomKind.Reward,
            Connections = new RoomConnections(true, true, true, true),
        };

        var puzzle = PuzzleFactory.Create("reward-seed", room, MazeDifficulty.Medium, runNonce: "nonce-a");
        var panelPuzzle = Assert.IsAssignableFrom<ISoloPanelPuzzle>(puzzle);
        panelPuzzle.EnsureTierLevel(solvedRoomsBeforeCurrent: 0, totalPuzzleRooms: 11);
        puzzle.SyncCompleted("Solved for reward multiplier validation.");

        var state = new RoomRuntimeState
        {
            Definition = room,
            Puzzle = puzzle,
            PuzzleGoldReward = 100,
            RewardPickupGold = 40,
            RewardPickupBounds = new PlayAreaRect(100d, 100d, 80d, 80d),
            FinishPortalBounds = new PlayAreaRect(300d, 300d, 80d, 80d),
        };

        var baseReward = state.GrantPuzzleReward();
        var scaledBonus = state.CollectRewardPickup(1.5d);

        Assert.Equal(100, baseReward);
        Assert.Equal(60, scaledBonus);
    }
}
