namespace TriQL.Client;

/// <summary>
/// Carries the latest <see cref="TrinoQueryStats"/> for <see cref="TrinoResultSet.Progress"/>. See FR-11.3.1.
/// </summary>
public sealed class TrinoQueryProgressEventArgs(TrinoQueryStats stats) : EventArgs
{
    /// <summary>The latest query statistics.</summary>
    public TrinoQueryStats Stats { get; } = stats;
}
