using TriQL.Client.Internal;

namespace TriQL.Client.Tests;

public sealed class ProtocolHeadersTests
{
    [Fact]
    public void WriteSessionHeaders_SetsUserAndSourceAndCapabilities()
    {
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/"), User = "alice" };
        var session = new TrinoSession(options);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://trino.example.com/v1/statement");

        ProtocolHeaders.WriteSessionHeaders(request, options, session);

        Assert.Equal("alice", Single(request, "X-Trino-User"));
        Assert.Equal("triql-dotnet", Single(request, "X-Trino-Source"));
        Assert.Equal(ProtocolHeaders.ClientCapabilities, Single(request, "X-Trino-Client-Capabilities"));
    }

    [Fact]
    public void WriteSessionHeaders_OmitsEmptyValues()
    {
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/"), TraceToken = null };
        var session = new TrinoSession(options);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://trino.example.com/v1/statement");

        ProtocolHeaders.WriteSessionHeaders(request, options, session);

        Assert.False(request.Headers.Contains("X-Trino-Trace-Token"));
        Assert.False(request.Headers.Contains("X-Trino-Catalog"));
    }

    [Fact]
    public void WriteSessionHeaders_CatalogAndSchema_ComeFromLiveSession()
    {
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/"), Catalog = "hive", Schema = "default" };
        var session = new TrinoSession(options);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://trino.example.com/v1/statement");

        ProtocolHeaders.WriteSessionHeaders(request, options, session);

        Assert.Equal("hive", Single(request, "X-Trino-Catalog"));
        Assert.Equal("default", Single(request, "X-Trino-Schema"));
    }

    [Fact]
    public void WriteSessionHeaders_UrlEncodesSessionPropertyValues()
    {
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        options.SessionProperties["query_max_run_time"] = "1 day, 2 hours";
        var session = new TrinoSession(options);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://trino.example.com/v1/statement");

        ProtocolHeaders.WriteSessionHeaders(request, options, session);

        var value = Single(request, "X-Trino-Session");
        Assert.StartsWith("query_max_run_time=", value, StringComparison.Ordinal);
        Assert.DoesNotContain(" ", value, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSessionHeaders_EmitsRepeatedHeaders_ForMultipleSessionProperties()
    {
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        options.SessionProperties["a"] = "1";
        options.SessionProperties["b"] = "2";
        var session = new TrinoSession(options);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://trino.example.com/v1/statement");

        ProtocolHeaders.WriteSessionHeaders(request, options, session);

        Assert.Equal(2, request.Headers.GetValues("X-Trino-Session").Count());
    }

    [Fact]
    public void WriteSessionHeaders_RendersSystemRole_WithoutCatalogPrefix()
    {
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        options.Roles[""] = TrinoSelectedRole.Named("admin");
        var session = new TrinoSession(options);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://trino.example.com/v1/statement");

        ProtocolHeaders.WriteSessionHeaders(request, options, session);

        Assert.Equal("ROLE{admin}", Single(request, "X-Trino-Role"));
    }

    [Fact]
    public void WriteSessionHeaders_RendersCatalogScopedRole_WithCatalogPrefix()
    {
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        options.Roles["hive"] = TrinoSelectedRole.All;
        var session = new TrinoSession(options);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://trino.example.com/v1/statement");

        ProtocolHeaders.WriteSessionHeaders(request, options, session);

        Assert.Equal("hive=ALL", Single(request, "X-Trino-Role"));
    }

    [Fact]
    public void WriteSessionHeaders_JoinsClientTagsWithCommas()
    {
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        options.ClientTags.Add("etl");
        options.ClientTags.Add("nightly");
        var session = new TrinoSession(options);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://trino.example.com/v1/statement");

        ProtocolHeaders.WriteSessionHeaders(request, options, session);

        var value = Single(request, "X-Trino-Client-Tags");
        Assert.Contains("etl", value, StringComparison.Ordinal);
        Assert.Contains("nightly", value, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSessionHeaders_IncludesAdditionalHeadersVerbatim()
    {
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        options.AdditionalHeaders["X-Custom"] = "value";
        var session = new TrinoSession(options);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://trino.example.com/v1/statement");

        ProtocolHeaders.WriteSessionHeaders(request, options, session);

        Assert.Equal("value", Single(request, "X-Custom"));
    }

    [Fact]
    public void ResolveUser_FallsBackToBasicAuthenticatorUsername_WhenUserNotSet()
    {
        var options = new TrinoSessionOptions
        {
            Server = new Uri("https://trino.example.com/"),
            User = null,
            Authenticator = new Auth.BasicAuthenticator("service-account", "secret"),
        };

        Assert.Equal("service-account", ProtocolHeaders.ResolveUser(options));
    }

    private static string Single(HttpRequestMessage request, string name) => request.Headers.GetValues(name).Single();
}
