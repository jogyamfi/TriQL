using System.Numerics;
using TriQL.Client.Types;

namespace TriQL.Client.Tests.Types;

public sealed class TrinoBigDecimalTests
{
    [Fact]
    public void Equals_IgnoresTrailingZeroScaleDifferences()
    {
        Assert.Equal(TrinoBigDecimal.Parse("1.0"), TrinoBigDecimal.Parse("1.00"));
    }

    [Fact]
    public void GetHashCode_AgreesWithEquals()
    {
        var a = TrinoBigDecimal.Parse("1.0");
        var b = TrinoBigDecimal.Parse("1.00");

        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void GetHashCode_AgreesWithEqualsForZero()
    {
        Assert.Equal(TrinoBigDecimal.Parse("0.00").GetHashCode(), TrinoBigDecimal.Parse("0").GetHashCode());
    }

    [Fact]
    public void CompareTo_OrdersAcrossDifferentScales()
    {
        Assert.True(TrinoBigDecimal.Parse("1.5") > TrinoBigDecimal.Parse("1.4999"));
        Assert.True(TrinoBigDecimal.Parse("-2.5") < TrinoBigDecimal.Parse("-2.4"));
    }

    [Fact]
    public void ExplicitDecimalConversion_LosslessValue_Succeeds()
    {
        Assert.Equal(123.45m, (decimal)TrinoBigDecimal.Parse("123.45"));
    }

    [Fact]
    public void ExplicitDecimalConversion_PrecisionLoss_ThrowsOverflowException()
    {
        var value = TrinoBigDecimal.Parse("1.23456789012345678901234567890123");

        Assert.Throws<OverflowException>(() => (decimal)value);
    }

    [Fact]
    public void ExplicitDecimalConversion_MagnitudeOverflow_ThrowsOverflowException()
    {
        var value = TrinoBigDecimal.Parse("123456789012345678901234567890123456789");

        Assert.Throws<OverflowException>(() => (decimal)value);
    }

    [Fact]
    public void RoundTrip_ParseThenToString_PreservesScale()
    {
        Assert.Equal("0.0100", TrinoBigDecimal.Parse("0.0100").ToString());
        Assert.Equal("-1.5", TrinoBigDecimal.Parse("-1.5").ToString());
    }

    [Fact]
    public void Constructor_NegativeScale_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrinoBigDecimal(BigInteger.One, -1));
    }

    [Fact]
    public void TryParse_RejectsNonNumericText()
    {
        Assert.False(TrinoBigDecimal.TryParse("abc", null, out _));
    }
}
