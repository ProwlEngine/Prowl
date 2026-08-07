using System;
using System.Collections.Generic;
using Prowl.Recast.Core.Buffers;

namespace Prowl.Recast.Core.Tests;

public class RcRentedArrayTest
{
    public List<int> RandomValues(int length)
    {
        var rand = new RcRand();

        // excepted values
        var list = new List<int>();
        for (int i = 0; i < length; ++i)
        {
            list.Add(rand.NextInt32());
        }

        return list;
    }

    [Fact]
    public void TestRentedArray()
    {
        var rand = new RcRand();
        for (int loop = 0; loop < 1024; ++loop)
        {
            {
                int length = Math.Max(2, (int)(rand.Next() * 2048));
                var values = RandomValues(length);
                using var array = RcRentedArray.Shared.Rent<int>(length);
                using var array2 = RcRentedArray.Shared.Rent<int>(length);

                for (int i = 0; i < array.Length; ++i)
                {
                    array[i] = values[i];
                }

                for (int i = 0; i < array.Length; ++i)
                {
                    Assert.Equal(values[i], array[i]);
                }

                Assert.Equal(values[^1], array[array.Length - 1]);
            }
        }
    }

    [Fact]
    public void TestSame()
    {
        // not same
        {
            using var r1 = RcRentedArray.Shared.Rent<float>(1024);
            using var r2 = RcRentedArray.Shared.Rent<float>(1024);

            Assert.Equal(true, r2.AsSpan() != r1.AsSpan());
        }

        // same
        {
            // error case
            Span<float> r1Array;

            {
                using var r1 = RcRentedArray.Shared.Rent<float>(1024);
                r1Array = r1.AsSpan();
                for (int i = 0; i < r1.Length; ++i)
                {
                    r1[i] = 123;
                }
            }

            using var r2 = RcRentedArray.Shared.Rent<float>(1024);

            Assert.Equal(true, r2.AsSpan() == r1Array);
        }
    }

    [Fact]
    public void TestDispose()
    {
        using var r1 = RcRentedArray.Shared.Rent<float>(1024);
        for (int i = 0; i < r1.Length; ++i)
        {
            r1[i] = 123;
        }

        Assert.Equal(false, r1.IsDisposed);
        r1.Dispose();
        Assert.Equal(true, r1.IsDisposed);
    }
}