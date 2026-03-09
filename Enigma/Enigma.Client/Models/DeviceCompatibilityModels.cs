using System.Text.Json.Serialization;

namespace Enigma.Client.Models;

public enum DeviceCompatibilityClass
{
    DesktopPlayable,
    TabletBrowseOnly,
    MobileBrowseOnly,
    UnknownFallback,
}

public enum GameplayCompatibilityDecision
{
    Allowed,
    BlockedBrowseOnly,
    BlockedUnknown,
}

public sealed class DeviceCapabilitySnapshot
{
    [JsonPropertyName("viewportWidth")]
    public int ViewportWidth { get; set; }

    [JsonPropertyName("viewportHeight")]
    public int ViewportHeight { get; set; }

    [JsonPropertyName("orientation")]
    public string Orientation { get; set; } = "unknown";

    [JsonPropertyName("hasTouch")]
    public bool HasTouch { get; set; }

    [JsonPropertyName("maxTouchPoints")]
    public int MaxTouchPoints { get; set; }

    [JsonPropertyName("primaryPointerFine")]
    public bool PrimaryPointerFine { get; set; }

    [JsonPropertyName("primaryPointerCoarse")]
    public bool PrimaryPointerCoarse { get; set; }

    [JsonPropertyName("canHover")]
    public bool CanHover { get; set; }

    [JsonPropertyName("anyFinePointer")]
    public bool AnyFinePointer { get; set; }

    [JsonPropertyName("anyCoarsePointer")]
    public bool AnyCoarsePointer { get; set; }

    [JsonPropertyName("userAgentMobile")]
    public bool UserAgentMobile { get; set; }
}
