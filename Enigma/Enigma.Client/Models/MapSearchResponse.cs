namespace Enigma.Client.Models;

public sealed class MapSearchResponse
{
    public string Status { get; set; } = string.Empty;
    public List<MapSummary> Maps { get; set; } = [];
}
