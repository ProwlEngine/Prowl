using Prowl.Recast.Core.Collections;

namespace Prowl.Recast.Core.Tests;

public class RcBinaryMinHeapTest
{
    private static readonly RcAtomicLong Gen = new();

    private class Node
    {
        public readonly long Id;
        public long Value;

        public Node(int value)
        {
            Id = Gen.IncrementAndGet();
            Value = value;
        }
    }

    [Fact]
    public void TestPush()
    {
        var minHeap = new RcBinaryMinHeap<Node>((x, y) => x.Value.CompareTo(y.Value));

        minHeap.Push(new Node(5));
        minHeap.Push(new Node(3));
        minHeap.Push(new Node(7));
        minHeap.Push(new Node(2));
        minHeap.Push(new Node(4));

        // Push 후 힙의 속성을 검증
        AssertHeapProperty(minHeap.ToArray());
    }

    [Fact]
    public void TestPop()
    {
        var minHeap = new RcBinaryMinHeap<Node>((x, y) => x.Value.CompareTo(y.Value));

        minHeap.Push(new Node(5));
        minHeap.Push(new Node(3));
        minHeap.Push(new Node(7));
        minHeap.Push(new Node(2));
        minHeap.Push(new Node(4));

        // Pop을 통해 최소 값부터 순서대로 제거하면서 검증
        Assert.Equal(2, minHeap.Pop().Value);
        Assert.Equal(3, minHeap.Pop().Value);
        Assert.Equal(4, minHeap.Pop().Value);
        Assert.Equal(5, minHeap.Pop().Value);
        Assert.Equal(7, minHeap.Pop().Value);

        // 모든 요소를 Pop한 후에는 비어있어야 함
        Assert.True(minHeap.IsEmpty());
    }


    [Fact]
    public void TestTop()
    {
        var minHeap = new RcBinaryMinHeap<Node>((x, y) => x.Value.CompareTo(y.Value));

        minHeap.Push(new Node(5));
        minHeap.Push(new Node(3));
        minHeap.Push(new Node(7));

        Assert.Equal(3, minHeap.Top().Value);
        AssertHeapProperty(minHeap.ToArray());
    }

    [Fact]
    public void TestModify()
    {
        var minHeap = new RcBinaryMinHeap<Node>((x, y) => x.Value.CompareTo(y.Value));

        var node7 = new Node(7);
        minHeap.Push(new Node(5));
        minHeap.Push(new Node(3));
        minHeap.Push(node7);
        minHeap.Push(new Node(2));
        minHeap.Push(new Node(4));

        node7.Value = 1;
        var result = minHeap.Modify(node7); // Modify value 7 to 1
        var result2 = minHeap.Modify(new Node(4));

        Assert.Equal(true, result);
        Assert.Equal(false, result2);
        Assert.Equal(1, minHeap.Top().Value);
        AssertHeapProperty(minHeap.ToArray());
    }

    [Fact]
    public void TestCount()
    {
        var minHeap = new RcBinaryMinHeap<Node>((x, y) => x.Value.CompareTo(y.Value));

        minHeap.Push(new Node(5));
        minHeap.Push(new Node(3));
        minHeap.Push(new Node(7));

        Assert.Equal(3, minHeap.Count);

        minHeap.Pop();

        Assert.Equal(2, minHeap.Count);

        minHeap.Clear();

        Assert.Equal(0, minHeap.Count);
    }

    [Fact]
    public void TestIsEmpty()
    {
        var minHeap = new RcBinaryMinHeap<Node>((x, y) => x.Value.CompareTo(y.Value));

        Assert.True(minHeap.IsEmpty());

        minHeap.Push(new Node(5));

        Assert.False(minHeap.IsEmpty());

        minHeap.Pop();

        Assert.True(minHeap.IsEmpty());
    }

    private void AssertHeapProperty(Node[] array)
    {
        for (int i = 0; i < array.Length / 2; i++)
        {
            int leftChildIndex = 2 * i + 1;
            int rightChildIndex = 2 * i + 2;

            // 왼쪽 자식 노드가 있는지 확인하고 비교
            if (leftChildIndex < array.Length)
                Assert.True(array[i].Value <= array[leftChildIndex].Value);

            // 오른쪽 자식 노드가 있는지 확인하고 비교
            if (rightChildIndex < array.Length)
                Assert.True(array[i].Value <= array[rightChildIndex].Value);
        }
    }
}