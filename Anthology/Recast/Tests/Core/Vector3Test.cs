using System;
using System.Numerics;
using Prowl.Recast.Core.Numerics;

namespace Prowl.Recast.Core.Tests;

public class Vector3Test
{
    [Fact]
    public void TestVectorLength()
    {
        for (int __r = 0; __r < 100000; __r++)
        {
            var v1 = new Vector3(Random.Shared.NextSingle(), Random.Shared.NextSingle(), Random.Shared.NextSingle());
            var v11 = new RcVec3f(v1.X, v1.Y, v1.Z);

            Assert.Equal(v11.Length(), v1.Length());
            Assert.Equal(v11.LengthSquared(), v1.LengthSquared());
        }
    }

    [Fact]
    public void TestVectorSubtract()
    {
        for (int __r = 0; __r < 100000; __r++)
        {
            var v1 = new Vector3(Random.Shared.NextSingle(), Random.Shared.NextSingle(), Random.Shared.NextSingle());
            var v2 = new Vector3(Random.Shared.NextSingle(), Random.Shared.NextSingle(), Random.Shared.NextSingle());
            var v3 = Vector3.Subtract(v1, v2);
            var v4 = v1 - v2;
            Assert.Equal(v4, v3);

            var v11 = new RcVec3f(v1.X, v1.Y, v1.Z);
            var v22 = new RcVec3f(v2.X, v2.Y, v2.Z);
            var v33 = RcVec3f.Subtract(v11, v22);
            var v44 = v11 - v22;
            Assert.Equal(v44, v33);

            Assert.Equal(v33.X, v3.X, 0.0000001d);
            Assert.Equal(v33.Y, v3.Y, 0.0000001d);
            Assert.Equal(v33.Z, v3.Z, 0.0000001d);
        }
    }


    [Fact]
    public void TestVectorAdd()
    {
        for (int __r = 0; __r < 100000; __r++)
        {
            var v1 = new Vector3(Random.Shared.NextSingle(), Random.Shared.NextSingle(), Random.Shared.NextSingle());
            var v2 = new Vector3(Random.Shared.NextSingle(), Random.Shared.NextSingle(), Random.Shared.NextSingle());
            var v3 = Vector3.Add(v1, v2);
            var v4 = v1 + v2;
            Assert.Equal(v4, v3);

            var v11 = new RcVec3f(v1.X, v1.Y, v1.Z);
            var v22 = new RcVec3f(v2.X, v2.Y, v2.Z);
            var v33 = RcVec3f.Add(v11, v22);
            var v44 = v11 + v22;
            Assert.Equal(v44, v33);

            Assert.Equal(v33.X, v3.X);
            Assert.Equal(v33.Y, v3.Y);
            Assert.Equal(v33.Z, v3.Z);
        }
    }

    [Fact]
    public void TestVectorNormalize()
    {
        for (int __r = 0; __r < 100000; __r++)
        {
            var v1 = new Vector3(Random.Shared.NextSingle(), Random.Shared.NextSingle(), Random.Shared.NextSingle());
            var v2 = Vector3.Normalize(v1);

            var v11 = new RcVec3f(v1.X, v1.Y, v1.Z);
            var v22 = RcVec3f.Normalize(v11);

            Assert.Equal(v22.X, v2.X, 0.000001d);
            Assert.Equal(v22.Y, v2.Y, 0.000001d);
            Assert.Equal(v22.Z, v2.Z, 0.000001d);
        }
    }

    [Fact]
    public void TestVectorCross()
    {
        for (int __r = 0; __r < 100000; __r++)
        {
            var v1 = new Vector3(Random.Shared.NextSingle(), Random.Shared.NextSingle(), Random.Shared.NextSingle());
            var v2 = new Vector3(Random.Shared.NextSingle(), Random.Shared.NextSingle(), Random.Shared.NextSingle());
            var v3 = Vector3.Cross(v1, v2);

            var v11 = new RcVec3f(v1.X, v1.Y, v1.Z);
            var v22 = new RcVec3f(v2.X, v2.Y, v2.Z);
            var v33 = RcVec3f.Cross(v11, v22);

            Assert.Equal(v33.X, v3.X, 0.000001d);
            Assert.Equal(v33.Y, v3.Y, 0.000001d);
            Assert.Equal(v33.Z, v3.Z, 0.000001d);
        }
    }

