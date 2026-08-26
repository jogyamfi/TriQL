using System.Net;
using System.Reflection;
using TriQL.Client.Internal;

namespace TriQL.Client.Tests;

public sealed class HttpHandlerFactoryTests
{
    [Fact]
    public void Create_ReturnsRedirectHandler_WrappingConfiguredSocketsHttpHandler()
    {
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };

        using var handler = HttpHandlerFactory.Create(options, logger: null);

        var redirectHandler = Assert.IsType<RedirectHandler>(handler);
        var innerHandler = GetInnerHandler(redirectHandler);
        var socketsHandler = Assert.IsType<SocketsHttpHandler>(innerHandler);

        Assert.Equal(TimeSpan.FromMinutes(5), socketsHandler.PooledConnectionLifetime);
        Assert.True(socketsHandler.EnableMultipleHttp2Connections);
        Assert.False(socketsHandler.AllowAutoRedirect);
        Assert.Equal(DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli, socketsHandler.AutomaticDecompression);
    }

    [Fact]
    public void Create_DisablesDecompression_WhenCompressionDisabled()
    {
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/"), CompressionDisabled = true };

        using var handler = HttpHandlerFactory.Create(options, logger: null);
        var socketsHandler = Assert.IsType<SocketsHttpHandler>(GetInnerHandler((RedirectHandler)handler));

        Assert.Equal(DecompressionMethods.None, socketsHandler.AutomaticDecompression);
    }

    [Fact]
    public void Create_AttachesConfiguredClientCertificates()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=triql-test", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        options.Tls.ClientCertificates.Add(certificate);

        using var handler = HttpHandlerFactory.Create(options, logger: null);
        var socketsHandler = Assert.IsType<SocketsHttpHandler>(GetInnerHandler((RedirectHandler)handler));

        Assert.NotNull(socketsHandler.SslOptions.ClientCertificates);
        Assert.Single(socketsHandler.SslOptions.ClientCertificates!);
    }

    private static HttpMessageHandler GetInnerHandler(DelegatingHandler handler)
    {
        var property = typeof(DelegatingHandler).GetProperty("InnerHandler", BindingFlags.Public | BindingFlags.Instance)!;
        return (HttpMessageHandler)property.GetValue(handler)!;
    }
}
