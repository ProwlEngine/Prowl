using System;
using System.Collections.Generic;

namespace Prowl.Recast.Core.Tests;

public class RcSpanTest
{
    [Fact]
    public void TestCopy()
    {
        // Test for copying all elements to the destination span.
        {
            Span<long> src = stackalloc long[] { 1, 2, 3 };
            Span<long> dst = stackalloc long[] { 0, 0, 0 };

            RcSpans.Copy(src, dst);
            Assert.Equal(dst.ToArray(), src.ToArray());
        }

        // Test for successful copying when the destination Span has a larger size.
        {
            Span<long> src = stackalloc long[] { 1, 2 };
            Span<long> dst = stackalloc long[] { 0, 0, 0, 0 };

            RcSpans.Copy(src, dst);
            Assert.Equal(dst.Slice(0, src.Length).ToArray(), src.ToArray());
        }

        // Test for an error when copying to a Span with a smaller size.
        {
            Assert.Throws<ArgumentException>((Action)(() =>
            {
                Span<long> src = stackalloc long[] { 1, 2, 3, 4, 5 };
                Span<long> dst = stackalloc long[] { 0, 0, 0 };
                RcSpans.Copy(src, dst);
            }));
        }

        // Test for copying a specific range of elements from the source span to a specific range in the destination span.
        {
            Span<long> src = stackalloc long[] { 1, 2, 3, 4, 5 };
            Span<long> dst = stackalloc long[] { 0, 0, 0 };

            RcSpans.Copy(src, 2, dst, 0, 2);
            Assert.Equal(dst.Slice(0, 2).ToArray(), src.Slice(2, 2).ToArray());
        }

        // Test for copying a specific range of elements from the source span to a specific range in the destination span.
        {
            Span<long> src = stackalloc long[] { 5, 4, 3, 2, 1 };
            Span<long> dst = stackalloc long[] { 0, 0, 0 };

            RcSpans.Copy(src, 2, dst, 0, 3);
            Assert.Equal(dst.ToArray(), src.Slice(2, 3).ToArray());
        }

        // Test for src (index + length) over
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
        {
            Span<long> src = stackalloc long[] { 5, 4, 3, 2, 1 };
            Span<long> dst = stackalloc long[] { 0, 0, 0 };

            //
            RcSpans.Copy(src, 3, dst, 0, 3);
        }));

        // Test for src (index + length) over
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
        {
            Span<long> src = stackalloc long[] { 5, 4, 3, 2, 1 };
            Span<long> dst = stackalloc long[] { 0, 0, 0 };

            //
            RcSpans.Copy(src, 5, dst, 0, 1);
        }));


        // Test for dst (idx + length) over
        Assert.Throws<ArgumentException>((Action)(() =>
        {
            Span<long> src = stackalloc long[] { 5, 4, 3, 2, 1 };
            Span<long> dst = stackalloc long[] { 0, 0, 0 };

            //
            RcSpans.Copy(src, 0, dst, 1, 3);
        }));

        // Test for dst (idx + length) over
        Assert.Throws<ArgumentException>((Action)(() =>
        {
            Span<long> src = stackalloc long[] { 5, 4, 3, 2, 1 };
            Span<long> dst = stackalloc long[] { 0, 0, 0 };

            //
            RcSpans.Copy(src, 0, dst, 0, 4);
        }));
    }

    [Fact]
    public void TestMove()
    {
        // [3, 2, 1] -> [3, 1, 1]
        {
            var expected = new List<long>() { 3, 1, 1 };
            Span<long> src = stackalloc long[] { 3, 2, 1 };
            RcSpans.Move(src, 2, 1, 1);
            Assert.Equal(expected, src.ToArray());
        }

        // [3, 2, 1] -> [2, 1, 1]
        {
            var expected = new List<long>() { 2, 1, 1 };
            Span<long> src = stackalloc long[] { 3, 2, 1 };
            RcSpans.Move(src, 1, 0, 2);
            Assert.Equal(expected, src.ToArray());
        }

        // [3, 2, 1] -> [3, 2, 1]
        {
            var expected = new List<long>() { 3, 2, 1 };
            Span<long> src = stackalloc long[] { 3, 2, 1 };
            RcSpans.Move(src, 0, 0, 3);
            Assert.Equal(expected, src.ToArray());
        }

        // length over
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
        {
            Span<long> src = stackalloc long[] { 3, 2, 1 };
            RcSpans.Move(src, 0, 0, 4);
        }));

        // source index over
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
        {
            Span<long> src = stackalloc long[] { 3, 2, 1 };
            RcSpans.Move(src, 3, 0, 1);
        }));

        // destination index over
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
        {
            Span<long> src = stackalloc long[] { 3, 2, 1 };
            RcSpans.Move(src, 0, 3, 1);
        }));
    }
}