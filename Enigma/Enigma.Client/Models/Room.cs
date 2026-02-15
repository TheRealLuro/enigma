using System.Numerics;

namespace Enigma.Client.Models
{
    public class Room
    {
        public char RoomType { get; set; }
        public char? RoomPuzzle { get; set; }
        public Vector2 RoomPosition { get; set; }
    }
}
