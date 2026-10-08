using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace TriQL.EntityFrameworkCore.Query.Internal;

/// <summary>
/// Translates scalar LINQ expressions to SQL. Adds what EF's relational visitor leaves to providers:
/// <c>byte[].Length</c> becomes <c>length(b)</c>.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoSqlTranslatingExpressionVisitor : RelationalSqlTranslatingExpressionVisitor
{
    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    /// <summary>Initializes a new instance.</summary>
    public TrinoSqlTranslatingExpressionVisitor(
        RelationalSqlTranslatingExpressionVisitorDependencies dependencies,
        QueryCompilationContext queryCompilationContext,
        QueryableMethodTranslatingExpressionVisitor queryableMethodTranslatingExpressionVisitor)
        : base(dependencies, queryCompilationContext, queryableMethodTranslatingExpressionVisitor)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        _sqlExpressionFactory = dependencies.SqlExpressionFactory;
    }

    /// <inheritdoc />
    protected override Expression VisitUnary(UnaryExpression unaryExpression)
    {
        ArgumentNullException.ThrowIfNull(unaryExpression);

        if (unaryExpression.NodeType == ExpressionType.ArrayLength
            && unaryExpression.Operand.Type == typeof(byte[])
            && Visit(unaryExpression.Operand) is SqlExpression bytes)
        {
            return _sqlExpressionFactory.Function(
                "length",
                [bytes],
                nullable: true,
                argumentsPropagateNullability: [true],
                typeof(int));
        }

        return base.VisitUnary(unaryExpression);
    }

    /// <summary>
    /// <c>greatest(…)</c>, for <see cref="Math.Max(int, int)"/> and <c>Max</c> over an inline collection.
    /// Not generated when a value's type is nullable: Trino's <c>greatest</c> is <c>NULL</c> when any argument
    /// is, while LINQ's <c>Max</c> ignores <see langword="null"/> values.
    /// </summary>
    public override SqlExpression? GenerateGreatest(IReadOnlyList<SqlExpression> expressions, Type resultType) =>
        GenerateGreatestOrLeast("greatest", expressions, resultType);

    /// <summary>
    /// <c>least(…)</c>, for <see cref="Math.Min(int, int)"/> and <c>Min</c> over an inline collection.
    /// Not generated when a value's type is nullable (see <see cref="GenerateGreatest"/>).
    /// </summary>
    public override SqlExpression? GenerateLeast(IReadOnlyList<SqlExpression> expressions, Type resultType) =>
        GenerateGreatestOrLeast("least", expressions, resultType);

    private SqlExpression? GenerateGreatestOrLeast(string name, IReadOnlyList<SqlExpression> expressions, Type resultType)
    {
        ArgumentNullException.ThrowIfNull(expressions);

        if (expressions.Any(e => Nullable.GetUnderlyingType(e.Type) is not null))
        {
            return null;
        }

        var typeMapping = ExpressionExtensions.InferTypeMapping(expressions);
        return _sqlExpressionFactory.Function(
            name,
            [.. expressions.Select(e => _sqlExpressionFactory.ApplyTypeMapping(e, typeMapping))],
            nullable: true,
            argumentsPropagateNullability: expressions.Select(_ => true),
            resultType,
            typeMapping);
    }
}
