using System.Net;
using System.Net.Http.Headers;

namespace TriQL.Client.Internal;

/// <summary>
/// The complete set of session mutations parsed from a single response, applied atomically. See FR-1.2.2, FR-1.2.3.
/// </summary>
internal sealed record SessionMutationBatch(
    string? Catalog,
    string? Schema,
    string? Path,
    IReadOnlyList<KeyValuePair<string, string>> SetSessionProperties,
    IReadOnlyList<string> ClearedSessionProperties,
    IReadOnlyList<KeyValuePair<string, TrinoSelectedRole>> SetRoles,
    IReadOnlyList<TrinoSelectedRole>? OriginalRoles,
    IReadOnlyList<KeyValuePair<string, string>> AddedPreparedStatements,
    IReadOnlyList<string> DeallocatedPreparedStatements,
    string? SetAuthorizationUser,
    bool ResetAuthorizationUser,
    string? StartedTransactionId,
    bool ClearedTransactionId,
    IReadOnlyList<string> AppliedHeaderNames)
{
    public bool IsEmpty => AppliedHeaderNames.Count == 0;
}

/// <summary>
/// Parses the server-driven session mutation headers of Appendix A.2 into a <see cref="SessionMutationBatch"/>,
/// in the order required by FR-1.2.2.
/// </summary>
internal static class SessionMutationApplier
{
    public const string SetCatalog = "X-Trino-Set-Catalog";
    public const string SetSchema = "X-Trino-Set-Schema";
    public const string SetPath = "X-Trino-Set-Path";
    public const string SetSession = "X-Trino-Set-Session";
    public const string ClearSession = "X-Trino-Clear-Session";
    public const string SetRole = "X-Trino-Set-Role";
    public const string SetOriginalRoles = "X-Trino-Set-Original-Roles";
    public const string AddedPrepare = "X-Trino-Added-Prepare";
    public const string DeallocatedPrepare = "X-Trino-Deallocated-Prepare";
    public const string SetAuthorizationUser = "X-Trino-Set-Authorization-User";
    public const string ResetAuthorizationUser = "X-Trino-Reset-Authorization-User";
    public const string StartedTransactionId = "X-Trino-Started-Transaction-Id";
    public const string ClearTransactionId = "X-Trino-Clear-Transaction-Id";

    public static SessionMutationBatch Parse(HttpResponseMessage response)
    {
        var headers = response.Headers;
        var appliedHeaders = new List<string>();

        var catalog = TakeLast(headers, SetCatalog, appliedHeaders);
        var schema = TakeLast(headers, SetSchema, appliedHeaders);
        var path = TakeLast(headers, SetPath, appliedHeaders);

        var setSessionProperties = ParseKeyEncodedValuePairs(headers, SetSession, appliedHeaders);
        var clearedSessionProperties = ParseValues(headers, ClearSession, appliedHeaders);

        var setRoles = ParseRoles(headers, appliedHeaders);
        var originalRoles = ParseOriginalRoles(headers, appliedHeaders);

        var addedPrepares = ParseKeyEncodedValuePairs(headers, AddedPrepare, appliedHeaders);
        var deallocatedPrepares = ParseValues(headers, DeallocatedPrepare, appliedHeaders);

        var setAuthUser = TakeLast(headers, SetAuthorizationUser, appliedHeaders);
        var resetAuthUser = HasHeader(headers, ResetAuthorizationUser, appliedHeaders);

        var startedTransactionId = TakeLast(headers, StartedTransactionId, appliedHeaders);
        var clearedTransactionId = HasHeader(headers, ClearTransactionId, appliedHeaders);

        return new SessionMutationBatch(
            catalog,
            schema,
            path,
            setSessionProperties,
            clearedSessionProperties,
            setRoles,
            originalRoles,
            addedPrepares,
            deallocatedPrepares,
            setAuthUser,
            resetAuthUser,
            startedTransactionId,
            clearedTransactionId,
            appliedHeaders);
    }

    private static string? TakeLast(HttpResponseHeaders headers, string name, List<string> appliedHeaders)
    {
        if (!headers.TryGetValues(name, out var values))
        {
            return null;
        }

        appliedHeaders.Add(name);
        return values.LastOrDefault();
    }

    private static bool HasHeader(HttpResponseHeaders headers, string name, List<string> appliedHeaders)
    {
        if (!headers.TryGetValues(name, out _))
        {
            return false;
        }

        appliedHeaders.Add(name);
        return true;
    }

    private static IReadOnlyList<string> ParseValues(HttpResponseHeaders headers, string name, List<string> appliedHeaders)
    {
        if (!headers.TryGetValues(name, out var values))
        {
            return [];
        }

        appliedHeaders.Add(name);
        return [.. values];
    }

    private static List<KeyValuePair<string, string>> ParseKeyEncodedValuePairs(
        HttpResponseHeaders headers, string name, List<string> appliedHeaders)
    {
        if (!headers.TryGetValues(name, out var values))
        {
            return [];
        }

        appliedHeaders.Add(name);
        var result = new List<KeyValuePair<string, string>>();
        foreach (var value in values)
        {
            var separatorIndex = value.IndexOf('=');
            if (separatorIndex < 0)
            {
                result.Add(new KeyValuePair<string, string>(value, string.Empty));
                continue;
            }

            var key = value[..separatorIndex];
            var encodedValue = value[(separatorIndex + 1)..];
            result.Add(new KeyValuePair<string, string>(key, WebUtility.UrlDecode(encodedValue)));
        }

        return result;
    }

    private static List<KeyValuePair<string, TrinoSelectedRole>> ParseRoles(HttpResponseHeaders headers, List<string> appliedHeaders)
    {
        if (!headers.TryGetValues(SetRole, out var values))
        {
            return [];
        }

        appliedHeaders.Add(SetRole);
        var result = new List<KeyValuePair<string, TrinoSelectedRole>>();
        foreach (var value in values)
        {
            var separatorIndex = value.IndexOf('=');
            var catalogKey = separatorIndex < 0 ? string.Empty : value[..separatorIndex];
            var roleToken = separatorIndex < 0 ? value : value[(separatorIndex + 1)..];
            result.Add(new KeyValuePair<string, TrinoSelectedRole>(catalogKey, TrinoSelectedRole.Parse(WebUtility.UrlDecode(roleToken))));
        }

        return result;
    }

    private static IReadOnlyList<TrinoSelectedRole>? ParseOriginalRoles(HttpResponseHeaders headers, List<string> appliedHeaders)
    {
        if (!headers.TryGetValues(SetOriginalRoles, out var values))
        {
            return null;
        }

        appliedHeaders.Add(SetOriginalRoles);
        return [.. values.Select(v => TrinoSelectedRole.Parse(WebUtility.UrlDecode(v)))];
    }
}
