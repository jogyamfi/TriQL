using Amazon.S3.Model;
using TriQL.IntegrationTests.Fixtures;
using Xunit;

namespace TriQL.IntegrationTests.Spooling;

/// <summary>
/// Throwaway-turned-permanent smoke check for the MinIO half of the Phase 7 networking approach:
/// TLS comes up, and the bucket accepts a plain put/get plus an SSE-C put/get. Kept as a fast,
/// isolated diagnostic distinct from the full cluster tests, since a failure here narrows the
/// problem to MinIO/TLS rather than the Trino+MinIO composition.
/// </summary>
[Trait("Category", "Spooling")]
public sealed class MinioSmokeTests : IAsyncLifetime, IDisposable
{
    private readonly MinioFixture _minio = new();

    public Task InitializeAsync() => _minio.InitializeAsync();

    public Task DisposeAsync() => _minio.DisposeAsync();

    public void Dispose() => _minio.Dispose();

    [Fact]
    public async Task Bucket_AcceptsPlainPutAndGet()
    {
        using var client = _minio.CreateS3Client();
        const string key = "smoke/plain.txt";
        const string content = "hello from triql phase 7";

        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _minio.BucketName,
            Key = key,
            ContentBody = content,
        });

        using var response = await client.GetObjectAsync(_minio.BucketName, key);
        using var reader = new StreamReader(response.ResponseStream);
        var actual = await reader.ReadToEndAsync();

        Assert.Equal(content, actual);
    }

    [Fact]
    public async Task Bucket_AcceptsSseCPutAndGet()
    {
        using var client = _minio.CreateS3Client();
        const string key = "smoke/ssec.txt";
        const string content = "sse-c round trip";

        var keyBytes = new byte[32];
        Random.Shared.NextBytes(keyBytes);
        var keyBase64 = Convert.ToBase64String(keyBytes);
        // S3's SSE-C wire protocol mandates an MD5 digest of the customer key (RFC-defined
        // Content-MD5-style integrity check, not a security use of MD5) — required by the API, not
        // a choice made here.
#pragma warning disable CA5351
        var keyMd5 = Convert.ToBase64String(System.Security.Cryptography.MD5.HashData(keyBytes));
#pragma warning restore CA5351

        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _minio.BucketName,
            Key = key,
            ContentBody = content,
            ServerSideEncryptionCustomerMethod = Amazon.S3.ServerSideEncryptionCustomerMethod.AES256,
            ServerSideEncryptionCustomerProvidedKey = keyBase64,
            ServerSideEncryptionCustomerProvidedKeyMD5 = keyMd5,
        });

        using var response = await client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = _minio.BucketName,
            Key = key,
            ServerSideEncryptionCustomerMethod = Amazon.S3.ServerSideEncryptionCustomerMethod.AES256,
            ServerSideEncryptionCustomerProvidedKey = keyBase64,
            ServerSideEncryptionCustomerProvidedKeyMD5 = keyMd5,
        });
        using var reader = new StreamReader(response.ResponseStream);
        var actual = await reader.ReadToEndAsync();

        Assert.Equal(content, actual);
    }
}
