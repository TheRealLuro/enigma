namespace Enigma.Client.Models.Gameplay;

public sealed class PlayerIdentity
{
    public string Username { get; set; } = string.Empty;
}

public sealed class GameCompletionSummary
{
    public string Seed { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string CompletionTime { get; set; } = string.Empty;
    public int GoldCollected { get; set; }
    public string Source { get; set; } = "new";
    public string? LoadedMapName { get; set; }
    public string Difficulty { get; set; } = string.Empty;
    public int Size { get; set; }
    public bool HasBeenSubmitted { get; set; }
}
