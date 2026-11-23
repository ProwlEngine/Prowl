using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    // A ship whose NetTestComponent is its second networked component, on a child.
    private (GameObject Ship, NetTestComponent Component) IndexedShip(Scene scene)
    {
        GameObject ship = CreateGameObject("Ship");
        ship.AddComponent<PlainTestComponent>();
        ship.AddComponent<NetCollectionTestComponent>();
        GameObject turret = CreateGameObject("Turret");
        turret.SetParent(ship, false);
        NetTestComponent component = turret.AddComponent<NetTestComponent>();
        scene.Add(ship);
        return (ship, component);
    }

    [Fact]
    public void RpcsAreIndexedByTheirSortedIds()
    {
        NetworkMembers members = NetworkMembers.Of(typeof(NetTestComponent));
        Assert.Equal(NetTestComponent.ShoutId, members.RpcAt(0)!.Id);
        Assert.Equal(NetTestComponent.AskHostId, members.RpcAt(1)!.Id);
        Assert.Equal(NetTestComponent.ExplodeId, members.RpcAt(2)!.Id);
        Assert.Equal(204u, members.RpcAt(3)!.Id);
        Assert.Null(members.RpcAt(4));
        Assert.Equal(1, members.IndexOf(members.Rpc(NetTestComponent.AskHostId)!));
    }

    [Fact]
    public void AnRpcFromTheHostNamesItsObjectByNetIdAndComponentIndex()
    {
        Scene scene = CreateScene();
        (GameObject ship, NetTestComponent component) = IndexedShip(scene);
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.Scene), alice);
            ship.SpawnBuilt();
            Pump(() => alice.Has(FakePeer.Spawn), alice);

            component.Shout("hi");
            Pump(() => alice.Has(FakePeer.RpcFromHost), alice);
            NetReader wire = new(alice.WireReceived.Last(m => m.Id == FakePeer.RpcFromHost).Data);
            Assert.Equal((uint)Networking.Networking.Session!.LocalPeer!.Id, wire.ReadVarUInt32());
            Assert.Equal(NetworkTestScene.Current.NetIdOf(ship), wire.ReadVarUInt32());
            Assert.Equal(1, wire.ReadByte());
            Assert.Equal(0u, wire.ReadVarUInt32());
            NetReader arguments = new(wire.ReadBytesWithLength());
            Assert.Equal("hi", arguments.ReadString());
            Assert.True(wire.IsAtEnd);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AClientCallsAHostRpcByNetIdAndComponentIndex()
    {
        Scene scene = CreateScene();
        (GameObject ship, NetTestComponent component) = IndexedShip(scene);
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.Scene), alice);
            ship.SpawnBuilt();
            Pump(() => alice.Has(FakePeer.Spawn), alice);

            using NetWriter call = new(), arguments = new();
            arguments.WriteVarInt32(7);
            call.WriteVarUInt32(NetworkTestScene.Current.NetIdOf(ship));
            call.WriteByte(1);
            call.WriteVarUInt32(1);
            call.WriteBytesWithLength(arguments.WrittenSpan);
            alice.Session.SendRaw(alice.Session.HostPeer!, FakePeer.RpcToHost, call.WrittenSpan);
            alice.Session.Flush();
            Pump(() => component.Asked.Contains(7), alice);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AnRpcOnAnObjectThatIsNotNetworkedIsDroppedWhileASessionRuns()
    {
        Scene scene = CreateScene();
        GameObject prop = CreateGameObject("Prop");
        NetTestComponent component = prop.AddComponent<NetTestComponent>();
        scene.Add(prop);
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.Scene), alice);
            component.Shout("nobody hears");
            PumpFor(30, alice);
            Assert.Empty(component.Shouts);
            Assert.False(alice.Has(FakePeer.RpcFromHost));
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AStaticRpcNamesItsTypeByHash()
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        ClearStaticProbe();
        try
        {
            Pump(() => alice.IsReady, alice);
            StaticRpcProbe.Call(901, 5);
            Pump(() => alice.Has(FakePeer.StaticRpcFromHost), alice);
            NetReader wire = new(alice.WireReceived.Last(m => m.Id == FakePeer.StaticRpcFromHost).Data);
            wire.ReadVarUInt32();
            Assert.Equal(StaticRpcMember.HashOf(typeof(StaticRpcProbe)), wire.ReadUInt32());
            Assert.Equal(901u, wire.ReadUInt32());
            NetReader arguments = new(wire.ReadBytesWithLength());
            Assert.Equal(5, NetSerializer<int>.Read(ref arguments));
            Assert.True(wire.IsAtEnd);
        }
        finally { Networking.Networking.Shutdown(); ClearStaticProbe(); }
    }
}
