using TriQL.Client.Exceptions;
using TriQL.Client.Internal;

namespace TriQL.Client.Tests.Security;

/// <summary>
/// SSRF guard accept/reject tests for segment and acknowledgement URIs (P5-T9, SEC-7, the closed G3
/// decision in requirements.md §23 Q6).
/// </summary>
public sealed class UriGuardTests
{
    private static readonly Uri CoordinatorOrigin = new("https://coordinator.example.com:8443/");

    [Fact]
    public void ValidateAckUri_SameOriginHttps_Accepted()
    {
        var ackUri = new Uri("https://coordinator.example.com:8443/v1/spooling/ack/1");
        UriGuard.ValidateAckUri(ackUri, CoordinatorOrigin); // does not throw
    }

    [Fact]
    public void ValidateAckUri_OffOriginHost_Rejected()
    {
        var ackUri = new Uri("https://objectstore.example.com/ack/1");
        var ex = Assert.Throws<TrinoProtocolException>(() => UriGuard.ValidateAckUri(ackUri, CoordinatorOrigin));
        Assert.Contains("origin", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateAckUri_DifferentPortSameHost_Rejected()
    {
        var ackUri = new Uri("https://coordinator.example.com:9999/v1/spooling/ack/1");
        Assert.Throws<TrinoProtocolException>(() => UriGuard.ValidateAckUri(ackUri, CoordinatorOrigin));
    }

    [Fact]
    public void ValidateAckUri_HttpDowngrade_Rejected()
    {
        var ackUri = new Uri("http://coordinator.example.com:8443/v1/spooling/ack/1");
        var ex = Assert.Throws<TrinoProtocolException>(() => UriGuard.ValidateAckUri(ackUri, CoordinatorOrigin));
        Assert.Contains("https", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateSegmentUri_OffOriginHttps_AcceptedWhenNoAllowlist()
    {
        var segmentUri = new Uri("https://s3.amazonaws.com/bucket/segment-1");
        UriGuard.ValidateSegmentUri(segmentUri, hostAllowlist: []); // object storage is legitimately off-origin
    }

    [Fact]
    public void ValidateSegmentUri_HttpDowngrade_RejectedEvenOffOrigin()
    {
        var segmentUri = new Uri("http://s3.amazonaws.com/bucket/segment-1");
        var ex = Assert.Throws<TrinoProtocolException>(() => UriGuard.ValidateSegmentUri(segmentUri, hostAllowlist: []));
        Assert.Contains("https", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateSegmentUri_HostInAllowlist_Accepted()
    {
        var segmentUri = new Uri("https://s3.amazonaws.com/bucket/segment-1");
        UriGuard.ValidateSegmentUri(segmentUri, hostAllowlist: ["s3.amazonaws.com"]);
    }

    [Fact]
    public void ValidateSegmentUri_HostAllowlistCaseInsensitive_Accepted()
    {
        var segmentUri = new Uri("https://S3.Amazonaws.com/bucket/segment-1");
        UriGuard.ValidateSegmentUri(segmentUri, hostAllowlist: ["s3.amazonaws.com"]);
    }

    [Fact]
    public void ValidateSegmentUri_HostNotInAllowlist_Rejected()
    {
        var segmentUri = new Uri("https://evil.example.com/steal");
        var ex = Assert.Throws<TrinoProtocolException>(
            () => UriGuard.ValidateSegmentUri(segmentUri, hostAllowlist: ["s3.amazonaws.com"]));
        Assert.Contains("SegmentHostAllowlist", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IsSameOrigin_MatchesSchemeHostPortExactly()
    {
        Assert.True(UriGuard.IsSameOrigin(new Uri("https://coordinator.example.com:8443/x"), CoordinatorOrigin));
        Assert.False(UriGuard.IsSameOrigin(new Uri("https://other.example.com:8443/x"), CoordinatorOrigin));
        Assert.False(UriGuard.IsSameOrigin(new Uri("http://coordinator.example.com:8443/x"), CoordinatorOrigin));
        Assert.False(UriGuard.IsSameOrigin(new Uri("https://coordinator.example.com:1234/x"), CoordinatorOrigin));
    }
}
