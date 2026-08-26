using Microsoft.Extensions.Logging;
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
