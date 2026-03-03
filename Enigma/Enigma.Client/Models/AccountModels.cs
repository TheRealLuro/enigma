using System.Text.Json.Serialization;

namespace Enigma.Client.Models;

public class ProfileImageState
{
    [JsonPropertyName("map_name")]
    public string MapName { get; set; } = string.Empty;

    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; set; }

    [JsonPropertyName("crop")]
    public ImageCropState Crop { get; set; } = new();

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }
}

public class ImageCropState
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("size")]
    public double Size { get; set; } = 100;
}

public class TutorialState
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("seen_at")]
    public string? SeenAt { get; set; }

    [JsonPropertyName("completed_at")]
    public string? CompletedAt { get; set; }

    [JsonPropertyName("skipped_at")]
    public string? SkippedAt { get; set; }
}

public class InventoryResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("items")]
    public List<ItemCatalogEntry> Items { get; set; } = [];
}

public class ItemShopResponse
{
    [JsonPropertyName("items")]
    public List<ItemCatalogEntry> Items { get; set; } = [];
}

public class ItemCatalogEntry
{
    [JsonPropertyName("item_id")]
    public string ItemId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("slot_kind")]
    public string SlotKind { get; set; } = string.Empty;

    [JsonPropertyName("rarity")]
    public string Rarity { get; set; } = string.Empty;

    [JsonPropertyName("price")]
    public int Price { get; set; }

    [JsonPropertyName("stackable")]
    public bool Stackable { get; set; }

    [JsonPropertyName("max_per_run")]
    public int MaxPerRun { get; set; } = 1;

    [JsonPropertyName("effect_config")]
    public Dictionary<string, object?> EffectConfig { get; set; } = [];

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("stock")]
    public int Stock { get; set; }
}
