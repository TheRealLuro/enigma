using System.Text.Json.Serialization;

namespace Enigma.Client.Models;

public sealed class StakingOverviewResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("overview")]
    public StakingOverviewData? Overview { get; set; }

    [JsonPropertyName("user")]
    public LoginUserSummary? User { get; set; }
}

public sealed class StakingActionResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("reward_granted")]
    public bool RewardGranted { get; set; }

    [JsonPropertyName("rewarded_mn")]
    public int RewardedMn { get; set; }

    [JsonPropertyName("overview")]
    public StakingOverviewData? Overview { get; set; }

    [JsonPropertyName("user")]
    public LoginUserSummary? User { get; set; }
}

public sealed class StakingOverviewData
{
    [JsonPropertyName("staked_maps")]
    public List<StakedMapEntry> StakedMaps { get; set; } = [];

    [JsonPropertyName("available_maps")]
    public List<StakedMapEntry> AvailableMaps { get; set; } = [];

    [JsonPropertyName("staked_maps_count")]
    public int StakedMapsCount { get; set; }

    [JsonPropertyName("available_maps_count")]
    public int AvailableMapsCount { get; set; }

    [JsonPropertyName("total_daily_reward")]
    public int TotalDailyReward { get; set; }

    [JsonPropertyName("last_claim_at")]
    public string? LastClaimAt { get; set; }

    [JsonPropertyName("next_claim_at")]
    public string? NextClaimAt { get; set; }

    [JsonPropertyName("can_claim_today")]
    public bool CanClaimToday { get; set; }

    [JsonPropertyName("stake_lock_hours")]
    public int StakeLockHours { get; set; }
}

public sealed class StakedMapEntry
{
    [JsonPropertyName("map_id")]
    public string MapId { get; set; } = string.Empty;

    [JsonPropertyName("map_name")]
    public string MapName { get; set; } = string.Empty;

    [JsonPropertyName("difficulty")]
    public string Difficulty { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public int Size { get; set; }

    [JsonPropertyName("base_reward")]
    public int BaseReward { get; set; }

    [JsonPropertyName("size_multiplier")]
    public double SizeMultiplier { get; set; }

    [JsonPropertyName("daily_reward")]
    public int DailyReward { get; set; }

    [JsonPropertyName("locked_until")]
    public string? LockedUntil { get; set; }

    [JsonPropertyName("is_locked")]
    public bool IsLocked { get; set; }

    [JsonPropertyName("lock_seconds_remaining")]
    public int LockSecondsRemaining { get; set; }
}

public sealed class GovernanceSessionResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("voting_open")]
    public bool VotingOpen { get; set; }

    [JsonPropertyName("is_bank_user")]
    public bool IsBankUser { get; set; }

    [JsonPropertyName("active_session")]
    public GovernanceSessionData? ActiveSession { get; set; }

    [JsonPropertyName("latest_closed_session")]
    public GovernanceSessionData? LatestClosedSession { get; set; }

    [JsonPropertyName("closed_session")]
    public GovernanceSessionData? ClosedSession { get; set; }

    [JsonPropertyName("recent_votes")]
    public List<GovernanceRecentVote> RecentVotes { get; set; } = [];

    [JsonPropertyName("user")]
    public LoginUserSummary? User { get; set; }
}

public sealed class GovernanceRecentVote
{
    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("session_title")]
    public string SessionTitle { get; set; } = string.Empty;

    [JsonPropertyName("session_status")]
    public string SessionStatus { get; set; } = string.Empty;

    [JsonPropertyName("session_result_leader")]
    public string? SessionResultLeader { get; set; }

    [JsonPropertyName("vote_type")]
    public string VoteType { get; set; } = string.Empty;

    [JsonPropertyName("selection_labels")]
    public List<string> SelectionLabels { get; set; } = [];

    [JsonPropertyName("vote_quantity")]
    public int VoteQuantity { get; set; }

    [JsonPropertyName("mn_spent")]
    public int MnSpent { get; set; }

    [JsonPropertyName("vote_power")]
    public double VotePower { get; set; }

    [JsonPropertyName("stake_weight_multiplier")]
    public double StakeWeightMultiplier { get; set; } = 1.0;

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
}

