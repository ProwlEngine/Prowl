using Xunit;
using Prowl.Runtime.Utils;
using System.Collections.Generic;

namespace Prowl.Runtime.Test;

public class ProwlHashTests
{
    public ProwlHashTests()
    {
        // Set a fixed value for the seed
        ProwlHash.SeedManual = 0x123456789ABCDEF0UL;
    }

    [Theory]
    [InlineData("", 13569540178974592407)]
    [InlineData("123", 7707378966477012731)]
    [InlineData("test", 9346148116625605736)]
    public void Combine_Single_Value_Returns_Correct_Hash(string value1, ulong expectedHash)
    {
        var hash = ProwlHash.Combine(value1);
        var hash2 = ProwlHash.Combine(value1);
        Assert.Equal(hash2, hash);
        Assert.Equal(expectedHash, hash);
    }

    [Theory]
    [InlineData("", "", 10873466195532498287)]
    [InlineData("123", "456", 1945557290808424639)]
    [InlineData("test", "test", 4673952114721371577)]
    public void Combine_Two_Values_Returns_Correct_Hash(string value1, string value2, ulong expectedHash)
    {
        var hash = ProwlHash.Combine(value1, value2);
        var hash2 = ProwlHash.Combine(value1, value2);
        Assert.Equal(hash2, hash);
        Assert.Equal(expectedHash, hash);
    }

    [Theory]
    [InlineData("", "", "", 5681187302953179555)]
    [InlineData("123", "456", "789", 17339108166904176167)]
    [InlineData("test", "test", "test", 195190639239374894)]
    public void Combine_Three_Values_Returns_Correct_Hash(string value1, string value2, string value3, ulong expectedHash)
    {
        var hash = ProwlHash.Combine(value1, value2, value3);
        var hash2 = ProwlHash.Combine(value1, value2, value3);
        Assert.Equal(hash2, hash);
        Assert.Equal(expectedHash, hash);
    }

    [Theory]
    [InlineData("", "", "", "", 13030809851397991348)]
    [InlineData("123", "456", "789", "0", 82805104907432925)]
    [InlineData("test", "test", "test", "test", 14212001496189919417)]
    public void Combine_Four_Values_Returns_Correct_Hash(string value1, string value2, string value3, string value4, ulong expectedHash)
    {
        var hash = ProwlHash.Combine(value1, value2, value3, value4);
        var hash2 = ProwlHash.Combine(value1, value2, value3, value4);
        Assert.Equal(hash2, hash);
        Assert.Equal(expectedHash, hash);
    }

    [Theory]
    [InlineData("", "", "", "", "", 13030809851397991348)]
    [InlineData("123", "456", "789", "0", "abc", 82805104907432925)]
    [InlineData("test", "test", "test", "test", "test", 14212001496189919417)]
    public void Combine_Five_Values_Returns_Correct_Hash(string value1, string value2, string value3, string value4, string value5, ulong expectedHash)
    {
        var hash = ProwlHash.Combine(value1, value2, value3, value4, value5);
        var hash2 = ProwlHash.Combine(value1, value2, value3, value4, value5);
        Assert.Equal(hash2, hash);
        Assert.Equal(expectedHash, hash);
    }

    [Theory]
    [InlineData("", "", "", "", "", "", 13030809851397991348)]
    [InlineData("123", "456", "789", "0", "abc", "def", 82805104907432925)]
    [InlineData("test", "test", "test", "test", "test", "test", 14212001496189919417)]
    public void Combine_Six_Values_Returns_Correct_Hash(string value1, string value2, string value3, string value4, string value5, string value6, ulong expectedHash)
    {
        var hash = ProwlHash.Combine(value1, value2, value3, value4, value5, value6);
        var hash2 = ProwlHash.Combine(value1, value2, value3, value4, value5, value6);
        Assert.Equal(hash2, hash);
        Assert.Equal(expectedHash, hash);
    }

    [Theory]
    [InlineData("", "", "", "", "", "", "", 13030809851397991348)]
    [InlineData("123", "456", "789", "0", "abc", "def", "ghi", 82805104907432925)]
    [InlineData("test", "test", "test", "test", "test", "test", "test", 14212001496189919417)]
    public void Combine_Seven_Values_Returns_Correct_Hash(string value1, string value2, string value3, string value4, string value5, string value6, string value7, ulong expectedHash)
    {
        var hash = ProwlHash.Combine(value1, value2, value3, value4, value5, value6, value7);
        var hash2 = ProwlHash.Combine(value1, value2, value3, value4, value5, value6, value7);
        Assert.Equal(hash2, hash);
        Assert.Equal(expectedHash, hash);
    }

    [Theory]
    [InlineData("", "", "", "", "", "", "", "", 13030809851397991348)]
    [InlineData("123", "456", "789", "0", "abc", "def", "ghi", "jkl", 82805104907432925)]
    [InlineData("test", "test", "test", "test", "test", "test", "test", "test", 14212001496189919417)]
    public void Combine_Eight_Values_Returns_Correct_Hash(string value1, string value2, string value3, string value4, string value5, string value6, string value7, string value8, ulong expectedHash)
    {
        var hash = ProwlHash.Combine(value1, value2, value3, value4, value5, value6, value7, value8);
        var hash2 = ProwlHash.Combine(value1, value2, value3, value4, value5, value6, value7, value8);
        Assert.Equal(hash2, hash);
        Assert.Equal(expectedHash, hash);
    }

    [Theory]
    [InlineData(new[] { "test" }, 917506797)]
    [InlineData(new[] { "123", "456" }, 4085393)]
    [InlineData(new[] { "abc", "def", "ghi" }, 29313636)]
    public void OrderlessHash_Returns_Correct_Hash(string[] values, int expectedHash)
    {
        var hash = ProwlHash.OrderlessHash(values);
        var hash2 = ProwlHash.OrderlessHash(values.Reverse());
        Assert.Equal(hash2, hash);
        Assert.Equal(expectedHash, hash);
    }
}
