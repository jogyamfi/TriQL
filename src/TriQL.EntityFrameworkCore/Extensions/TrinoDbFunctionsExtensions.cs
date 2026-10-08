using Microsoft.EntityFrameworkCore.Diagnostics;

// Extension methods live in EF's namespace, next to EF.Functions.Like and the other providers' functions.
namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Trino-specific functions for LINQ queries, called through <see cref="EF.Functions"/>. They are
/// translated to SQL and run on Trino; calling one outside a query throws.
/// </summary>
public static class TrinoDbFunctionsExtensions
{
    /// <summary>
    /// Case-insensitive <c>LIKE</c>, translated to <c>lower(matchExpression) LIKE lower(pattern)</c>
    /// (Trino has no <c>ILIKE</c>).
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="matchExpression">The string to match.</param>
    /// <param name="pattern">The pattern, with <c>%</c> and <c>_</c> wildcards.</param>
    /// <returns><see langword="true"/> when the string matches the pattern, ignoring case.</returns>
    public static bool ILike(this DbFunctions _, string? matchExpression, string? pattern) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(ILike)));

    /// <summary>
    /// Case-insensitive <c>LIKE</c> with an escape character, translated to
    /// <c>lower(matchExpression) LIKE lower(pattern) ESCAPE escapeCharacter</c>.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="matchExpression">The string to match.</param>
    /// <param name="pattern">The pattern, with <c>%</c> and <c>_</c> wildcards.</param>
    /// <param name="escapeCharacter">The character that makes the next <c>%</c> or <c>_</c> in the pattern literal.</param>
    /// <returns><see langword="true"/> when the string matches the pattern, ignoring case.</returns>
    public static bool ILike(this DbFunctions _, string? matchExpression, string? pattern, string? escapeCharacter) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(ILike)));

    /// <summary>
    /// The approximate number of distinct non-null values in a group, translated to Trino's
    /// <c>approx_distinct(x)</c> aggregate (HyperLogLog, standard error 2.3%). Much cheaper than an exact
    /// <c>Distinct().Count()</c> on large data: <c>g =&gt; EF.Functions.ApproxDistinct(g.Select(o =&gt; o.CustomerId))</c>.
    /// </summary>
    /// <typeparam name="T">The type of the values.</typeparam>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="values">The values of the group.</param>
    /// <returns>The approximate number of distinct values.</returns>
    public static long ApproxDistinct<T>(this DbFunctions _, IEnumerable<T> values) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(ApproxDistinct)));

    /// <summary>
    /// Extracts a scalar (string, number or boolean) from JSON text with a JSONPath expression, translated to
    /// Trino's <c>json_extract_scalar(json, path)</c>. Returns <see langword="null"/> when the path does not
    /// lead to a scalar.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="json">The JSON text.</param>
    /// <param name="path">The JSONPath expression, such as <c>$.address.city</c>.</param>
    /// <returns>The scalar as text.</returns>
    public static string? JsonExtractScalar(this DbFunctions _, string? json, string path) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(JsonExtractScalar)));

    /// <summary>
    /// The number of whole years from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('year', startDate, endDate)</c>. Trino counts <em>complete</em> years elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole years between the two values.</returns>
    public static long DateDiffYear(this DbFunctions _, DateTime startDate, DateTime endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffYear)));

    /// <summary>
    /// The number of whole years from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('year', startDate, endDate)</c>. Trino counts <em>complete</em> years elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole years between the two values.</returns>
    public static long? DateDiffYear(this DbFunctions _, DateTime? startDate, DateTime? endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffYear)));

    /// <summary>
    /// The number of whole years from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('year', startDate, endDate)</c>. Trino counts <em>complete</em> years elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole years between the two values.</returns>
    public static long DateDiffYear(this DbFunctions _, DateTimeOffset startDate, DateTimeOffset endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffYear)));

    /// <summary>
    /// The number of whole years from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('year', startDate, endDate)</c>. Trino counts <em>complete</em> years elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole years between the two values.</returns>
    public static long? DateDiffYear(this DbFunctions _, DateTimeOffset? startDate, DateTimeOffset? endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffYear)));

    /// <summary>
    /// The number of whole years from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('year', startDate, endDate)</c>. Trino counts <em>complete</em> years elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole years between the two values.</returns>
    public static long DateDiffYear(this DbFunctions _, DateOnly startDate, DateOnly endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffYear)));

    /// <summary>
    /// The number of whole years from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('year', startDate, endDate)</c>. Trino counts <em>complete</em> years elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole years between the two values.</returns>
    public static long? DateDiffYear(this DbFunctions _, DateOnly? startDate, DateOnly? endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffYear)));

    /// <summary>
    /// The number of whole months from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('month', startDate, endDate)</c>. Trino counts <em>complete</em> months elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole months between the two values.</returns>
    public static long DateDiffMonth(this DbFunctions _, DateTime startDate, DateTime endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffMonth)));

    /// <summary>
    /// The number of whole months from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('month', startDate, endDate)</c>. Trino counts <em>complete</em> months elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole months between the two values.</returns>
    public static long? DateDiffMonth(this DbFunctions _, DateTime? startDate, DateTime? endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffMonth)));

    /// <summary>
    /// The number of whole months from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('month', startDate, endDate)</c>. Trino counts <em>complete</em> months elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole months between the two values.</returns>
    public static long DateDiffMonth(this DbFunctions _, DateTimeOffset startDate, DateTimeOffset endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffMonth)));

    /// <summary>
    /// The number of whole months from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('month', startDate, endDate)</c>. Trino counts <em>complete</em> months elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole months between the two values.</returns>
    public static long? DateDiffMonth(this DbFunctions _, DateTimeOffset? startDate, DateTimeOffset? endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffMonth)));

    /// <summary>
    /// The number of whole months from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('month', startDate, endDate)</c>. Trino counts <em>complete</em> months elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole months between the two values.</returns>
    public static long DateDiffMonth(this DbFunctions _, DateOnly startDate, DateOnly endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffMonth)));

    /// <summary>
    /// The number of whole months from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('month', startDate, endDate)</c>. Trino counts <em>complete</em> months elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole months between the two values.</returns>
    public static long? DateDiffMonth(this DbFunctions _, DateOnly? startDate, DateOnly? endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffMonth)));

    /// <summary>
    /// The number of whole days from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('day', startDate, endDate)</c>. Trino counts <em>complete</em> days elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole days between the two values.</returns>
    public static long DateDiffDay(this DbFunctions _, DateTime startDate, DateTime endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffDay)));

    /// <summary>
    /// The number of whole days from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('day', startDate, endDate)</c>. Trino counts <em>complete</em> days elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole days between the two values.</returns>
    public static long? DateDiffDay(this DbFunctions _, DateTime? startDate, DateTime? endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffDay)));

    /// <summary>
    /// The number of whole days from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('day', startDate, endDate)</c>. Trino counts <em>complete</em> days elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole days between the two values.</returns>
    public static long DateDiffDay(this DbFunctions _, DateTimeOffset startDate, DateTimeOffset endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffDay)));

    /// <summary>
    /// The number of whole days from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('day', startDate, endDate)</c>. Trino counts <em>complete</em> days elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole days between the two values.</returns>
    public static long? DateDiffDay(this DbFunctions _, DateTimeOffset? startDate, DateTimeOffset? endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffDay)));

    /// <summary>
    /// The number of whole days from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('day', startDate, endDate)</c>. Trino counts <em>complete</em> days elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole days between the two values.</returns>
    public static long DateDiffDay(this DbFunctions _, DateOnly startDate, DateOnly endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffDay)));

    /// <summary>
    /// The number of whole days from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('day', startDate, endDate)</c>. Trino counts <em>complete</em> days elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole days between the two values.</returns>
    public static long? DateDiffDay(this DbFunctions _, DateOnly? startDate, DateOnly? endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffDay)));

    /// <summary>
    /// The number of whole hours from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('hour', startDate, endDate)</c>. Trino counts <em>complete</em> hours elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole hours between the two values.</returns>
    public static long DateDiffHour(this DbFunctions _, DateTime startDate, DateTime endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffHour)));

    /// <summary>
    /// The number of whole hours from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('hour', startDate, endDate)</c>. Trino counts <em>complete</em> hours elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole hours between the two values.</returns>
    public static long? DateDiffHour(this DbFunctions _, DateTime? startDate, DateTime? endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffHour)));

    /// <summary>
    /// The number of whole hours from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('hour', startDate, endDate)</c>. Trino counts <em>complete</em> hours elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole hours between the two values.</returns>
    public static long DateDiffHour(this DbFunctions _, DateTimeOffset startDate, DateTimeOffset endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffHour)));

    /// <summary>
    /// The number of whole hours from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('hour', startDate, endDate)</c>. Trino counts <em>complete</em> hours elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole hours between the two values.</returns>
    public static long? DateDiffHour(this DbFunctions _, DateTimeOffset? startDate, DateTimeOffset? endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffHour)));

    /// <summary>
    /// The number of whole minutes from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('minute', startDate, endDate)</c>. Trino counts <em>complete</em> minutes elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole minutes between the two values.</returns>
    public static long DateDiffMinute(this DbFunctions _, DateTime startDate, DateTime endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffMinute)));

    /// <summary>
    /// The number of whole minutes from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('minute', startDate, endDate)</c>. Trino counts <em>complete</em> minutes elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole minutes between the two values.</returns>
    public static long? DateDiffMinute(this DbFunctions _, DateTime? startDate, DateTime? endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffMinute)));

    /// <summary>
    /// The number of whole minutes from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('minute', startDate, endDate)</c>. Trino counts <em>complete</em> minutes elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole minutes between the two values.</returns>
    public static long DateDiffMinute(this DbFunctions _, DateTimeOffset startDate, DateTimeOffset endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffMinute)));

    /// <summary>
    /// The number of whole minutes from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('minute', startDate, endDate)</c>. Trino counts <em>complete</em> minutes elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole minutes between the two values.</returns>
    public static long? DateDiffMinute(this DbFunctions _, DateTimeOffset? startDate, DateTimeOffset? endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffMinute)));

    /// <summary>
    /// The number of whole seconds from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('second', startDate, endDate)</c>. Trino counts <em>complete</em> seconds elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole seconds between the two values.</returns>
    public static long DateDiffSecond(this DbFunctions _, DateTime startDate, DateTime endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffSecond)));

    /// <summary>
    /// The number of whole seconds from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('second', startDate, endDate)</c>. Trino counts <em>complete</em> seconds elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole seconds between the two values.</returns>
    public static long? DateDiffSecond(this DbFunctions _, DateTime? startDate, DateTime? endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffSecond)));

    /// <summary>
    /// The number of whole seconds from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('second', startDate, endDate)</c>. Trino counts <em>complete</em> seconds elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole seconds between the two values.</returns>
    public static long DateDiffSecond(this DbFunctions _, DateTimeOffset startDate, DateTimeOffset endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffSecond)));

    /// <summary>
    /// The number of whole seconds from <paramref name="startDate"/> to <paramref name="endDate"/>, translated to
    /// <c>date_diff('second', startDate, endDate)</c>. Trino counts <em>complete</em> seconds elapsed (SQL Server's
    /// <c>DATEDIFF</c> counts boundaries crossed), and the result is negative when <paramref name="endDate"/> is earlier.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="startDate">The start of the period.</param>
    /// <param name="endDate">The end of the period.</param>
    /// <returns>The number of whole seconds between the two values.</returns>
    public static long? DateDiffSecond(this DbFunctions _, DateTimeOffset? startDate, DateTimeOffset? endDate) =>
        throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DateDiffSecond)));
}
