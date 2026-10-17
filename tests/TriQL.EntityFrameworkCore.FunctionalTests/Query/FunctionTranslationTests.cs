using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TriQL.IntegrationTests.Fixtures;
using Xunit.Abstractions;

// The queries call culture-sensitive APIs on purpose: they are the LINQ shapes users write. The
// LINQ-to-Objects oracle runs them under the test machine's culture, on ASCII and invariant-safe data.
#pragma warning disable CA1304, CA1305, CA1307, CA1309, CA1310, CA1311, CA1847, CA1862, CA1866

namespace TriQL.EntityFrameworkCore.FunctionalTests.Query;

/// <summary>
/// Phase 4 live suite: every translated member and method runs on Trino over the seeded rows of
/// <see cref="FunctionsDatabase"/> and is compared with LINQ-to-Objects over the same rows. This is what
/// catches off-by-one positions (<c>substr</c>, <c>strpos</c>), day-of-week numbering and rounding
/// differences. Each check also asserts the function appears in the SQL, so a silent client-side
/// evaluation of the final projection cannot pass for a translation.
/// </summary>
[Collection(TrinoContainerCollection.Name)]
[Trait("Category", "EfRead")]
public sealed class FunctionTranslationTests(TrinoContainerFixture fixture, ITestOutputHelper output)
{
    private readonly List<string> _sql = [];

    private static IEnumerable<FunctionRow> Rows => FunctionsDatabase.Rows.OrderBy(r => r.Id);

    // ---- string ---------------------------------------------------------------------------

    [Fact]
    public Task String_Length_Case_Replace() =>
        AssertSameAsync(r => new { r.Name.Length, Upper = r.Name.ToUpper(), Lower = r.Name.ToLowerInvariant(), Replaced = r.Name.Replace("l", "L") }, "length(", "upper(", "replace(");

    [Fact]
    public Task String_Trim() =>
        AssertSameAsync(r => new { A = r.Name.Trim(), B = r.Name.TrimStart(), C = r.Name.TrimEnd(), D = r.Name.Trim('x'), E = r.Name.TrimStart('x'), F = r.Name.TrimEnd('x') }, "trim(", "ltrim(", "rtrim(");

    [Fact]
    public Task String_Substring() =>
        AssertSameAsync(r => new { A = r.Name.Substring(1), B = r.Name.Substring(1, 2), C = r.Name.Substring(r.Name.Length - 1) }, r => r.Name.Length >= 3, "substr(");

    [Fact]
    public Task String_IndexOf() =>
        AssertSameAsync(r => new { A = r.Name.IndexOf("l"), B = r.Name.IndexOf('%'), C = r.Name.IndexOf(string.Empty), D = r.Name.IndexOf("zz") }, "strpos(");

    [Fact]
    public async Task String_IndexOf_Parameter()
    {
        var value = "ph";
        await AssertSameAsync(r => r.Name.IndexOf(value), "strpos(");
    }

    [Fact]
    public Task String_Contains_StartsWith_EndsWith_Constants() =>
        AssertSameAsync(
            r => new { A = r.Name.Contains("%_"), B = r.Name.Contains("lph"), C = r.Name.StartsWith("50%"), D = r.Name.EndsWith("\\deal"), E = r.Name.StartsWith(string.Empty), F = r.Name.EndsWith('a') },
            "LIKE");

    [Fact]
    public async Task String_Contains_StartsWith_EndsWith_Parameters()
    {
        var percent = "%_";
        var prefix = "50%";
        var suffix = "\\deal";
        var empty = string.Empty;
        await AssertSameAsync(
            r => new { A = r.Name.Contains(percent), B = r.Name.StartsWith(prefix), C = r.Name.EndsWith(suffix), D = r.Name.EndsWith(empty), E = r.Name.Contains(empty) },
            "strpos(", "starts_with(", "reverse(");
    }

    [Fact]
    public async Task String_Predicates_Filter()
    {
        var suffix = "s  ";
        await AssertSameFilterAsync(r => r.Name.Contains("l") || r.Name.EndsWith(suffix) || r.Name.StartsWith("xx"), "LIKE", "reverse(");
    }

