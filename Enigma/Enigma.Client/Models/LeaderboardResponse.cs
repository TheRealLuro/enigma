using System.Text.Json.Serialization;

namespace Enigma.Client.Models;

public class LeaderboardResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("sort_by")]
    public string SortBy { get; set; } = string.Empty;

    [JsonPropertyName("order")]
    public string Order { get; set; } = string.Empty;

    [JsonPropertyName("maps")]
    public List<MapSummary> Maps { get; set; } = [];
}
