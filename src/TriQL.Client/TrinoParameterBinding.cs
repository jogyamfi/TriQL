namespace TriQL.Client;

/// <summary>
/// How a parameterized statement's text reaches the coordinator. In both modes the values are bound
/// by the server from typed literals in a <c>USING</c> clause; parameter values are never
/// interpolated into the statement text (FR-8.3, SEC-4).
/// </summary>
public enum TrinoParameterBinding
{
    /// <summary>
    /// The statement is registered as a session prepared statement, sent in the
    /// <c>X-Trino-Prepared-Statement</c> request header, and run with <c>EXECUTE name USING …</c>.
    /// The default. Large statements can exceed the header limits of proxies or gateways in front
    /// of the coordinator (often 8–16 KB per header).
    /// </summary>
    PreparedStatementHeader,

    /// <summary>
    /// The statement is sent in the request body as <c>EXECUTE IMMEDIATE '…' USING …</c> (Trino 418+),
    /// so no request header grows with the statement. Bounded instead by the coordinator's
    /// <c>query.max-length</c> (1,000,000 characters by default), which counts the statement and its values.
    /// </summary>
    ExecuteImmediate,
}
