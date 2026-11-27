using System.Collections.Immutable;
using System.Linq;

namespace Prowl.Recast.Detour.Tests;

public class DtNodePoolTest
{
    [Fact]
    public void TestGetNode()
    {
        var pool = new DtNodePool();

        var node1St = pool.GetNode(0);
        var node2St = pool.GetNode(0);
        Assert.Same(node2St, node1St);

        node1St.state = 1;
        var node3St = pool.GetNode(0);
        Assert.NotSame(node3St, node1St);
    }

    [Fact]
    public void TestFindNode()
    {
        var pool = new DtNodePool();

        var counts = ImmutableArray.Create(2, 3, 5);

        // get and create
        for (int i = 0; i < counts.Length; ++i)
        {
            var count = counts[i];
            for (int ii = 0; ii < count; ++ii)
            {
                var node = pool.GetNode(i);
                node.state = ii + 1;
            }
        }

        int sum = counts.Sum();
        Assert.Equal(10, sum);

        // check GetNodeIdx GetNodeAtIdx
        for (int i = 0; i < sum; ++i)
        {
            var node = pool.GetNodeAtIdx(i);
            var nodeIdx = pool.GetNodeIdx(node);
            var nodeByIdx = pool.GetNodeAtIdx(nodeIdx);

            Assert.Same(nodeByIdx, node);
            Assert.Equal(i, nodeIdx);
        }

        // check count
        for (int i = 0; i < counts.Length; ++i)
        {
            var count = counts[i];
            var n = pool.FindNodes(i, out var nodes);
            Assert.Equal(count, n);

            int chainLength = 0;
            for (var chainNode = nodes; chainNode != null; chainNode = chainNode.next)
            {
                chainLength++;
            }

            Assert.Equal(count, chainLength);

            var node = pool.FindNode(i);
            Assert.Same(node, nodes);

            var node2 = pool.FindNode(i);
            Assert.Same(node2, nodes);
        }

        // check other count
        {
            var n = pool.FindNodes(4, out var nodes);
            Assert.Equal(0, n);
            Assert.Null(nodes);
        }

        var totalCount = pool.GetNodeCount();
        Assert.Equal(sum, totalCount);

        pool.Clear();
        totalCount = pool.GetNodeCount();
        Assert.Equal(0, totalCount);
    }
}