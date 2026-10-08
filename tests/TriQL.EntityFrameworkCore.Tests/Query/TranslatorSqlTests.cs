using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TriQL.EntityFrameworkCore.Tests.TestUtilities;

// The queries call culture-sensitive string APIs on purpose: they are the LINQ shapes users write, and
// they are translated to SQL, never run in .NET.
#pragma warning disable CA1304, CA1305, CA1307, CA1309, CA1310, CA1311, CA1862

namespace TriQL.EntityFrameworkCore.Tests.Query;

/// <summary>SQL baselines for the function and member translators (Phase 4): one per translation.</summary>
public sealed class TranslatorSqlTests
{
    private const string From = "FROM \"Rows\" AS \"r\"";

    // ---- string ---------------------------------------------------------------------------

    [Fact]
    public Task String_Length() => AssertProjection(r => r.Name.Length, "length(\"r\".\"Name\")");

    [Fact]
    public Task String_ToUpper_ToLower() =>
        AssertProjection(r => r.Name.ToUpper() + r.Name.ToLowerInvariant(), "upper(\"r\".\"Name\") || lower(\"r\".\"Name\")");

    [Fact]
    public Task String_Trim_NoArguments() =>
        AssertProjection(r => new { A = r.Name.Trim(), B = r.Name.TrimStart(), C = r.Name.TrimEnd() }, "trim(\"r\".\"Name\") AS \"A\", ltrim(\"r\".\"Name\") AS \"B\", rtrim(\"r\".\"Name\") AS \"C\"");

    [Fact]
    public Task String_Trim_Char() =>
        AssertProjection(r => new { A = r.Name.Trim('x'), B = r.Name.TrimStart('x'), C = r.Name.TrimEnd('x') }, "trim(\"r\".\"Name\", 'x') AS \"A\", ltrim(\"r\".\"Name\", 'x') AS \"B\", rtrim(\"r\".\"Name\", 'x') AS \"C\"");

    [Fact]
    public Task String_Substring_IsOneBased() =>
        AssertProjection(r => r.Name.Substring(r.Count, 2), "substr(\"r\".\"Name\", \"r\".\"Count\" + 1, 2)");

    [Fact]
    public Task String_Substring_StartOnly() => AssertProjection(r => r.Name.Substring(1), "substr(\"r\".\"Name\", 1 + 1)");

    [Fact]
    public Task String_IndexOf() =>
        AssertProjection(r => r.Name.IndexOf("ab") + r.Name.IndexOf('c'), "(strpos(\"r\".\"Name\", 'ab') - 1) + (strpos(\"r\".\"Name\", 'c') - 1)");

    [Fact]
    public Task String_Replace() => AssertProjection(r => r.Name.Replace("a", "b"), "replace(\"r\".\"Name\", 'a', 'b')");

    [Fact]
    public Task String_Contains_Constant_IsLike_EscapedOnlyWhenNeeded() =>
        AssertFilter(r => r.Name.Contains("ab") && r.Name.Contains("5%_"), "(\"r\".\"Name\" LIKE '%ab%') AND (\"r\".\"Name\" LIKE '%5\\%\\_%' ESCAPE '\\')");

    [Fact]
    public Task String_StartsWith_EndsWith_Constant_AreLike() =>
        AssertFilter(r => r.Name.StartsWith("a\\b") && r.Name.EndsWith('z'), "(\"r\".\"Name\" LIKE 'a\\\\b%' ESCAPE '\\') AND (\"r\".\"Name\" LIKE '%z')");

    [Fact]
    public async Task String_Contains_StartsWith_EndsWith_Parameter_AreFunctions()
    {
        var value = "a%";
        await AssertFilter(
            r => r.Name.Contains(value) && r.Name.StartsWith(value) && r.Name.EndsWith(value),
            "strpos(\"r\".\"Name\", @value) > 0 AND starts_with(\"r\".\"Name\", @value) AND starts_with(reverse(\"r\".\"Name\"), reverse(@value))");
    }

    [Fact]
    public Task String_IsNullOrWhiteSpace() =>
        AssertFilter(r => string.IsNullOrWhiteSpace(r.Note), "\"r\".\"Note\" IS NULL OR trim(\"r\".\"Note\") = ''");

