using System.Text.Json.Serialization;

public class LoginResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("user")]
    public UserData? User { get; set; }

    [JsonPropertyName("daily_reward_granted")]
    public bool DailyRewardGranted { get; set; }

    [JsonPropertyName("daily_reward_amount")]
    public int DailyRewardAmount { get; set; }
}

public class UserData
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("maze_nuggets")]
    public int MazeNuggets { get; set; }
}