using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Courier;
using Prowl.Courier.Transports.Local;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// What the script compiler generates for
/// <code>
/// [Sync] public int Health { get; set; } = 100;
/// [Sync] public TestShape? Shape { get; set; }
/// [Sync] public NetList&lt;TestShape&gt; Shapes { get; } = new();
/// </code>
/// written out by hand.
/// </summary>
public class ShapeHolderTestComponent : Component
{
    public const uint HealthSlot = 501, ShapeSlot = 502, ShapesSlot = 503;

    private int __sync_Health = 100;
    private static readonly SyncMember<ShapeHolderTestComponent, int> __syncnet_Health = new(HealthSlot, "Health", SyncFlags.None, static s => s.__sync_Health, static (s, v) => s.__sync_Health = v);
    public int Health { get => __sync_Health; set => __syncnet_Health.Set(this, ref __sync_Health, value); }

    private TestShape? __sync_Shape;
    private static readonly SyncMember<ShapeHolderTestComponent, TestShape?> __syncnet_Shape = new(ShapeSlot, "Shape", SyncFlags.None, static s => s.__sync_Shape, static (s, v) => s.__sync_Shape = v);
    public TestShape? Shape { get => __sync_Shape; set => __syncnet_Shape.Set(this, ref __sync_Shape, value); }

    private readonly NetList<TestShape> __sync_Shapes = new();
    private static readonly SyncCollectionMember<ShapeHolderTestComponent, NetList<TestShape>> __syncnet_Shapes = new(ShapesSlot, "Shapes", SyncFlags.None, static s => s.__sync_Shapes);
    public NetList<TestShape> Shapes { get => __syncnet_Shapes.Bind(this); }
}

public partial class NetworkingTests
{
    private (SceneInstance Scene, ShapeHolderTestComponent Holder) SpawnShapeHolder(FakePeer alice)
    {
        SceneInstance scene = SceneInstance.Main;
        Pump(() => alice.IsReady, alice);
        GameObject crate = CreateGameObject("Crate");
        ShapeHolderTestComponent holder = crate.AddComponent<ShapeHolderTestComponent>();
        scene.Add(crate);
        crate.Network.Spawn();
        Pump(() => alice.Has(FakePeer.Spawn), alice);
        return (scene, holder);
    }

    [Fact]
    public void AValueThatCannotBeSentIsDroppedAndTheRestStillGo()
    {
        MakeCurrent(CreateScene());
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            (_, ShapeHolderTestComponent holder) = SpawnShapeHolder(alice);
            holder.Shape = new TestTriangle();
            holder.Health = 7;
            Pump(() => alice.SnapshotInt(holder.Identifier, ShapeHolderTestComponent.HealthSlot) == 7, alice);
            holder.Health = 8;
            Pump(() => alice.SnapshotInt(holder.Identifier, ShapeHolderTestComponent.HealthSlot) == 8, alice);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AnItemThatCannotBeSentLeavesNoHalfWrittenChange()
    {
        MakeCurrent(CreateScene());
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            (_, ShapeHolderTestComponent holder) = SpawnShapeHolder(alice);
            ShapeHolderTestComponent replica = CreateGameObject("Replica").AddComponent<ShapeHolderTestComponent>();
            Apply(replica, WholeState(holder), initial: true);

            Assert.Throws<NotSupportedException>(() => holder.Shapes.Add(new TestTriangle()));
            Assert.Empty(holder.Shapes);
            holder.Shapes.Add(new TestSquare { Side = 2 });
            Pump(() => SyncValues(alice, holder.Identifier).Any(v => v.Key == ShapeHolderTestComponent.ShapesSlot), alice);
            Apply(replica, SyncValues(alice, holder.Identifier).Where(v => v.Key == ShapeHolderTestComponent.ShapesSlot), initial: false);
            Assert.Equal(2, Assert.IsType<TestSquare>(Assert.Single(replica.Shapes)).Side);
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
