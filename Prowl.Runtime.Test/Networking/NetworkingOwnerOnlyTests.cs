using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>What the script compiler generates for <c>[Sync(SyncFlags.ToOwner)] public int Ammo</c>.</summary>
public class AmmoTestComponent : Component
{
    public const uint AmmoSlot = 971;
    private int __sync_Ammo;
    private static readonly SyncMember<AmmoTestComponent, int> __syncnet_Ammo =
        new(AmmoSlot, "Ammo", SyncFlags.ToOwner, static s => s.__sync_Ammo, static (s, v) => s.__sync_Ammo = v);
    public int Ammo { get => __sync_Ammo; set => __syncnet_Ammo.Set(this, ref __sync_Ammo, value); }
}

public partial class NetworkingTests
{
    private static bool SpawnCarries(FakePeer peer, uint slot) =>
        peer.WireReceived.Where(m => m.Id == FakePeer.Spawn).Any(m =>
        {
            NetReader reader = new(m.Data);
            SpawnMessage.Read(ref reader);
            uint count = SyncBlock.Read(ref reader).Count;
            for (uint i = 0; i < count; i++)
                if (SyncValue.Read(ref reader).Slot == slot) return true;
            return false;
        });

    [Fact]
    public void AnOwnerOnlyValueReachesTheOwnerAndNobodyElse()
    {
        SceneInstance scene = CreateScene();
        GameObject gun = CreateGameObject("Gun");
        AmmoTestComponent ammo = gun.AddComponent<AmmoTestComponent>();
        ammo.Ammo = 30;
        scene.Add(gun);
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice"), bob = FakePeer.Join(network, "bob");
        try
        {
            Pump(() => alice.IsReady && bob.IsReady, alice, bob);
            gun.Network.SpawnSerialized(Networking.Networking.Session!.Peers.Single(p => p.Name == "alice"));
            Pump(() => alice.Has(FakePeer.Spawn) && bob.Has(FakePeer.Spawn), alice, bob);
            Assert.False(SpawnCarries(bob, AmmoTestComponent.AmmoSlot));
            Pump(() => alice.SnapshotInt(ammo.Identifier, AmmoTestComponent.AmmoSlot) == 30, alice, bob);

            ammo.Ammo = 12;
            Pump(() => alice.SnapshotInt(ammo.Identifier, AmmoTestComponent.AmmoSlot) == 12, alice, bob);
            PumpFor(5, alice, bob);
            Assert.Null(bob.SnapshotInt(ammo.Identifier, AmmoTestComponent.AmmoSlot));
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