    [Fact]
    public Task String_IsNullOrEmpty_IsNullOrWhiteSpace() =>
        AssertSameAsync(r => new { A = string.IsNullOrEmpty(r.Note), B = string.IsNullOrWhiteSpace(r.Note) }, "trim(");

    [Fact]
    public Task Regex_IsMatch() =>
        AssertSameAsync(r => new { A = Regex.IsMatch(r.Name, "^[a-z]"), B = Regex.IsMatch(r.Name, "^A", RegexOptions.IgnoreCase), C = Regex.IsMatch(r.Name, "\\d+%") }, "regexp_like(");

    // ---- math -----------------------------------------------------------------------------

    [Fact]
    public Task Math_SameTypeFunctions() =>
        AssertSameAsync(
            r => new { A = Math.Abs(r.Count), B = Math.Ceiling(r.Amount), C = Math.Floor(r.Ratio), D = Math.Truncate(r.Amount), E = Math.Max(r.Count, 3), F = Math.Min(r.Ratio, 1.0), G = Math.Sign(r.Amount), H = Math.Abs(r.Amount) },
            "abs(", "ceiling(", "floor(", "truncate(", "greatest(", "least(", "sign(");

    [Fact]
    public Task Math_DoubleFunctions() =>
        AssertCloseAsync(
            r => new[]
            {
                Math.Sqrt(Math.Abs(r.Ratio)), Math.Cbrt(r.Ratio), Math.Pow(r.Ratio, 2), Math.Exp(r.Ratio / 1e10), Math.Log(Math.Abs(r.Ratio)),
                Math.Log10(Math.Abs(r.Ratio)), Math.Log2(Math.Abs(r.Ratio)), Math.Log(Math.Abs(r.Ratio), 3), Math.Sin(r.Ratio), Math.Cos(r.Ratio),
                Math.Tan(r.Ratio), Math.Atan(r.Ratio), Math.Atan2(r.Ratio, 2), Math.Tanh(r.Ratio / 1e10), Math.Sinh(r.Ratio / 1e10), Math.Cosh(r.Ratio / 1e10),
                Math.Acos(r.Ratio / 1e10), Math.Asin(r.Ratio / 1e10),
            },
            "sqrt(", "cbrt(", "power(", "exp(", "ln(", "log10(", "log2(", "log(", "atan2(", "sinh(");

    [Fact]
    public Task Math_Round_HalfToEven_Default() =>
        AssertSameAsync(r => new { Ratio = Math.Round(r.Ratio), Amount = Math.Round(r.Amount), ToEven = Math.Round(r.Ratio, MidpointRounding.ToEven) }, "round(");

    [Fact]
    public Task Math_Round_DecimalDigits_HalfToEven() =>
        AssertSameAsync(r => new { One = Math.Round(r.Amount, 1), Two = Math.Round(r.Amount, 2), Three = Math.Round(r.Amount, 3) }, "round(");

    [Fact]
    public Task Math_Round_AwayFromZero() =>
        AssertSameAsync(
            r => new { A = Math.Round(r.Ratio, MidpointRounding.AwayFromZero), B = Math.Round(r.Amount, MidpointRounding.AwayFromZero), C = Math.Round(r.Amount, 2, MidpointRounding.AwayFromZero), D = Math.Round(r.Ratio, 1, MidpointRounding.AwayFromZero) },
            "round(");

    // ---- date and time --------------------------------------------------------------------

    [Fact]
    public Task DateTime_Parts() =>
        AssertSameAsync(
            r => new { r.Created.Year, r.Created.Month, r.Created.Day, r.Created.Hour, r.Created.Minute, r.Created.Second, r.Created.Millisecond, r.Created.DayOfYear, r.Created.DayOfWeek, r.Created.Date },
            "year(", "day_of_week(", "date_trunc(");

