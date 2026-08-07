using System.Collections.Generic;
using Prowl.Recast.Core.Collections;
using Prowl.Recast.Core.Collections.Extensions;

namespace Prowl.Recast.Core.Tests;

public class RcSortedQueueTest
{
    [Fact]
    public void TestEnqueueAndDequeue()
    {
        var sortedQueue = new RcSortedQueue<int>((a, b) => a.CompareTo(b));

        var r = new RcRand();
        var expectedList = new List<int>();
        for (int i = 0; i < 999; ++i)
        {
            expectedList.Add(r.NextInt32() % 300); // allow duplication
        }

        // ready
        foreach (var expected in expectedList)
        {
            sortedQueue.Enqueue(expected);
        }

        expectedList.Sort();

        // check count
        Assert.Equal(expectedList.Count, sortedQueue.Count());
        Assert.False(sortedQueue.IsEmpty());

        Assert.Equal(expectedList, sortedQueue.ToList());

        // check Peek and Dequeue
        for (int i = 0; i < expectedList.Count; ++i)
        {
            Assert.Equal(expectedList[i], sortedQueue.Peek());
            Assert.Equal(expectedList.Count - i, sortedQueue.Count());

            Assert.Equal(expectedList[i], sortedQueue.Dequeue());
            Assert.Equal(expectedList.Count - i - 1, sortedQueue.Count());
        }

        // check count
        Assert.Equal(0, sortedQueue.Count());
        Assert.True(sortedQueue.IsEmpty());
    }

    [Fact]
    public void TestRemoveForValueType()
    {
        var sortedQueue = new RcSortedQueue<int>((a, b) => a.CompareTo(b));

        var r = new RcRand();
        var expectedList = new List<int>();
        for (int i = 0; i < 999; ++i)
        {
            expectedList.Add(r.NextInt32() % 300); // allow duplication
        }

        // ready
        foreach (var expected in expectedList)
        {
            sortedQueue.Enqueue(expected);
        }

        expectedList.Shuffle();

        // check
        Assert.Equal(expectedList.Count, sortedQueue.Count());

        foreach (var expected in expectedList)
        {
            Assert.True(sortedQueue.Remove(expected));
        }

        Assert.True(sortedQueue.IsEmpty());
    }

    [Fact]
    public void TestRemoveForReferenceType()
    {
        var sortedQueue = new RcSortedQueue<RcAtomicLong>((a, b) => a.Read().CompareTo(b.Read()));

        var r = new RcRand();
        var expectedList = new List<RcAtomicLong>();
        for (int i = 0; i < 999; ++i)
        {
            expectedList.Add(new RcAtomicLong(r.NextInt32() % 300)); // allow duplication
        }

        // ready
        foreach (var expected in expectedList)
        {
            sortedQueue.Enqueue(expected);
        }

        expectedList.Shuffle();

        // check
        Assert.Equal(expectedList.Count, sortedQueue.Count());

        foreach (var expected in expectedList)
        {
            Assert.True(sortedQueue.Remove(expected));
        }

        Assert.True(sortedQueue.IsEmpty());
    }

}