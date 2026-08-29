using System.Diagnostics;

namespace TriQL.Client.Diagnostics;

/// <summary>
/// The <c>TriQL.Client</c> <see cref="ActivitySource"/>, emitting a span per query (<c>trino.query</c>)
/// and per HTTP request (<c>trino.request</c>) with OpenTelemetry-conventional tags (FR-11.2.2).
/// <see cref="ActivitySource.StartActivity(string, ActivityKind)"/> itself is the zero-cost path
/// when no listener is attached (FR-11.2.4): it returns <see langword="null"/> without allocating,
/// and every helper below tolerates that.
/// </summary>
internal static class Tracing
{
    public const string SourceName = "TriQL.Client";

    private static readonly ActivitySource Source = new(SourceName);

    /// <summary>Starts the per-query <c>trino.query</c> span. <paramref name="statement"/> is redacted when <paramref name="redactStatement"/> is set (FR-11.2.2, SEC-1).</summary>
    public static Activity? StartQueryActivity(string statement, string? catalog, string? schema, Uri? server, bool redactStatement)
    {
        var activity = Source.StartActivity("trino.query", ActivityKind.Client);
        if (activity is null)
        {
            return activity;
        }

        activity.SetTag("db.system", "trino");
        activity.SetTag("db.statement", redactStatement ? Internal.Redactor.RedactedPlaceholder : statement);

        if (catalog is not null)
        {
            activity.SetTag("trino.catalog", catalog);
        }

        if (schema is not null)
        {
            activity.SetTag("trino.schema", schema);
        }

        if (server is not null)
        {
            activity.SetTag("server.address", server.Host);
        }

        return activity;
    }

    /// <summary>Tags a started <c>trino.query</c> span with the id assigned once the statement is submitted.</summary>
    public static void SetQueryId(Activity? activity, string queryId) => activity?.SetTag("trino.query_id", queryId);

    /// <summary>Marks a query span as failed (FR-11.2.2). No-op when <paramref name="activity"/> is <see langword="null"/>.</summary>
    public static void RecordFailure(Activity? activity, Exception exception) =>
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);

    /// <summary>Starts the per-HTTP-request <c>trino.request</c> span (FR-11.2.2).</summary>
    public static Activity? StartRequestActivity(HttpMethod method, Uri? requestUri)
    {
        var activity = Source.StartActivity("trino.request", ActivityKind.Client);
        if (activity is null)
        {
            return activity;
        }

        activity.SetTag("http.request.method", method.Method);
        if (requestUri is not null)
        {
            activity.SetTag("server.address", requestUri.Host);
        }

        return activity;
    }

    /// <summary>Tags a started <c>trino.request</c> span with the response status code.</summary>
    public static void SetResponseStatus(Activity? activity, int statusCode) => activity?.SetTag("http.response.status_code", statusCode);

    /// <summary>
    /// Propagates the ambient <see cref="Activity.Current"/> (this source's own span, or one from
    /// a hosting application) onto an outbound request as a <c>traceparent</c> header (FR-11.2.3).
    /// </summary>
    public static void PropagateTraceContext(HttpRequestMessage request)
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        request.Headers.Remove("traceparent");
        request.Headers.TryAddWithoutValidation("traceparent", activity.Id);

        if (!string.IsNullOrEmpty(activity.TraceStateString))
        {
            request.Headers.Remove("tracestate");
            request.Headers.TryAddWithoutValidation("tracestate", activity.TraceStateString);
        }
    }
}
