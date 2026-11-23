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
        NetReader payload = new(message.Payload);
        return TemplateSpawn.Read(ref payload);
    }

    [Fact]
    public void APrefabSpawnSendsItsGuidAndIdentifiersInsteadOfTheTree()
    {
        PrefabAsset prefab = RegisterCratePrefab();
        Scene scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.Scene), alice);
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
        Scene scene = CreateScene();
        MakeCurrent(scene);
        byte[] snapshot, spawn;
        List<Guid> ids;
        LocalNetwork source = new();
        Networking.Networking.Host(new LocalTransport(source, "host"));
        using (FakePeer recorder = FakePeer.Join(source, "recorder"))
        {
            try
            {
                Pump(() => recorder.Has(FakePeer.Scene), recorder);
                GameObject crate = Networking.Networking.SpawnPrefab(prefab, new Float3(4, 5, 6), Quaternion.Identity);
                ids = TreeIdentifiers(crate);
                crate.GetComponent<NetTestComponent>()!.Health = 9;
                using NetWriter written = new();
                NetworkTestScene.Current.WriteSpawn(crate, -1, NetworkOrphaned.Destroy, written,
                    NetworkGeneration(crate), NetworkTestScene.Current.NetIdOf(crate));
                spawn = written.ToArray();
                snapshot = recorder.WireReceived.First(m => m.Id == FakePeer.Scene).Data;
                crate.Dispose();
            }
            finally { Networking.Networking.Shutdown(); }
        }

        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer =>
        {
            host.Send(FakePeer.Scene, w => w.WriteBytes(snapshot), peer);
            host.Send(FakePeer.Spawn, w => w.WriteBytes(spawn), peer);
        };
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Scene.Main.Objects.Any(o => o.Identifier == ids[0]), host);
            GameObject copy = Find(Scene.Main, ids[0]);

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
    public void OnlyWhatEveryPeerCanBuildSpawns()
    {
        PrefabAsset prefab = RegisterCratePrefab();
        Scene scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.IsReady, alice);
            GameObject handBuilt = CreateGameObject("Hand built");
            scene.Add(handBuilt);
            Assert.Throws<InvalidOperationException>(() => Networking.Networking.Spawn(handBuilt));
            Assert.False(handBuilt.Network.Active);

            // An instance of a prefab made outside networking is one clients build from the prefab.
            GameObject instance = GameObject.Instantiate(prefab, scene, Float3.Zero, Quaternion.Identity)!;
            Networking.Networking.Spawn(instance);
            Pump(() => alice.Has(FakePeer.Spawn), alice);
            TemplateSpawn spawn = ReadPrefabSpawn(alice.WireReceived.Last(m => m.Id == FakePeer.Spawn).Data);
            Assert.Equal(prefab.AssetID, spawn.Prefab);
            Assert.Equal(TreeIdentifiers(instance), spawn.Identifiers);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void APrefabInstanceIsSpawnedAsItsPrefabBuildsItOrRefusedOnTheHost()
    {
        PrefabAsset prefab = RegisterCratePrefab();
        Scene scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.IsReady, alice);

            // A part of an instance is not the prefab: clients would build the whole crate for it.
            GameObject whole = GameObject.Instantiate(prefab, scene, Float3.Zero, Quaternion.Identity)!;
            Assert.Throws<InvalidOperationException>(() => Networking.Networking.Spawn(whole.Children[0]));
            Assert.False(whole.Children[0].Network.Active);

            // One that gained a component and a child since it was built spawns as the prefab builds it.
            GameObject grown = GameObject.Instantiate(prefab, scene, Float3.Zero, Quaternion.Identity)!;
            List<Guid> asBuilt = TreeIdentifiers(grown);
            SyncPairTestComponent added = grown.AddComponent<SyncPairTestComponent>();
            CreateGameObject("Extra").SetParent(grown);
            Networking.Networking.Spawn(grown);
            Pump(() => alice.Has(FakePeer.Spawn), alice);
            TemplateSpawn spawn = ReadPrefabSpawn(alice.WireReceived.Last(m => m.Id == FakePeer.Spawn).Data);
            Assert.Equal(asBuilt, spawn.Identifiers);
            Assert.DoesNotContain(added, Networking.Networking.System!.FindEntry(grown)!.Components);
            Assert.Equal(2, spawn.ComponentCount);

            // One that lost what its prefab builds can't be built by clients, and the host is told so.
            GameObject cut = GameObject.Instantiate(prefab, scene, Float3.Zero, Quaternion.Identity)!;
            cut.Children[0].Dispose();
            Assert.Throws<InvalidOperationException>(() => Networking.Networking.Spawn(cut));
            Assert.Equal(1, alice.WireReceived.Count(m => m.Id == FakePeer.Spawn));
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AChildPutAheadOfThePrefabsOwnIsNotTakenForOneOfThem()
    {
        PrefabAsset prefab = RegisterCratePrefab();
        Scene scene = CreateScene();
        MakeCurrent(scene);
        Networking.Networking.Host(new LocalTransport(new(), "host"));
        try
        {
            GameObject crate = GameObject.Instantiate(prefab, scene, Float3.Zero, Quaternion.Identity)!;
            GameObject lid = crate.Children[0];

            // It has what the lid has, and sits where the prefab builds the lid.
            GameObject decoy = CreateGameObject("Decoy");
            NetTestComponent decoyState = decoy.AddComponent<NetTestComponent>();
            decoy.SetParent(crate);
            decoy.SetSiblingIndex(0);
            Assert.Same(decoy, crate.Children[0]);

            Networking.Networking.Spawn(crate);
            NetworkObject entry = Networking.Networking.System!.FindEntry(crate)!;
            Assert.Contains(lid.GetComponent<NetTestComponent>()!, entry.Components);
            Assert.DoesNotContain(decoyState, entry.Components);
            Assert.Contains(lid.Identifier, entry.Template.Identifiers);
            Assert.DoesNotContain(decoy.Identifier, entry.Template.Identifiers);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ALateJoinerGetsPrefabInstancesAsPrefabSpawns()
    {
        PrefabAsset prefab = RegisterCratePrefab();
        Scene scene = CreateScene();
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

            // One that gained a component since it was built still goes as the prefab's own tree.
            Assert.DoesNotContain(changed.GetComponent<SyncPairTestComponent>()!.Identifier,
                ReadPrefabSpawn(spawns.Single(d => SpawnedId(d) == changed.Identifier)).Identifiers);

            TemplateSpawn spawn = ReadPrefabSpawn(spawns.Single(d => SpawnedId(d) == crate.Identifier));
            Assert.Equal(TreeIdentifiers(crate), spawn.Identifiers);
            Assert.Equal(new Float3(1, 2, 3), spawn.Position);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void OnlyTheHostCanSpawnAPrefab()
    {
        PrefabAsset prefab = RegisterCratePrefab();
        Scene scene = CreateScene();
        MakeCurrent(scene);
        using NetWriter snapshot = new();
        NetworkTestScene.Current.WriteScene(scene, snapshot);
        byte[] bytes = snapshot.ToArray();
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer => host.Send(FakePeer.Scene, w => w.WriteBytes(bytes), peer);
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady, host);
            int before = Scene.Main.Objects.Count();
            Assert.Throws<InvalidOperationException>(() => Networking.Networking.SpawnPrefab(prefab, Float3.Zero, Quaternion.Identity));
            Assert.Equal(before, Scene.Main.Objects.Count());
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
