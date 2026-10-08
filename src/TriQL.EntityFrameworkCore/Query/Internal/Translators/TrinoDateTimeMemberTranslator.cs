using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace TriQL.EntityFrameworkCore.Query.Internal.Translators;

/// <summary>
/// Translates members of <see cref="DateTime"/>, <see cref="DateTimeOffset"/>, <see cref="DateOnly"/> and
/// <see cref="TimeOnly"/> to Trino's date and time functions.
/// </summary>
/// <remarks>
/// <para>
/// <c>DateTime.Now</c> is the current time in the <em>session</em> time zone, which the provider
/// sets to UTC unless the connection string names another one, so by default it equals
/// <see cref="DateTime.UtcNow"/> (not the client machine's local time). The parts of a
/// <see cref="DateTimeOffset"/> (<c>Year</c>, <c>Hour</c>, …) are those of its own offset; Iceberg stores
/// <c>timestamp with time zone</c> values in UTC, so values read from Iceberg have offset zero.
/// <c>DayOfWeek</c> converts Trino's ISO day (Monday = 1 … Sunday = 7) to .NET's (Sunday = 0).
/// </para>
/// <para>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </para>
/// </remarks>
public class TrinoDateTimeMemberTranslator : IMemberTranslator
{
    private static readonly Dictionary<string, string> PartFunctions = new(StringComparer.Ordinal)
    {
        [nameof(DateTime.Year)] = "year",
        [nameof(DateTime.Month)] = "month",
        [nameof(DateTime.Day)] = "day",
        [nameof(DateTime.Hour)] = "hour",
        [nameof(DateTime.Minute)] = "minute",
        [nameof(DateTime.Second)] = "second",
        [nameof(DateTime.Millisecond)] = "millisecond",
        [nameof(DateTime.DayOfYear)] = "day_of_year",
    };

    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private readonly IRelationalTypeMappingSource _typeMappingSource;

    /// <summary>Initializes a new instance.</summary>
    public TrinoDateTimeMemberTranslator(ISqlExpressionFactory sqlExpressionFactory, IRelationalTypeMappingSource typeMappingSource)
    {
        _sqlExpressionFactory = sqlExpressionFactory;
        _typeMappingSource = typeMappingSource;
    }

    /// <inheritdoc />
    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(returnType);

        var declaringType = member.DeclaringType;
        if (declaringType != typeof(DateTime) && declaringType != typeof(DateTimeOffset)
            && declaringType != typeof(DateOnly) && declaringType != typeof(TimeOnly))
        {
            return null;
        }

        if (instance is null)
        {
            return TranslateStatic(declaringType, member.Name);
        }

        if (PartFunctions.TryGetValue(member.Name, out var partFunction)
            && !(declaringType == typeof(DateOnly) && member.Name is nameof(DateTime.Hour) or nameof(DateTime.Minute) or nameof(DateTime.Second) or nameof(DateTime.Millisecond))
            && !(declaringType == typeof(TimeOnly) && member.Name is nameof(DateTime.Year) or nameof(DateTime.Month) or nameof(DateTime.Day) or nameof(DateTime.DayOfYear)))
        {
            return _sqlExpressionFactory.Function(partFunction, [instance], returnType);
        }

        return member.Name switch
        {
            nameof(DateTime.DayOfWeek) when declaringType != typeof(TimeOnly) =>
                _sqlExpressionFactory.Convert(
                    _sqlExpressionFactory.Modulo(
                        _sqlExpressionFactory.Function("day_of_week", [instance], typeof(int)),
                        _sqlExpressionFactory.Constant(7)),
                    returnType),

            nameof(DateTime.Date) when declaringType == typeof(DateTime) => DateTrunc(instance),
            nameof(DateTimeOffset.Date) when declaringType == typeof(DateTimeOffset) => DateTrunc(Timestamp(instance)),

            // The local date and time in the value's own offset.
            nameof(DateTimeOffset.DateTime) when declaringType == typeof(DateTimeOffset) => Timestamp(instance),
            nameof(DateTimeOffset.UtcDateTime) when declaringType == typeof(DateTimeOffset) => Timestamp(AtUtc(instance)),

            _ => null,
        };
    }

    private SqlExpression? TranslateStatic(Type declaringType, string memberName)
    {
        if (declaringType == typeof(DateTime))
        {
            return memberName switch
            {
                // String literals: the DateTime.Now/Today symbols are banned in this repository, nameof included.
                "Now" => LocalTimestamp(),
                nameof(DateTime.UtcNow) => Timestamp(AtUtc(CurrentTimestamp())),
                "Today" => Timestamp(_sqlExpressionFactory.NiladicFunction("current_date", nullable: false, typeof(DateOnly))),
                _ => null,
            };
        }

        if (declaringType == typeof(DateTimeOffset))
        {
            return memberName switch
            {
                nameof(DateTimeOffset.Now) => CurrentTimestamp(),
                nameof(DateTimeOffset.UtcNow) => AtUtc(CurrentTimestamp()),
                _ => null,
            };
        }

        return null;
    }

    /// <summary><c>localtimestamp(6)</c>: the session time zone's wall-clock time, as <c>timestamp(6)</c>.</summary>
    private SqlExpression LocalTimestamp() =>
        _sqlExpressionFactory.Function(
            "localtimestamp",
            [_sqlExpressionFactory.Constant(6)],
            nullable: false,
            argumentsPropagateNullability: [false],
            typeof(DateTime),
            _typeMappingSource.FindMapping(typeof(DateTime)));

    /// <summary><c>current_timestamp(6)</c>, as <c>timestamp(6) with time zone</c>.</summary>
    private SqlExpression CurrentTimestamp() =>
        _sqlExpressionFactory.Function(
            "current_timestamp",
            [_sqlExpressionFactory.Constant(6)],
            nullable: false,
            argumentsPropagateNullability: [false],
            typeof(DateTimeOffset),
            _typeMappingSource.FindMapping(typeof(DateTimeOffset)));

    private AtTimeZoneExpression AtUtc(SqlExpression value) =>
        new AtTimeZoneExpression(
            value,
            _sqlExpressionFactory.Constant("UTC", _typeMappingSource.FindMapping(typeof(string))),
            typeof(DateTimeOffset),
            _typeMappingSource.FindMapping(typeof(DateTimeOffset)));

    /// <summary><c>CAST(value AS timestamp(6))</c>: drops the time zone, keeping the local date and time.</summary>
    private SqlExpression Timestamp(SqlExpression value) =>
        _sqlExpressionFactory.Convert(value, typeof(DateTime), _typeMappingSource.FindMapping(typeof(DateTime)));

    private SqlExpression DateTrunc(SqlExpression timestamp) =>
        _sqlExpressionFactory.Function(
            "date_trunc",
            [_sqlExpressionFactory.Constant("day", _typeMappingSource.FindMapping(typeof(string))), timestamp],
            nullable: true,
            argumentsPropagateNullability: [false, true],
            typeof(DateTime),
            timestamp.TypeMapping);
}
