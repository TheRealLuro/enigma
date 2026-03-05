namespace Enigma.Client.Models.Gameplay;

public sealed class RunLoadoutSelection
{
    public string ItemId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SlotKind { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public Dictionary<string, object?> EffectConfig { get; set; } = [];
}
