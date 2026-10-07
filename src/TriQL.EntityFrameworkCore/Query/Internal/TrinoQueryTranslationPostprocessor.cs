using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace TriQL.EntityFrameworkCore.Query.Internal;

/// <summary>
/// Rejects, at translation time and with guidance, the correlated-subquery shapes Trino cannot run
/// (it fails them with "Given correlated subquery is not supported"). Measured on Trino 466 and 483,
/// a subquery that references the outer query (the "outer row") fails when:
/// <list type="bullet">
/// <item><description>it has an <c>OFFSET</c> (from <c>Skip</c>), however it is correlated;</description></item>
/// <item><description>its projection uses an outer column (e.g. <c>Select(p =&gt; p.Title + b.Name)</c>), whatever its kind;</description></item>
/// <item><description>it has a <c>LIMIT</c> (from <c>Take</c>/<c>First</c>) and is the right-hand side of <c>IN</c> (<c>Contains</c>);</description></item>
/// <item><description>it has a <c>LIMIT</c> or <c>GROUP BY</c> and uses the outer row other than in a plain
/// equality with it, e.g. <c>p.Views &gt; b.Id</c>.</description></item>
/// </list>
/// Everything else measured runs: equality correlation with any kind of subquery, and non-equality
/// correlation in plain, <c>DISTINCT</c> and aggregate subqueries (<c>Any</c>, <c>Count</c>, <c>Max</c>, …).
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoQueryTranslationPostprocessor : RelationalQueryTranslationPostprocessor
{
    internal const string CorrelatedOffsetMessage =
        "Trino cannot run this query: it needs a correlated subquery with OFFSET (from Skip on a navigation or "
        + "subquery that also filters on the outer row), which Trino does not support. EF avoids the correlated "
        + "subquery when the paged subquery's only link to the outer row is an equality, such as the navigation itself: "
        + "move other conditions on the outer row out of it, or load the related rows with a separate query.";

    internal const string CorrelatedProjectionMessage =
        "Trino cannot run this query: it needs a correlated subquery whose selected values use a column of the outer "
        + "row (for example 'b.Posts.Select(p => p.Title + b.Name)'), which Trino does not support. Select the outer "
        + "row's columns in the outer query instead of inside the subquery.";

    internal const string CorrelatedLimitInInMessage =
        "Trino cannot run this query: it needs a correlated subquery with LIMIT (from Take/First) inside an IN "
        + "predicate (Contains), which Trino does not support. Rewrite the condition with Any(x => x.Key == outer.Key) "
        + "instead of Select(x => x.Key).Contains(outer.Key).";

    internal const string CorrelatedNonEqualityMessage =
        "Trino cannot run this query: it needs a correlated subquery with LIMIT (from Take/First/FirstOrDefault) or "
        + "GROUP BY that uses the outer row other than in a plain equality with it (for example 'p.Views > b.Id'), "
        + "which Trino does not support. Keep only equality conditions on the outer row inside such a subquery "
        + "(a navigation is one), or load the related rows with a separate query and filter them in memory.";

    /// <summary>Initializes a new instance.</summary>
    public TrinoQueryTranslationPostprocessor(
        QueryTranslationPostprocessorDependencies dependencies,
        RelationalQueryTranslationPostprocessorDependencies relationalDependencies,
        RelationalQueryCompilationContext queryCompilationContext)
        : base(dependencies, relationalDependencies, queryCompilationContext)
    {
    }

    /// <inheritdoc />
    public override Expression Process(Expression query)
    {
        var processed = base.Process(query);
        new CorrelatedSubqueryValidator().Visit(processed);
        return processed;
    }

    private sealed class CorrelatedSubqueryValidator : ExpressionVisitor
    {
        protected override Expression VisitExtension(Expression node)
        {
            switch (node)
            {
                // EF forbids ShapedQueryExpression.VisitChildren; its parts are visited explicitly. The
                // shaper matters too: split queries keep their additional SELECTs there.
                case ShapedQueryExpression shaped:
                    Visit(shaped.QueryExpression);
                    Visit(shaped.ShaperExpression);
                    return node;

                case InExpression { Subquery: { } subquery } inExpression:
                    Visit(inExpression.Item);
                    Validate(subquery, isInSubquery: true);
                    VisitChildrenOf(subquery);
                    return node;

                case SelectExpression select:
                    Validate(select, isInSubquery: false);
                    VisitChildrenOf(select);
                    return node;

                default:
                    return base.VisitExtension(node);
            }
        }

        private void VisitChildrenOf(SelectExpression select)
        {
            foreach (var table in select.Tables)
            {
                Visit(table);
            }

            Visit(select.Predicate);
            Visit(select.Having);
            foreach (var projection in select.Projection)
            {
                Visit(projection);
            }

            foreach (var ordering in select.Orderings)
            {
                Visit(ordering.Expression);
            }

            foreach (var key in select.GroupBy)
            {
                Visit(key);
            }

            Visit(select.Limit);
            Visit(select.Offset);
        }

        private static void Validate(SelectExpression select, bool isInSubquery)
        {
            var defined = new AliasCollector(collectTables: true);
            defined.Visit(select);
            var outer = new AliasCollector(collectTables: false);
            outer.Visit(select);
            outer.Aliases.ExceptWith(defined.Aliases);
            if (outer.Aliases.Count == 0)
            {
                return;
            }

            if (select.Offset is not null)
            {
                throw new InvalidOperationException(CorrelatedOffsetMessage);
            }

            if (select.Projection.Any(p => References(p, outer.Aliases)))
            {
                throw new InvalidOperationException(CorrelatedProjectionMessage);
            }

            if (select.Limit is null && select.GroupBy.Count == 0)
            {
                return;
            }

            if (select.Limit is not null && isInSubquery)
            {
                throw new InvalidOperationException(CorrelatedLimitInInMessage);
            }

            // With LIMIT or GROUP BY, the outer row may appear only in equality-correlation conjuncts (and
            // EF's null checks around them) — not in another predicate, an ordering, a grouping key, HAVING
            // or a nested subquery.
            var conjuncts = select.Predicate is null ? [] : Conjuncts(select.Predicate).ToList();
            var elsewhere = new List<Expression>();
            elsewhere.AddRange(conjuncts.Where(c => !IsEqualityCorrelationOrUncorrelated(c, outer.Aliases)));
            elsewhere.AddRange(select.Orderings.Select(o => o.Expression));
            elsewhere.AddRange(select.GroupBy);
            elsewhere.AddRange(select.Tables);
            if (select.Having is not null)
            {
                elsewhere.Add(select.Having);
            }

            if (elsewhere.Any(e => References(e, outer.Aliases)))
            {
                throw new InvalidOperationException(CorrelatedNonEqualityMessage);
            }
        }

        private static IEnumerable<SqlExpression> Conjuncts(SqlExpression predicate) =>
            predicate is SqlBinaryExpression { OperatorType: ExpressionType.AndAlso } and
                ? Conjuncts(and.Left).Concat(Conjuncts(and.Right))
                : [predicate];

        /// <summary>
        /// A conjunct is fine if it does not reference the outer query, is <c>outerColumn = innerExpression</c>,
        /// or is a null check on an outer column. EF adds those null checks around equality correlation
        /// (<c>b.Id IS NOT NULL AND b.Id = p.BlogId</c>) and removes them later for non-nullable keys.
        /// </summary>
        private static bool IsEqualityCorrelationOrUncorrelated(SqlExpression conjunct, HashSet<string> outerAliases)
        {
            if (!References(conjunct, outerAliases))
            {
                return true;
            }

            if (conjunct is SqlUnaryExpression { OperatorType: ExpressionType.Equal or ExpressionType.NotEqual, Operand: ColumnExpression }
                or SqlBinaryExpression { OperatorType: ExpressionType.Equal or ExpressionType.NotEqual, Left: ColumnExpression, Right: SqlConstantExpression { Value: null } }
                or SqlBinaryExpression { OperatorType: ExpressionType.Equal or ExpressionType.NotEqual, Left: SqlConstantExpression { Value: null }, Right: ColumnExpression })
            {
                return true;
            }

            return conjunct is SqlBinaryExpression { OperatorType: ExpressionType.Equal } equal
                && ((equal.Left is ColumnExpression left && outerAliases.Contains(left.TableAlias) && !References(equal.Right, outerAliases))
                    || (equal.Right is ColumnExpression right && outerAliases.Contains(right.TableAlias) && !References(equal.Left, outerAliases)));
        }

        private static bool References(Expression expression, HashSet<string> aliases)
        {
            var collector = new AliasCollector(collectTables: false);
            collector.Visit(expression);
            return collector.Aliases.Overlaps(aliases);
        }
    }

    /// <summary>Collects either the table aliases defined in a tree, or the table aliases its columns reference.</summary>
    private sealed class AliasCollector(bool collectTables) : ExpressionVisitor
    {
        public HashSet<string> Aliases { get; } = new(StringComparer.Ordinal);

        protected override Expression VisitExtension(Expression node)
        {
            if (collectTables && node is TableExpressionBase { Alias: { } alias })
            {
                Aliases.Add(alias);
            }
            else if (!collectTables && node is ColumnExpression column)
            {
                Aliases.Add(column.TableAlias);
            }

            return base.VisitExtension(node);
        }
    }
}

/// <summary>Creates <see cref="TrinoQueryTranslationPostprocessor"/> instances.</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoQueryTranslationPostprocessorFactory : IQueryTranslationPostprocessorFactory
{
    /// <summary>Initializes a new instance.</summary>
    public TrinoQueryTranslationPostprocessorFactory(
        QueryTranslationPostprocessorDependencies dependencies,
        RelationalQueryTranslationPostprocessorDependencies relationalDependencies)
    {
        Dependencies = dependencies;
        RelationalDependencies = relationalDependencies;
    }

    /// <summary>Dependencies for this service.</summary>
    protected virtual QueryTranslationPostprocessorDependencies Dependencies { get; }

    /// <summary>Relational provider-specific dependencies for this service.</summary>
    protected virtual RelationalQueryTranslationPostprocessorDependencies RelationalDependencies { get; }

    /// <inheritdoc />
    public virtual QueryTranslationPostprocessor Create(QueryCompilationContext queryCompilationContext) =>
        new TrinoQueryTranslationPostprocessor(Dependencies, RelationalDependencies, (RelationalQueryCompilationContext)queryCompilationContext);
}
