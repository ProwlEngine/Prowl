using System;
using System.Collections.Generic;
using Prowl.Recast.Core.Collections;

namespace Prowl.Recast.Core.Tests;


public class RcFixedArrayTest
{
    public List<int> RandomValues(int size)
    {
        var rand = new RcRand();

        // excepted values
        var list = new List<int>();
        for (int i = 0; i < size; ++i)
        {
            list.Add(rand.NextInt32());
        }

        return list;
    }

    [Fact]
    public void TestStackOverflow()
    {
        // normal
        var array_128_512_1 = new RcFixedArray2<RcFixedArray512<float>>(); // 128 * 512 = 65536

        // warn
        //var array_128_512_2 = RcFixedArray128<RcFixedArray512<float>>.Empty; // 128 * 512 = 65536

        // danger
        // var array_32_512_1 = RcFixedArray32<RcFixedArray512<float>>.Empty; // 32 * 512 = 16384
        // var array_16_512_1 = RcFixedArray16<RcFixedArray512<float>>.Empty; // 16 * 512 = 8192
        // var array_8_512_1 = RcFixedArray8<RcFixedArray512<float>>.Empty; // 8 * 512 = 4196
        // var array_4_256_1 = RcFixedArray4<RcFixedArray256<float>>.Empty; // 4 * 256 = 1024
        // var array_4_64_1 = RcFixedArray4<RcFixedArray64<float>>.Empty; // 4 * 64 = 256
        // var array_2_8_1 = RcFixedArray2<RcFixedArray8<float>>.Empty; // 2 * 8 = 16
        // var array_2_4_1 = RcFixedArray2<RcFixedArray2<float>>.Empty; // 2 * 2 = 4

        float f1 = 0.0f; // 1
        //float f2 = 0.0f; // my system stack overflow!
        Assert.Equal(0.0f, f1);

        RcDebug.UnusedRef(ref array_128_512_1);
    }

    [Fact]
    public void TestRcFixedArray2()
    {
        var array = new RcFixedArray2<int>();
        Assert.Equal(2, array.Length);

        var values = RandomValues(array.Length);
        for (int i = 0; i < array.Length; ++i)
        {
            array[i] = values[i];
        }

        for (int i = 0; i < array.Length; ++i)
        {
            Assert.Equal(values[i], array[i]);
        }

        Assert.Equal(values[^1], array[^1]);

        Assert.Throws<IndexOutOfRangeException>((Action)(() => array[-1] = 0));
        Assert.Throws<IndexOutOfRangeException>((Action)(() => array[array.Length + 1] = 0));
        Assert.Throws<IndexOutOfRangeException>((Action)(() => _ = array[-1]));
        Assert.Throws<IndexOutOfRangeException>((Action)(() => _ = array[array.Length + 1]));
    }

    [Fact]
    public void TestRcFixedArray4()
    {
        var array = new RcFixedArray4<int>();
        Assert.Equal(4, array.Length);

        var values = RandomValues(array.Length);
        for (int i = 0; i < array.Length; ++i)
        {
            array[i] = values[i];
        }

        for (int i = 0; i < array.Length; ++i)
        {
            Assert.Equal(values[i], array[i]);
        }

        Assert.Equal(values[^1], array[^1]);

        Assert.Throws<IndexOutOfRangeException>((Action)(() => array[-1] = 0));
        Assert.Throws<IndexOutOfRangeException>((Action)(() => array[array.Length + 1] = 0));
        Assert.Throws<IndexOutOfRangeException>((Action)(() => _ = array[-1]));
        Assert.Throws<IndexOutOfRangeException>((Action)(() => _ = array[array.Length + 1]));
    }

    [Fact]
    public void TestRcFixedArray8()
    {
        var array = new RcFixedArray8<int>();
        Assert.Equal(8, array.Length);

        var values = RandomValues(array.Length);
        for (int i = 0; i < array.Length; ++i)
        {
            array[i] = values[i];
        }

        for (int i = 0; i < array.Length; ++i)
        {
            Assert.Equal(values[i], array[i]);
        }

        Assert.Equal(values[^1], array[^1]);

        Assert.Throws<IndexOutOfRangeException>((Action)(() => array[-1] = 0));
        Assert.Throws<IndexOutOfRangeException>((Action)(() => array[array.Length + 1] = 0));
        Assert.Throws<IndexOutOfRangeException>((Action)(() => _ = array[-1]));
        Assert.Throws<IndexOutOfRangeException>((Action)(() => _ = array[array.Length + 1]));
    }

