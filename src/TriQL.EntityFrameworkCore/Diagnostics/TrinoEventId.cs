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

    // Provider event IDs start at CoreEventId.ProviderBaseId (30000), as the other providers' do.
    private enum Id
    {
        UniqueIndexNotEnforced = CoreEventId.ProviderBaseId,
    }

    /// <summary>
    /// A unique index or alternate key is in the model, but Trino does not enforce uniqueness, so
    /// duplicate values can be written. Category <c>Microsoft.EntityFrameworkCore.Model.Validation</c>;
    /// logged as a warning.
    /// </summary>
    public static readonly EventId UniqueIndexNotEnforced = new((int)Id.UniqueIndexNotEnforced, ValidationPrefix + Id.UniqueIndexNotEnforced);
}
