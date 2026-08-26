namespace TriQL.Client;

/// <summary>
/// The final summary of a non-query (DDL/DML) statement, or of a query drained to completion via
/// <see cref="TrinoResultSet.DrainAsync"/>. See FR-6.12.
/// </summary>
/// <param name="UpdateType">The server-reported update type, e.g. <c>CREATE TABLE</c>, if any.</param>
/// <param name="UpdateCount">The number of rows affected, if reported.</param>
/// <param name="Stats">The final query statistics, if any page carried them.</param>
public sealed record TrinoExecutionSummary(string? UpdateType, long? UpdateCount, TrinoQueryStats? Stats);
