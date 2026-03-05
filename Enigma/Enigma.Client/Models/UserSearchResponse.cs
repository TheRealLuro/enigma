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
    public long MazeNuggets { get; set; }

    [JsonPropertyName("owned_maps_count")]
    public int OwnedMapsCount { get; set; }

    [JsonPropertyName("discovered_maps_count")]
    public int DiscoveredMapsCount { get; set; }

    [JsonPropertyName("maps_completed")]
    public int MapsCompleted { get; set; }

    [JsonPropertyName("maps_lost")]
    public int MapsLost { get; set; }

    [JsonPropertyName("profile_image")]
    public ProfileImageState? ProfileImage { get; set; }

    [JsonPropertyName("is_online")]
    public bool IsOnline { get; set; }
}
