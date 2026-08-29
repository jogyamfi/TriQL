using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using TriQL.Client.Diagnostics;
using TriQL.Client.Exceptions;
using TriQL.Client.Internal;

namespace TriQL.Client;

/// <summary>
/// The streaming result of a query submitted via <see cref="TrinoClient.ExecuteAsync(string, TrinoParameterCollection?, bool, System.Threading.CancellationToken)"/>. Pages are
/// fetched on a background task while the consumer processes already-buffered rows (FR-6.1);
/// disposing before enumeration completes cancels the query server-side (FR-4.6.6).
/// </summary>
public sealed class TrinoResultSet : IAsyncDisposable, IDisposable
{
    private readonly StatementClient _statementClient;
    private readonly TrinoSessionOptions _options;
    private readonly ILogger? _logger;
    private readonly CancellationTokenSource _linkedCts;
    private readonly CancellationToken _callerToken;
    private readonly PageBuffer _buffer;
    private readonly Task _pumpTask;
    private readonly TaskCompletionSource<IReadOnlyList<TrinoColumn>> _schemaTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly QueryStateMachine _state = new();
    private readonly Stopwatch _stopwatch;
    private readonly Activity? _queryActivity;
    private readonly KeyValuePair<string, object?> _queryIdTag;
    private bool _firstRowObserved;
    private IReadOnlyList<TrinoColumn> _columns;
    private TrinoQueryStats? _lastStats;
    private string? _updateType;
    private long? _updateCount;
    private long _rowsProduced;
    private int _disposed;

    internal TrinoResultSet(
        StatementClient statementClient,
        TrinoPageEnvelope initial,
        TrinoSessionOptions options,
        ILogger? logger,
        CancellationTokenSource linkedCts,
        Activity? queryActivity,
        Stopwatch stopwatch,
        CancellationToken callerToken)
    {
        _statementClient = statementClient;
        _options = options;
        _logger = logger;
        _linkedCts = linkedCts;
        _callerToken = callerToken;
        _queryActivity = queryActivity;
        _stopwatch = stopwatch;
        QueryId = initial.QueryId;
        _queryIdTag = new KeyValuePair<string, object?>("trino.query_id", QueryId);
        InfoUri = initial.InfoUri;
        _columns = initial.Columns ?? [];
        _updateType = initial.UpdateType;
        _updateCount = initial.UpdateCount;

        if (_columns.Count > 0)
        {
            _schemaTcs.TrySetResult(_columns);
        }

        _state.MarkRunning();
        _buffer = new PageBuffer(options.ReadAheadBufferBytes);
        var backoff = new PollingBackoff(options.PollingBackoffInitialDelay, options.PollingBackoffMultiplier, options.PollingBackoffMaxDelay);
        _pumpTask = Task.Run(() => RunPumpAsync(initial, backoff), CancellationToken.None);
    }

    /// <summary>The id of the underlying query.</summary>
    public string QueryId { get; }

    /// <summary>The Trino web UI deep-link for this query, if reported. See FR-10.4.</summary>
    public Uri? InfoUri { get; }

    /// <summary>The column schema, once known. Empty until <see cref="WaitForSchemaAsync"/> completes or a row is read.</summary>
    public IReadOnlyList<TrinoColumn> Columns => _columns;

    /// <summary>Whether this is a row-producing query, as opposed to a DDL/DML statement.</summary>
    public bool IsQuery => _columns.Count > 0;

    /// <summary>The server-reported update type for DDL/DML statements, once known.</summary>
    public string? UpdateType => _updateType;

    /// <summary>The server-reported affected-row count for DDL/DML statements, once known.</summary>
    public long? UpdateCount => _updateCount;

    /// <summary>The client-observed lifecycle state. See FR-4.5.2.</summary>
    public TrinoQueryState State => _state.Current;

    /// <summary>The last server-reported query state string (e.g. <c>RUNNING</c>), once known. See FR-4.5.2.</summary>
    public string? ServerState => _lastStats?.State;

