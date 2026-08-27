using System.Text;

namespace TriQL.Client.Internal;

/// <summary>
/// Rewrites named placeholders (<c>:name</c>, <c>@name</c>) to positional <c>?</c> form. Aware of
/// string literals, quoted identifiers, and <c>--</c>/<c>/* */</c> comments so placeholder-like text
/// inside them is left untouched. See FR-8.4, FR-8.5.
/// </summary>
internal static class ParameterRewriter
{
    /// <summary>
    /// Returns the rewritten SQL (every placeholder replaced with <c>?</c>) plus, in placeholder
    /// order, the original name for named placeholders or <see langword="null"/> for positional ones.
    /// </summary>
    public static (string Sql, IReadOnlyList<string?> PlaceholderNames) Rewrite(string sql)
    {
        var output = new StringBuilder(sql.Length);
        var names = new List<string?>();
        var i = 0;

        while (i < sql.Length)
        {
            var c = sql[i];
            if (c == '\'')
            {
                i = CopyDelimited(sql, i, '\'', output);
                continue;
            }

            if (c == '"')
            {
                i = CopyDelimited(sql, i, '"', output);
                continue;
            }

            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                i = CopyLineComment(sql, i, output);
                continue;
            }

            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                i = CopyBlockComment(sql, i, output);
                continue;
            }

            if (c == '?')
            {
                output.Append('?');
                names.Add(null);
                i++;
                continue;
            }

            if ((c == ':' || c == '@') && i + 1 < sql.Length && (char.IsLetter(sql[i + 1]) || sql[i + 1] == '_'))
            {
                var start = i + 1;
                var end = start;
                while (end < sql.Length && (char.IsLetterOrDigit(sql[end]) || sql[end] == '_'))
                {
                    end++;
                }

                output.Append('?');
                names.Add(sql[start..end]);
                i = end;
                continue;
            }

            output.Append(c);
            i++;
        }

        return (output.ToString(), names);
    }

    private static int CopyDelimited(string sql, int start, char delimiter, StringBuilder output)
    {
        output.Append(delimiter);
        var i = start + 1;
        while (i < sql.Length)
        {
            var c = sql[i];
            output.Append(c);
            if (c == delimiter)
            {
                i++;
                if (i < sql.Length && sql[i] == delimiter)
                {
                    // Doubled delimiter (escaped quote): the literal/identifier continues.
                    output.Append(delimiter);
                    i++;
                    continue;
                }

                break;
            }

            i++;
        }

        return i;
    }

    private static int CopyLineComment(string sql, int start, StringBuilder output)
    {
        var i = start;
        while (i < sql.Length && sql[i] != '\n')
        {
            output.Append(sql[i]);
            i++;
        }

        return i;
    }

    private static int CopyBlockComment(string sql, int start, StringBuilder output)
    {
        output.Append(sql[start]);
        output.Append(sql[start + 1]);
        var i = start + 2;
        while (i < sql.Length)
        {
            if (sql[i] == '*' && i + 1 < sql.Length && sql[i + 1] == '/')
            {
                output.Append('*').Append('/');
                i += 2;
                break;
            }

            output.Append(sql[i]);
            i++;
        }

        return i;
    }
}
