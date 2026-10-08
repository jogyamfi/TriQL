using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Microsoft.Extensions.Logging;
using TriQL.EntityFrameworkCore.Tests.TestUtilities;

namespace TriQL.EntityFrameworkCore.Tests.Metadata;

/// <summary>Phase 5: value-generation conventions, model validation rules and the uniqueness warning.</summary>
public sealed class ModelValidationTests
{
    // ---- conventions ----------------------------------------------------------------------

    [Fact]
    public void IntegerKey_IsNotGeneratedByConvention()
    {
        var model = BuildModel(b => b.Entity<IntKeyed>());

        Assert.Equal(ValueGenerated.Never, model.FindEntityType(typeof(IntKeyed))!.FindProperty(nameof(IntKeyed.Id))!.ValueGenerated);
    }

    [Fact]
    public void GuidKey_IsGeneratedOnTheClient_AsUuidV7()
    {
        using var fake = new FakeTrino();
        using var db = new ModelContext(ModelContext.CreateOptions(fake), b => b.Entity<GuidKeyed>());

        var first = db.Add(new GuidKeyed());
        var second = db.Add(new GuidKeyed());

        Assert.Equal(ValueGenerated.OnAdd, db.Model.FindEntityType(typeof(GuidKeyed))!.FindProperty(nameof(GuidKeyed.Id))!.ValueGenerated);
        Assert.Equal(7, first.Entity.Id.Version);
        Assert.NotEqual(first.Entity.Id, second.Entity.Id);
        Assert.False(first.Property(e => e.Id).IsTemporary);
    }

    [Fact]
    public void ClientManagedValues_AreAccepted()
    {
        var model = BuildModel(b =>
        {
            b.Entity<IntKeyed>(e =>
            {
                e.Property(x => x.Version).IsConcurrencyToken();
                e.Property(x => x.Name).ValueGeneratedOnAdd().HasValueGenerator<StringValueGenerator>();
            });
            b.Entity<Order>().OwnsMany(o => o.Lines, l =>
            {
                l.Property<Guid>("Id");
                l.HasKey("Id");
            });
        });

        Assert.NotNull(model.FindEntityType(typeof(IntKeyed)));
    }

    // ---- rejected configuration -----------------------------------------------------------

    [Fact]
    public void ExplicitIntegerValueGeneratedOnAdd_IsRejected() =>
        AssertRejected(b => b.Entity<IntKeyed>().Property(e => e.Id).ValueGeneratedOnAdd(), "'IntKeyed.Id'", "ValueGeneratedOnAdd", "no identity columns");

    [Fact]
    public void ValueGeneratedOnUpdate_IsRejected() =>
        AssertRejected(b => b.Entity<IntKeyed>().Property(e => e.Version).ValueGeneratedOnUpdate(), "'IntKeyed.Version'", "on update");

    [Fact]
    public void RowVersion_IsRejected() =>
        AssertRejected(b => b.Entity<IntKeyed>().Property(e => e.Stamp).IsRowVersion(), "'IntKeyed.Stamp'", "row version", "IsConcurrencyToken()");

    [Fact]
    public void ComputedColumn_IsRejected() =>
        AssertRejected(b => b.Entity<IntKeyed>().Property(e => e.Name).HasComputedColumnSql("upper(x)"), "'IntKeyed.Name'", "computed column");

    [Fact]
    public void DefaultValue_IsRejected() =>
        AssertRejected(b => b.Entity<IntKeyed>().Property(e => e.Version).HasDefaultValue(1L), "'IntKeyed.Version'", "default value");

    [Fact]
    public void DefaultValueSql_IsRejected() =>
        AssertRejected(b => b.Entity<IntKeyed>().Property(e => e.Name).HasDefaultValueSql("'x'"), "'IntKeyed.Name'", "default value");

    [Fact]
    public void Sequence_IsRejected() =>
        AssertRejected(b => { b.Entity<IntKeyed>(); b.HasSequence<long>("ids"); }, "sequence 'ids'", "HiLo");

    [Fact]
    public void OwnsMany_WithTheConventionalOrdinalKey_IsRejected() =>
        AssertRejected(b => b.Entity<Order>().OwnsMany(o => o.Lines), "owned collection", "OwnsMany", "HasKey");

