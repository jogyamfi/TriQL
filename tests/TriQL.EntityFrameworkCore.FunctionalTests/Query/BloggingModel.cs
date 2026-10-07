using Microsoft.EntityFrameworkCore;

namespace TriQL.EntityFrameworkCore.FunctionalTests.Query;

/// <summary>The model the query suite runs against: references, collections, many-to-many, an owned type and a keyless query type.</summary>
public sealed class BloggingContext(DbContextOptions<BloggingContext> options) : DbContext(options)
{
    public DbSet<Blog> Blogs => Set<Blog>();

    public DbSet<Post> Posts => Set<Post>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<BlogPostCount> BlogPostCounts => Set<BlogPostCount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Blog>(b =>
        {
            b.Property(e => e.Id).ValueGeneratedNever();
            b.OwnsOne(e => e.Address);
            b.HasMany(e => e.Posts).WithOne(p => p.Blog).HasForeignKey(p => p.BlogId);
        });

        modelBuilder.Entity<Post>(p =>
        {
            p.Property(e => e.Id).ValueGeneratedNever();
            p.Property(e => e.Score).HasPrecision(10, 2);
            p.HasMany(e => e.Tags).WithMany(t => t.Posts).UsingEntity("PostTags");
        });

        modelBuilder.Entity<Tag>().Property(e => e.Id).ValueGeneratedNever();

        modelBuilder.Entity<BlogPostCount>()
            .HasNoKey()
            .ToSqlQuery("SELECT \"BlogId\", count(*) AS \"PostCount\" FROM \"Posts\" GROUP BY \"BlogId\"");
    }
}

public sealed class Blog
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int? Rating { get; set; }

    public DateTime Created { get; set; }

    public bool IsActive { get; set; }

    public Address Address { get; set; } = new();

    public List<Post> Posts { get; set; } = [];
}

public sealed class Address
{
    public string City { get; set; } = string.Empty;

    public string? Country { get; set; }
}

public sealed class Post
{
    public int Id { get; set; }

    public int BlogId { get; set; }

    public Blog Blog { get; set; } = null!;

    public string Title { get; set; } = string.Empty;

    public string? Content { get; set; }

    public long Views { get; set; }

    public decimal Score { get; set; }

    public bool Published { get; set; }

    public List<Tag> Tags { get; set; } = [];
}

public sealed class Tag
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<Post> Posts { get; set; } = [];
}

public sealed class BlogPostCount
{
    public int BlogId { get; set; }

    public long PostCount { get; set; }
}
