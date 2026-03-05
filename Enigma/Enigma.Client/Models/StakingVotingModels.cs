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

    [JsonPropertyName("analysis_full_hours")]
    public int AnalysisFullHours { get; set; } = 72;

    [JsonPropertyName("research_influence")]
    public StakingResearchInfluence? ResearchInfluence { get; set; }

    [JsonPropertyName("collective_research")]
    public StakingCollectiveResearch? CollectiveResearch { get; set; }

    [JsonPropertyName("anomaly_stability")]
    public StakingAnomalyStability? AnomalyStability { get; set; }
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

    [JsonPropertyName("analysis_started_at")]
    public string? AnalysisStartedAt { get; set; }

    [JsonPropertyName("analysis_elapsed_seconds")]
    public int AnalysisElapsedSeconds { get; set; }

    [JsonPropertyName("analysis_progress_percent")]
    public double AnalysisProgressPercent { get; set; }

    [JsonPropertyName("analysis_phase_key")]
    public string AnalysisPhaseKey { get; set; } = string.Empty;

    [JsonPropertyName("analysis_phase_label")]
    public string AnalysisPhaseLabel { get; set; } = string.Empty;

    [JsonPropertyName("containment_active")]
    public bool ContainmentActive { get; set; }

    [JsonPropertyName("containment_remaining_seconds")]
    public int ContainmentRemainingSeconds { get; set; }

    [JsonPropertyName("containment_progress_percent")]
    public double ContainmentProgressPercent { get; set; }

    [JsonPropertyName("research_depth_level")]
    public int ResearchDepthLevel { get; set; }

    [JsonPropertyName("research_depth_label")]
    public string ResearchDepthLabel { get; set; } = string.Empty;

    [JsonPropertyName("research_data_percent")]
    public double ResearchDataPercent { get; set; }

    [JsonPropertyName("puzzle_structures_identified")]
    public int PuzzleStructuresIdentified { get; set; }

    [JsonPropertyName("spatial_layers_detected")]
    public int SpatialLayersDetected { get; set; }

    [JsonPropertyName("anomaly_classification")]
    public string AnomalyClassification { get; set; } = string.Empty;

    [JsonPropertyName("analysis_eta_seconds")]
    public int AnalysisEtaSeconds { get; set; }

    [JsonPropertyName("analysis_eta_at")]
    public string? AnalysisEtaAt { get; set; }

    [JsonPropertyName("analysis_ai_message")]
    public string AnalysisAiMessage { get; set; } = string.Empty;

    [JsonPropertyName("analysis_unlocks")]
    public List<StakingAnalysisUnlockEntry> AnalysisUnlocks { get; set; } = [];

    [JsonPropertyName("analysis_deepest_unlock_label")]
    public string AnalysisDeepestUnlockLabel { get; set; } = string.Empty;

    [JsonPropertyName("analysis_report")]
    public StakingAnalysisReport? AnalysisReport { get; set; }
}

public sealed class StakingAnalysisUnlockEntry
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("required_hours")]
    public double RequiredHours { get; set; }

    [JsonPropertyName("unlocked")]
    public bool Unlocked { get; set; }

    [JsonPropertyName("unlock_at")]
    public string? UnlockAt { get; set; }
}

public sealed class StakingAnalysisReport
{
    [JsonPropertyName("report_id")]
    public string ReportId { get; set; } = string.Empty;

    [JsonPropertyName("anomaly_type")]
    public string AnomalyType { get; set; } = string.Empty;

    [JsonPropertyName("puzzle_structure")]
    public string PuzzleStructure { get; set; } = string.Empty;

    [JsonPropertyName("stability_rating")]
    public int StabilityRating { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("conclusion")]
    public string Conclusion { get; set; } = string.Empty;
}

public sealed class StakingResearchInfluence
{
    [JsonPropertyName("staked_maps_count")]
    public int StakedMapsCount { get; set; }

    [JsonPropertyName("multiplier")]
    public double Multiplier { get; set; } = 1.0;

    [JsonPropertyName("stake_component")]
    public double StakeComponent { get; set; } = 1.0;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

public sealed class StakingCollectiveResearch
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("target_maps")]
    public int TargetMaps { get; set; }

    [JsonPropertyName("contributed_maps")]
    public int ContributedMaps { get; set; }

    [JsonPropertyName("contributor_count")]
    public int ContributorCount { get; set; }

    [JsonPropertyName("progress_percent")]
    public double ProgressPercent { get; set; }
}

public sealed class StakingAnomalyStability
{
    [JsonPropertyName("percent")]
    public double Percent { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("required_maps")]
    public int RequiredMaps { get; set; }
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

    [JsonPropertyName("effective_vote_multiplier")]
    public double EffectiveVoteMultiplier { get; set; }

    [JsonPropertyName("stake_multiplier")]
    public double StakeMultiplier { get; set; } = 1.0;

    [JsonPropertyName("participation_multiplier")]
    public double ParticipationMultiplier { get; set; } = 1.0;

    [JsonPropertyName("raw_multiplier_before_cap")]
    public double RawMultiplierBeforeCap { get; set; } = 1.0;

    [JsonPropertyName("multiplier_cap")]
    public double MultiplierCap { get; set; } = 2.25;

    [JsonPropertyName("prior_votes_count")]
    public int PriorVotesCount { get; set; }

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

    [JsonPropertyName("effective_vote_multiplier")]
    public double EffectiveVoteMultiplier { get; set; } = 1.0;

    [JsonPropertyName("stake_multiplier")]
    public double StakeMultiplier { get; set; } = 1.0;

    [JsonPropertyName("participation_multiplier")]
    public double ParticipationMultiplier { get; set; } = 1.0;

    [JsonPropertyName("raw_multiplier")]
    public double RawMultiplier { get; set; } = 1.0;

    [JsonPropertyName("multiplier_cap")]
    public double MultiplierCap { get; set; } = 2.25;

    [JsonPropertyName("participation_votes_count")]
    public int ParticipationVotesCount { get; set; }

    [JsonPropertyName("mn_spent")]
    public int MnSpent { get; set; }

    [JsonPropertyName("vote_power")]
    public double VotePower { get; set; }

    [JsonPropertyName("vote_quantity")]
    public int VoteQuantity { get; set; }
}