    [Fact]
    public void TestRcFixedArray16()
    {
        var array = new RcFixedArray16<int>();
        Assert.Equal(16, array.Length);

        var values = RandomValues(array.Length);
        for (int i = 0; i < array.Length; ++i)
        {
            array[i] = values[i];
        }

        for (int i = 0; i < array.Length; ++i)
        {
            Assert.Equal(values[i], array[i]);
        }

        Assert.Equal(values[^1], array[^1]);

        Assert.Throws<IndexOutOfRangeException>((Action)(() => array[-1] = 0));
        Assert.Throws<IndexOutOfRangeException>((Action)(() => array[array.Length + 1] = 0));
        Assert.Throws<IndexOutOfRangeException>((Action)(() => _ = array[-1]));
        Assert.Throws<IndexOutOfRangeException>((Action)(() => _ = array[array.Length + 1]));
    }

    [Fact]
    public void TestRcFixedArray32()
    {
        var array = new RcFixedArray32<int>();
        Assert.Equal(32, array.Length);

        var values = RandomValues(array.Length);
        for (int i = 0; i < array.Length; ++i)
        {
            array[i] = values[i];
        }

        for (int i = 0; i < array.Length; ++i)
        {
            Assert.Equal(values[i], array[i]);
        }

        Assert.Equal(values[^1], array[^1]);

        Assert.Throws<IndexOutOfRangeException>((Action)(() => array[-1] = 0));
        Assert.Throws<IndexOutOfRangeException>((Action)(() => array[array.Length + 1] = 0));
        Assert.Throws<IndexOutOfRangeException>((Action)(() => _ = array[-1]));
        Assert.Throws<IndexOutOfRangeException>((Action)(() => _ = array[array.Length + 1]));
    }

    [Fact]
    public void TestRcFixedArray64()
    {
        var array = new RcFixedArray64<int>();
        Assert.Equal(64, array.Length);

        var values = RandomValues(array.Length);
        for (int i = 0; i < array.Length; ++i)
        {
            array[i] = values[i];
        }

        for (int i = 0; i < array.Length; ++i)
        {
            Assert.Equal(values[i], array[i]);
        }

        Assert.Equal(values[^1], array[^1]);

        Assert.Throws<IndexOutOfRangeException>((Action)(() => array[-1] = 0));
        Assert.Throws<IndexOutOfRangeException>((Action)(() => array[array.Length + 1] = 0));
        Assert.Throws<IndexOutOfRangeException>((Action)(() => _ = array[-1]));
        Assert.Throws<IndexOutOfRangeException>((Action)(() => _ = array[array.Length + 1]));
    }

    [Fact]
    public void TestRcFixedArray128()
    {
        var array = new RcFixedArray128<int>();
        Assert.Equal(128, array.Length);

        var values = RandomValues(array.Length);
        for (int i = 0; i < array.Length; ++i)
        {
            array[i] = values[i];
        }

        for (int i = 0; i < array.Length; ++i)
        {
            Assert.Equal(values[i], array[i]);
        }

        Assert.Equal(values[^1], array[^1]);

        Assert.Throws<IndexOutOfRangeException>((Action)(() => array[-1] = 0));
        Assert.Throws<IndexOutOfRangeException>((Action)(() => array[array.Length + 1] = 0));
        Assert.Throws<IndexOutOfRangeException>((Action)(() => _ = array[-1]));
        Assert.Throws<IndexOutOfRangeException>((Action)(() => _ = array[array.Length + 1]));
    }

    [Fact]
    public void TestRcFixedArray256()
    {
        var array = new RcFixedArray256<int>();
        Assert.Equal(256, array.Length);

        var values = RandomValues(array.Length);
        for (int i = 0; i < array.Length; ++i)
        {
            array[i] = values[i];
        }

        for (int i = 0; i < array.Length; ++i)
        {
            Assert.Equal(values[i], array[i]);
        }

        Assert.Equal(values[^1], array[^1]);

        Assert.Throws<IndexOutOfRangeException>((Action)(() => array[-1] = 0));
        Assert.Throws<IndexOutOfRangeException>((Action)(() => array[array.Length + 1] = 0));
        Assert.Throws<IndexOutOfRangeException>((Action)(() => _ = array[-1]));
        Assert.Throws<IndexOutOfRangeException>((Action)(() => _ = array[array.Length + 1]));
    }

    [Fact]
    public void TestRcFixedArray512()
    {
        var array = new RcFixedArray512<int>();
        Assert.Equal(512, array.Length);

        var values = RandomValues(array.Length);
        for (int i = 0; i < array.Length; ++i)
        {
            array[i] = values[i];
        }

        for (int i = 0; i < array.Length; ++i)
        {
            Assert.Equal(values[i], array[i]);
        }

        Assert.Equal(values[^1], array[^1]);

        Assert.Throws<IndexOutOfRangeException>((Action)(() => array[-1] = 0));
        Assert.Throws<IndexOutOfRangeException>((Action)(() => array[array.Length + 1] = 0));
        Assert.Throws<IndexOutOfRangeException>((Action)(() => _ = array[-1]));
        Assert.Throws<IndexOutOfRangeException>((Action)(() => _ = array[array.Length + 1]));
    }
}