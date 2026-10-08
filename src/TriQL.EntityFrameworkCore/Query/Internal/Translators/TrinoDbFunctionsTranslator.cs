using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace TriQL.EntityFrameworkCore.Query.Internal.Translators;

/// <summary>
/// Translates the scalar functions of <see cref="TrinoDbFunctionsExtensions"/>: <c>ILike</c>,
/// <c>DateDiff*</c> and <c>JsonExtractScalar</c>. (<c>ApproxDistinct</c> is an aggregate, translated by
/// <see cref="TrinoAggregateMethodTranslator"/>.)
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoDbFunctionsTranslator : IMethodCallTranslator
{
    private const string DateDiffPrefix = "DateDiff";

    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private readonly IRelationalTypeMappingSource _typeMappingSource;

    /// <summary>Initializes a new instance.</summary>
    public TrinoDbFunctionsTranslator(ISqlExpressionFactory sqlExpressionFactory, IRelationalTypeMappingSource typeMappingSource)
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

        if (method.DeclaringType != typeof(TrinoDbFunctionsExtensions))
        {
            return null;
        }

        // arguments[0] is the EF.Functions instance.
        switch (method.Name)
        {
            case nameof(TrinoDbFunctionsExtensions.ILike):
            {
                var typeMapping = ExpressionExtensions.InferTypeMapping(arguments[1], arguments[2]);
                SqlExpression Lower(SqlExpression value) =>
                    _sqlExpressionFactory.Function("lower", [_sqlExpressionFactory.ApplyTypeMapping(value, typeMapping)], typeof(string), typeMapping);

                return _sqlExpressionFactory.Like(
                    Lower(arguments[1]),
                    Lower(arguments[2]),
                    arguments.Count == 4 ? arguments[3] : null);
            }

            case nameof(TrinoDbFunctionsExtensions.JsonExtractScalar):
                return _sqlExpressionFactory.Function("json_extract_scalar", [arguments[1], arguments[2]], typeof(string));

            case var name when name.StartsWith(DateDiffPrefix, StringComparison.Ordinal):
            {
                var unit = name[DateDiffPrefix.Length..].ToLowerInvariant();
                var typeMapping = ExpressionExtensions.InferTypeMapping(arguments[1], arguments[2]);
                return _sqlExpressionFactory.Function(
                    "date_diff",
                    [
                        _sqlExpressionFactory.Constant(unit, _typeMappingSource.FindMapping(typeof(string))),
                        _sqlExpressionFactory.ApplyTypeMapping(arguments[1], typeMapping),
                        _sqlExpressionFactory.ApplyTypeMapping(arguments[2], typeMapping),
                    ],
                    nullable: true,
                    argumentsPropagateNullability: [false, true, true],
                    typeof(long));
            }

            default:
                return null;
        }
    }
}
