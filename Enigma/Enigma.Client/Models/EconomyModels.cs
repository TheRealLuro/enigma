using System.Text.Json.Serialization;

namespace Enigma.Client.Models;

public sealed class EconomyOverviewResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("overview")]
    public EconomyOverviewData? Overview { get; set; }

    [JsonPropertyName("user")]
    public LoginUserSummary? User { get; set; }
}

public sealed class EconomyPerfResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("generated_at")]
    public string? GeneratedAt { get; set; }

    [JsonPropertyName("runtime")]
    public EconomyPerfRuntimeSnapshot? Runtime { get; set; }

    [JsonPropertyName("dependencies")]
    public EconomyPerfDependencies? Dependencies { get; set; }
}

public sealed class EconomyPerfRuntimeSnapshot
{
    [JsonPropertyName("uptime_seconds")]
    public double UptimeSeconds { get; set; }

    [JsonPropertyName("inflight_requests")]
    public int InflightRequests { get; set; }

    [JsonPropertyName("peak_inflight_requests")]
    public int PeakInflightRequests { get; set; }

    [JsonPropertyName("total_requests")]
    public long TotalRequests { get; set; }

    [JsonPropertyName("total_errors")]
    public long TotalErrors { get; set; }

    [JsonPropertyName("error_rate_percent")]
    public double ErrorRatePercent { get; set; }

    [JsonPropertyName("rps_10s")]
    public double Rps10s { get; set; }

    [JsonPropertyName("rps_60s")]
    public double Rps60s { get; set; }

    [JsonPropertyName("error_rps_60s")]
    public double ErrorRps60s { get; set; }

    [JsonPropertyName("avg_response_ms")]
    public double AvgResponseMs { get; set; }

    [JsonPropertyName("p95_response_ms")]
    public double P95ResponseMs { get; set; }
}

public sealed class EconomyPerfDependencies
{
    [JsonPropertyName("redis")]
    public EconomyDependencyPerfSnapshot? Redis { get; set; }

    [JsonPropertyName("mongo")]
    public EconomyDependencyPerfSnapshot? Mongo { get; set; }
}

public sealed class EconomyDependencyPerfSnapshot
{
    [JsonPropertyName("available")]
    public bool Available { get; set; }

    [JsonPropertyName("latency_ms")]
    public double LatencyMs { get; set; }

    [JsonPropertyName("wait_estimate_ms")]
    public double WaitEstimateMs { get; set; }

    [JsonPropertyName("avg_latency_ms_60s")]
    public double AvgLatencyMs60s { get; set; }

    [JsonPropertyName("p95_latency_ms_60s")]
    public double P95LatencyMs60s { get; set; }

    [JsonPropertyName("avg_wait_estimate_ms_60s")]
    public double AvgWaitEstimateMs60s { get; set; }

    [JsonPropertyName("p95_wait_estimate_ms_60s")]
    public double P95WaitEstimateMs60s { get; set; }

    [JsonPropertyName("failure_rate_60s")]
    public double FailureRate60s { get; set; }

    [JsonPropertyName("samples_60s")]
    public int Samples60s { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public sealed class EconomyOverviewData
{
    [JsonPropertyName("generated_at")]
    public string? GeneratedAt { get; set; }

    [JsonPropertyName("mn_player_wallets_total")]
    public long MnPlayerWalletsTotal { get; set; }

    [JsonPropertyName("mn_bank_reserve_total")]
    public long MnBankReserveTotal { get; set; }

    [JsonPropertyName("mn_total_known")]
    public long MnTotalKnown { get; set; }

    [JsonPropertyName("maps_total")]
    public int MapsTotal { get; set; }

    [JsonPropertyName("maps_listed")]
    public int MapsListed { get; set; }

    [JsonPropertyName("maps_staked")]
    public int MapsStaked { get; set; }

    [JsonPropertyName("maps_staked_percent")]
    public double MapsStakedPercent { get; set; }

    [JsonPropertyName("maps_listed_percent")]
    public double MapsListedPercent { get; set; }

    [JsonPropertyName("users_total")]
    public int UsersTotal { get; set; }

    [JsonPropertyName("users_online_now")]
    public int UsersOnlineNow { get; set; }

    [JsonPropertyName("users_active_24h")]
    public int UsersActive24h { get; set; }

    [JsonPropertyName("stakers_total")]
    public int StakersTotal { get; set; }

    [JsonPropertyName("marketplace_listing_value_total")]
    public long MarketplaceListingValueTotal { get; set; }

    [JsonPropertyName("marketplace_listing_price_avg")]
    public double MarketplaceListingPriceAvg { get; set; }

    [JsonPropertyName("governance_voting_open")]
    public bool GovernanceVotingOpen { get; set; }

    [JsonPropertyName("governance_active_title")]
    public string? GovernanceActiveTitle { get; set; }

    [JsonPropertyName("governance_total_mn_spent")]
    public long GovernanceTotalMnSpent { get; set; }
}
