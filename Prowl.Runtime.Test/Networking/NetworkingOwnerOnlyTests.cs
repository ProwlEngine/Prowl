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
        peer.WireReceived.Where(m => m.Id == FakePeer.Spawn).Any(m => SpawnValues(m.Data).Any(v => v.Slot == slot));

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
            gun.SpawnBuilt(Networking.Networking.Session!.Peers.Single(p => p.Name == "alice"));
            Pump(() => alice.Has(FakePeer.Spawn) && bob.Has(FakePeer.Spawn), alice, bob);
            Assert.False(SpawnCarries(bob, AmmoTestComponent.AmmoSlot));
            Pump(() => alice.SyncInt(ammo.Identifier, AmmoTestComponent.AmmoSlot) == 30, alice, bob);

            ammo.Ammo = 12;
            Pump(() => alice.SyncInt(ammo.Identifier, AmmoTestComponent.AmmoSlot) == 12, alice, bob);
            PumpFor(5, alice, bob);
            Assert.Null(bob.SyncInt(ammo.Identifier, AmmoTestComponent.AmmoSlot));
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AnOwnerChangeGivesTheNewOwnerItsValuesAndTheLastOneWhatItWroteBack()
    {
        SceneInstance scene = CreateScene();
        GameObject gun = CreateGameObject("Gun");
        AmmoTestComponent ammo = gun.AddComponent<AmmoTestComponent>();
        NetTestComponent stats = gun.AddComponent<NetTestComponent>();
        ammo.Ammo = 30;
        scene.Add(gun);
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice"), bob = FakePeer.Join(network, "bob");
        try
        {
            Pump(() => alice.IsReady && bob.IsReady, alice, bob);
            NetPeer aliceHere = Networking.Networking.Session!.Peers.Single(p => p.Name == "alice");
            NetPeer bobHere = Networking.Networking.Session!.Peers.Single(p => p.Name == "bob");
            gun.SpawnBuilt(aliceHere);
            Pump(() => alice.SyncInt(ammo.Identifier, AmmoTestComponent.AmmoSlot) == 30, alice, bob);
            Assert.Null(bob.SyncInt(ammo.Identifier, AmmoTestComponent.AmmoSlot));

            // Alice wrote Health as the owner. The host has it, and bob, but nobody sends it back to her.
            alice.SendSync(stats.Identifier, NetTestComponent.HealthSlot, 42);
            Pump(() => stats.Health == 42 && bob.SyncInt(stats.Identifier, NetTestComponent.HealthSlot) == 42, alice, bob);
            Assert.Null(alice.SyncInt(stats.Identifier, NetTestComponent.HealthSlot));

            gun.Network.AssignOwnership(bobHere);
            Pump(() => bob.SyncInt(ammo.Identifier, AmmoTestComponent.AmmoSlot) == 30 &&
                       alice.SyncInt(stats.Identifier, NetTestComponent.HealthSlot) == 42, alice, bob);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ASyncValueOnAnObjectThatIsNotNetworkedIsSentNowhere()
    {
        SceneInstance scene = CreateScene();
        GameObject prop = CreateGameObject("Prop");
        NetTestComponent stats = prop.AddComponent<NetTestComponent>();
        scene.Add(prop);
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.IsReady, alice);
            stats.Score = 5;
            Assert.Equal(5, stats.Score);
            Assert.Equal(0, NetworkTestScene.Current.Sync.PendingCount);
            PumpFor(30, alice);
            Assert.False(alice.Has(FakePeer.Sync));
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
