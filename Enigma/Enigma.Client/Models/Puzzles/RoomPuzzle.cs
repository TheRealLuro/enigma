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

public interface IRevealOnOpenPuzzle
{
    bool RevealStarted { get; }
    void BeginReveal();
}

public static class PuzzleFactory
{
    public static RoomPuzzle Create(string seed, MazeRoomDefinition room, MazeDifficulty difficulty, string? runNonce = null)
    {
        var normalizedRunNonce = runNonce ?? Guid.NewGuid().ToString("N");
        return AdvancedPuzzleFactory.Create(seed, normalizedRunNonce, room, difficulty);
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
}
