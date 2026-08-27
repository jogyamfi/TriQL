using TriQL.Client.Exceptions;
using TriQL.Client.Internal.Json;

namespace TriQL.Client.Internal;

/// <summary>
/// Classifies a page-level <c>error</c> and builds the corresponding <see cref="TrinoQueryException"/>.
/// Every <c>errorType</c> in FR-12.2.1 is terminal, so this never signals retryability. See FR-12.2.1, FR-12.2.2.
/// </summary>
internal static class FailureClassifier
{
    public static TrinoQueryException ToException(StatementErrorDto error, string? queryId) =>
        new(
            BuildMessage(error, queryId),
            queryId,
            error.ErrorCode,
            error.ErrorName,
            ParseErrorType(error.ErrorType),
            ToFailureInfo(error.FailureInfo),
            error.ErrorLocation is { } loc ? new TrinoErrorLocation(loc.LineNumber, loc.ColumnNumber) : null);

    private static string BuildMessage(StatementErrorDto error, string? queryId) =>
        queryId is null ? error.Message : $"Query {queryId} failed: {error.Message}";

    private static TrinoErrorType ParseErrorType(string raw) => raw switch
    {
        "USER_ERROR" => TrinoErrorType.UserError,
        "INSUFFICIENT_RESOURCES" => TrinoErrorType.InsufficientResources,
        "EXTERNAL" => TrinoErrorType.External,
        _ => TrinoErrorType.InternalError,
    };

    internal static TrinoFailureInfo? ToFailureInfo(StatementFailureInfoDto? dto)
    {
        if (dto is null)
        {
            return null;
        }

        var suppressed = new List<TrinoFailureInfo>();
        if (dto.Suppressed is not null)
        {
            foreach (var s in dto.Suppressed)
            {
                if (ToFailureInfo(s) is { } converted)
                {
                    suppressed.Add(converted);
                }
            }
        }

        return new TrinoFailureInfo(dto.Type, dto.Message, suppressed, dto.Stack ?? []);
    }
}
