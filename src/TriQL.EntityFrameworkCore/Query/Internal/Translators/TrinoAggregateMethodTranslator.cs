using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace TriQL.EntityFrameworkCore.Query.Internal.Translators;

/// <summary>
/// Translates the aggregates EF does not know: <see cref="string.Join(string, IEnumerable{string})"/> and
/// <see cref="string.Concat(IEnumerable{string})"/> over a group (ordered, filtered and distinct groups
/// included) and <see cref="TrinoDbFunctionsExtensions.ApproxDistinct{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>string.Join(", ", g.OrderBy(x => x.Name).Select(x => x.Name))</c> becomes
/// <c>COALESCE(array_join(array_agg(COALESCE(name, '') ORDER BY name), ', '), '')</c>. A
/// <see langword="null"/> value joins as an empty string and an empty group gives <c>''</c>, as in .NET.
/// </para>
/// <para>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </para>
/// </remarks>
public class TrinoAggregateMethodTranslator : IAggregateMethodCallTranslator
{
    private static readonly MethodInfo StringJoinMethod =
        typeof(string).GetMethod(nameof(string.Join), [typeof(string), typeof(IEnumerable<string>)])!;

    private static readonly MethodInfo StringConcatMethod =
        typeof(string).GetMethod(nameof(string.Concat), [typeof(IEnumerable<string>)])!;

    private static readonly MethodInfo ApproxDistinctMethod =
        typeof(TrinoDbFunctionsExtensions).GetMethod(nameof(TrinoDbFunctionsExtensions.ApproxDistinct))!;

    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private readonly IRelationalTypeMappingSource _typeMappingSource;

    /// <summary>Initializes a new instance.</summary>
    public TrinoAggregateMethodTranslator(ISqlExpressionFactory sqlExpressionFactory, IRelationalTypeMappingSource typeMappingSource)
    {
        _sqlExpressionFactory = sqlExpressionFactory;
        _typeMappingSource = typeMappingSource;
    }

    /// <inheritdoc />
    public virtual SqlExpression? Translate(
        MethodInfo method,
        EnumerableExpression source,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(arguments);

        if (source.Selector is not SqlExpression value)
        {
            return null;
        }

        if (method == StringJoinMethod || method == StringConcatMethod)
        {
            return TranslateStringJoin(source, value, method == StringJoinMethod ? arguments[0] : _sqlExpressionFactory.Constant(string.Empty));
        }

        if (method.IsGenericMethod && method.GetGenericMethodDefinition() == ApproxDistinctMethod)
        {
            // Distinctness is what approx_distinct estimates, so a Distinct() on the source changes nothing.
            return _sqlExpressionFactory.Function(
                "approx_distinct",
                [Filter(source, value)],
                nullable: false,
                argumentsPropagateNullability: [false],
                typeof(long),
                _typeMappingSource.FindMapping(typeof(long)));
        }

        return null;
    }

    private SqlExpression TranslateStringJoin(EnumerableExpression source, SqlExpression value, SqlExpression separator)
    {
        var typeMapping = value.TypeMapping ?? _typeMappingSource.FindMapping(typeof(string));
        var empty = _sqlExpressionFactory.Constant(string.Empty, typeMapping);

        // .NET joins a null as an empty string; array_join would skip it.
        if (value is not ColumnExpression { IsNullable: false })
        {
            value = _sqlExpressionFactory.Coalesce(value, empty, typeMapping);
        }

        // Rows the predicate filters out become NULL, which array_join skips.
        value = Filter(source, value);
        if (source.IsDistinct)
        {
            value = new DistinctExpression(value);
        }

        return _sqlExpressionFactory.Coalesce(
            new TrinoStringAggregateExpression(value, _sqlExpressionFactory.ApplyTypeMapping(separator, typeMapping), source.Orderings, typeMapping),
            empty,
            typeMapping);
    }

    private SqlExpression Filter(EnumerableExpression source, SqlExpression value) =>
        source.Predicate is null
            ? value
            : _sqlExpressionFactory.Case([new CaseWhenClause(source.Predicate, value)], elseResult: null);
}
