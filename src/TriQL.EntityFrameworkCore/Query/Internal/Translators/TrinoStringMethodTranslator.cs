using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace TriQL.EntityFrameworkCore.Query.Internal.Translators;

/// <summary>
/// Translates <see cref="string"/> methods to Trino functions. Positions are converted between .NET's
/// 0-based and Trino's 1-based indexing. Trino counts and indexes Unicode code points where .NET counts
/// UTF-16 code units, so results differ for text outside the Basic Multilingual Plane.
/// </summary>
/// <remarks>
/// <para>
/// <c>Contains</c>/<c>StartsWith</c>/<c>EndsWith</c> with a constant argument become <c>LIKE</c> with the
/// pattern escaped at translation time (connectors can push a <c>LIKE</c> prefix down to the source);
/// with any other argument they become <c>strpos</c>/<c>starts_with</c> calls, so the value stays a bound
/// parameter and needs no escaping. Trino's no-argument <c>trim</c> removes Unicode white space except
/// the non-breaking spaces (U+00A0, U+2007, U+202F), which .NET's <c>Trim()</c> also removes.
/// </para>
/// <para>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </para>
/// </remarks>
public class TrinoStringMethodTranslator : IMethodCallTranslator
{
    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    /// <summary>Initializes a new instance.</summary>
    public TrinoStringMethodTranslator(ISqlExpressionFactory sqlExpressionFactory) => _sqlExpressionFactory = sqlExpressionFactory;

    /// <inheritdoc />
    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(arguments);

        if (method.DeclaringType != typeof(string))
        {
            return null;
        }

        if (instance is null)
        {
            return method.Name == nameof(string.IsNullOrWhiteSpace) && method.HasParameters(typeof(string))
                ? _sqlExpressionFactory.OrElse(
                    _sqlExpressionFactory.IsNull(arguments[0]),
                    _sqlExpressionFactory.Equal(
                        _sqlExpressionFactory.Function("trim", [arguments[0]], typeof(string), arguments[0].TypeMapping),
                        _sqlExpressionFactory.Constant(string.Empty)))
                : null;
        }

        var typeMapping = ExpressionExtensions.InferTypeMapping([instance, .. arguments.Where(a => a.Type == typeof(string))]);
        instance = _sqlExpressionFactory.ApplyTypeMapping(instance, typeMapping);

