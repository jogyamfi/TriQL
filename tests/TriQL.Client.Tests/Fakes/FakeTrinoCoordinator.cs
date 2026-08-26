using System.Net;
using System.Text;

namespace TriQL.Client.Tests.Fakes;

/// <summary>
/// A request captured by <see cref="FakeTrinoCoordinator"/>, decoupled from <see cref="HttpRequestMessage"/>
/// so callers can inspect it after the (possibly disposed) request has been sent.
/// </summary>
public sealed record CapturedRequest(HttpMethod Method, Uri? RequestUri, IReadOnlyDictionary<string, string[]> Headers)
{
    public bool HasHeader(string name) => Headers.ContainsKey(name);

    public IReadOnlyList<string> HeaderValues(string name) => Headers.TryGetValue(name, out var values) ? values : [];
}

/// <summary>
/// An in-process fake coordinator built on <see cref="HttpMessageHandler"/> interception (TEST-1, P1-T17).
/// Responses are scripted with <see cref="Enqueue"/> and served in FIFO order per call; an empty queue
/// yields <see cref="HttpStatusCode.NotFound"/> so unscripted calls fail loudly instead of hanging.
/// </summary>
public sealed class FakeTrinoCoordinator : HttpMessageHandler
{
    private readonly Queue<ScriptedResponse> _responses = new();
    private readonly List<CapturedRequest> _receivedRequests = [];
    private readonly Lock _gate = new();

    /// <summary>Every request received so far, in arrival order.</summary>
    public IReadOnlyList<CapturedRequest> ReceivedRequests
    {
        get { lock (_gate) { return [.. _receivedRequests]; } }
    }

    /// <summary>Creates an <see cref="HttpMessageInvoker"/> backed by this fake, which does not dispose the fake.</summary>
    public HttpMessageInvoker CreateInvoker() => new(this, disposeHandler: false);

    /// <summary>Enqueues the next scripted response.</summary>
    public void Enqueue(
        HttpStatusCode statusCode,
        string? jsonBody = null,
        IReadOnlyDictionary<string, string>? headers = null,
        TimeSpan? delay = null)
    {
        lock (_gate)
        {
            _responses.Enqueue(new ScriptedResponse(statusCode, jsonBody, headers, delay));
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _receivedRequests.Add(Capture(request));
        }

        ScriptedResponse? scripted;
        lock (_gate)
        {
            scripted = _responses.Count > 0 ? _responses.Dequeue() : null;
        }

        if (scripted is null)
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        if (scripted.Delay is { } delay)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        var response = new HttpResponseMessage(scripted.StatusCode);
        if (scripted.JsonBody is not null)
        {
            response.Content = new StringContent(scripted.JsonBody, Encoding.UTF8, "application/json");
        }

        if (scripted.Headers is not null)
        {
            foreach (var (name, value) in scripted.Headers)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }
        }

        return response;
    }

    private static CapturedRequest Capture(HttpRequestMessage request)
    {
        var headers = request.Headers
            .ToDictionary(h => h.Key, h => h.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
        return new CapturedRequest(request.Method, request.RequestUri, headers);
    }

    private sealed record ScriptedResponse(
        HttpStatusCode StatusCode,
        string? JsonBody,
        IReadOnlyDictionary<string, string>? Headers,
        TimeSpan? Delay);
}
