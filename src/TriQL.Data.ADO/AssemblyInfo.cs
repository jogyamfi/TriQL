using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("TriQL.Data.ADO.Tests")]

// Grants TriQL.Client.Auth access to the internal AuthenticatorRegistry so it can register its
// providers ("entra-id", "oauth2-client-credentials") from a module initializer once referenced
// (FR-2.3.4, P6-T3). This is the one direction of coupling REQ-ARCH-3 permits: TriQL.Data.ADO
// still never references TriQL.Client.Auth.
[assembly: InternalsVisibleTo("TriQL.Client.Auth")]
[assembly: InternalsVisibleTo("TriQL.Client.Auth.Tests")]
