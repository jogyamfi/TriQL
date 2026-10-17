using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace TriQL.EntityFrameworkCore.Query.Internal.Translators;

/// <summary>
/// Translates <see cref="Guid.NewGuid"/> to <c>uuid()</c> and <see cref="Regex.IsMatch(string, string)"/>
/// to <c>regexp_like(input, pattern)</c>.
/// </summary>
/// <remarks>
/// <para>
/// Trino evaluates patterns with its own regular-expression library (Joni, Ruby syntax, by default), not
/// .NET's; common syntax agrees, but constructs such as named groups and look-behind can differ.
/// <see cref="RegexOptions.IgnoreCase"/> is translated as the inline <c>(?i)</c> flag; other options are not
/// translated.
/// </para>
/// <para>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </para>
/// </remarks>
public class TrinoMiscMethodTranslator : IMethodCallTranslator
{
    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    /// <summary>Initializes a new instance.</summary>
    public TrinoMiscMethodTranslator(ISqlExpressionFactory sqlExpressionFactory) => _sqlExpressionFactory = sqlExpressionFactory;

    /// <inheritdoc />
    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(arguments);

        if (method.DeclaringType == typeof(Guid) && method.Name == nameof(Guid.NewGuid) && method.HasParameters())
        {
            return _sqlExpressionFactory.Function(
                "uuid",
                [],
                nullable: false,
                argumentsPropagateNullability: [],
                typeof(Guid));
        }

        if (method.DeclaringType != typeof(Regex) || instance is not null || method.Name != nameof(Regex.IsMatch))
        {
            return null;
        }

        if (method.HasParameters(typeof(string), typeof(string)))
        {
            return RegexpLike(arguments[0], arguments[1]);
        }

        if (method.HasParameters(typeof(string), typeof(string), typeof(RegexOptions)) && arguments[2] is SqlConstantExpression { Value: RegexOptions options })
        {
            return options switch
            {
                RegexOptions.None => RegexpLike(arguments[0], arguments[1]),
                RegexOptions.IgnoreCase => RegexpLike(arguments[0], _sqlExpressionFactory.Add(_sqlExpressionFactory.Constant("(?i)"), arguments[1])),
                _ => null,
            };
        }

        return null;
    }

    private SqlExpression RegexpLike(SqlExpression input, SqlExpression pattern)
    {
        var typeMapping = ExpressionExtensions.InferTypeMapping(input, pattern);
        return _sqlExpressionFactory.Function(
            "regexp_like",
            [_sqlExpressionFactory.ApplyTypeMapping(input, typeMapping), _sqlExpressionFactory.ApplyTypeMapping(pattern, typeMapping)],
            typeof(bool));
    }
}
