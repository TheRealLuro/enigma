namespace Enigma.Client.Models.Gameplay;

public class ActiveGameSession
{
    public string Seed { get; set; } = string.Empty;
    public string? MapName { get; set; }
    public string Source { get; set; } = "new";
    public string Username { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;
    public int Size { get; set; }
    public string CurrentRoomLabel { get; set; } = string.Empty;
    public string SavedAtUtc { get; set; } = string.Empty;
}
