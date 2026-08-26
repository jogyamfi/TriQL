namespace TriQL.Client;

/// <summary>
/// The kind of role selection applied to a catalog. See FR-1.1.1, FR-1.2.2.
/// </summary>
public enum TrinoSelectedRoleType
{
    /// <summary>A specific named role.</summary>
    Role,

    /// <summary>All roles granted to the user.</summary>
    All,

    /// <summary>No role.</summary>
    None,
}

/// <summary>
/// A role selection sent via <c>X-Trino-Role</c> or received via <c>X-Trino-Set-Role</c>. See FR-1.1.1, FR-1.2.2.
/// </summary>
/// <param name="Type">The kind of selection.</param>
/// <param name="RoleName">The role name, required only when <paramref name="Type"/> is <see cref="TrinoSelectedRoleType.Role"/>.</param>
public sealed record TrinoSelectedRole(TrinoSelectedRoleType Type, string? RoleName = null)
{
    /// <summary>The <c>ALL</c> role selection.</summary>
    public static TrinoSelectedRole All { get; } = new(TrinoSelectedRoleType.All);

    /// <summary>The <c>NONE</c> role selection.</summary>
    public static TrinoSelectedRole None { get; } = new(TrinoSelectedRoleType.None);

    /// <summary>Creates a selection for the named role.</summary>
    public static TrinoSelectedRole Named(string roleName) => new(TrinoSelectedRoleType.Role, roleName);

    /// <summary>Renders the wire form used by <c>X-Trino-Role</c>, e.g. <c>ROLE{admin}</c>, <c>ALL</c>, or <c>NONE</c>.</summary>
    public override string ToString() => Type switch
    {
        TrinoSelectedRoleType.Role => $"ROLE{{{RoleName}}}",
        TrinoSelectedRoleType.All => "ALL",
        TrinoSelectedRoleType.None => "NONE",
        _ => throw new InvalidOperationException($"Unknown {nameof(TrinoSelectedRoleType)}: {Type}."),
    };

    /// <summary>Parses the wire form produced by <see cref="ToString"/>.</summary>
    public static TrinoSelectedRole Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            return All;
        }

        if (value.Equals("NONE", StringComparison.OrdinalIgnoreCase))
        {
            return None;
        }

        if (value.StartsWith("ROLE{", StringComparison.Ordinal) && value.EndsWith('}'))
        {
            return Named(value["ROLE{".Length..^1]);
        }

        throw new FormatException($"'{value}' is not a valid Trino role selection.");
    }
}
