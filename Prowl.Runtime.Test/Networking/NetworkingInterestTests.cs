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

    private static readonly RpcMember<BeaconTestComponent> __rpc_601 = new(MarkId, "Mark(GameObject)", RpcMode.Broadcast, RpcFlags.AnyCaller,
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
        SyncFlags.FromOwner, static c => c._pair, static (c, v) => c._pair = v);
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
    private sealed record InterestSession(Scene World, FakePeer Alice, FakePeer Bob, LocalNetwork Network, NetworkConfiguration Previous)
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

    private InterestSession InterestHost(Action<NetworkConfiguration>? configure = null, Action<Scene>? setUp = null)
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
            Scene world = CreateScene();
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

    private GameObject CrateAt(Scene world, float x, NetworkVisibility visibility = NetworkVisibility.Default, string name = "Crate")
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
        go.Network.Networked = true;
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
        crate.SpawnBuilt();
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
        crate.SpawnBuilt();
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
        crate.SpawnBuilt();
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
        small.SpawnBuilt();
        big.SpawnBuilt();
        plain.SpawnBuilt();

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
        avatar.SpawnBuilt(session.AliceHere);
        GameObject near = CrateAt(session.World, 105, name: "Near"), far = CrateAt(session.World, 0, name: "Far");
        near.SpawnBuilt();
        far.SpawnBuilt();
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
    public void ViewpointsSetAsAClientConnectsBringNearbyObjectsInItsJoinSpawns()
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
        near.SpawnBuilt();
        far.SpawnBuilt();

        using FakePeer carol = FakePeer.Join(session.Network, "carol");
        Pump(() => carol.IsReady, carol, session.Alice, session.Bob);
        PumpFor(100, carol, session.Alice, session.Bob);
        List<Guid> has = SpawnedIds(carol);
        Assert.Contains(near.Identifier, has);
        Assert.DoesNotContain(far.Identifier, has);
        Assert.False(Mentions(carol, far.Identifier));
        Assert.True(near.Network.IsVisibleTo(HostPeerOf(carol)));
    }

    [Fact]
    public void AClientWithoutViewpointsGetsOnlyWhatItOwnsWhatIsAlwaysSentAndWhatItIsShown()
    {
        using InterestSession session = InterestHost();
        GameObject owned = CrateAt(session.World, 500, name: "Owned");
        owned.SpawnBuilt(session.AliceHere);
        GameObject always = CrateAt(session.World, 500, NetworkVisibility.Always, "Always");
        always.SpawnBuilt();
        GameObject plain = CrateAt(session.World, 500, name: "Plain");
        plain.SpawnBuilt();
        GameObject shown = CrateAt(session.World, 500, name: "Shown");
        shown.SpawnBuilt();
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
        plain.SpawnBuilt();
        GameObject secret = CrateAt(session.World, 500, NetworkVisibility.Manual, "Secret");
        secret.SpawnBuilt();

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
    public void RpcReferencesGoByNetIdSoAClientLackingTheObjectReadsNull()
    {
        using InterestSession session = InterestHost();
        session.AliceSees.SetViewpoints([Float3.Zero]);
        GameObject crate = CrateAt(session.World, 3);
        crate.SpawnBuilt();
        GameObject holder = CrateAt(session.World, 0, NetworkVisibility.Always, "Holder");
        BeaconTestComponent beacon = holder.AddComponent<BeaconTestComponent>();
        holder.SpawnBuilt();
        Pump(() => SpawnsOf(session.Bob, holder) == 1 && SpawnsOf(session.Alice, crate) == 1, session.Alice, session.Bob);

        beacon.Mark(crate);
        Pump(() => session.Alice.Has(FakePeer.RpcFromHost) && session.Bob.Has(FakePeer.RpcFromHost), session.Alice, session.Bob);
        // The same bytes go to both: the object's NetId and that it is the object itself. Bob has no such object,
        // so there it reads as null.
        using NetWriter reference = new();
        NetSerializer<GameObject>.Write(reference, crate);
        Assert.True(session.Alice.Last(FakePeer.RpcFromHost).AsSpan().EndsWith(reference.WrittenSpan));
        Assert.True(session.Bob.Last(FakePeer.RpcFromHost).AsSpan().EndsWith(reference.WrittenSpan));
        Assert.False(Mentions(session.Bob, crate.Identifier));
        Assert.Same(crate, Assert.Single(beacon.Marks));
    }

    [Fact]
    public void AnObjectGoingInAndOutOfRangeEveryTickArrivesWholeEachTime()
    {
        using InterestSession session = InterestHost(c => c.InterestHysteresis = 0);
        session.AliceSees.SetViewpoints([Float3.Zero]);
        GameObject crate = CrateAt(session.World, 5);
        NetTestComponent stats = crate.GetComponent<NetTestComponent>()!;
        crate.SpawnBuilt();
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
                   (session.Alice.SyncInt(stats.Identifier, NetTestComponent.HealthSlot) == 999 ||
                    ReadSpawn(session.Alice.Last(FakePeer.Spawn), stats.Identifier).Health == 999), session.Alice, session.Bob);

        int spawns = SpawnsOf(session.Alice, crate), despawns = DespawnsOf(session.Alice, crate);
        Assert.True(despawns >= 3, $"Only {despawns} exits happened.");
        Assert.Equal(despawns + 1, spawns);
        Assert.All(session.Alice.Received.Where(m => m.Id == FakePeer.Spawn).Skip(1),
            m => Assert.True(ReadSpawn(m.Data, Guid.Empty).Replace));
        Assert.True(Networking.Networking.IsActive);
        Assert.False(Mentions(session.Bob, crate.Identifier));
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
    public void TurningInterestOffSendsWhatDistanceHeldBack()
    {
        using InterestSession session = InterestHost();
        session.AliceSees.SetViewpoints([Float3.Zero]);
        GameObject far = CrateAt(session.World, 500);
        far.SpawnBuilt();
        PumpFor(100, session.Alice, session.Bob);
        Assert.False(far.Network.IsVisibleTo(session.AliceHere));

        NetworkConfiguration off = Networking.Networking.Configuration;
        off.InterestMode = InterestMode.None;
        Networking.Networking.Configure(off);
        Pump(() => SpawnsOf(session.Alice, far) == 1 && SpawnsOf(session.Bob, far) == 1, session.Alice, session.Bob);
        Assert.Empty(NetworkTestScene.Current.Restricted);
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
        crate.SpawnBuilt();
        Pump(() => session.Alice.Has(FakePeer.Spawn), session.Alice, session.Bob);
        PumpFor(800, session.Alice, session.Bob);

        crate.Transform.Position = new Float3(50, 0, 0);
        Stopwatch left = Stopwatch.StartNew();
        Pump(() => session.Alice.Has(FakePeer.Despawn), session.Alice, session.Bob);
        Assert.InRange(left.Elapsed.TotalSeconds, 0.4, 30);
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
                avatar.SpawnBuilt(peer);
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
        Pump(() => Count(session.Alice, FakePeer.Scene) == 2 && Count(session.Bob, FakePeer.Scene) == 2,
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
        avatar.SpawnBuilt(session.AliceHere);
        GameObject near = CrateAt(session.World, 105, name: "Near");
        near.SpawnBuilt();
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

    [Fact]
    public void AnObserverWithAnEndlessRadiusSeesEveryObject()
    {
        using InterestSession session = InterestHost();
        GameObject crate = CrateAt(session.World, 50000);
        crate.SpawnBuilt();
        PumpFor(50, session.Alice, session.Bob);
        Assert.False(crate.Network.IsVisibleTo(session.AliceHere));

        GameObject avatar = CreateGameObject("Avatar");
        avatar.AddComponent<NetworkObserver>().Radius = float.PositiveInfinity;
        session.World.Add(avatar);
        avatar.SpawnBuilt(session.AliceHere);
        Pump(() => crate.Network.IsVisibleTo(session.AliceHere), session.Alice, session.Bob);
    }

    [Fact]
    public void AGridLooksUpAHugeRangeWithoutWalkingEveryCell()
    {
        InterestGrid grid = new();
        grid.Build([], 10, 1);
        HashSet<NetworkObject> found = new();
        Task lookup = Task.Run(() =>
        {
            grid.Gather(Float3.Zero, 1e9f, found);
            grid.Gather(Float3.Zero, float.MaxValue, found);
        });
        Assert.True(lookup.Wait(TimeSpan.FromSeconds(5)));
        Assert.Empty(found);
    }

    [Fact]
    public void TurningInterestOffAndOnWithinATickStillHidesFarObjects()
    {
        using InterestSession session = InterestHost();
        session.AliceSees.SetViewpoints([Float3.Zero]);
        GameObject far = CrateAt(session.World, 500, NetworkVisibility.Manual);
        far.SpawnBuilt();
        PumpFor(50, session.Alice, session.Bob);

        NetworkConfiguration configuration = Networking.Networking.Configuration;
        configuration.InterestMode = InterestMode.None;
        Networking.Networking.Configure(configuration);
        far.Network.Visibility = NetworkVisibility.Default;
        Assert.True(far.Network.IsVisibleTo(session.AliceHere));
        configuration.InterestMode = InterestMode.Distance;
        Networking.Networking.Configure(configuration);

        Pump(() => !far.Network.IsVisibleTo(session.AliceHere), session.Alice, session.Bob);
    }
}
