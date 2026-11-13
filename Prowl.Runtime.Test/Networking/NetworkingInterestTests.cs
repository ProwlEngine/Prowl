using System.Diagnostics;

using Prowl.Echo;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Courier;
using Prowl.Courier.Transports.Local;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>A broadcast RPC taking a GameObject, as the script compiler generates it.</summary>
public class BeaconTestComponent : Component
{
    public const uint MarkId = 601;

    [SerializeIgnore] public readonly List<GameObject?> Marks = new();

    private static readonly RpcMember<BeaconTestComponent> __rpc_601 = new(MarkId, "Mark(GameObject)", RpcMode.Broadcast, RpcFlags.None,
        static (BeaconTestComponent s, ref NetReader r, bool execute) => { GameObject? a0 = NetSerializer<GameObject?>.Read(ref r); if (!execute) return; RpcMember.ArgumentsRead(); s.Mark(a0); });

    public void Mark(GameObject? target)
    {
        if (__rpc_601.Begin(this, out NetWriter w)) { NetSerializer<GameObject?>.Write(w, target); if (!__rpc_601.Send(this, w)) return; }
        Marks.Add(target);
    }
}

public readonly record struct TestReferencePair(GameObject? First, GameObject? Second);

/// <summary>A [Sync] value naming two objects at once.</summary>
public class SyncPairTestComponent : Component
{
    public const uint PairSlot = 402;
    private TestReferencePair _pair;
    private static readonly SyncMember<SyncPairTestComponent, TestReferencePair> PairState = new(PairSlot, "Pair",
        SyncFlags.None, static c => c._pair, static (c, v) => c._pair = v);
    public TestReferencePair Pair { get => _pair; set => PairState.Set(this, ref _pair, value); }

    public static void RegisterSerializer() => NetSerializer<TestReferencePair>.Register(
        static (w, v) =>
        {
            NetSerializer<GameObject?>.Write(w, v.First);
            NetSerializer<GameObject?>.Write(w, v.Second);
        },
        static (ref NetReader r) => new TestReferencePair(NetSerializer<GameObject?>.Read(ref r), NetSerializer<GameObject?>.Read(ref r)));
}

public class InterestReadyListener : Component, INetworkListener
{
    [SerializeIgnore] public Action<NetPeer>? Ready;

    public void OnPeerReady(NetPeer peer) => Ready?.Invoke(peer);
}

public partial class NetworkingTests
{
    private sealed record InterestSession(SceneInstance World, FakePeer Alice, FakePeer Bob, LocalNetwork Network, NetworkConfiguration Previous)
        : IDisposable
    {
        public NetPeer AliceHere => HostPeerOf(Alice);
        public NetPeer BobHere => HostPeerOf(Bob);
        public PeerInterest AliceSees => Networking.Networking.Interest!.For(AliceHere);
        public PeerInterest BobSees => Networking.Networking.Interest!.For(BobHere);

        public void Dispose()
        {
            Networking.Networking.Shutdown();
            Alice.Dispose();
            Bob.Dispose();
            Networking.Networking.Configure(Previous);
        }
    }

