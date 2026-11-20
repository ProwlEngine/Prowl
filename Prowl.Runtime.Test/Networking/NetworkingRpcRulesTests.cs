using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void TheHostDropsBroadcastAndOwnerRpcsSentByAClient()
    {
        SceneInstance scene = CreateScene();
        GameObject ship = CreateGameObject("Ship");
        NetTestComponent component = ship.AddComponent<NetTestComponent>();
        scene.Add(ship);
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        using FakePeer bob = FakePeer.Join(network, "bob");
        try
        {
            Pump(() => alice.Has(FakePeer.Scene) && bob.Has(FakePeer.Scene), alice, bob);
            ship.Network.SpawnSerialized(Networking.Networking.Session!.Peers.Single(p => p.Name == "bob"));
            Pump(() => alice.Has(FakePeer.Spawn) && bob.Has(FakePeer.Spawn), alice, bob);

            alice.Send(FakePeer.RpcToHost, w => { w.WriteGuid(component.Identifier); w.WriteUInt32(NetTestComponent.ShoutId); w.WriteString("hello"); });
            alice.Send(FakePeer.RpcToHost, w => { w.WriteGuid(component.Identifier); w.WriteUInt32(204); w.WriteString("psst"); });
            alice.Send(FakePeer.RpcToHost, w => { w.WriteGuid(component.Identifier); w.WriteUInt32(NetTestComponent.AskHostId); w.WriteVarInt32(3); });
            Pump(() => component.Asked.Contains(3), alice, bob);
            PumpFor(30, alice, bob);

            Assert.Empty(component.Shouts);
            Assert.False(bob.Has(FakePeer.RpcFromHost));
            Assert.False(alice.Has(FakePeer.RpcFromHost));
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AClientCallingABroadcastOrOwnerRpcSendsNothingAndDoesNotRunIt()
    {
        SceneInstance scene = CreateScene();
        GameObject level = CreateGameObject("Level");
        level.Network.Networked = true;
        level.AddComponent<NetTestComponent>();
        scene.Add(level);
        MakeCurrent(scene);
        Guid levelId = level.Identifier;
        byte[] snapshot;
        byte[][] spawns;
        LocalNetwork source = new();
        Networking.Networking.Host(new LocalTransport(source, "host"));
        try
        {
            (snapshot, spawns) = RecordJoin(source);
        }
        finally { Networking.Networking.Shutdown(); }

        MakeCurrent(CreateScene());
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        SendJoin(host, snapshot, spawns);
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady && SceneInstance.Main.Objects.Any(o => o.Identifier == levelId), host);
            NetTestComponent here = Find(SceneInstance.Main, levelId).GetComponent<NetTestComponent>()!;

            here.Shout("hello");
            here.TellOwner("psst");
            Assert.Empty(here.Shouts);

            here.AskHost(1);
            Pump(() => host.Has(FakePeer.RpcToHost), host);
            PumpFor(30, host);
            Assert.Single(host.WireReceived, m => m.Id == FakePeer.RpcToHost);
        }
        finally { Networking.Networking.Shutdown(); }
    }

}
