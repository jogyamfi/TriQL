using Microsoft.EntityFrameworkCore;
using TriQL.EntityFrameworkCore.Query.Internal;
using TriQL.EntityFrameworkCore.Tests.TestUtilities;

namespace TriQL.EntityFrameworkCore.Tests.Query;

/// <summary>SQL baselines for the Trino-specific parts of query translation (Phase 3).</summary>
public sealed class QuerySqlTests
{
    private static readonly (string Name, string Type)[] IdColumn = [("Id", "integer")];

    [Fact]
    public async Task SkipTake_GeneratesOffsetBeforeLimit_WithParameters()
    {
        using var fake = new FakeTrino();
        await using var db = new QueryBloggingContext(fake.CreateOptions<QueryBloggingContext>());
        fake.EnqueueRows(IdColumn, [3]);
        var skip = 2;
        var take = 1;

        await db.Posts.OrderBy(p => p.Id).Skip(skip).Take(take).Select(p => p.Id).ToListAsync();

        fake.AssertSql(
            """
            SELECT "p"."Id"
            FROM "Posts" AS "p"
            ORDER BY "p"."Id"
            OFFSET @p
            LIMIT @p1
            """);
    }

    [Fact]
    public async Task TakeOnly_GeneratesLimit_ParameterizedEvenForAConstant()
    {
        using var fake = new FakeTrino();
        await using var db = new QueryBloggingContext(fake.CreateOptions<QueryBloggingContext>());
        fake.EnqueueRows(IdColumn, [1]);

        await db.Posts.OrderBy(p => p.Id).Take(5).Select(p => p.Id).ToListAsync();

        fake.AssertSql(
            """
            SELECT "p"."Id"
            FROM "Posts" AS "p"
            ORDER BY "p"."Id"
            LIMIT @p
            """);
    }

    [Fact]
    public async Task StringConcatenation_UsesDoublePipe()
    {
        using var fake = new FakeTrino();
        await using var db = new QueryBloggingContext(fake.CreateOptions<QueryBloggingContext>());
        fake.EnqueueRows([("c", "varchar")], ["x"]);

        await db.Blogs.Select(b => b.Name + "!").ToListAsync();

        fake.AssertSql(
            """
            SELECT "b"."Name" || '!'
            FROM "Blogs" AS "b"
            """);
    }

    [Fact]
    public async Task AverageOfDecimal_WidensTheScale()
    {
        using var fake = new FakeTrino();
        await using var db = new QueryBloggingContext(fake.CreateOptions<QueryBloggingContext>());
        fake.EnqueueRows([("_col0", "decimal(38,10)")], ["5.8237500000"]);

        var average = await db.Posts.AverageAsync(p => p.Score);

        Assert.Equal(5.82375m, average);
        fake.AssertSql(
            """
            SELECT AVG(CAST("p"."Score" AS decimal(38, 10)))
            FROM "Posts" AS "p"
            """);
    }

