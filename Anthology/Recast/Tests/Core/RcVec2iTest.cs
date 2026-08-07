using System;
using Prowl.Recast.Core.Numerics;

namespace Prowl.Recast.Core.Tests;

public class RcVec2iTest
{
    [Fact]
    public void TestEquals()
    {
        for (int __r = 0; __r < 100000; __r++)
        {
            var x = Random.Shared.Next();
            var y = Random.Shared.Next();

            var v1 = new RcVec2i(x, y);
            var v2 = new RcVec2i(x, y);
            var v3 = new RcVec2i(x + 1, y);

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
            var v1 = new RcVec2i(Random.Shared.Next(1000), Random.Shared.Next(1000));
            var v2 = new RcVec2i(Random.Shared.Next(1000), Random.Shared.Next(1000));
            var scalar = Random.Shared.Next(100);

            // Add
            var vAdd = v1 + v2;
            Assert.Equal(v1.X + v2.X, vAdd.X);
            Assert.Equal(v1.Y + v2.Y, vAdd.Y);

            // Subtract
            var vSub = v1 - v2;
            Assert.Equal(v1.X - v2.X, vSub.X);
            Assert.Equal(v1.Y - v2.Y, vSub.Y);

            // Multiply
            var vMul = v1 * scalar;
            Assert.Equal(v1.X * scalar, vMul.X);
            Assert.Equal(v1.Y * scalar, vMul.Y);
        }
    }

    [Fact]
    public void TestIndexer()
    {
        var v = new RcVec2i(1, 2);
        Assert.Equal(1, v[0]);
        Assert.Equal(2, v[1]);
        Assert.Throws<IndexOutOfRangeException>((Action)(() =>
        {
            var _ = v[2];
        }));
    }

    [Fact]
    public void TestToString()
    {
        var v = new RcVec2i(1, 2);
        Assert.Equal("(1, 2)", v.ToString());
    }

    [Fact]
    public void TestStaticProperties()
    {
        Assert.Equal(new RcVec2i(0, 0), RcVec2i.Zero);
        Assert.Equal(new RcVec2i(1, 0), RcVec2i.UnitX);
        Assert.Equal(new RcVec2i(0, 1), RcVec2i.UnitY);
    }
}