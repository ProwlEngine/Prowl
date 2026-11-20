using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>A grid of cells packed into bits by hand, as Mirror's OnSerialize allows.</summary>
public class PackedGridTestComponent : Component, INetworkSerialize
{
    public bool[] Cells = new bool[16];
    public readonly List<bool> Initial = new();
    public readonly List<bool> WrittenInitial = new();

    public void OnSerialize(NetWriter writer, bool initial)
    {
        WrittenInitial.Add(initial);
        ushort bits = 0;
        for (int i = 0; i < Cells.Length; i++) if (Cells[i]) bits |= (ushort)(1 << i);
        writer.WriteUInt16(bits);
    }

    public void OnDeserialize(ref NetReader reader, bool initial)
    {
        ushort bits = reader.ReadUInt16();
        for (int i = 0; i < Cells.Length; i++) Cells[i] = (bits & (1 << i)) != 0;
        Initial.Add(initial);
    }
}

/// <summary>Game code that fails while reading its own state.</summary>
public class FailingStateTestComponent : Component, INetworkSerialize
{
    public static Func<Exception>? Failure;
    public void OnSerialize(NetWriter writer, bool initial) => writer.WriteByte(1);

    public void OnDeserialize(ref NetReader reader, bool initial)
    {
        if (Failure != null) throw Failure();
        reader.ReadByte();
    }
}

/// <summary>Game code that changes the network while it is asked for its state.</summary>
public class MeddlingStateTestComponent : Component, INetworkSerialize
{
    [Prowl.Echo.SerializeIgnore] public Action? WhileWriting;
    [Prowl.Echo.SerializeIgnore] public Exception? Thrown;

    public void OnSerialize(NetWriter writer, bool initial)
    {
        writer.WriteByte(1);
        if (initial || WhileWriting == null) return;
        try { WhileWriting(); }
        catch (Exception e) { Thrown = e; }
    }

    public void OnDeserialize(ref NetReader reader, bool initial) => reader.ReadByte();
}

public partial class NetworkingTests
{
    [Fact]
    public void SpawningOrHandingOverWhileStateIsWrittenIsRefused()
    {
        Networking.Networking.RegisterFactory("crate", BuildCrate);
        SceneInstance scene = CreateScene();
        GameObject root = CreateGameObject("Meddler"), other = CreateGameObject("Other");
        root.Network.Networked = true;
        other.Network.Networked = true;
        MeddlingStateTestComponent meddler = root.AddComponent<MeddlingStateTestComponent>();
        scene.Add(root); scene.Add(other);
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.IsReady, alice);
            NetPeer aliceHere = Networking.Networking.Session!.Peers.Single(p => !p.IsLocal);
            alice.WireReceived.Clear();

            // Each of these writes state of its own, into the buffers the state being written is using.
            foreach (Action change in new Action[]
                     {
                         () => Networking.Networking.SpawnFactory("crate", default, Prowl.Vector.Quaternion.Identity),
                         () => other.Network.AssignOwnership(aliceHere),
                         () => other.Network.HideFrom(aliceHere),
                     })
            {
                meddler.Thrown = null;
                meddler.WhileWriting = change;
                Networking.Networking.SetDirty(meddler);
                Pump(() => meddler.Thrown != null, alice);
                Assert.IsType<InvalidOperationException>(meddler.Thrown);
            }