    [Fact]
    public Task String_IsNullOrEmpty() =>
        AssertFilter(r => string.IsNullOrEmpty(r.Note), "\"r\".\"Note\" IS NULL OR \"r\".\"Note\" = ''");

    [Fact]
    public Task Regex_IsMatch() =>
        AssertFilter(r => Regex.IsMatch(r.Name, "^a") && Regex.IsMatch(r.Name, "b$", RegexOptions.IgnoreCase), "regexp_like(\"r\".\"Name\", '^a') AND regexp_like(\"r\".\"Name\", '(?i)' || 'b$')");

    // ---- math -----------------------------------------------------------------------------

    [Fact]
    public Task Math_SameTypeFunctions() =>
        AssertProjection(
            r => new { A = Math.Abs(r.Count), B = Math.Ceiling(r.Amount), C = Math.Floor(r.Ratio), D = Math.Truncate(r.Amount), E = Math.Max(r.Count, 3), F = Math.Min(r.Ratio, 0.5) },
            "abs(\"r\".\"Count\") AS \"A\", ceiling(\"r\".\"Amount\") AS \"B\", floor(\"r\".\"Ratio\") AS \"C\", truncate(\"r\".\"Amount\") AS \"D\", greatest(\"r\".\"Count\", 3) AS \"E\", least(\"r\".\"Ratio\", DOUBLE '0.5') AS \"F\"");

    [Fact]
    public Task Math_DoubleFunctions() =>
        AssertProjection(
            r => Math.Sqrt(r.Ratio) + Math.Pow(r.Ratio, 2) + Math.Log(r.Ratio) + Math.Log(r.Ratio, 2) + Math.Atan2(r.Ratio, 1),
            "(((sqrt(\"r\".\"Ratio\") + power(\"r\".\"Ratio\", DOUBLE '2')) + ln(\"r\".\"Ratio\")) + log(DOUBLE '2', \"r\".\"Ratio\")) + atan2(\"r\".\"Ratio\", DOUBLE '1')");

    [Fact]
    public Task Math_Sign_CastsToInteger() => AssertProjection(r => Math.Sign(r.Amount), "CAST(sign(\"r\".\"Amount\") AS integer)");

    [Fact]
    public Task Math_Round_AwayFromZero_IsRound() =>
        AssertProjection(
            r => new { A = Math.Round(r.Ratio, MidpointRounding.AwayFromZero), B = Math.Round(r.Amount, 1, MidpointRounding.AwayFromZero) },
            "round(\"r\".\"Ratio\") AS \"A\", round(\"r\".\"Amount\", 1) AS \"B\"");

    [Fact]
    public Task Math_Round_Default_EmulatesHalfToEven() =>
        AssertProjection(
            r => Math.Round(r.Ratio),
            "CASE\n    WHEN abs(\"r\".\"Ratio\" - round(\"r\".\"Ratio\")) = DOUBLE '0.5' AND mod(round(\"r\".\"Ratio\"), DOUBLE '2') <> DOUBLE '0' THEN round(\"r\".\"Ratio\") - sign(\"r\".\"Ratio\")\n    ELSE round(\"r\".\"Ratio\")\nEND");

    [Fact]
    public Task Math_Round_DecimalDigits_EmulatesHalfToEven() =>
        AssertProjection(
            r => Math.Round(r.Amount, 1),
            "CASE\n    WHEN abs(\"r\".\"Amount\" - round(\"r\".\"Amount\", 1)) = DECIMAL '0.05' AND mod(round(\"r\".\"Amount\", 1) * DECIMAL '10', DECIMAL '2') <> DECIMAL '0' THEN round(\"r\".\"Amount\", 1) - (sign(\"r\".\"Amount\") * DECIMAL '0.1')\n    ELSE round(\"r\".\"Amount\", 1)\nEND");

    [Fact]
    public async Task Math_Round_DoubleDigits_HalfToEven_IsNotTranslated()
    {
        using var fake = new FakeTrino();
        await using var db = new FunctionsContext(fake.CreateOptions<FunctionsContext>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Rows.Where(r => Math.Round(r.Ratio, 2) > 1).ToListAsync());
    }

    // ---- date and time --------------------------------------------------------------------

