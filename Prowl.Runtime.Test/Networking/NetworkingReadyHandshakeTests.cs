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
    public void AJoiningClientRunsSpawnCallbacksOnlyOnceTheHostFinishesSpawning()
    {
        Scene scene = CreateScene();
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
            Pump(() => Scene.Main.Objects.Any(o => o.Identifier == id), host);
            PumpFor(30, host);

            // The object is in the world with its state, but nothing was told about it yet.
            GameObject copy = Find(Scene.Main, id);
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
