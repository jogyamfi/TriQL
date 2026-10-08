using System.Reflection;
using System.Text;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace TriQL.EntityFrameworkCore.Query.Internal.Translators;

/// <summary>Helpers shared by the Trino translators.</summary>
internal static class TrinoSqlFunctions
{
    /// <summary>The escape character used in the <c>LIKE</c> patterns the translators build from constants.</summary>
    public const char LikeEscapeChar = '\\';

    /// <summary>
    /// A built-in function call that returns <c>NULL</c> when any argument is <c>NULL</c>, which is how
    /// almost every Trino scalar function behaves.
    /// </summary>
    public static SqlExpression Function(
        this ISqlExpressionFactory factory,
        string name,
        IReadOnlyList<SqlExpression> arguments,
        Type returnType,
        RelationalTypeMapping? typeMapping = null) =>
        factory.Function(name, arguments, nullable: true, argumentsPropagateNullability: Enumerable.Repeat(true, arguments.Count), returnType, typeMapping);

    /// <summary>Whether <paramref name="method"/> takes exactly the given parameter types.</summary>
    public static bool HasParameters(this MethodInfo method, params Type[] parameterTypes)
    {
        var parameters = method.GetParameters();
        if (parameters.Length != parameterTypes.Length)
        {
            return false;
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].ParameterType != parameterTypes[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Escapes <c>%</c>, <c>_</c> and the escape character itself for use in a <c>LIKE</c> pattern.</summary>
    /// <returns>The escaped text, and whether anything needed escaping.</returns>
    public static (string Escaped, bool NeedsEscape) EscapeLikePattern(string value)
    {
        if (value.AsSpan().IndexOfAny('%', '_', LikeEscapeChar) < 0)
        {
            return (value, false);
        }

        var builder = new StringBuilder(value.Length + 4);
        foreach (var c in value)
        {
            if (c is '%' or '_' or LikeEscapeChar)
            {
                builder.Append(LikeEscapeChar);
            }

            builder.Append(c);
        }

        return (builder.ToString(), true);
    }
}
