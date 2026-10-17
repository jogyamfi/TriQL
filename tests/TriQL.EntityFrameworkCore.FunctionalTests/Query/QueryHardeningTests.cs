using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TriQL.IntegrationTests.Fixtures;
using Xunit.Abstractions;

// string.Compare is the LINQ shape users write; it is translated to SQL, never run in .NET.
#pragma warning disable CA1309

namespace TriQL.EntityFrameworkCore.FunctionalTests.Query;

/// <summary>
/// Phase 8 hardening (EF8-T3): query shapes beyond the Phase 3 operator suite, where Trino's semantics
/// could differ from .NET's (null ordering, null in string concatenation, empty and null-containing
/// parameter lists, empty aggregates, …), run live and compared with LINQ-to-Objects.
/// </summary>
[Collection(TrinoContainerCollection.Name)]
[Trait("Category", "EfRead")]
public sealed class QueryHardeningTests(TrinoContainerFixture fixture, ITestOutputHelper output)
{
    private static IReadOnlyList<Blog> Blogs => BloggingDatabase.Blogs;

    private static IEnumerable<Post> Posts => BloggingDatabase.Posts;

    // ---- null semantics -------------------------------------------------------------------

    [Fact]
    public Task OrderBy_NullableColumn_PutsNullsFirst_AsLinqDoes() =>
        AssertSameAsync(db => db.Blogs.OrderBy(b => b.Rating).ThenBy(b => b.Id).Select(b => b.Id), Blogs.OrderBy(b => b.Rating).ThenBy(b => b.Id).Select(b => b.Id));

    [Fact]
    public Task OrderByDescending_NullableColumn_PutsNullsLast_AsLinqDoes() =>
        AssertSameAsync(
            db => db.Blogs.OrderByDescending(b => b.Address.Country).ThenBy(b => b.Id).Select(b => b.Id),
            Blogs.OrderByDescending(b => b.Address.Country, StringComparer.Ordinal).ThenBy(b => b.Id).Select(b => b.Id));

    [Fact]
    public Task StringConcatenation_WithANullOperand_TreatsNullAsEmpty() =>
        AssertSameAsync(
            db => db.Posts.OrderBy(p => p.Id).Select(p => p.Title + "/" + p.Content),
            Posts.OrderBy(p => p.Id).Select(p => p.Title + "/" + p.Content));

    [Fact]
    public Task NullableBool_AndCoalesce() =>
        AssertSameAsync(
            db => db.Blogs.OrderBy(b => b.Id).Select(b => new { b.Id, Rated = (bool?)(b.Rating > 3), Rating = b.Rating ?? -1, Country = b.Address.Country ?? "??" }),
            Blogs.OrderBy(b => b.Id).Select(b => new { b.Id, Rated = (bool?)(b.Rating > 3), Rating = b.Rating ?? -1, Country = b.Address.Country ?? "??" }));

    [Fact]
    public async Task Contains_OnAListWithANull_MatchesNullRows()
    {
        int?[] ratings = [null, 5];
        await AssertSameAsync(
            db => db.Blogs.Where(b => ratings.Contains(b.Rating)).OrderBy(b => b.Id).Select(b => b.Id),
            Blogs.Where(b => ratings.Contains(b.Rating)).OrderBy(b => b.Id).Select(b => b.Id));
    }

    [Fact]
    public async Task Contains_OnAnEmptyList_MatchesNothing_AndNotContainsMatchesEverything()
    {
        int[] none = [];
        await AssertSameAsync(db => db.Posts.Where(p => none.Contains(p.Id)).Select(p => p.Id), Enumerable.Empty<int>());
        await AssertSameAsync(db => db.Posts.Where(p => !none.Contains(p.Id)).OrderBy(p => p.Id).Select(p => p.Id), Posts.OrderBy(p => p.Id).Select(p => p.Id));
    }

    [Fact]
    public async Task Contains_OnALargeList()
    {
        var ids = Enumerable.Range(-2_000, 4_000).ToList();
        await AssertSameAsync(
            db => db.Posts.Where(p => ids.Contains(p.Id)).OrderBy(p => p.Id).Select(p => p.Id),
            Posts.Where(p => ids.Contains(p.Id)).OrderBy(p => p.Id).Select(p => p.Id));
    }

