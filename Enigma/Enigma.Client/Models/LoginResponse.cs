using System.Text.Json.Serialization;

namespace Enigma.Client.Models;

public class LoginResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("user")]
    public LoginUserSummary? User { get; set; }

    [JsonPropertyName("daily_reward_granted")]
    public bool DailyRewardGranted { get; set; }

    [JsonPropertyName("daily_reward_amount")]
    public int DailyRewardAmount { get; set; }
}

public class LoginUserSummary
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("maze_nuggets")]
    public int MazeNuggets { get; set; }

    [JsonPropertyName("friends")]
    public List<string> Friends { get; set; } = [];

    [JsonPropertyName("friend_requests")]
    public List<string> FriendRequests { get; set; } = [];

    [JsonPropertyName("maps_owned")]
    public List<MapSummary> MapsOwned { get; set; } = [];

    [JsonPropertyName("maps_discovered")]
    public List<MapSummary> MapsDiscovered { get; set; } = [];

    [JsonPropertyName("number_of_maps_played")]
    public int NumberOfMapsPlayed { get; set; }

    [JsonPropertyName("maps_completed")]
    public int MapsCompleted { get; set; }

    [JsonPropertyName("maps_lost")]
    public int MapsLost { get; set; }

    [JsonPropertyName("owned_cosmetics")]
    public List<string> OwnedCosmetics { get; set; } = [];

    [JsonPropertyName("item_counts")]
    public Dictionary<string, int> ItemCounts { get; set; } = [];

    [JsonPropertyName("last_login_at")]
    public string? LastLoginAt { get; set; }

    public double WinRate => NumberOfMapsPlayed <= 0 ? 0 : (double)MapsCompleted / NumberOfMapsPlayed;
}
