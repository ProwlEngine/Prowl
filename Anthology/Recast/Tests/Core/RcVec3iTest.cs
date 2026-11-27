using System;
using Prowl.Recast.Core.Numerics;

namespace Prowl.Recast.Core.Tests;

public class RcVec3iTest
{
    [Fact]
    public void TestEquals()
    {
        for (int __r = 0; __r < 100000; __r++)
        {
            var x = Random.Shared.Next();
            var y = Random.Shared.Next();
            var z = Random.Shared.Next();

            var v1 = new RcVec3i(x, y, z);
            var v2 = new RcVec3i(x, y, z);
            var v3 = new RcVec3i(x + 1, y, z);

            Assert.Equal(v2, v1);
            Assert.True(v1 == v2);
            Assert.True(v1 != v3);
            Assert.True(v1.Equals(v2));
            Assert.True(v1.Equals((object)v2));
            Assert.Equal(v2.GetHashCode(), v1.GetHashCode());
        }
    }

    [Fact]
    public void TestArithmetic()
    {
        for (int __r = 0; __r < 100000; __r++)
        {
            var v1 = new RcVec3i(Random.Shared.Next(1000), Random.Shared.Next(1000), Random.Shared.Next(1000));
            var v2 = new RcVec3i(Random.Shared.Next(1000), Random.Shared.Next(1000), Random.Shared.Next(1000));
            var scalar = Random.Shared.Next(100);

            // Add
            var vAdd = v1 + v2;
            Assert.Equal(v1.X + v2.X, vAdd.X);
            Assert.Equal(v1.Y + v2.Y, vAdd.Y);
            Assert.Equal(v1.Z + v2.Z, vAdd.Z);

            // Subtract
            var vSub = v1 - v2;
            Assert.Equal(v1.X - v2.X, vSub.X);
            Assert.Equal(v1.Y - v2.Y, vSub.Y);
            Assert.Equal(v1.Z - v2.Z, vSub.Z);

            // Multiply
            var vMul = v1 * scalar;
            Assert.Equal(v1.X * scalar, vMul.X);
            Assert.Equal(v1.Y * scalar, vMul.Y);
            Assert.Equal(v1.Z * scalar, vMul.Z);
        }
    }

    [Fact]
    public void TestIndexer()
    {
        var v = new RcVec3i(1, 2, 3);
        Assert.Equal(1, v[0]);
        Assert.Equal(2, v[1]);
        Assert.Equal(3, v[2]);
        Assert.Throws<IndexOutOfRangeException>((Action)(() =>
        {
            var _ = v[3];
        }));
    }

    [Fact]
    public void TestToString()
    {
        var v = new RcVec3i(1, 2, 3);
        Assert.Equal("(1, 2, 3)", v.ToString());
    }

    [Fact]
    public void TestStaticProperties()
    {
        Assert.Equal(new RcVec3i(0, 0, 0), RcVec3i.Zero);
        Assert.Equal(new RcVec3i(1, 0, 0), RcVec3i.UnitX);
        Assert.Equal(new RcVec3i(0, 1, 0), RcVec3i.UnitY);
        Assert.Equal(new RcVec3i(0, 1, 1), RcVec3i.UnitZ);
    }
}