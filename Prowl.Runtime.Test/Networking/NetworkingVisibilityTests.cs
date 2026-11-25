using Prowl.Echo;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Courier;
using Prowl.Courier.Transports.Local;

using Xunit;

namespace Prowl.Runtime.Test;

public class VisibilityListener : NetworkBehaviour
{
    [SerializeIgnore] public NetworkDespawnReason? DespawnReason;

    public override void OnStopClient() => DespawnReason = GameObject.Network.DespawnReason;
}

/// <summary>Kept from every client by <see cref="ManualInterest" /> until it is shown to one.</summary>
public class HiddenUntilShown : Component
{
}

/// <summary>Sends an object to every client, but those it is hidden from, and one with <see cref="HiddenUntilShown" /> only to those it is shown to.</summary>
public sealed class ManualInterest : NetworkInterest
{
    public readonly Dictionary<(Guid Object, int Peer), bool> Overrides = new();

    public override bool OnCheckObserver(GameObject go, NetPeer peer) =>
        Overrides.TryGetValue((go.Identifier, peer.Id), out bool shown)
            ? shown
            : ReferenceEquals(go.GetComponent<HiddenUntilShown>(), null);

    public override void OnRebuildObservers(GameObject go, HashSet<NetPeer> observers)
    {
        foreach (NetPeer peer in Clients())
        {
            if (OnCheckObserver(go, peer)) observers.Add(peer);
        }
    }

    public override void ResetState() => Overrides.Clear();
}

public static class ManualVisibility
{
    public static void ShowTo(this NetworkAccessor network, NetPeer peer) => Set(network, peer, true);

    public static void HideFrom(this NetworkAccessor network, NetPeer peer) => Set(network, peer, false);

    public static void ClearVisibilityOverride(this NetworkAccessor network, NetPeer peer) => Set(network, peer, null);

    private static void Set(NetworkAccessor network, NetPeer peer, bool? shown)
    {
        ManualInterest interest = (ManualInterest)Networking.Networking.Interest!;
        (Guid, int) key = (network.Root!.Identifier, peer.Id);
        if (shown is { } value) interest.Overrides[key] = value;
        else interest.Overrides.Remove(key);
        network.RebuildObservers();
    }
}

public class ShowOnSpawn : NetworkBehaviour
{
    [SerializeIgnore] public static NetPeer? Peer;

    public override void OnStartClient()
    {
        if (Peer != null) GameObject.Network.ShowTo(Peer);
    }
}

public class VisibilityPeerListener : Component, INetworkListener
{
    [SerializeIgnore] public Action<NetPeer>? Connected, Disconnected;

    public void OnPeerConnected(NetPeer peer) => Connected?.Invoke(peer);

    public void OnPeerDisconnected(NetPeer peer, SessionEndReason reason) => Disconnected?.Invoke(peer);
}

/// <summary>A host RPC taking a GameObject, as the script compiler generates it.</summary>
public class PointerTestComponent : NetworkBehaviour
{
    public const uint PointId = 501;

    [SerializeIgnore] public readonly List<(GameObject? Target, NetPeer? Caller)> Points = new();

    private static readonly RpcMember<PointerTestComponent> __rpc_501 = new(PointId, "Point(GameObject)", RpcMode.Host, RpcFlags.AnyCaller,
        static (PointerTestComponent s, ref NetReader r, bool execute) => { GameObject? a0 = NetSerializer<GameObject?>.Read(ref r); if (!execute) return; RpcMember.ArgumentsRead(); s.Point(a0); });

    public void Point(GameObject? target)
    {
        if (__rpc_501.Begin(this, out NetWriter w)) { NetSerializer<GameObject?>.Write(w, target); if (!__rpc_501.Send(this, w)) return; }
        Points.Add((target, Rpc.Caller));
    }
}

public partial class NetworkingTests
{
    private sealed record VisibilitySession(Scene World, FakePeer Alice, FakePeer Bob, LocalNetwork Network) : IDisposable
    {
        public NetPeer AliceHere => HostPeerOf(Alice);
        public NetPeer BobHere => HostPeerOf(Bob);

        public void Dispose()
        {
            Networking.Networking.Shutdown();
            Networking.Networking.Interest = null;
            Alice.Dispose();
            Bob.Dispose();
        }
    }

    private static NetPeer HostPeerOf(FakePeer peer) =>
        Networking.Networking.Session!.Peers.Single(p => p.Id == peer.Session.LocalPeer!.Id);

