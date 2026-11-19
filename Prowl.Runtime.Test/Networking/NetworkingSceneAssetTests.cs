using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void ASceneAssetIsLoadedByTheClientAndItsNetworkedObjectsArriveBySceneSpawn()
    {
        AssetBackend? previous = AssetDatabase.Backend;
        MemoryAssetBackend backend = new();
        AssetDatabase.ClearForTests();
        AssetDatabase.Backend = backend;
        try
        {
            SceneInstance authoring = new();
            authoring.Create("Arena Level");
            GameObject authoredDoor = authoring.Create("Door");
            authoredDoor.Network.Networked = true;
            authoredDoor.AddComponent<NetTestComponent>();
            GameObject authoredGone = authoring.Create("Gone");
            authoredGone.Network.Networked = true;
            authoring.Create("Secret").Network.ServerOnly = true;
            Scene arena = AssetDatabase.Get<Scene>(backend.Add(new Scene { Data = authoring.ActiveSection.CaptureTree() }, "Arena.scene"))!;
            authoring.Dispose();

            SceneInstance world = CreateScene();
            MakeCurrent(world);
            byte[] scene;
            byte[][] spawns;
            Guid doorId, goneId;
            uint doorNetId;
            LocalNetwork source = new();
            Networking.Networking.Host(new LocalTransport(source, "host"));
            try
            {
                arena.LoadAdditive(world).Wait();
                GameObject door = world.Objects.Single(o => o.Name == "Door");
                GameObject gone = world.Objects.Single(o => o.Name == "Gone");
                (doorId, goneId) = (door.Identifier, gone.Identifier);
                Assert.True(door.Network.Active);
                door.GetComponent<NetTestComponent>()!.Score = 7;
                doorNetId = NetworkTestScene.Current.NetIdOf(door);
                gone.Dispose();

                (scene, spawns) = RecordJoin(source);

                // The scene message names the asset, and the door's spawn names the object without carrying it.
                NetReader message = new(scene);
                message.ReadByte(); message.ReadUInt64();
                uint sections = message.ReadVarUInt32();
                List<SectionHeader> headers = new();
                for (uint i = 0; i < sections; i++) headers.Add(SectionHeader.Read(ref message));
                Assert.True(message.IsAtEnd);
                Assert.Equal(arena.AssetID, headers.Single(h => h.Name == "Arena").Source);
                Assert.True(ReadTemplateSpawn(spawns.Single(s => SpawnedId(s) == doorId)).Scene);
                Assert.DoesNotContain(spawns, s => SpawnedId(s) == goneId);
            }
            finally { Networking.Networking.Shutdown(); }

            // The client has a world of its own, so what is in it afterwards it loaded or was spawned.
            MakeCurrent(CreateScene());
            LocalNetwork network = new();
            using FakePeer host = FakePeer.Host(network);
            SendJoin(host, scene, spawns);
            try
            {
                Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
                Pump(() => Networking.Networking.IsReady, host);
                SceneInstance mine = SceneInstance.Main;
                Assert.Contains(mine.Objects, o => o.Name == "Arena Level");
                Assert.DoesNotContain(mine.Objects, o => o.Name == "Secret");
                Assert.DoesNotContain(mine.Objects, o => o.Identifier == goneId);

                GameObject doorHere = Find(mine, doorId);
                Assert.True(doorHere.Network.Active);
                Assert.Equal(doorNetId, NetworkTestScene.Current.NetIdOf(doorHere));
                Assert.Equal(7, doorHere.GetComponent<NetTestComponent>()!.Score);
                Assert.Equal("Arena", doorHere.Section!.Name);
            }
            finally { Networking.Networking.Shutdown(); }
        }
        finally
        {
            AssetDatabase.Backend = previous;
            AssetDatabase.ClearForTests();
        }
    }
}
