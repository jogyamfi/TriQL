using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace TriQL.EntityFrameworkCore.Query.Internal.Translators;

/// <summary>Translates <see cref="Math"/> methods to Trino's mathematical functions.</summary>
/// <remarks>
/// <para>
/// <c>Math.Max</c>/<c>Math.Min</c> are translated by EF's SQL translator itself, through
/// <see cref="TrinoSqlTranslatingExpressionVisitor.GenerateGreatest"/>/<see cref="TrinoSqlTranslatingExpressionVisitor.GenerateLeast"/>.
/// </para>
/// <para>
/// Trino's <c>round</c> rounds half away from zero; .NET's <see cref="Math.Round(double)"/> rounds half
/// to even by default. <c>Math.Round(x, MidpointRounding.AwayFromZero)</c> (with or without digits)
/// becomes <c>round</c> directly. Half-to-even rounding is emulated, exactly, for a <see cref="double"/>
/// or <see cref="decimal"/> rounded to an integer and for a <see cref="decimal"/> rounded to a constant
/// number of digits. A <see cref="double"/> rounded half-to-even to digits is not translated: its
/// midpoints (such as 0.125) are not exact after scaling, so no SQL expression matches .NET's result.
/// </para>
/// <para>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </para>
/// </remarks>
public class TrinoMathTranslator : IMethodCallTranslator
{
    /// <summary>The most digits half-to-even rounding of a decimal is emulated for.</summary>
    internal const int MaxEmulatedRoundingDigits = 18;

    private static readonly Dictionary<string, string> SameTypeFunctions = new(StringComparer.Ordinal)
    {
        [nameof(Math.Abs)] = "abs",
        [nameof(Math.Ceiling)] = "ceiling",
        [nameof(Math.Floor)] = "floor",
        [nameof(Math.Truncate)] = "truncate",
    };

    private static readonly Dictionary<string, string> DoubleFunctions = new(StringComparer.Ordinal)
    {
        [nameof(Math.Sqrt)] = "sqrt",
        [nameof(Math.Cbrt)] = "cbrt",
        [nameof(Math.Exp)] = "exp",
        [nameof(Math.Log10)] = "log10",
        [nameof(Math.Log2)] = "log2",
        [nameof(Math.Pow)] = "power",
        [nameof(Math.Acos)] = "acos",
        [nameof(Math.Asin)] = "asin",
        [nameof(Math.Atan)] = "atan",
        [nameof(Math.Atan2)] = "atan2",
        [nameof(Math.Cos)] = "cos",
        [nameof(Math.Cosh)] = "cosh",
        [nameof(Math.Sin)] = "sin",
        [nameof(Math.Sinh)] = "sinh",
        [nameof(Math.Tan)] = "tan",
        [nameof(Math.Tanh)] = "tanh",
    };

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    /// <summary>Initializes a new instance.</summary>
    public TrinoMathTranslator(ISqlExpressionFactory sqlExpressionFactory) => _sqlExpressionFactory = sqlExpressionFactory;

    /// <inheritdoc />
    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(arguments);

        if (method.DeclaringType != typeof(Math) || arguments.Count == 0)
        {
            return null;
        }

        if (SameTypeFunctions.TryGetValue(method.Name, out var sameTypeFunction))
        {
            var typeMapping = ExpressionExtensions.InferTypeMapping(arguments);
            return _sqlExpressionFactory.Function(
                sameTypeFunction,
                [.. arguments.Select(a => _sqlExpressionFactory.ApplyTypeMapping(a, typeMapping))],
                method.ReturnType,
                typeMapping);
        }

        if (DoubleFunctions.TryGetValue(method.Name, out var doubleFunction))
        {
            return _sqlExpressionFactory.Function(doubleFunction, [.. arguments.Select(a => _sqlExpressionFactory.ApplyDefaultTypeMapping(a)!)], method.ReturnType);
        }