    private VisibilitySession VisibilityHost(Action<Scene>? setUp = null)
    {
        Scene world = CreateScene();
        world.Add(CreateGameObject("Level"));
        setUp?.Invoke(world);
        MakeCurrent(world);
        LocalNetwork network = new();
        Networking.Networking.Interest = new ManualInterest();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        FakePeer alice = FakePeer.Join(network, "alice"), bob = FakePeer.Join(network, "bob");
        Pump(() => alice.IsReady && bob.IsReady, alice, bob);
        return new VisibilitySession(world, alice, bob, network);
    }

    private GameObject Crate(Scene world, NetworkVisibility visibility, string name = "Crate")
    {
        GameObject crate = CreateGameObject(name);
        crate.AddComponent<NetTestComponent>();
        crate.AddComponent<VisibilityListener>();
        crate.Network.Visibility = visibility;
        world.Add(crate);
        return crate;
    }

    private GameObject HiddenCrate(Scene world, string name = "Crate")
    {
        GameObject crate = Crate(world, NetworkVisibility.Default, name);
        crate.AddComponent<HiddenUntilShown>();
        return crate;
    }

    private static bool Contains(byte[] data, byte[] part)
    {
        for (int i = 0; i + part.Length <= data.Length; i++)
        {
            if (data.AsSpan(i, part.Length).SequenceEqual(part)) return true;
        }

        return false;
    }

    /// <summary>True if any message <paramref name="peer" /> received carries one of these identifiers as raw bytes.</summary>
    private static bool Mentions(FakePeer peer, params Guid[] ids) =>
        peer.WireReceived.Any(m => ids.Any(id => Contains(m.Data, id.ToByteArray())));

    private static int Count(FakePeer peer, ushort id) => peer.Received.Count(m => m.Id == id);

    private static int IndexOf(FakePeer peer, ushort id, int from = 0) => peer.Received.FindIndex(from, m => m.Id == id);

    private static (Guid Id, int Owner, bool Replace, int? Health) ReadSpawn(byte[] payload, Guid component)
    {
        NetReader reader = new(payload);
        SpawnMessage message = SpawnMessage.Read(ref reader);
        (Guid id, int owner, bool replace) = (message.Identifier, message.OwnerId, message.Replace);
        int? health = null;
        foreach ((Guid target, uint slot, byte[] bytes) in SpawnValues(payload))
        {
            NetReader value = new(bytes);
            if (target == component && slot == NetTestComponent.HealthSlot) health = NetSerializer<int>.Read(ref value);
        }

        return (id, owner, replace, health);
    }

    [Fact]
    public void AHiddenPeerReceivesNothingAboutTheObject()
    {
        using VisibilitySession session = VisibilityHost();
        GameObject crate = HiddenCrate(session.World);
        NetTestComponent stats = crate.GetComponent<NetTestComponent>()!;
        NetCollectionTestComponent bag = crate.AddComponent<NetCollectionTestComponent>();
        crate.AddComponent<NetworkTransform>().Interpolate = false;
        crate.SpawnBuilt();
        crate.Network.ShowTo(session.AliceHere);

        stats.Health = 7;
        bag.Scores.Add(3);
        crate.Transform.LocalPosition = new Float3(1, 2, 3);
        stats.Shout("hello");
        crate.Network.Orphaned = NetworkOrphaned.ClearOwner;
        Pump(() => session.Alice.SyncInt(stats.Identifier, NetTestComponent.HealthSlot) == 7 &&
                   session.Alice.Has(FakePeer.Sync) && session.Alice.Has(FakePeer.RpcFromHost), session.Alice, session.Bob);
        crate.Network.AssignOwnership(session.AliceHere);
        crate.Network.AssignOwnership(null);
        Pump(() => session.Alice.Has(FakePeer.OwnershipState), session.Alice, session.Bob);
        PumpFor(50, session.Alice, session.Bob);

        uint netId = NetworkTestScene.Current.NetIdOf(crate);
        Assert.False(Mentions(session.Bob, crate.Identifier, stats.Identifier, bag.Identifier));
        Assert.DoesNotContain(session.Bob.WireReceived.Where(m => m.Id == FakePeer.Transform)
            .SelectMany(m => ReadTransforms(m.Data)), r => r.NetId == netId);
        Assert.Contains(session.Alice.WireReceived.Where(m => m.Id == FakePeer.Transform)
            .SelectMany(m => ReadTransforms(m.Data)), r => r.NetId == netId);
        Assert.False(crate.Network.IsVisibleTo(session.BobHere));
        Assert.True(crate.Network.IsVisibleTo(session.AliceHere));
    }