    /// <summary>Raised after each page with the latest statistics. See FR-11.3.1.</summary>
    public event EventHandler<TrinoQueryProgressEventArgs>? Progress;

    /// <summary>An alternative progress sink to <see cref="Progress"/>, invoked identically. See FR-11.3.1.</summary>
    public IProgress<TrinoQueryStats>? ProgressReporter { get; set; }

    /// <summary>
    /// Completes as soon as the column schema is known, without requiring a row to be read. See FR-6.9.
    /// </summary>
    public Task<IReadOnlyList<TrinoColumn>> WaitForSchemaAsync(CancellationToken cancellationToken = default) =>
        _schemaTcs.Task.WaitAsync(cancellationToken);

    /// <summary>The page-level streaming surface. See FR-6.7.</summary>
    public async IAsyncEnumerable<TrinoPage> ReadPagesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Cancelling `cancellationToken` stops the producer promptly via this registration (FR-6.10).
        // The buffer read itself is deliberately NOT tied to `_linkedCts.Token`: that token is also
        // what the producer observes, and racing the same token on both sides would let a bare
        // OperationCanceledException reach the consumer directly from the channel, bypassing the
        // producer's classification into TrinoTimeoutException vs. plain cancellation (FR-4.6.5).
        using var registration = cancellationToken.CanBeCanceled
            ? cancellationToken.Register(static state => ((CancellationTokenSource)state!).Cancel(), _linkedCts)
            : default;

        await foreach (var page in _buffer.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
        {
            ObservePage(page);
            yield return page;
        }
    }