    [Fact]
    public void CatalogWithoutSchema_IsRejected() =>
        AssertRejected(b => b.Entity<IntKeyed>().HasCatalog("lake"), "catalog 'lake'", "no schema", "HasDefaultSchema");

    [Fact]
    public void DefaultCatalogWithoutSchema_IsRejected() =>
        AssertRejected(b => { b.HasDefaultCatalog("lake"); b.Entity<IntKeyed>(); }, "catalog 'lake'", "no schema");

    [Fact]
    public void EntityTypesSharingATable_InDifferentCatalogs_AreRejected() =>
        AssertRejected(
            b =>
            {
                b.HasDefaultSchema("sales").HasDefaultCatalog("lake");
                b.Entity<IntKeyed>();
                b.Entity<DerivedKeyed>().HasCatalog("archive");
            },
            "'IntKeyed'",
            "'DerivedKeyed'",
            "'lake'",
            "'archive'",
            "must be in the same catalog");

    [Fact]
    public void TableNamesDifferingOnlyByCase_AreRejected() =>
        AssertRejected(b => { b.Entity<IntKeyed>().ToTable("orders"); b.Entity<GuidKeyed>().ToTable("Orders"); }, "'orders'", "'Orders'", "differ only by case");

    [Fact]
    public void ColumnNamesDifferingOnlyByCase_AreRejected() =>
        AssertRejected(b => b.Entity<IntKeyed>().Property(e => e.Name).HasColumnName("id"), "'Id'", "'id'", "differ only by case");

    // ---- uniqueness warning ---------------------------------------------------------------

    [Fact]
    public void UniqueIndexAndAlternateKey_LogAWarning()
    {
        using var fake = new FakeTrino();
        var messages = new List<(EventId Id, string Message)>();
        using var db = new ModelContext(
            ModelContext.CreateOptions(fake, o => o.LogTo((id, _) => id == TrinoEventId.UniqueIndexNotEnforced, e => messages.Add((e.EventId, e.ToString())))),
            b => b.Entity<IntKeyed>(e =>
            {
                e.HasIndex(x => x.Name).IsUnique();
                e.HasAlternateKey(x => x.Version);
                e.HasIndex(x => x.Stamp);
            }));

        _ = db.Model;

        Assert.Equal(2, messages.Count);
        Assert.Contains(messages, m => m.Message.Contains("unique index {'Name'}", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Message.Contains("alternate key {'Version'}", StringComparison.Ordinal));
        Assert.All(messages, m => Assert.Contains("'IntKeyed'", m.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void UniqueIndexWarning_CanBeMadeAnError()
    {
        using var fake = new FakeTrino();
        using var db = new ModelContext(
            ModelContext.CreateOptions(fake, o => o.ConfigureWarnings(w => w.Throw(TrinoEventId.UniqueIndexNotEnforced))),
            b => b.Entity<IntKeyed>().HasIndex(x => x.Name).IsUnique());

        var exception = Assert.Throws<InvalidOperationException>(() => db.Model);
        Assert.Contains(nameof(TrinoEventId.UniqueIndexNotEnforced), exception.Message, StringComparison.Ordinal);
    }

    // ---- helpers --------------------------------------------------------------------------

    private static IModel BuildModel(Action<ModelBuilder> configure)
    {
        using var fake = new FakeTrino();
        using var db = new ModelContext(ModelContext.CreateOptions(fake), configure);
        return db.Model;
    }

    private static void AssertRejected(Action<ModelBuilder> configure, params string[] messageParts)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => BuildModel(configure));
        foreach (var part in messageParts)
        {
            Assert.Contains(part, exception.Message, StringComparison.Ordinal);
        }
    }

    private class IntKeyed
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public long Version { get; set; }

        public byte[] Stamp { get; set; } = [];
    }

    private sealed class DerivedKeyed : IntKeyed
    {
        public string Extra { get; set; } = string.Empty;
    }

    private sealed class GuidKeyed
    {
        public Guid Id { get; set; }
    }

    private sealed class Order
    {
        public int Id { get; set; }

        public List<OrderLine> Lines { get; set; } = [];
    }

    private sealed class OrderLine
    {
        public string Product { get; set; } = string.Empty;
    }
}
