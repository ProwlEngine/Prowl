// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.CompilerServices;

using Prowl.Echo;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Warp;
using Prowl.Warp.Transports.Local;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// A game struct, with by hand what Prowl's script compiler generates once a [Sync] value uses it: a
/// serializer registered from a module initializer. (The generated one lives in a class of its own and
/// reaches the members through UnsafeAccessor.)
/// </summary>
public struct Loadout
{
    public int Ammo;
    public string Weapon;

    internal static void __Net_Write(NetWriter __w, Loadout __v) { NetSerializer<int>.Write(__w, __v.Ammo); NetSerializer<string>.Write(__w, __v.Weapon); }

    internal static Loadout __Net_Read(ref NetReader __r) { Loadout __v = new(); __v.Ammo = NetSerializer<int>.Read(ref __r); __v.Weapon = NetSerializer<string>.Read(ref __r); return __v; }

    [ModuleInitializer] internal static void __Net_Register() => NetSerializer<Loadout>.Register(__Net_Write, __Net_Read);
}

/// <summary>
/// Exactly what Prowl's script compiler generates for
/// <code>
/// [Sync, Change(nameof(OnHealthChanged))] public int Health { get; set; } = 100;
/// [Sync] public NetList&lt;int&gt; Scores { get; } = new();
/// [Sync] public NetDictionary&lt;string, int&gt; Stats { get; } = new();
/// [Sync] public NetSet&lt;string&gt; Tags { get; } = new();
/// [Sync] public Loadout Loadout { get; set; }
/// </code>
/// written out by hand, with what its handlers saw.
/// </summary>
public class NetCollectionTestComponent : Component
{
    public const uint HealthSlot = 301, ScoresSlot = 302, StatsSlot = 303, TagsSlot = 304, LoadoutSlot = 305;

    // What this copy saw, not part of the scene a host sends.
    [SerializeIgnore] public readonly List<(int Old, int New)> HealthChanges = new();
    [SerializeIgnore] public readonly List<NetListChange<int>> ScoreChanges = new();
    [SerializeIgnore] public readonly List<NetDictionaryChange<string, int>> StatChanges = new();
    [SerializeIgnore] public readonly List<NetSetChange<string>> TagChanges = new();
    [SerializeIgnore] public int? HealthWhenEnabled;
    [SerializeIgnore] public int? ScoreCountWhenEnabled;

    private int __sync_Health = 100;
    private static readonly SyncMember<NetCollectionTestComponent, int> __syncnet_Health = new(HealthSlot, "Health", SyncFlags.None, static s => s.__sync_Health, static (s, v) => s.__sync_Health = v, static (s, o, n) => s.OnHealthChanged(o, n));
    public int Health { get => __sync_Health; set => __syncnet_Health.Set(this, ref __sync_Health, value); }

    private readonly NetList<int> __sync_Scores = new();
    private static readonly SyncCollectionMember<NetCollectionTestComponent, NetList<int>> __syncnet_Scores = new(ScoresSlot, "Scores", SyncFlags.None, static s => s.__sync_Scores);
    public NetList<int> Scores { get => __syncnet_Scores.Bind(this); }

    private readonly NetDictionary<string, int> __sync_Stats = new();
    private static readonly SyncCollectionMember<NetCollectionTestComponent, NetDictionary<string, int>> __syncnet_Stats = new(StatsSlot, "Stats", SyncFlags.None, static s => s.__sync_Stats);
    public NetDictionary<string, int> Stats { get => __syncnet_Stats.Bind(this); }

    private readonly NetSet<string> __sync_Tags = new();
    private static readonly SyncCollectionMember<NetCollectionTestComponent, NetSet<string>> __syncnet_Tags = new(TagsSlot, "Tags", SyncFlags.None, static s => s.__sync_Tags);
    public NetSet<string> Tags { get => __syncnet_Tags.Bind(this); }

