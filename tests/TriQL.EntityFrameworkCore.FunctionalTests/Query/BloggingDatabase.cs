using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using TriQL.IntegrationTests.Fixtures;

namespace TriQL.EntityFrameworkCore.FunctionalTests.Query;

/// <summary>
/// The seeded Blogging database: deterministic in-memory data (the LINQ-to-Objects oracle) and
/// the same rows loaded once per test run into a fresh schema of the container's <c>memory</c>
/// catalog. Tables are created from EF's own relational model, so the DDL uses the provider's store
/// types. Seeding uses raw SQL, because SaveChanges arrives in Phase 6.
/// </summary>
public static class BloggingDatabase
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? _schema;

    public static IReadOnlyList<Blog> Blogs { get; } = CreateData();

    public static IEnumerable<Post> Posts => Blogs.SelectMany(b => b.Posts);

    public static IEnumerable<Tag> Tags => Posts.SelectMany(p => p.Tags).DistinctBy(t => t.Id).OrderBy(t => t.Id);

    /// <summary>Creates and seeds the schema on first use; returns a connection string scoped to it.</summary>
    public static async Task<string> GetConnectionStringAsync(TrinoContainerFixture fixture)
    {
        await Gate.WaitAsync();
        try
        {
            if (_schema is null)
            {
                var schema = $"ef_query_{Guid.NewGuid():N}";
                await SeedAsync(ConnectionString(fixture, schema), schema);
                _schema = schema;
            }

            return ConnectionString(fixture, _schema);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static string ConnectionString(TrinoContainerFixture fixture, string schema) =>
        $"Server={fixture.ServerUri};User=triql-ef;Catalog=memory;Schema={schema}";

    private static async Task SeedAsync(string connectionString, string schema)
    {
        await using var context = new BloggingContext(new DbContextOptionsBuilder<BloggingContext>().UseTrino(connectionString).Options);

        // Identifiers are generated here, not user input; every value is a bound parameter.
        Task ExecuteAsync(string sql, params object?[] parameters) => context.Database.ExecuteSqlRawAsync(sql, parameters!);

        await ExecuteAsync($"CREATE SCHEMA memory.{schema}");

        foreach (var table in context.Model.GetRelationalModel().Tables)
        {
            var columns = string.Join(", ", table.Columns.Select(c => $"\"{c.Name}\" {c.StoreType}"));
            await ExecuteAsync($"CREATE TABLE \"{table.Name}\" ({columns})");
        }

        foreach (var blog in Blogs)
        {
            await ExecuteAsync(
                $"INSERT INTO \"Blogs\" (\"Id\", \"Name\", \"Rating\", \"Created\", \"IsActive\", \"Address_City\", \"Address_Country\") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6)",
                blog.Id, blog.Name, blog.Rating, blog.Created, blog.IsActive, blog.Address.City, blog.Address.Country);
        }

        foreach (var post in Posts)
        {
            await ExecuteAsync(
                $"INSERT INTO \"Posts\" (\"Id\", \"BlogId\", \"Title\", \"Content\", \"Views\", \"Score\", \"Published\") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6)",
                post.Id, post.BlogId, post.Title, post.Content, post.Views, post.Score, post.Published);
        }

        foreach (var tag in Tags)
        {
            await ExecuteAsync($"INSERT INTO \"Tags\" (\"Id\", \"Name\") VALUES (@p0, @p1)", tag.Id, tag.Name);
        }

        foreach (var post in Posts)
        {
            foreach (var tag in post.Tags)
            {
                await ExecuteAsync($"INSERT INTO \"PostTags\" (\"PostsId\", \"TagsId\") VALUES (@p0, @p1)", post.Id, tag.Id);
            }
        }
    }

    private static List<Blog> CreateData()
    {
        var csharp = new Tag { Id = 1, Name = "csharp" };
        var sql = new Tag { Id = 2, Name = "sql" };
        var trino = new Tag { Id = 3, Name = "trino" };

        var blogs = new List<Blog>
        {
            new() { Id = 1, Name = "Alpha", Rating = 5, Created = new DateTime(2026, 1, 1, 9, 30, 0), IsActive = true, Address = new() { City = "Oslo", Country = "NO" } },
            new() { Id = 2, Name = "Beta", Rating = null, Created = new DateTime(2025, 6, 15), IsActive = false, Address = new() { City = "Lima", Country = null } },
            new() { Id = 3, Name = "Gamma", Rating = 3, Created = new DateTime(2024, 12, 31, 23, 59, 59), IsActive = true, Address = new() { City = "Oslo", Country = "NO" } },
            new() { Id = 4, Name = "Delta's", Rating = null, Created = new DateTime(2026, 3, 1), IsActive = true, Address = new() { City = "Paris", Country = "FR" } },
        };

        void AddPost(Blog blog, int id, string title, string? content, long views, decimal score, bool published, params Tag[] tags)
        {
            var post = new Post { Id = id, BlogId = blog.Id, Blog = blog, Title = title, Content = content, Views = views, Score = score, Published = published, Tags = [.. tags] };
            blog.Posts.Add(post);
            foreach (var tag in tags)
            {
                tag.Posts.Add(post);
            }
        }

        AddPost(blogs[0], 1, "Hello", "First post", 100, 4.50m, true, csharp);
        AddPost(blogs[0], 2, "LINQ tips", "Use Where before Select", 2_500, 9.75m, true, csharp, sql);
        AddPost(blogs[0], 3, "Draft", null, 0, 0m, false);
        AddPost(blogs[1], 4, "Joins", "INNER, LEFT, CROSS", 750, 7.25m, true, sql);
        AddPost(blogs[1], 5, "Window functions", "ROW_NUMBER", 1_200, 8.00m, true, sql, trino);
        AddPost(blogs[2], 6, "Trino 101", "Connectors and catalogs", 5_000_000_000, 9.99m, true, trino);
        AddPost(blogs[2], 7, "Iceberg", null, 42, 6.10m, true, trino);
        AddPost(blogs[2], 8, "Old news", "Stale", 3, 1.00m, false);

        return blogs;
    }
}
