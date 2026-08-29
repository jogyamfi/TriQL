using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("TriQL.Client.Tests")]
[assembly: InternalsVisibleTo("TriQL.Benchmarks")]

// Grants TriQL.Client.Auth access to the internal ITransmitsBearerCredential marker so its
// bearer-token authenticators participate in the plaintext-credential guard (FR-1.1.3, SEC-3).
[assembly: InternalsVisibleTo("TriQL.Client.Auth")]
