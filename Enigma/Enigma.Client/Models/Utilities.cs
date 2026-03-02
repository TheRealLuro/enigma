using System.Numerics;
using Enigma.Client.Models.Gameplay;

namespace Enigma.Client.Models
{
    public enum RoomType
    {
        Normal,
        Start,
        Finish,
        Reward
    }

    public static class Utilities
    {
        public static int SeedGetDifficulty(string seed)
        {
            var parsed = MazeSeedParser.Parse(seed);
            return MazeSeedParser.DifficultyToLevel(parsed.Difficulty);
        }

        public static int SeedGetSize(string seed)
        {
            var parsed = MazeSeedParser.Parse(seed);
            return parsed.Size;
        }

        public static Room GetNextRoom(string seed, Vector2 currentRoom, string direction)
        {
            var parsed = MazeSeedParser.Parse(seed);
            var currentPoint = new GridPoint((int)currentRoom.X, (int)currentRoom.Y);
            if (!parsed.TryGetRoom(currentPoint, out var room))
            {
                throw new InvalidOperationException($"No room exists at {currentPoint}.");
            }

            var nextPoint = direction.ToUpperInvariant() switch
            {
                "N" => new GridPoint(currentPoint.X, currentPoint.Y - 1),
                "E" => new GridPoint(currentPoint.X + 1, currentPoint.Y),
                "S" => new GridPoint(currentPoint.X, currentPoint.Y + 1),
                "W" => new GridPoint(currentPoint.X - 1, currentPoint.Y),
                _ => throw new ArgumentOutOfRangeException(nameof(direction), "Direction must be N, E, S, or W."),
            };

            if (!parsed.TryGetRoom(nextPoint, out var nextRoom))
            {
                throw new InvalidOperationException($"No room exists at {nextPoint}.");
            }

            return MazeSeedParser.ToLegacyRoom(nextRoom);
        }

        public static RoomType GetRoomType(string seed, Vector2 currentRoom)
        {
            var parsed = MazeSeedParser.Parse(seed);
            var currentPoint = new GridPoint((int)currentRoom.X, (int)currentRoom.Y);
            if (!parsed.TryGetRoom(currentPoint, out var room))
            {
                throw new InvalidOperationException($"No room exists at {currentPoint}.");
            }

            return room.Kind switch
            {
                MazeRoomKind.Start => RoomType.Start,
                MazeRoomKind.Finish => RoomType.Finish,
                MazeRoomKind.Reward => RoomType.Reward,
                _ => RoomType.Normal,
            };
        }
    }
}
