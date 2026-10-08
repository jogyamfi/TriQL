using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace TriQL.EntityFrameworkCore.Query.Internal.Translators;

/// <summary>
/// Translates <see cref="string.Length"/> to <c>length(s)</c>, which counts Unicode code points (.NET
/// counts UTF-16 code units, so the two differ for text outside the Basic Multilingual Plane).
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoStringMemberTranslator : IMemberTranslator
{
    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    /// <summary>Initializes a new instance.</summary>
    public TrinoStringMemberTranslator(ISqlExpressionFactory sqlExpressionFactory) => _sqlExpressionFactory = sqlExpressionFactory;

    /// <inheritdoc />
    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        ArgumentNullException.ThrowIfNull(member);

        return instance is not null && member.DeclaringType == typeof(string) && member.Name == nameof(string.Length)
            ? _sqlExpressionFactory.Function("length", [instance], returnType)
            : null;
    }
}
