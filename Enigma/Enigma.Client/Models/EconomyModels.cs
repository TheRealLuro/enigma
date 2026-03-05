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
