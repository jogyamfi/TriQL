using TriQL.Client;

namespace TriQL.Data.ADO.Tests;

public sealed class TrinoConnectionStringBuilderTests
{
    [Fact]
    public void RoundTrip_ParsesAndSerializesToEquivalentBuilder()
    {
        var builder = new TrinoConnectionStringBuilder
        {
            Host = "trino.example.com",
            Port = 443,
            EnableSsl = true,
            Catalog = "hive",
            Schema = "default",
            User = "alice",
        };

        var roundTripped = new TrinoConnectionStringBuilder(builder.ToString());

        Assert.Equal(builder.Host, roundTripped.Host);
        Assert.Equal(builder.Port, roundTripped.Port);
        Assert.Equal(builder.EnableSsl, roundTripped.EnableSsl);
        Assert.Equal(builder.Catalog, roundTripped.Catalog);
        Assert.Equal(builder.Schema, roundTripped.Schema);
        Assert.Equal(builder.User, roundTripped.User);
    }

    [Fact]
    public void Quoting_PreservesSemicolonAndEqualsInValues()
    {
        var builder = new TrinoConnectionStringBuilder { Password = "p@ss;word=1" };
        var roundTripped = new TrinoConnectionStringBuilder(builder.ToString());
        Assert.Equal("p@ss;word=1", roundTripped.Password);
    }

    [Fact]
    public void UnknownKey_ThrowsArgumentException()
    {
        var builder = new TrinoConnectionStringBuilder();
        Assert.Throws<ArgumentException>(() => builder["NotARealKey"] = "value");
    }

    [Fact]
    public void UnknownKey_InConnectionString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new TrinoConnectionStringBuilder("NotARealKey=value"));
    }

    [Fact]
    public void ToString_Default_DoesNotRedactSecrets()
    {
        var builder = new TrinoConnectionStringBuilder { Password = "supersecret" };
        Assert.Contains("supersecret", builder.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ToString_WithRedactSecrets_RedactsPasswordAccessTokenAndClientSecret()
    {
        var builder = new TrinoConnectionStringBuilder
        {
            Password = "p-secret",
            AccessToken = "t-secret",
            ClientSecret = "c-secret",
            RedactSecrets = true,
        };

        var text = builder.ToString();

        Assert.DoesNotContain("p-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("t-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("c-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ToSessionOptions_UsesServerWhenSet_OverHostPortEnableSsl()
    {
        var builder = new TrinoConnectionStringBuilder
        {
            Server = "https://override.example.com:9999/",
            Host = "ignored.example.com",
            Port = 1111,
        };

        var options = InvokeToSessionOptions(builder);

        Assert.Equal(new Uri("https://override.example.com:9999/"), options.Server);
    }

    [Fact]
    public void ToSessionOptions_BuildsServerFromHostPortEnableSsl()
    {
        var builder = new TrinoConnectionStringBuilder { Host = "trino.example.com", Port = 8443, EnableSsl = true };

        var options = InvokeToSessionOptions(builder);

        Assert.Equal("https", options.Server!.Scheme);
        Assert.Equal("trino.example.com", options.Server.Host);
        Assert.Equal(8443, options.Server.Port);
    }

    [Fact]
    public void ToSessionOptions_DefaultsPortByEnableSsl()
    {
        var httpsBuilder = new TrinoConnectionStringBuilder { Host = "h", EnableSsl = true };
        var httpBuilder = new TrinoConnectionStringBuilder { Host = "h", EnableSsl = false };

        Assert.Equal(443, InvokeToSessionOptions(httpsBuilder).Server!.Port);
        Assert.Equal(8080, InvokeToSessionOptions(httpBuilder).Server!.Port);
    }

    [Fact]
    public void ToSessionOptions_Auth_None_ResolvesAnonymousAuthenticator()
    {
        var builder = new TrinoConnectionStringBuilder { Host = "h" };
        var options = InvokeToSessionOptions(builder);
        Assert.IsType<TriQL.Client.Auth.AnonymousAuthenticator>(options.Authenticator);
    }

    [Fact]
    public void ToSessionOptions_Auth_Basic_ResolvesBasicAuthenticator()
    {
        var builder = new TrinoConnectionStringBuilder { Host = "h", Auth = "basic", User = "alice", Password = "pw" };
        var options = InvokeToSessionOptions(builder);
        Assert.IsType<TriQL.Client.Auth.BasicAuthenticator>(options.Authenticator);
    }

    [Fact]
    public void ToSessionOptions_Auth_Basic_MissingPassword_Throws()
    {
        var builder = new TrinoConnectionStringBuilder { Host = "h", Auth = "basic", User = "alice" };
        Assert.Throws<TriQL.Client.Exceptions.TrinoConfigurationException>(() => InvokeToSessionOptions(builder));
    }

    [Fact]
    public void ToSessionOptions_Auth_Unknown_ThrowsArgumentException()
    {
        var builder = new TrinoConnectionStringBuilder { Host = "h", Auth = "not-a-real-auth-kind" };
        Assert.Throws<ArgumentException>(() => InvokeToSessionOptions(builder));
    }

    [Fact]
    public void ToSessionOptions_Auth_Entra_NamesTheMissingPackage()
    {
        var builder = new TrinoConnectionStringBuilder { Host = "h", Auth = "entra-id" };
        var ex = Assert.Throws<TriQL.Client.Exceptions.TrinoConfigurationException>(() => InvokeToSessionOptions(builder));
        Assert.Contains("TriQL.Client.Auth", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToSessionOptions_ParsesCommaSeparatedCollections()
    {
        var builder = new TrinoConnectionStringBuilder
        {
            Host = "h",
            ClientTags = "a,b,c",
            SessionProperties = "k1=v1,k2=v2",
            Roles = "hive=admin",
            QueryDataEncoding = "json+zstd,json",
        };

        var options = InvokeToSessionOptions(builder);

        Assert.Equal(["a", "b", "c"], options.ClientTags.Order(StringComparer.Ordinal));
        Assert.Equal("v1", options.SessionProperties["k1"]);
        Assert.Equal("v2", options.SessionProperties["k2"]);
        Assert.Equal(TriQL.Client.TrinoSelectedRoleType.Role, options.Roles["hive"].Type);
        Assert.Equal(["json+zstd", "json"], options.QueryDataEncodings);
    }

    private static TrinoSessionOptions InvokeToSessionOptions(TrinoConnectionStringBuilder builder) => builder.ToSessionOptions();
}