public sealed class GovernanceVoteResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("mn_spent")]
    public int MnSpent { get; set; }

    [JsonPropertyName("vote_power")]
    public double VotePower { get; set; }

    [JsonPropertyName("stake_weight_multiplier")]
    public double StakeWeightMultiplier { get; set; }

    [JsonPropertyName("vote_quantity")]
    public int VoteQuantity { get; set; }

    [JsonPropertyName("vote_cost_mn")]
    public int VoteCostMn { get; set; }

    [JsonPropertyName("selection_count")]
    public int SelectionCount { get; set; }

    [JsonPropertyName("voting_open")]
    public bool VotingOpen { get; set; }

    [JsonPropertyName("active_session")]
    public GovernanceSessionData? ActiveSession { get; set; }

    [JsonPropertyName("latest_closed_session")]
    public GovernanceSessionData? LatestClosedSession { get; set; }

    [JsonPropertyName("recent_votes")]
    public List<GovernanceRecentVote> RecentVotes { get; set; } = [];

    [JsonPropertyName("user")]
    public LoginUserSummary? User { get; set; }
}

public sealed class GovernanceSessionData
{
    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("started_by")]
    public string StartedBy { get; set; } = string.Empty;

    [JsonPropertyName("started_at")]
    public string? StartedAt { get; set; }

    [JsonPropertyName("ends_at")]
    public string? EndsAt { get; set; }

    [JsonPropertyName("closed_at")]
    public string? ClosedAt { get; set; }

    [JsonPropertyName("vote_type")]
    public string VoteType { get; set; } = "one_choice";

    [JsonPropertyName("vote_cost_mn")]
    public int VoteCostMn { get; set; } = 1;

    [JsonPropertyName("duration_value")]
    public int DurationValue { get; set; }

    [JsonPropertyName("duration_unit")]
    public string DurationUnit { get; set; } = "hours";

    [JsonPropertyName("number_min")]
    public int? NumberMin { get; set; }

    [JsonPropertyName("number_max")]
    public int? NumberMax { get; set; }

    [JsonPropertyName("total_mn_spent")]
    public int TotalMnSpent { get; set; }

    [JsonPropertyName("total_vote_power")]
    public double TotalVotePower { get; set; }

    [JsonPropertyName("total_votes_cast")]
    public int TotalVotesCast { get; set; }

    [JsonPropertyName("unique_voter_count")]
    public int UniqueVoterCount { get; set; }

    [JsonPropertyName("options")]
    public List<GovernanceOptionTally> Options { get; set; } = [];

    [JsonPropertyName("user_vote_summary")]
    public GovernanceUserVoteSummary UserVoteSummary { get; set; } = new();
}

public sealed class GovernanceOptionTally
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("mn_spent")]
    public int MnSpent { get; set; }

    [JsonPropertyName("vote_power")]
    public double VotePower { get; set; }

    [JsonPropertyName("vote_count")]
    public int VoteCount { get; set; }

    [JsonPropertyName("vote_quantity")]
    public int VoteQuantity { get; set; }
}

public sealed class GovernanceUserVoteSummary
{
    [JsonPropertyName("staked_maps_count")]
    public int StakedMapsCount { get; set; }

    [JsonPropertyName("stake_weight_multiplier")]
    public double StakeWeightMultiplier { get; set; } = 1.0;

    [JsonPropertyName("mn_spent")]
    public int MnSpent { get; set; }

    [JsonPropertyName("vote_power")]
    public double VotePower { get; set; }

    [JsonPropertyName("vote_quantity")]
    public int VoteQuantity { get; set; }
}
