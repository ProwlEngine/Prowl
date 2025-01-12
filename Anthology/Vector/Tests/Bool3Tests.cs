// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Echo.Test;

public class Bool3Tests
{
    [Fact]
    public void Constructor_WithThreeValues_SetsComponentsCorrectly()
    {
        var bool3 = new Bool3(true, false, true);

        Assert.True(bool3.x);
        Assert.False(bool3.y);
        Assert.True(bool3.z);
    }

    [Fact]
    public void Constructor_WithSingleValue_SetsAllComponentsToValue()
    {
        var bool3True = new Bool3(true);
        Assert.True(bool3True.x && bool3True.y && bool3True.z);

        var bool3False = new Bool3(false);
        Assert.False(bool3False.x || bool3False.y || bool3False.z);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void Indexer_GetAndSet_WorksCorrectly(int index, bool value)
    {
        var bool3 = new Bool3(false);
        bool3[index] = value;
        Assert.Equal(value, bool3[index]);
    }

    [Fact]
    public void Indexer_InvalidIndex_ThrowsArgumentOutOfRangeException()
    {
        var bool3 = new Bool3();
        Assert.Throws<ArgumentOutOfRangeException>(() => bool3[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => bool3[3]);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, false, false)]
    public void Any_ReturnsExpectedResult(bool x, bool y, bool z)
    {
        var bool3 = new Bool3(x, y, z);
        Assert.Equal(x || y || z, bool3.Any());
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    public void All_ReturnsExpectedResult(bool x, bool y, bool z)
    {
        var bool3 = new Bool3(x, y, z);
        Assert.Equal(x && y && z, bool3.All());
    }

    [Theory]
    [InlineData(true, true, true, 3)]
    [InlineData(true, false, true, 2)]
    [InlineData(false, false, false, 0)]
    [InlineData(true, false, false, 1)]
    public void CountTrue_ReturnsCorrectCount(bool x, bool y, bool z, int expectedCount)
    {
        var bool3 = new Bool3(x, y, z);
        Assert.Equal(expectedCount, bool3.CountTrue());
    }

    [Fact]
    public void SetAll_SetsAllComponentsToSpecifiedValue()
    {
        var bool3 = new Bool3(true, false, true);

        bool3.SetAll(false);
        Assert.False(bool3.x || bool3.y || bool3.z);

        bool3.SetAll(true);
        Assert.True(bool3.x && bool3.y && bool3.z);
    }

    [Fact]
    public void ToArray_ReturnsCorrectArray()
    {
        var bool3 = new Bool3(true, false, true);
        var array = bool3.ToArray();

        Assert.Equal(3, array.Length);
        Assert.Equal(bool3.x, array[0]);
        Assert.Equal(bool3.y, array[1]);
        Assert.Equal(bool3.z, array[2]);
    }

    [Fact]
    public void OperatorAnd_ReturnsExpectedResult()
    {
        var a = new Bool3(true, false, true);
        var b = new Bool3(true, true, false);
        var result = a & b;

        Assert.True(result.x);
        Assert.False(result.y);
        Assert.False(result.z);
    }

    [Fact]
    public void OperatorOr_ReturnsExpectedResult()
    {
        var a = new Bool3(true, false, true);
        var b = new Bool3(false, true, false);
        var result = a | b;

        Assert.True(result.x);
        Assert.True(result.y);
        Assert.True(result.z);
    }

    [Fact]
    public void OperatorNot_ReturnsExpectedResult()
    {
        var bool3 = new Bool3(true, false, true);
        var result = !bool3;

        Assert.False(result.x);
        Assert.True(result.y);
        Assert.False(result.z);
    }

    [Fact]
    public void Equality_WorksCorrectly()
    {
        var a = new Bool3(true, false, true);
        var b = new Bool3(true, false, true);
        var c = new Bool3(false, true, false);

        Assert.True(a == b);
        Assert.False(a == c);
        Assert.True(a.Equals(b));
        Assert.False(a.Equals(c));
    }

    [Fact]
    public void ToString_ReturnsCorrectFormat()
    {
        var bool3 = new Bool3(true, false, true);
        Assert.Equal("(True, False, True)", bool3.ToString());
    }
}
