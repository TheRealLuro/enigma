using System.ComponentModel.DataAnnotations;

namespace Enigma.Client.Models
{
    public class Map
    {
        public enum Difficulty
        {
            Easy,
            Medium,
            Hard
        }

        [Required(ErrorMessage = "Cannot be empty.")]
        public string MapName { get; set; }
        [Required]
        public Difficulty MapDifficulty { get; set; } = Difficulty.Easy;
        [Range(2, int.MaxValue, ErrorMessage = "A map's dimensions must be at least a 2x2")]
        public int MapSize { get; set; }
        public Seed Seed { get; set; }
    }
}
