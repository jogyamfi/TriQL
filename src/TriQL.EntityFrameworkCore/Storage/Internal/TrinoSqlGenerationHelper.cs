using System.Text;
using Microsoft.EntityFrameworkCore.Storage;

namespace TriQL.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Trino SQL syntax: identifiers in double quotes with <c>""</c> as the escape (EF's default),
/// string literals in single quotes with <c>''</c> as the escape (no backslash escapes), named
/// parameter placeholders <c>@name</c>, and one statement per request (no terminator).
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoSqlGenerationHelper : RelationalSqlGenerationHelper
{
    /// <summary>Initializes a new instance.</summary>
    public TrinoSqlGenerationHelper(RelationalSqlGenerationHelperDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <summary>
    /// Empty: Trino accepts exactly one statement per request, so the provider never emits batches
    /// and a trailing <c>;</c> would be a syntax error.
    /// </summary>
    public override string StatementTerminator => string.Empty;

    /// <summary>Separates statements in a generated script (e.g. <c>GenerateCreateScript</c>), which is never sent as one request.</summary>
    public override string BatchTerminator => ";" + Environment.NewLine;

    /// <summary>Renders <paramref name="value"/> as a Trino string literal, doubling embedded single quotes.</summary>
    public static string GenerateStringLiteral(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    /// <summary>Appends <paramref name="value"/> to <paramref name="builder"/> as a Trino string literal.</summary>
    public static void GenerateStringLiteral(StringBuilder builder, string value)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(value);
        builder.Append('\'').Append(value.Replace("'", "''", StringComparison.Ordinal)).Append('\'');
    }
}
