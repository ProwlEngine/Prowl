// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

using static Prowl.Echo.Test.RoundtripTestHelpers;

namespace Prowl.Echo.Test;

// Round-trip edge cases for primitive and value-type payloads: special floats, integer extremes, decimals,
// unicode chars, tricky strings, dates, and nullables.
public class SpecialValues_Tests
{
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(float.Epsilon)]
    [InlineData(float.MaxValue)]
    [InlineData(float.MinValue)]
    [InlineData(-0.0f)]
    public void Float_SpecialValues_TextRoundtrip(float v) => Assert.Equal(v, RoundtripText(v));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.Epsilon)]
    [InlineData(double.MaxValue)]
    [InlineData(double.MinValue)]
    public void Double_SpecialValues_TextRoundtrip(double v) => Assert.Equal(v, RoundtripText(v));

    [Fact]
    public void Float_NegativeZero_PreservesSign() => Assert.True(float.IsNegative(Roundtrip(-0.0f)));

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    public void Long_Extremes_TextRoundtrip(long v) => Assert.Equal(v, RoundtripText(v));

    [Fact]
    public void ULong_Max_TextRoundtrip() => Assert.Equal(ulong.MaxValue, RoundtripText(ulong.MaxValue));

    [Fact]
    public void Decimal_HighPrecision_Roundtrip()
    {
        decimal v = 0.1234567890123456789012345678m;
        Assert.Equal(v, Roundtrip(v));
        Assert.Equal(v, RoundtripText(v));
    }

    [Fact]
    public void Decimal_NotConflatedWithDouble()
    {
        decimal v = 79228162514264337593543950335m; // decimal.MaxValue
        Assert.Equal(v, Roundtrip(v));
    }

    [Theory]
    [InlineData('中')] // CJK
    [InlineData('α')] // Greek alpha
    [InlineData('ÿ')] // last byte-safe codepoint (control)
    public void Char_HighCodepoint_Roundtrips(char c) => Assert.Equal(c, Roundtrip(c));

    [Theory]
    [InlineData("")]
    [InlineData(" leading/trailing ")]
    [InlineData("with \"quotes\" inside")]
    [InlineData("back\\slash")]
    [InlineData("line1\nline2\ttab")]
    [InlineData("null char\0here")]
    [InlineData("unicode é世界 emoji")]
    [InlineData("true")]
    [InlineData("123")]
    [InlineData("$id")]
    public void String_EdgeCases_TextRoundtrip(string s) => Assert.Equal(s, RoundtripText(s));

    [Theory]
    [InlineData("with \"quotes\"")]
    [InlineData("line1\nline2")]
    [InlineData("null\0char")]
    [InlineData("emoji \U0001F600")]
    public void String_EdgeCases_JsonRoundtrip(string s) => Assert.Equal(s, RoundtripJson(s));

    [Fact]
    public void DateTime_UtcKind_Preserved()
    {
        var dt = new DateTime(2020, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(DateTimeKind.Utc, Roundtrip(dt).Kind);
    }

    [Fact]
    public void DateTime_LocalKind_Preserved()
    {
        var dt = new DateTime(2020, 1, 1, 12, 0, 0, DateTimeKind.Local);
        Assert.Equal(DateTimeKind.Local, Roundtrip(dt).Kind);
    }

    [Fact]
    public void DateTimeOffset_PreservesOffset()
    {
        var dto = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.FromHours(5));
        Assert.Equal(TimeSpan.FromHours(5), Roundtrip(dto).Offset);
    }

    [Fact]
    public void TimeSpan_Negative_Roundtrips()
    {
        var ts = TimeSpan.FromSeconds(-1234.5);
        Assert.Equal(ts, Roundtrip(ts));
    }

    [Fact]
    public void NullableInt_Set_And_Null()
    {
        Assert.Equal(5, Roundtrip<int?>(5));
        Assert.Null(Roundtrip<int?>(null));
    }
}
