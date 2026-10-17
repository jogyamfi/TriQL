using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace TriQL.EntityFrameworkCore.Query.Internal.Translators;

/// <summary>
/// Translates <c>x.ToString()</c> to <c>CAST(x AS varchar)</c> for the types whose text Trino formats as
/// .NET does: integers, <see cref="decimal"/> (both keep the value's scale) and <see cref="Guid"/>
/// (lower-case, hyphenated). A <see cref="bool"/> becomes <c>'True'</c>/<c>'False'</c>. A nullable column
/// that is <c>NULL</c> becomes <c>''</c>, as <c>Nullable&lt;T&gt;.ToString()</c> returns.
/// </summary>
/// <remarks>
/// <para>
/// Floating-point and date/time values are not translated: Trino formats them differently
/// (<c>1.5E0</c>, ISO dates) and .NET's text depends on the culture, so no translation could match.
/// </para>
/// <para>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </para>
/// </remarks>
public class TrinoObjectToStringTranslator : IMethodCallTranslator
{
    private static readonly HashSet<Type> CastTypes =
    [
        typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
        typeof(long), typeof(ulong), typeof(decimal), typeof(Guid),
    ];

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    /// <summary>Initializes a new instance.</summary>
    public TrinoObjectToStringTranslator(ISqlExpressionFactory sqlExpressionFactory) => _sqlExpressionFactory = sqlExpressionFactory;

    /// <inheritdoc />
    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        ArgumentNullException.ThrowIfNull(method);

        return instance is not null && method.Name == nameof(ToString) && method.HasParameters()
            ? TranslateToString(instance)
            : null;
    }

    /// <summary>The SQL text of <paramref name="value"/>, or <see langword="null"/> if its type is not translated.</summary>
    internal SqlExpression? TranslateToString(SqlExpression value)
    {
        var type = Nullable.GetUnderlyingType(value.Type) ?? value.Type;
        var isNullable = value is ColumnExpression { IsNullable: true };

        if (type == typeof(string))
        {
            return value;
        }

        if (type == typeof(char))
        {
            return _sqlExpressionFactory.ApplyDefaultTypeMapping(value);
        }

        if (type == typeof(bool))
        {
            var whenClauses = new List<CaseWhenClause>
            {
                new(_sqlExpressionFactory.Equal(value, _sqlExpressionFactory.Constant(true)), _sqlExpressionFactory.Constant("True")),
            };
            if (isNullable)
            {
                whenClauses.Add(new(_sqlExpressionFactory.Equal(value, _sqlExpressionFactory.Constant(false)), _sqlExpressionFactory.Constant("False")));
            }

            return _sqlExpressionFactory.Case(whenClauses, _sqlExpressionFactory.Constant(isNullable ? string.Empty : "False"));
        }

        if (!CastTypes.Contains(type))
        {
            return null;
        }

        var text = _sqlExpressionFactory.Convert(value, typeof(string));
        return isNullable ? _sqlExpressionFactory.Coalesce(text, _sqlExpressionFactory.Constant(string.Empty)) : text;
    }
}
