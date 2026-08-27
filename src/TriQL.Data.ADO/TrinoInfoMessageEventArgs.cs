using TriQL.Client;

namespace TriQL.Data.ADO;

/// <summary>
/// Carries a query progress snapshot or a query failure for <see cref="TrinoConnection.InfoMessage"/>. See FR-9.1.14.
/// </summary>
public sealed class TrinoInfoMessageEventArgs : EventArgs
{
    internal TrinoInfoMessageEventArgs(TrinoQueryStats? stats, Exception? error)
    {
        Stats = stats;
        Error = error;
    }

    /// <summary>The latest query statistics, if this notification carries progress.</summary>
    public TrinoQueryStats? Stats { get; }

    /// <summary>The query failure, if this notification carries an error.</summary>
    public Exception? Error { get; }
}
