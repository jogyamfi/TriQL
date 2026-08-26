namespace TriQL.Client;

/// <summary>
/// The response of <c>GET /v1/info</c>. See FR-10.1.
/// </summary>
/// <param name="Version">The coordinator's Trino node version string.</param>
/// <param name="Environment">The configured cluster environment name.</param>
/// <param name="Coordinator">Whether the responding node is the coordinator.</param>
/// <param name="Starting">Whether the node is still starting up.</param>
/// <param name="Uptime">The node uptime, as reported by the server, if present.</param>
public sealed record TrinoServerInfo(string Version, string Environment, bool Coordinator, bool Starting, string? Uptime);