    [Fact]
    public async Task ContainsOnAParameterList_ExpandsToParameters()
    {
        using var fake = new FakeTrino();
        await using var db = new QueryBloggingContext(fake.CreateOptions<QueryBloggingContext>());
        fake.EnqueueRows(IdColumn, [2]);
        int[] ids = [2, 4, 6];

        await db.Posts.Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToListAsync();

        Assert.Single(fake.Sql);
        Assert.Contains("WHERE \"p\".\"Id\" IN (@ids1, @ids2, @ids3)", fake.Sql[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task CollectionProjectionWithTake_OnANavigation_UsesAWindowedJoinNotLateral()
    {
        using var fake = new FakeTrino();
        await using var db = new QueryBloggingContext(fake.CreateOptions<QueryBloggingContext>());
        fake.EnqueueRows([("Id", "integer"), ("Id0", "integer"), ("BlogId", "integer")], [1, 2, 1]);

        await db.Blogs.Select(b => b.Posts.OrderByDescending(p => p.Views).Take(2).Select(p => p.Id).ToList()).ToListAsync();

        Assert.Single(fake.Sql);
        Assert.DoesNotContain("APPLY", fake.Sql[0], StringComparison.Ordinal);
        Assert.Contains("ROW_NUMBER() OVER(PARTITION BY \"p\".\"BlogId\" ORDER BY \"p\".\"Views\" DESC)", fake.Sql[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task OuterApply_IsGeneratedAsLeftJoinLateralOnTrue()
    {
        using var fake = new FakeTrino();
        await using var db = new QueryBloggingContext(fake.CreateOptions<QueryBloggingContext>());
        fake.EnqueueRows([("Id", "integer")]);

        // A DISTINCT subquery correlated through a non-equality cannot become a join, so EF's tree has an
        // OUTER APPLY. Trino runs this shape (no paging, no outer column in the projection; see QueryShapeTests).
        await db.Blogs.SelectMany(b => db.Posts.Where(p => p.Views > b.Id).Select(p => p.Title).Distinct().DefaultIfEmpty(), (b, t) => new { b.Id, t }).ToListAsync();

        Assert.Single(fake.Sql);
        Assert.DoesNotContain("APPLY", fake.Sql[0], StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN LATERAL (", fake.Sql[0], StringComparison.Ordinal);
        Assert.Contains(" ON TRUE", fake.Sql[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SplitQuery_IsBuffered_SoTheSecondQueryRunsAfterTheFirstReaderCloses()
    {
        using var fake = new FakeTrino();
        await using var db = new QueryBloggingContext(fake.CreateOptions<QueryBloggingContext>());
        fake.EnqueueRows([("Id", "integer"), ("Name", "varchar")], [1, "a"], [2, "b"]);
        fake.EnqueueRows([("Id", "integer"), ("BlogId", "integer"), ("Score", "decimal(10,2)"), ("Title", "varchar"), ("Views", "bigint"), ("Id0", "integer")], [10, 1, "1.00", "t", 5L, 1]);

        var blogs = await db.Blogs.Include(b => b.Posts).AsSplitQuery().OrderBy(b => b.Id).ToListAsync();

        Assert.Equal(2, fake.Sql.Count);
        Assert.Single(blogs[0].Posts);
        Assert.Empty(blogs[1].Posts);
    }

    [Fact]
    public async Task UnsupportedCorrelatedPaging_FailsAtTranslation_WithoutSendingSql()
    {
        using var fake = new FakeTrino();
        await using var db = new QueryBloggingContext(fake.CreateOptions<QueryBloggingContext>());

        var outerInProjection = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.Blogs.Select(b => b.Posts.Max(p => p.Views + b.Id)).ToListAsync());
        var nonEquality = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.Blogs.Select(b => b.Posts.Where(p => p.Views > b.Id).OrderBy(p => p.Views).Select(p => p.Title).FirstOrDefault()).ToListAsync());
        var inPredicate = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.Posts.Where(p => db.Posts.Where(x => x.BlogId == p.BlogId).OrderBy(x => x.Views).Take(1).Select(x => x.Id).Contains(p.Id)).ToListAsync());

        Assert.Equal(TrinoQueryTranslationPostprocessor.CorrelatedProjectionMessage, outerInProjection.Message);
        Assert.Equal(TrinoQueryTranslationPostprocessor.CorrelatedNonEqualityMessage, nonEquality.Message);
        Assert.Equal(TrinoQueryTranslationPostprocessor.CorrelatedLimitInInMessage, inPredicate.Message);
        Assert.Empty(fake.SubmittedBodies);
    }

    [Fact]
    public async Task SupportedCorrelatedLimit_WithEqualityOnly_IsNotRejected()
    {
        using var fake = new FakeTrino();
        await using var db = new QueryBloggingContext(fake.CreateOptions<QueryBloggingContext>());
        fake.EnqueueRows([("c", "varchar")], ["t"]);

        await db.Blogs.Select(b => b.Posts.OrderByDescending(p => p.Views).Select(p => p.Title).FirstOrDefault()).ToListAsync();

        fake.AssertSql(
            """
            SELECT (
                SELECT "p"."Title"
                FROM "Posts" AS "p"
                WHERE "b"."Id" = "p"."BlogId"
                ORDER BY "p"."Views" DESC
                LIMIT 1)
            FROM "Blogs" AS "b"
            """);
    }
}
