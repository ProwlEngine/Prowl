using System.Diagnostics;

using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void AnUnknownIdentifierIsAMissWithoutWalkingTheWorld()
    {
        SceneInstance scene = CreateScene();
        for (int i = 0; i < 20_000; i++)
        {
            GameObject go = new($"Filler {i}");
            go.AddComponent<NetTestComponent>();
            scene.Add(go);
        }

        GameObject late = new("Late");
        scene.Add(late);
        MakeCurrent(scene);
        Networking.Networking.Host(new LocalTransport(new(), "host"));
        try
        {
            NetTestComponent added = late.AddComponent<NetTestComponent>();
            Assert.Same(added, NetworkTestScene.Current.FindByIdentifier(added.Identifier));

            Stopwatch watch = Stopwatch.StartNew();
            for (int i = 0; i < 200; i++)
            {
                NetworkTestScene.Current.Frame++;
                Assert.Null(NetworkTestScene.Current.FindByIdentifier(Guid.NewGuid()));
            }

            Assert.True(watch.ElapsedMilliseconds < 200, $"200 misses took {watch.ElapsedMilliseconds} ms");
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
