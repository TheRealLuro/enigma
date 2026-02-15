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
        [Range(3, 12, ErrorMessage = "A map's dimensions must be between 3 and 12.")]
        public int MapSize { get; set; }
        public Seed Seed { get; set; }
    }
}
