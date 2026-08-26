using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;

namespace TriQL.Client.Internal;

/// <summary>
/// Builds the configured <see cref="SocketsHttpHandler"/> pipeline for a <see cref="TrinoClient"/>. See FR-3.1.4.
/// </summary>
internal static class HttpHandlerFactory
{
    /// <summary>Creates the terminal <see cref="SocketsHttpHandler"/>, wrapped in a <see cref="RedirectHandler"/>.</summary>
    public static HttpMessageHandler Create(TrinoSessionOptions options, ILogger? logger)
    {
#pragma warning disable CA2000 // Ownership transfers to the returned RedirectHandler, disposed by the owning HttpMessageInvoker.
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            EnableMultipleHttp2Connections = true,
            AutomaticDecompression = options.CompressionDisabled
                ? DecompressionMethods.None
                : DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            AllowAutoRedirect = false,
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = ResolveEnabledProtocols(options.Tls.MinimumTlsVersion),
                RemoteCertificateValidationCallback = CertificateValidator.Create(options.Tls, logger),
            },
        };
#pragma warning restore CA2000

        foreach (var clientCertificate in options.Tls.ClientCertificates)
        {
            handler.SslOptions.ClientCertificates ??= [];
            handler.SslOptions.ClientCertificates.Add(clientCertificate);
        }

        options.Authenticator?.ConfigureHandler(handler);

        return new RedirectHandler(handler);
    }

    private static SslProtocols ResolveEnabledProtocols(SslProtocols minimum) => minimum switch
    {
        SslProtocols.Tls13 => SslProtocols.Tls13,
        _ => SslProtocols.Tls12 | SslProtocols.Tls13,
    };
}
