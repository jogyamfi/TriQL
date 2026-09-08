using System.Data.Common;
using TriQL.Client;
using TriQL.Client.Auth;
using TriQL.Data.ADO;

namespace TriQL.Samples.CertificateAuth;

/// <summary>
/// Connects to the  Trino coordinator over mutual TLS using a client certificate,
/// </summary>
public static class CertificateAuthSample
{
    private const string Host = "YourHost";
    private const int Port = 443;
    private const string Catalog = "Your Catalog";
    private const string UserName = "Your User Name";

    // update this, or set TRINO_CLIENT_CERTIFICATE_PATH, to point at your local .pem.
    private const string DefaultCertificatePemPath = @"Path\To\Your\Certificate.pem";

    private const string Query = "SELECT * FROM YOUR_SCHEMA.YOUR_TABLE LIMIT 1";

    public static async Task Main()
    {
        var certificatePemPath = Environment.GetEnvironmentVariable("TRINO_CLIENT_CERTIFICATE_PATH") ?? DefaultCertificatePemPath;
        if (!File.Exists(certificatePemPath))
        {
            await Console.Error.WriteLineAsync(
                $"Client certificate not found at '{certificatePemPath}'. Set TRINO_CLIENT_CERTIFICATE_PATH or update DefaultCertificatePemPath.");
            return;
        }

        // The .pem holds both the certificate and its private key, so the same text is passed for both parameters.
        var pem = await File.ReadAllTextAsync(certificatePemPath);
        using var authenticator = ClientCertificateAuthenticator.FromPem(pem, pem);

        var options = new TrinoSessionOptions
        {
            Server = TrinoSessionOptions.FromParts(Host, Port, useTls: true),
            User = UserName,
            Catalog = Catalog,
            Authenticator = authenticator,
        };

        await using DbConnection connection = new TrinoConnection(options);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = Query;

        await using var reader = await command.ExecuteReaderAsync();

        var columnNames = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName);
        Console.WriteLine(string.Join(" | ", columnNames));

        while (await reader.ReadAsync())
        {
            var values = Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "NULL" : reader.GetValue(i)?.ToString());
            Console.WriteLine(string.Join(" | ", values));
        }
    }
}