        var isText = method.HasParameters(typeof(string)) || method.HasParameters(typeof(char));
        return method.Name switch
        {
            nameof(string.ToUpper) or nameof(string.ToUpperInvariant) when method.HasParameters() => Function("upper", typeMapping, instance),
            nameof(string.ToLower) or nameof(string.ToLowerInvariant) when method.HasParameters() => Function("lower", typeMapping, instance),

            // trim(s, chars) removes any of the characters in chars. A single char is the only argument
            // form translated: params arrays and spans are client values without a SQL type.
            nameof(string.Trim) when method.HasParameters() => Function("trim", typeMapping, instance),
            nameof(string.TrimStart) when method.HasParameters() => Function("ltrim", typeMapping, instance),
            nameof(string.TrimEnd) when method.HasParameters() => Function("rtrim", typeMapping, instance),
            nameof(string.Trim) when method.HasParameters(typeof(char)) => Function("trim", typeMapping, instance, Text(arguments[0], typeMapping)),
            nameof(string.TrimStart) when method.HasParameters(typeof(char)) => Function("ltrim", typeMapping, instance, Text(arguments[0], typeMapping)),
            nameof(string.TrimEnd) when method.HasParameters(typeof(char)) => Function("rtrim", typeMapping, instance, Text(arguments[0], typeMapping)),

            nameof(string.Substring) when method.HasParameters(typeof(int)) =>
                Function("substr", typeMapping, instance, OneBased(arguments[0])),
            nameof(string.Substring) when method.HasParameters(typeof(int), typeof(int)) =>
                Function("substr", typeMapping, instance, OneBased(arguments[0]), arguments[1]),

            // strpos is 1-based and returns 0 when not found (and 1 for an empty value), so - 1 gives .NET's result.
            nameof(string.IndexOf) when isText =>
                _sqlExpressionFactory.Subtract(Strpos(instance, Text(arguments[0], typeMapping)), _sqlExpressionFactory.Constant(1)),

            nameof(string.Replace) when method.HasParameters(typeof(string), typeof(string)) || method.HasParameters(typeof(char), typeof(char)) =>
                Function("replace", typeMapping, instance, Text(arguments[0], typeMapping), Text(arguments[1], typeMapping)),

            nameof(string.Contains) when isText =>
                Like(instance, arguments[0], prefix: "%", suffix: "%")
                ?? _sqlExpressionFactory.GreaterThan(Strpos(instance, Text(arguments[0], typeMapping)), _sqlExpressionFactory.Constant(0)),

            nameof(string.StartsWith) when isText =>
                Like(instance, arguments[0], prefix: string.Empty, suffix: "%")
                ?? StartsWith(instance, Text(arguments[0], typeMapping)),

            // Trino has no ends_with: a suffix of s is a prefix of reverse(s).
            nameof(string.EndsWith) when isText =>
                Like(instance, arguments[0], prefix: "%", suffix: string.Empty)
                ?? StartsWith(Function("reverse", typeMapping, instance), Function("reverse", typeMapping, Text(arguments[0], typeMapping))),

            _ => null,
        };
    }

    /// <summary>
    /// A text argument with the string's type mapping. A <see cref="char"/> constant becomes a string
    /// constant; any other <see cref="char"/> keeps its own mapping, whose converter binds it as a string.
    /// </summary>
    private SqlExpression Text(SqlExpression argument, RelationalTypeMapping? typeMapping) => argument switch
    {
        SqlConstantExpression { Value: char c } => _sqlExpressionFactory.Constant(c.ToString(), typeMapping),
        _ when argument.Type == typeof(char) => _sqlExpressionFactory.ApplyDefaultTypeMapping(argument),
        _ => _sqlExpressionFactory.ApplyTypeMapping(argument, typeMapping),
    };

    /// <summary>
    /// <c>instance LIKE prefix + escaped(value) + suffix</c> when the argument is a constant, else <see langword="null"/>.
    /// <c>ESCAPE</c> is generated only when the value contains a character that needed escaping.
    /// </summary>
    private SqlExpression? Like(SqlExpression instance, SqlExpression argument, string prefix, string suffix)
    {
        var value = argument switch
        {
            SqlConstantExpression { Value: string s } => s,
            SqlConstantExpression { Value: char c } => c.ToString(),
            _ => null,
        };

        if (value is null)
        {
            return null;
        }

        var (escaped, needsEscape) = TrinoSqlFunctions.EscapeLikePattern(value);
        return _sqlExpressionFactory.Like(
            instance,
            _sqlExpressionFactory.Constant(prefix + escaped + suffix, instance.TypeMapping),
            needsEscape ? _sqlExpressionFactory.Constant(TrinoSqlFunctions.LikeEscapeChar.ToString(), instance.TypeMapping) : null);
    }

    private SqlExpression Strpos(SqlExpression instance, SqlExpression value) =>
        _sqlExpressionFactory.Function("strpos", [instance, value], typeof(int));

    private SqlExpression StartsWith(SqlExpression instance, SqlExpression value) =>
        _sqlExpressionFactory.Function("starts_with", [instance, value], typeof(bool));

    private SqlExpression OneBased(SqlExpression index) => _sqlExpressionFactory.Add(index, _sqlExpressionFactory.Constant(1));

    private SqlExpression Function(string name, RelationalTypeMapping? typeMapping, params SqlExpression[] arguments) =>
        _sqlExpressionFactory.Function(name, arguments, typeof(string), typeMapping);
}
