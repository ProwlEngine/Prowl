using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
///     What the script compiler generates for <c>[Rpc.Host(RpcFlags.AnyCaller)] public void Knock(int)</c> and
///     <c>[Rpc.Host] public void Ring(int)</c>.
/// </summary>
public class OpenDoorTestComponent : NetworkBehaviour
{
    public const uint KnockId = 901, RingId = 902;
    public readonly List<int> Knocks = new(), Rings = new();

    private static readonly RpcMember<OpenDoorTestComponent> __rpc_902 = new(RingId, "Ring(int)", RpcMode.Host, RpcFlags.None,
        static (OpenDoorTestComponent s, ref NetReader r, bool execute) => { int a0 = NetSerializer<int>.Read(ref r); if (!execute) return; RpcMember.ArgumentsRead(); s.Ring(a0); });

    public void Ring(int value)
    {
        if (__rpc_902.Begin(this, out NetWriter w)) { NetSerializer<int>.Write(w, value); if (!__rpc_902.Send(this, w)) return; }
        Rings.Add(value);
    }

    private static readonly RpcMember<OpenDoorTestComponent> __rpc_901 = new(KnockId, "Knock(int)", RpcMode.Host, RpcFlags.AnyCaller,
        static (OpenDoorTestComponent s, ref NetReader r, bool execute) => { int a0 = NetSerializer<int>.Read(ref r); if (!execute) return; RpcMember.ArgumentsRead(); s.Knock(a0); });

    public void Knock(int value)
    {
        if (__rpc_901.Begin(this, out NetWriter w)) { NetSerializer<int>.Write(w, value); if (!__rpc_901.Send(this, w)) return; }
        Knocks.Add(value);
    }
}

public partial class NetworkingTests
{
    [Fact]
    public void AClientRpcNeedsAuthorityUnlessItTakesAnyCaller()
    {
        Scene scene = CreateScene();
        GameObject door = CreateGameObject("Door");
        OpenDoorTestComponent open = door.AddComponent<OpenDoorTestComponent>();
        scene.Add(door);
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.IsReady, alice);
            door.SpawnBuilt();
            Pump(() => alice.Has(FakePeer.Spawn), alice);

            alice.Send(FakePeer.RpcToHost, w => { w.WriteGuid(open.Identifier); w.WriteUInt32(OpenDoorTestComponent.RingId); w.WriteVarInt32(1); });
            alice.Send(FakePeer.RpcToHost, w => { w.WriteGuid(open.Identifier); w.WriteUInt32(OpenDoorTestComponent.KnockId); w.WriteVarInt32(2); });
            Pump(() => open.Knocks.Count == 1, alice);
            PumpFor(5, alice);
            Assert.Empty(open.Rings);

            door.Network.AssignOwnership(Networking.Networking.Session!.Peers.Single(p => p.Name == "alice"));
            alice.Send(FakePeer.RpcToHost, w => { w.WriteGuid(open.Identifier); w.WriteUInt32(OpenDoorTestComponent.RingId); w.WriteVarInt32(3); });
            Pump(() => open.Rings.Count == 1, alice);
            Assert.Equal(3, open.Rings[0]);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AnOwnerWritesOnlyItsClientToServerBehaviours()
    {
        Scene scene = CreateScene();
        GameObject ship = CreateGameObject("Ship");
        NetTestComponent component = ship.AddComponent<NetTestComponent>().OwnerWritten();
        NetCollectionTestComponent hostSide = ship.AddComponent<NetCollectionTestComponent>();
        scene.Add(ship);
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.IsReady, alice);
            ship.SpawnBuilt(Networking.Networking.Session!.Peers.Single(p => p.Name == "alice"));
            Pump(() => alice.Has(FakePeer.Spawn), alice);

            alice.SendSync(hostSide.Identifier, NetCollectionTestComponent.HealthSlot, 7);
            alice.SendSync(component.Identifier, NetTestComponent.HealthSlot, 5);
            Pump(() => component.Health == 5, alice);
            PumpFor(5, alice);
            Assert.Equal(100, hostSide.Health);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AWriteFromAPeerWithoutControlWarnsOnce()
    {
        Scene hostWorld = CreateScene();
        using NetWriter snapshot = new(), spawn = new();
        NetworkTestScene.Current.WriteScene(hostWorld, snapshot);
        GameObject remote = CreateGameObject("Remote");
        remote.AddComponent<NetTestComponent>();
        remote.Network.Networked = true;
        NetworkTestScene.Current.WriteSpawn(remote, owner: -1, NetworkOrphaned.Destroy, spawn, generation: 1);
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer =>
        {
            host.Session.SendRaw(peer, FakePeer.Scene, snapshot.WrittenSpan);
            host.Session.SendRaw(peer, FakePeer.Spawn, spawn.WrittenSpan);
        };
        List<string> warnings = new();
        void Capture(LogEvent e)
        {
            if (e.Severity == LogSeverity.Warning) warnings.Add(e.Message);
        }

        Debug.OnLog += Capture;
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady && Scene.Main.Objects.Any(o => o.Identifier == remote.Identifier), host);
            NetTestComponent copy = Find(Scene.Main, remote.Identifier).GetComponent<NetTestComponent>()!;
            copy.Health = 5;
            copy.Health = 6;
            Assert.Equal(100, copy.Health);
            Assert.Single(warnings, w => w.Contains("Health") && w.Contains("without authority"));
        }
        finally
        {
            Debug.OnLog -= Capture;
            Networking.Networking.Shutdown();
        }
    }
}
