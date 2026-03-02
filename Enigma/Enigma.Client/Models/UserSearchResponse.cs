using System.Text.Json.Serialization;

namespace Enigma.Client.Models;

public class UserSearchResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("users")]
    public List<UserSearchResult> Users { get; set; } = [];
}

public class UserSearchResult
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("maze_nuggets")]
    public int MazeNuggets { get; set; }
}