            meddler.WhileWriting = null;
            PumpFor(30, alice);
            Assert.DoesNotContain(scene.Objects, o => o.Name == "Crate");
            Assert.Null(other.Network.Owner);
            Assert.True(other.Network.IsVisibleTo(aliceHere));
            Assert.DoesNotContain(alice.WireReceived, m => m.Id is FakePeer.Spawn or FakePeer.Despawn or FakePeer.Owner);
            Assert.True(Networking.Networking.IsActive);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GameCodeFailingToReadItsStateLosesOnlyThatComponentsState(bool argument)
    {
        SceneInstance hostScene = CreateScene();
        using NetWriter snapshot = new();
        NetworkTestScene.Current.WriteScene(hostScene, snapshot);
        GameObject original = CreateGameObject("Fragile");
        original.AddComponent<FailingStateTestComponent>();
        original.AddComponent<NetTestComponent>().Health = 9;
        original.Network.Networked = true;
        using NetWriter spawn = new();
        NetworkTestScene.Current.WriteSpawn(original, -1, NetworkOrphaned.Destroy, spawn, 1, 4);

        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        SendJoin(host, snapshot.ToArray(), spawn.ToArray());
        FailingStateTestComponent.Failure = argument
            ? () => new ArgumentException("bad cell")
            : () => new InvalidOperationException("not ready");
        try
        {
            // As Mirror catches what OnDeserialize throws and reads on at the next component: the spawn still
            // arrives with the rest of its state, and the session goes on.
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady && SceneInstance.Main.Objects.Any(o => o.Identifier == original.Identifier), host);
            GameObject copy = Find(SceneInstance.Main, original.Identifier);
            NetTestComponent stats = copy.GetComponent<NetTestComponent>()!;
            Assert.Equal(9, stats.Health);

            NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);
            host.SendSyncValues(client, 0, 1,
                (copy.GetComponent<FailingStateTestComponent>()!.Identifier, NetworkSerializeMember.CustomSlot, 1, new byte[] { 0, 1 }),
                (stats.Identifier, NetTestComponent.HealthSlot, 1, IntValue(42)));
            Pump(() => stats.Health == 42, host);
            Assert.True(Networking.Networking.IsActive);
        }
        finally
        {
            FailingStateTestComponent.Failure = null;
            Networking.Networking.Shutdown();
        }
    }

    [Fact]
    public void AComponentWritesItsOwnStateAndSendsItWhenMarkedDirty()
    {
        SceneInstance scene = CreateScene();
        GameObject board = CreateGameObject("Board");
        PackedGridTestComponent grid = board.AddComponent<PackedGridTestComponent>();
        grid.Cells[3] = true;
        scene.Add(board);
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.IsReady, alice);
            board.SpawnBuilt();
            Pump(() => alice.Has(FakePeer.Spawn), alice);
            Assert.True(SpawnCarries(alice, NetworkSerializeMember.CustomSlot));

            // A spawn asks for the whole state; a send after SetDirty may hold only what changed.
            Assert.All(grid.WrittenInitial, Assert.True);
            grid.WrittenInitial.Clear();
            grid.Cells[0] = true;
            Networking.Networking.SetDirty(grid);
            Pump(() => alice.SyncValues().Any(v => v.Component == grid.Identifier && v.Slot == NetworkSerializeMember.CustomSlot), alice);
            Assert.Equal([false], grid.WrittenInitial);
            byte[] bytes = alice.SyncValues().Last(v => v.Component == grid.Identifier && v.Slot == NetworkSerializeMember.CustomSlot).Value;
            NetReader sent = new(bytes);
            Assert.False(sent.ReadBool());
            Assert.Equal(0b1001, sent.ReadUInt16());

            PackedGridTestComponent copy = CreateGameObject("Copy").AddComponent<PackedGridTestComponent>();
            NetReader reader = new(bytes);
            NetworkMembers.Of(copy).Sync(NetworkSerializeMember.CustomSlot)!.Read(copy, ref reader, true);
            Assert.True(copy.Cells[0] && copy.Cells[3] && !copy.Cells[1]);
            Assert.Equal([false], copy.Initial);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void WholeStateIsReadAsInitialWhateverMessageCarriesIt()
    {
        PackedGridTestComponent source = CreateGameObject("Source").AddComponent<PackedGridTestComponent>();
        source.Cells[5] = true;
        SyncMember member = NetworkMembers.Of(source).Sync(NetworkSerializeMember.CustomSlot)!;

        // Written whole, as for a new owner, but arriving in a state record that reads as a change.
        using NetWriter whole = new();
        member.Write(source, whole);
        PackedGridTestComponent copy = CreateGameObject("Copy").AddComponent<PackedGridTestComponent>();
        NetReader reader = new(whole.WrittenSpan);
        member.Read(copy, ref reader, false);
        Assert.True(copy.Cells[5]);
        Assert.Equal([true], copy.Initial);

        using NetWriter changes = new();
        member.WriteChanges(source, changes);
        reader = new(changes.WrittenSpan);
        member.Read(copy, ref reader, true);
        Assert.Equal([true, false], copy.Initial);
    }

    [Fact]
    public void ComponentsWritingTheirOwnStateArePartOfTheSchema() =>
        Assert.Contains(NetworkSchema.Describe(typeof(PackedGridTestComponent)), e => e.Contains("serialize"));
}
