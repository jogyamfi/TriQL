using TriQL.Client.Exceptions;
using TriQL.Client.Internal;
using TriQL.Client.Internal.Json;

namespace TriQL.Client.Tests.Streaming;

public sealed class FailureClassifierTests
{
    [Theory]
    [InlineData("USER_ERROR", TrinoErrorType.UserError)]
    [InlineData("INSUFFICIENT_RESOURCES", TrinoErrorType.InsufficientResources)]
    [InlineData("EXTERNAL", TrinoErrorType.External)]
    [InlineData("INTERNAL_ERROR", TrinoErrorType.InternalError)]
    [InlineData("SOME_UNKNOWN_FUTURE_TYPE", TrinoErrorType.InternalError)]
    public void ToException_MapsErrorTypeAndIsNeverRetryable(string wireErrorType, TrinoErrorType expected)
    {
        var error = new StatementErrorDto
        {
            Message = "syntax error",
            ErrorCode = 1,
            ErrorName = "SYNTAX_ERROR",
            ErrorType = wireErrorType,
        };

        var exception = FailureClassifier.ToException(error, "20240101_000000_00001_abcde");

        Assert.Equal(expected, exception.ErrorType);
        Assert.False(exception.IsRetryable);
        Assert.Equal("20240101_000000_00001_abcde", exception.QueryId);
        Assert.Contains("syntax error", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToException_CarriesErrorLocationAndFailureInfo()
    {
        var error = new StatementErrorDto
        {
            Message = "boom",
            ErrorCode = 42,
            ErrorName = "GENERIC_INTERNAL_ERROR",
            ErrorType = "INTERNAL_ERROR",
            ErrorLocation = new StatementErrorLocationDto { LineNumber = 3, ColumnNumber = 7 },
            FailureInfo = new StatementFailureInfoDto { Type = "java.lang.RuntimeException", Message = "boom", Stack = ["at Foo.bar"] },
        };

        var exception = FailureClassifier.ToException(error, queryId: null);

        Assert.Equal(3, exception.ErrorLocation?.LineNumber);
        Assert.Equal(7, exception.ErrorLocation?.ColumnNumber);
        Assert.Equal("java.lang.RuntimeException", exception.FailureInfo?.Type);
        Assert.Equal(["at Foo.bar"], exception.FailureInfo?.Stack);
    }
}
