namespace Enigma.Client.Models;

public sealed class TutorialStepDefinition
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string Eyebrow { get; init; } = string.Empty;
    public string HelperText { get; init; } = string.Empty;
}
