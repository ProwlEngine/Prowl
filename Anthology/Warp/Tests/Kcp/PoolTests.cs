// From kcp2k (MIT, see Warp/Transports/Kcp/Protocol/LICENSE.txt).

using Prowl.Warp.Transports.Kcp.Protocol;

using Xunit;

namespace Prowl.Warp.Tests.Kcp;

public class PoolTests
{
    private readonly Pool<string> pool;
    private int resetCalled;

    public PoolTests() =>
        pool = new Pool<string>(
            () => "new string",
            value => ++resetCalled,
            0
        );

    [Fact]
    public void TakeFromEmpty() =>
        // taking from an empty pool should give us a completely new string
        Assert.Equal("new string", pool.Take());

    [Fact]
    public void ReturnAndTake()
    {
        // returning and then taking should get the returned one, not a
        // newly generated one.
        pool.Return("returned");
        Assert.Equal("returned", pool.Take());
    }

    [Fact]
    public void ReturnCallsReset()
    {
        pool.Return("returned");
        pool.Return("returned");
        Assert.Equal(2, resetCalled);
    }

    [Fact]
    public void Count()
    {
        Assert.Equal(0, pool.Count);
        pool.Return("returned");
        Assert.Equal(1, pool.Count);
    }

    [Fact]
    public void Clear()
    {
        pool.Return("returned");
        pool.Clear();
        Assert.Equal(0, pool.Count);
    }
}
