using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    private static GameObject BuildCrate(in NetworkSpawn spawn)
    {
        GameObject crate = new("Crate");
        crate.AddComponent<NetTestComponent>();
        GameObject lid = new("Lid");
        lid.SetParent(crate, false);
        crate.Transform.Position = spawn.Position;
        crate.Transform.Rotation = spawn.Rotation;
        return crate;
    }

    private static TemplateSpawn ReadTemplateSpawn(byte[] spawn)
    {
        NetReader reader = new(spawn);
        SpawnMessage message = SpawnMessage.Read(ref reader);
        Assert.True(message.Template);
        NetReader payload = new(message.Payload);
        return TemplateSpawn.Read(ref payload);
    }

    [Fact]
    public void AFactorySpawnSendsItsNameDataAndIdentifiers()
    {
        Networking.Networking.RegisterFactory("crate", BuildCrate);
        SceneInstance scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.Scene), alice);
            GameObject crate = Networking.Networking.SpawnFactory("crate", new Float3(1, 2, 3), Quaternion.Identity, [1, 2, 3]);
            Assert.True(crate.Network.Active);
            Assert.Same(scene, crate.Scene);
            Assert.Equal(new Float3(1, 2, 3), crate.Transform.Position);

            Pump(() => alice.Has(FakePeer.Spawn), alice);
            TemplateSpawn spawn = ReadTemplateSpawn(alice.WireReceived.Last(m => m.Id == FakePeer.Spawn).Data);
            Assert.Equal("crate", spawn.Factory);
            Assert.Equal(new byte[] { 1, 2, 3 }, spawn.Data);
            Assert.Equal(TreeIdentifiers(crate), spawn.Identifiers);
        }
        finally
        {
            Networking.Networking.Shutdown();
            Networking.Networking.UnregisterFactory("crate");
        }
    }

    [Fact]
    public void AClientBuildsAFactorySpawnAndHandsItToTheDestroyDelegateOnDespawn()
    {
        List<GameObject> returned = new();
        Networking.Networking.RegisterFactory("crate", BuildCrate, go => returned.Add(go));
        SceneInstance scene = CreateScene();
        MakeCurrent(scene);
        byte[] snapshot, spawn;
        List<Guid> ids;
        LocalNetwork source = new();
        Networking.Networking.Host(new LocalTransport(source, "host"));
        try
        {
            using FakePeer recorder = FakePeer.Join(source, "recorder");
            Pump(() => recorder.Has(FakePeer.Scene), recorder);
            snapshot = recorder.WireReceived.First(m => m.Id == FakePeer.Scene).Data;
            GameObject crate = Networking.Networking.SpawnFactory("crate", new Float3(4, 5, 6), Quaternion.Identity);
            crate.GetComponent<NetTestComponent>()!.Health = 9;
            ids = TreeIdentifiers(crate);
            using NetWriter written = new();
            NetworkTestScene.Current.WriteSpawn(crate, -1, NetworkOrphaned.Destroy, written,
                NetworkGeneration(crate), NetworkTestScene.Current.NetIdOf(crate));
            spawn = written.ToArray();
            crate.Dispose();
        }
        finally { Networking.Networking.Shutdown(); }

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
            Pump(() => Networking.Networking.IsReady && SceneInstance.Main.Objects.Any(o => o.Identifier == ids[0]), host);
            GameObject copy = Find(SceneInstance.Main, ids[0]);
            Assert.Equal(ids, TreeIdentifiers(copy));
            Assert.Equal(9, copy.GetComponent<NetTestComponent>()!.Health);

            using NetWriter despawn = new();
            new DespawnMessage(copy.Identifier).Write(despawn);
            byte[] bytes = despawn.ToArray();
            host.Send(FakePeer.Despawn, w => w.WriteBytes(bytes), host.Session.Peers.Single(p => !p.IsLocal));
            Pump(() => returned.Count == 1, host);
            Assert.Same(copy, returned[0]);
            Assert.False(copy.IsDisposed);
            Assert.False(copy.Network.Active);
        }
        finally
        {
            Networking.Networking.Shutdown();
            Networking.Networking.UnregisterFactory("crate");
        }
    }

    [Fact]
    public void AnUnknownFactoryOrOneReturningAnObjectInASceneIsRefused()
    {
        SceneInstance scene = CreateScene();
        MakeCurrent(scene);
        Networking.Networking.RegisterFactory("placed", (in NetworkSpawn spawn) =>
        {
            GameObject go = new("Placed");
            SceneInstance.Main.Add(go);
            return go;
        });
        Networking.Networking.Host(new LocalTransport(new(), "host"));
        try
        {
            Assert.Throws<ArgumentException>(() => Networking.Networking.SpawnFactory("missing", Float3.Zero, Quaternion.Identity));
            Assert.Throws<InvalidOperationException>(() => Networking.Networking.SpawnFactory("placed", Float3.Zero, Quaternion.Identity));
        }
        finally
        {
            Networking.Networking.Shutdown();
            Networking.Networking.UnregisterFactory("placed");
        }
    }

}
