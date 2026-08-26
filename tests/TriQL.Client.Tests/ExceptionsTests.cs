using TriQL.Client.Exceptions;

namespace TriQL.Client.Tests;

public sealed class ExceptionsTests
{
    [Fact]
    public void TrinoConfigurationException_ExposesMessageAndDefaults()
    {
        var ex = new TrinoConfigurationException("bad config");

        Assert.Equal("bad config", ex.Message);
        Assert.Null(ex.QueryId);
        Assert.False(ex.IsRetryable);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void TrinoConfigurationException_PreservesInnerException()
    {
        var inner = new InvalidOperationException("boom");

        var ex = new TrinoConfigurationException("bad config", inner);

        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void TrinoConnectionException_DefaultsToRetryable()
    {
        var ex = new TrinoConnectionException("network down");

        Assert.True(ex.IsRetryable);
    }

    [Fact]
    public void TrinoConnectionException_AllowsOverridingRetryability()
    {
        var ex = new TrinoConnectionException("network down", isRetryable: false);

        Assert.False(ex.IsRetryable);
    }

    [Fact]
    public void TrinoConnectionException_PreservesInnerException()
    {
        var inner = new IOException("reset");

        var ex = new TrinoConnectionException("network down", inner);

        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void TrinoAuthenticationException_IsTerminal()
    {
        var ex = new TrinoAuthenticationException("401");

        Assert.False(ex.IsRetryable);
    }

    [Fact]
    public void TrinoAuthenticationException_PreservesInnerException()
    {
        var inner = new InvalidOperationException("token expired");

        var ex = new TrinoAuthenticationException("401", inner);

        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void TrinoProtocolException_CarriesQueryIdAndInnerException()
    {
        var inner = new FormatException("bad json");

        var ex = new TrinoProtocolException("malformed", inner, "query-1");

        Assert.Equal("query-1", ex.QueryId);
        Assert.Same(inner, ex.InnerException);
        Assert.False(ex.IsRetryable);
    }

    [Fact]
    public void TrinoParameterException_PreservesInnerException()
    {
        var inner = new ArgumentException("bad param");

        var ex = new TrinoParameterException("parameter mismatch", inner);

        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void TrinoTypeConversionException_PreservesInnerException()
    {
        var inner = new OverflowException();

        var ex = new TrinoTypeConversionException("cannot convert", inner);

        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void TrinoTimeoutException_ExposesConfiguredAndElapsedDurations()
    {
        var ex = new TrinoTimeoutException("deadline exceeded", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(31), "query-2");

        Assert.Equal(TimeSpan.FromSeconds(30), ex.ConfiguredTimeout);
        Assert.Equal(TimeSpan.FromSeconds(31), ex.Elapsed);
        Assert.Equal("query-2", ex.QueryId);
        Assert.False(ex.IsRetryable);
    }

    [Fact]
    public void TrinoQueryException_ExposesErrorDetails()
    {
        var failureInfo = new TrinoFailureInfo("java.lang.RuntimeException", "boom", [], ["at Foo.Bar()"]);
        var location = new TrinoErrorLocation(3, 7);

        var ex = new TrinoQueryException(
            "query failed", "query-3", 12345, "SYNTAX_ERROR", TrinoErrorType.UserError, failureInfo, location);

        Assert.Equal("query-3", ex.QueryId);
        Assert.Equal(12345, ex.ErrorCode);
        Assert.Equal("SYNTAX_ERROR", ex.ErrorName);
        Assert.Equal(TrinoErrorType.UserError, ex.ErrorType);
        Assert.Same(failureInfo, ex.FailureInfo);
        Assert.Same(location, ex.ErrorLocation);
        Assert.False(ex.IsRetryable);
    }

    [Fact]
    public void TrinoFailureInfo_ToStringIncludesTypeAndMessage()
    {
        var failureInfo = new TrinoFailureInfo("java.lang.RuntimeException", "boom", [], []);

        Assert.Contains("RuntimeException", failureInfo.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TrinoErrorLocation_SupportsEqualityAndDeconstruction()
    {
        var a = new TrinoErrorLocation(1, 2);
        var b = new TrinoErrorLocation(1, 2);

        Assert.Equal(a, b);
        var (line, column) = a;
        Assert.Equal(1, line);
        Assert.Equal(2, column);
    }
}
