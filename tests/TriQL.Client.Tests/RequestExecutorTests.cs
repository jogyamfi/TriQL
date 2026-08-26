using System.Net;
using Microsoft.Extensions.Logging;
using TriQL.Client.Auth;
using TriQL.Client.Exceptions;
using TriQL.Client.Internal;
using TriQL.Client.Tests.Fakes;

namespace TriQL.Client.Tests;

public sealed class RequestExecutorTests
{
    [Fact]
    public async Task SendAsync_RetriesOnce_AfterSuccessfulRefresh()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.Unauthorized);
        fake.Enqueue(HttpStatusCode.OK);
        using var invoker = fake.CreateInvoker();
        var authenticator = new RefreshableAuthenticator(refreshSucceeds: true);
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };

        using var response = await RequestExecutor.SendAsync(
            invoker,
            () => new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/v1/info"),
            authenticator,
            options,
            logger: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, authenticator.RefreshAttempts);
    }

    [Fact]
    public async Task SendAsync_ThrowsAuthenticationException_WhenRefreshFails()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.Unauthorized);
        using var invoker = fake.CreateInvoker();
        var authenticator = new RefreshableAuthenticator(refreshSucceeds: false);
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };

        await Assert.ThrowsAsync<TrinoAuthenticationException>(() => RequestExecutor.SendAsync(
            invoker,
            () => new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/v1/info"),
            authenticator,
            options,
            logger: null,
            CancellationToken.None));
    }

    [Fact]
    public async Task SendAsync_ThrowsAuthenticationException_WhenStillUnauthorizedAfterRefresh()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.Unauthorized);
        fake.Enqueue(HttpStatusCode.Unauthorized);
        using var invoker = fake.CreateInvoker();
        var authenticator = new RefreshableAuthenticator(refreshSucceeds: true);
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };

        await Assert.ThrowsAsync<TrinoAuthenticationException>(() => RequestExecutor.SendAsync(
            invoker,
            () => new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/v1/info"),
            authenticator,
            options,
            logger: null,
            CancellationToken.None));

        Assert.Equal(1, authenticator.RefreshAttempts);
    }

    [Fact]
    public async Task SendAsync_ThrowsTrinoConnectionException_WhenRequestTimesOut()
    {
        using var fake = new FakeTrinoCoordinator();
        // The per-request timeout is treated as a transient, retryable failure (FR-3.3.1), so every
        // attempt up to RetryPolicyOptions.Default.MaxAttempts must time out for the exception to
        // ultimately propagate instead of being masked by a later successful retry.
        for (var i = 0; i < 10; i++)
        {
            fake.Enqueue(HttpStatusCode.OK, delay: TimeSpan.FromSeconds(5));
        }

        using var invoker = fake.CreateInvoker();
        var options = new TrinoSessionOptions
        {
            Server = new Uri("https://trino.example.com/"),
            RequestTimeout = TimeSpan.FromMilliseconds(50),
        };

        await Assert.ThrowsAsync<TrinoConnectionException>(() => RequestExecutor.SendAsync(
            invoker,
            () => new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/v1/info"),
            AnonymousAuthenticator.Instance,
            options,
            logger: null,
            CancellationToken.None));
    }

    [Fact]
    public async Task SendAsync_LogsRedactedHeaders_WhenTraceEnabled()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK);
        using var invoker = fake.CreateInvoker();
        var options = new TrinoSessionOptions
        {
            Server = new Uri("https://trino.example.com/"),
            Authenticator = new JwtAuthenticator("secret-token"),
        };
        using var loggerFactory = new CapturingLoggerFactory();

        using var response = await RequestExecutor.SendAsync(
            invoker,
            () => new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/v1/info"),
            options.Authenticator!,
            options,
            loggerFactory.CreateLogger("test"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(loggerFactory.Logger.Messages, m => m.Contains("Authorization=***REDACTED***", StringComparison.Ordinal));
        Assert.DoesNotContain(loggerFactory.Logger.Messages, m => m.Contains("secret-token", StringComparison.Ordinal));
    }

    private sealed class RefreshableAuthenticator(bool refreshSucceeds) : ITrinoAuthenticator
    {
        public int RefreshAttempts { get; private set; }

        public ValueTask InitializeAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<bool> TryRefreshAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            RefreshAttempts++;
            return ValueTask.FromResult(refreshSucceeds);
        }

        public void ConfigureHandler(SocketsHttpHandler handler)
        {
        }
    }
}