        return method.Name switch
        {
            nameof(Math.Log) when arguments.Count == 1 => _sqlExpressionFactory.Function("ln", [arguments[0]], typeof(double)),

            // Math.Log(a, newBase) is log(base, a) in Trino.
            nameof(Math.Log) => _sqlExpressionFactory.Function("log", [arguments[1], arguments[0]], typeof(double)),

            // .NET returns int; Trino's sign has the argument's type.
            nameof(Math.Sign) => _sqlExpressionFactory.Convert(
                _sqlExpressionFactory.Function("sign", [arguments[0]], arguments[0].Type, arguments[0].TypeMapping),
                typeof(int)),

            nameof(Math.Round) => TranslateRound(method, arguments),

            _ => null,
        };
    }

    private SqlExpression? TranslateRound(MethodInfo method, IReadOnlyList<SqlExpression> arguments)
    {
        var value = arguments[0];
        if (value.Type != typeof(double) && value.Type != typeof(decimal))
        {
            return null;
        }

        SqlExpression? digits = null;
        SqlExpression? mode = null;
        var parameters = method.GetParameters();
        switch (parameters.Length)
        {
            case 1:
                break;
            case 2 when parameters[1].ParameterType == typeof(int):
                digits = arguments[1];
                break;
            case 2:
                mode = arguments[1];
                break;
            case 3:
                digits = arguments[1];
                mode = arguments[2];
                break;
            default:
                return null;
        }

        switch (mode)
        {
            case null or SqlConstantExpression { Value: MidpointRounding.ToEven }:
                return TranslateRoundHalfToEven(value, digits);

            case SqlConstantExpression { Value: MidpointRounding.AwayFromZero }:
                return _sqlExpressionFactory.Function(
                    "round",
                    digits is null ? [value] : [value, digits],
                    value.Type,
                    value.TypeMapping);

            default:
                return null;
        }
    }

    /// <summary>
    /// <c>CASE WHEN abs(x - r) = half AND mod(r * 10^d, 2) &lt;&gt; 0 THEN r - sign(x) * unit ELSE r END</c>
    /// with <c>r = round(x, d)</c> and <c>unit = 10^-d</c>: where Trino rounded a midpoint away from zero
    /// to an odd last digit, step back towards zero to the even neighbour.
    /// </summary>
    private SqlExpression? TranslateRoundHalfToEven(SqlExpression value, SqlExpression? digitsArgument)
    {
        var digits = digitsArgument switch
        {
            null => 0,
            SqlConstantExpression { Value: int d } => d,
            _ => -1,
        };

        if (digits < 0 || digits > MaxEmulatedRoundingDigits || (digits > 0 && value.Type == typeof(double)))
        {
            return null;
        }

        var typeMapping = value.TypeMapping;
        var isDecimal = value.Type == typeof(decimal);
        var scale = 1m;
        for (var i = 0; i < digits; i++)
        {
            scale *= 10;
        }

        SqlExpression Number(decimal number) =>
            _sqlExpressionFactory.Constant(isDecimal ? number : (object)(double)number, typeMapping);

        var rounded = _sqlExpressionFactory.Function(
            "round",
            digits == 0 ? [value] : [value, _sqlExpressionFactory.Constant(digits)],
            value.Type,
            typeMapping);
        var sign = _sqlExpressionFactory.Function("sign", [value], value.Type, typeMapping);

        var isMidpoint = _sqlExpressionFactory.Equal(
            _sqlExpressionFactory.Function("abs", [_sqlExpressionFactory.Subtract(value, rounded)], value.Type, typeMapping),
            Number(0.5m / scale));
        var isOdd = _sqlExpressionFactory.NotEqual(
            _sqlExpressionFactory.Function(
                "mod",
                [digits == 0 ? rounded : _sqlExpressionFactory.Multiply(rounded, Number(scale)), Number(2)],
                value.Type,
                typeMapping),
            Number(0));
        var step = digits == 0 ? sign : _sqlExpressionFactory.Multiply(sign, Number(1m / scale));

        return _sqlExpressionFactory.Case(
            [new CaseWhenClause(_sqlExpressionFactory.AndAlso(isMidpoint, isOdd), _sqlExpressionFactory.Subtract(rounded, step))],
            rounded);
    }
}
