using System.Collections.ObjectModel;
using System.Numerics;

namespace Enigma.Client.Models.Gameplay;

public enum MazeDifficulty
{
    Easy,
    Medium,
    Hard,
}

public enum MazeRoomKind
{
    Normal,
    Start,
    Finish,
    Reward,
}

public enum PlayerDirection
{
    Up,
    Right,
    Down,
    Left,
}

public readonly record struct GridPoint(int X, int Y)
{
    public override string ToString() => $"({X}, {Y})";
}

public readonly record struct RoomConnections(bool North, bool East, bool South, bool West)
{
    public bool HasDoor(PlayerDirection direction) => direction switch
    {
        PlayerDirection.Up => North,
        PlayerDirection.Right => East,
        PlayerDirection.Down => South,
        PlayerDirection.Left => West,
        _ => false,
    };
}

public readonly record struct PlayAreaRect(double X, double Y, double Width, double Height)
{
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + (Width / 2d);
    public double CenterY => Y + (Height / 2d);

    public bool Contains(double x, double y) => x >= Left && x <= Right && y >= Top && y <= Bottom;

    public bool Intersects(PlayAreaRect other) =>
        Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;
}

public sealed class MazeRoomDefinition
{
    public required GridPoint Coordinates { get; init; }
    public required char ConnectionKey { get; init; }
    public required char PuzzleKey { get; init; }
    public required MazeRoomKind Kind { get; init; }
    public required RoomConnections Connections { get; init; }
}

public sealed class MazeSeedDefinition
{
    public required string RawSeed { get; init; }
    public required MazeDifficulty Difficulty { get; init; }
    public required int Size { get; init; }
    public required MazeRoomDefinition StartRoom { get; init; }
    public required MazeRoomDefinition FinishRoom { get; init; }
    public required IReadOnlyDictionary<GridPoint, MazeRoomDefinition> Rooms { get; init; }

    public bool TryGetRoom(GridPoint point, out MazeRoomDefinition room) => Rooms.TryGetValue(point, out room!);
}

public sealed class RoomRuntimeState
{
    public required MazeRoomDefinition Definition { get; init; }
    public required RoomPuzzle Puzzle { get; init; }
    public required int PuzzleGoldReward { get; init; }
    public required int RewardPickupGold { get; init; }
    public required PlayAreaRect RewardPickupBounds { get; init; }
    public required PlayAreaRect FinishPortalBounds { get; init; }

    public bool PuzzleRewardGranted { get; private set; }
    public bool RewardPickupCollected { get; private set; }

    public bool RewardPickupVisible =>
        Definition.Kind == MazeRoomKind.Reward &&
        Puzzle.IsCompleted &&
        !RewardPickupCollected;

    public bool FinishPortalVisible =>
        Definition.Kind == MazeRoomKind.Finish &&
        Puzzle.IsCompleted;

    public int GrantPuzzleReward()
    {
        if (PuzzleRewardGranted)
        {
            return 0;
        }

        PuzzleRewardGranted = true;
        return PuzzleGoldReward;
    }

    public int CollectRewardPickup(double multiplier = 1d)
    {
        if (!RewardPickupVisible)
        {
            return 0;
        }

        RewardPickupCollected = true;
        var scaled = RewardPickupGold * Math.Max(1d, multiplier);
        return (int)Math.Round(scaled, MidpointRounding.AwayFromZero);
    }

    public void SyncCoopProgress(bool puzzleSolved, bool rewardPickupCollected)
    {
        if (puzzleSolved)
        {
            Puzzle.SyncCompleted("Team sync complete. Doors unlocked.");
            PuzzleRewardGranted = true;
        }

        if (rewardPickupCollected)
        {
            RewardPickupCollected = true;
        }
    }

    public void MarkRewardPickupCollectedForSync()
    {
        RewardPickupCollected = true;
    }
}

public sealed class PuzzleUpdateContext
{
    public required PlayAreaRect PlayerBounds { get; init; }
    public required double DeltaTimeSeconds { get; init; }
    public required double NowSeconds { get; init; }
    public required PlayerDirection PlayerFacing { get; init; }
}

