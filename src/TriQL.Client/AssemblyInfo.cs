using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("TriQL.Client.Tests")]
[assembly: InternalsVisibleTo("TriQL.Benchmarks")]

// Grants TriQL.Client.Auth access to the internal ITransmitsBearerCredential marker so its
// bearer-token authenticators participate in the plaintext-credential guard (FR-1.1.3, SEC-3).
[assembly: InternalsVisibleTo("TriQL.Client.Auth")]

// Grants TriQL.Client.Compression access to the internal CodecRegistry/ISegmentCodec extension
// point so its json+lz4/json+zstd codecs can register themselves (FR-5.3.6) without TriQL.Client
// taking a compile-time dependency on either compression package (REQ-ARCH-4).
[assembly: InternalsVisibleTo("TriQL.Client.Compression")]
