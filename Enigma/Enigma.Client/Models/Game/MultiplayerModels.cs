using System.Text.Json;
using System.Text.Json.Serialization;

namespace Enigma.Client.Models.Gameplay;

public sealed class MultiplayerPuzzleCatalogResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("catalog")]
    public Dictionary<string, Dictionary<string, MultiplayerPuzzleDefinition>> Catalog { get; set; } = [];
}

public sealed class MultiplayerPuzzleDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("core_mechanic")]
    public string CoreMechanic { get; set; } = string.Empty;
}

public sealed class MultiplayerSessionEnvelope
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("room_moved")]
    public bool RoomMoved { get; set; }

    [JsonPropertyName("session")]
    public MultiplayerSessionState? Session { get; set; }

    [JsonPropertyName("completion")]
    public MultiplayerCompletionData? Completion { get; set; }
}

public sealed class MultiplayerSessionState
{
    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("owner_username")]
    public string OwnerUsername { get; set; } = string.Empty;

    [JsonPropertyName("guest_username")]
    public string? GuestUsername { get; set; }

    [JsonPropertyName("seed")]
    public string Seed { get; set; } = string.Empty;

    [JsonPropertyName("map_name")]
    public string? MapName { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "new";

    [JsonPropertyName("difficulty")]
    public string Difficulty { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public int Size { get; set; }

    [JsonPropertyName("team_gold")]
    public int TeamGold { get; set; }

    [JsonPropertyName("solved_room_count")]
    public int SolvedRoomCount { get; set; }

    [JsonPropertyName("current_room")]
    public MultiplayerRoomState CurrentRoom { get; set; } = new();

    [JsonPropertyName("current_room_progress")]
    public MultiplayerRoomProgress CurrentRoomProgress { get; set; } = new();

    [JsonPropertyName("current_room_puzzle")]
    public MultiplayerRoomPuzzleState? CurrentRoomPuzzle { get; set; }

    [JsonPropertyName("start_room")]
    public MultiplayerRoomState StartRoom { get; set; } = new();

    [JsonPropertyName("finish_room")]
    public MultiplayerRoomState FinishRoom { get; set; } = new();

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("started_at")]
    public string? StartedAt { get; set; }

    [JsonPropertyName("completed_at")]
    public string? CompletedAt { get; set; }

    [JsonPropertyName("invited_friends")]
    public List<string> InvitedFriends { get; set; } = [];

    [JsonPropertyName("all_ready")]
    public bool AllReady { get; set; }

    [JsonPropertyName("required_players")]
    public int RequiredPlayers { get; set; } = 2;

    [JsonPropertyName("move_vote")]
    public MultiplayerMoveVote? MoveVote { get; set; }

    [JsonPropertyName("you")]
    public MultiplayerPlayerState? You { get; set; }

    [JsonPropertyName("other_player_visible")]
    public bool OtherPlayerVisible { get; set; }

    [JsonPropertyName("other_player")]
    public MultiplayerPlayerState? OtherPlayer { get; set; }

    [JsonPropertyName("completion")]
    public MultiplayerCompletionData? Completion { get; set; }
}

public sealed class MultiplayerRoomState
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    public override string ToString() => $"({X}, {Y})";
}

public sealed class MultiplayerMoveVote
{
    [JsonPropertyName("target_key")]
    public string TargetKey { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public MultiplayerRoomState Target { get; set; } = new();

    [JsonPropertyName("votes")]
    public List<string> Votes { get; set; } = [];
}

public sealed class MultiplayerPlayerState
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("joined_at")]
    public string? JoinedAt { get; set; }

    [JsonPropertyName("ready")]
    public bool Ready { get; set; }

    [JsonPropertyName("last_seen_at")]
    public string? LastSeenAt { get; set; }

    [JsonPropertyName("room")]
    public MultiplayerRoomState Room { get; set; } = new();

    [JsonPropertyName("position")]
    public MultiplayerPlayerPosition Position { get; set; } = new();

    [JsonPropertyName("facing")]
    public string Facing { get; set; } = "Down";

    [JsonPropertyName("is_on_black_hole")]
    public bool IsOnBlackHole { get; set; }

    [JsonPropertyName("gold_collected")]
    public int GoldCollected { get; set; }
}

public sealed class MultiplayerRoomProgress
{
    [JsonPropertyName("puzzle_solved")]
    public bool PuzzleSolved { get; set; }

    [JsonPropertyName("reward_pickup_collected")]
    public bool RewardPickupCollected { get; set; }
}

public sealed class MultiplayerPlayerPosition
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; } = 8;

    [JsonPropertyName("height")]
    public double Height { get; set; } = 8;

    [JsonPropertyName("x_percent")]
    public double XPercent { get; set; } = 50;

    [JsonPropertyName("y_percent")]
    public double YPercent { get; set; } = 50;
}

public sealed class MultiplayerCompletionData
{
    [JsonPropertyName("total_rewards")]
    public int TotalRewards { get; set; }

    [JsonPropertyName("bank_dividend")]
    public int BankDividend { get; set; }

    [JsonPropertyName("owner_reward")]
    public int OwnerReward { get; set; }

    [JsonPropertyName("guest_reward")]
    public int GuestReward { get; set; }

    [JsonPropertyName("discoverers")]
    public List<string> Discoverers { get; set; } = [];

    [JsonPropertyName("owner_username")]
    public string OwnerUsername { get; set; } = string.Empty;

    [JsonPropertyName("seed_existed")]
    public bool SeedExisted { get; set; }

    [JsonPropertyName("requires_owner_save")]
    public bool RequiresOwnerSave { get; set; }

    [JsonPropertyName("saved_map_id")]
    public string? SavedMapId { get; set; }

    [JsonPropertyName("saved_map_name")]
    public string? SavedMapName { get; set; }

    [JsonPropertyName("discoverers_synced")]
    public bool DiscoverersSynced { get; set; }
}

public sealed class MultiplayerSocketEnvelope
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [JsonPropertyName("room_moved")]
    public bool RoomMoved { get; set; }

    [JsonPropertyName("session")]
    public MultiplayerSessionState? Session { get; set; }

    [JsonPropertyName("completion")]
    public MultiplayerCompletionData? Completion { get; set; }
}

public sealed class MultiplayerRoomPuzzleState
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("difficulty")]
    public string Difficulty { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("instruction")]
    public string Instruction { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("completed")]
    public bool Completed { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("view_type")]
    public string ViewType { get; set; } = string.Empty;

    [JsonPropertyName("view")]
    public JsonElement View { get; set; }
}
