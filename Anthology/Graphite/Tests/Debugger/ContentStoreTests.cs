using System.Threading.Tasks;
using Prowl.Graphite.Debugger.Data;
using Prowl.Graphite.Debugger.Trace;
using Xunit;

namespace Prowl.Graphite.Debugger.Tests;

public class ContentStoreTests
{
    [Fact]
    public void EqualBytes_YieldOneBlob()
    {
        ContentStore store = new();
        BlobRef a = store.Put(new byte[] { 1, 2, 3, 4 });
        BlobRef b = store.Put(new byte[] { 1, 2, 3, 4 });

        Assert.Equal(a, b);
        Assert.Equal(1, store.Count);
        Assert.Equal(4UL, store.TotalBytes);
    }

    [Fact]
    public void DifferentBytes_YieldDifferentBlobs()
    {
        ContentStore store = new();
        BlobRef a = store.Put(new byte[] { 1, 2, 3, 4 });
        BlobRef b = store.Put(new byte[] { 1, 2, 3, 5 });

        Assert.NotEqual(a, b);
        Assert.Equal(2, store.Count);
        Assert.Equal(8UL, store.TotalBytes);
    }

    [Fact]
    public void Get_ReturnsStoredBytes()
    {
        ContentStore store = new();
        BlobRef blob = store.Put(new byte[] { 9, 8, 7 });

        Assert.True(store.Contains(blob));
        Assert.True(store.TryGet(blob, out byte[] data));
        Assert.Equal(new byte[] { 9, 8, 7 }, data);
    }

    [Fact]
    public void Get_UnknownBlobFails()
    {
        ContentStore store = new();
        BlobRef blob = new ContentStore().Put(new byte[] { 1 });

        Assert.False(store.Contains(blob));
        Assert.False(store.TryGet(blob, out _));
    }

    [Fact]
    public void Put_CopiesInput()
    {
        ContentStore store = new();
        byte[] source = { 1, 2, 3 };
        BlobRef blob = store.Put(source);
        source[0] = 99;

        store.TryGet(blob, out byte[] data);
        Assert.Equal(new byte[] { 1, 2, 3 }, data);
    }

    [Fact]
    public void EmptyInput_IsStored()
    {
        ContentStore store = new();
        BlobRef a = store.Put(System.ReadOnlySpan<byte>.Empty);
        BlobRef b = store.Put(System.ReadOnlySpan<byte>.Empty);

        Assert.Equal(a, b);
        Assert.Equal(0UL, a.Length);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void ConcurrentPuts_Dedup()
    {
        ContentStore store = new();
        byte[] data = new byte[1024];
        Parallel.For(0, 64, _ => store.Put(data));

        Assert.Equal(1, store.Count);
        Assert.Equal(1024UL, store.TotalBytes);
    }
}
