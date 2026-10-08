using Microsoft.EntityFrameworkCore;

namespace TriQL.EntityFrameworkCore.Tests.Query;

/// <summary>A small Blog/Post model for SQL-baseline tests.</summary>
internal sealed class QueryBloggingContext(DbContextOptions<QueryBloggingContext> options) : DbContext(options)
{
    public DbSet<QBlog> Blogs => Set<QBlog>();

    public DbSet<QPost> Posts => Set<QPost>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<QBlog>().Property(b => b.Id).ValueGeneratedNever();
        modelBuilder.Entity<QPost>().Property(p => p.Id).ValueGeneratedNever();
        modelBuilder.Entity<QPost>().Property(p => p.Score).HasPrecision(10, 2);
        modelBuilder.Entity<QBlog>().HasMany(b => b.Posts).WithOne().HasForeignKey(p => p.BlogId);
    }
}

internal sealed class QBlog
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<QPost> Posts { get; set; } = [];
}

internal sealed class QPost
{
    public int Id { get; set; }

    public int BlogId { get; set; }

    public string Title { get; set; } = string.Empty;

    public long Views { get; set; }

    public decimal Score { get; set; }
}
