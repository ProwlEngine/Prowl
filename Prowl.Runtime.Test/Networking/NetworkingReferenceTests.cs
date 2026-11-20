using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void ASyncReferenceToAnObjectThatArrivesLaterStartsReadingAsIt()
    {
        SceneInstance scene = CreateScene();
        GameObject holder = CreateGameObject("Holder");
        holder.Network.Networked = true;
        SyncReferenceTestComponent pointer = holder.AddComponent<SyncReferenceTestComponent>();
        GameObject chest = CreateGameObject("Chest");
        chest.Network.Networked = true;
        scene.Add(holder);
        scene.Add(chest);
        MakeCurrent(scene);
        Guid holderId = holder.Identifier, chestId = chest.Identifier;
        byte[] sceneMessage;
        byte[][] spawns;
        LocalNetwork source = new();
        Networking.Networking.Host(new LocalTransport(source, "host"));
        try
        {
            pointer.Target = chest;
            (sceneMessage, spawns) = RecordJoin(source);
        }
        finally { Networking.Networking.Shutdown(); }

        byte[] holderSpawn = spawns.Single(s => SpawnedId(s) == holderId);
        byte[] chestSpawn = spawns.Single(s => SpawnedId(s) == chestId);

        MakeCurrent(CreateScene());
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        SendJoin(host, sceneMessage, holderSpawn);
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady && SceneInstance.Main.Objects.Any(o => o.Identifier == holderId), host);
            SyncReferenceTestComponent here = Find(SceneInstance.Main, holderId).GetComponent<SyncReferenceTestComponent>()!;

            // The chest hasn't been spawned here, so the reference names nothing yet.
            Assert.Null(here.Target);

            host.Send(FakePeer.Spawn, w => w.WriteBytes(chestSpawn), host.Session.Peers.Single(p => !p.IsLocal));
            Pump(() => SceneInstance.Main.Objects.Any(o => o.Identifier == chestId), host);
            Assert.Same(Find(SceneInstance.Main, chestId), here.Target);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AReferenceToSomethingThatIsNotNetworkedGoesAsNull()
    {
        SceneInstance scene = CreateScene();
        GameObject ship = CreateGameObject("Ship");
        ship.Network.Networked = true;
        NetTestComponent stats = ship.AddComponent<NetTestComponent>();
        PlainTestComponent plain = ship.AddComponent<PlainTestComponent>();
        GameObject turret = CreateGameObject("Turret");
        turret.SetParent(ship, false);
        GameObject prop = CreateGameObject("Prop");
        scene.Add(ship);
        scene.Add(prop);
        MakeCurrent(scene);
        Networking.Networking.Host(new LocalTransport(new LocalNetwork(), "host"));
        try
        {
            static byte[] Written<T>(T value)
            {
                using NetWriter writer = new();
                NetSerializer<T>.Write(writer, value);
                return writer.ToArray();
            }

            uint netId = NetworkTestScene.Current.NetIdOf(ship);
            using NetWriter root = new(), component = new();
            root.WriteVarUInt32(netId); root.WriteByte(byte.MaxValue);
            component.WriteVarUInt32(netId); component.WriteByte(0);

            // The networked object and its networked component can be named.
            Assert.Equal(root.ToArray(), Written(ship));
            Assert.Equal(component.ToArray(), Written(stats));

            // A child of it, a plain component and an object that isn't networked can't, and go as null.
            Assert.Equal(new byte[] { 0 }, Written(turret));
            Assert.Equal(new byte[] { 0 }, Written(plain));
            Assert.Equal(new byte[] { 0 }, Written(prop));
            Assert.Equal(new byte[] { 0 }, Written<GameObject?>(null));

            NetReader reader = new(root.WrittenSpan);
            Assert.Same(ship, NetSerializer<GameObject>.Read(ref reader));
            reader = new NetReader(component.WrittenSpan);
            Assert.Same(stats, NetSerializer<NetTestComponent>.Read(ref reader));
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ADespawnedObjectsReferencesAreForgotten()
    {
        SceneInstance scene = CreateScene();
        GameObject bullet = CreateGameObject("Bullet");
        bullet.Network.Networked = true;
        NetTestComponent component = bullet.AddComponent<NetTestComponent>();
        scene.Add(bullet); MakeCurrent(scene);
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            SyncSystem sync = Networking.Networking.System!.Sync;
            SyncMember member = NetworkMembers.Of(component).Sync(NetTestComponent.HealthSlot)!;

            // As when a reference value arrives naming an object that is not here yet.
            sync.NoteReference(component, member, (99, 0));
            Assert.Equal(1, sync.ReferenceCount);

            bullet.Dispose();
            Assert.Equal(0, sync.ReferenceCount);
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
