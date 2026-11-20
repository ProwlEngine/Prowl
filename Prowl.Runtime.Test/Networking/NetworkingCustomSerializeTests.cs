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

public partial class NetworkingTests
{
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
            board.Network.SpawnSerialized();
            Pump(() => alice.Has(FakePeer.Spawn), alice);
            Assert.True(SpawnCarries(alice, NetworkSerializeMember.CustomSlot));

            // A spawn asks for the whole state; a send after SetDirty may hold only what changed.
            Assert.All(grid.WrittenInitial, Assert.True);
            grid.WrittenInitial.Clear();
            grid.Cells[0] = true;
            Networking.Networking.SetDirty(grid);
            Pump(() => alice.SnapshotValues().Any(v => v.Component == grid.Identifier && v.Slot == NetworkSerializeMember.CustomSlot), alice);
            Assert.Equal([false], grid.WrittenInitial);
            byte[] bytes = alice.SnapshotValues().Last(v => v.Component == grid.Identifier && v.Slot == NetworkSerializeMember.CustomSlot).Value;
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