    [Fact]
    public Task DateTime_Parts() =>
        AssertProjection(
            r => new { r.Created.Year, r.Created.Month, r.Created.Day, r.Created.Hour, r.Created.Minute, r.Created.Second, r.Created.Millisecond, r.Created.DayOfYear },
            "year(\"r\".\"Created\") AS \"Year\", month(\"r\".\"Created\") AS \"Month\", day(\"r\".\"Created\") AS \"Day\", hour(\"r\".\"Created\") AS \"Hour\", minute(\"r\".\"Created\") AS \"Minute\", second(\"r\".\"Created\") AS \"Second\", millisecond(\"r\".\"Created\") AS \"Millisecond\", day_of_year(\"r\".\"Created\") AS \"DayOfYear\"");

    [Fact]
    public Task DateTime_DayOfWeek_IsSundayZero() =>
        AssertProjection(r => r.Created.DayOfWeek, "CAST(day_of_week(\"r\".\"Created\") % 7 AS integer)");

    [Fact]
    public Task DateTime_Date() => AssertProjection(r => r.Created.Date, "date_trunc('day', \"r\".\"Created\")");

    [Fact]
    public Task DateTime_Now_UtcNow_Today() =>
        AssertProjection(
            r => new { Now = DateTime.Now, DateTime.UtcNow, Today = DateTime.Today },
            "localtimestamp(6) AS \"Now\", CAST(current_timestamp(6) AT TIME ZONE 'UTC' AS timestamp(6)) AS \"UtcNow\", CAST(current_date AS timestamp(6)) AS \"Today\"");

    [Fact]
    public Task DateTimeOffset_Now_UtcNow() =>
        AssertProjection(r => new { DateTimeOffset.Now, DateTimeOffset.UtcNow }, "current_timestamp(6) AS \"Now\", current_timestamp(6) AT TIME ZONE 'UTC' AS \"UtcNow\"");

    [Fact]
    public Task DateTimeOffset_Members() =>
        AssertProjection(
            r => new { r.Stamp.Year, r.Stamp.DateTime, r.Stamp.UtcDateTime, r.Stamp.Date },
            "year(\"r\".\"Stamp\") AS \"Year\", CAST(\"r\".\"Stamp\" AS timestamp(6)) AS \"DateTime\", CAST(\"r\".\"Stamp\" AT TIME ZONE 'UTC' AS timestamp(6)) AS \"UtcDateTime\", date_trunc('day', CAST(\"r\".\"Stamp\" AS timestamp(6))) AS \"Date\"");

    [Fact]
    public Task DateOnly_And_TimeOnly_Members() =>
        AssertProjection(
            r => new { r.Day.Year, r.Day.DayOfWeek, r.Time.Hour, r.Time.Millisecond, FromDateTime = DateOnly.FromDateTime(r.Created) },
            "year(\"r\".\"Day\") AS \"Year\", CAST(day_of_week(\"r\".\"Day\") % 7 AS integer) AS \"DayOfWeek\", hour(\"r\".\"Time\") AS \"Hour\", millisecond(\"r\".\"Time\") AS \"Millisecond\", CAST(\"r\".\"Created\" AS date) AS \"FromDateTime\"");

    [Fact]
    public Task DateTime_Add_WholeUnits() =>
        AssertProjection(
            r => new { A = r.Created.AddYears(1), B = r.Created.AddMonths(r.Count), C = r.Created.AddDays(2), D = r.Created.AddHours(r.Count), E = r.Day.AddDays(3) },
            "date_add('year', 1, \"r\".\"Created\") AS \"A\", date_add('month', \"r\".\"Count\", \"r\".\"Created\") AS \"B\", date_add('day', 2, \"r\".\"Created\") AS \"C\", date_add('hour', \"r\".\"Count\", \"r\".\"Created\") AS \"D\", date_add('day', 3, \"r\".\"Day\") AS \"E\"");

    [Fact]
    public Task DateTime_Add_FractionalAmount_UsesMilliseconds() =>
        AssertProjection(
            r => new { A = r.Created.AddDays(r.Ratio), B = r.Stamp.AddMilliseconds(r.Ratio) },
            "date_add('millisecond', CAST(\"r\".\"Ratio\" * DOUBLE '86400000' AS bigint), \"r\".\"Created\") AS \"A\", date_add('millisecond', CAST(\"r\".\"Ratio\" AS bigint), \"r\".\"Stamp\") AS \"B\"");

