namespace TriQL.Client;

/// <summary>
/// A single node of the <see cref="TrinoQueryStats.RootStage"/> tree. See FR-11.3.3.
/// </summary>
/// <param name="StageId">The stage id, if reported.</param>
/// <param name="State">The server-reported stage state.</param>
/// <param name="Done">Whether the stage has finished scheduling.</param>
/// <param name="Nodes">The number of worker nodes participating in the stage.</param>
/// <param name="TotalSplits">The total number of splits for the stage.</param>
/// <param name="QueuedSplits">The number of splits still queued.</param>
/// <param name="RunningSplits">The number of splits currently running.</param>
/// <param name="CompletedSplits">The number of splits that have completed.</param>
/// <param name="SubStages">The stage's child stages.</param>
public sealed record TrinoStageStats(
    string? StageId,
    string? State,
    bool Done,
    int Nodes,
    int TotalSplits,
    int QueuedSplits,
    int RunningSplits,
    int CompletedSplits,
    IReadOnlyList<TrinoStageStats> SubStages);