    [Fact]
    public void TestVectorCopyTo()
    {
        for (int __r = 0; __r < 100000; __r++)
        {
            var v1 = new Vector3(Random.Shared.NextSingle(), Random.Shared.NextSingle(), Random.Shared.NextSingle());
            var array1 = new float[3];
            var array2 = new float[3];
            v1.CopyTo(array1);
            v1.CopyTo(array2, 0);

            var v11 = new RcVec3f(v1.X, v1.Y, v1.Z);
            var array11 = new float[3];
            var array22 = new float[3];
            v11.CopyTo(array11);
            v11.CopyTo(array22, 0);

            Assert.Equal(array11, array1);
            Assert.Equal(array22, array2);
        }
    }

    [Fact]
    public void TestVectorDot()
    {
        for (int __r = 0; __r < 100000; __r++)
        {
            var v1 = new Vector3(Random.Shared.NextSingle(), Random.Shared.NextSingle(), Random.Shared.NextSingle());
            var v2 = new Vector3(Random.Shared.NextSingle(), Random.Shared.NextSingle(), Random.Shared.NextSingle());
            float d3 = Vector3.Dot(v1, v2);

            var v11 = new RcVec3f(v1.X, v1.Y, v1.Z);
            var v22 = new RcVec3f(v2.X, v2.Y, v2.Z);
            var d33 = RcVec3f.Dot(v11, v22);

            Assert.Equal(d33, d3);
        }
    }

    [Fact]
    public void TestVectorDistance()
    {
        for (int __r = 0; __r < 100000; __r++)
        {
            var v1 = new Vector3(Random.Shared.NextSingle(), Random.Shared.NextSingle(), Random.Shared.NextSingle());
            var v2 = new Vector3(Random.Shared.NextSingle(), Random.Shared.NextSingle(), Random.Shared.NextSingle());
            var d3 = Vector3.Distance(v1, v2);
            var d4 = Vector3.DistanceSquared(v1, v2);

            var v11 = new RcVec3f(v1.X, v1.Y, v1.Z);
            var v22 = new RcVec3f(v2.X, v2.Y, v2.Z);
            var d33 = RcVec3f.Distance(v11, v22);
            var d44 = RcVec3f.DistanceSquared(v11, v22);

            Assert.Equal(d33, d3);
            Assert.Equal(d44, d4);
        }
    }

    [Fact]
    public void TestVectorMinMax()
    {
        for (int __r = 0; __r < 100000; __r++)
        {
            var v1 = new Vector3(Random.Shared.NextSingle(), Random.Shared.NextSingle(), Random.Shared.NextSingle());
            var v2 = new Vector3(Random.Shared.NextSingle(), Random.Shared.NextSingle(), Random.Shared.NextSingle());
            var v3 = Vector3.Min(v1, v2);
            var v4 = Vector3.Max(v1, v2);

            var v11 = new RcVec3f(v1.X, v1.Y, v1.Z);
            var v22 = new RcVec3f(v2.X, v2.Y, v2.Z);
            var v33 = RcVec3f.Min(v11, v22);
            var v44 = RcVec3f.Max(v11, v22);

            Assert.Equal(v33.X, v3.X);
            Assert.Equal(v33.Y, v3.Y);
            Assert.Equal(v33.Z, v3.Z);

            Assert.Equal(v44.X, v4.X);
            Assert.Equal(v44.Y, v4.Y);
            Assert.Equal(v44.Z, v4.Z);
        }
    }

    [Fact]
    public void TestVectorLerp()
    {
        for (int __r = 0; __r < 100000; __r++)
        {
            var amt = Random.Shared.NextSingle();
            var v1 = new Vector3(Random.Shared.NextSingle(), Random.Shared.NextSingle(), Random.Shared.NextSingle());
            var v2 = new Vector3(Random.Shared.NextSingle(), Random.Shared.NextSingle(), Random.Shared.NextSingle());
            var v3 = Vector3.Lerp(v1, v2, amt);

            var v11 = new RcVec3f(v1.X, v1.Y, v1.Z);
            var v22 = new RcVec3f(v2.X, v2.Y, v2.Z);
            var v33 = RcVec3f.Lerp(v11, v22, amt);

            Assert.Equal(v33.X, v3.X, 0.0000001d);
            Assert.Equal(v33.Y, v3.Y, 0.0000001d);
            Assert.Equal(v33.Z, v3.Z, 0.0000001d);
        }
    }


    [Fact]
    public void TestImplicitCasting()
    {
        for (int __r = 0; __r < 100000; __r++)
        {
            var v1 = new Vector3(Random.Shared.NextSingle(), Random.Shared.NextSingle(), Random.Shared.NextSingle());
            var v2 = new RcVec3f(Random.Shared.NextSingle(), Random.Shared.NextSingle(), Random.Shared.NextSingle());

            Assert.Equal(RcVec3f.Distance(v1, v2), Vector3.Distance(v1, v2));
        }
    }
}