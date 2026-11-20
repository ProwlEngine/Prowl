using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    private static bool IsSpawnRun(ushort id) => id is FakePeer.SpawnStarted or FakePeer.Spawn or FakePeer.SpawnFinished;

    [Fact]
    public void HostSpawnsForAClientOnlyOnceItSaysItIsReady()
    {
        var (listener, state, network) = ReadinessHost();
        using FakePeer client = FakePeer.Join(network, "client");
        client.AutoReady = false;
        try
        {
            Pump(() => client.Has(FakePeer.Scene), client);
            NetPeer peer = Networking.Networking.Session!.Peers.Single(p => !p.IsLocal);
            PumpFor(30, client);
            Assert.False(Networking.Networking.IsPeerReady(peer));
            Assert.DoesNotContain($"peer ready {peer.Id}", listener.Events);
            Assert.DoesNotContain(client.Received, m => IsSpawnRun(m.Id));

            SendGeneration(client, FakePeer.Ready, SceneGenerationOf(client.Last(FakePeer.Scene)));
            Pump(() => client.Has(FakePeer.SpawnFinished), client);
            Assert.Equal(new[] { FakePeer.SpawnStarted, FakePeer.Spawn, FakePeer.SpawnFinished },
                client.Received.Select(m => m.Id).Where(IsSpawnRun));
            Assert.Equal(state.GameObject.Identifier, Assert.Single(SpawnedIds(client)));
            Assert.True(Networking.Networking.IsPeerReady(peer));
            Assert.Contains($"peer ready {peer.Id}", listener.Events);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ASectionLoadedDuringAPeersJoinRunReachesThatPeer()
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        NetworkConfiguration paced = Networking.Networking.Configuration;
        paced.JoinSpawnBytesPerTick = 1;
        Networking.Networking.Configure(paced);
        SceneInstance scene = CreateScene();
        for (int i = 0; i < 8; i++)
        {
            GameObject actor = CreateGameObject($"Actor {i}");
            actor.Network.Networked = true;
            scene.Add(actor);
        }

        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer client = FakePeer.Join(network, "client");
        try
        {
            Pump(() => client.Has(FakePeer.Spawn), client);
            Assert.False(client.Has(FakePeer.SpawnFinished));

            // The run is under way, and the host loads another section.
            GameObject crate = new("Crate");
            crate.Network.Networked = true;
            scene.AddContent(new SceneContent { Objects = [crate] }, "Arena", null);

            Pump(() => client.Has(FakePeer.SpawnFinished), client);
            PumpFor(20, client);
            Assert.Contains(crate.Identifier, SpawnedIds(client));
            Assert.Equal(9, SpawnedIds(client).Distinct().Count());
        }
        finally
        {
            Networking.Networking.Shutdown();
            Networking.Networking.Configure(previous);
        }
    }

    [Fact]
    public void AnRpcCalledDuringAPeersJoinRunReachesThatPeer()
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        NetworkConfiguration paced = Networking.Networking.Configuration;
        paced.JoinSpawnBytesPerTick = 1;
        Networking.Networking.Configure(paced);
        SceneInstance scene = CreateScene();
        List<NetTestComponent> actors = new();
        for (int i = 0; i < 6; i++)
        {
            GameObject actor = CreateGameObject($"Actor {i}");
            actor.Network.Networked = true;
            actors.Add(actor.AddComponent<NetTestComponent>());
            scene.Add(actor);
        }

        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer client = FakePeer.Join(network, "client");
        try
        {
            // The run is under way: the client has some of the objects and is not ready yet. In Mirror every spawn
            // goes at once, so there is no such window, and here a call made in it must not be lost.
            Pump(() => client.Has(FakePeer.Spawn), client);
            Assert.False(client.Has(FakePeer.SpawnFinished));
            actors[0].Shout("during the run");
            Pump(() => client.Has(FakePeer.SpawnFinished), client);
            PumpFor(20, client);
            Assert.Single(client.Received, m => m.Id == FakePeer.RpcFromHost);

            // A peer that is still loading the scene gets none.
            using FakePeer loading = FakePeer.Join(network, "loading");
            loading.AutoReady = false;
            Pump(() => loading.Has(FakePeer.Scene), loading);
            actors[0].Shout("while loading");
            PumpFor(30, client, loading);
            Assert.False(loading.Has(FakePeer.RpcFromHost));
        }
        finally
        {
            Networking.Networking.Shutdown();
            Networking.Networking.Configure(previous);
        }
    }

    [Fact]
    public void AJoinsSpawnsAreSpreadOverTicksByTheBudgetAndFinishAfterTheLast()
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        NetworkConfiguration paced = Networking.Networking.Configuration;
        paced.JoinSpawnBytesPerTick = 1;
        Networking.Networking.Configure(paced);
        SceneInstance scene = CreateScene();
        for (int i = 0; i < 3; i++)
        {
            GameObject actor = CreateGameObject($"Actor {i}");
            actor.Network.Networked = true;
            actor.AddComponent<NetTestComponent>();
            scene.Add(actor);
        }

        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer client = FakePeer.Join(network, "client");
        try
        {
            // A budget of one byte lets one spawn out each time the host looks, so the run takes several frames.
            Pump(() => client.Has(FakePeer.Spawn), client);
            NetPeer peer = Networking.Networking.Session!.Peers.Single(p => !p.IsLocal);
            Assert.InRange(SpawnedIds(client).Count, 1, 2);
            Assert.False(client.Has(FakePeer.SpawnFinished));
            Assert.False(Networking.Networking.IsPeerReady(peer));

            Pump(() => client.Has(FakePeer.SpawnFinished), client);
            Assert.Equal(3, SpawnedIds(client).Distinct().Count());
            Assert.Equal(FakePeer.SpawnFinished, client.Received.Last(m => IsSpawnRun(m.Id)).Id);
            Assert.True(Networking.Networking.IsPeerReady(peer));
        }
        finally
        {
            Networking.Networking.Shutdown();
            Networking.Networking.Configure(previous);
        }
    }

    [Fact]
    public void AJoiningClientRunsSpawnCallbacksOnlyOnceTheHostFinishesSpawning()
    {
        SceneInstance scene = CreateScene();
        GameObject root = CreateGameObject("Actor");
        root.Network.Networked = true;
        root.AddComponent<NetworkLifecycleListener>();
        scene.Add(root);
        MakeCurrent(scene);
        Guid id = root.Identifier;
        byte[] snapshot;
        byte[][] spawns;
        LocalNetwork source = new();
        Networking.Networking.Host(new LocalTransport(source, "host"));
        try
        {
            (snapshot, spawns) = RecordJoin(source);
        }
        finally { Networking.Networking.Shutdown(); }

        MakeCurrent(CreateScene());
        ulong generation = SceneGenerationOf(snapshot);
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.AutoReady = false;
        host.Session.PeerJoined += peer => host.Session.SendRaw(peer, FakePeer.Scene, snapshot);
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => host.Has(FakePeer.Ready), host);
            NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);
            host.Send(FakePeer.SpawnStarted, w => w.WriteUInt64(generation), client);
            host.Session.SendRaw(client, FakePeer.Spawn, Assert.Single(spawns));
            host.Session.Flush();
            Pump(() => SceneInstance.Main.Objects.Any(o => o.Identifier == id), host);
            PumpFor(30, host);

            // The object is in the world with its state, but nothing was told about it yet.
            GameObject copy = Find(SceneInstance.Main, id);
            NetworkLifecycleListener listener = copy.GetComponent<NetworkLifecycleListener>()!;
            Assert.True(copy.Network.Active);
            Assert.Empty(listener.Events);
            Assert.False(Networking.Networking.IsReady);
            Assert.Equal(NetworkJoinState.Synchronizing, Networking.Networking.JoinState);

            host.Send(FakePeer.SpawnFinished, w => w.WriteUInt64(generation), client);
            Pump(() => Networking.Networking.IsReady, host);
            Assert.Equal(new[] { "spawn" }, listener.Events);
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
