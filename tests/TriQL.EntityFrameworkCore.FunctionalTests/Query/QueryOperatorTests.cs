using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TriQL.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace TriQL.EntityFrameworkCore.FunctionalTests.Query;

/// <summary>
/// Phase 3 live suite: the core LINQ operators, run against the seeded Blogging schema and compared
/// with LINQ-to-Objects over the same data (<see cref="BloggingDatabase"/>). The generated SQL is
/// written to the test output, so a server-side failure shows the statement Trino rejected.
/// </summary>
[Collection(TrinoContainerCollection.Name)]
[Trait("Category", "EfRead")]
public sealed class QueryOperatorTests(TrinoContainerFixture fixture, ITestOutputHelper output)
{
    private static IReadOnlyList<Blog> Blogs => BloggingDatabase.Blogs;

    private static IEnumerable<Post> Posts => BloggingDatabase.Posts;

    private async Task<BloggingContext> CreateContextAsync() =>
        new(new DbContextOptionsBuilder<BloggingContext>()
            .UseTrino(await BloggingDatabase.GetConnectionStringAsync(fixture))
            .LogTo(output.WriteLine, [RelationalEventId.CommandExecuting])
            .Options);

    // ---- Filtering, ordering, projection --------------------------------------------------

    [Fact]
    public async Task Where_OrderBy_Select()
    {
        await using var db = await CreateContextAsync();

        var actual = await db.Posts.Where(p => p.Published && p.Views > 100).OrderBy(p => p.Title).Select(p => p.Title).ToListAsync();

        Assert.Equal(Posts.Where(p => p.Published && p.Views > 100).OrderBy(p => p.Title, StringComparer.Ordinal).Select(p => p.Title), actual);
    }

    [Fact]
    public async Task OrderByDescending_ThenBy_WithParameter()
    {
        await using var db = await CreateContextAsync();
        var minScore = 5m;

        var actual = await db.Posts.Where(p => p.Score >= minScore).OrderByDescending(p => p.BlogId).ThenBy(p => p.Id).Select(p => p.Id).ToListAsync();

        Assert.Equal(Posts.Where(p => p.Score >= minScore).OrderByDescending(p => p.BlogId).ThenBy(p => p.Id).Select(p => p.Id), actual);
    }

    [Fact]
    public async Task Projection_AnonymousType_WithStringConcatenation_AndConditional()
    {
        await using var db = await CreateContextAsync();

        var actual = await db.Blogs.OrderBy(b => b.Id)
            .Select(b => new { b.Id, Label = b.Name + " (" + b.Address.City + ")", Rated = b.Rating != null ? "rated" : "unrated", Rating = b.Rating ?? 0 })
            .ToListAsync();

        Assert.Equal(
            Blogs.OrderBy(b => b.Id).Select(b => new { b.Id, Label = b.Name + " (" + b.Address.City + ")", Rated = b.Rating != null ? "rated" : "unrated", Rating = b.Rating ?? 0 }),
            actual);
    }

    [Fact]
    public async Task NullSemantics_ComparingNullableColumnWithNullParameter()
    {
        await using var db = await CreateContextAsync();
        int? rating = null;

        var actual = await db.Blogs.Where(b => b.Rating == rating).OrderBy(b => b.Id).Select(b => b.Id).ToListAsync();
        var notEqual = await db.Blogs.Where(b => b.Rating != rating).OrderBy(b => b.Id).Select(b => b.Id).ToListAsync();

        Assert.Equal(Blogs.Where(b => b.Rating == rating).Select(b => b.Id), actual);
        Assert.Equal(Blogs.Where(b => b.Rating != rating).Select(b => b.Id), notEqual);
    }

    [Fact]
    public async Task BooleanColumn_AndNegation_AndDateComparison()
    {
        await using var db = await CreateContextAsync();
        var since = new DateTime(2025, 1, 1);

        var actual = await db.Blogs.Where(b => !b.IsActive || b.Created < since).OrderBy(b => b.Id).Select(b => b.Id).ToListAsync();

        Assert.Equal(Blogs.Where(b => !b.IsActive || b.Created < since).Select(b => b.Id), actual);
    }

    // ---- Paging ----------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 3)]
    [InlineData(2, 3)]
    [InlineData(7, 5)]
    public async Task SkipTake_WithParameters(int skip, int take)
    {
        await using var db = await CreateContextAsync();

        var actual = await db.Posts.OrderBy(p => p.Id).Skip(skip).Take(take).Select(p => p.Id).ToListAsync();

        Assert.Equal(Posts.OrderBy(p => p.Id).Skip(skip).Take(take).Select(p => p.Id), actual);
    }

