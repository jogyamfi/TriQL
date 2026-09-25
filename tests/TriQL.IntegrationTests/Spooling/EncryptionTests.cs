using Amazon.S3;
using Amazon.S3.Model;
using TriQL.IntegrationTests.Fixtures;
using Xunit;

namespace TriQL.IntegrationTests.Spooling;

/// <summary>
/// P7-T7: SSE-C verification. Confirms spooled segments are genuinely encrypted at rest in MinIO —
/// not readable without the customer-provided key — and that the key material TriQL's
/// <c>SegmentHeaderWriter</c> would attach on a real fetch (the server-supplied per-segment
/// <c>headers</c>) is exactly what unlocks the object.
/// </summary>
/// <remarks>
/// This bypasses <c>TriQL.Client</c> entirely and talks to MinIO directly via
/// <see cref="RawProtocolProbe"/> and <see cref="MinioFixture.CreateS3Client"/>, since the question
/// under test — "is the object actually encrypted server-side" — cannot be answered by the client's
/// own successful decode (the client only proves it can produce correct rows, which it would do
/// identically whether the underlying object were encrypted or not, since decryption happens inside
/// MinIO before the bytes reach the client at all).
/// </remarks>
[Collection(SpoolingClusterCollection.Name)]
[Trait("Category", "Spooling")]
public sealed class EncryptionTests(SpoolingClusterFixture cluster)
{
    [Fact]
    public async Task SpooledSegment_RequiresCorrectCustomerKey_ToRead()
    {
        var segments = await RawProtocolProbe.CollectSegmentsAsync(
            cluster, "SELECT orderkey, linenumber, quantity FROM tpch.tiny.lineitem ORDER BY orderkey, linenumber");

        var spooled = segments.Find(s => s.Type == "spooled");
        Assert.True(spooled is not null, "Expected at least one real 'spooled' segment for this query shape (established by P7-T5's exploration).");
        Assert.NotNull(spooled!.Headers);

        var algorithm = spooled.Headers!["x-amz-server-side-encryption-customer-algorithm"][0];
        var key = spooled.Headers["x-amz-server-side-encryption-customer-key"][0];
        var keyMd5 = spooled.Headers["x-amz-server-side-encryption-customer-key-MD5"][0];
        Assert.Equal("AES256", algorithm);

        var objectKey = RawProtocolProbe.ObjectKeyOf(spooled.SegmentUri!);
        using var s3 = cluster.Minio.CreateS3Client();

        // (1) No key at all: MinIO must refuse to serve SSE-C-encrypted content.
        var withoutKey = await Assert.ThrowsAsync<AmazonS3Exception>(() =>
            s3.GetObjectAsync(new GetObjectRequest { BucketName = cluster.Minio.BucketName, Key = objectKey }));
        Assert.True((int)withoutKey.StatusCode is 400 or 403, $"Expected a rejection status without a key, got {(int)withoutKey.StatusCode}.");

        // (2) A different client's (wrong) key: must also be refused — proves the key genuinely
        // gates access rather than any key being accepted.
        var wrongKeyBytes = new byte[32];
        Random.Shared.NextBytes(wrongKeyBytes);
        var wrongKey = Convert.ToBase64String(wrongKeyBytes);
#pragma warning disable CA5351 // SSE-C's wire protocol mandates an MD5 digest of the customer key; not a security use of MD5.
        var wrongKeyMd5 = Convert.ToBase64String(System.Security.Cryptography.MD5.HashData(wrongKeyBytes));
#pragma warning restore CA5351
        var withWrongKey = await Assert.ThrowsAsync<AmazonS3Exception>(() => s3.GetObjectAsync(new GetObjectRequest
        {
            BucketName = cluster.Minio.BucketName,
            Key = objectKey,
            ServerSideEncryptionCustomerMethod = ServerSideEncryptionCustomerMethod.AES256,
            ServerSideEncryptionCustomerProvidedKey = wrongKey,
            ServerSideEncryptionCustomerProvidedKeyMD5 = wrongKeyMd5,
        }));
        Assert.True((int)withWrongKey.StatusCode is 400 or 403, $"Expected a rejection status with a wrong key, got {(int)withWrongKey.StatusCode}.");

        // (3) The correct key — exactly the header value the real client would have attached via
        // SegmentHeaderWriter — must succeed.
        using var response = await s3.GetObjectAsync(new GetObjectRequest
        {
            BucketName = cluster.Minio.BucketName,
            Key = objectKey,
            ServerSideEncryptionCustomerMethod = ServerSideEncryptionCustomerMethod.AES256,
            ServerSideEncryptionCustomerProvidedKey = key,
            ServerSideEncryptionCustomerProvidedKeyMD5 = keyMd5,
        });
        using var memoryStream = new MemoryStream();
        await response.ResponseStream.CopyToAsync(memoryStream);
        Assert.True(memoryStream.Length > 0, "Decrypted segment content should be non-empty.");
    }
}
