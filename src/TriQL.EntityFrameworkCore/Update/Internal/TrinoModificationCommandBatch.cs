using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;

namespace TriQL.EntityFrameworkCore.Update.Internal;

/// <summary>
/// A <c>SaveChanges</c> batch that is always a single statement, as Trino runs one statement per request.
/// Consecutive inserts into the same table with the same columns are combined into one multi-row
/// <c>INSERT … VALUES (…), (…)</c>, which saves a round trip per row, commits the rows atomically, and
/// creates one Iceberg snapshot instead of one per row. Updates and deletes run one per batch.
/// </summary>
/// <remarks>
/// <para>
/// All rows of a combined insert share one <c>rows</c> count, which is checked against the number of rows
/// (every command but the last is mapped as <c>NotLastInResultSet</c>). A batch holds at most
/// <see cref="MaxParameters"/> parameters: Trino itself accepted 30,000 in one statement (measured on 466),
/// but with the default prepared-statement binding the statement text travels in an HTTP header, which
/// proxies and gateways often limit to 8–16 KB.
/// </para>
/// <para>
/// Measured on Trino 466: an Iceberg <c>DELETE</c> that Trino runs as a metadata-only operation (its
/// predicate selects whole partitions) and that matches nothing reports <c>rows</c> as <c>NULL</c> rather
/// than 0. A <c>NULL</c> count is read as 0 rows, so a key-based delete of a row another writer already
/// removed is still reported as a concurrency conflict.
/// </para>
/// <para>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </para>
/// </remarks>
public class TrinoModificationCommandBatch : AffectedCountModificationCommandBatch
{
    /// <summary>The most parameters a combined insert may bind.</summary>
    public const int MaxParameters = 2000;

    private const ResultSetMapping LastRow = ResultSetMapping.LastInResultSet | ResultSetMapping.ResultSetWithRowsAffectedOnly;
    private const ResultSetMapping EarlierRow = ResultSetMapping.NotLastInResultSet | ResultSetMapping.ResultSetWithRowsAffectedOnly;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="dependencies">Service dependencies.</param>
    /// <param name="maxBatchSize">The most rows a combined insert may contain.</param>
    public TrinoModificationCommandBatch(ModificationCommandBatchFactoryDependencies dependencies, int maxBatchSize)
        : base(dependencies, maxBatchSize)
    {
    }

    /// <inheritdoc />
    public override bool TryAddCommand(IReadOnlyModificationCommand modificationCommand)
    {
        ArgumentNullException.ThrowIfNull(modificationCommand);

        if (ModificationCommands.Count > 0)
        {
            // Only further rows of the same INSERT join a non-empty batch, within the parameter budget.
            var parameterCount = modificationCommand.ColumnModifications.Count(o => o.UseParameter);
            if (UpdateSqlGenerator is not TrinoUpdateSqlGenerator
                || !TrinoUpdateSqlGenerator.CanShareInsert(ModificationCommands[0], modificationCommand)
                || ParameterValues.Count + parameterCount > MaxParameters)
            {
                return false;
            }
        }

        return base.TryAddCommand(modificationCommand);
    }

    /// <inheritdoc />
    protected override void AddCommand(IReadOnlyModificationCommand modificationCommand)
    {
        ArgumentNullException.ThrowIfNull(modificationCommand);

        if (ModificationCommands.Count == 0)
        {
            base.AddCommand(modificationCommand);
            return;
        }

        // Continue the INSERT: drop the statement's trailing line break, then append ", (values)".
        while (SqlBuilder.Length > 0 && char.IsWhiteSpace(SqlBuilder[^1]))
        {
            SqlBuilder.Length--;
        }

        SqlBuilder.Append(',').AppendLine();
        ((TrinoUpdateSqlGenerator)UpdateSqlGenerator).AppendInsertValuesRow(SqlBuilder, modificationCommand);
        SqlBuilder.AppendLine();

        ResultSetMappings[^1] = EarlierRow;
        ResultSetMappings.Add(LastRow);
        AddParameters(modificationCommand);
    }

    /// <inheritdoc />
    protected override void RollbackLastCommand(IReadOnlyModificationCommand modificationCommand)
    {
        base.RollbackLastCommand(modificationCommand);

        // The row before the rolled-back one is the last again.
        if (ResultSetMappings.Count > 0)
        {
            ResultSetMappings[^1] = LastRow;
        }
    }

    /// <summary>
    /// Checks the statement's <c>rows</c> count against the number of rows the commands it covers expected
    /// to change, throwing <c>DbUpdateConcurrencyException</c> on a mismatch. A <c>NULL</c> count is 0 rows.
    /// </summary>
    protected override int ConsumeResultSetWithRowsAffectedOnly(int commandIndex, RelationalDataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var (expected, next) = ExpectedRows(commandIndex);
        var actual = reader.Read() && !reader.DbDataReader.IsDBNull(0) ? reader.DbDataReader.GetInt64(0) : 0;
        if (actual != expected)
        {
            ThrowAggregateUpdateConcurrencyException(reader, next, expected, (int)Math.Min(actual, int.MaxValue));
        }

        return next;
    }

    /// <inheritdoc cref="ConsumeResultSetWithRowsAffectedOnly" />
    protected override async Task<int> ConsumeResultSetWithRowsAffectedOnlyAsync(
        int commandIndex,
        RelationalDataReader reader,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var (expected, next) = ExpectedRows(commandIndex);
        var actual = await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            && !await reader.DbDataReader.IsDBNullAsync(0, cancellationToken).ConfigureAwait(false)
                ? reader.DbDataReader.GetInt64(0)
                : 0;
        if (actual != expected)
        {
            await ThrowAggregateUpdateConcurrencyExceptionAsync(reader, next, expected, (int)Math.Min(actual, int.MaxValue), cancellationToken)
                .ConfigureAwait(false);
        }

        return next;
    }

    /// <summary>
    /// The rows the result set at <paramref name="commandIndex"/> should report (one per command it covers),
    /// and the index of the first command after them, which is what EF's consumer loop continues from.
    /// </summary>
    private (int Expected, int Next) ExpectedRows(int commandIndex)
    {
        var expected = 1;
        while (++commandIndex < ResultSetMappings.Count && ResultSetMappings[commandIndex - 1].HasFlag(ResultSetMapping.NotLastInResultSet))
        {
            expected++;
        }

        return (expected, commandIndex);
    }
}
