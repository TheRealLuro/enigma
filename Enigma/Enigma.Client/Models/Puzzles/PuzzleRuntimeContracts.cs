namespace Enigma.Client.Models.Gameplay;

public enum PuzzleStatus
{
    NotStarted,
    Active,
    Solved,
    FailedTemporary,
    Resetting,
    Cooldown,
    HintAvailable,
    HintConsumed,
}

public enum PuzzlePhase
{
    Observe,
    Configure,
    Commit,
    Resolve,
}

public enum PuzzleInteractionSource
{
    Keyboard,
    Click,
    Proximity,
}

public readonly record struct PuzzleProgressState(PlayAreaRect AnchorRect, double Progress, string Label);

public readonly record struct PuzzleSolveStep(
    string? InteractableId,
    double AdvanceSeconds = 0d,
    PuzzleInteractionSource Source = PuzzleInteractionSource.Keyboard);

public sealed record PuzzleSolveTrace(IReadOnlyList<PuzzleSolveStep> Steps, string? Note = null);
public readonly record struct PuzzleSolveResult(string StatusText, int InteractionCount, int FailureCount);
public readonly record struct PuzzleTelemetryStat(string Label, string Value);

public sealed record PuzzleWorldInteractable(
    string Id,
    PlayAreaRect Bounds,
    string CssClass,
    string Label,
    bool Enabled = true,
    int Priority = 0,
    double InteractionRange = 170d,
    bool Clickable = true);

public interface IPuzzleLifecycle
{
    PuzzleStatus Status { get; }
    PuzzlePhase Phase { get; }
    PuzzleSolveResult? SolveResult { get; }
    bool IsSolved { get; }
    bool IsFailed { get; }
    bool CanInteract { get; }
    string GetStatusText();
}

public interface ITimedPuzzleUpdate
{
    void Update(double nowSeconds, double deltaSeconds);
}

public interface IWorldInteractivePuzzle : IPuzzleLifecycle, ITimedPuzzleUpdate
{
    IReadOnlyList<PuzzleWorldInteractable> GetWorldInteractables();

    bool TryInteract(
        string interactableId,
        PuzzleInteractionSource source,
        PlayAreaRect playerBounds,
        PlayerDirection playerFacing,
        double nowSeconds);

    bool TryGetProgressState(out PuzzleProgressState progressState);
}

public interface ISolverBackedWorldPuzzle
{
    bool TryBuildSolveTrace(out PuzzleSolveTrace trace);
}

public interface IWorldPuzzleTelemetry
{
    IReadOnlyList<PuzzleTelemetryStat> GetTelemetryStats();
}
