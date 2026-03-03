namespace Enigma.Client.Models.Gameplay;

public sealed class PendingLossSummary
{
    public string RunNonce { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Seed { get; set; } = string.Empty;
    public string? MapName { get; set; }
    public string Source { get; set; } = "new";
    public string Difficulty { get; set; } = string.Empty;
    public string ThemeLabel { get; set; } = string.Empty;
    public int MapValue { get; set; }
    public int ForfeitedRunPayout { get; set; }
    public int ProjectedCompletionPayout { get; set; }
    public List<string> UsedItems { get; set; } = [];
    public string Reason { get; set; } = "abandoned";
    public string AbandonedAtUtc { get; set; } = string.Empty;
    public bool IsMultiplayer { get; set; }
    public string? MultiplayerSessionId { get; set; }
    public string? PartnerUsername { get; set; }
}
