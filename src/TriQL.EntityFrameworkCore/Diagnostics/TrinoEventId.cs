using Microsoft.Extensions.Logging;

// Lives in EF's diagnostics namespace, next to CoreEventId and RelationalEventId.
namespace Microsoft.EntityFrameworkCore.Diagnostics;

/// <summary>
/// Event IDs for the Trino provider's events, used with EF's logging and with
/// <c>ConfigureWarnings</c> (for example to turn a warning into an error, or to ignore it).
/// </summary>
public static class TrinoEventId
{
    private const string ValidationPrefix = "Microsoft.EntityFrameworkCore.Model.Validation.";
    private const string UpdatePrefix = "Microsoft.EntityFrameworkCore.Update.";

    // Provider event IDs start at CoreEventId.ProviderBaseId (30000), as the other providers' do.
    private enum Id
    {
        UniqueIndexNotEnforced = CoreEventId.ProviderBaseId,

        NonAtomicSaveChanges = CoreEventId.ProviderBaseId + 100,
    }

    /// <summary>
    /// A unique index or alternate key is in the model, but Trino does not enforce uniqueness, so
    /// duplicate values can be written. Category <c>Microsoft.EntityFrameworkCore.Model.Validation</c>;
    /// logged as a warning.
    /// </summary>
    public static readonly EventId UniqueIndexNotEnforced = new((int)Id.UniqueIndexNotEnforced, ValidationPrefix + Id.UniqueIndexNotEnforced);

    /// <summary>
    /// <c>SaveChanges</c> is running more than one statement. Trino commits each statement on its own (there
    /// are no multi-statement transactions), so if a later statement fails the earlier ones stay committed;
    /// their entities are marked as saved. Category <c>Microsoft.EntityFrameworkCore.Update</c>; logged once
    /// per <c>SaveChanges</c>, as a warning.
    /// </summary>
    public static readonly EventId NonAtomicSaveChanges = new((int)Id.NonAtomicSaveChanges, UpdatePrefix + Id.NonAtomicSaveChanges);
}
