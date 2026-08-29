# Connection-String Reference

`TriQL.Data.ADO`'s `TrinoConnectionStringBuilder` accepts the following keys. Unknown keys are
rejected (`ArgumentException`) rather than silently ignored. Values containing `;`, `=`, or
whitespace must be quoted per the standard `DbConnectionStringBuilder` rules.

| Key | Type | Default | Maps to |
|---|---|---|---|
| `Host` | string | — | `Server` host |
| `Port` | int | `443` when `EnableSsl`, else `8080` | `Server` port |
| `EnableSsl` | bool | `true` | `Server` scheme |
| `Path` | string | — | `Server` path |
| `Server` | string | — | Full URI; mutually exclusive with `Host`/`Port`/`EnableSsl` |
| `User` | string | `Environment.UserName` | `User` |
| `Password` | string | — | Basic/LDAP password (secret) |
| `AuthorizationUser` | string | — | `AuthorizationUser` |
| `Auth` | enum | `none` | Authenticator selection: `none`, `basic`, `ldap`, `jwt`, `certificate`, `entra-id`, `oauth2-client-credentials` |
| `AccessToken` | string | — | JWT token (secret) |
| `ClientId` | string | — | OAuth2 / Entra client id |
| `ClientSecret` | string | — | OAuth2 client secret (secret) |
| `TokenEndpoint` | string | — | OAuth2 token endpoint |
| `Scopes` | string | — | Comma- or space-separated OAuth2/Entra scopes |
| `TenantId` | string | — | Entra tenant (enables `ClientSecretCredential` when combined with `ClientId`/`ClientSecret`) |
| `Catalog` | string | — | `Catalog` |
| `Schema` | string | — | `Schema` |
| `Source` | string | `triql-dotnet` | `Source` |
| `ClientInfo` | string | — | `ClientInfo` |
| `ClientTags` | string | — | Comma-separated `ClientTags` |
| `TraceToken` | string | — | `TraceToken` |
| `TimeZone` | string | host zone | `TimeZone` |
| `Locale` | string | current culture | `Locale` |
| `SessionProperties` | string | — | `k=v` pairs, comma-separated |
| `ExtraCredentials` | string | — | `k=v` pairs, comma-separated |
| `ResourceEstimates` | string | — | `k=v` pairs, comma-separated |
| `Roles` | string | — | `catalog=role` pairs, comma-separated |
| `QueryTimeout` | int (s) | `0` (unbounded) | `QueryTimeout` |
| `RequestTimeout` | int (s) | `100` | `RequestTimeout` |
| `ReadAheadBufferBytes` | long | `52428800` | `ReadAheadBufferBytes` |
| `TargetResultSizeBytes` | long | `5242880` | `TargetResultSizeBytes` |
| `QueryDataEncoding` | string | **1.0:** empty (opt-in) | `QueryDataEncodings`; empty forces the direct protocol. Set `json+zstd,json+lz4,json` to enable spooling — see [README § Spooling protocol](../README.md#spooling-protocol-experimental). |
| `CompressionDisabled` | bool | `false` | `CompressionDisabled` |
| `TestConnection` | bool | `false` | `TestConnectionOnOpen` |
| `AllowSelfSignedCertificate` | bool | `false` | `Tls.AllowSelfSignedCertificate` |
| `AllowHostNameMismatch` | bool | `false` | `Tls.AllowHostNameMismatch` |
| `UseSystemTrustStore` | bool | `true` | `Tls.UseSystemTrustStore` |
| `TrustedCertificatePath` | string | — | `Tls.TrustedRootCertificatePath` |
| `ClientCertificatePath` | string | — | `Tls.ClientCertificates` |
| `ClientCertificateThumbprint` | string | — | `X509Store` lookup |
| `AllowPlaintextCredentials` | bool | `false` | `Tls.AllowPlaintextCredentials` |

`Auth=entra-id` and `Auth=oauth2-client-credentials` are only resolvable once the process has
referenced the `TriQL.Client.Auth` package (it registers itself on load); resolving them without
that package referenced throws `TrinoConfigurationException` naming the missing package.

## Examples

Basic authentication over TLS:

```text
Host=trino.example.com;Port=443;EnableSsl=true;Catalog=hive;Schema=default;
Auth=basic;User=alice;Password=secret
```

Microsoft Entra ID with an explicit client credential:

```text
Host=trino.example.com;Port=443;EnableSsl=true;Catalog=hive;Schema=default;
Auth=entra-id;TenantId=00000000-0000-0000-0000-000000000000;ClientId=...;ClientSecret=...;
Scopes=api://trino/.default;QueryTimeout=300;ReadAheadBufferBytes=104857600
```

OAuth 2.0 client-credentials grant:

```text
Host=trino.example.com;Port=443;EnableSsl=true;Auth=oauth2-client-credentials;
TokenEndpoint=https://idp.example.com/oauth2/token;ClientId=...;ClientSecret=...;Scopes=trino
```