public sealed class MazeSeedParseException(string message) : Exception(message);

public static class MazeSeedParser
{
    private static readonly IReadOnlyDictionary<char, RoomConnections> ConnectionMap =
        new ReadOnlyDictionary<char, RoomConnections>(new Dictionary<char, RoomConnections>
        {
            ['A'] = new(true, false, false, false),
            ['B'] = new(false, true, false, false),
            ['C'] = new(true, true, false, false),
            ['D'] = new(false, false, true, false),
            ['E'] = new(true, false, true, false),
            ['F'] = new(false, true, true, false),
            ['G'] = new(true, true, true, false),
            ['H'] = new(false, false, false, true),
            ['I'] = new(true, false, false, true),
            ['J'] = new(false, true, false, true),
            ['K'] = new(true, true, false, true),
            ['L'] = new(false, false, true, true),
            ['M'] = new(true, false, true, true),
            ['N'] = new(false, true, true, true),
            ['O'] = new(true, true, true, true),
        });

    public static MazeSeedDefinition Parse(string seed)
    {
        if (string.IsNullOrWhiteSpace(seed))
        {
            throw new MazeSeedParseException("Seed is required.");
        }

        var trimmedSeed = seed.Trim();
        var separatorIndex = trimmedSeed.IndexOf('-');
        if (separatorIndex <= 0 || separatorIndex == trimmedSeed.Length - 1)
        {
            throw new MazeSeedParseException("Seed must begin with a difficulty prefix.");
        }

        var difficultySegment = trimmedSeed[..separatorIndex];
        if (!Enum.TryParse<MazeDifficulty>(difficultySegment, true, out var difficulty))
        {
            throw new MazeSeedParseException($"Unknown difficulty '{difficultySegment}'.");
        }

        var roomEntries = trimmedSeed[(separatorIndex + 1)..]
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (roomEntries.Length == 0)
        {
            throw new MazeSeedParseException("Seed does not contain any rooms.");
        }

        var rooms = new Dictionary<GridPoint, MazeRoomDefinition>();
        MazeRoomDefinition? startRoom = null;
        MazeRoomDefinition? finishRoom = null;
        var maxX = 0;
        var maxY = 0;

        foreach (var entry in roomEntries)
        {
            var commaIndex = entry.IndexOf(',');
            if (commaIndex <= 0)
            {
                throw new MazeSeedParseException($"Invalid room entry '{entry}'.");
            }

            var xSegment = entry[..commaIndex];
            if (!int.TryParse(xSegment, out var x) || x < 0)
            {
                throw new MazeSeedParseException($"Invalid x coordinate in '{entry}'.");
            }

            var cursor = commaIndex + 1;
            var yDigits = new List<char>();
            while (cursor < entry.Length && char.IsDigit(entry[cursor]))
            {
                yDigits.Add(entry[cursor]);
                cursor++;
            }

            if (yDigits.Count == 0 || !int.TryParse(new string(yDigits.ToArray()), out var y) || y < 0)
            {
                throw new MazeSeedParseException($"Invalid y coordinate in '{entry}'.");
            }

            if (cursor + 2 >= entry.Length)
            {
                throw new MazeSeedParseException($"Room entry '{entry}' is missing required flags.");
            }

            var connectionKey = entry[cursor++];
            var puzzleKey = entry[cursor++];
            var kindKey = entry[cursor];

            if (!ConnectionMap.TryGetValue(connectionKey, out var connections))
            {
                throw new MazeSeedParseException($"Unknown room connection '{connectionKey}'.");
            }

            var roomKind = kindKey switch
            {
                'N' => MazeRoomKind.Normal,
                'S' => MazeRoomKind.Start,
                'F' => MazeRoomKind.Finish,
                'R' => MazeRoomKind.Reward,
                _ => throw new MazeSeedParseException($"Unknown room type '{kindKey}'."),
            };

            var point = new GridPoint(x, y);
            if (rooms.ContainsKey(point))
            {
                throw new MazeSeedParseException($"Duplicate room detected at {point}.");
            }

            var room = new MazeRoomDefinition
            {
                Coordinates = point,
                ConnectionKey = connectionKey,
                PuzzleKey = puzzleKey,
                Kind = roomKind,
                Connections = connections,
            };

            rooms[point] = room;
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);

            if (roomKind == MazeRoomKind.Start)
            {
                startRoom = startRoom is null
                    ? room
                    : throw new MazeSeedParseException("Seed contains multiple start rooms.");
            }

            if (roomKind == MazeRoomKind.Finish)
            {
                finishRoom = finishRoom is null
                    ? room
                    : throw new MazeSeedParseException("Seed contains multiple finish rooms.");
            }
        }

