using TriQL.Client.Auth;
using TriQL.Client.Tests.Fakes;

namespace TriQL.Client.Tests.Security;

/// <summary>
/// SEC-1: seeds known secret values into credential-bearing options, runs a fully authenticated
/// request against the fake coordinator, captures logs at Trace, and asserts no seeded secret
/// value appears anywhere in the captured output.
/// </summary>
public sealed class RedactionTests
{
    private const string SeededPassword = "sup3r-s3cret-p4ssw0rd";
    private const string SeededToken = "sup3r-s3cret-jwt-token-value";

    [Fact]
    public async Task CapturedLogs_NeverContain_BasicAuthPassword()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(System.Net.HttpStatusCode.OK, InfoJson());
        using var loggerFactory = new CapturingLoggerFactory();
        using var invoker = fake.CreateInvoker();

        var options = new TrinoSessionOptions
        {
            Server = new Uri("https://trino.example.com/"),
            Authenticator = new BasicAuthenticator("svc-account", SeededPassword),
        };
        await using var client = new TrinoClient(options, invoker, loggerFactory);

        await client.GetServerInfoAsync();

        AssertNoSecretLeaked(loggerFactory, SeededPassword);
    }

    [Fact]
    public async Task CapturedLogs_NeverContain_JwtBearerToken()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(System.Net.HttpStatusCode.OK, InfoJson());
        using var loggerFactory = new CapturingLoggerFactory();
        using var invoker = fake.CreateInvoker();

        var options = new TrinoSessionOptions
        {
            Server = new Uri("https://trino.example.com/"),
            Authenticator = new JwtAuthenticator(SeededToken),
        };
        await using var client = new TrinoClient(options, invoker, loggerFactory);

        await client.GetServerInfoAsync();

        AssertNoSecretLeaked(loggerFactory, SeededToken);
    }

    private static void AssertNoSecretLeaked(CapturingLoggerFactory loggerFactory, string secret)
    {
        foreach (var message in loggerFactory.Logger.Messages)
        {
            Assert.DoesNotContain(secret, message, StringComparison.Ordinal);
        }
    }

    private static string InfoJson() =>
        """
        {"nodeVersion":{"version":"466"},"environment":"test","coordinator":true,"starting":false,"uptime":"1.00m"}
        """;
}
