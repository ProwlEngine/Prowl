using Prowl.Echo;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Courier;
using Prowl.Courier.Transports.Local;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>A broadcast RPC taking a GameObject, as the script compiler generates it.</summary>
public class BeaconTestComponent : NetworkBehaviour
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
public class SyncPairTestComponent : NetworkBehaviour
{
    public const uint PairSlot = 402;
    private TestReferencePair _pair;
    private static readonly SyncMember<SyncPairTestComponent, TestReferencePair> PairState = new(PairSlot, "Pair",
        static c => c._pair, static (c, v) => c._pair = v);
    public TestReferencePair Pair { get => _pair; set => PairState.Set(this, ref _pair, value); }

    public static void RegisterSerializer() => NetSerializer<TestReferencePair>.Register(
        static (w, v) =>
        {
            NetSerializer<GameObject?>.Write(w, v.First);
            NetSerializer<GameObject?>.Write(w, v.Second);
        },
        static (ref NetReader r) => new TestReferencePair(NetSerializer<GameObject?>.Read(ref r), NetSerializer<GameObject?>.Read(ref r)));
}

public partial class NetworkingTests
{
    private sealed record InterestSession(Scene World, FakePeer Alice, FakePeer Bob) : IDisposable
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

    private InterestSession InterestHost(NetworkInterest? interest)
    {
        Scene world = CreateScene();
        world.Add(CreateGameObject("Level"));
        MakeCurrent(world);
        LocalNetwork network = new();
        Networking.Networking.Interest = interest;
        Networking.Networking.Host(new LocalTransport(network, "host"));
        FakePeer alice = FakePeer.Join(network, "alice"), bob = FakePeer.Join(network, "bob");
        Pump(() => alice.IsReady && bob.IsReady, alice, bob);
        return new InterestSession(world, alice, bob);
    }

    private GameObject Placed(Scene world, string name, Float3 position)
    {
        GameObject go = CreateGameObject(name);
        go.Transform.Position = position;
        world.Add(go);
        return go;
    }

    private static int Spawns(FakePeer peer, GameObject go) =>
        peer.Received.Count(m => m.Id == FakePeer.Spawn && SpawnedId(m.Data) == go.Identifier);

    private static bool HasSpawn(FakePeer peer, GameObject go) => Spawns(peer, go) > 0;

    private static int Despawns(FakePeer peer, GameObject go) =>
        peer.Received.Count(m => m.Id == FakePeer.Despawn && new NetReader(m.Data).ReadGuid() == go.Identifier);

    private static bool HasDespawn(FakePeer peer, GameObject go) => Despawns(peer, go) > 0;