        if (startRoom is null)
        {
            throw new MazeSeedParseException("Seed does not contain a start room.");
        }

        if (finishRoom is null)
        {
            throw new MazeSeedParseException("Seed does not contain a finish room.");
        }

        var size = Math.Max(maxX, maxY) + 1;

        var normalizedRooms = NormalizeRooms(rooms, size);

        return new MazeSeedDefinition
        {
            RawSeed = trimmedSeed,
            Difficulty = difficulty,
            Size = size,
            StartRoom = normalizedRooms[startRoom.Coordinates],
            FinishRoom = normalizedRooms[finishRoom.Coordinates],
            Rooms = new ReadOnlyDictionary<GridPoint, MazeRoomDefinition>(normalizedRooms),
        };
    }

    public static int DifficultyToLevel(MazeDifficulty difficulty) => difficulty switch
    {
        MazeDifficulty.Easy => 0,
        MazeDifficulty.Medium => 1,
        MazeDifficulty.Hard => 2,
        _ => 0,
    };

    public static Room ToLegacyRoom(MazeRoomDefinition room) => new()
    {
        RoomType = room.Kind switch
        {
            MazeRoomKind.Start => 'S',
            MazeRoomKind.Finish => 'F',
            MazeRoomKind.Reward => 'R',
            _ => 'N',
        },
        RoomPuzzle = room.PuzzleKey,
        RoomPosition = new Vector2(room.Coordinates.X, room.Coordinates.Y),
    };

    private static Dictionary<GridPoint, MazeRoomDefinition> NormalizeRooms(
        IReadOnlyDictionary<GridPoint, MazeRoomDefinition> rooms,
        int size)
    {
        var normalized = new Dictionary<GridPoint, MazeRoomDefinition>(rooms.Count);

        foreach (var room in rooms.Values)
        {
            var point = room.Coordinates;
            var connections = new RoomConnections(
                NormalizeDoor(room.Connections.North, rooms, new GridPoint(point.X, point.Y - 1), neighbor => neighbor.Connections.South, size),
                NormalizeDoor(room.Connections.East, rooms, new GridPoint(point.X + 1, point.Y), neighbor => neighbor.Connections.West, size),
                NormalizeDoor(room.Connections.South, rooms, new GridPoint(point.X, point.Y + 1), neighbor => neighbor.Connections.North, size),
                NormalizeDoor(room.Connections.West, rooms, new GridPoint(point.X - 1, point.Y), neighbor => neighbor.Connections.East, size));

            normalized[point] = new MazeRoomDefinition
            {
                Coordinates = point,
                ConnectionKey = room.ConnectionKey,
                PuzzleKey = room.PuzzleKey,
                Kind = room.Kind,
                Connections = connections,
            };
        }

        return normalized;
    }

    private static bool NormalizeDoor(
        bool hasDoor,
        IReadOnlyDictionary<GridPoint, MazeRoomDefinition> rooms,
        GridPoint neighborPoint,
        Func<MazeRoomDefinition, bool> reciprocalDoor,
        int size)
    {
        var inBounds = neighborPoint.X >= 0 && neighborPoint.Y >= 0 && neighborPoint.X < size && neighborPoint.Y < size;
        if (!inBounds)
        {
            return false;
        }

        if (!rooms.TryGetValue(neighborPoint, out var neighbor))
        {
            return false;
        }

        return hasDoor || reciprocalDoor(neighbor);
    }
}