    [Fact]
    public async Task StringList_Contains_AndStringComparison()
    {
        string[] names = ["Alpha", "Delta's", "missing"];
        await AssertSameAsync(
            db => db.Blogs.Where(b => names.Contains(b.Name) || string.Compare(b.Name, "Beta") > 0).OrderBy(b => b.Id).Select(b => b.Id),
            Blogs.Where(b => names.Contains(b.Name) || string.CompareOrdinal(b.Name, "Beta") > 0).OrderBy(b => b.Id).Select(b => b.Id));
    }

    // ---- aggregates -----------------------------------------------------------------------

    [Fact]
    public async Task Aggregates_OverAnEmptySet()
    {
        await using var db = await CreateContextAsync();
        var empty = db.Posts.Where(p => p.Id < 0);

        Assert.Equal(0, await empty.CountAsync());
        Assert.Equal(0L, await empty.SumAsync(p => p.Views));
        Assert.Equal(0m, await empty.SumAsync(p => p.Score));
        Assert.Null(await empty.MaxAsync(p => (long?)p.Views));
        Assert.Null(await empty.AverageAsync(p => (decimal?)p.Score));
        Assert.Null(await empty.Select(p => p.Title).FirstOrDefaultAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => empty.MaxAsync(p => p.Views));
    }

    [Fact]
    public Task GroupBy_CompositeKey_WithHaving() =>
        AssertSameAsync(
            db => db.Posts.GroupBy(p => new { p.BlogId, p.Published })
                .Where(g => g.Count() > 1)
                .Select(g => new { g.Key.BlogId, g.Key.Published, Count = g.Count(), Views = g.Sum(p => p.Views) })
                .OrderBy(x => x.BlogId).ThenBy(x => x.Published),
            Posts.GroupBy(p => new { p.BlogId, p.Published })
                .Where(g => g.Count() > 1)
                .Select(g => new { g.Key.BlogId, g.Key.Published, Count = g.Count(), Views = g.Sum(p => p.Views) })
                .OrderBy(x => x.BlogId).ThenBy(x => x.Published));

    [Fact]
    public Task DistinctCount_AndAverageOfInt() =>
        AssertSameAsync(
            db => db.Blogs.Select(b => new
            {
                b.Id,
                Distinct = b.Posts.Select(p => p.Published).Distinct().Count(),
                Average = b.Posts.Select(p => (double?)p.Id).Average(),
            }).OrderBy(x => x.Id),
            Blogs.Select(b => new
            {
                b.Id,
                Distinct = b.Posts.Select(p => p.Published).Distinct().Count(),
                Average = b.Posts.Select(p => (double?)p.Id).Average(),
            }).OrderBy(x => x.Id));

    // ---- paging and element operators -----------------------------------------------------

    [Fact]
    public async Task Last_SkipWithoutTake_AndTakeZero()
    {
        await using var db = await CreateContextAsync();

        Assert.Equal(Posts.OrderBy(p => p.Views).Last().Id, (await db.Posts.OrderBy(p => p.Views).LastAsync()).Id);
        Assert.Equal(Posts.OrderBy(p => p.Id).Skip(6).Select(p => p.Id), await db.Posts.OrderBy(p => p.Id).Skip(6).Select(p => p.Id).ToListAsync());
        Assert.Empty(await db.Posts.Take(0).ToListAsync());
    }

    [Fact]
    public Task ConditionalOrdering() =>
        AssertSameAsync(
            db => db.Posts.OrderBy(p => p.Published ? 0 : 1).ThenByDescending(p => p.Score).ThenBy(p => p.Id).Select(p => p.Id),
            Posts.OrderBy(p => p.Published ? 0 : 1).ThenByDescending(p => p.Score).ThenBy(p => p.Id).Select(p => p.Id));

    // ---- helpers --------------------------------------------------------------------------

    private async Task<BloggingContext> CreateContextAsync() =>
        new(new DbContextOptionsBuilder<BloggingContext>()
            .UseTrino(await BloggingDatabase.GetConnectionStringAsync(fixture))
            .LogTo(output.WriteLine, [RelationalEventId.CommandExecuting])
            .Options);

    private async Task AssertSameAsync<T>(Func<BloggingContext, IQueryable<T>> query, IEnumerable<T> expected)
    {
        await using var db = await CreateContextAsync();
        Assert.Equal(expected, await query(db).ToListAsync());
    }
}