    // Distance interest of range 10, with Alice's player at the origin and Bob without one.
    private InterestSession NearAlice()
    {
        InterestSession session = InterestHost(new DistanceInterest { Range = 10, RebuildInterval = 0 });
        try
        {
            Player(session.World, session.AliceHere, "Alice", Float3.Zero);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private GameObject Player(Scene world, NetPeer peer, string name, Float3 position)
    {
        GameObject player = Placed(world, name, position);
        player.SpawnBuilt();
        Networking.Networking.AddPlayer(peer, player);
        return player;
    }

    private GameObject CrateAt(Scene world, float x, NetworkVisibility visibility = NetworkVisibility.Default, string name = "Crate")
    {
        GameObject crate = Crate(world, visibility, name);
        crate.Transform.Position = new Float3(x, 0, 0);
        return crate;
    }

    [Fact]
    public void WithoutAnInterestEveryClientReceivesEveryObject()
    {
        using InterestSession session = InterestHost(null);
        GameObject crate = Placed(session.World, "Crate", new Float3(1000, 0, 0));
        crate.SpawnBuilt();
        Pump(() => HasSpawn(session.Alice, crate) && HasSpawn(session.Bob, crate), session.Alice, session.Bob);
        Assert.True(crate.Network.IsVisibleTo(session.AliceHere));
        Assert.True(crate.Network.IsVisibleTo(session.BobHere));
    }

    [Fact]
    public void ForceHiddenReachesOnlyTheOwnerWithoutAnInterest()
    {
        using InterestSession session = InterestHost(null);
        GameObject crate = Placed(session.World, "Crate", Float3.Zero);
        crate.Network.Visibility = NetworkVisibility.ForceHidden;
        crate.SpawnBuilt(session.AliceHere);
        Pump(() => HasSpawn(session.Alice, crate), session.Alice, session.Bob);
        PumpFor(10, session.Alice, session.Bob);
        Assert.False(HasSpawn(session.Bob, crate));
        Assert.False(crate.Network.IsVisibleTo(session.BobHere));
    }

    [Fact]
    public void DistanceInterestSendsWhatIsNearEachPlayer()
    {
        using InterestSession session = InterestHost(new DistanceInterest { Range = 10, RebuildInterval = 0 });
        GameObject aliceAvatar = Placed(session.World, "Alice", Float3.Zero);
        GameObject bobAvatar = Placed(session.World, "Bob", new Float3(100, 0, 0));
        aliceAvatar.SpawnBuilt();
        Networking.Networking.AddPlayer(session.AliceHere, aliceAvatar);
        bobAvatar.SpawnBuilt();
        Networking.Networking.AddPlayer(session.BobHere, bobAvatar);
        GameObject crate = Placed(session.World, "Crate", new Float3(5, 0, 0));
        crate.SpawnBuilt();

        Pump(() => HasSpawn(session.Alice, crate) && HasSpawn(session.Bob, bobAvatar), session.Alice, session.Bob);
        PumpFor(10, session.Alice, session.Bob);
        Assert.False(HasSpawn(session.Bob, crate));
        Assert.False(HasSpawn(session.Alice, bobAvatar));

        crate.Transform.Position = new Float3(95, 0, 0);
        Pump(() => HasSpawn(session.Bob, crate) && HasDespawn(session.Alice, crate), session.Alice, session.Bob);
        Assert.False(crate.Network.IsVisibleTo(session.AliceHere));
        Assert.True(crate.Network.IsVisibleTo(session.BobHere));
    }

    [Fact]
    public void ARangeOnTheObjectReplacesTheInterestsOwn()
    {
        using InterestSession session = InterestHost(new DistanceInterest { Range = 10, RebuildInterval = 0 });
        GameObject aliceAvatar = Placed(session.World, "Alice", Float3.Zero);
        aliceAvatar.SpawnBuilt();
        Networking.Networking.AddPlayer(session.AliceHere, aliceAvatar);
        GameObject tower = Placed(session.World, "Tower", new Float3(50, 0, 0));
        tower.AddComponent<DistanceInterestRange>().Range = 100;
        tower.SpawnBuilt();
        Pump(() => HasSpawn(session.Alice, tower), session.Alice, session.Bob);

        // Bob has no player, so it receives nothing it doesn't own.
        PumpFor(10, session.Alice, session.Bob);
        Assert.False(HasSpawn(session.Bob, tower));
    }

    [Fact]
    public void TeamInterestSendsATeamsObjectsToItsMembers()
    {
        using InterestSession session = InterestHost(new TeamInterest());
        GameObject aliceAvatar = Placed(session.World, "Alice", Float3.Zero);
        aliceAvatar.AddComponent<NetworkTeam>().TeamId = "red";
        GameObject bobAvatar = Placed(session.World, "Bob", Float3.Zero);
        NetworkTeam bobTeam = bobAvatar.AddComponent<NetworkTeam>();
        bobTeam.TeamId = "blue";
        aliceAvatar.SpawnBuilt();
        Networking.Networking.AddPlayer(session.AliceHere, aliceAvatar);
        bobAvatar.SpawnBuilt();
        Networking.Networking.AddPlayer(session.BobHere, bobAvatar);

        GameObject flag = Placed(session.World, "Flag", Float3.Zero);
        flag.AddComponent<NetworkTeam>().TeamId = "red";
        flag.SpawnBuilt();
        GameObject crate = Placed(session.World, "Crate", Float3.Zero);
        crate.SpawnBuilt();
        GameObject banner = Placed(session.World, "Banner", Float3.Zero);
        banner.AddComponent<NetworkTeam>().ForceShown = true;
        banner.SpawnBuilt();

        Pump(() => HasSpawn(session.Alice, flag) && HasSpawn(session.Bob, crate) && HasSpawn(session.Bob, banner),
            session.Alice, session.Bob);
        PumpFor(10, session.Alice, session.Bob);
        Assert.False(HasSpawn(session.Bob, flag));
        Assert.False(HasSpawn(session.Bob, aliceAvatar));
        Assert.True(HasSpawn(session.Alice, crate));

        bobTeam.TeamId = "red";
        Pump(() => HasSpawn(session.Bob, flag) && HasSpawn(session.Bob, aliceAvatar), session.Alice, session.Bob);
        Assert.True(bobAvatar.Network.IsVisibleTo(session.AliceHere));
    }

    [Fact]
    public void ReplacingTheInterestWhileHostingRebuildsEveryObject()
    {
        using InterestSession session = InterestHost(null);
        GameObject crate = Placed(session.World, "Crate", Float3.Zero);
        crate.AddComponent<HiddenUntilShown>();
        crate.SpawnBuilt();
        Pump(() => HasSpawn(session.Alice, crate) && HasSpawn(session.Bob, crate), session.Alice, session.Bob);

        Networking.Networking.Interest = new ManualInterest();
        Pump(() => HasDespawn(session.Alice, crate) && HasDespawn(session.Bob, crate), session.Alice, session.Bob);
        Assert.False(crate.Network.IsVisibleTo(session.AliceHere));

        Networking.Networking.Interest = null;
        Pump(() => Spawns(session.Alice, crate) == 2 && Spawns(session.Bob, crate) == 2, session.Alice, session.Bob);
        Assert.True(crate.Network.IsVisibleTo(session.AliceHere));
    }

    [Fact]
    public void RpcReferencesGoByNetIdSoAClientLackingTheObjectReadsNull()
    {
        using InterestSession session = NearAlice();
        GameObject crate = CrateAt(session.World, 3);
        crate.SpawnBuilt();
        GameObject holder = CrateAt(session.World, 0, NetworkVisibility.ForceShown, "Holder");
        BeaconTestComponent beacon = holder.AddComponent<BeaconTestComponent>();
        holder.SpawnBuilt();
        Pump(() => Spawns(session.Bob, holder) == 1 && Spawns(session.Alice, crate) == 1, session.Alice, session.Bob);

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
        using InterestSession session = NearAlice();
        GameObject crate = CrateAt(session.World, 5);
        NetTestComponent stats = crate.GetComponent<NetTestComponent>()!;
        crate.SpawnBuilt();
        Pump(() => Spawns(session.Alice, crate) == 1, session.Alice, session.Bob);

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

        int spawns = Spawns(session.Alice, crate), despawns = Despawns(session.Alice, crate);
        Assert.True(despawns >= 3, $"Only {despawns} exits happened.");
        Assert.Equal(despawns + 1, spawns);
        Assert.All(session.Alice.Received.Where(m => m.Id == FakePeer.Spawn && SpawnedId(m.Data) == crate.Identifier).Skip(1),
            m => Assert.True(ReadSpawn(m.Data, Guid.Empty).Replace));
        Assert.True(Networking.Networking.IsActive);
        Assert.False(Mentions(session.Bob, crate.Identifier));
    }
}