    [Fact]
    public void ShowToSendsTheWholeObjectAheadOfAnRpcCalledRightAfter()
    {
        using VisibilitySession session = VisibilityHost();
        GameObject crate = HiddenCrate(session.World);
        NetTestComponent stats = crate.GetComponent<NetTestComponent>()!;
        crate.SpawnBuilt();
        stats.Health = 7;
        PumpFor(30, session.Alice, session.Bob);
        Assert.False(session.Alice.Has(FakePeer.Spawn));

        crate.Network.ShowTo(session.AliceHere);
        stats.Shout("hi");
        Pump(() => session.Alice.Has(FakePeer.RpcFromHost), session.Alice, session.Bob);

        Assert.InRange(IndexOf(session.Alice, FakePeer.Spawn), 0, IndexOf(session.Alice, FakePeer.RpcFromHost) - 1);
        (Guid id, _, bool replace, int? health) = ReadSpawn(session.Alice.Last(FakePeer.Spawn), stats.Identifier);
        Assert.Equal(crate.Identifier, id);
        Assert.True(replace);
        Assert.Equal(7, health);
        Assert.False(Mentions(session.Bob, crate.Identifier));
    }

    [Fact]
    public void HideFromDespawnsTheObjectThereAsOutOfInterest()
    {
        using VisibilitySession session = VisibilityHost();
        GameObject crate = Crate(session.World, NetworkVisibility.Default);
        crate.SpawnBuilt();
        Pump(() => session.Alice.Has(FakePeer.Spawn) && session.Bob.Has(FakePeer.Spawn), session.Alice, session.Bob);

        crate.Network.HideFrom(session.AliceHere);
        Pump(() => session.Alice.Has(FakePeer.Despawn), session.Alice, session.Bob);
        NetReader reader = new(session.Alice.Last(FakePeer.Despawn));
        DespawnMessage despawn = DespawnMessage.Read(ref reader);
        Assert.Equal(crate.Identifier, despawn.Identifier);
        Assert.Equal(NetworkDespawnReason.OutOfInterest, despawn.Reason);
        Assert.False(session.Bob.Has(FakePeer.Despawn));
        Assert.True(crate.IsValid());
        Assert.False(crate.Network.IsVisibleTo(session.AliceHere));

        crate.Network.ClearVisibilityOverride(session.AliceHere);
        Pump(() => Count(session.Alice, FakePeer.Spawn) == 2, session.Alice, session.Bob);
        Assert.True(ReadSpawn(session.Alice.Last(FakePeer.Spawn), Guid.Empty).Replace);
    }

    [Fact]
    public void DestroyingAnObjectSendsTheSameDespawnAsBefore()
    {
        using VisibilitySession session = VisibilityHost();
        GameObject crate = Crate(session.World, NetworkVisibility.Default);
        crate.SpawnBuilt();
        Pump(() => session.Alice.Has(FakePeer.Spawn), session.Alice, session.Bob);
        Assert.False(ReadSpawn(session.Alice.Last(FakePeer.Spawn), Guid.Empty).Replace);
        crate.Destroy();
        Pump(() => session.Alice.Has(FakePeer.Despawn) && session.Bob.Has(FakePeer.Despawn), session.Alice, session.Bob);
        Assert.Equal(crate.Identifier.ToByteArray(), session.Alice.Last(FakePeer.Despawn));
    }

    [Fact]
    public void TheOwnerAlwaysHasItsObject()
    {
        using VisibilitySession session = VisibilityHost();
        GameObject crate = HiddenCrate(session.World);
        crate.SpawnBuilt(session.AliceHere);
        Pump(() => session.Alice.Has(FakePeer.Spawn), session.Alice, session.Bob);
        Assert.False(ReadSpawn(session.Alice.Last(FakePeer.Spawn), Guid.Empty).Replace);
        Assert.True(crate.Network.IsVisibleTo(session.AliceHere));
        Assert.False(crate.Network.IsVisibleTo(session.BobHere));

        crate.Network.AssignOwnership(session.BobHere);
        Pump(() => session.Bob.Has(FakePeer.Spawn) && session.Alice.Has(FakePeer.Despawn), session.Alice, session.Bob);
        (Guid id, int owner, bool replace, _) = ReadSpawn(session.Bob.Last(FakePeer.Spawn), Guid.Empty);
        Assert.Equal(crate.Identifier, id);
        Assert.Equal(session.BobHere.Id, owner);
        Assert.True(replace);
        Assert.False(session.Bob.Has(FakePeer.Owner));
        Assert.False(session.Bob.Has(FakePeer.OwnershipState));
        Assert.InRange(IndexOf(session.Alice, FakePeer.Owner), 0, session.Alice.Received.FindLastIndex(m => m.Id == FakePeer.Despawn) - 1);
        Assert.False(crate.Network.IsVisibleTo(session.AliceHere));
    }

