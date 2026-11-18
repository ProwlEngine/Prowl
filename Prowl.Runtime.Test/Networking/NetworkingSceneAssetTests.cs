using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Echo;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void AnAssetSectionGoesByItsAssetWithOnlyWhatTheHostChanged()
    {
        AssetBackend? previous = AssetDatabase.Backend;
        MemoryAssetBackend backend = new();
        AssetDatabase.ClearForTests();
        AssetDatabase.Backend = backend;
        SceneInstance world = CreateScene();
        world.Add(CreateGameObject("Level"));
        MakeCurrent(world);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        try
        {
            SceneInstance authoring = new();
            authoring.Create("Arena Level");
            authoring.Create("Gone");
            GameObject authoredDoor = authoring.Create("Door");
            authoredDoor.Network.Mode = NetworkMode.Object;
            authoredDoor.AddComponent<NetTestComponent>();
            Scene arena = AssetDatabase.Get<Scene>(backend.Add(new Scene { Data = authoring.ActiveSection.CaptureTree() }, "Arena.scene"))!;
            authoring.Dispose();

            arena.LoadAdditive(world).Wait();
            SceneSection section = world.Sections.Single(s => s.Name == "Arena");
            GameObject gone = world.Objects.Single(o => o.Name == "Gone");
            GameObject door = world.Objects.Single(o => o.Name == "Door");
            Guid goneId = gone.Identifier;
            gone.Dispose();
            GameObject extra = CreateGameObject("Extra");
            world.Add(extra, section);

            using FakePeer late = FakePeer.Join(network, "late");
            Pump(() => late.Has(FakePeer.SceneSnapshot), late);
            NetReader reader = new(late.Last(FakePeer.SceneSnapshot));
            EchoObject root = NetworkTestScene.Current.Decompress(SceneSnapshotMessage.Read(ref reader).Sections);
            EchoObject header = root["Sections"]!.List!.Single(h => h["Name"]?.StringValue == "Arena");
            Assert.True(header["FromSource"]!.BoolValue);
            Assert.Equal([goneId.ToString()], header["Removed"]!.List!.Select(e => e.StringValue));
            Assert.Single(header["Added"]!.List!);

            NetReader again = new(late.Last(FakePeer.SceneSnapshot));
            using SnapshotSections received = NetworkTestScene.Current.ReadSnapshot(ref again);
            SectionData built = received.Sections.Single(s => s.Name == "Arena");
            Assert.Equal(["Arena Level", "Door", "Extra"], built.Content.Objects.Select(o => o.Name).Order());
            Assert.Equal(door.Identifier, built.Content.Objects.Single(o => o.Name == "Door").Identifier);
            Assert.Equal(extra.Identifier, built.Content.Objects.Single(o => o.Name == "Extra").Identifier);

            AssetDatabase.ClearForTests();
            AssetDatabase.Backend = new MemoryAssetBackend();
            byte[] bytes = late.Last(FakePeer.SceneSnapshot);
            Assert.Throws<InvalidDataException>(() =>
            {
                NetReader missing = new(bytes);
                NetworkTestScene.Current.ReadSnapshot(ref missing).Dispose();
            });
        }
        finally
        {
            Networking.Networking.Shutdown();
            AssetDatabase.Backend = previous;
            AssetDatabase.ClearForTests();
        }
    }
}