    /// <summary>The primary row-level streaming surface. See FR-6.6.</summary>
    public async IAsyncEnumerable<TrinoRow> ReadRowsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var page in ReadPagesAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var values in page.RawRows)
            {
                yield return new TrinoRow(page.Columns, values, page.ValuesAreDecoded);
            }
        }
    }

    /// <summary>
    /// Drains every row without materializing them, for DDL/DML statements or callers that only
    /// need <see cref="UpdateType"/>/<see cref="UpdateCount"/>. See FR-6.12.
    /// </summary>
    public async Task<TrinoExecutionSummary> DrainAsync(CancellationToken cancellationToken = default)
    {
        await foreach (var _ in ReadRowsAsync(cancellationToken).ConfigureAwait(false))
        {
            // Intentionally discarded: this path exists for callers that only need the summary.
        }

        return new TrinoExecutionSummary(UpdateType, UpdateCount, _lastStats);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        if (!_pumpTask.IsCompleted)
        {
            await _linkedCts.CancelAsync().ConfigureAwait(false);
        }

        try
        {
            await _pumpTask.ConfigureAwait(false);
        }
        catch
        {
            // Already surfaced to consumers via the buffer; disposal must not throw.
        }

        _buffer.Dispose();
        _linkedCts.Dispose();
    }

    /// <summary>
    /// Signals cancellation and returns immediately without blocking on the background pump
    /// (NFR-REL-2 forbids sync-over-async). The pump still performs its own bounded (FR-4.6.2)
    /// server-side cancellation in the background. Prefer <see cref="DisposeAsync"/> for a
    /// deterministic bounded wait.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _buffer.Dispose();

        if (!_pumpTask.IsCompleted)
        {
            _linkedCts.Cancel();
        }
    }

    private void ObservePage(TrinoPage page)
    {
        CaptureSchema(page.Columns);

        if (page.UpdateType is not null)
        {
            _updateType = page.UpdateType;
        }

        if (page.UpdateCount is not null)
        {
            _updateCount = page.UpdateCount;
        }

        if (page.Stats is { } stats)
        {
            _lastStats = stats;
            RaiseProgress(stats);
        }
    }

    /// <summary>
    /// Publishes the column schema the first time it is seen. Called from both the background pump
    /// (so <see cref="WaitForSchemaAsync"/> completes as soon as the schema arrives on the wire,
    /// not only once a consumer reads that page) and the consumer's <see cref="ObservePage"/>.
    /// </summary>
    private void CaptureSchema(IReadOnlyList<TrinoColumn> columns)
    {
        if (columns.Count > 0 && _columns.Count == 0)
        {
            _columns = columns;
            _schemaTcs.TrySetResult(columns);
        }
    }

    private void RaiseProgress(TrinoQueryStats stats)
    {
        try
        {
            Progress?.Invoke(this, new TrinoQueryProgressEventArgs(stats));
            ProgressReporter?.Report(stats);
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                Log.ProgressCallbackFailed(_logger, ex);
            }
        }
    }

    private async Task RunPumpAsync(TrinoPageEnvelope initial, PollingBackoff backoff)
    {
        Exception? failure = null;
        try
        {
            await foreach (var page in PageReader.ReadPagesAsync(_statementClient, initial, backoff, _linkedCts.Token).ConfigureAwait(false))
            {
                if (_logger is not null)
                {
                    Log.PageReceived(_logger, QueryId, page.RowCount);
                }

                _rowsProduced += page.RowCount;
                CaptureSchema(page.Columns);
                var sizeBytes = PayloadSizeEstimator.EstimateRows(page.RawRows);
                await _buffer.EnqueueAsync(page, sizeBytes, _linkedCts.Token).ConfigureAwait(false);

                Metrics.PagesReceived.Add(1, _queryIdTag);
                Metrics.RowsRead.Add(page.RowCount, _queryIdTag);
                Metrics.BytesReceived.Add(sizeBytes, _queryIdTag);
                if (!_firstRowObserved && page.RowCount > 0)
                {
                    _firstRowObserved = true;
                    Metrics.TimeToFirstRow.Record(_stopwatch.Elapsed.TotalMilliseconds, _queryIdTag);
                }
            }

            _state.TryFinish();
        }
        catch (OperationCanceledException oce) when (_linkedCts.IsCancellationRequested)
        {
            await _statementClient.CancelAsync().ConfigureAwait(false);

            if (!_callerToken.IsCancellationRequested && _options.QueryTimeout is { } configuredTimeout)
            {
                _state.TryTimeout();
                failure = new TrinoTimeoutException(
                    $"The query exceeded the configured QueryTimeout of {configuredTimeout}.", configuredTimeout, _stopwatch.Elapsed, QueryId);
                Metrics.QueriesFailed.Add(1, _queryIdTag);
            }
            else
            {
                _state.TryCancel();
                failure = oce;
                Metrics.QueriesCancelled.Add(1, _queryIdTag);
            }
        }
        catch (Exception ex)
        {
            _state.TryFail();
            failure = ex;
            Metrics.QueriesFailed.Add(1, _queryIdTag);
        }

        Metrics.QueryDuration.Record(_stopwatch.Elapsed.TotalMilliseconds, _queryIdTag);
        if (failure is not null)
        {
            Tracing.RecordFailure(_queryActivity, failure);
        }

        _queryActivity?.Dispose();

        if (_logger is not null && failure is null)
        {
            Log.QueryCompleted(_logger, QueryId, _stopwatch.Elapsed, _rowsProduced);
        }

        SettleSchema(failure);
        _buffer.Complete(failure);
    }

    /// <summary>
    /// Guarantees <see cref="WaitForSchemaAsync"/> always completes once the query is over. A DDL/DML
    /// statement never carries columns, and a query that fails before its first schema-bearing page
    /// never carries them either — without this, both would wait forever (FR-6.9).
    /// </summary>
    private void SettleSchema(Exception? failure)
    {
        if (failure is null)
        {
            _schemaTcs.TrySetResult(_columns);
            return;
        }

        if (_schemaTcs.TrySetException(failure))
        {
            // Nothing may ever await the schema task, so observe the fault here to keep it from
            // resurfacing as an unobserved TaskScheduler exception at finalization.
            _ = _schemaTcs.Task.Exception;
        }
    }
}
