using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;

namespace TriQL.EntityFrameworkCore.Update.Internal;

/// <summary>
/// A <c>SaveChanges</c> batch of exactly one statement: Trino runs one statement per request. The rows
/// affected come from the <c>rows</c> column of the statement's result.
/// </summary>
/// <remarks>
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
    /// <summary>Initializes a new instance.</summary>
    /// <param name="dependencies">Service dependencies.</param>
    public TrinoModificationCommandBatch(ModificationCommandBatchFactoryDependencies dependencies)
        : base(dependencies, maxBatchSize: 1)
    {
    }

    /// <inheritdoc />
    public override bool TryAddCommand(IReadOnlyModificationCommand modificationCommand) =>
        ModificationCommands.Count == 0 && base.TryAddCommand(modificationCommand);

    /// <summary>
    /// Checks the statement's <c>rows</c> count against the number of rows the command expected to change,
    /// throwing <c>DbUpdateConcurrencyException</c> on a mismatch. A <c>NULL</c> count is 0 rows.
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
