using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using TriQL.EntityFrameworkCore.Diagnostics.Internal;
using TriQL.EntityFrameworkCore.Infrastructure.Internal;
using TriQL.EntityFrameworkCore.Storage.Internal;

namespace TriQL.EntityFrameworkCore.Update.Internal;

/// <summary>Executes <c>SaveChanges</c> command batches one after another, without a transaction.</summary>
/// <remarks>
/// <para>
/// EF's default executor wraps multi-statement saves in a transaction, which Trino cannot provide, so each
/// statement commits on its own. Consequently:
/// </para>
/// <list type="bullet">
/// <item><see cref="TrinoEventId.NonAtomicSaveChanges"/> is logged when a save needs more than one statement.</item>
/// <item>If a statement fails, the entities of the statements that already committed are marked as saved, so a
/// later <c>SaveChanges</c> does not apply them twice. The failed and unexecuted ones keep their pending state.</item>
/// <item>With <c>EnableRetryOnFailure</c>, a statement Trino reports as failed for a transient reason (such as
/// <c>ICEBERG_COMMIT_ERROR</c>) is retried on its own. Retrying the whole save would repeat statements that
/// already committed, so the failure is marked as not retryable for the execution strategy.</item>
/// </list>
/// <para>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </para>
/// </remarks>
public class TrinoBatchExecutor : IBatchExecutor
{
    private readonly IDiagnosticsLogger<DbLoggerCategory.Update> _updateLogger;
    private readonly int _maxRetryCount;
    private readonly TimeSpan _maxRetryDelay;

    /// <summary>Initializes a new instance.</summary>
    public TrinoBatchExecutor(IDbContextOptions options, IDiagnosticsLogger<DbLoggerCategory.Update> updateLogger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _updateLogger = updateLogger;
        var extension = options.FindExtension<TrinoOptionsExtension>();
        _maxRetryCount = extension?.MaxRetryCount ?? 0;
        _maxRetryDelay = extension?.MaxRetryDelay ?? TimeSpan.Zero;
    }

    /// <inheritdoc />
    public virtual int Execute(IEnumerable<ModificationCommandBatch> commandBatches, IRelationalConnection connection)
    {
        ArgumentNullException.ThrowIfNull(commandBatches);
        ArgumentNullException.ThrowIfNull(connection);

        var committed = new List<ModificationCommandBatch>();
        connection.Open();
        try
        {
            foreach (var batch in commandBatches)
            {
                WarnIfNonAtomic(committed);
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        batch.Execute(connection);
                        break;
                    }
                    catch (Exception ex) when (ShouldRetry(ex, attempt))
                    {
                        Thread.Sleep(TrinoTransientExceptionDetector.GetDelay(attempt, _maxRetryDelay));
                    }
                }

                committed.Add(batch);
            }
        }
        catch (Exception ex)
        {
            AcceptCommitted(committed);
            TrinoTransientExceptionDetector.MarkNotRetryable(ex);
            throw;
        }
        finally
        {
            connection.Close();
        }

        return committed.Sum(batch => batch.ModificationCommands.Count);
    }

    /// <inheritdoc />
    public virtual async Task<int> ExecuteAsync(
        IEnumerable<ModificationCommandBatch> commandBatches,
        IRelationalConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandBatches);
        ArgumentNullException.ThrowIfNull(connection);

        var committed = new List<ModificationCommandBatch>();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var batch in commandBatches)
            {
                WarnIfNonAtomic(committed);
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        await batch.ExecuteAsync(connection, cancellationToken).ConfigureAwait(false);
                        break;
                    }
                    catch (Exception ex) when (ShouldRetry(ex, attempt))
                    {
                        await Task.Delay(TrinoTransientExceptionDetector.GetDelay(attempt, _maxRetryDelay), cancellationToken)
                            .ConfigureAwait(false);
                    }
                }

                committed.Add(batch);
            }
        }
        catch (Exception ex)
        {
            AcceptCommitted(committed);
            TrinoTransientExceptionDetector.MarkNotRetryable(ex);
            throw;
        }
        finally
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }

        return committed.Sum(batch => batch.ModificationCommands.Count);
    }

    // Warn once, as the second statement of a save is about to run.
    private void WarnIfNonAtomic(List<ModificationCommandBatch> committed)
    {
        if (committed.Count == 1)
        {
            _updateLogger.NonAtomicSaveChanges();
        }
    }

    // A statement Trino failed did not commit, so retrying it alone is safe. Concurrency conflicts are never retried.
    private bool ShouldRetry(Exception exception, int attempt) =>
        attempt <= _maxRetryCount
        && exception is not DbUpdateConcurrencyException
        && TrinoTransientExceptionDetector.ShouldRetryStatementOn(exception);

    private static void AcceptCommitted(List<ModificationCommandBatch> committed)
    {
        foreach (var entry in committed.SelectMany(b => b.ModificationCommands).SelectMany(c => c.Entries))
        {
            if (entry is InternalEntityEntry internalEntry)
            {
                internalEntry.AcceptChanges();
            }
        }
    }
}