    [Fact]
    public async Task DateTime_Add()
    {
        var fraction = 1.5;
        var minutes = -90.25;
        await AssertSameAsync(
            r => new
            {
                Years = r.Created.AddYears(1),
                Months = r.Created.AddMonths(12),
                Days = r.Created.AddDays(2),
                ColumnDays = r.Created.AddDays(r.Count),
                Hours = r.Created.AddHours(-r.Count),
                FractionalDays = r.Created.AddDays(fraction),
                Minutes = r.Created.AddMinutes(minutes),
                Seconds = r.Created.AddSeconds(30),
                Milliseconds = r.Created.AddMilliseconds(r.Count),
            },
            "date_add(");
    }

    [Fact]
    public Task DateOnly_And_TimeOnly() =>
        AssertSameAsync(
            r => new
            {
                r.Day.Year,
                r.Day.Month,
                r.Day.Day,
                r.Day.DayOfYear,
                r.Day.DayOfWeek,
                Plus = r.Day.AddDays(r.Count),
                NextMonth = r.Day.AddMonths(1),
                LastYear = r.Day.AddYears(-1),
                FromDateTime = DateOnly.FromDateTime(r.Created),
                r.Time.Hour,
                r.Time.Minute,
                r.Time.Second,
                r.Time.Millisecond,
            },
            "year(", "date_add(", "AS date)");

    [Fact]
    public Task DateTimeOffset_Members() =>
        AssertSameAsync(
            r => new { r.Stamp.Year, r.Stamp.Month, r.Stamp.Day, r.Stamp.Hour, r.Stamp.Minute, r.Stamp.DayOfWeek, r.Stamp.DateTime, r.Stamp.UtcDateTime, r.Stamp.Date, Later = r.Stamp.AddHours(r.Count) },
            "AT TIME ZONE 'UTC'", "date_add(");

    [Fact]
    public async Task Now_UtcNow_Today_AreTheSessionClock()
    {
        await using var db = await CreateContextAsync();
        var before = DateTime.UtcNow.AddMinutes(-5);

        var result = await db.Rows.Where(r => r.Id == 1).Select(r => new { Now = DateTime.Now, DateTime.UtcNow, Today = DateTime.Today, Offset = DateTimeOffset.UtcNow }).SingleAsync();

        var after = DateTime.UtcNow.AddMinutes(5);
        Assert.InRange(result.UtcNow, before, after);
        Assert.InRange(result.Now, before, after); // the provider's session time zone is UTC
        Assert.Equal(result.UtcNow.Date, result.Today);
        Assert.InRange(result.Offset.UtcDateTime, before, after);
        AssertSqlContains("localtimestamp(6)", "current_timestamp(6)", "current_date");
    }

    // ---- conversions, ToString, Guid, byte[] ----------------------------------------------

    [Fact]
    public Task ToString_And_Convert() =>
        AssertSameAsync(
            r => new
            {
                Count = r.Count.ToString(),
                Rating = r.Rating.ToString(),
                Key = r.Key.ToString(),
                Flag = r.Flag.ToString(),
                MaybeFlag = r.MaybeFlag.ToString(),
                AsLong = Convert.ToInt64(r.Count),
                AsDouble = Convert.ToDouble(r.Amount),
                AsDecimal = Convert.ToDecimal(r.Count),
                Text = Convert.ToString(r.Count),
            },
            "AS varchar)");

    [Fact]
    public async Task ToString_OfDecimal_KeepsTheColumnScale()
    {
        await using var db = await CreateContextAsync();

        var actual = await db.Rows.OrderBy(r => r.Id).Select(r => r.Amount.ToString()).ToListAsync();

        // decimal(10,3): Trino and .NET both print the value at the column's scale.
        Assert.Equal(Rows.Select(r => (r.Amount + 0.000m).ToString(System.Globalization.CultureInfo.InvariantCulture)), actual);
    }

    [Fact]
    public async Task Guid_NewGuid_IsGeneratedPerRow()
    {
        await using var db = await CreateContextAsync();

        var guids = await db.Rows.Select(r => Guid.NewGuid()).ToListAsync();

        Assert.Equal(Rows.Count(), guids.Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, guids);
        AssertSqlContains("uuid()");
    }

