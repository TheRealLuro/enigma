using System.Text.Json.Serialization;

namespace Enigma.Client.Models;

public class PlayerLeaderboardResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("players")]
    public List<PlayerLeaderboardEntry> Players { get; set; } = [];
}

public class PlayerLeaderboardEntry
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

    [JsonPropertyName("maps_played")]
    public int MapsPlayed { get; set; }

    [JsonPropertyName("win_rate")]
    public double WinRate { get; set; }

    [JsonPropertyName("profile_image")]
    public ProfileImageState? ProfileImage { get; set; }
}
