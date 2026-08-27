using Microsoft.Extensions.Logging;
using System.Diagnostics;
using TriQL.Client.Exceptions;
using TriQL.Client.Internal;

namespace TriQL.Client;

/// <summary>
/// Entry point for a Trino session: submits statements (from Phase 2 onward) and exposes
/// <c>/v1/info</c> and <c>/v1/query/{id}</c>. Thread-safe. See FR-1, FR-10, NFR-REL-1.
/// </summary>
/// <remarks>
/// <see cref="HttpMessageInvoker"/> is the supported extension point for externally managed HTTP
/// pipelines (FR-3.1.2), including clients created by <c>IHttpClientFactory</c> — pass the created
/// <see cref="HttpClient"/> directly, since <see cref="HttpClient"/> derives from
/// <see cref="HttpMessageInvoker"/>. TriQL.Client intentionally does not reference
/// <c>Microsoft.Extensions.Http</c> so the core package stays dependency-free (REQ-ARCH-4).
/// </remarks>
public sealed class TrinoClient : IAsyncDisposable, IDisposable
{
    private readonly HttpMessageInvoker _invoker;
    private readonly bool _ownsInvoker;
    private readonly ILogger? _logger;
    private bool _disposed;
    private int _serverVersionLogged;

    /// <summary>
    /// Initializes a new instance. When <paramref name="invoker"/> is <see langword="null"/>, the
    /// client builds and owns its own HTTP handler (disposed with the client); when supplied —
    /// including a plain <see cref="HttpClient"/>, or one created by an external
    /// <c>IHttpClientFactory</c> — it is treated as externally owned and never disposed by this
    /// instance (FR-3.1.2, FR-3.1.3).
    /// </summary>
    public TrinoClient(TrinoSessionOptions options, HttpMessageInvoker? invoker = null, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        Options = options;
        Session = new TrinoSession(options);
        _logger = loggerFactory?.CreateLogger<TrinoClient>();

        if (invoker is null)
        {
            _invoker = CreateOwnedInvoker(options, loggerFactory);
            _ownsInvoker = true;
        }
        else
        {
            _invoker = invoker;
            _ownsInvoker = false;
        }
    }

    /// <summary>The options this client was constructed with.</summary>
    public TrinoSessionOptions Options { get; }

    /// <summary>The live session state.</summary>
    public TrinoSession Session { get; }

    /// <summary><c>GET /v1/info</c>. See FR-10.1.</summary>
    public async Task<TrinoServerInfo> GetServerInfoAsync(CancellationToken cancellationToken = default)
    {
        var info = await InfoClient.GetServerInfoAsync(_invoker, Options, _logger, cancellationToken).ConfigureAwait(false);

        // FR-10.5: log the detected server version once per client instance.
        if (_logger is not null && Interlocked.Exchange(ref _serverVersionLogged, 1) == 0)
        {
            Log.ServerVersionDetected(_logger, info.Version);
        }

        return info;
    }

