namespace TriQL.Client;

/// <summary>
/// A snapshot of query execution statistics reported with each page.
/// </summary>
public sealed record TrinoQueryStats(
    string State,
    bool Queued,
    bool Scheduled,
    int Nodes,
    int TotalSplits,
    int QueuedSplits,
    int RunningSplits,
    int CompletedSplits,
    TimeSpan CpuTime,
    TimeSpan WallTime,
    TimeSpan QueuedTime,
    TimeSpan ElapsedTime,
    long ProcessedRows,
    long ProcessedBytes,
    long PhysicalInputBytes,
    long PeakMemoryBytes,
    long SpilledBytes,
    double? ProgressPercentage,
    TrinoStageStats? RootStage);
