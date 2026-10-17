using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace TriQL.EntityFrameworkCore.Query.Internal.Translators;

/// <summary>
/// Translates the <c>Add*</c> methods of <see cref="DateTime"/>, <see cref="DateTimeOffset"/> and
/// <see cref="DateOnly"/> to <c>date_add(unit, n, value)</c>, and <see cref="DateOnly.FromDateTime"/> to
/// <c>CAST(value AS date)</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>date_add</c> takes a whole number of units. <c>AddDays</c>, <c>AddHours</c>, … take a
/// <see cref="double"/>: an integer argument (a constant, or an integer column or expression converted by the
/// compiler) adds whole units; any other value is converted to milliseconds, so a fraction below one
/// millisecond is rounded (.NET keeps it to the tick).
/// </para>
/// <para>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </para>
/// </remarks>
public class TrinoDateTimeMethodTranslator : IMethodCallTranslator
{
    private static readonly Dictionary<string, (string Unit, long Milliseconds)> FractionalUnits = new(StringComparer.Ordinal)
    {
        [nameof(DateTime.AddDays)] = ("day", 86_400_000),
        [nameof(DateTime.AddHours)] = ("hour", 3_600_000),
        [nameof(DateTime.AddMinutes)] = ("minute", 60_000),
        [nameof(DateTime.AddSeconds)] = ("second", 1_000),
        [nameof(DateTime.AddMilliseconds)] = ("millisecond", 1),
    };

    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private readonly IRelationalTypeMappingSource _typeMappingSource;

    /// <summary>Initializes a new instance.</summary>
    public TrinoDateTimeMethodTranslator(ISqlExpressionFactory sqlExpressionFactory, IRelationalTypeMappingSource typeMappingSource)
    {
        _sqlExpressionFactory = sqlExpressionFactory;
        _typeMappingSource = typeMappingSource;
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

        var declaringType = method.DeclaringType;
        if (declaringType != typeof(DateTime) && declaringType != typeof(DateTimeOffset) && declaringType != typeof(DateOnly))
        {
            return null;
        }

        if (instance is null)
        {
            return declaringType == typeof(DateOnly) && method.Name == nameof(DateOnly.FromDateTime) && method.HasParameters(typeof(DateTime))
                ? _sqlExpressionFactory.Convert(arguments[0], typeof(DateOnly), _typeMappingSource.FindMapping(typeof(DateOnly)))
                : null;
        }

        if (arguments.Count != 1)
        {
            return null;
        }

        var amount = arguments[0];
        return method.Name switch
        {
            nameof(DateTime.AddYears) when amount.Type == typeof(int) => DateAdd("year", amount, instance),
            nameof(DateTime.AddMonths) when amount.Type == typeof(int) => DateAdd("month", amount, instance),

            // DateOnly.AddDays takes an int.
            nameof(DateOnly.AddDays) when amount.Type == typeof(int) => DateAdd("day", amount, instance),

            _ when declaringType != typeof(DateOnly) && amount.Type == typeof(double)
                && FractionalUnits.TryGetValue(method.Name, out var unit) => TranslateFractional(unit.Unit, unit.Milliseconds, amount, instance),

            _ => null,
        };
    }

    private SqlExpression TranslateFractional(string unit, long milliseconds, SqlExpression amount, SqlExpression instance)
    {
        switch (amount)
        {
            case SqlConstantExpression { Value: double value } when value == Math.Truncate(value) && Math.Abs(value) <= int.MaxValue:
                return DateAdd(unit, _sqlExpressionFactory.Constant((int)value), instance);

            // The compiler's conversion of an integer argument (AddDays(p.Days)): add the integer itself.
            case SqlUnaryExpression { OperatorType: System.Linq.Expressions.ExpressionType.Convert, Operand: var operand }
                when operand.Type == typeof(int) || operand.Type == typeof(long) || operand.Type == typeof(short) || operand.Type == typeof(byte):
                return DateAdd(unit, operand, instance);

            default:
                // CAST(double AS bigint) rounds to the nearest integer.
                var totalMilliseconds = unit == "millisecond"
                    ? amount
                    : _sqlExpressionFactory.Multiply(amount, _sqlExpressionFactory.Constant((double)milliseconds));
                return DateAdd("millisecond", _sqlExpressionFactory.Convert(totalMilliseconds, typeof(long)), instance);
        }
    }

    private SqlExpression DateAdd(string unit, SqlExpression amount, SqlExpression instance) =>
        _sqlExpressionFactory.Function(
            "date_add",
            [
                _sqlExpressionFactory.Constant(unit, _typeMappingSource.FindMapping(typeof(string))),
                _sqlExpressionFactory.ApplyDefaultTypeMapping(amount),
                instance,
            ],
            nullable: true,
            argumentsPropagateNullability: [false, true, true],
            instance.Type,
            instance.TypeMapping);
}
