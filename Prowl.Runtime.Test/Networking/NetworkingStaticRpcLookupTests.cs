using Prowl.Runtime.Networking;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void FindingAStaticRpcByNameAllocatesNothingPerAssembly()
    {
        string typeName = $"{typeof(StaticRpcProbe).FullName}, {typeof(StaticRpcProbe).Assembly.GetName().Name}";
        Assert.NotNull(StaticRpcMember.Find(typeName, 901));

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) StaticRpcMember.Find(typeName, 901);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 2048 * 1000, $"1000 lookups allocated {allocated} bytes");
    }
}
