// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

namespace Prowl.Rosetta.Tests;

public class PluralRulesTests
{
    // PluralRules is internal, so we call it via reflection
    private static string GetCategory(string locale, int count)
    {
        var method = typeof(PluralRules).GetMethod("GetCategory", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        return (string)method.Invoke(null, new object[] { locale, count })!;
    }

    [Theory]
    [InlineData("en", 0, "other")]
    [InlineData("en", 1, "one")]
    [InlineData("en", 2, "other")]
    [InlineData("en", 100, "other")]
    public void English(string locale, int count, string expected)
    {
        Assert.Equal(expected, GetCategory(locale, count));
    }

    [Theory]
    [InlineData("fr", 0, "one")]
    [InlineData("fr", 1, "one")]
    [InlineData("fr", 2, "other")]
    [InlineData("fr", 100, "other")]
    public void French(string locale, int count, string expected)
    {
        Assert.Equal(expected, GetCategory(locale, count));
    }

    [Theory]
    [InlineData("ru", 1, "one")]
    [InlineData("ru", 2, "few")]
    [InlineData("ru", 3, "few")]
    [InlineData("ru", 4, "few")]
    [InlineData("ru", 5, "many")]
    [InlineData("ru", 11, "many")]
    [InlineData("ru", 12, "many")]
    [InlineData("ru", 14, "many")]
    [InlineData("ru", 21, "one")]
    [InlineData("ru", 22, "few")]
    [InlineData("ru", 25, "many")]
    [InlineData("ru", 101, "one")]
    [InlineData("ru", 111, "many")]
    [InlineData("ru", 112, "many")]
    public void Russian(string locale, int count, string expected)
    {
        Assert.Equal(expected, GetCategory(locale, count));
    }

    [Theory]
    [InlineData("ar", 0, "zero")]
    [InlineData("ar", 1, "one")]
    [InlineData("ar", 2, "two")]
    [InlineData("ar", 3, "few")]
    [InlineData("ar", 10, "few")]
    [InlineData("ar", 11, "many")]
    [InlineData("ar", 99, "many")]
    [InlineData("ar", 100, "other")]
    public void Arabic(string locale, int count, string expected)
    {
        Assert.Equal(expected, GetCategory(locale, count));
    }

    [Theory]
    [InlineData("pl", 1, "one")]
    [InlineData("pl", 2, "few")]
    [InlineData("pl", 3, "few")]
    [InlineData("pl", 4, "few")]
    [InlineData("pl", 5, "many")]
    [InlineData("pl", 12, "many")]
    [InlineData("pl", 22, "few")]
    [InlineData("pl", 25, "many")]
    public void Polish(string locale, int count, string expected)
    {
        Assert.Equal(expected, GetCategory(locale, count));
    }

    [Theory]
    [InlineData("ja", 0, "other")]
    [InlineData("ja", 1, "other")]
    [InlineData("ja", 42, "other")]
    [InlineData("zh", 1, "other")]
    [InlineData("ko", 1, "other")]
    public void EastAsian_AlwaysOther(string locale, int count, string expected)
    {
        Assert.Equal(expected, GetCategory(locale, count));
    }

    [Theory]
    [InlineData("cs", 1, "one")]
    [InlineData("cs", 2, "few")]
    [InlineData("cs", 4, "few")]
    [InlineData("cs", 5, "other")]
    [InlineData("cs", 100, "other")]
    public void Czech(string locale, int count, string expected)
    {
        Assert.Equal(expected, GetCategory(locale, count));
    }

    [Theory]
    [InlineData("ro", 1, "one")]
    [InlineData("ro", 0, "few")]
    [InlineData("ro", 2, "few")]
    [InlineData("ro", 19, "few")]
    [InlineData("ro", 20, "other")]
    [InlineData("ro", 100, "other")]
    public void Romanian(string locale, int count, string expected)
    {
        Assert.Equal(expected, GetCategory(locale, count));
    }

    [Fact]
    public void LocaleWithRegion_UsesLanguageCode()
    {
        // "en-US" should resolve to "en" rules
        Assert.Equal("one", GetCategory("en-US", 1));
        Assert.Equal("other", GetCategory("en-US", 2));
        Assert.Equal("one", GetCategory("fr-CA", 0));
    }

    [Fact]
    public void UnknownLocale_FallsBackToEnglishLike()
    {
        Assert.Equal("one", GetCategory("xx", 1));
        Assert.Equal("other", GetCategory("xx", 2));
    }
}