    /// <summary>
    /// Returns <see langword="true"/> only when <c>/v1/info</c> responds successfully and the
    /// server reports <c>starting = false</c>. See FR-10.2.
    /// </summary>
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var info = await GetServerInfoAsync(cancellationToken).ConfigureAwait(false);
            return !info.Starting;
        }
        catch (TrinoException)
        {
            return false;
        }
    }

    /// <summary><c>GET /v1/query/{queryId}</c>. See FR-10.3.</summary>
    public Task<TrinoQueryInfo> GetQueryInfoAsync(string queryId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(queryId);
        return QueryInfoClient.GetQueryInfoAsync(_invoker, Options, queryId, _logger, cancellationToken);
    }

    /// <summary>
    /// Submits <paramref name="sql"/> via <c>POST /v1/statement</c> and returns a
    /// <see cref="TrinoResultSet"/> ready for streaming. See FR-4.1, FR-4.5.
    /// </summary>
    /// <param name="sql">
    /// The statement text. When <paramref name="parameters"/> is non-<see langword="null"/>, this may use
    /// <c>?</c>, <c>:name</c>, or <c>@name</c> placeholders, which are rewritten to positional form
    /// (FR-8.4, FR-8.5) and bound server-side via <c>PREPARE</c>/<c>EXECUTE</c> (FR-8.1, FR-8.2).
    /// Client-side interpolation of parameter values into <paramref name="sql"/> is never performed.
    /// </param>
    /// <param name="parameters">
    /// The parameter values, matched to placeholders in occurrence order (or by name). <see langword="null"/>
    /// (the default) submits <paramref name="sql"/> verbatim with no parameter binding.
    /// </param>
    /// <param name="retainPreparedStatement">
    /// When <paramref name="parameters"/> is non-<see langword="null"/> and this is <see langword="true"/>,
    /// the generated prepared statement is left registered on <see cref="Session"/> after submission
    /// instead of being deallocated (FR-8.9). Ignored when <paramref name="parameters"/> is <see langword="null"/>.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the initial submission.</param>
    /// <exception cref="ArgumentException"><paramref name="sql"/> is null or empty.</exception>
    /// <exception cref="Exceptions.TrinoParameterException">
    /// <paramref name="parameters"/> is supplied and its count does not match the statement's
    /// placeholder count, or a named placeholder has no matching parameter (FR-8.6).
    /// </exception>
    public async Task<TrinoResultSet> ExecuteAsync(
        string sql, TrinoParameterCollection? parameters = null, bool retainPreparedStatement = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sql);

        if (parameters is null)
        {
            return await ExecuteCoreAsync(sql, cancellationToken).ConfigureAwait(false);
        }

        var (rewrittenSql, placeholderNames) = ParameterRewriter.Rewrite(sql);
        var orderedParameters = ParameterBinder.Bind(placeholderNames, parameters);

        var name = "triql_" + Guid.NewGuid().ToString("N");
        var usingClause = orderedParameters.Count == 0
            ? string.Empty
            : " USING " + string.Join(", ", orderedParameters.Select(SqlLiteralEncoder.Encode));

        Session.RegisterPreparedStatement(name, rewrittenSql);
        try
        {
            return await ExecuteCoreAsync($"EXECUTE {name}{usingClause}", cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (!retainPreparedStatement)
            {
                Session.UnregisterPreparedStatement(name);
            }
        }
    }

    private async Task<TrinoResultSet> ExecuteCoreAsync(string sql, CancellationToken cancellationToken)
    {
        var statementClient = new StatementClient(_invoker, Options, Session, _logger);
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (Options.QueryTimeout is { } timeout)
        {
            linkedCts.CancelAfter(timeout);
        }

        var stopwatch = Stopwatch.StartNew();
        TrinoPageEnvelope initial;
        try
        {
            initial = await statementClient.SubmitAsync(sql, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
        {
            await statementClient.CancelAsync().ConfigureAwait(false);
            linkedCts.Dispose();

            if (!cancellationToken.IsCancellationRequested && Options.QueryTimeout is { } configuredTimeout)
            {
                throw new TrinoTimeoutException(
                    $"The query exceeded the configured QueryTimeout of {configuredTimeout}.", configuredTimeout, stopwatch.Elapsed, queryId: null);
            }

            throw;
        }
        catch
        {
            linkedCts.Dispose();
            throw;
        }

        if (_logger is not null)
        {
            Log.QuerySubmitted(_logger, initial.QueryId);
        }

        return new TrinoResultSet(statementClient, initial, Options, _logger, linkedCts, cancellationToken);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsInvoker)
        {
            _invoker.Dispose();
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private static HttpMessageInvoker CreateOwnedInvoker(TrinoSessionOptions options, ILoggerFactory? loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var handlerLogger = loggerFactory?.CreateLogger<TrinoClient>();
#pragma warning disable CA2000 // Ownership transfers to the HttpMessageInvoker constructed below (disposeHandler: true).
        var handler = HttpHandlerFactory.Create(options, handlerLogger);
#pragma warning restore CA2000
        return new HttpMessageInvoker(handler, disposeHandler: true);
    }
}