    private InterestSession InterestHost(Action<NetworkConfiguration>? configure = null, Action<SceneInstance>? setUp = null)
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        NetworkConfiguration configuration = new()
        {
            InterestMode = InterestMode.Distance, InterestRadius = 10, InterestUpdateTicks = 1, InterestHysteresis = 0.5f,
            InterestLingerSeconds = 0
        };
        configure?.Invoke(configuration);
        Networking.Networking.Configure(configuration);
        try
        {
            SceneInstance world = CreateScene();
            world.Add(CreateGameObject("Level"));
            setUp?.Invoke(world);
            MakeCurrent(world);
            LocalNetwork network = new();
            Networking.Networking.Host(new LocalTransport(network, "host"));
            FakePeer alice = FakePeer.Join(network, "alice"), bob = FakePeer.Join(network, "bob");
            Pump(() => alice.IsReady && bob.IsReady, alice, bob);
            return new InterestSession(world, alice, bob, network, previous);
        }
        catch
        {
            Networking.Networking.Shutdown();
            Networking.Networking.Configure(previous);
            throw;
        }
    }

    private GameObject CrateAt(SceneInstance world, float x, NetworkVisibility visibility = NetworkVisibility.Default, string name = "Crate")
    {
        GameObject crate = Crate(world, visibility, name);
        crate.Transform.Position = new Float3(x, 0, 0);
        return crate;
    }

    private static Guid FirstGuid(byte[] data)
    {
        NetReader reader = new(data);
        return reader.ReadGuid();
    }

    private static int SpawnsOf(FakePeer peer, GameObject go) =>
        peer.Received.Count(m => m.Id == FakePeer.Spawn && ReadSpawn(m.Data, Guid.Empty).Id == go.Identifier);

    private static int DespawnsOf(FakePeer peer, GameObject go) =>
        peer.Received.Count(m => m.Id == FakePeer.Despawn && FirstGuid(m.Data) == go.Identifier);

    private static bool HoldsReference(FakePeer peer, SyncReferenceTestComponent pointer, Guid target) =>
        peer.SnapshotValues().Any(v => v.Component == pointer.Identifier && v.Slot == SyncReferenceTestComponent.TargetSlot &&
                                       v.Value.AsSpan().SequenceEqual(target.ToByteArray()));

    private static int CountReference(FakePeer peer, SyncReferenceTestComponent pointer, Guid target) =>
        peer.SnapshotValues().Count(v => v.Component == pointer.Identifier && v.Slot == SyncReferenceTestComponent.TargetSlot &&
                                         v.Value.AsSpan().SequenceEqual(target.ToByteArray()));

    private static Guid LastArgument(FakePeer peer, ushort id) => new(peer.Last(id).AsSpan(^16));

    [Fact]
    public void InterestSettingsDefaultToOffAndNormalizeInvalidValues()
    {
        NetworkConfiguration defaults = new();
        Assert.Equal(InterestMode.None, defaults.InterestMode);
        Assert.Equal(100f, defaults.InterestRadius);
        Assert.Equal(6, defaults.InterestUpdateTicks);
        Assert.Equal(0.15f, defaults.InterestHysteresis);
        Assert.Equal(1f, defaults.InterestLingerSeconds);

        NetworkConfiguration normalized = new NetworkConfiguration
        {
            InterestMode = (InterestMode)9, InterestRadius = float.NaN, InterestUpdateTicks = 0, InterestHysteresis = -1,
            InterestLingerSeconds = float.PositiveInfinity
        }.ValidatedCopy();
        Assert.Equal(InterestMode.None, normalized.InterestMode);
        Assert.Equal(100f, normalized.InterestRadius);
        Assert.Equal(1, normalized.InterestUpdateTicks);
        Assert.Equal(0f, normalized.InterestHysteresis);
        Assert.Equal(1f, normalized.InterestLingerSeconds);
        Assert.Null(Networking.Networking.Interest);
    }

    [Fact]
    public void CullDistanceIsStoredOnlyWhenSet()
    {
        GameObject go = CreateGameObject();
        go.Network.Mode = NetworkMode.Object;
        EchoObject plain = Serializer.Serialize(go);
        Assert.False(plain["Network"].TryGet("CullDistance", out _));

        go.Network.CullDistance = 25;
        EchoObject stored = Serializer.Serialize(go);
        Assert.Equal(25f, stored["Network"]["CullDistance"].FloatValue);
        GameObject copy = Serializer.Deserialize<GameObject>(stored)!;
        Assert.Equal(25f, copy.Network.CullDistance);
        copy.Dispose();
        GameObject reset = Serializer.Deserialize<GameObject>(plain)!;
        Assert.Equal(0f, reset.Network.CullDistance);
        reset.Dispose();

        Assert.Throws<ArgumentOutOfRangeException>(() => go.Network.CullDistance = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => go.Network.CullDistance = float.NaN);
    }

    [Fact]
    public void WithoutInterestEveryClientGetsEveryObjectWhereverItIs()
    {
        using VisibilitySession session = VisibilityHost();
        GameObject crate = Crate(session.World, NetworkVisibility.Default);
        crate.Transform.Position = new Float3(100000, 0, 0);
        crate.Network.Spawn();
        Networking.Networking.Interest!.For(session.AliceHere).SetViewpoints([Float3.Zero]);
        Networking.Networking.Interest.Refresh();
        Pump(() => session.Alice.Has(FakePeer.Spawn) && session.Bob.Has(FakePeer.Spawn), session.Alice, session.Bob);
        PumpFor(100, session.Alice, session.Bob);

        Assert.False(session.Alice.Has(FakePeer.Despawn));
        Assert.Empty(NetworkTestScene.Current.Restricted);
        Assert.True(crate.Network.IsVisibleTo(session.AliceHere));
        Assert.False(ReadSpawn(session.Alice.Last(FakePeer.Spawn), Guid.Empty).Replace);
        Assert.Equal(1, Networking.Networking.Interest.VisibleCount(session.BobHere));
    }

    [Fact]
    public void AnObjectEntersInRangeAndLeavesPastTheHysteresisAsOutOfInterest()
    {
        using InterestSession session = InterestHost();
        session.AliceSees.SetViewpoints([Float3.Zero]);
        GameObject crate = CrateAt(session.World, 5);
        crate.Network.Spawn();
        Assert.True(crate.Network.IsVisibleTo(session.AliceHere));
        Assert.False(crate.Network.IsVisibleTo(session.BobHere));
        Pump(() => session.Alice.Has(FakePeer.Spawn), session.Alice, session.Bob);

        crate.Transform.Position = new Float3(14, 0, 0);
        PumpFor(150, session.Alice, session.Bob);
        Assert.False(session.Alice.Has(FakePeer.Despawn));

        crate.Transform.Position = new Float3(16, 0, 0);
        Pump(() => session.Alice.Has(FakePeer.Despawn), session.Alice, session.Bob);
        NetReader reader = new(session.Alice.Last(FakePeer.Despawn));
        Assert.Equal(NetworkDespawnReason.OutOfInterest, DespawnMessage.Read(ref reader).Reason);
        Assert.False(crate.Network.IsVisibleTo(session.AliceHere));

        crate.Transform.Position = new Float3(12, 0, 0);
        PumpFor(150, session.Alice, session.Bob);
        Assert.Equal(1, Count(session.Alice, FakePeer.Spawn));

        crate.Transform.Position = new Float3(9, 0, 0);
        Pump(() => Count(session.Alice, FakePeer.Spawn) == 2, session.Alice, session.Bob);
        Assert.True(ReadSpawn(session.Alice.Last(FakePeer.Spawn), Guid.Empty).Replace);
        Assert.False(Mentions(session.Bob, crate.Identifier));
        Assert.Equal(new[] { (session.AliceHere.Id, false), (session.AliceHere.Id, true) },
            crate.GetComponent<VisibilityListener>()!.Changes);
    }

    [Fact]
    public void AnObjectThatJustCameIntoRangeLingersBeforeDistanceTakesItAway()
    {
        using InterestSession session = InterestHost(c =>
        {
            c.InterestHysteresis = 0;
            c.InterestLingerSeconds = 0.5f;
        });
        session.AliceSees.SetViewpoints([Float3.Zero]);
        GameObject crate = CrateAt(session.World, 50);
        crate.Network.Spawn();
        crate.Transform.Position = new Float3(5, 0, 0);
        Pump(() => session.Alice.Has(FakePeer.Spawn), session.Alice, session.Bob);
        Stopwatch entered = Stopwatch.StartNew();

        crate.Transform.Position = new Float3(50, 0, 0);
        Pump(() => session.Alice.Has(FakePeer.Despawn), session.Alice, session.Bob);
        Assert.InRange(entered.Elapsed.TotalSeconds, 0.4, 30);
    }

    [Fact]
    public void CullDistanceReplacesTheViewpointRadius()
    {
        using InterestSession session = InterestHost();
        session.AliceSees.SetViewpoints([Float3.Zero]);
        GameObject small = CrateAt(session.World, 5, name: "Small");
        small.Network.CullDistance = 3;
        GameObject big = CrateAt(session.World, 30, name: "Big");
        big.Network.CullDistance = 50;
        GameObject plain = CrateAt(session.World, 30, name: "Plain");
        small.Network.Spawn();
        big.Network.Spawn();
        plain.Network.Spawn();

        PumpFor(150, session.Alice, session.Bob);
        Assert.False(small.Network.IsVisibleTo(session.AliceHere));
        Assert.True(big.Network.IsVisibleTo(session.AliceHere));
        Assert.False(plain.Network.IsVisibleTo(session.AliceHere));
        Assert.False(Mentions(session.Alice, small.Identifier, plain.Identifier));
    }

    [Fact]
    public void ObserversTheClientOwnsAreItsViewpointsUnlessItHasExplicitOnes()
    {
        using InterestSession session = InterestHost();
        GameObject avatar = CreateGameObject("Avatar");
        NetworkObserver eye = avatar.AddComponent<NetworkObserver>();
        eye.Radius = 10;
        avatar.Transform.Position = new Float3(100, 0, 0);
        session.World.Add(avatar);
        avatar.Network.Spawn(session.AliceHere);
        GameObject near = CrateAt(session.World, 105, name: "Near"), far = CrateAt(session.World, 0, name: "Far");
        near.Network.Spawn();
        far.Network.Spawn();
        Assert.True(near.Network.IsVisibleTo(session.AliceHere));
        Assert.False(far.Network.IsVisibleTo(session.AliceHere));
        Assert.False(near.Network.IsVisibleTo(session.BobHere));

        bool Sees(GameObject seen, GameObject unseen) =>
            seen.Network.IsVisibleTo(session.AliceHere) && !unseen.Network.IsVisibleTo(session.AliceHere);

        avatar.Transform.Position = Float3.Zero;
        Pump(() => Sees(far, near), session.Alice, session.Bob);
        eye.Offset = new Float3(105, 0, 0);
        Pump(() => Sees(near, far), session.Alice, session.Bob);
        session.AliceSees.SetViewpoints([Float3.Zero]);
        Pump(() => Sees(far, near), session.Alice, session.Bob);
        session.AliceSees.ClearViewpoints();
        Pump(() => Sees(near, far), session.Alice, session.Bob);
        Assert.False(near.Network.IsVisibleTo(session.BobHere));
    }

    [Fact]
    public void ViewpointsSetAsAClientConnectsBringNearbyObjectsInItsSnapshot()
    {
        VisibilityPeerListener? listener = null;
        using InterestSession session = InterestHost(setUp: world =>
        {
            GameObject rules = CreateGameObject("Rules");
            listener = rules.AddComponent<VisibilityPeerListener>();
            world.Add(rules);
        });
        listener!.Connected = peer => Networking.Networking.Interest!.For(peer).SetViewpoints([Float3.Zero]);
        GameObject near = CrateAt(session.World, 3, name: "Near"), far = CrateAt(session.World, 50, name: "Far");
        near.Network.Spawn();
        far.Network.Spawn();

        using FakePeer carol = FakePeer.Join(session.Network, "carol");
        Pump(() => carol.IsReady, carol, session.Alice, session.Bob);
        PumpFor(100, carol, session.Alice, session.Bob);
        List<Guid> has = SnapshotObjectIds(carol.Last(FakePeer.SceneSnapshot));
        Assert.Contains(near.Identifier, has);
        Assert.DoesNotContain(far.Identifier, has);
        Assert.False(carol.Has(FakePeer.Spawn));
        Assert.False(Mentions(carol, far.Identifier));
        Assert.True(near.Network.IsVisibleTo(HostPeerOf(carol)));
    }

    [Fact]
    public void AClientWithoutViewpointsGetsOnlyWhatItOwnsWhatIsAlwaysSentAndWhatItIsShown()
    {
        using InterestSession session = InterestHost();
        GameObject owned = CrateAt(session.World, 500, name: "Owned");
        owned.Network.Spawn(session.AliceHere);
        GameObject always = CrateAt(session.World, 500, NetworkVisibility.Always, "Always");
        always.Network.Spawn();
        GameObject plain = CrateAt(session.World, 500, name: "Plain");
        plain.Network.Spawn();
        GameObject shown = CrateAt(session.World, 500, name: "Shown");
        shown.Network.Spawn();
        shown.Network.ShowTo(session.BobHere);
        PumpFor(150, session.Alice, session.Bob);

        NetworkInterest interest = Networking.Networking.Interest!;
        List<GameObject> aliceHas = new(), bobHas = new();
        interest.GetVisible(session.AliceHere, aliceHas);
        interest.GetVisible(session.BobHere, bobHas);
        Assert.True(aliceHas.ToHashSet().SetEquals([owned, always]));
        Assert.True(bobHas.ToHashSet().SetEquals([always, shown]));
        Assert.Equal(2, interest.VisibleCount(session.AliceHere));
        Assert.False(Mentions(session.Alice, plain.Identifier, shown.Identifier));
        Assert.False(Mentions(session.Bob, plain.Identifier, owned.Identifier));
    }

    [Fact]
    public void AClientThatSeesEverythingStillMissesManualObjects()
    {
        using InterestSession session = InterestHost();
        GameObject plain = CrateAt(session.World, 500, name: "Plain");
        plain.Network.Spawn();
        GameObject secret = CrateAt(session.World, 500, NetworkVisibility.Manual, "Secret");
        secret.Network.Spawn();

        session.BobSees.SeesEverything = true;
        Assert.True(plain.Network.IsVisibleTo(session.BobHere));
        Assert.False(secret.Network.IsVisibleTo(session.BobHere));
        Pump(() => SpawnsOf(session.Bob, plain) == 1, session.Alice, session.Bob);
        PumpFor(100, session.Alice, session.Bob);
        Assert.True(plain.Network.IsVisibleTo(session.BobHere));

        session.BobSees.SeesEverything = false;
        Assert.False(plain.Network.IsVisibleTo(session.BobHere));
        Pump(() => DespawnsOf(session.Bob, plain) == 1, session.Alice, session.Bob);
        Assert.False(Mentions(session.Bob, secret.Identifier));
        Assert.False(Mentions(session.Alice, plain.Identifier));
    }

    [Fact]
    public void SyncReferencesToObjectsAClientLacksReachItEmptyAndAreResentWhenTheyArrive()
    {
        using InterestSession session = InterestHost();
        session.AliceSees.SetViewpoints([Float3.Zero]);
        session.BobSees.SetViewpoints([new Float3(1000, 0, 0)]);
        GameObject crate = CrateAt(session.World, 3);
        crate.Network.Spawn();
        GameObject holder = CrateAt(session.World, 0, NetworkVisibility.Always, "Holder");
        SyncReferenceTestComponent pointer = holder.AddComponent<SyncReferenceTestComponent>();
        holder.Network.Spawn();

        pointer.Target = crate;
        Pump(() => HoldsReference(session.Alice, pointer, crate.Identifier) && HoldsReference(session.Bob, pointer, Guid.Empty),
            session.Alice, session.Bob);
        Assert.False(Mentions(session.Bob, crate.Identifier));

        session.BobSees.SetViewpoints([Float3.Zero]);
        Pump(() => SpawnsOf(session.Bob, crate) == 1 && HoldsReference(session.Bob, pointer, crate.Identifier),
            session.Alice, session.Bob);

        session.BobSees.SetViewpoints([new Float3(1000, 0, 0)]);
        Pump(() => DespawnsOf(session.Bob, crate) == 1, session.Alice, session.Bob);
        int before = CountReference(session.Bob, pointer, crate.Identifier);
        session.BobSees.SetViewpoints([Float3.Zero]);
        Pump(() => SpawnsOf(session.Bob, crate) == 2 && CountReference(session.Bob, pointer, crate.Identifier) > before,
            session.Alice, session.Bob);
    }

    [Fact]
    public void AClientsSyncReferenceIsPassedOnEmptyToClientsLackingItsTarget()
    {
        using InterestSession session = InterestHost();
        session.AliceSees.SetViewpoints([Float3.Zero]);
        GameObject crate = CrateAt(session.World, 3);
        crate.Network.Spawn();
        GameObject holder = CrateAt(session.World, 0, NetworkVisibility.Always, "Holder");
        SyncReferenceTestComponent pointer = holder.AddComponent<SyncReferenceTestComponent>();
        holder.Network.Spawn(session.AliceHere);
        Pump(() => SpawnsOf(session.Bob, holder) == 1, session.Alice, session.Bob);

        session.Alice.SendSnapshotValues(null, 0, 1,
            (pointer.Identifier, SyncReferenceTestComponent.TargetSlot, NetworkTestScene.Current.GenerationOf(holder),
                crate.Identifier.ToByteArray()));
        Pump(() => ReferenceEquals(pointer.Target, crate) && HoldsReference(session.Bob, pointer, Guid.Empty), session.Alice, session.Bob);
        Assert.False(Mentions(session.Bob, crate.Identifier));
    }

    [Fact]
    public void RpcReferencesToObjectsAClientLacksReachItEmpty()
    {
        using InterestSession session = InterestHost();
        session.AliceSees.SetViewpoints([Float3.Zero]);
        GameObject crate = CrateAt(session.World, 3);
        crate.Network.Spawn();
        GameObject holder = CrateAt(session.World, 0, NetworkVisibility.Always, "Holder");
        BeaconTestComponent beacon = holder.AddComponent<BeaconTestComponent>();
        holder.Network.Spawn();
        Pump(() => SpawnsOf(session.Bob, holder) == 1 && SpawnsOf(session.Alice, crate) == 1, session.Alice, session.Bob);

        beacon.Mark(crate);
        Pump(() => session.Alice.Has(FakePeer.RpcFromHost) && session.Bob.Has(FakePeer.RpcFromHost), session.Alice, session.Bob);
        Assert.Equal(crate.Identifier, LastArgument(session.Alice, FakePeer.RpcFromHost));
        Assert.Equal(Guid.Empty, LastArgument(session.Bob, FakePeer.RpcFromHost));
        Assert.False(Mentions(session.Bob, crate.Identifier));
        Assert.Same(crate, Assert.Single(beacon.Marks));
    }

    [Fact]
    public void RelayedRpcReferencesReachClientsLackingTheTargetEmpty()
    {
        using InterestSession session = InterestHost();
        session.AliceSees.SetViewpoints([Float3.Zero]);
        GameObject crate = CrateAt(session.World, 3);
        crate.Network.Spawn();
        GameObject holder = CrateAt(session.World, 0, NetworkVisibility.Always, "Holder");
        BeaconTestComponent beacon = holder.AddComponent<BeaconTestComponent>();
        holder.Network.Spawn();
        Pump(() => SpawnsOf(session.Bob, holder) == 1 && SpawnsOf(session.Alice, crate) == 1, session.Alice, session.Bob);

        void MarkFrom(FakePeer peer, Guid target) => peer.Send(FakePeer.RpcToHost, w =>
        {
            w.WriteGuid(beacon.Identifier);
            w.WriteUInt32(BeaconTestComponent.MarkId);
            w.WriteGuid(target);
        });

        MarkFrom(session.Alice, crate.Identifier);
        Pump(() => session.Bob.Has(FakePeer.RpcFromHost) && beacon.Marks.Count == 1, session.Alice, session.Bob);
        Assert.Equal(Guid.Empty, LastArgument(session.Bob, FakePeer.RpcFromHost));
        Assert.Same(crate, beacon.Marks[0]);
        Assert.False(Mentions(session.Bob, crate.Identifier));

        MarkFrom(session.Bob, crate.Identifier);
        Pump(() => session.Alice.Has(FakePeer.RpcFromHost) && beacon.Marks.Count == 2, session.Alice, session.Bob);
        Assert.Equal(Guid.Empty, LastArgument(session.Alice, FakePeer.RpcFromHost));
        Assert.Null(beacon.Marks[1]);
    }

    [Fact]
    public void SceneLinksToObjectsOutOfRangeCarryTheirIdButLinksToHiddenOnesDoNot()
    {
        GameObject? chest = null;
        NetTestComponent? vaultStats = null;
        using InterestSession session = InterestHost(setUp: world =>
        {
            chest = CreateGameObject("Chest");
            chest.Network.Mode = NetworkMode.Object;
            chest.AddComponent<SecretHolder>().Value = HostSecret;
            chest.Transform.Position = new Float3(50, 0, 0);
            GameObject vault = CreateGameObject("Vault");
            vault.Network.Mode = NetworkMode.Object;
            vault.Network.Visibility = NetworkVisibility.Manual;
            vaultStats = vault.AddComponent<NetTestComponent>();
            GameObject door = CreateGameObject("Door");
            LinkHolder link = door.AddComponent<LinkHolder>();
            link.Target = chest;
            link.Part = vaultStats;
            world.Add(door);
            world.Add(chest);
            world.Add(vault);
        });

        byte[] payload = session.Alice.Last(FakePeer.SceneSnapshot);
        string json = SnapshotJson(payload);
        Assert.Contains(chest!.Identifier.ToString(), json);
        Assert.DoesNotContain(HostSecret, json);
        Assert.DoesNotContain(vaultStats!.Identifier.ToString(), json);
        Assert.Empty(SnapshotObjectIds(payload));
    }

    [Fact]
    public void ASceneLinkToAnObjectOutOfRangeIsFilledInWhenItArrives()
    {
        SceneInstance scene = CreateScene();
        GameObject chest = CreateGameObject("Chest");
        chest.Network.Mode = NetworkMode.Object;
        NetTestComponent stats = chest.AddComponent<NetTestComponent>();
        GameObject door = CreateGameObject("Door");
        LinkHolder link = door.AddComponent<LinkHolder>();
        link.Target = chest;
        link.Part = stats;
        scene.Add(door);
        scene.Add(chest);
        using NetWriter snapshot = new(), spawn = new();
        NetworkSerialization.HiddenRoots = new HashSet<GameObject>(ReferenceEqualityComparer.Instance) { chest };
        try { NetworkTestScene.Current.WriteSnapshot(scene, snapshot); }
        finally { NetworkSerialization.HiddenRoots = null; }
        Assert.Contains(chest.Identifier.ToString(), SnapshotJson(snapshot.ToArray()));
        NetworkTestScene.Current.WriteSpawn(chest, -1, NetworkOrphaned.Destroy, spawn, generation: 1, netId: 5);

        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer => host.Session.SendRaw(peer, FakePeer.SceneSnapshot, snapshot.WrittenSpan);
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady, host);
            LinkHolder received = Find(SceneInstance.Main, "Door").GetComponent<LinkHolder>()!;
            Assert.Null(received.Target);
            Assert.Null(received.Part);

            NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);
            host.Session.SendRaw(client, FakePeer.Spawn, spawn.WrittenSpan);
            host.Session.Flush();
            Pump(() => received.Target.IsValid(), host);
            GameObject arrived = Find(SceneInstance.Main, chest.Identifier);
            Assert.Same(arrived, received.Target);
            Assert.Same(arrived.GetComponent<NetTestComponent>(), received.Part);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ASpawnedObjectsLinkFollowsItsTargetInAndOutOfRange()
    {
        SceneInstance scene = CreateScene();
        using NetWriter snapshot = new(), door = new(), chest = new(), despawn = new(), back = new();
        NetworkTestScene.Current.WriteSnapshot(scene, snapshot);
        GameObject box = CreateGameObject("Chest");
        box.Network.Mode = NetworkMode.Object;
        NetTestComponent stats = box.AddComponent<NetTestComponent>();
        GameObject gate = CreateGameObject("Door");
        gate.Network.Mode = NetworkMode.Object;
        LinkHolder link = gate.AddComponent<LinkHolder>();
        link.Target = box;
        link.Part = stats;
        NetworkTestScene.Current.WriteSpawn(gate, -1, NetworkOrphaned.Destroy, door, generation: 1, netId: 5);
        NetworkTestScene.Current.WriteSpawn(box, -1, NetworkOrphaned.Destroy, chest, generation: 2, netId: 6);
        new DespawnMessage(box.Identifier, NetworkDespawnReason.OutOfInterest).Write(despawn);
        NetworkTestScene.Current.WriteSpawn(box, -1, NetworkOrphaned.Destroy, back, generation: 2, netId: 6, replace: true);

        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer => host.Session.SendRaw(peer, FakePeer.SceneSnapshot, snapshot.WrittenSpan);
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady, host);
            NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);
            host.Session.SendRaw(client, FakePeer.Spawn, door.WrittenSpan);
            host.Session.Flush();
            Pump(() => SceneInstance.Main.Objects.Any(o => o.Identifier == gate.Identifier), host);
            LinkHolder received = Find(SceneInstance.Main, gate.Identifier).GetComponent<LinkHolder>()!;
            Assert.Null(received.Target);

            host.Session.SendRaw(client, FakePeer.Spawn, chest.WrittenSpan);
            host.Session.Flush();
            Pump(() => received.Target.IsValid(), host);
            GameObject first = Find(SceneInstance.Main, box.Identifier);
            Assert.Same(first, received.Target);
            Assert.Same(first.GetComponent<NetTestComponent>(), received.Part);

            host.Session.SendRaw(client, FakePeer.Despawn, despawn.WrittenSpan);
            host.Session.SendRaw(client, FakePeer.Spawn, back.WrittenSpan);
            host.Session.Flush();
            Pump(() => first.IsDisposed && received.Target.IsValid() && !ReferenceEquals(received.Target, first), host);
            GameObject second = Find(SceneInstance.Main, box.Identifier);
            Assert.Same(second, received.Target);
            Assert.Same(second.GetComponent<NetTestComponent>(), received.Part);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AnObjectGoingInAndOutOfRangeEveryTickArrivesWholeEachTime()
    {
        using InterestSession session = InterestHost(c => c.InterestHysteresis = 0);
        session.AliceSees.SetViewpoints([Float3.Zero]);
        GameObject crate = CrateAt(session.World, 5);
        NetTestComponent stats = crate.GetComponent<NetTestComponent>()!;
        crate.Network.Spawn();
        Pump(() => SpawnsOf(session.Alice, crate) == 1, session.Alice, session.Bob);

        for (int i = 0; i < 16; i++)
        {
            crate.Transform.Position = new Float3(i % 2 == 0 ? 50 : 5, 0, 0);
            stats.Health = 200 + i;
            PumpFor(40, session.Alice, session.Bob);
        }

        crate.Transform.Position = new Float3(5, 0, 0);
        stats.Health = 999;
        Pump(() => crate.Network.IsVisibleTo(session.AliceHere) &&
                   (session.Alice.SnapshotInt(stats.Identifier, NetTestComponent.HealthSlot) == 999 ||
                    ReadSpawn(session.Alice.Last(FakePeer.Spawn), stats.Identifier).Health == 999), session.Alice, session.Bob);

        int spawns = SpawnsOf(session.Alice, crate), despawns = DespawnsOf(session.Alice, crate);
        Assert.True(despawns >= 3, $"Only {despawns} exits happened.");
        Assert.Equal(despawns + 1, spawns);
        Assert.All(session.Alice.Received.Where(m => m.Id == FakePeer.Spawn).Skip(1),
            m => Assert.True(ReadSpawn(m.Data, Guid.Empty).Replace));
        Assert.True(Networking.Networking.IsActive);
        Assert.False(Mentions(session.Bob, crate.Identifier));
    }

    private static string SectionAddJson(byte[] payload)
    {
        NetReader reader = new(payload);
        return NetworkTestScene.Current.Decompress(SectionAddMessage.Read(ref reader).Sections).WriteToJson();
    }

    private static string SpawnJson(FakePeer peer, GameObject go)
    {
        byte[] payload = peer.Received.Last(m => m.Id == FakePeer.Spawn && ReadSpawn(m.Data, Guid.Empty).Id == go.Identifier).Data;
        NetReader reader = new(payload);
        return NetworkTestScene.Current.Decompress(SpawnMessage.Read(ref reader).Snapshot).WriteToJson();
    }

    private static int CountPair(FakePeer peer, SyncPairTestComponent pair, GameObject first, GameObject second)
    {
        byte[] both = [.. first.Identifier.ToByteArray(), .. second.Identifier.ToByteArray()];
        return peer.SnapshotValues().Count(v => v.Component == pair.Identifier && v.Slot == SyncPairTestComponent.PairSlot &&
                                                v.Value.AsSpan().SequenceEqual(both));
    }

    private static int NoViewpointWarnings(NetPeer peer)
    {
        int count = 0;
        for (int i = 0; i < NetworkDiagnostics.Count; i++)
        {
            NetworkDiagnostics.Entry entry = NetworkDiagnostics.GetEntry(i);
            if (entry.Kind == "Interest" && entry.Detail == "No viewpoint" && entry.Peer == peer.ToString()) count++;
        }

        return count;
    }

    [Fact]
    public void AnAddedSectionDoesNotLinkToAHiddenObjectElsewhereInTheWorld()
    {
        using VisibilitySession session = VisibilityHost();
        GameObject vault = Crate(session.World, NetworkVisibility.Manual, "Vault");
        vault.Network.Spawn();
        GameObject door = CreateGameObject("Door");
        door.AddComponent<LinkHolder>().Target = vault;
        session.World.AddContent(new SceneContent { Objects = [door] }, "Arena", null);
        Pump(() => session.Alice.Has(FakePeer.SectionAdd) && session.Bob.Has(FakePeer.SectionAdd), session.Alice, session.Bob);

        Assert.DoesNotContain(vault.Identifier.ToString(), SectionAddJson(session.Alice.Last(FakePeer.SectionAdd)));
        Assert.DoesNotContain(vault.Identifier.ToString(), SectionAddJson(session.Bob.Last(FakePeer.SectionAdd)));
        Assert.False(Mentions(session.Alice, vault.Identifier));
    }

    [Fact]
    public void AnAddedSectionsSyncReferenceToAHiddenObjectElsewhereReachesClientsEmpty()
    {
        using VisibilitySession session = VisibilityHost();
        GameObject vault = Crate(session.World, NetworkVisibility.Manual, "Vault");
        vault.Network.Spawn();
        GameObject holder = CreateGameObject("Holder");
        holder.Network.Mode = NetworkMode.Object;
        holder.AddComponent<SyncReferenceTestComponent>().Target = vault;
        session.World.AddContent(new SceneContent { Objects = [holder] }, "Arena", null);
        Pump(() => session.Alice.Has(FakePeer.SectionAdd) && session.Bob.Has(FakePeer.SectionAdd), session.Alice, session.Bob);
        PumpFor(50, session.Alice, session.Bob);

        Assert.False(Mentions(session.Alice, vault.Identifier));
        Assert.False(Mentions(session.Bob, vault.Identifier));
    }

    [Fact]
    public void TurningInterestOffSendsWhatDistanceHeldBack()
    {
        using InterestSession session = InterestHost();
        session.AliceSees.SetViewpoints([Float3.Zero]);
        GameObject far = CrateAt(session.World, 500);
        far.Network.Spawn();
        PumpFor(100, session.Alice, session.Bob);
        Assert.False(far.Network.IsVisibleTo(session.AliceHere));

        NetworkConfiguration off = Networking.Networking.Configuration;
        off.InterestMode = InterestMode.None;
        Networking.Networking.Configure(off);
        Pump(() => SpawnsOf(session.Alice, far) == 1 && SpawnsOf(session.Bob, far) == 1, session.Alice, session.Bob);
        Assert.Empty(NetworkTestScene.Current.Restricted);
    }

    [Fact]
    public void ALinkIsForgottenWithItsHolderAndNoneIsKeptInsideOneObject()
    {
        SceneInstance scene = CreateScene();
        using NetWriter snapshot = new(), player = new(), turret = new();
        NetworkTestScene.Current.WriteSnapshot(scene, snapshot);
        GameObject hero = CreateGameObject("Player");
        hero.Network.Mode = NetworkMode.Object;
        NetworkTestScene.Current.WriteSpawn(hero, -1, NetworkOrphaned.Destroy, player, generation: 1, netId: 5);
        GameObject gun = CreateGameObject("Turret");
        gun.Network.Mode = NetworkMode.Object;
        GameObject barrel = CreateGameObject("Barrel");
        barrel.SetParent(gun, false);
        gun.AddComponent<LinkHolder>().Target = barrel;
        NetworkTestScene.Current.WriteSpawn(gun, -1, NetworkOrphaned.Destroy, turret, generation: 2, netId: 6);
        List<(byte[] Spawn, byte[] Despawn)> bullets = new();
        for (int i = 0; i < 20; i++)
        {
            GameObject bullet = CreateGameObject($"Bullet{i}");
            bullet.Network.Mode = NetworkMode.Object;
            bullet.AddComponent<LinkHolder>().Target = hero;
            using NetWriter spawn = new(), despawn = new();
            NetworkTestScene.Current.WriteSpawn(bullet, -1, NetworkOrphaned.Destroy, spawn, generation: (uint)(10 + i), netId: (uint)(10 + i));
            new DespawnMessage(bullet.Identifier).Write(despawn);
            bullets.Add((spawn.ToArray(), despawn.ToArray()));
        }

        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer => host.Session.SendRaw(peer, FakePeer.SceneSnapshot, snapshot.WrittenSpan);
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady, host);
            NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);
            host.Session.SendRaw(client, FakePeer.Spawn, player.WrittenSpan);
            host.Session.SendRaw(client, FakePeer.Spawn, turret.WrittenSpan);
            host.Session.Flush();
            Pump(() => SceneInstance.Main.Objects.Any(o => o.Identifier == gun.Identifier), host);
            GameObject receivedGun = Find(SceneInstance.Main, gun.Identifier);
            Assert.Same(receivedGun.Children.Single(), receivedGun.GetComponent<LinkHolder>()!.Target);
            Assert.Equal(0, NetworkTestScene.Current.LinkCount);

            host.Session.SendRaw(client, FakePeer.Spawn, bullets[0].Spawn);
            host.Session.Flush();
            Pump(() => NetworkTestScene.Current.Entries.Count == 3, host);
            Assert.Equal(1, NetworkTestScene.Current.LinkCount);

            foreach ((byte[] spawn, byte[] despawn) in bullets.Skip(1))
            {
                host.Session.SendRaw(client, FakePeer.Spawn, spawn);
                host.Session.SendRaw(client, FakePeer.Despawn, despawn);
            }

            host.Session.Flush();
            Pump(() => NetworkTestScene.Current.Entries.Count == 3 && SceneInstance.Main.Objects.Count(o => o.Name.StartsWith("Bullet")) == 1, host);
            Assert.Equal(1, NetworkTestScene.Current.LinkCount);

            host.Session.SendRaw(client, FakePeer.Despawn, bullets[0].Despawn);
            host.Session.Flush();
            Pump(() => NetworkTestScene.Current.Entries.Count == 2, host);
            Assert.Equal(0, NetworkTestScene.Current.LinkCount);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AClientGetsTheLinkToASecretObjectItHasWhileOthersGetItEmpty()
    {
        using VisibilitySession session = VisibilityHost();
        GameObject key = Crate(session.World, NetworkVisibility.OwnerOnly, "Key");
        key.Network.Spawn(session.AliceHere);
        GameObject chest = Crate(session.World, NetworkVisibility.Default, "Chest");
        chest.AddComponent<LinkHolder>().Target = key;
        chest.Network.Spawn();
        Pump(() => SpawnsOf(session.Alice, chest) == 1 && SpawnsOf(session.Bob, chest) == 1, session.Alice, session.Bob);

        Assert.Contains(key.Identifier.ToString(), SpawnJson(session.Alice, chest));
        Assert.DoesNotContain(key.Identifier.ToString(), SpawnJson(session.Bob, chest));
        Assert.False(Mentions(session.Bob, key.Identifier));
    }

    [Fact]
    public void AnObjectLingersFromWhenItWasLastInRange()
    {
        using InterestSession session = InterestHost(c =>
        {
            c.InterestHysteresis = 0;
            c.InterestLingerSeconds = 0.5f;
        });
        session.AliceSees.SetViewpoints([Float3.Zero]);
        GameObject crate = CrateAt(session.World, 5);
        crate.Network.Spawn();
        Pump(() => session.Alice.Has(FakePeer.Spawn), session.Alice, session.Bob);
        PumpFor(800, session.Alice, session.Bob);

        crate.Transform.Position = new Float3(50, 0, 0);
        Stopwatch left = Stopwatch.StartNew();
        Pump(() => session.Alice.Has(FakePeer.Despawn), session.Alice, session.Bob);
        Assert.InRange(left.Elapsed.TotalSeconds, 0.4, 30);
    }

    [Fact]
    public void OnlyValuesNamingTheObjectThatArrivedGoAgain()
    {
        using InterestSession session = InterestHost();
        session.AliceSees.SetViewpoints([new Float3(1000, 0, 0)]);
        GameObject near = CrateAt(session.World, 0, name: "Near"), far = CrateAt(session.World, 500, name: "Far");
        near.Network.Spawn();
        far.Network.Spawn();
        GameObject nearHolder = CrateAt(session.World, 0, NetworkVisibility.Always, "NearHolder");
        SyncReferenceTestComponent toNear = nearHolder.AddComponent<SyncReferenceTestComponent>();
        nearHolder.Network.Spawn();
        GameObject farHolder = CrateAt(session.World, 0, NetworkVisibility.Always, "FarHolder");
        SyncReferenceTestComponent toFar = farHolder.AddComponent<SyncReferenceTestComponent>();
        farHolder.Network.Spawn();

        toNear.Target = near;
        toFar.Target = far;
        Pump(() => HoldsReference(session.Alice, toNear, Guid.Empty) && HoldsReference(session.Alice, toFar, Guid.Empty),
            session.Alice, session.Bob);
        PumpFor(100, session.Alice, session.Bob);
        int farBefore = CountReference(session.Alice, toFar, Guid.Empty);

        session.AliceSees.SetViewpoints([Float3.Zero]);
        Pump(() => HoldsReference(session.Alice, toNear, near.Identifier), session.Alice, session.Bob);
        PumpFor(100, session.Alice, session.Bob);
        Assert.Equal(farBefore, CountReference(session.Alice, toFar, Guid.Empty));
    }

    [Fact]
    public void AValueNamingSeveralObjectsGoesAgainWhenAnyOfThemComesBack()
    {
        SyncPairTestComponent.RegisterSerializer();
        using InterestSession session = InterestHost();
        session.AliceSees.SetViewpoints([Float3.Zero]);
        GameObject first = CrateAt(session.World, 3, name: "First"), second = CrateAt(session.World, -3, name: "Second");
        first.Network.Spawn();
        second.Network.Spawn();
        GameObject holder = CrateAt(session.World, 0, NetworkVisibility.Always, "Holder");
        SyncPairTestComponent pair = holder.AddComponent<SyncPairTestComponent>();
        holder.Network.Spawn();

        pair.Pair = new TestReferencePair(first, second);
        Pump(() => CountPair(session.Alice, pair, first, second) > 0, session.Alice, session.Bob);
        first.Transform.Position = new Float3(500, 0, 0);
        Pump(() => DespawnsOf(session.Alice, first) == 1, session.Alice, session.Bob);
        int before = CountPair(session.Alice, pair, first, second);

        first.Transform.Position = new Float3(3, 0, 0);
        Pump(() => SpawnsOf(session.Alice, first) == 2 && CountPair(session.Alice, pair, first, second) > before,
            session.Alice, session.Bob);
    }

    [Fact]
    public void EachReadyClientWithoutAViewpointIsWarnedOnceAndALoadingOneIsNot()
    {
        NetworkDiagnostics.Clear();
        NetworkDiagnostics.Enabled = true;
        try
        {
            InterestReadyListener? listener = null;
            using InterestSession session = InterestHost(setUp: world =>
            {
                GameObject rules = CreateGameObject("Rules");
                listener = rules.AddComponent<InterestReadyListener>();
                world.Add(rules);
            });
            listener!.Ready = peer =>
            {
                GameObject avatar = CreateGameObject("Avatar");
                avatar.AddComponent<NetworkObserver>();
                session.World.Add(avatar);
                avatar.Network.Spawn(peer);
            };

            using FakePeer carol = FakePeer.Join(session.Network, "carol");
            Pump(() => carol.IsReady, carol, session.Alice, session.Bob);
            PumpFor(150, carol, session.Alice, session.Bob);

            Assert.Equal(1, NoViewpointWarnings(session.AliceHere));
            Assert.Equal(1, NoViewpointWarnings(session.BobHere));
            Assert.Equal(0, NoViewpointWarnings(HostPeerOf(carol)));
        }
        finally
        {
            NetworkDiagnostics.Enabled = false;
            NetworkDiagnostics.Clear();
        }
    }

    [Fact]
    public void ViewpointsSetForAClientAreClearedWhenTheHostChangesScene()
    {
        using InterestSession session = InterestHost();
        session.AliceSees.SetViewpoints([Float3.Zero]);
        session.World.ReplaceContent(new SceneContent { Objects = [new GameObject("Next")] }, "Next", null);
        Pump(() => Count(session.Alice, FakePeer.SceneSnapshot) == 2 && Count(session.Bob, FakePeer.SceneSnapshot) == 2,
            session.Alice, session.Bob);

        Assert.False(session.AliceSees.HasExplicit);
        Assert.Empty(session.AliceSees.Explicit);
    }

    [Fact]
    public void AnObserverWithABadRadiusOrOffsetUsesTheDefaults()
    {
        using InterestSession session = InterestHost();
        GameObject avatar = CreateGameObject("Avatar");
        NetworkObserver eye = avatar.AddComponent<NetworkObserver>();
        eye.Radius = -5;
        eye.Offset = new Float3(float.NaN, 0, 0);
        avatar.Transform.Position = new Float3(100, 0, 0);
        session.World.Add(avatar);
        avatar.Network.Spawn(session.AliceHere);
        GameObject near = CrateAt(session.World, 105, name: "Near");
        near.Network.Spawn();
        Assert.True(near.Network.IsVisibleTo(session.AliceHere));

        eye.Radius = float.NaN;
        eye.Offset = Float3.Zero;
        near.Network.RefreshVisibility();
        Assert.True(near.Network.IsVisibleTo(session.AliceHere));
    }

    [Fact]
    public void AnInterestRadiusOfZeroIsRaisedToTheSmallestRange()
    {
        float radius = new NetworkConfiguration { InterestRadius = 0 }.ValidatedCopy().InterestRadius;
        Assert.True(radius > 0);
    }
}