    // ---- conversions, ToString, Guid, byte[] ----------------------------------------------

    [Fact]
    public Task Convert_IsCast() =>
        AssertProjection(
            r => new { A = Convert.ToInt64(r.Count), B = Convert.ToDouble(r.Amount), C = Convert.ToInt32(r.Name) },
            "CAST(\"r\".\"Count\" AS bigint) AS \"A\", CAST(\"r\".\"Amount\" AS double) AS \"B\", CAST(\"r\".\"Name\" AS integer) AS \"C\"");

    [Fact]
    public Task ToString_CastsToVarchar_CoalescingNullableColumns() =>
        AssertProjection(
            r => new { A = r.Count.ToString(), B = r.Rating.ToString(), C = r.Key.ToString(), D = r.Amount.ToString() },
            "CAST(\"r\".\"Count\" AS varchar) AS \"A\", COALESCE(CAST(\"r\".\"Rating\" AS varchar), '') AS \"B\", CAST(\"r\".\"Key\" AS varchar) AS \"C\", CAST(\"r\".\"Amount\" AS varchar) AS \"D\"");

    [Fact]
    public Task ToString_OfBool_UsesDotNetSpelling() =>
        AssertProjection(
            r => new { A = r.Flag.ToString(), B = r.MaybeFlag.ToString() },
            "CASE\n    WHEN \"r\".\"Flag\" THEN 'True'\n    ELSE 'False'\nEND AS \"A\", CASE\n    WHEN \"r\".\"MaybeFlag\" THEN 'True'\n    WHEN \"r\".\"MaybeFlag\" = FALSE THEN 'False'\n    ELSE ''\nEND AS \"B\"");

    [Fact]
    public async Task ToString_OfDouble_IsNotTranslated()
    {
        using var fake = new FakeTrino();
        await using var db = new FunctionsContext(fake.CreateOptions<FunctionsContext>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Rows.Where(r => r.Ratio.ToString() == "1.5").ToListAsync());
    }

    [Fact]
    public Task Guid_NewGuid() => AssertProjection(r => Guid.NewGuid(), "uuid()");

    [Fact]
    public Task ByteArray_Length() => AssertProjection(r => r.Data.Length, "length(\"r\".\"Data\")");

    // ---- EF.Functions ---------------------------------------------------------------------

    [Fact]
    public Task Functions_Like_And_ILike() =>
        AssertFilter(
            r => EF.Functions.Like(r.Name, "a%") && EF.Functions.ILike(r.Name, "B%") && EF.Functions.ILike(r.Name, "c!%%", "!"),
            "(\"r\".\"Name\" LIKE 'a%') AND (lower(\"r\".\"Name\") LIKE lower('B%')) AND (lower(\"r\".\"Name\") LIKE lower('c!%%') ESCAPE '!')");

    [Fact]
    public Task Functions_DateDiff() =>
        AssertProjection(
            r => new { A = EF.Functions.DateDiffDay(r.Created, DateTime.UtcNow), B = EF.Functions.DateDiffMonth(r.Day, r.Day), C = EF.Functions.DateDiffSecond(r.Stamp, r.Stamp) },
            "date_diff('day', \"r\".\"Created\", CAST(current_timestamp(6) AT TIME ZONE 'UTC' AS timestamp(6))) AS \"A\", date_diff('month', \"r\".\"Day\", \"r\".\"Day\") AS \"B\", date_diff('second', \"r\".\"Stamp\", \"r\".\"Stamp\") AS \"C\"");

    [Fact]
    public Task Functions_JsonExtractScalar() =>
        AssertProjection(r => EF.Functions.JsonExtractScalar(r.Note, "$.a"), "json_extract_scalar(\"r\".\"Note\", '$.a')");

    // ---- aggregates -----------------------------------------------------------------------

