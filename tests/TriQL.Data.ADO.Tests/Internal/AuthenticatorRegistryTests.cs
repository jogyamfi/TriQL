using TriQL.Client.Exceptions;
using TriQL.Data.ADO.Internal;

namespace TriQL.Data.ADO.Tests.Internal;

public sealed class AuthenticatorRegistryTests
{
    [Fact]
    public void Resolve_None_ReturnsAnonymousAuthenticator()
    {
        var auth = AuthenticatorRegistry.Resolve("none", default);
        Assert.IsType<TriQL.Client.Auth.AnonymousAuthenticator>(auth);
    }

    [Fact]
    public void Resolve_Basic_ReturnsBasicAuthenticator()
    {
        var context = new AuthenticatorContext("alice", "pw", null, null, null, null, null, null, null, null);
        var auth = AuthenticatorRegistry.Resolve("basic", context);
        Assert.IsType<TriQL.Client.Auth.BasicAuthenticator>(auth);
    }

    [Fact]
    public void Resolve_Basic_MissingUser_Throws()
    {
        var context = new AuthenticatorContext(null, "pw", null, null, null, null, null, null, null, null);
        Assert.Throws<TrinoConfigurationException>(() => AuthenticatorRegistry.Resolve("basic", context));
    }

    [Fact]
    public void Resolve_Jwt_ReturnsJwtAuthenticator()
    {
        var context = new AuthenticatorContext(null, null, "token", null, null, null, null, null, null, null);
        var auth = AuthenticatorRegistry.Resolve("jwt", context);
        Assert.IsType<TriQL.Client.Auth.JwtAuthenticator>(auth);
    }

    [Fact]
    public void Resolve_Certificate_WithoutPathOrThumbprint_Throws()
    {
        Assert.Throws<TrinoConfigurationException>(() => AuthenticatorRegistry.Resolve("certificate", default));
    }

    [Fact]
    public void Resolve_UnknownName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => AuthenticatorRegistry.Resolve("not-a-real-kind", default));
    }

    [Theory]
    [InlineData("entra-id")]
    [InlineData("oauth2-client-credentials")]
    public void Resolve_PackageBackedAuthenticator_NamesTheMissingPackage(string authKind)
    {
        var ex = Assert.Throws<TrinoConfigurationException>(() => AuthenticatorRegistry.Resolve(authKind, default));
        Assert.Contains("TriQL.Client.Auth", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Register_AddsAResolvableCustomAuthenticator()
    {
        AuthenticatorRegistry.Register("test-custom-auth-kind", _ => TriQL.Client.Auth.AnonymousAuthenticator.Instance);
        var auth = AuthenticatorRegistry.Resolve("test-custom-auth-kind", default);
        Assert.Same(TriQL.Client.Auth.AnonymousAuthenticator.Instance, auth);
    }
}
