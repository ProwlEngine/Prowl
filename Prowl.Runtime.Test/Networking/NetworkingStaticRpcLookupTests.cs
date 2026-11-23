using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void FindingAStaticRpcByHashAllocatesNothingPerAssembly()
    {
        uint type = StaticRpcMember.HashOf(typeof(StaticRpcProbe));
        Assert.NotNull(StaticRpcMember.Find(type, 901));

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) StaticRpcMember.Find(type, 901);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 2048 * 1000, $"1000 lookups allocated {allocated} bytes");
    }

    [Fact]
    public void CheckingReadinessEachFrameAllocatesNothing()
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.IsReady, alice);
            NetworkTestScene.Current.UpdateReadiness();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) NetworkTestScene.Current.UpdateReadiness();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(allocated < 1000, $"1000 checks allocated {allocated} bytes");
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
