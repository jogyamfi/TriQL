using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TriQL.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace TriQL.EntityFrameworkCore.FunctionalTests.Query;

/// <summary>
/// EF3-T8: query shapes that stress Trino's correlated-subquery support — correlation through a
/// non-equality predicate, inside aggregates, and combined with paging. Supported shapes are
/// compared with LINQ-to-Objects; the shapes Trino rejects must fail at translation time with the
/// provider's guidance, before any SQL is sent (TrinoQueryTranslationPostprocessor).
/// </summary>
[Collection(TrinoContainerCollection.Name)]
[Trait("Category", "EfRead")]
public sealed class QueryShapeTests(TrinoContainerFixture fixture, ITestOutputHelper output)
{
    private static IReadOnlyList<Blog> Blogs => BloggingDatabase.Blogs;

    private async Task<BloggingContext> CreateContextAsync() =>
        new(new DbContextOptionsBuilder<BloggingContext>()
            .UseTrino(await BloggingDatabase.GetConnectionStringAsync(fixture))
            .LogTo(output.WriteLine, [RelationalEventId.CommandExecuting])
            .Options);

    [Fact]
    public async Task CorrelatedExists_WithNonEqualityCorrelation()
    {
        await using var db = await CreateContextAsync();

        var actual = await db.Blogs.Where(b => b.Posts.Any(p => p.Views > b.Id * 100)).OrderBy(b => b.Id).Select(b => b.Id).ToListAsync();

        Assert.Equal(Blogs.Where(b => b.Posts.Any(p => p.Views > b.Id * 100)).Select(b => b.Id), actual);
    }

    [Fact]
    public async Task CorrelatedCount_WithNonEqualityCorrelation_InProjection()
    {
        await using var db = await CreateContextAsync();

        var actual = await db.Blogs.OrderBy(b => b.Id).Select(b => b.Posts.Count(p => p.Score > b.Id)).ToListAsync();

        Assert.Equal(Blogs.OrderBy(b => b.Id).Select(b => b.Posts.Count(p => p.Score > b.Id)), actual);
    }

    [Fact]
    public async Task CorrelatedFirstOrDefault_WithNonEqualityCorrelation_IsRejectedWithGuidance()
    {
        await using var db = await CreateContextAsync();

        var query = db.Blogs.OrderBy(b => b.Id)
            .Select(b => b.Posts.Where(p => p.Views >= b.Id).OrderBy(p => p.Views).Select(p => p.Title).FirstOrDefault());

        await AssertRejectedAsync(() => query.ToListAsync(), "plain equality");
    }

    [Fact]
    public async Task CollectionProjection_WithSkip_AndEqualityOnlyCorrelation_Works()
    {
        // EF itself rewrites this to a ROW_NUMBER() OVER (PARTITION BY ...) join, so no correlated OFFSET reaches Trino.
        await using var db = await CreateContextAsync();

        var actual = await db.Blogs.OrderBy(b => b.Id).Select(b => b.Posts.OrderBy(p => p.Id).Skip(1).Take(1).Select(p => p.Id).ToList()).ToListAsync();

        var expected = Blogs.OrderBy(b => b.Id).Select(b => b.Posts.OrderBy(p => p.Id).Skip(1).Take(1).Select(p => p.Id).ToList());
        Assert.Equal(expected.Select(l => string.Join(",", l)), actual.Select(l => string.Join(",", l)));
    }

    [Fact]
    public async Task CollectionProjection_WithSkip_AndNonEqualityCorrelation_IsRejectedWithGuidance()
    {
        await using var db = await CreateContextAsync();

        var query = db.Blogs.OrderBy(b => b.Id).Select(b => b.Posts.Where(p => p.Id > b.Id).OrderBy(p => p.Id).Skip(1).Take(1).Select(p => p.Id).ToList());

        await AssertRejectedAsync(() => query.ToListAsync(), "OFFSET");
    }

    [Fact]
    public async Task ContainsOnACorrelatedSubqueryWithTake_IsRejectedWithGuidance()
    {
        await using var db = await CreateContextAsync();

        var query = db.Posts.Where(p => p.Blog.Posts.OrderByDescending(x => x.Views).Take(1).Select(x => x.Id).Contains(p.Id));

        await AssertRejectedAsync(() => query.ToListAsync(), "inside an IN predicate");
    }

