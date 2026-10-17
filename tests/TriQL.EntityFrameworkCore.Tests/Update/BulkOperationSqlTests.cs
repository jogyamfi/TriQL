using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TriQL.EntityFrameworkCore.Tests.Query;
using TriQL.EntityFrameworkCore.Tests.TestUtilities;
using TriQL.EntityFrameworkCore.Update.Internal;

namespace TriQL.EntityFrameworkCore.Tests.Update;

/// <summary>
/// Phase 7 SQL baselines: <c>ExecuteDelete</c>/<c>ExecuteUpdate</c> without the table alias Trino's
/// <c>DELETE</c>/<c>UPDATE</c> reject, <c>MERGE</c> for updates that join other tables, and multi-row inserts.
/// </summary>
public sealed class BulkOperationSqlTests
{
    // ---- ExecuteDelete --------------------------------------------------------------------

    [Fact]
    public Task ExecuteDelete_Simple_HasNoAlias() =>
        AssertBlogging(
            db => db.Posts.Where(p => p.Views > 5).ExecuteDeleteAsync(),
            """
            DELETE FROM "Posts"
            WHERE "Posts"."Views" > BIGINT '5'
            """);

    [Fact]
    public Task ExecuteDelete_FilterOnARelatedTable_IsExists() =>
        AssertBlogging(
            db => db.Posts.Where(p => db.Blogs.Any(b => b.Id == p.BlogId && b.Name == "x")).ExecuteDeleteAsync(),
            """
            DELETE FROM "Posts"
            WHERE EXISTS (
                SELECT 1
                FROM "Blogs" AS "b"
                WHERE "b"."Id" = "Posts"."BlogId" AND "b"."Name" = 'x')
            """);

    [Fact]
    public Task ExecuteDelete_OverAJoinOrTake_IsKeyIn() =>
        AssertBlogging(
            db => db.Posts.OrderBy(p => p.Id).Take(3).ExecuteDeleteAsync(),
            """
            DELETE FROM "Posts"
            WHERE "Posts"."Id" IN (
                SELECT "p0"."Id"
                FROM "Posts" AS "p0"
                ORDER BY "p0"."Id"
                LIMIT @p
            )
            """);

    // ---- ExecuteUpdate --------------------------------------------------------------------

    [Fact]
    public Task ExecuteUpdate_SingleTable_IsUpdateWithoutAlias() =>
        AssertBlogging(
            db => db.Posts.Where(p => p.Views > 5).ExecuteUpdateAsync(s => s.SetProperty(p => p.Views, p => p.Views + 1).SetProperty(p => p.Title, "t")),
            """
            UPDATE "Posts"
            SET "Views" = "Posts"."Views" + BIGINT '1', "Title" = @p
            WHERE "Posts"."Views" > BIGINT '5'
            """);

