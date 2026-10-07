using Microsoft.EntityFrameworkCore.Infrastructure;
using TriQL.EntityFrameworkCore.Infrastructure.Internal;

// Extension methods live in EF's namespace, next to IsSqlServer, IsNpgsql etc.
namespace Microsoft.EntityFrameworkCore;

/// <summary>Trino-specific extension methods for <see cref="DatabaseFacade"/>.</summary>
public static class TrinoDatabaseFacadeExtensions
{
    /// <summary>
    /// Returns <see langword="true"/> when the context is configured to use the Trino provider.
    /// Only checks configuration; it does not connect.
    /// </summary>
    /// <param name="database">The facade from <see cref="DbContext.Database"/>.</param>
    /// <returns><see langword="true"/> when the Trino provider is in use.</returns>
    public static bool IsTrino(this DatabaseFacade database)
    {
        ArgumentNullException.ThrowIfNull(database);
        return database.ProviderName == typeof(TrinoOptionsExtension).Assembly.GetName().Name;
    }
}