    [Fact]
    public void HidingAnObjectFromItsOwnerIsIgnored()
    {
        using VisibilitySession session = VisibilityHost();
        GameObject crate = Crate(session.World, NetworkVisibility.Default);
        crate.SpawnBuilt(session.AliceHere);
        Pump(() => session.Alice.Has(FakePeer.Spawn), session.Alice, session.Bob);

        crate.Network.HideFrom(session.AliceHere);
        PumpFor(50, session.Alice, session.Bob);
        Assert.False(session.Alice.Has(FakePeer.Despawn));
        Assert.True(crate.Network.IsVisibleTo(session.AliceHere));
    }

    [Fact]
    public void ForceShownAndForceHiddenWinOverTheInterest()
    {
        using VisibilitySession session = VisibilityHost();
        GameObject shown = Crate(session.World, NetworkVisibility.ForceShown, "Shown");
        shown.SpawnBuilt();
        shown.Network.HideFrom(session.AliceHere);
        Assert.True(shown.Network.IsVisibleTo(session.AliceHere));

        GameObject secret = Crate(session.World, NetworkVisibility.ForceHidden, "Secret");
        secret.SpawnBuilt(session.AliceHere);
        secret.Network.ShowTo(session.BobHere);
        Assert.False(secret.Network.IsVisibleTo(session.BobHere));
        Assert.True(secret.Network.IsVisibleTo(session.AliceHere));

        secret.Network.Visibility = NetworkVisibility.Default;
        Assert.True(secret.Network.IsVisibleTo(session.BobHere));
        Pump(() => session.Bob.Received.Any(m => m.Id == FakePeer.Spawn && ReadSpawn(m.Data, Guid.Empty).Id == secret.Identifier),
            session.Alice, session.Bob);
        secret.Network.Visibility = NetworkVisibility.ForceHidden;
        Assert.False(secret.Network.IsVisibleTo(session.BobHere));
        Assert.True(secret.Network.IsVisibleTo(session.AliceHere));
    }

    [Fact]
    public void HidingAndShowingInOneFrameSendsADespawnThenAReplacingSpawn()
    {
        using VisibilitySession session = VisibilityHost();
        GameObject crate = Crate(session.World, NetworkVisibility.Default);
        crate.SpawnBuilt();
        Pump(() => session.Alice.Has(FakePeer.Spawn), session.Alice, session.Bob);

        crate.Network.HideFrom(session.AliceHere);
        crate.Network.ShowTo(session.AliceHere);
        Pump(() => Count(session.Alice, FakePeer.Spawn) == 2, session.Alice, session.Bob);
        int despawn = IndexOf(session.Alice, FakePeer.Despawn);
        Assert.InRange(despawn, 0, session.Alice.Received.FindLastIndex(m => m.Id == FakePeer.Spawn) - 1);
        Assert.True(ReadSpawn(session.Alice.Last(FakePeer.Spawn), Guid.Empty).Replace);
    }