    [Fact]
    public async Task StringJoin_OrderedAndFiltered_IsArrayJoinOfArrayAgg()
    {
        using var fake = new FakeTrino();
        await using var db = new FunctionsContext(fake.CreateOptions<FunctionsContext>());
        fake.EnqueueRows([("Key", "integer"), ("Names", "varchar")]);

        await db.Rows.GroupBy(r => r.Count)
            .Select(g => new { g.Key, Names = string.Join(", ", g.Where(r => r.Flag).OrderByDescending(r => r.Id).Select(r => r.Note)) })
            .ToListAsync();

        fake.AssertSql(
            """
            SELECT "r"."Count" AS "Key", COALESCE(array_join(array_agg(CASE
                WHEN "r"."Flag" THEN COALESCE("r"."Note", '')
            END ORDER BY "r"."Id" DESC), ', '), '') AS "Names"
            FROM "Rows" AS "r"
            GROUP BY "r"."Count"
            """);
    }

    [Fact]
    public async Task StringJoin_Distinct_And_StringConcat()
    {
        using var fake = new FakeTrino();
        await using var db = new FunctionsContext(fake.CreateOptions<FunctionsContext>());
        fake.EnqueueRows([("A", "varchar"), ("B", "varchar")]);

        await db.Rows.GroupBy(r => r.Count)
            .Select(g => new { A = string.Join("|", g.Select(r => r.Name).Distinct()), B = string.Concat(g.Select(r => r.Name)) })
            .ToListAsync();

        fake.AssertSql(
            """
            SELECT COALESCE(array_join(array_agg(DISTINCT ("r"."Name")), '|'), '') AS "A", COALESCE(array_join(array_agg("r"."Name"), ''), '') AS "B"
            FROM "Rows" AS "r"
            GROUP BY "r"."Count"
            """);
    }

    [Fact]
    public async Task ApproxDistinct_IsApproxDistinct()
    {
        using var fake = new FakeTrino();
        await using var db = new FunctionsContext(fake.CreateOptions<FunctionsContext>());
        fake.EnqueueRows([("c", "bigint")]);

        await db.Rows.GroupBy(r => r.Count).Select(g => EF.Functions.ApproxDistinct(g.Select(r => r.Name))).ToListAsync();

        fake.AssertSql(
            """
            SELECT approx_distinct("r"."Name")
            FROM "Rows" AS "r"
            GROUP BY "r"."Count"
            """);
    }

    [Fact]
    public void DbFunctions_ThrowOnClientEvaluation() =>
        Assert.Throws<InvalidOperationException>(() => EF.Functions.ILike("a", "a"));

    // ---- helpers --------------------------------------------------------------------------

    /// <summary>Asserts the SQL of <c>Rows.Select(selector)</c>: <c>SELECT {expected} FROM "Rows" AS "r"</c>.</summary>
    private static async Task AssertProjection<T>(Expression<Func<FRow, T>> selector, string expectedProjection)
    {
        using var fake = new FakeTrino();
        await using var db = new FunctionsContext(fake.CreateOptions<FunctionsContext>());
        fake.EnqueueRows([("c", "varchar")]);

        await db.Rows.Select(selector).ToListAsync();

        AssertSingleSql(fake, $"SELECT {expectedProjection}\n{From}");
    }

    /// <summary>Asserts the SQL of <c>Rows.Where(predicate).Select(r =&gt; r.Id)</c>.</summary>
    private static async Task AssertFilter(Expression<Func<FRow, bool>> predicate, string expectedWhere)
    {
        using var fake = new FakeTrino();
        await using var db = new FunctionsContext(fake.CreateOptions<FunctionsContext>());
        fake.EnqueueRows([("Id", "integer")]);

        await db.Rows.Where(predicate).Select(r => r.Id).ToListAsync();

        AssertSingleSql(fake, $"SELECT \"r\".\"Id\"\n{From}\nWHERE {expectedWhere}");
    }

    /// <summary>Like <see cref="FakeTrino.AssertSql"/>, but a mismatch shows both statements in full.</summary>
    private static void AssertSingleSql(FakeTrino fake, string expected)
    {
        var actual = Assert.Single(fake.Sql).ReplaceLineEndings("\n").Trim();
        if (actual != expected)
        {
            Assert.Fail($"Expected:\n{expected}\n\nActual:\n{actual}");
        }
    }
}
