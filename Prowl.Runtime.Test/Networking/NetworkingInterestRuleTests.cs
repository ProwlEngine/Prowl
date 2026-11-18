using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void AnInterestRuleDecidesWhichClientsGetAnObject()
    {
        SceneInstance scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.IsReady, alice);
            Networking.Networking.Interest!.Rule = (go, peer) => go.Name == "Secret" ? false : null;
            GameObject secret = CreateGameObject("Secret"), plain = CreateGameObject("Plain");
            scene.Add(secret);
            scene.Add(plain);
            secret.Network.SpawnSerialized();
            plain.Network.SpawnSerialized();
            Pump(() => alice.Received.Any(m => m.Id == FakePeer.Spawn && ReadSpawn(m.Data, Guid.Empty).Id == plain.Identifier), alice);
            Assert.DoesNotContain(alice.Received, m => m.Id == FakePeer.Spawn && ReadSpawn(m.Data, Guid.Empty).Id == secret.Identifier);

            Networking.Networking.Interest!.Rule = null;
            Pump(() => alice.Received.Any(m => m.Id == FakePeer.Spawn && ReadSpawn(m.Data, Guid.Empty).Id == secret.Identifier), alice);
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
