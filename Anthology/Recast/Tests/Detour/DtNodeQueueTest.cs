using System.Collections.Generic;
using Prowl.Recast.Core;
using Prowl.Recast.Core.Collections.Extensions;

namespace Prowl.Recast.Detour.Tests;

public class DtNodeQueueTest
{
    private static List<DtNode> ShuffledNodes(int count)
    {
        var nodes = new List<DtNode>();
        for (int i = 0; i < count; ++i)
        {
            var node = new DtNode(i);
            node.total = i;
            nodes.Add(node);
        }

        nodes.Shuffle();
        return nodes;
    }

    [Fact]
    public void TestPushAndPop()
    {
        var queue = new DtNodeQueue();

        // check count
        Assert.Equal(0, queue.Count());

        // null push
        queue.Push(null);
        Assert.Equal(0, queue.Count());

        // test push
        const int count = 1000;
        var expectedNodes = ShuffledNodes(count);
        foreach (var node in expectedNodes)
        {
            queue.Push(node);
        }

        Assert.Equal(count, queue.Count());

        // test pop
        expectedNodes.Sort(DtNode.ComparisonNodeTotal);
        foreach (var node in expectedNodes)
        {
            Assert.Same(node, queue.Peek());
            Assert.Same(node, queue.Pop());
        }

        Assert.Equal(0, queue.Count());
    }

    [Fact]
    public void TestClear()
    {
        var queue = new DtNodeQueue();

        const int count = 555;
        var expectedNodes = ShuffledNodes(count);
        foreach (var node in expectedNodes)
        {
            queue.Push(node);
        }

        Assert.Equal(count, queue.Count());

        queue.Clear();
        Assert.Equal(0, queue.Count());
        Assert.True(queue.IsEmpty());
    }

    [Fact]
    public void TestModify()
    {
        var queue = new DtNodeQueue();

        const int count = 5000;
        var expectedNodes = ShuffledNodes(count);

        foreach (var node in expectedNodes)
        {
            queue.Push(node);
        }

        // check modify
        queue.Modify(null);

        // change total
        var r = new RcRand();
        foreach (var node in expectedNodes)
        {
            node.total = r.NextInt32() % (count / 50); // duplication for test
        }

        // test modify
        foreach (var node in expectedNodes)
        {
            queue.Modify(node);
        }

        // check
        expectedNodes.Sort(DtNode.ComparisonNodeTotal);
        foreach (var node in expectedNodes)
        {
            Assert.Same(node, queue.Pop());
        }
    }
}