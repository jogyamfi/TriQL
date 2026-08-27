namespace TriQL.Client;

/// <summary>
/// A single page of a query response: zero or more rows plus the schema and statistics known at
/// that point. See FR-6.7.
/// </summary>
public sealed class TrinoPage
{
    internal TrinoPage(
        IReadOnlyList<TrinoColumn> columns,
        IReadOnlyList<object?[]> rawRows,
        bool valuesAreDecoded,
        TrinoQueryStats? stats,
        string? updateType,
        long? updateCount)
    {
        Columns = columns;
        RawRows = rawRows;
        ValuesAreDecoded = valuesAreDecoded;
        Stats = stats;
        UpdateType = updateType;
        UpdateCount = updateCount;
    }

    /// <summary>The column schema known as of this page. Empty until the first schema-bearing page arrives.</summary>
    public IReadOnlyList<TrinoColumn> Columns { get; }

    /// <summary>The number of rows carried by this page.</summary>
    public int RowCount => RawRows.Count;

    /// <summary>The query statistics reported with this page, if any.</summary>
    public TrinoQueryStats? Stats { get; }

    /// <summary>The server-reported update type for DDL/DML statements, if any.</summary>
    public string? UpdateType { get; }

    /// <summary>The server-reported affected-row count for DDL/DML statements, if any.</summary>
    public long? UpdateCount { get; }

    /// <summary>The raw decoded row values. See <see cref="TrinoRow"/> remarks on materialization.</summary>
    internal IReadOnlyList<object?[]> RawRows { get; }

    /// <summary>Whether <see cref="RawRows"/> scalars were already materialized by <c>Utf8RowDecoder</c>.</summary>
    internal bool ValuesAreDecoded { get; }
}