    private Loadout __sync_Loadout;
    private static readonly SyncMember<NetCollectionTestComponent, Loadout> __syncnet_Loadout = new(LoadoutSlot, "Loadout", SyncFlags.None, static s => s.__sync_Loadout, static (s, v) => s.__sync_Loadout = v);
    public Loadout Loadout { get => __sync_Loadout; set => __syncnet_Loadout.Set(this, ref __sync_Loadout, value); }

    public NetCollectionTestComponent()
    {
        __sync_Scores.Changed += ScoreChanges.Add;
        __sync_Stats.Changed += StatChanges.Add;
        __sync_Tags.Changed += TagChanges.Add;
    }

    private void OnHealthChanged(int oldValue, int newValue) => HealthChanges.Add((oldValue, newValue));

    public override void OnEnable()
    {
        HealthWhenEnabled = Health;
        ScoreCountWhenEnabled = Scores.Count;
    }

    public void ClearSeen()
    {
        HealthChanges.Clear();
        ScoreChanges.Clear();
        StatChanges.Clear();
        TagChanges.Clear();
    }
}

public partial class NetworkingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitialCollectionsRejectOperationsAndRpcPayloadsWithoutChangingContents(bool rpcPayload)
    {
        NetList<int> source = new();
        source.Add(1);
        using NetWriter writer = new();
        if (rpcPayload) source.WriteRpc(writer);
        else source.WriteChanges(writer);
        byte[] data = writer.ToArray();
        NetList<int> target = new();
        target.Add(9);
        int notifications = 0;
        target.Changed += _ => notifications++;
        Assert.Throws<System.IO.InvalidDataException>(() =>
        {
            NetReader reader = new(data);
            target.Read(ref reader, initial: true);
        });
        Assert.Equal(new[] { 9 }, target);
        Assert.Equal(0, notifications);
    }

    /// <summary>Writes every [Sync] value of the component, as a snapshot or spawn carries it.</summary>
    private static Dictionary<uint, byte[]> WholeState(Component component)
    {
        Dictionary<uint, byte[]> state = new();
        foreach (SyncMember sync in NetworkMembers.Of(component).Syncs)
        {
            using NetWriter writer = new();
            sync.Write(component, writer);
            state[sync.Slot] = writer.ToArray();
        }
        return state;
    }

    /// <summary>Applies values to <paramref name="replica" /> the way a receiving peer does, one by one, notifying as it goes.</summary>
    private static void Apply(Component replica, IEnumerable<KeyValuePair<uint, byte[]>> values, bool initial)
    {
        foreach ((uint slot, byte[] bytes) in values)
        {
            NetReader reader = new(bytes);
            NetworkMembers.Of(replica).Sync(slot)!.Read(replica, ref reader, initial);
            Assert.True(reader.IsAtEnd);
        }
    }

    /// <summary>The values in every Sync message <paramref name="peer" /> received for <paramref name="component" />, in order.</summary>
    private static List<KeyValuePair<uint, byte[]>> SyncValues(FakePeer peer, Guid component)
    {
        List<KeyValuePair<uint, byte[]>> values = new();
        foreach ((ushort id, byte[] data) in peer.Received.Where(m => m.Id == FakePeer.Sync))
        {
            NetReader reader = new(data);
            uint count = reader.ReadVarUInt32();
            for (uint i = 0; i < count; i++)
            {
                Guid target = reader.ReadGuid();
                uint slot = reader.ReadVarUInt32();
                byte[] value = reader.ReadBytesWithLength().ToArray();
                if (target == component)
                    values.Add(new(slot, value));
            }
        }
        return values;
    }

    private static byte[] ListChange(uint sequence, params Action<NetWriter>[] changes)
    {
        using NetWriter writer = new();
        writer.WriteByte(1);
        writer.WriteVarUInt32(sequence);
        writer.WriteVarUInt32((uint)changes.Length);
        foreach (Action<NetWriter> change in changes)
            change(writer);
        return writer.ToArray();
    }

    private void SendSyncBytes(FakePeer peer, Guid component, uint slot, byte[] value) => peer.Send(FakePeer.Sync, w =>
    {
        w.WriteVarUInt32(1);
        w.WriteGuid(component);
        w.WriteVarUInt32(slot);
        w.WriteBytesWithLength(value);
    });

    // --- Offline ---------------------------------------------------------------------------------

    [Fact]
    public void HooksAndCollectionsWorkWithoutASession()
    {
        GameObject go = CreateGameObject();
        NetCollectionTestComponent component = go.AddComponent<NetCollectionTestComponent>();

        component.Health = 5;
        component.Health = 5;
        Assert.Equal([(100, 5)], component.HealthChanges);

        component.Scores.Add(1);
        component.Scores.Add(2);
        component.Scores[0] = 3;
        Assert.True(component.Scores.Remove(2));
        component.Scores.Clear();
        Assert.Empty(component.Scores);
        Assert.Equal(
            [
                new NetListChange<int>(NetChangeKind.Added, 0, 0, 1), new(NetChangeKind.Added, 1, 0, 2),
                new(NetChangeKind.Replaced, 0, 1, 3), new(NetChangeKind.Removed, 1, 2, 0), new(NetChangeKind.Cleared, -1, 0, 0)
            ],
            component.ScoreChanges);

        component.Stats["hp"] = 1;
        component.Stats["hp"] = 2;
        Assert.False(component.Stats.TryAdd("hp", 9));
        Assert.True(component.Stats.Remove("hp"));
        Assert.Equal([NetChangeKind.Added, NetChangeKind.Replaced, NetChangeKind.Removed], component.StatChanges.Select(c => c.Kind));

        Assert.True(component.Tags.Add("red"));
        Assert.False(component.Tags.Add("red"));
        component.Tags.SymmetricExceptWith(["red", "blue"]);
        Assert.Equal(["blue"], component.Tags);
    }

    [Fact]
    public void RegisteredSerializersRoundTripAndBuiltInsCantBeReplaced()
    {
        Assert.True(NetSerializer<Loadout>.IsSupported);
        using NetWriter writer = new();
        NetSerializer<Loadout>.Write(writer, new Loadout { Ammo = 30, Weapon = "rifle" });
        NetReader reader = new(writer.WrittenSpan);
        Loadout back = NetSerializer<Loadout>.Read(ref reader);
        Assert.Equal((30, "rifle"), (back.Ammo, back.Weapon));

        Assert.Throws<InvalidOperationException>(() => NetSerializer<int>.Register((w, v) => { }, (ref NetReader r) => 0));
    }

    // --- Host side -------------------------------------------------------------------------------

    [Fact]
    public void HostSendsEachChangeAndAJoinersWholeStateIsntChangedTwice()
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);

        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.SceneSnapshot), alice);

            GameObject crate = CreateGameObject("Crate");
            NetCollectionTestComponent component = crate.AddComponent<NetCollectionTestComponent>();
            scene.Add(crate);
            crate.Network.Spawn();
            Pump(() => alice.Has(FakePeer.Spawn), alice);

            // An earlier joiner has what the spawn carried; a later one will get the whole state mid-change.
            NetCollectionTestComponent earlier = CreateGameObject("Earlier").AddComponent<NetCollectionTestComponent>();
            Apply(earlier, WholeState(component), initial: true);
            alice.Received.Clear();

            component.Health = 40;
            component.Scores.Add(1);
            component.Scores.Add(2);
            component.Scores[0] = 5;
            component.Scores.Insert(0, 9);
            component.Scores.RemoveAt(2);
            component.Stats["hp"] = 3;
            component.Stats["mp"] = 1;
            component.Stats.Remove("mp");
            component.Tags.Add("red");
            component.Loadout = new Loadout { Ammo = 12, Weapon = "pistol" };
            Assert.Equal([9, 5], component.Scores);

            NetCollectionTestComponent later = CreateGameObject("Later").AddComponent<NetCollectionTestComponent>();
            Apply(later, WholeState(component), initial: true);

            Pump(() => SyncValues(alice, component.Identifier).Any(v => v.Key == NetCollectionTestComponent.ScoresSlot), alice);
            List<KeyValuePair<uint, byte[]>> changes = SyncValues(alice, component.Identifier);
            Apply(earlier, changes, initial: false);
            Apply(later, changes, initial: false);

            foreach (NetCollectionTestComponent replica in new[] { earlier, later })
            {
                Assert.Equal(40, replica.Health);
                Assert.Equal([9, 5], replica.Scores);
                Assert.Equal(new Dictionary<string, int> { ["hp"] = 3 }, replica.Stats.ToDictionary());
                Assert.Equal(["red"], replica.Tags);
                Assert.Equal((12, "pistol"), (replica.Loadout.Ammo, replica.Loadout.Weapon));
            }

            // The earlier one saw each change, in order; the later one had them all already, so saw none.
            Assert.Equal([(100, 40)], earlier.HealthChanges);
            Assert.Equal([NetChangeKind.Added, NetChangeKind.Added, NetChangeKind.Replaced, NetChangeKind.Added, NetChangeKind.Removed],
                earlier.ScoreChanges.Select(c => c.Kind));
            Assert.Equal(new NetListChange<int>(NetChangeKind.Removed, 2, 2, 0), earlier.ScoreChanges[^1]);
            Assert.Equal(3, earlier.StatChanges.Count);
            Assert.Single(earlier.TagChanges);
            Assert.Empty(later.HealthChanges);
            Assert.Empty(later.ScoreChanges);
            Assert.Empty(later.StatChanges);
            Assert.Empty(later.TagChanges);

            // From here on both are in step, and a change is one small message, not the list.
            alice.Received.Clear();
            later.ClearSeen();
            component.Scores.Add(4);
            Pump(() => SyncValues(alice, component.Identifier).Count > 0, alice);
            changes = SyncValues(alice, component.Identifier);
            Assert.Equal(NetCollectionTestComponent.ScoresSlot, Assert.Single(changes).Key);
            Apply(later, changes, initial: false);
            Assert.Equal([9, 5, 4], later.Scores);
            Assert.Equal([new NetListChange<int>(NetChangeKind.Added, 2, 0, 4)], later.ScoreChanges);
        }
        finally
        {
            Networking.Networking.Shutdown();
            alice.Dispose();
        }
    }

    [Fact]
    public void OwnersChangeTheirCollectionsAndNobodyElseCan()
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);

        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.SceneSnapshot), alice);
            NetPeer aliceHere = Networking.Networking.Session!.Peers.Single(p => !p.IsLocal);

            GameObject ship = CreateGameObject("Ship");
            NetCollectionTestComponent component = ship.AddComponent<NetCollectionTestComponent>();
            scene.Add(ship);
            ship.Network.Spawn(aliceHere);
            Pump(() => alice.Has(FakePeer.Spawn), alice);

            // Hers, so the host's changes go nowhere and tell nobody.
            component.Scores.Add(1);
            component.Stats["hp"] = 1;
            Assert.False(component.Tags.Add("x"));
            Assert.Empty(component.Scores);
            Assert.Empty(component.Stats);
            Assert.Empty(component.ScoreChanges);

            // Hers land, with the hooks seeing them once the whole block is in.
            alice.SendSync(component.Identifier, NetCollectionTestComponent.HealthSlot, 42);
            byte[] add42 = ListChange(0, w => { w.WriteByte((byte)NetChangeKind.Added); w.WriteVarUInt32(0); w.WriteVarInt32(42); });
            SendSyncBytes(alice, component.Identifier, NetCollectionTestComponent.ScoresSlot, add42);
            Pump(() => component.Scores.Count == 1 && component.Health == 42, alice);
            Assert.Equal([(100, 42)], component.HealthChanges);
            Assert.Equal([new NetListChange<int>(NetChangeKind.Added, 0, 0, 42)], component.ScoreChanges);

            // The same change twice is the same change.
            SendSyncBytes(alice, component.Identifier, NetCollectionTestComponent.ScoresSlot, add42);
            byte[] add7 = ListChange(1, w => { w.WriteByte((byte)NetChangeKind.Added); w.WriteVarUInt32(1); w.WriteVarInt32(7); });
            SendSyncBytes(alice, component.Identifier, NetCollectionTestComponent.ScoresSlot, add7);
            Pump(() => component.Scores.Count == 2, alice);
            PumpFor(30, alice);
            Assert.Equal([42, 7], component.Scores);
        }
        finally
        {
            Networking.Networking.Shutdown();
            alice.Dispose();
        }
    }

    // --- Client side -----------------------------------------------------------------------------

    [Fact]
    public void ReceivedStateIsInPlaceBeforeComponentsStartAndRunsNoHooks()
    {
        Scene hostScene = CreateScene();
        GameObject level = CreateGameObject("Level");
        NetCollectionTestComponent levelSource = level.AddComponent<NetCollectionTestComponent>();
        levelSource.Health = 3;
        levelSource.Scores.Add(1);
        hostScene.Add(level);
        using NetWriter snapshot = new();
        Networking.Networking.WriteSnapshot(hostScene, snapshot);

        GameObject ship = CreateGameObject("Ship");
        NetCollectionTestComponent shipSource = ship.AddComponent<NetCollectionTestComponent>();
        shipSource.Health = 9;
        shipSource.Scores.Add(1);
        shipSource.Scores.Add(2);
        ship.Network.Mode = NetworkMode.Object;
        using NetWriter shipSpawn = new();
        Networking.Networking.WriteSpawn(ship, owner: -1, NetworkOrphaned.Destroy, shipSpawn);
        Guid levelId = level.Identifier, shipId = ship.Identifier;

        LocalNetwork network = new();
        FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer =>
        {
            host.Session.SendRaw(peer, FakePeer.SceneSnapshot, snapshot.WrittenSpan);
            host.Session.SendRaw(peer, FakePeer.Spawn, shipSpawn.WrittenSpan);
        };

        Networking.Networking.Join(new LocalTransport(network, "bob"), "host", "bob");
        try
        {
            Pump(() => Scene.Current.AllObjects.Any(o => o.Identifier == shipId), host);
            NetCollectionTestComponent levelHere = Find(Scene.Current, levelId).GetComponent<NetCollectionTestComponent>()!;
            NetCollectionTestComponent shipHere = Find(Scene.Current, shipId).GetComponent<NetCollectionTestComponent>()!;

            Assert.Equal(3, levelHere.Health);
            Assert.Equal([1], levelHere.Scores);
            Assert.Equal(9, shipHere.Health);
            Assert.Equal([1, 2], shipHere.Scores);
            Assert.Equal((3, 1), (levelHere.HealthWhenEnabled, levelHere.ScoreCountWhenEnabled));
            Assert.Equal((9, 2), (shipHere.HealthWhenEnabled, shipHere.ScoreCountWhenEnabled));
            Assert.Empty(levelHere.HealthChanges);
            Assert.Empty(levelHere.ScoreChanges);
            Assert.Empty(shipHere.HealthChanges);
            Assert.Empty(shipHere.ScoreChanges);

            // The host's, so not ours to change.
            shipHere.Scores.Add(5);
            Assert.Equal([1, 2], shipHere.Scores);

            // Later changes run the hooks.
            NetPeer bob = host.Session.Peers.Single(p => !p.IsLocal);
            host.Send(FakePeer.Sync, w =>
            {
                w.WriteVarUInt32(2);
                w.WriteGuid(shipHere.Identifier);
                w.WriteVarUInt32(NetCollectionTestComponent.HealthSlot);
                w.WriteBytesWithLength([(byte)(8 << 1)]);
                w.WriteGuid(shipHere.Identifier);
                w.WriteVarUInt32(NetCollectionTestComponent.ScoresSlot);
                // Its items were added before any session, so this is the first change anyone counted.
                w.WriteBytesWithLength(ListChange(0, c => c.WriteByte((byte)NetChangeKind.Cleared)));
            }, to: bob);
            Pump(() => shipHere.Health == 8, host);
            Assert.Equal([(9, 8)], shipHere.HealthChanges);
            Assert.Empty(shipHere.Scores);
            Assert.Equal(NetChangeKind.Cleared, Assert.Single(shipHere.ScoreChanges).Kind);
        }
        finally
        {
            Networking.Networking.Shutdown();
            host.Dispose();
        }
    }
}
