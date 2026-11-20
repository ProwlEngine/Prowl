using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Echo;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    private static PrefabAsset RegisterCratePrefab()
    {
        GameObject crate = new("Crate");
        crate.AddComponent<NetTestComponent>();
        crate.AddComponent<NetworkTransform>().SendInterval = 2;
        GameObject lid = new("Lid");
        lid.AddComponent<NetTestComponent>();
        lid.SetParent(crate, false);
        PrefabAsset prefab = new() { GameObjectData = Serializer.Serialize(typeof(object), crate) };
        crate.Dispose();
        AssetDatabase.Register(prefab, Guid.NewGuid(), "Crate.prefab");
        return prefab;
    }

    private static List<Guid> TreeIdentifiers(GameObject go)
    {
        List<Guid> ids = [go.Identifier, .. go.GetComponents<Component>().Select(c => c.Identifier)];
        foreach (GameObject child in go.Children)
            ids.AddRange(TreeIdentifiers(child));
        return ids;
    }

    private static TemplateSpawn ReadPrefabSpawn(byte[] spawn)
    {
        NetReader reader = new(spawn);
        SpawnMessage message = SpawnMessage.Read(ref reader);
        Assert.True(message.Template);
        NetReader payload = new(message.Snapshot);
        return TemplateSpawn.Read(ref payload);
    }

    [Fact]
    public void APrefabSpawnSendsItsGuidAndIdentifiersInsteadOfTheTree()
    {
        PrefabAsset prefab = RegisterCratePrefab();
        SceneInstance scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.SceneSnapshot), alice);
            GameObject crate = Networking.Networking.SpawnPrefab(prefab, new Float3(1, 2, 3), Quaternion.Identity);

            Assert.True(crate.Network.Active);
            Assert.Equal(prefab.AssetID, crate.PrefabAssetId);
            Assert.Same(scene, crate.Scene);
            Assert.Equal(new Float3(1, 2, 3), crate.Transform.LocalPosition);

            Pump(() => alice.Has(FakePeer.Spawn), alice);
            TemplateSpawn spawn = ReadPrefabSpawn(alice.WireReceived.Last(m => m.Id == FakePeer.Spawn).Data);
            Assert.Equal(prefab.AssetID, spawn.Prefab);
            Assert.Equal(TreeIdentifiers(crate), spawn.Identifiers);
            Assert.Equal(new Float3(1, 2, 3), spawn.Position);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AClientBuildsAPrefabSpawnWithTheHostsIdentifiersAndValues()
    {
        PrefabAsset prefab = RegisterCratePrefab();
        SceneInstance scene = CreateScene();
        MakeCurrent(scene);
        byte[] snapshot, spawn;
        List<Guid> ids;
        LocalNetwork source = new();
        Networking.Networking.Host(new LocalTransport(source, "host"));
        using (FakePeer recorder = FakePeer.Join(source, "recorder"))
        {
            try
            {
                Pump(() => recorder.Has(FakePeer.SceneSnapshot), recorder);
                GameObject crate = Networking.Networking.SpawnPrefab(prefab, new Float3(4, 5, 6), Quaternion.Identity);
                ids = TreeIdentifiers(crate);
                crate.GetComponent<NetTestComponent>()!.Health = 9;
                using NetWriter written = new();
                NetworkTestScene.Current.WriteSpawn(crate, -1, NetworkOrphaned.Destroy, written,
                    NetworkGeneration(crate), NetworkTestScene.Current.NetIdOf(crate));
                spawn = written.ToArray();
                snapshot = recorder.WireReceived.First(m => m.Id == FakePeer.SceneSnapshot).Data;
                crate.Dispose();
            }
            finally { Networking.Networking.Shutdown(); }
        }

        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer =>
        {
            host.Send(FakePeer.SceneSnapshot, w => w.WriteBytes(snapshot), peer);
            host.Send(FakePeer.Spawn, w => w.WriteBytes(spawn), peer);
        };
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => SceneInstance.Main.Objects.Any(o => o.Identifier == ids[0]), host);
            GameObject copy = Find(SceneInstance.Main, ids[0]);

            Assert.Equal(ids, TreeIdentifiers(copy));
            Assert.Equal(prefab.AssetID, copy.PrefabAssetId);
            Assert.Equal(new Float3(4, 5, 6), copy.Transform.LocalPosition);
            Assert.Equal(9, copy.GetComponent<NetTestComponent>()!.Health);
            Assert.Equal(2, copy.GetComponent<NetworkTransform>()!.SendInterval);
            Assert.True(copy.Network.Active);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void APrefabInstanceWhoseTreeChangedIsSentWhole()
    {
        PrefabAsset prefab = RegisterCratePrefab();
        SceneInstance scene = CreateScene();
        MakeCurrent(scene);
        Networking.Networking.Host(new LocalTransport(new(), "host"));
        try
        {
            GameObject crate = Networking.Networking.SpawnPrefab(prefab, Float3.Zero, Quaternion.Identity);
            Assert.True(SpawnIsPrefab(crate));

            crate.AddComponent<SyncPairTestComponent>();
            Assert.False(SpawnIsPrefab(crate));
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ALateJoinerGetsUnchangedPrefabInstancesAsPrefabSpawnsAfterTheSnapshot()
    {
        PrefabAsset prefab = RegisterCratePrefab();
        SceneInstance scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        try
        {
            GameObject crate = Networking.Networking.SpawnPrefab(prefab, new Float3(1, 2, 3), Quaternion.Identity);
            GameObject changed = Networking.Networking.SpawnPrefab(prefab, Float3.Zero, Quaternion.Identity);
            changed.AddComponent<SyncPairTestComponent>();

            using FakePeer late = FakePeer.Join(network, "late");
            Pump(() => late.WireReceived.Count(m => m.Id == FakePeer.Spawn) == 2, late);
            List<byte[]> spawns = [.. late.WireReceived.Where(m => m.Id == FakePeer.Spawn).Select(m => m.Data)];

            // One whose tree changed since it was built goes whole instead.
            NetReader whole = new(spawns.Single(d => SpawnedId(d) == changed.Identifier));
            Assert.False(SpawnMessage.Read(ref whole).Template);

            TemplateSpawn spawn = ReadPrefabSpawn(spawns.Single(d => SpawnedId(d) == crate.Identifier));
            Assert.Equal(TreeIdentifiers(crate), spawn.Identifiers);
            Assert.Equal(new Float3(1, 2, 3), spawn.Position);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    private static bool SpawnIsPrefab(GameObject go)
    {
        using NetWriter output = new();
        NetworkTestScene.Current.WriteSpawn(go, -1, NetworkOrphaned.Destroy, output, 1, 1);
        NetReader reader = new(output.WrittenSpan);
        return SpawnMessage.Read(ref reader).Template;
    }

    [Fact]
    public void OnlyTheHostCanSpawnAPrefab()
    {
        PrefabAsset prefab = RegisterCratePrefab();
        SceneInstance scene = CreateScene();
        MakeCurrent(scene);
        using NetWriter snapshot = new();
        NetworkTestScene.Current.WriteScene(scene, snapshot);
        byte[] bytes = snapshot.ToArray();
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer => host.Send(FakePeer.SceneSnapshot, w => w.WriteBytes(bytes), peer);
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady, host);
            int before = SceneInstance.Main.Objects.Count();
            Assert.Throws<InvalidOperationException>(() => Networking.Networking.SpawnPrefab(prefab, Float3.Zero, Quaternion.Identity));
            Assert.Equal(before, SceneInstance.Main.Objects.Count());
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
