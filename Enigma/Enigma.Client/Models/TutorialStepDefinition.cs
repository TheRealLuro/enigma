namespace Enigma.Client.Models;

public sealed class TutorialStepDefinition
{
    public string Chapter { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Objective { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public List<string> MatchPaths { get; init; } = [];
    public bool AllowPathPrefixMatch { get; init; }
    public string Eyebrow { get; init; } = string.Empty;
    public string HelperText { get; init; } = string.Empty;
    public string CompletionEventKey { get; init; } = string.Empty;
}
