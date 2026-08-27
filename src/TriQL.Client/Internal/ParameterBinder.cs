using TriQL.Client.Exceptions;

namespace TriQL.Client.Internal;

/// <summary>
/// Matches parsed placeholders to supplied <see cref="TrinoParameter"/> values, in the order the
/// placeholders occur, before any network call. See FR-8.4, FR-8.5, FR-8.6.
/// </summary>
internal static class ParameterBinder
{
    public static IReadOnlyList<TrinoParameter> Bind(IReadOnlyList<string?> placeholderNames, TrinoParameterCollection parameters)
    {
        if (placeholderNames.Count == 0 && parameters.Count == 0)
        {
            return [];
        }

        var hasNamed = false;
        var hasPositional = false;
        foreach (var name in placeholderNames)
        {
            if (name is null)
            {
                hasPositional = true;
            }
            else
            {
                hasNamed = true;
            }
        }

        if (hasNamed && hasPositional)
        {
            throw new TrinoParameterException(
                "Mixing positional ('?') and named (':name'/'@name') placeholders in the same statement is not supported.");
        }

        if (!hasNamed)
        {
            if (placeholderNames.Count != parameters.Count)
            {
                throw new TrinoParameterException(
                    $"The statement has {placeholderNames.Count} parameter placeholder(s) but {parameters.Count} parameter(s) were supplied.");
            }

            return [.. parameters];
        }

        // A named parameter may fill several placeholders, so distinct names — not placeholder count — must match.
        var distinctNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in placeholderNames)
        {
            distinctNames.Add(name!);
        }

        if (distinctNames.Count != parameters.Count)
        {
            throw new TrinoParameterException(
                $"The statement references {distinctNames.Count} distinct named parameter(s) but {parameters.Count} parameter(s) were supplied.");
        }

        var result = new List<TrinoParameter>(placeholderNames.Count);
        foreach (var name in placeholderNames)
        {
            if (!parameters.TryGetValue(name!, out var parameter))
            {
                throw new TrinoParameterException($"No parameter named '{name}' was supplied.");
            }

            result.Add(parameter);
        }

        return result;
    }
}
