using TriQL.Client.Auth;
using TriQL.Client.Exceptions;

namespace TriQL.Client.Tests;

public sealed class TrinoSessionOptionsTests
{
    [Fact]
    public void Validate_ThrowsArgumentException_WhenServerIsNull()
    {
        var options = new TrinoSessionOptions();

        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Theory]
    [InlineData("ftp://trino.example.com")]
    [InlineData("trino.example.com")]
    public void Validate_ThrowsArgumentException_ForNonHttpScheme(string rawValue)
    {
        var options = new TrinoSessionOptions { Server = new Uri(rawValue, UriKind.RelativeOrAbsolute) };

        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Fact]
    public void Validate_Succeeds_ForHttpsServer()
    {
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com:443/") };

        options.Validate();
    }

    [Fact]
    public void Validate_ThrowsTrinoConfigurationException_WhenBasicAuthOverPlaintextHttp()
    {
        var options = new TrinoSessionOptions
        {
            Server = new Uri("http://trino.example.com:8080/"),
            Authenticator = new BasicAuthenticator("user", "secret"),
        };

        Assert.Throws<TrinoConfigurationException>(options.Validate);
    }

    [Fact]
    public void Validate_Succeeds_WhenPlaintextCredentialsExplicitlyAllowed()
    {
        var options = new TrinoSessionOptions
        {
            Server = new Uri("http://trino.example.com:8080/"),
            Authenticator = new BasicAuthenticator("user", "secret"),
        };
        options.Tls.AllowPlaintextCredentials = true;

        options.Validate();
    }

    [Fact]
    public void Validate_Succeeds_ForAnonymousOverPlaintextHttp()
    {
        var options = new TrinoSessionOptions { Server = new Uri("http://trino.example.com:8080/") };

        options.Validate();
    }

    [Fact]
    public void Validate_ThrowsArgumentOutOfRangeException_WhenReadAheadBufferIsZero()
    {
        var options = new TrinoSessionOptions
        {
            Server = new Uri("https://trino.example.com/"),
            ReadAheadBufferBytes = 0,
        };

        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }

    [Fact]
    public void Validate_ThrowsArgumentOutOfRangeException_WhenBufferSmallerThanTargetResultSize()
    {
        var options = new TrinoSessionOptions
        {
            Server = new Uri("https://trino.example.com/"),
            ReadAheadBufferBytes = 1024,
            TargetResultSizeBytes = 2048,
        };

        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }

    [Fact]
    public void FromParts_BuildsExpectedUri()
    {
        var uri = TrinoSessionOptions.FromParts("trino.example.com", 8443, useTls: true, path: "gateway");

        Assert.Equal("https://trino.example.com:8443/gateway", uri.ToString());
    }
}
