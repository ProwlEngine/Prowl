using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Echo;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public class PlainTestComponent : Component
{
    public int Value;
}

public partial class NetworkingTests
{
    private static bool s_buildSwapped;

    private static GameObject BuildSwappable(in NetworkSpawn spawn)
    {
        GameObject built = new("Swappable");
        if (s_buildSwapped)
        {
            built.AddComponent<OpenDoorTestComponent>();
        }
        else
        {
            built.AddComponent<NetTestComponent>();
        }

        return built;
    }

#pragma warning disable CS0618
    [Fact]
    public void ServerOnlyMeansNothingInsideANetworkedObject()
    {
        // Server only is the whole networked object's: a part of one can't stay behind,
        // since every peer builds the whole object and its networked components have to match.
        GameObject root = CreateGameObject("Root"), part = CreateGameObject("Part"), leaf = CreateGameObject("Leaf");
        part.SetParent(root);
        leaf.SetParent(part);
        root.Network.Networked = true;
        part.Network.ServerOnly = true;
        Assert.False(part.Network.IsExcluded);
        Assert.False(leaf.Network.IsExcluded);

        // On the networked object itself, or above it, it keeps the whole object on the host.
        root.Network.ServerOnly = true;
        Assert.True(root.Network.IsExcluded);
        Assert.True(leaf.Network.IsExcluded);
        root.Network.ServerOnly = false;
        GameObject holder = CreateGameObject("Holder");
        holder.Network.ServerOnly = true;
        root.SetParent(holder);
        Assert.True(root.Network.IsExcluded);
        Assert.True(part.Network.IsExcluded);
    }

    [Fact]
    public void AStoredModeStillReadsAsNetworkedOrServerOnly()
    {
        GameObject go = CreateGameObject();
        go.Network.Networked = true;
        EchoObject stored = Serializer.Serialize(go);
        EchoObject settings = stored["Network"];
        settings.Remove("Networked");
        settings.Add("Mode", new EchoObject((byte)1));

        GameObject copy = Serializer.Deserialize<GameObject>(stored)!;
        Assert.True(copy.Network.ServerOnly);
        Assert.False(copy.Network.Networked);
        copy.Dispose();
        go.Dispose();
    }
#pragma warning restore CS0618

    [Fact]
    public void ANetworkedObjectIndexesItsNetworkedComponentsInTreeOrder()
    {
        SceneInstance scene = CreateScene();
        MakeCurrent(scene);
        Networking.Networking.Host(new LocalTransport(new LocalNetwork(), "host"));
        try
        {
            GameObject root = CreateGameObject("Root");
            NetTestComponent first = root.AddComponent<NetTestComponent>();
            PlainTestComponent plain = root.AddComponent<PlainTestComponent>();
            GameObject child = CreateGameObject("Child");
            child.SetParent(root, false);
            NetCollectionTestComponent second = child.AddComponent<NetCollectionTestComponent>();
            second.Enabled = false;
            GameObject grandchild = CreateGameObject("Grandchild");
            grandchild.SetParent(child, false);
            grandchild.Enabled = false;
            NetTestComponent third = grandchild.AddComponent<NetTestComponent>();
            scene.Add(root);

            root.SpawnBuilt();
            NetworkObject entry = NetworkTestScene.Current.FindEntry(root)!;
            Assert.Equal(new Component[] { first, second, third }, entry.Components);
            Assert.Equal(0, entry.IndexOf(first));
            Assert.Equal(1, entry.IndexOf(second));
            Assert.Equal(2, entry.IndexOf(third));
            Assert.Equal(-1, entry.IndexOf(plain));
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AnObjectWithMoreThan64NetworkedComponentsCannotSpawn()
    {
        SceneInstance scene = CreateScene();
        MakeCurrent(scene);
        Networking.Networking.Host(new LocalTransport(new LocalNetwork(), "host"));
        try
        {
            GameObject root = CreateGameObject("Crowded");
            for (int i = 0; i < 65; i++)
            {
                CreateGameObject($"Part {i}").SetParent(root, false);
                root.Children[i].AddComponent<NetTestComponent>();
            }

            scene.Add(root);
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => root.SpawnBuilt());
            Assert.Contains("64", error.Message);
            Assert.False(root.Network.Active);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ATemplateSpawnCarriesItsNetworkedComponentCountAndTypeHash()
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
            GameObject crate = Networking.Networking.SpawnFactory("crate", Float3.Zero, Quaternion.Identity);
            Pump(() => alice.Has(FakePeer.Spawn), alice);
            TemplateSpawn spawn = ReadTemplateSpawn(alice.WireReceived.Last(m => m.Id == FakePeer.Spawn).Data);
            Assert.Equal(1, spawn.ComponentCount);
            Assert.Equal(NetworkComponents.Hash([crate.GetComponent<NetTestComponent>()!]), spawn.ComponentHash);
            Assert.NotEqual(NetworkComponents.Hash([]), spawn.ComponentHash);
        }
        finally
        {
            Networking.Networking.Shutdown();
            Networking.Networking.UnregisterFactory("crate");
        }
    }

    [Fact]
    public void AClientRefusesATemplateSpawnWhoseNetworkedComponentsDiffer()
    {
        s_buildSwapped = false;
        Networking.Networking.RegisterFactory("swappable", BuildSwappable);
        SceneInstance scene = CreateScene();
        MakeCurrent(scene);
        byte[] snapshot, spawn;
        Guid id;
        LocalNetwork source = new();
        Networking.Networking.Host(new LocalTransport(source, "host"));
        try
        {
            using FakePeer recorder = FakePeer.Join(source, "recorder");
            Pump(() => recorder.Has(FakePeer.Scene), recorder);
            snapshot = recorder.WireReceived.First(m => m.Id == FakePeer.Scene).Data;
            GameObject built = Networking.Networking.SpawnFactory("swappable", Float3.Zero, Quaternion.Identity);
            id = built.Identifier;
            using NetWriter written = new();
            NetworkTestScene.Current.WriteSpawn(built, -1, NetworkOrphaned.Destroy, written,
                NetworkGeneration(built), NetworkTestScene.Current.NetIdOf(built));
            spawn = written.ToArray();
            built.Dispose();
        }
        finally { Networking.Networking.Shutdown(); }

        s_buildSwapped = true;
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
            PumpFor(300, host);
            Assert.DoesNotContain(SceneInstance.Main.Objects, o => o.Identifier == id);
        }
        finally
        {
            Networking.Networking.Shutdown();
            Networking.Networking.UnregisterFactory("swappable");
            s_buildSwapped = false;
        }
    }

    [Fact]
    public void ANetworkedObjectInsideAnotherIsPartOfIt()
    {
        SceneInstance scene = CreateScene();
        GameObject outer = CreateGameObject("Outer");
        outer.Network.Networked = true;
        GameObject inner = CreateGameObject("Inner");
        inner.SetParent(outer, false);
        inner.Network.Networked = true;
        scene.Add(outer);
        MakeCurrent(scene);
        Networking.Networking.Host(new LocalTransport(new LocalNetwork(), "host"));
        try
        {
            Assert.Same(outer, inner.Network.Root);
            NetworkObject entry = NetworkTestScene.Current.FindEntry(outer)!;
            Assert.Same(outer, entry.GameObject);
            Assert.Same(entry, NetworkTestScene.Current.FindEntry(inner));
            Assert.False(NetworkTestScene.Current.Entries.Contains(inner.Identifier));
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
