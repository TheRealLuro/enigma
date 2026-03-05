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
    public long MazeNuggets { get; set; }

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

    [JsonPropertyName("pending_multiplayer_invites")]
    public List<PendingMultiplayerInvite> PendingMultiplayerInvites { get; set; } = [];

    [JsonPropertyName("last_login_at")]
    public string? LastLoginAt { get; set; }

    [JsonPropertyName("last_username_change_at")]
    public string? LastUsernameChangeAt { get; set; }

    [JsonPropertyName("username_change_cooldown_days")]
    public int UsernameChangeCooldownDays { get; set; }

    [JsonPropertyName("next_username_change_at")]
    public string? NextUsernameChangeAt { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("profile_image")]
    public ProfileImageState? ProfileImage { get; set; }

    [JsonPropertyName("tutorial_state")]
    public TutorialState TutorialState { get; set; } = new();

    [JsonPropertyName("is_system_account")]
    public bool IsSystemAccount { get; set; }

    [JsonPropertyName("allow_public_profile")]
    public bool AllowPublicProfile { get; set; } = true;

    [JsonPropertyName("owned_maps_count")]
    public int OwnedMapsCount { get; set; }

    [JsonPropertyName("discovered_maps_count")]
    public int DiscoveredMapsCount { get; set; }

    public double WinRate => NumberOfMapsPlayed <= 0 ? 0 : (double)MapsCompleted / NumberOfMapsPlayed;
}

public class PendingMultiplayerInvite
{
    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("owner_username")]
    public string OwnerUsername { get; set; } = string.Empty;

    [JsonPropertyName("map_name")]
    public string? MapName { get; set; }

    [JsonPropertyName("difficulty")]
    public string Difficulty { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public int Size { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "new";
}
