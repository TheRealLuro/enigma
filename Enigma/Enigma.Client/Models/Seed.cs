using System.ComponentModel.DataAnnotations;

namespace Enigma.Client.Models
{
    public class Seed
    {
        [Required(ErrorMessage = "Cannot be empty.")]
        public string MapSeed { get; set; }
    }
}
