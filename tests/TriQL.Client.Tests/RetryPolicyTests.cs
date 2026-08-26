using System.Net;
using TriQL.Client.Internal;

namespace TriQL.Client.Tests;

public sealed class RetryPolicyTests
{
    private static readonly RetryPolicyOptions FastOptions = new(
        BaseDelay: TimeSpan.FromMilliseconds(1),
        Multiplier: 2.0,
        MaxDelay: TimeSpan.FromMilliseconds(5),
        MaxAttempts: 5);

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task ExecuteAsync_RetriesRetryableStatusCodes_UntilSuccess(HttpStatusCode retryableStatus)
    {
        var attempt = 0;

        using var response = await RetryPolicy.ExecuteAsync(
            _ =>
            {
                attempt++;
                var statusCode = attempt < 3 ? retryableStatus : HttpStatusCode.OK;
                return Task.FromResult(new HttpResponseMessage(statusCode));
            },
            FastOptions,
            logger: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, attempt);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    public async Task ExecuteAsync_DoesNotRetry_TerminalStatusCodes(HttpStatusCode terminalStatus)
    {
        var attempt = 0;

        using var response = await RetryPolicy.ExecuteAsync(
            _ =>
            {
                attempt++;
                return Task.FromResult(new HttpResponseMessage(terminalStatus));
            },
            FastOptions,
            logger: null,
            CancellationToken.None);

        Assert.Equal(terminalStatus, response.StatusCode);
        Assert.Equal(1, attempt);
    }

    [Fact]
    public async Task ExecuteAsync_StopsAfterMaxAttempts()
    {
        var attempt = 0;

        using var response = await RetryPolicy.ExecuteAsync(
            _ =>
            {
                attempt++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            },
            FastOptions with { MaxAttempts = 3 },
            logger: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, attempt);
    }

    [Fact]
    public async Task ExecuteAsync_HonoursRetryAfterDelaySeconds()
    {
        var attempt = 0;

        using var response = await RetryPolicy.ExecuteAsync(
            _ =>
            {
                attempt++;
                if (attempt == 1)
                {
                    var first = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                    first.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(1));
                    return Task.FromResult(first);
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            },
            FastOptions,
            logger: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, attempt);
    }

    [Fact]
    public async Task ExecuteAsync_RetriesTransientTransportExceptions()
    {
        var attempt = 0;

        using var response = await RetryPolicy.ExecuteAsync(
            _ =>
            {
                attempt++;
                if (attempt < 2)
                {
                    throw new HttpRequestException("connection refused");
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            },
            FastOptions,
            logger: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, attempt);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotRetry_NonTransientException()
    {
        var attempt = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => RetryPolicy.ExecuteAsync(
            _ =>
            {
                attempt++;
                throw new InvalidOperationException("not transient");
            },
            FastOptions,
            logger: null,
            CancellationToken.None));

        Assert.Equal(1, attempt);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsOperationCanceledException_WhenCancelledBeforeFirstAttempt()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var attempt = 0;

        await Assert.ThrowsAsync<OperationCanceledException>(() => RetryPolicy.ExecuteAsync(
            _ =>
            {
                attempt++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            },
            FastOptions,
            logger: null,
            cts.Token));

        Assert.Equal(0, attempt);
    }
}
