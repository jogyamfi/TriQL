using Microsoft.EntityFrameworkCore;

namespace TriQL.EntityFrameworkCore.Tests.Query;

/// <summary>A one-table model with a column of every type the function and member translators handle.</summary>
internal sealed class FunctionsContext(DbContextOptions<FunctionsContext> options) : DbContext(options)
{
    public DbSet<FRow> Rows => Set<FRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FRow>().Property(r => r.Id).ValueGeneratedNever();
        modelBuilder.Entity<FRow>().Property(r => r.Amount).HasPrecision(10, 2);
    }
}

internal sealed class FRow
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Note { get; set; }

    public char Initial { get; set; }

    public int Count { get; set; }

    public int? Rating { get; set; }

    public long Views { get; set; }

    public double Ratio { get; set; }

    public decimal Amount { get; set; }

    public bool Flag { get; set; }

    public bool? MaybeFlag { get; set; }

    public DateTime Created { get; set; }

    public DateTimeOffset Stamp { get; set; }

    public DateOnly Day { get; set; }

    public TimeOnly Time { get; set; }

    public Guid Key { get; set; }

    public byte[] Data { get; set; } = [];
}