    [Fact]
    public void AClientReplacesAnObjectThatLeftAndCameBackInTheSameFrame()
    {
        Scene scene = CreateScene();
        using NetWriter snapshot = new(), spawn = new(), despawn = new(), back = new();
        NetworkTestScene.Current.WriteScene(scene, snapshot);
        GameObject crate = CreateGameObject("Crate");
        crate.Network.Networked = true;
        NetTestComponent component = crate.AddComponent<NetTestComponent>();
        crate.AddComponent<VisibilityListener>();
        NetworkTestScene.Current.WriteSpawn(crate, -1, NetworkOrphaned.Destroy, spawn, generation: 1, netId: 5);
        new DespawnMessage(crate.Identifier, NetworkDespawnReason.OutOfInterest).Write(despawn);
        component.Health = 9;
        NetworkTestScene.Current.WriteSpawn(crate, -1, NetworkOrphaned.Destroy, back, generation: 1, netId: 5, replace: true);

        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer => host.Session.SendRaw(peer, FakePeer.Scene, snapshot.WrittenSpan);
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady, host);
            NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);
            host.Session.SendRaw(client, FakePeer.Spawn, spawn.WrittenSpan);
            host.Session.Flush();
            Pump(() => Scene.Main.Objects.Any(o => o.Identifier == crate.Identifier), host);
            GameObject first = Find(Scene.Main, crate.Identifier);
            VisibilityListener listener = first.GetComponent<VisibilityListener>()!;

            host.Session.SendRaw(client, FakePeer.Despawn, despawn.WrittenSpan);
            host.Session.SendRaw(client, FakePeer.Spawn, back.WrittenSpan);
            host.Session.Flush();
            Pump(() => first.IsDisposed, host);
            PumpFor(30, host);

            GameObject second = Find(Scene.Main, crate.Identifier);
            Assert.NotSame(first, second);
            Assert.False(second.IsDisposed);
            Assert.Equal(9, second.GetComponent<NetTestComponent>()!.Health);
            Assert.True(second.Network.Active);
            Assert.Equal(NetworkDespawnReason.OutOfInterest, listener.DespawnReason);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AHiddenPeerCannotCallRpcsOnTheObject()
    {
        using VisibilitySession session = VisibilityHost();
        GameObject crate = HiddenCrate(session.World);
        NetTestComponent stats = crate.GetComponent<NetTestComponent>()!;
        crate.SpawnBuilt();
        crate.Network.ShowTo(session.AliceHere);

        session.Bob.Send(FakePeer.RpcToHost, w => { w.WriteGuid(stats.Identifier); w.WriteUInt32(NetTestComponent.AskHostId); w.WriteVarInt32(1); });
        PumpFor(50, session.Alice, session.Bob);
        session.Alice.Send(FakePeer.RpcToHost, w => { w.WriteGuid(stats.Identifier); w.WriteUInt32(NetTestComponent.AskHostId); w.WriteVarInt32(2); });
        Pump(() => stats.Asked.Count > 0, session.Alice, session.Bob);

        Assert.Equal(new[] { 2 }, stats.Asked);
    }

    public class LinkHolder : Component
    {
        public GameObject? Target;
        public Component? Part;
    }

    [Fact]
    public void ASceneChangeSendsEachClientOnlyThePersistentObjectsItHas()
    {
        GameObject? chest = null;
        using VisibilitySession session = VisibilityHost(world =>
        {
            chest = HiddenCrate(world, "Chest");
            chest.Network.Networked = true;
            chest.Persistent = true;
        });
        chest!.Network.ShowTo(session.AliceHere);
        Pump(() => session.Alice.Has(FakePeer.Spawn), session.Alice, session.Bob);

        session.World.ReplaceContent(new SceneContent { Objects = [new GameObject("Next")] }, "Next", null);
        Pump(() => Count(session.Alice, FakePeer.Scene) == 2 && Count(session.Bob, FakePeer.Scene) == 2,
            session.Alice, session.Bob);
        Assert.Contains(chest.Identifier, SpawnedIds(session.Alice));
        Assert.DoesNotContain(chest.Identifier, SpawnedIds(session.Bob));
        Assert.False(Mentions(session.Bob, chest.Identifier));
        Assert.True(chest.Network.IsVisibleTo(session.AliceHere));
        Assert.False(chest.Network.IsVisibleTo(session.BobHere));
    }

    [Fact]
    public void APeerLeavingIsHiddenFromNothing()
    {
        using VisibilitySession session = VisibilityHost();
        GameObject shown = HiddenCrate(session.World, "Shown");
        GameObject hidden = Crate(session.World, NetworkVisibility.Default, "Hidden");
        shown.SpawnBuilt();
        hidden.SpawnBuilt();
        shown.Network.ShowTo(session.AliceHere);
        hidden.Network.HideFrom(session.AliceHere);
        Pump(() => session.Alice.Has(FakePeer.Despawn), session.Alice, session.Bob);

        int aliceId = session.AliceHere.Id;
        session.Alice.Session.Shutdown();
        Pump(() => Networking.Networking.Session!.Peers.All(p => p.Id != aliceId), session.Alice, session.Bob);
        NetworkSystem system = NetworkTestScene.Current;
        Assert.DoesNotContain(system.Entries.Values, e => e.HiddenFrom?.Contains(aliceId) == true);
        using FakePeer carol = FakePeer.Join(session.Network, "carol");
        Pump(() => carol.IsReady, carol, session.Bob);

        List<Guid> carolHas = SpawnedIds(carol);
        Assert.DoesNotContain(shown.Identifier, carolHas);
        Assert.Contains(hidden.Identifier, carolHas);
        NetPeer carolHere = HostPeerOf(carol);
        Assert.False(shown.Network.IsVisibleTo(carolHere));
        Assert.True(hidden.Network.IsVisibleTo(carolHere));
    }

    [Fact]
    public void UnloadingASectionWithAHiddenObjectSendsNothingAboutIt()
    {
        var (world, alice) = SectionHost();
        try
        {
            GameObject chest = new("Chest");
            chest.Network.Networked = true;
            chest.Network.Visibility = NetworkVisibility.ForceHidden;
            SceneSection arena = world.AddContent(new SceneContent { Objects = [chest] }, "Arena", null);
            Pump(() => alice.Has(FakePeer.SectionAdd), alice);
            Assert.Equal(1, NetworkDiagnostics.NetworkObjectCount);

            world.UnloadNow(arena);
            Pump(() => alice.Has(FakePeer.SectionRemove), alice);
            Assert.False(alice.Has(FakePeer.Despawn));
            Assert.False(Mentions(alice, chest.Identifier));
            Assert.Equal(0, NetworkDiagnostics.NetworkObjectCount);
        }
        finally
        {
            Networking.Networking.Shutdown();
            alice.Dispose();
        }
    }

    [Fact]
    public void NetworkVisibilityIsStoredOnlyWhenSet()
    {
        GameObject go = CreateGameObject();
        go.Network.Networked = true;
        EchoObject plain = Serializer.Serialize(go);
        Assert.False(plain["Network"].TryGet("Visibility", out _));

        go.Network.Visibility = NetworkVisibility.ForceHidden;
        EchoObject stored = Serializer.Serialize(go);
        Assert.Equal((byte)NetworkVisibility.ForceHidden, stored["Network"]["Visibility"].ByteValue);
        GameObject copy = Serializer.Deserialize<GameObject>(stored)!;
        Assert.Equal(NetworkVisibility.ForceHidden, copy.Network.Visibility);
        copy.Dispose();

        // The old Manual visibility, 3, still hides the object.
        stored["Network"]["Visibility"] = new EchoObject((byte)3);
        GameObject old = Serializer.Deserialize<GameObject>(stored)!;
        Assert.Equal(NetworkVisibility.ForceHidden, old.Network.Visibility);
        old.Dispose();

        GameObject reset = Serializer.Deserialize<GameObject>(plain)!;
        Assert.Equal(NetworkVisibility.Default, reset.Network.Visibility);
        reset.Dispose();
    }

    [Fact]
    public void ShowToFromOnNetworkSpawnOfALoadedSectionTravelsInsideTheSection()
    {
        using VisibilitySession session = VisibilityHost();
        try
        {
            ShowOnSpawn.Peer = session.AliceHere;
            GameObject chest = new("Chest");
            chest.Network.Networked = true;
            chest.AddComponent<HiddenUntilShown>();
            chest.AddComponent<ShowOnSpawn>();
            session.World.AddContent(new SceneContent { Objects = [chest] }, "Arena", null);
            Pump(() => session.Alice.Has(FakePeer.SectionAdd) && session.Bob.Has(FakePeer.SectionAdd), session.Alice, session.Bob);
            PumpFor(10, session.Alice, session.Bob);

            Assert.Contains(chest.Identifier, SpawnedIds(session.Alice));
            Assert.False(Mentions(session.Bob, chest.Identifier));
            Assert.True(chest.Network.IsVisibleTo(session.AliceHere));
        }
        finally { ShowOnSpawn.Peer = null; }
    }

    [Fact]
    public void ShowToFromOnNetworkSpawnOfALoadedSceneSpawnsItForThatPeerOnly()
    {
        using VisibilitySession session = VisibilityHost();
        try
        {
            ShowOnSpawn.Peer = session.AliceHere;
            GameObject chest = new("Chest");
            chest.Network.Networked = true;
            chest.AddComponent<HiddenUntilShown>();
            chest.AddComponent<ShowOnSpawn>();
            session.World.ReplaceContent(new SceneContent { Objects = [chest] }, "Next", null);
            Pump(() => Count(session.Alice, FakePeer.Scene) == 2 && Count(session.Bob, FakePeer.Scene) == 2,
                session.Alice, session.Bob);
            Pump(() => session.Alice.IsReady && session.Bob.IsReady, session.Alice, session.Bob);
            PumpFor(10, session.Alice, session.Bob);

            Assert.Contains(chest.Identifier, SpawnedIds(session.Alice));
            Assert.False(Mentions(session.Bob, chest.Identifier));
        }
        finally { ShowOnSpawn.Peer = null; }
    }

    [Fact]
    public void ReferencesFromAClientToObjectsItDoesNotHaveReadAsNullOnTheHost()
    {
        using VisibilitySession session = VisibilityHost();
        GameObject lever = CreateGameObject("Lever");
        PointerTestComponent pointer = lever.AddComponent<PointerTestComponent>();
        SyncReferenceTestComponent holder = lever.AddComponent<SyncReferenceTestComponent>().OwnerWritten();
        session.World.Add(lever);
        lever.SpawnBuilt(session.BobHere);
        GameObject crate = HiddenCrate(session.World);
        crate.SpawnBuilt();
        crate.Network.ShowTo(session.AliceHere);
        Pump(() => session.Bob.Has(FakePeer.Spawn), session.Alice, session.Bob);

        session.Bob.Send(FakePeer.RpcToHost, w => { w.WriteGuid(pointer.Identifier); w.WriteUInt32(PointerTestComponent.PointId); NetSerializer<GameObject>.Write(w, crate); });
        session.Alice.Send(FakePeer.RpcToHost, w => { w.WriteGuid(pointer.Identifier); w.WriteUInt32(PointerTestComponent.PointId); NetSerializer<GameObject>.Write(w, crate); });
        Pump(() => pointer.Points.Count == 2, session.Alice, session.Bob);
        Assert.Contains((null, session.BobHere), pointer.Points);
        Assert.Contains((crate, session.AliceHere), pointer.Points);

        void SendTarget(GameObject target) => session.Bob.Send(FakePeer.Sync, w =>
        {
            w.WriteVarUInt32(1);
            w.WriteGuid(holder.Identifier);
            w.WriteVarUInt32(SyncReferenceTestComponent.TargetSlot);
            using NetWriter v = new();
            NetSerializer<GameObject>.Write(v, target);
            w.WriteBytesWithLength(v.WrittenSpan);
        });

        SendTarget(lever);
        Pump(() => holder.Target == lever, session.Alice, session.Bob);
        SendTarget(crate);
        Pump(() => holder.Target != lever, session.Alice, session.Bob);
        Assert.Null(holder.Target);
    }

    [Fact]
    public void APeerIsHiddenFromRestrictedObjectsWhileItConnects()
    {
        VisibilityPeerListener? listener = null;
        using VisibilitySession session = VisibilityHost(world =>
        {
            GameObject rules = CreateGameObject("Rules");
            listener = rules.AddComponent<VisibilityPeerListener>();
            world.Add(rules);
        });
        GameObject crate = HiddenCrate(session.World);
        NetTestComponent stats = crate.GetComponent<NetTestComponent>()!;
        crate.SpawnBuilt();
        bool? visible = null;
        listener!.Connected = peer =>
        {
            visible = crate.Network.IsVisibleTo(peer);
            stats.Shout("welcome");
        };

        using FakePeer carol = FakePeer.Join(session.Network, "carol");
        Pump(() => carol.IsReady, carol, session.Alice, session.Bob);
        Assert.False(visible);
        Assert.False(Mentions(carol, crate.Identifier, stats.Identifier));
    }

    [Fact]
    public void AnObjectMadeBeforeTheWorldWasSentButNeverInItCanSpawnHidden()
    {
        GameObject pooled = CreateGameObject("Pooled");
        using VisibilitySession session = VisibilityHost();
        session.World.Add(pooled);
        pooled.Network.Visibility = NetworkVisibility.ForceHidden;
        pooled.SpawnBuilt();
        Assert.True(pooled.Network.Active);
        PumpFor(10, session.Alice, session.Bob);
        Assert.False(Mentions(session.Alice, pooled.Identifier));
    }
}
