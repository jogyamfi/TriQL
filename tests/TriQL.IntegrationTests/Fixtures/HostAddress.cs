using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace TriQL.IntegrationTests.Fixtures;

/// <summary>
/// Resolves this machine's own routable IPv4 address for Phase 7's Lane A fixtures.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not <c>localhost</c> or <c>host.docker.internal</c>.</b> The spooled segment URIs the
/// Trino coordinator hands back are pre-signed URLs built from <c>spooling-manager.properties</c>'
/// <c>s3.endpoint</c>, which the *bare-host* test process must then fetch directly — it never goes
/// through the coordinator. That same <c>s3.endpoint</c> value is also what the Trino coordinator
/// (running inside its own container) uses to reach MinIO to write segments in the first place. No
/// single hostname resolves correctly from both sides: <c>localhost</c> inside the Trino container
/// means the Trino container itself, not the host machine; <c>host.docker.internal</c> is a Docker
/// Desktop convenience DNS name that is not guaranteed to resolve from the bare host process itself
/// (only from inside containers). This machine's real IPv4 address resolves correctly from both:
/// from inside a container reaching out through Docker's default bridge networking — containers can
/// reach the host's own routable interfaces this way on ordinary bridge-mode Docker — and from the
/// host test process as one of its own addresses. Verified end to end against Docker Desktop
/// (Windows, WSL2 backend) during Phase 7 development; CI runs this suite on native Linux Docker
/// (GitHub-hosted ubuntu runners, see the P7-T4 workflow wiring), which the same bridge-networking
/// reasoning is expected to cover but has not yet been separately confirmed in CI at the time of
/// writing — worth a first-run check.
/// </para>
/// </remarks>
internal static class HostAddress
{
    private static readonly Lazy<IPAddress> Cached = new(ResolveCore);

    public static IPAddress Resolve() => Cached.Value;

    private static IPAddress ResolveCore()
    {
        // Prefer a real, active, non-virtual network interface's unicast IPv4 address.
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            var name = nic.Name + " " + nic.Description;
            if (name.Contains("virtual", StringComparison.OrdinalIgnoreCase)
                || name.Contains("vEthernet", StringComparison.OrdinalIgnoreCase)
                || name.Contains("loopback", StringComparison.OrdinalIgnoreCase)
                || name.Contains("docker", StringComparison.OrdinalIgnoreCase)
                || name.Contains("wsl", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr.Address))
                {
                    return addr.Address;
                }
            }
        }

        // Fall back to any non-loopback IPv4 address at all, including virtual adapters — still
        // correct as long as it is reachable from both the host process and Docker Desktop's
        // default bridge network, which is true for every interface Docker Desktop can route to.
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr.Address))
                {
                    return addr.Address;
                }
            }
        }

        // Last resort.
        var hostAddresses = Dns.GetHostAddresses(Dns.GetHostName());
        foreach (var addr in hostAddresses)
        {
            if (addr.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr))
            {
                return addr;
            }
        }

        throw new InvalidOperationException("Could not resolve a non-loopback IPv4 address for this host.");
    }
}
