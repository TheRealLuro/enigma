using System.Text.Json.Serialization;

namespace Enigma.Client.Models;

public class ProfileResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("user")]
    public ProfileUserData? User { get; set; }
}

public class ProfileUserData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("maze_nuggets")]
    public long MazeNuggets { get; set; }

    [JsonPropertyName("friends")]
    public List<string> Friends { get; set; } = [];

    [JsonPropertyName("friend_requests")]
    public List<string> FriendRequests { get; set; } = [];

    [JsonPropertyName("number_of_maps_played")]
    public int NumberOfMapsPlayed { get; set; }

    [JsonPropertyName("maps_completed")]
    public int MapsCompleted { get; set; }

    [JsonPropertyName("maps_lost")]
    public int MapsLost { get; set; }

    [JsonPropertyName("maps_owned")]
    public List<MapSummary> MapsOwned { get; set; } = [];

    [JsonPropertyName("maps_discovered")]
    public List<MapSummary> MapsDiscovered { get; set; } = [];

    [JsonPropertyName("owned_cosmetics")]
    public List<string> OwnedCosmetics { get; set; } = [];

    [JsonPropertyName("owned_maps_count")]
    public int OwnedMapsCount { get; set; }

    [JsonPropertyName("discovered_maps_count")]
    public int DiscoveredMapsCount { get; set; }

    [JsonPropertyName("profile_image")]
    public ProfileImageState? ProfileImage { get; set; }

    [JsonPropertyName("is_system_account")]
    public bool IsSystemAccount { get; set; }

    [JsonPropertyName("allow_public_profile")]
    public bool AllowPublicProfile { get; set; } = true;

    [JsonPropertyName("relationship")]
    public FriendRelationshipState Relationship { get; set; } = new();

    public double WinRate => NumberOfMapsPlayed <= 0 ? 0 : (double)MapsCompleted / NumberOfMapsPlayed;
}

public class FriendRelationshipState
{
    [JsonPropertyName("are_friends")]
    public bool AreFriends { get; set; }

    [JsonPropertyName("incoming_request")]
    public bool HasIncomingRequest { get; set; }

    [JsonPropertyName("outgoing_request")]
    public bool HasOutgoingRequest { get; set; }
}
