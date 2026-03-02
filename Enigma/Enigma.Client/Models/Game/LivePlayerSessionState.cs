namespace Enigma.Client.Models.Gameplay;

public class LivePlayerSessionState
{
    public string Username { get; set; } = string.Empty;
    public string Seed { get; set; } = string.Empty;
    public string? MapName { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;
    public int MapSize { get; set; }
    public string CurrentRoomLabel { get; set; } = string.Empty;
    public LiveRoomState Room { get; set; } = new();
    public LivePositionState Position { get; set; } = new();
    public string Facing { get; set; } = string.Empty;
    public bool IsMoving { get; set; }
    public int GoldCollected { get; set; }
    public string ElapsedTime { get; set; } = string.Empty;
    public string UpdatedAtUtc { get; set; } = string.Empty;
}

public class LiveRoomState
{
    public int X { get; set; }
    public int Y { get; set; }
    public string Kind { get; set; } = string.Empty;
}

public class LivePositionState
{
    public double X { get; set; }
    public double Y { get; set; }
    public double XPercent { get; set; }
    public double YPercent { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}
