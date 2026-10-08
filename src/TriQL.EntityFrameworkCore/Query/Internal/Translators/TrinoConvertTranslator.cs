using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace TriQL.EntityFrameworkCore.Query.Internal.Translators;

/// <summary>
/// Translates <see cref="Convert"/>'s <c>ToBoolean</c>, <c>ToByte</c>, <c>ToDecimal</c>, <c>ToDouble</c>,
/// <c>ToInt16</c>, <c>ToInt32</c>, <c>ToInt64</c> and <c>ToString</c> to <c>CAST</c>. A value Trino
/// cannot convert fails the query; it never becomes <c>NULL</c> (no <c>TRY_CAST</c>).
/// </summary>
/// <remarks>
/// <para>
/// Trino's <c>CAST</c> to an integer type rounds half away from zero, while .NET's <see cref="Convert"/>
/// rounds half to even (<c>Convert.ToInt32(2.5)</c> is 2 in .NET and 3 in Trino). <c>ToDecimal</c> casts
/// to the default <c>decimal(18,2)</c>. <c>ToString</c> is translated only for the types whose text Trino
/// formats as .NET does (integers, <see cref="decimal"/>, <see cref="Guid"/>, <see cref="string"/>); a
/// <see cref="bool"/> becomes <c>'True'</c>/<c>'False'</c>, .NET's spelling.
/// </para>
/// <para>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </para>
/// </remarks>
public class TrinoConvertTranslator : IMethodCallTranslator
{
    private static readonly Dictionary<string, Type> TargetTypes = new(StringComparer.Ordinal)
    {
        [nameof(Convert.ToBoolean)] = typeof(bool),
        [nameof(Convert.ToByte)] = typeof(byte),
        [nameof(Convert.ToDecimal)] = typeof(decimal),
        [nameof(Convert.ToDouble)] = typeof(double),
        [nameof(Convert.ToInt16)] = typeof(short),
        [nameof(Convert.ToInt32)] = typeof(int),
        [nameof(Convert.ToInt64)] = typeof(long),
        [nameof(Convert.ToString)] = typeof(string),
    };

    private static readonly HashSet<Type> SupportedSourceTypes =
    [
        typeof(bool), typeof(byte), typeof(decimal), typeof(double), typeof(float),
        typeof(short), typeof(int), typeof(long), typeof(string),
    ];

    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private readonly TrinoObjectToStringTranslator _toStringTranslator;

    /// <summary>Initializes a new instance.</summary>
    public TrinoConvertTranslator(ISqlExpressionFactory sqlExpressionFactory)
    {
        _sqlExpressionFactory = sqlExpressionFactory;
        _toStringTranslator = new TrinoObjectToStringTranslator(sqlExpressionFactory);
    }

    /// <inheritdoc />
    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(arguments);

        if (method.DeclaringType != typeof(Convert)
            || arguments.Count != 1
            || !TargetTypes.TryGetValue(method.Name, out var targetType)
            || !SupportedSourceTypes.Contains(method.GetParameters()[0].ParameterType))
        {
            return null;
        }

        return targetType == typeof(string)
            ? _toStringTranslator.TranslateToString(arguments[0])
            : _sqlExpressionFactory.Convert(arguments[0], targetType);
    }
}