    [Fact]
    public Task ExecuteUpdate_WithAValueFromAJoinedTable_IsMerge() =>
        AssertBlogging(
            db => db.Posts.Join(db.Blogs, p => p.BlogId, b => b.Id, (p, b) => new { p, b })
                .Where(x => x.b.Name != "skip")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.p.Title, x => x.b.Name + ": " + x.p.Title)),
            """
            MERGE INTO "Posts" AS "p"
            USING (
                SELECT "p"."Id" AS "k0", ("b"."Name" || ': ') || "p"."Title" AS "v0"
                FROM "Posts" AS "p"
                INNER JOIN "Blogs" AS "b" ON "p"."BlogId" = "b"."Id"
                WHERE "b"."Name" <> 'skip'
            ) AS "triql_source"
            ON "p"."Id" = "triql_source"."k0"
            WHEN MATCHED THEN UPDATE SET "Title" = "triql_source"."v0"
            """);

    [Fact]
    public Task ExecuteUpdate_WithTake_IsMerge() =>
        AssertBlogging(
            db => db.Posts.OrderBy(p => p.Id).Take(3).ExecuteUpdateAsync(s => s.SetProperty(p => p.Views, 0L)),
            """
            MERGE INTO "Posts" AS "p0"
            USING (
                SELECT "p0"."Id" AS "k0", @p1 AS "v0"
                FROM "Posts" AS "p0"
                INNER JOIN (
                    SELECT "p"."Id"
                    FROM "Posts" AS "p"
                    ORDER BY "p"."Id"
                    LIMIT @p
                ) AS "p1" ON "p0"."Id" = "p1"."Id"
            ) AS "triql_source"
            ON "p0"."Id" = "triql_source"."k0"
            WHEN MATCHED THEN UPDATE SET "Views" = "triql_source"."v0"
            """);

    [Fact]
    public async Task ExecuteDelete_WhenASubqueryAliasEqualsTheTargetTableName_IsRejected()
    {
        using var fake = new FakeTrino();
        await using var db = new ModelContext(ModelContext.CreateOptions(fake), b =>
        {
            b.Entity<QBlog>().ToTable("Blogs").HasMany(x => x.Posts).WithOne().HasForeignKey(p => p.BlogId);
            b.Entity<QPost>().ToTable("p0");
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.Set<QPost>().OrderBy(p => p.Id).Take(3).ExecuteDeleteAsync());

        Assert.Contains("'p0' as an alias", ex.Message, StringComparison.Ordinal);
        Assert.Empty(fake.SubmittedBodies);
    }

    // ---- multi-row inserts ----------------------------------------------------------------

    [Fact]
    public async Task ConsecutiveInserts_AreCombinedIntoOneStatement()
    {
        using var fake = new FakeTrino();
        await using var db = new QueryBloggingContext(fake.CreateOptions<QueryBloggingContext>());
        db.Blogs.AddRange(new QBlog { Id = 1, Name = "a" }, new QBlog { Id = 2, Name = "b" }, new QBlog { Id = 3, Name = "c" });
        fake.EnqueueUpdate("INSERT", 3);

        Assert.Equal(3, await db.SaveChangesAsync());

        fake.AssertSql(
            """
            INSERT INTO "Blogs" ("Id", "Name")
            VALUES (@p0, @p1),
            (@p2, @p3),
            (@p4, @p5)
            """);
        Assert.Empty(fake.ProviderEvents);
    }

    [Fact]
    public async Task CombinedInsert_ReportingFewerRows_IsAConcurrencyConflict()
    {
        using var fake = new FakeTrino();
        await using var db = new QueryBloggingContext(fake.CreateOptions<QueryBloggingContext>());
        db.Blogs.AddRange(new QBlog { Id = 1, Name = "a" }, new QBlog { Id = 2, Name = "b" });
        fake.EnqueueUpdate("INSERT", 1);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task MaxBatchSize_LimitsTheRowsPerInsert()
    {
        using var fake = new FakeTrino();
        await using var db = new QueryBloggingContext(fake.CreateOptions<QueryBloggingContext>(o => o.UseTrino(fake.Connection, t => t.MaxBatchSize(2))));
        db.Blogs.AddRange(Enumerable.Range(1, 5).Select(i => new QBlog { Id = i, Name = "n" }));
        fake.EnqueueUpdate("INSERT", 2);
        fake.EnqueueUpdate("INSERT", 2);
        fake.EnqueueUpdate("INSERT", 1);

        Assert.Equal(5, await db.SaveChangesAsync());

        Assert.Equal(3, fake.Sql.Count);
        Assert.Equal(TrinoEventId.NonAtomicSaveChanges, Assert.Single(fake.ProviderEvents).Id);
    }

    [Fact]
    public async Task ParameterCap_LimitsTheRowsPerInsert()
    {
        // 2 parameters per row: 1000 rows fill the 2000-parameter budget.
        using var fake = new FakeTrino();
        await using var db = new QueryBloggingContext(fake.CreateOptions<QueryBloggingContext>(o => o.UseTrino(fake.Connection, t => t.MaxBatchSize(100_000))));
        db.Blogs.AddRange(Enumerable.Range(1, 2500).Select(i => new QBlog { Id = i, Name = "n" }));
        fake.EnqueueUpdate("INSERT", 1000);
        fake.EnqueueUpdate("INSERT", 1000);
        fake.EnqueueUpdate("INSERT", 500);

        Assert.Equal(2500, await db.SaveChangesAsync());

        Assert.Equal(3, fake.Sql.Count);
        Assert.Equal(TrinoModificationCommandBatch.MaxParameters, fake.Sql[0].Split('@').Length - 1);
    }

    [Fact]
    public async Task DefaultMaxBatchSize_Is1000Rows()
    {
        using var fake = new FakeTrino();
        await using var db = new ModelContext(ModelContext.CreateOptions(fake), b => b.Entity<Tag>());
        db.AddRange(Enumerable.Range(1, 1001).Select(i => new Tag { Id = i }));
        fake.EnqueueUpdate("INSERT", 1000);
        fake.EnqueueUpdate("INSERT", 1);

        await db.SaveChangesAsync();

        Assert.Equal(2, fake.Sql.Count);
        Assert.Equal(TrinoModificationCommandBatchFactory.DefaultMaxBatchSize, fake.Sql[0].Split('@').Length - 1);
    }

    // ---- helpers --------------------------------------------------------------------------

    private static async Task AssertBlogging(Func<QueryBloggingContext, Task<int>> operation, string expectedSql)
    {
        using var fake = new FakeTrino();
        await using var db = new QueryBloggingContext(fake.CreateOptions<QueryBloggingContext>());
        fake.EnqueueUpdate("X", 1);

        await operation(db);

        fake.AssertSql(expectedSql);
    }

    private sealed class Tag
    {
        public int Id { get; set; }
    }
}
