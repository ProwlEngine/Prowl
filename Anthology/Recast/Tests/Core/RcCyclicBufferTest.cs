using System;
using System.Linq;
using Prowl.Recast.Core.Buffers;
using Prowl.Recast.Core.Collections.Extensions;

namespace Prowl.Recast.Core.Tests;

// https://github.com/joaoportela/CircularBuffer-CSharp/blob/master/CircularBuffer.Tests/CircularBufferTests.cs
public class RcCyclicBufferTests
{
    [Fact]
    public void RcCyclicBuffer_GetEnumeratorConstructorCapacity_ReturnsEmptyCollection()
    {
        var buffer = new RcCyclicBuffer<string>(5);
        Assert.Empty(buffer.ToArray());
    }

    [Fact]
    public void RcCyclicBuffer_ConstructorSizeIndexAccess_CorrectContent()
    {
        var buffer = new RcCyclicBuffer<int>(5, new[] { 0, 1, 2, 3 });

        Assert.Equal(5, buffer.Capacity);
        Assert.Equal(4, buffer.Size);
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(i, buffer[i]);
        }
    }

    [Fact]
    public void RcCyclicBuffer_Constructor_ExceptionWhenSourceIsLargerThanCapacity()
    {
        Assert.Throws<ArgumentException>((Action)(() => new RcCyclicBuffer<int>(3, new[] { 0, 1, 2, 3 })));
    }

    [Fact]
    public void RcCyclicBuffer_GetEnumeratorConstructorDefinedArray_CorrectContent()
    {
        var buffer = new RcCyclicBuffer<int>(5, new[] { 0, 1, 2, 3 });

        int x = 0;
        foreach (var item in buffer)
        {
            Assert.Equal(x, item);
            x++;
        }
    }

    [Fact]
    public void RcCyclicBuffer_PushBack_CorrectContent()
    {
        var buffer = new RcCyclicBuffer<int>(5);

        for (int i = 0; i < 5; i++)
        {
            buffer.PushBack(i);
        }

        Assert.Equal(0, buffer.Front());
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(i, buffer[i]);
        }
    }

    [Fact]
    public void RcCyclicBuffer_PushBackOverflowingBuffer_CorrectContent()
    {
        var buffer = new RcCyclicBuffer<int>(5);

        for (int i = 0; i < 10; i++)
        {
            buffer.PushBack(i);
        }

        Assert.Equal(new[] { 5, 6, 7, 8, 9 }, buffer.ToArray());
    }

    [Fact]
    public void RcCyclicBuffer_GetEnumeratorOverflowedArray_CorrectContent()
    {
        var buffer = new RcCyclicBuffer<int>(5);

        for (int i = 0; i < 10; i++)
        {
            buffer.PushBack(i);
        }

        // buffer should have [5,6,7,8,9]
        int x = 5;
        buffer.ForEach(item =>
        {
            Assert.Equal(x, item);
            x++;
        });
    }

    [Fact]
    public void RcCyclicBuffer_ToArrayConstructorDefinedArray_CorrectContent()
    {
        var buffer = new RcCyclicBuffer<int>(5, new[] { 0, 1, 2, 3 });

        Assert.Equal(new[] { 0, 1, 2, 3 }, buffer.ToArray());
    }

    [Fact]
    public void RcCyclicBuffer_ToArrayOverflowedBuffer_CorrectContent()
    {
        var buffer = new RcCyclicBuffer<int>(5);

        for (int i = 0; i < 10; i++)
        {
            buffer.PushBack(i);
        }

        Assert.Equal(new[] { 5, 6, 7, 8, 9 }, buffer.ToArray());
    }

    [Fact]
    public void RcCyclicBuffer_PushFront_CorrectContent()
    {
        var buffer = new RcCyclicBuffer<int>(5);

        for (int i = 0; i < 5; i++)
        {
            buffer.PushFront(i);
        }

        Assert.Equal(new[] { 4, 3, 2, 1, 0 }, buffer.ToArray());
    }

    [Fact]
    public void RcCyclicBuffer_PushFrontAndOverflow_CorrectContent()
    {
        var buffer = new RcCyclicBuffer<int>(5);

        for (int i = 0; i < 10; i++)
        {
            buffer.PushFront(i);
        }

        Assert.Equal(new[] { 9, 8, 7, 6, 5 }, buffer.ToArray());
    }

    [Fact]
    public void RcCyclicBuffer_Front_CorrectItem()
    {
        var buffer = new RcCyclicBuffer<int>(5, new[] { 0, 1, 2, 3, 4 });

        Assert.Equal(0, buffer.Front());
    }

    [Fact]
    public void RcCyclicBuffer_Back_CorrectItem()
    {
        var buffer = new RcCyclicBuffer<int>(5, new[] { 0, 1, 2, 3, 4 });
        Assert.Equal(4, buffer.Back());
    }

    [Fact]
    public void RcCyclicBuffer_BackOfBufferOverflowByOne_CorrectItem()
    {
        var buffer = new RcCyclicBuffer<int>(5, new[] { 0, 1, 2, 3, 4 });
        buffer.PushBack(42);
        Assert.Equal(new[] { 1, 2, 3, 4, 42 }, buffer.ToArray());
        Assert.Equal(42, buffer.Back());
    }

    [Fact]
    public void RcCyclicBuffer_Front_EmptyBufferThrowsException()
    {
        var buffer = new RcCyclicBuffer<int>(5);

        Assert.Throws<InvalidOperationException>((Action)(() => buffer.Front()));
    }

    [Fact]
    public void RcCyclicBuffer_Back_EmptyBufferThrowsException()
    {
        var buffer = new RcCyclicBuffer<int>(5);
        Assert.Throws<InvalidOperationException>((Action)(() => buffer.Back()));
    }

    [Fact]
    public void RcCyclicBuffer_PopBack_RemovesBackElement()
    {
        var buffer = new RcCyclicBuffer<int>(5, new[] { 0, 1, 2, 3, 4 });

        Assert.Equal(5, buffer.Size);

        buffer.PopBack();

        Assert.Equal(4, buffer.Size);
        Assert.Equal(new[] { 0, 1, 2, 3 }, buffer.ToArray());
    }

    [Fact]
    public void RcCyclicBuffer_PopBackInOverflowBuffer_RemovesBackElement()
    {
        var buffer = new RcCyclicBuffer<int>(5, new[] { 0, 1, 2, 3, 4 });
        buffer.PushBack(5);

        Assert.Equal(5, buffer.Size);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, buffer.ToArray());

        buffer.PopBack();

        Assert.Equal(4, buffer.Size);
        Assert.Equal(new[] { 1, 2, 3, 4 }, buffer.ToArray());
    }

    [Fact]
    public void RcCyclicBuffer_PopFront_RemovesBackElement()
    {
        var buffer = new RcCyclicBuffer<int>(5, new[] { 0, 1, 2, 3, 4 });

        Assert.Equal(5, buffer.Size);

        buffer.PopFront();

        Assert.Equal(4, buffer.Size);
        Assert.Equal(new[] { 1, 2, 3, 4 }, buffer.ToArray());
    }

    [Fact]
    public void RcCyclicBuffer_PopFrontInOverflowBuffer_RemovesBackElement()
    {
        var buffer = new RcCyclicBuffer<int>(5, new[] { 0, 1, 2, 3, 4 });
        buffer.PushFront(5);

        Assert.Equal(5, buffer.Size);
        Assert.Equal(new[] { 5, 0, 1, 2, 3 }, buffer.ToArray());

        buffer.PopFront();

        Assert.Equal(4, buffer.Size);
        Assert.Equal(new[] { 0, 1, 2, 3 }, buffer.ToArray());
    }

    [Fact]
    public void RcCyclicBuffer_SetIndex_ReplacesElement()
    {
        var buffer = new RcCyclicBuffer<int>(5, new[] { 0, 1, 2, 3, 4 });

        buffer[1] = 10;
        buffer[3] = 30;

        Assert.Equal(new[] { 0, 10, 2, 30, 4 }, buffer.ToArray());
    }

    [Fact]
    public void RcCyclicBuffer_WithDifferentSizeAndCapacity_BackReturnsLastArrayPosition()
    {
        // test to confirm this issue does not happen anymore:
        // https://github.com/joaoportela/RcCyclicBuffer-CSharp/issues/2

        var buffer = new RcCyclicBuffer<int>(5, new[] { 0, 1, 2, 3, 4 });

        buffer.PopFront(); // (make size and capacity different)

        Assert.Equal(4, buffer.Back());
    }

    [Fact]
    public void RcCyclicBuffer_Clear_ClearsContent()
    {
        var buffer = new RcCyclicBuffer<int>(5, new[] { 4, 3, 2, 1, 0 });

        buffer.Clear();

        Assert.Equal(0, buffer.Size);
        Assert.Equal(5, buffer.Capacity);
        Assert.Equal(new int[0], buffer.ToArray());
    }

    [Fact]
    public void RcCyclicBuffer_Clear_WorksNormallyAfterClear()
    {
        var buffer = new RcCyclicBuffer<int>(5, new[] { 4, 3, 2, 1, 0 });

        buffer.Clear();
        for (int i = 0; i < 5; i++)
        {
            buffer.PushBack(i);
        }

        Assert.Equal(0, buffer.Front());
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(i, buffer[i]);
        }
    }

    [Fact]
    public void RcCyclicBuffer_RegularForEachWorks()
    {
        var refValues = new[] { 4, 3, 2, 1, 0 };
        var buffer = new RcCyclicBuffer<int>(5, refValues);

        var index = 0;
        foreach (var element in buffer)
        {
            Assert.Equal(refValues[index++], element);
        }
    }

    [Fact]
    public void RcCyclicBuffer_EnumeratorWorks()
    {
        var refValues = new int[] { 4, 3, 2, 1, 0 };
        var buffer = new RcCyclicBuffer<int>(5, refValues);


        var index = 0;
        using var enumerator = buffer.GetEnumerator();
        enumerator.Reset();
        while (enumerator.MoveNext())
        {
            Assert.Equal(refValues[index++], enumerator.Current);
        }

        // Ensure Reset works properly
        index = 0;
        enumerator.Reset();
        while (enumerator.MoveNext())
        {
            Assert.Equal(refValues[index++], enumerator.Current);
        }
    }

    [Fact]
    public void RcCyclicBuffers_Sum()
    {
        var refValues = Enumerable.Range(-100, 211).Select(x => (long)x).ToArray();
        var buffer = new RcCyclicBuffer<long>(refValues.Length, refValues);
        Assert.Equal(refValues.Sum(), RcCyclicBuffers.Sum(buffer));
    }

    [Fact]
    public void RcCyclicBuffers_Average()
    {
        var refValues = Enumerable.Range(-100, 211).Select(x => (long)x).ToArray();
        var buffer = new RcCyclicBuffer<long>(refValues.Length, refValues);
        Assert.Equal(refValues.Average(), RcCyclicBuffers.Average(buffer));
    }

    [Fact]
    public void RcCyclicBuffers_Min()
    {
        var refValues = Enumerable.Range(-100, 211).Select(x => (long)x).ToArray();
        var buffer = new RcCyclicBuffer<long>(refValues.Length, refValues);
        Assert.Equal(refValues.Min(), RcCyclicBuffers.Min(buffer));
    }

    [Fact]
    public void RcCyclicBuffers_Max()
    {
        var refValues = Enumerable.Range(-100, 211).Select(x => (long)x).ToArray();
        var buffer = new RcCyclicBuffer<long>(refValues.Length, refValues);
        Assert.Equal(refValues.Max(), RcCyclicBuffers.Max(buffer));
    }

    [Fact]
    public void RcCyclicBuffers_SumUnaligned()
    {
        var refValues = Enumerable.Range(-1, 3).Select(x => (long)x).ToArray();
        var buffer = new RcCyclicBuffer<long>(refValues.Length, refValues);
        Assert.Equal(refValues.Sum(), RcCyclicBuffers.Sum(buffer));
    }

    [Fact]
    public void RcCyclicBuffers_AverageUnaligned()
    {
        var refValues = Enumerable.Range(-1, 3).Select(x => (long)x).ToArray();
        var buffer = new RcCyclicBuffer<long>(refValues.Length, refValues);
        Assert.Equal(refValues.Average(), RcCyclicBuffers.Average(buffer));
    }

    [Fact]
    public void RcCyclicBuffers_MinUnaligned()
    {
        var refValues = Enumerable.Range(5, 3).Select(x => (long)x).ToArray();
        var buffer = new RcCyclicBuffer<long>(refValues.Length, refValues);
        Assert.Equal(refValues.Min(), RcCyclicBuffers.Min(buffer));
    }

    [Fact]
    public void RcCyclicBuffers_MaxUnaligned()
    {
        var refValues = Enumerable.Range(-5, 3).Select(x => (long)x).ToArray();
        var buffer = new RcCyclicBuffer<long>(refValues.Length, refValues);
        Assert.Equal(refValues.Max(), RcCyclicBuffers.Max(buffer));
    }

    [Fact]
    public void RcCyclicBuffers_SumDeleted()
    {
        var initialValues = Enumerable.Range(-100, 211).Select(x => (long)x).ToArray();
        var refValues = initialValues.Skip(1).SkipLast(1).ToArray();
        var buffer = new RcCyclicBuffer<long>(initialValues.Length, initialValues);
        buffer.PopBack();
        buffer.PopFront();

        Assert.Equal(refValues.Sum(), RcCyclicBuffers.Sum(buffer));
    }

    [Fact]
    public void RcCyclicBuffers_SumSplit()
    {
        var refValues = Enumerable.Range(-100, 211).Select(x => (long)x).ToArray();
        var buffer = new RcCyclicBuffer<long>(refValues.Length, refValues);
        buffer.PopFront();
        buffer.PushBack(refValues[0]);
        Assert.Equal(refValues.Sum(), RcCyclicBuffers.Sum(buffer));
    }

    [Fact]
    public void RcCyclicBuffers_AverageSplit()
    {
        var refValues = Enumerable.Range(-100, 211).Select(x => (long)x).ToArray();
        var buffer = new RcCyclicBuffer<long>(refValues.Length, refValues);
        buffer.PopFront();
        buffer.PushBack(refValues[0]);
        Assert.Equal(refValues.Average(), RcCyclicBuffers.Average(buffer));
    }

    [Fact]
    public void RcCyclicBuffers_MinSplit()
    {
        var refValues = Enumerable.Range(-100, 211).Select(x => (long)x).ToArray();
        var buffer = new RcCyclicBuffer<long>(refValues.Length, refValues);
        buffer.PopFront();
        buffer.PushBack(refValues[0]);
        Assert.Equal(refValues.Min(), RcCyclicBuffers.Min(buffer));
    }

    [Fact]
    public void RcCyclicBuffers_MaxSplit()
    {
        var refValues = Enumerable.Range(-100, 211).Select(x => (long)x).ToArray();
        var buffer = new RcCyclicBuffer<long>(refValues.Length, refValues);
        buffer.PopFront();
        buffer.PushBack(refValues[0]);
        Assert.Equal(refValues.Max(), RcCyclicBuffers.Max(buffer));
    }
}