    [Fact]
    public Task ByteArray_Length() => AssertSameAsync(r => r.Data.Length, "length(");

    // ---- EF.Functions ---------------------------------------------------------------------

    [Fact]
    public Task Functions_Like_ILike() =>
        AssertSameFilterAsync(
            r => EF.Functions.ILike(r.Name, "ALPHA") || EF.Functions.ILike(r.Name, "%TRIM%") || EF.Functions.ILike(r.Name, "50!%%", "!"),
            expected: r => r.Id is 1 or 2 or 5,
            "lower(");

    [Fact]
    public async Task Functions_DateDiff()
    {
        var reference = new DateTime(2026, 1, 1, 12, 0, 0);
        var referenceDay = new DateOnly(2026, 1, 1);
        await AssertSameAsync(
            r => new
            {
                Days = EF.Functions.DateDiffDay(r.Created, reference),
                Hours = EF.Functions.DateDiffHour(r.Created, reference),
                Seconds = EF.Functions.DateDiffSecond(reference, r.Created),
                Months = EF.Functions.DateDiffMonth(r.Day, referenceDay),
                Years = EF.Functions.DateDiffYear(r.Day, referenceDay),
            },
            expected: r => new
            {
                Days = (long)(reference - r.Created).TotalDays,
                Hours = (long)(reference - r.Created).TotalHours,
                Seconds = (long)(r.Created - reference).TotalSeconds,
                Months = WholeMonths(r.Day, referenceDay),
                Years = WholeMonths(r.Day, referenceDay) / 12,
            },
            "date_diff(");
    }

    [Fact]
    public Task Functions_JsonExtractScalar() =>
        AssertSameAsync(
            r => EF.Functions.JsonExtractScalar(r.Json, "$.a.b"),
            expected: r => r.Id switch { 1 => "5", 4 => "text", _ => null },
            "json_extract_scalar(");

    // ---- aggregates -----------------------------------------------------------------------

    [Fact]
    public async Task StringJoin_And_ApproxDistinct_PerGroup()
    {
        await using var db = await CreateContextAsync();

        var actual = await db.Rows.GroupBy(r => r.Flag).OrderBy(g => g.Key)
            .Select(g => new
            {
                g.Key,
                Ordered = string.Join(", ", g.OrderByDescending(r => r.Id).Select(r => r.Name)),
                Filtered = string.Join("|", g.Where(r => r.Count > 0).OrderBy(r => r.Id).Select(r => r.Note)),
                Distinct = string.Join(",", g.Select(r => r.Rating.HasValue ? "rated" : "unrated").Distinct().OrderBy(x => x)),
                Concat = string.Concat(g.OrderBy(r => r.Id).Select(r => r.Day.Year.ToString())),
                Ratings = EF.Functions.ApproxDistinct(g.Select(r => r.Rating)),
            })
            .ToListAsync();

        var expected = Rows.GroupBy(r => r.Flag).OrderBy(g => g.Key)
            .Select(g => new
            {
                g.Key,
                Ordered = string.Join(", ", g.OrderByDescending(r => r.Id).Select(r => r.Name)),
                Filtered = string.Join("|", g.Where(r => r.Count > 0).OrderBy(r => r.Id).Select(r => r.Note)),
                Distinct = string.Join(",", g.Select(r => r.Rating.HasValue ? "rated" : "unrated").Distinct().OrderBy(x => x, StringComparer.Ordinal)),
                Concat = string.Concat(g.OrderBy(r => r.Id).Select(r => r.Day.Year.ToString())),
                Ratings = (long)g.Where(r => r.Rating.HasValue).Select(r => r.Rating).Distinct().Count(),
            });

        Assert.Equal(expected, actual);
        AssertSqlContains("array_join(array_agg(", "approx_distinct(");
    }

    [Fact]
    public async Task StringJoin_OfAnEmptyGroupSelection_IsEmpty()
    {
        await using var db = await CreateContextAsync();

        var actual = await db.Rows.GroupBy(r => r.Flag).OrderBy(g => g.Key).Select(g => string.Join(",", g.Where(r => r.Id > 100).Select(r => r.Name))).ToListAsync();

        Assert.Equal([string.Empty, string.Empty], actual);
    }

