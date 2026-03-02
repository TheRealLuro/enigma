using System.Text.Json.Serialization;

namespace Enigma.Client.Models;

public class MarketplaceResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("listings")]
    public List<MarketplaceListing> Listings { get; set; } = [];
}

public class MarketplaceListing
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("map_name")]
    public string MapName { get; set; } = string.Empty;

    [JsonPropertyName("map_image")]
    public string? MapImage { get; set; }

    [JsonPropertyName("image_available")]
    public bool ImageAvailable { get; set; }

    [JsonPropertyName("image_status")]
    public string ImageStatus { get; set; } = string.Empty;

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = string.Empty;

    [JsonPropertyName("difficulty")]
    public string Difficulty { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public int Size { get; set; }

    [JsonPropertyName("value")]
    public int Value { get; set; }

    [JsonPropertyName("price")]
    public int Price { get; set; }

    [JsonPropertyName("seller")]
    public string Seller { get; set; } = string.Empty;

    [JsonPropertyName("sold_for_last")]
    public int SoldForLast { get; set; }

    [JsonPropertyName("listed_at")]
    public string? ListedAt { get; set; }

    [JsonPropertyName("listed_at_display")]
    public string ListedAtDisplay { get; set; } = string.Empty;

    [JsonPropertyName("last_bought")]
    public string? LastBought { get; set; }

    [JsonPropertyName("last_bought_display")]
    public string LastBoughtDisplay { get; set; } = string.Empty;
}
