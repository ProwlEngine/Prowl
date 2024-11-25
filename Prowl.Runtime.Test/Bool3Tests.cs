// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

namespace Prowl.Runtime.Test;
public class Bool3Tests
{
    [Fact]
    public void Constructor_Default_AllComponentsFalse()
    {
        var b = new Bool3();
        Assert.False(b.x);
        Assert.False(b.y);
        Assert.False(b.z);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Constructor_SingleValue_AllComponentsSet(bool value)
    {
        var b = new Bool3(value);
        Assert.Equal(value, b.x);
        Assert.Equal(value, b.y);
        Assert.Equal(value, b.z);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    public void Constructor_ThreeValues_ComponentsSetCorrectly(bool x, bool y, bool z)
    {
        var b = new Bool3(x, y, z);
        Assert.Equal(x, b.x);
        Assert.Equal(y, b.y);
        Assert.Equal(z, b.z);
    }

    [Fact]
    public void Properties_SetAndGet_WorksCorrectly()
    {
        var b = new Bool3();

        b.x = true;
        Assert.True(b.x);
        Assert.False(b.y);
        Assert.False(b.z);

        b.y = true;
        Assert.True(b.x);
        Assert.True(b.y);
        Assert.False(b.z);

        b.z = true;
        Assert.True(b.x);
        Assert.True(b.y);
        Assert.True(b.z);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Indexer_GetSet_WorksCorrectly(int index)
    {
        var b = new Bool3();

        b[index] = true;
        Assert.True(b[index]);

        b[index] = false;
        Assert.False(b[index]);
    }

    [Fact]
    public void Indexer_InvalidIndex_ThrowsException()
    {
        var b = new Bool3();
        Assert.Throws<ArgumentOutOfRangeException>(() => b[-1] = true);
        Assert.Throws<ArgumentOutOfRangeException>(() => b[3] = true);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = b[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = b[3]);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    public void Any_ReturnsCorrectResult(bool x, bool y, bool z)
    {
        var b = new Bool3(x, y, z);
        Assert.Equal(x || y || z, b.Any());
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    public void All_ReturnsCorrectResult(bool x, bool y, bool z)
    {
        var b = new Bool3(x, y, z);
        Assert.Equal(x && y && z, b.All());
    }

    [Theory]
    [InlineData(true, false, false, 1)]
    [InlineData(true, true, false, 2)]
    [InlineData(true, true, true, 3)]
    [InlineData(false, false, false, 0)]
    public void CountTrue_ReturnsCorrectCount(bool x, bool y, bool z, int expected)
    {
        var b = new Bool3(x, y, z);
        Assert.Equal(expected, b.CountTrue());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SetAll_SetsAllComponents(bool value)
    {
        var b = new Bool3(!value);
        b.SetAll(value);
        Assert.Equal(value, b.x);
        Assert.Equal(value, b.y);
        Assert.Equal(value, b.z);
    }

    [Fact]
    public void ToArray_ReturnsCorrectArray()
    {
        var b = new Bool3(true, false, true);
        var arr = b.ToArray();
        Assert.Equal(3, arr.Length);
        Assert.Equal(b.x, arr[0]);
        Assert.Equal(b.y, arr[1]);
        Assert.Equal(b.z, arr[2]);
    }

    [Fact]
    public void Equals_ComparesCorrectly()
    {
        var b1 = new Bool3(true, false, true);
        var b2 = new Bool3(true, false, true);
        var b3 = new Bool3(true, true, true);

        Assert.True(b1.Equals(b2));
        Assert.False(b1.Equals(b3));
        Assert.True(b1 == b2);
        Assert.False(b1 == b3);
        Assert.False(b1 != b2);
        Assert.True(b1 != b3);
    }

    [Fact]
    public void LogicalOperators_WorkCorrectly()
    {
        var b1 = new Bool3(true, false, true);
        var b2 = new Bool3(true, true, false);

        // AND
        var and = b1 & b2;
        Assert.True(and.x);
        Assert.False(and.y);
        Assert.False(and.z);

        // OR
        var or = b1 | b2;
        Assert.True(or.x);
        Assert.True(or.y);
        Assert.True(or.z);

        // XOR
        var xor = b1 ^ b2;
        Assert.False(xor.x);
        Assert.True(xor.y);
        Assert.True(xor.z);

        // NOT
        var not = !b1;
        Assert.False(not.x);
        Assert.True(not.y);
        Assert.False(not.z);
    }

    [Fact]
    public void Conversion_ImplicitFromBool_WorksCorrectly()
    {
        Bool3 b = true;
        Assert.True(b.All());

        b = false;
        Assert.False(b.Any());
    }

    [Fact]
    public void Conversion_ExplicitToByte_WorksCorrectly()
    {
        var b = new Bool3(true, true, false);
        byte data = (byte)b;
        Assert.Equal(3, data); // 0000_0011
    }

    [Fact]
    public void Conversion_ExplicitFromByte_WorksCorrectly()
    {
        Bool3 b = (Bool3)3; // 0000_0011
        Assert.True(b.x);
        Assert.True(b.y);
        Assert.False(b.z);
    }
}
