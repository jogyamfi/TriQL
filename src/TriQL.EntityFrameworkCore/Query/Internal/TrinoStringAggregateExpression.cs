using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace TriQL.EntityFrameworkCore.Query.Internal;

/// <summary>
/// Concatenation of a group's strings: <c>array_join(array_agg(value ORDER BY …), separator)</c>.
/// Trino has no <c>string_agg</c>/<c>listagg</c> that accepts every connector's types and an ordering in
/// all supported versions, so the values are collected into an array and joined. <c>array_join</c> skips
/// <c>NULL</c> elements, and <c>array_agg</c> over no rows is <c>NULL</c>.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoStringAggregateExpression : SqlExpression
{
    /// <summary>Initializes a new instance.</summary>
    /// <param name="value">The value aggregated, possibly a <see cref="DistinctExpression"/>.</param>
    /// <param name="separator">The text placed between values.</param>
    /// <param name="orderings">The order of the values in the result; empty for an unspecified order.</param>
    /// <param name="typeMapping">The result's type mapping.</param>
    public TrinoStringAggregateExpression(
        SqlExpression value,
        SqlExpression separator,
        IReadOnlyList<OrderingExpression> orderings,
        RelationalTypeMapping? typeMapping)
        : base(typeof(string), typeMapping)
    {
        Value = value;
        Separator = separator;
        Orderings = orderings;
    }

    /// <summary>The value aggregated, possibly a <see cref="DistinctExpression"/>.</summary>
    public virtual SqlExpression Value { get; }

    /// <summary>The text placed between values.</summary>
    public virtual SqlExpression Separator { get; }

    /// <summary>The order of the values in the result.</summary>
    public virtual IReadOnlyList<OrderingExpression> Orderings { get; }

    /// <summary>Returns this expression, or a copy with the given children if any differ.</summary>
    public virtual TrinoStringAggregateExpression Update(SqlExpression value, SqlExpression separator, IReadOnlyList<OrderingExpression> orderings) =>
        value == Value && separator == Separator && orderings.SequenceEqual(Orderings)
            ? this
            : new TrinoStringAggregateExpression(value, separator, orderings, TypeMapping);

    /// <inheritdoc />
    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return Update(
            (SqlExpression)visitor.Visit(Value),
            (SqlExpression)visitor.Visit(Separator),
            [.. Orderings.Select(o => (OrderingExpression)visitor.Visit(o))]);
    }

    /// <summary>Not supported: precompiled (NativeAOT) queries are not supported by the Trino provider.</summary>
    public override Expression Quote() =>
        throw new NotSupportedException("Precompiled queries are not supported by the Trino provider.");

    /// <inheritdoc />
    protected override void Print(ExpressionPrinter expressionPrinter)
    {
        ArgumentNullException.ThrowIfNull(expressionPrinter);

        expressionPrinter.Append("array_join(array_agg(");
        expressionPrinter.Visit(Value);
        if (Orderings.Count > 0)
        {
            expressionPrinter.Append(" ORDER BY ");
            expressionPrinter.VisitCollection(Orderings);
        }

        expressionPrinter.Append("), ");
        expressionPrinter.Visit(Separator);
        expressionPrinter.Append(")");
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is TrinoStringAggregateExpression other
        && base.Equals(other)
        && Value.Equals(other.Value)
        && Separator.Equals(other.Separator)
        && Orderings.SequenceEqual(other.Orderings);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(base.GetHashCode());
        hash.Add(Value);
        hash.Add(Separator);
        foreach (var ordering in Orderings)
        {
            hash.Add(ordering);
        }

        return hash.ToHashCode();
    }
}