    [Fact]
    public async Task SkipOnly_AndTakeOnly()
    {
        await using var db = await CreateContextAsync();

        Assert.Equal(Posts.OrderBy(p => p.Id).Skip(6).Select(p => p.Id), await db.Posts.OrderBy(p => p.Id).Skip(6).Select(p => p.Id).ToListAsync());
        Assert.Equal(Posts.OrderBy(p => p.Id).Take(2).Select(p => p.Id), await db.Posts.OrderBy(p => p.Id).Take(2).Select(p => p.Id).ToListAsync());
    }

    // ---- Element operators and quantifiers -------------------------------------------------

    [Fact]
    public async Task First_FirstOrDefault_Single_SingleOrDefault()
    {
        await using var db = await CreateContextAsync();

        Assert.Equal("Alpha", (await db.Blogs.OrderBy(b => b.Name).FirstAsync()).Name);
        Assert.Null(await db.Blogs.FirstOrDefaultAsync(b => b.Name == "nobody"));
        Assert.Equal(4, (await db.Blogs.SingleAsync(b => b.Name == "Delta's")).Id);
        Assert.Null(await db.Blogs.SingleOrDefaultAsync(b => b.Id == 99));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Blogs.SingleAsync(b => b.IsActive));
    }

    [Fact]
    public async Task Any_All_AndCount()
    {
        await using var db = await CreateContextAsync();

        Assert.True(await db.Posts.AnyAsync(p => p.Content == null));
        Assert.False(await db.Posts.AllAsync(p => p.Published));
        Assert.True(await db.Blogs.AllAsync(b => b.Name != string.Empty));
        Assert.Equal(Posts.Count(p => p.Published), await db.Posts.CountAsync(p => p.Published));
        Assert.Equal(Posts.LongCount(), await db.Posts.LongCountAsync());
    }

    [Fact]
    public async Task Contains_OnAParameterList_AndOnASubquery()
    {
        await using var db = await CreateContextAsync();
        var ids = new[] { 2, 4, 6, 99 };

        var fromList = await db.Posts.Where(p => ids.Contains(p.Id)).OrderBy(p => p.Id).Select(p => p.Id).ToListAsync();
        var fromSubquery = await db.Blogs.Where(b => db.Posts.Where(p => p.Views > 1_000).Select(p => p.BlogId).Contains(b.Id)).OrderBy(b => b.Id).Select(b => b.Id).ToListAsync();

        Assert.Equal(Posts.Where(p => ids.Contains(p.Id)).Select(p => p.Id), fromList);
        Assert.Equal(Blogs.Where(b => Posts.Where(p => p.Views > 1_000).Select(p => p.BlogId).Contains(b.Id)).Select(b => b.Id), fromSubquery);
    }

    // ---- Aggregates and grouping ------------------------------------------------------------

    [Fact]
    public async Task Aggregates_OverIntLongAndDecimal()
    {
        await using var db = await CreateContextAsync();

        Assert.Equal(Posts.Sum(p => p.Views), await db.Posts.SumAsync(p => p.Views));
        Assert.Equal(Posts.Sum(p => p.Score), await db.Posts.SumAsync(p => p.Score));
        Assert.Equal(Posts.Sum(p => p.BlogId), await db.Posts.SumAsync(p => p.BlogId));
        Assert.Equal(Posts.Min(p => p.Score), await db.Posts.MinAsync(p => p.Score));
        Assert.Equal(Posts.Max(p => p.Title), await db.Posts.MaxAsync(p => p.Title));
        Assert.Equal(Posts.Average(p => p.BlogId), await db.Posts.AverageAsync(p => p.BlogId), precision: 10);
        Assert.Equal(Posts.Average(p => p.Score), await db.Posts.AverageAsync(p => p.Score));
        Assert.Equal(Blogs.Max(b => b.Rating), await db.Blogs.MaxAsync(b => b.Rating));
    }

    [Fact]
    public async Task GroupBy_WithAggregates_AndHaving()
    {
        await using var db = await CreateContextAsync();

        var actual = await db.Posts
            .GroupBy(p => p.BlogId)
            .Where(g => g.Count() > 2)
            .OrderBy(g => g.Key)
            .Select(g => new { g.Key, Count = g.Count(), Views = g.Sum(p => p.Views), Best = g.Max(p => p.Score), Published = g.Count(p => p.Published) })
            .ToListAsync();

        var expected = Posts
            .GroupBy(p => p.BlogId)
            .Where(g => g.Count() > 2)
            .OrderBy(g => g.Key)
            .Select(g => new { g.Key, Count = g.Count(), Views = g.Sum(p => p.Views), Best = g.Max(p => p.Score), Published = g.Count(p => p.Published) });
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task GroupBy_CompositeKey_OnOwnedProperty()
    {
        await using var db = await CreateContextAsync();

        var actual = await db.Blogs
            .GroupBy(b => new { b.Address.City, b.IsActive })
            .Select(g => new { g.Key.City, g.Key.IsActive, Count = g.Count() })
            .OrderBy(x => x.City).ThenBy(x => x.IsActive)
            .ToListAsync();

        var expected = Blogs
            .GroupBy(b => new { b.Address.City, b.IsActive })
            .Select(g => new { g.Key.City, g.Key.IsActive, Count = g.Count() })
            .OrderBy(x => x.City, StringComparer.Ordinal).ThenBy(x => x.IsActive);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task GroupBy_TopRowPerGroup()
    {
        await using var db = await CreateContextAsync();

        var actual = await db.Posts
            .GroupBy(p => p.BlogId)
            .Select(g => g.OrderByDescending(p => p.Views).First().Title)
            .ToListAsync();

        var expected = Posts.GroupBy(p => p.BlogId).Select(g => g.OrderByDescending(p => p.Views).First().Title);
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
    }

    // ---- Joins -------------------------------------------------------------------------------

    [Fact]
    public async Task Join_Inner()
    {
        await using var db = await CreateContextAsync();

        var actual = await db.Posts.Join(db.Blogs, p => p.BlogId, b => b.Id, (p, b) => new { p.Id, b.Name }).OrderBy(x => x.Id).ToListAsync();

        Assert.Equal(Posts.Join(Blogs, p => p.BlogId, b => b.Id, (p, b) => new { p.Id, b.Name }).OrderBy(x => x.Id), actual);
    }

    [Fact]
    public async Task GroupJoin_LeftJoin_KeepsBlogsWithoutPosts()
    {
        await using var db = await CreateContextAsync();

        var actual = await (
            from b in db.Blogs
            join p in db.Posts on b.Id equals p.BlogId into posts
            from p in posts.DefaultIfEmpty()
            orderby b.Id, p.Id
            select new { b.Id, PostId = (int?)p.Id }).ToListAsync();

        var expected =
            from b in Blogs
            join p in Posts on b.Id equals p.BlogId into posts
            from p in posts.DefaultIfEmpty()
            orderby b.Id, p?.Id
            select new { b.Id, PostId = p?.Id };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task SelectMany_CrossJoin_AndCollectionNavigation()
    {
        await using var db = await CreateContextAsync();

        var cross = await (from b in db.Blogs from t in db.Tags select new { b.Id, Tag = t.Id }).CountAsync();
        var navigation = await db.Blogs.SelectMany(b => b.Posts).Where(p => p.Published).OrderBy(p => p.Id).Select(p => p.Id).ToListAsync();

        Assert.Equal(Blogs.Count * BloggingDatabase.Tags.Count(), cross);
        Assert.Equal(Blogs.SelectMany(b => b.Posts).Where(p => p.Published).OrderBy(p => p.Id).Select(p => p.Id), navigation);
    }

    [Fact]
    public async Task NavigationProperty_InPredicateAndProjection()
    {
        await using var db = await CreateContextAsync();

        var actual = await db.Posts.Where(p => p.Blog.Address.City == "Oslo").OrderBy(p => p.Id).Select(p => new { p.Id, p.Blog.Name }).ToListAsync();

        Assert.Equal(Posts.Where(p => p.Blog.Address.City == "Oslo").OrderBy(p => p.Id).Select(p => new { p.Id, p.Blog.Name }), actual);
    }

    // ---- Set operations ----------------------------------------------------------------------

    [Fact]
    public async Task Distinct_Union_Concat_Intersect_Except()
    {
        await using var db = await CreateContextAsync();
        var popular = db.Posts.Where(p => p.Views >= 750).Select(p => p.BlogId);
        var drafts = db.Posts.Where(p => !p.Published).Select(p => p.BlogId);
        var popularLocal = Posts.Where(p => p.Views >= 750).Select(p => p.BlogId);
        var draftsLocal = Posts.Where(p => !p.Published).Select(p => p.BlogId);

        Assert.Equal(Posts.Select(p => p.BlogId).Distinct().Order(), (await db.Posts.Select(p => p.BlogId).Distinct().ToListAsync()).Order());
        Assert.Equal(popularLocal.Union(draftsLocal).Order(), (await popular.Union(drafts).ToListAsync()).Order());
        Assert.Equal(popularLocal.Concat(draftsLocal).Order(), (await popular.Concat(drafts).ToListAsync()).Order());
        Assert.Equal(popularLocal.Intersect(draftsLocal).Order(), (await popular.Intersect(drafts).ToListAsync()).Order());
        Assert.Equal(popularLocal.Except(draftsLocal).Order(), (await popular.Except(drafts).ToListAsync()).Order());
    }

    // ---- Subqueries --------------------------------------------------------------------------

    [Fact]
    public async Task CorrelatedScalarSubqueries_InProjection()
    {
        await using var db = await CreateContextAsync();

        var actual = await db.Blogs.OrderBy(b => b.Id)
            .Select(b => new
            {
                b.Id,
                PostCount = b.Posts.Count(),
                TopTitle = b.Posts.OrderByDescending(p => p.Views).Select(p => p.Title).FirstOrDefault(),
                HasDrafts = b.Posts.Any(p => !p.Published),
            })
            .ToListAsync();

        var expected = Blogs.OrderBy(b => b.Id).Select(b => new
        {
            b.Id,
            PostCount = b.Posts.Count,
            TopTitle = b.Posts.OrderByDescending(p => p.Views).Select(p => p.Title).FirstOrDefault(),
            HasDrafts = b.Posts.Any(p => !p.Published),
        });
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task CollectionProjection_WithTake_UsesALateralJoin()
    {
        await using var db = await CreateContextAsync();

        var actual = await db.Blogs.OrderBy(b => b.Id)
            .Select(b => new { b.Id, Top = b.Posts.OrderByDescending(p => p.Views).Take(2).Select(p => p.Id).ToList() })
            .ToListAsync();

        var expected = Blogs.OrderBy(b => b.Id).Select(b => new { b.Id, Top = b.Posts.OrderByDescending(p => p.Views).Take(2).Select(p => p.Id).ToList() }).ToList();
        Assert.Equal(expected.Select(x => x.Id), actual.Select(x => x.Id));
        Assert.Equal(expected.Select(x => string.Join(",", x.Top)), actual.Select(x => string.Join(",", x.Top)));
    }

    // ---- Include, owned and keyless types -----------------------------------------------------

    [Fact]
    public async Task Include_Reference_AndCollection_SingleQuery()
    {
        await using var db = await CreateContextAsync();

        var posts = await db.Posts.Include(p => p.Blog).OrderBy(p => p.Id).ToListAsync();
        var blogs = await db.Blogs.Include(b => b.Posts).ThenInclude(p => p.Tags).OrderBy(b => b.Id).ToListAsync();

        Assert.Equal(Posts.OrderBy(p => p.Id).Select(p => p.Blog.Name), posts.Select(p => p.Blog.Name));
        Assert.Equal(Blogs.Select(b => b.Posts.Count), blogs.Select(b => b.Posts.Count));
        Assert.Equal(Posts.Sum(p => p.Tags.Count), blogs.SelectMany(b => b.Posts).Sum(p => p.Tags.Count));
    }

    [Fact]
    public async Task Include_Collection_SplitQuery()
    {
        await using var db = await CreateContextAsync();

        var blogs = await db.Blogs.Include(b => b.Posts).ThenInclude(p => p.Tags).AsSplitQuery().OrderBy(b => b.Id).ToListAsync();

        Assert.Equal(Blogs.Select(b => b.Posts.Count), blogs.Select(b => b.Posts.Count));
        Assert.Equal(Posts.Sum(p => p.Tags.Count), blogs.SelectMany(b => b.Posts).Sum(p => p.Tags.Count));
    }

    [Fact]
    public async Task OwnedType_IsMaterialized_AndQueryable()
    {
        await using var db = await CreateContextAsync();

        var blog = await db.Blogs.AsNoTracking().SingleAsync(b => b.Address.Country == null);

        Assert.Equal("Beta", blog.Name);
        Assert.Equal("Lima", blog.Address.City);
    }

    [Fact]
    public async Task KeylessEntity_FromToSqlQuery_IsComposable()
    {
        await using var db = await CreateContextAsync();

        var actual = await db.BlogPostCounts.Where(c => c.PostCount >= 2).OrderBy(c => c.BlogId).ToListAsync();

        var expected = Posts.GroupBy(p => p.BlogId).Where(g => g.Count() >= 2).OrderBy(g => g.Key).Select(g => (g.Key, (long)g.Count()));
        Assert.Equal(expected, actual.Select(c => (c.BlogId, c.PostCount)));
    }

    [Fact]
    public async Task FromSql_IsComposable()
    {
        await using var db = await CreateContextAsync();
        var minViews = 100L;

        var actual = await db.Posts.FromSql($"SELECT * FROM \"Posts\" WHERE \"Views\" >= {minViews}").Where(p => p.Published).OrderBy(p => p.Id).Select(p => p.Id).ToListAsync();

        Assert.Equal(Posts.Where(p => p.Views >= minViews && p.Published).OrderBy(p => p.Id).Select(p => p.Id), actual);
    }

    [Fact]
    public async Task Tracking_Query_ReturnsTheSameInstanceForTheSameKey()
    {
        await using var db = await CreateContextAsync();

        var first = await db.Blogs.SingleAsync(b => b.Id == 1);
        var again = await db.Blogs.SingleAsync(b => b.Name == "Alpha");

        Assert.Same(first, again);
    }
}
