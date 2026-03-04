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

    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }

    [JsonPropertyName("maps")]
    public List<MapSummary> Maps { get; set; } = [];
}