    // ---- helpers --------------------------------------------------------------------------

    private static long WholeMonths(DateOnly start, DateOnly end)
    {
        var months = ((end.Year - start.Year) * 12) + end.Month - start.Month;
        if (months > 0 && end.Day < start.Day)
        {
            months--;
        }
        else if (months < 0 && end.Day > start.Day)
        {
            months++;
        }

        return months;
    }

    private async Task<FunctionsContext> CreateContextAsync() =>
        new(new DbContextOptionsBuilder<FunctionsContext>()
            .UseTrino(await FunctionsDatabase.GetConnectionStringAsync(fixture))
            .LogTo(
                (eventId, _) => eventId == RelationalEventId.CommandExecuting,
                eventData =>
                {
                    var sql = ((CommandEventData)eventData).Command.CommandText;
                    _sql.Add(sql);
                    output.WriteLine(sql);
                })
            .Options);

    private Task AssertSameAsync<T>(Expression<Func<FunctionRow, T>> selector, params string[] sqlFragments) =>
        AssertSameAsync(selector, filter: _ => true, sqlFragments);

    private async Task AssertSameAsync<T>(Expression<Func<FunctionRow, T>> selector, Expression<Func<FunctionRow, bool>> filter, params string[] sqlFragments)
    {
        await using var db = await CreateContextAsync();

        var actual = await db.Rows.Where(filter).OrderBy(r => r.Id).Select(selector).ToListAsync();

        Assert.Equal(Rows.Where(filter.Compile()).Select(selector.Compile()), actual);
        AssertSqlContains(sqlFragments);
    }

    private async Task AssertSameAsync<T>(Expression<Func<FunctionRow, T>> selector, Func<FunctionRow, T> expected, params string[] sqlFragments)
    {
        await using var db = await CreateContextAsync();

        var actual = await db.Rows.OrderBy(r => r.Id).Select(selector).ToListAsync();

        Assert.Equal(Rows.Select(expected), actual);
        AssertSqlContains(sqlFragments);
    }

    private async Task AssertCloseAsync(Expression<Func<FunctionRow, double[]>> selector, params string[] sqlFragments)
    {
        await using var db = await CreateContextAsync();

        var actual = await db.Rows.OrderBy(r => r.Id).Select(selector).ToListAsync();

        var expected = Rows.Select(selector.Compile()).ToList();
        Assert.Equal(expected.Count, actual.Count);
        for (var row = 0; row < expected.Count; row++)
        {
            for (var i = 0; i < expected[row].Length; i++)
            {
                var (e, a) = (expected[row][i], actual[row][i]);
                Assert.True(
                    e.Equals(a) || Math.Abs(e - a) <= 1e-12 * Math.Max(1, Math.Abs(e)),
                    $"Row {row + 1}, value {i}: expected {e:R}, actual {a:R}");
            }
        }

        AssertSqlContains(sqlFragments);
    }

    private Task AssertSameFilterAsync(Expression<Func<FunctionRow, bool>> predicate, params string[] sqlFragments) =>
        AssertSameFilterAsync(predicate, predicate.Compile(), sqlFragments);

    private async Task AssertSameFilterAsync(Expression<Func<FunctionRow, bool>> predicate, Func<FunctionRow, bool> expected, params string[] sqlFragments)
    {
        await using var db = await CreateContextAsync();

        var actual = await db.Rows.Where(predicate).OrderBy(r => r.Id).Select(r => r.Id).ToListAsync();

        Assert.Equal(Rows.Where(expected).Select(r => r.Id), actual);
        AssertSqlContains(sqlFragments);
    }

    private void AssertSqlContains(params string[] fragments)
    {
        var sql = Assert.Single(_sql);
        foreach (var fragment in fragments)
        {
            Assert.Contains(fragment, sql, StringComparison.Ordinal);
        }
    }
}