    [Fact]
    public async Task ContainsRewrittenWithAny_TheSuggestedWorkaround()
    {
        await using var db = await CreateContextAsync();

        var actual = await db.Posts
            .Where(p => p.Blog.Posts.OrderByDescending(x => x.Views).Take(1).Any(x => x.Id == p.Id))
            .OrderBy(p => p.Id).Select(p => p.Id).ToListAsync();

        var expected = BloggingDatabase.Posts.Where(p => p.Blog.Posts.OrderByDescending(x => x.Views).Take(1).Any(x => x.Id == p.Id)).OrderBy(p => p.Id).Select(p => p.Id);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task PagedNavigationProjectingAnOuterColumn_IsRejectedWithGuidance()
    {
        // The inner projection uses the outer row, so EF generates LEFT JOIN LATERAL with "b"."Name" inside a
        // LIMIT subquery: Trino rejects it even though the correlation itself is an equality.
        await using var db = await CreateContextAsync();

        var query = db.Blogs.OrderBy(b => b.Id)
            .Select(b => new { b.Id, Top = b.Posts.OrderBy(p => p.Id).Select(p => new { p.Title, b.Name }).Take(1).ToList() });

        await AssertRejectedAsync(() => query.ToListAsync(), "selected values use a column of the outer row");
    }

    [Fact]
    public async Task PagedNavigation_WithTheOuterColumnSelectedOutside_TheSuggestedRewrite()
    {
        await using var db = await CreateContextAsync();

        var actual = await db.Blogs.OrderBy(b => b.Id)
            .Select(b => new { b.Id, b.Name, Top = b.Posts.OrderBy(p => p.Id).Select(p => p.Title).Take(1).ToList() })
            .ToListAsync();

        var expected = Blogs.OrderBy(b => b.Id).Select(b => new { b.Id, b.Name, Top = b.Posts.OrderBy(p => p.Id).Select(p => p.Title).Take(1).ToList() }).ToList();
        Assert.Equal(expected.Select(x => (x.Id, x.Name, string.Join(",", x.Top))), actual.Select(x => (x.Id, x.Name, string.Join(",", x.Top))));
    }

    [Fact]
    public async Task CorrelatedSubquery_WithAnOuterColumnInItsProjection_IsRejectedWithGuidance()
    {
        await using var db = await CreateContextAsync();

        var query = db.Blogs.OrderBy(b => b.Id).Select(b => b.Posts.Max(p => p.Views + b.Id));

        await AssertRejectedAsync(() => query.ToListAsync(), "selected values use a column of the outer row");
    }

    [Fact]
    public async Task LeftJoinLateral_ForADistinctSubqueryWithNonEqualityCorrelation()
    {
        // EF generates LEFT JOIN LATERAL (SELECT DISTINCT ... WHERE p.Views > b.Id) ON TRUE: no paging and no
        // outer column in the projection, which Trino supports.
        await using var db = await CreateContextAsync();

        var actual = await db.Blogs
            .SelectMany(b => db.Posts.Where(p => p.Views > b.Id * 1000).Select(p => p.Published).Distinct().DefaultIfEmpty(), (b, published) => new { b.Id, published })
            .OrderBy(x => x.Id).ThenBy(x => x.published)
            .ToListAsync();

        var expected = Blogs
            .SelectMany(b => BloggingDatabase.Posts.Where(p => p.Views > b.Id * 1000).Select(p => p.Published).Distinct().DefaultIfEmpty(), (b, published) => new { b.Id, published })
            .OrderBy(x => x.Id).ThenBy(x => x.published);
        Assert.Equal(expected, actual);
    }

    private async Task AssertRejectedAsync(Func<Task> query, string messageFragment)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(query);

        Assert.StartsWith("Trino cannot run this query", ex.Message, StringComparison.Ordinal);
        Assert.Contains(messageFragment, ex.Message, StringComparison.Ordinal);
        output.WriteLine(ex.Message);
    }

    [Fact]
    public async Task GroupBy_ThenCollectionOfElements()
    {
        await using var db = await CreateContextAsync();

        var actual = await db.Posts.GroupBy(p => p.BlogId)
            .Select(g => new { g.Key, Titles = g.OrderBy(p => p.Id).Select(p => p.Title).ToList() })
            .OrderBy(x => x.Key).ToListAsync();

        var expected = BloggingDatabase.Posts.GroupBy(p => p.BlogId).Select(g => new { g.Key, Titles = g.OrderBy(p => p.Id).Select(p => p.Title).ToList() }).OrderBy(x => x.Key).ToList();
        Assert.Equal(expected.Select(x => (x.Key, string.Join("|", x.Titles))), actual.Select(x => (x.Key, string.Join("|", x.Titles))));
    }
}
