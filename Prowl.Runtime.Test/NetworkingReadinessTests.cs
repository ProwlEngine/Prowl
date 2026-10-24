using Prowl.Echo;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Warp;
using Prowl.Warp.Transports.Local;
using Xunit;

namespace Prowl.Runtime.Test;

public class NetworkReadinessListener : Component, INetworkListener
{
    [SerializeIgnore] public List<string> Events = new();
    [SerializeIgnore] public Action? ReadyAction;
    [SerializeIgnore] public Action<NetPeer>? PeerReadyAction;
    [SerializeIgnore] public bool WasActiveAtReady;
    [SerializeIgnore] public int HealthAtReady;
    public void OnNetworkLoading() => Events.Add("loading");
    public void OnNetworkReady()
    {
        WasActiveAtReady = GameObject.Scene!.IsActive;
        HealthAtReady = GameObject.GetComponent<NetTestComponent>()?.Health ?? -1;
        Events.Add("ready");
        ReadyAction?.Invoke();
    }
    public void OnPeerConnected(NetPeer peer) => Events.Add($"connected {peer.Id}");
    public void OnPeerReady(NetPeer peer)
    {
        Events.Add($"peer ready {peer.Id}");
        PeerReadyAction?.Invoke(peer);
    }
    public void OnPeerDisconnected(NetPeer peer, SessionEndReason reason) => Events.Add($"disconnected {peer.Id}");
}

public partial class NetworkingTests
{
    private (NetworkReadinessListener Listener, NetTestComponent State, LocalNetwork Network) ReadinessHost()
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject("Ready actor");
        root.Network.Mode = NetworkMode.Object;
        NetworkReadinessListener listener = root.AddComponent<NetworkReadinessListener>();
        NetTestComponent state = root.AddComponent<NetTestComponent>();
        scene.Add(root);
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        return (listener, state, network);
    }

    private static void SendGeneration(FakePeer peer, ushort kind, ulong generation) =>
        peer.Send(kind, w => w.WriteUInt64(generation));

    [Theory]
    [InlineData(NetworkExecution.Inline)]
    [InlineData(NetworkExecution.DedicatedThread)]
    public void AdmissionWaitsForActivationAndCatchUpBeforePeerReady(NetworkExecution execution)
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        Networking.Networking.Configure(new() { Execution = execution });
        var (listener, state, network) = ReadinessHost();
        using FakePeer client = FakePeer.Join(network, "client");
        client.AutoReady = false;
        try
        {
            Pump(() => client.Has(FakePeer.SceneSnapshot), client);
            NetPeer peer = Networking.Networking.Session!.Peers.Single(p => !p.IsLocal);
            Assert.True(Networking.Networking.IsReady);
            Assert.False(Networking.Networking.IsPeerReady(peer));
            Assert.Equal(new[] { "loading", "ready", $"connected {peer.Id}" }, listener.Events);
            NetReader snapshot = new(client.Last(FakePeer.SceneSnapshot));
            snapshot.ReadByte();
            ulong generation = snapshot.ReadUInt64();

            // A premature ready acknowledgement and gameplay input cannot activate the peer.
            SendGeneration(client, FakePeer.SceneReady, generation);
            client.Send(FakePeer.RpcToHost, w =>
            {
                w.WriteGuid(state.Identifier); w.WriteUInt32(NetTestComponent.AskHostId); w.WriteInt32(17);
            });
            state.Shout("while loading");
            PumpFor(20, client);
            Assert.False(Networking.Networking.IsPeerReady(peer));
            Assert.Empty(state.Asked);
            Assert.False(client.Has(FakePeer.RpcFromHost));

            state.Health = 42;
            state.GameObject.Transform.LocalPosition = new Float3(7, 8, 9);
            SendGeneration(client, FakePeer.SceneLoaded, generation);
            Pump(() => client.Has(FakePeer.SceneCatchUp), client);
            Assert.False(Networking.Networking.IsPeerReady(peer));
            NetReader catchUp = new(client.Last(FakePeer.SceneCatchUp));
            Assert.Equal(generation, catchUp.ReadUInt64());
            uint count = catchUp.ReadVarUInt32();
            int? health = null;
            for (uint i = 0; i < count; i++)
            {
                catchUp.ReadGuid(); uint slot = catchUp.ReadVarUInt32(); catchUp.ReadVarUInt32();
                NetReader value = new(catchUp.ReadBytesWithLength());
                if (slot == NetTestComponent.HealthSlot) health = NetSerializer<int>.Read(ref value);
            }
            Assert.Equal(42, health);
            Assert.Equal(1u, catchUp.ReadVarUInt32());
            Assert.Equal(Networking.Networking.NetIdOf(state.GameObject), catchUp.ReadVarUInt32());
            catchUp.ReadVarUInt32(); catchUp.ReadVarUInt32(); // Ownership generation, tick.
            Assert.Equal(new Float3(7, 8, 9), NetSerializers.ReadFloat3(ref catchUp));

            listener.PeerReadyAction = _ => state.Shout("welcome");
            SendGeneration(client, FakePeer.SceneReady, generation);
            Pump(() => client.Has(FakePeer.RpcFromHost), client);
            Assert.True(Networking.Networking.IsPeerReady(peer));
            Assert.Equal(1, listener.Events.Count(e => e == $"peer ready {peer.Id}"));
            Assert.True(client.Received.FindIndex(m => m.Id == FakePeer.SceneActivate) <
                client.Received.FindIndex(m => m.Id == FakePeer.RpcFromHost));
            SendGeneration(client, FakePeer.SceneReady, generation);
            PumpFor(20, client);
            Assert.Equal(1, listener.Events.Count(e => e == $"peer ready {peer.Id}"));
        }
        finally { Networking.Networking.Shutdown(); Networking.Networking.Configure(previous); }
    }

    [Fact]
    public void SceneChangeInvalidatesReadyPeersAndOldAcknowledgements()
    {
        var (_, _, network) = ReadinessHost();
        using FakePeer client = FakePeer.Join(network, "client");
        try
        {
            Pump(() => client.Has(FakePeer.SceneSnapshot), client);
            ulong oldGeneration = client.SceneGeneration;
            NetPeer peer = Networking.Networking.Session!.Peers.Single(p => !p.IsLocal);
            Assert.True(Networking.Networking.IsPeerReady(peer));
            client.AutoReady = false;
            Scene next = CreateScene();
            GameObject root = CreateGameObject();
            NetworkReadinessListener listener = root.AddComponent<NetworkReadinessListener>();
            next.Add(root); MakeCurrent(next);
            Pump(() => client.Received.Count(m => m.Id == FakePeer.SceneSnapshot) == 2, client);
            Assert.False(Networking.Networking.IsPeerReady(peer));
            SendGeneration(client, FakePeer.SceneLoaded, oldGeneration);
            SendGeneration(client, FakePeer.SceneReady, oldGeneration);
            PumpFor(20, client);
            Assert.False(Networking.Networking.IsPeerReady(peer));
            Assert.DoesNotContain($"peer ready {peer.Id}", listener.Events);
            NetReader snapshot = new(client.Last(FakePeer.SceneSnapshot));
            snapshot.ReadByte(); ulong generation = snapshot.ReadUInt64();
            Assert.True(generation > oldGeneration);
            SendGeneration(client, FakePeer.SceneLoaded, generation);
            Pump(() => client.Received.Count(m => m.Id == FakePeer.SceneCatchUp) == 2, client);
            SendGeneration(client, FakePeer.SceneReady, generation);
            Pump(() => Networking.Networking.IsPeerReady(peer), client);
            Assert.Contains($"peer ready {peer.Id}", listener.Events);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Theory]
    [InlineData(NetworkExecution.Inline)]
    [InlineData(NetworkExecution.DedicatedThread)]
    public void ClientReadyRunsAfterSceneActivationAndCatchUpOnlyOnce(NetworkExecution execution)
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        Networking.Networking.Configure(new() { Execution = execution });
        var (_, state, _) = ReadinessHost();
        Guid objectId = state.GameObject.Identifier;
        using NetWriter snapshot = new();
        Networking.Networking.WriteSnapshot(Scene.Current, snapshot);
        Networking.Networking.Shutdown();
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.AutoReady = false;
        host.Session.PeerJoined += peer => host.Send(FakePeer.SceneSnapshot, w => w.WriteBytes(snapshot.WrittenSpan), peer);
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => host.Has(FakePeer.SceneLoaded), host);
            Assert.Equal(NetworkJoinState.Synchronizing, Networking.Networking.JoinState);
            Assert.False(Networking.Networking.IsReady);
            GameObject root = Find(Scene.Current, objectId);
            NetworkReadinessListener listener = root.GetComponent<NetworkReadinessListener>()!;
            Assert.Equal(new[] { "loading" }, listener.Events);
            NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);
            NetReader loaded = new(host.Last(FakePeer.SceneLoaded));
            ulong generation = loaded.ReadUInt64();
            host.Send(FakePeer.SceneActivate, w => w.WriteUInt64(generation), client); // Too soon.
            PumpFor(20, host);
            Assert.False(Networking.Networking.IsReady);
            host.Send(FakePeer.SceneCatchUp, w =>
            {
                w.WriteUInt64(generation);
                w.WriteVarUInt32(1);
                w.WriteGuid(root.GetComponent<NetTestComponent>()!.Identifier);
                w.WriteVarUInt32(NetTestComponent.HealthSlot);
                w.WriteVarUInt32(Networking.Networking.GenerationOf(root));
                using NetWriter value = new(); NetSerializer<int>.Write(value, 37);
                w.WriteBytesWithLength(value.WrittenSpan);
                w.WriteVarUInt32(0);
            }, client);
            Pump(() => host.Has(FakePeer.SceneReady), host);
            Assert.False(Networking.Networking.IsReady);
            host.Send(FakePeer.SceneActivate, w => w.WriteUInt64(generation), client);
            Pump(() => Networking.Networking.IsReady, host);
            Assert.Equal(new[] { "loading", "ready" }, listener.Events);
            Assert.True(listener.WasActiveAtReady);
            Assert.Equal(37, listener.HealthAtReady);
            host.Send(FakePeer.SceneActivate, w => w.WriteUInt64(generation), client);
            PumpFor(20, host);
            Assert.Equal(1, listener.Events.Count(e => e == "ready"));
        }
        finally { Networking.Networking.Shutdown(); Networking.Networking.Configure(previous); }
    }

    [Fact]
    public void DisconnectDuringLoadingNeverRaisesPeerReady()
    {
        var (listener, _, network) = ReadinessHost();
        using FakePeer client = FakePeer.Join(network, "client");
        client.AutoReady = false;
        try
        {
            Pump(() => client.Has(FakePeer.SceneSnapshot), client);
            NetPeer peer = Networking.Networking.Session!.Peers.Single(p => !p.IsLocal);
            client.Session.Shutdown();
            Pump(() => listener.Events.Contains($"disconnected {peer.Id}"), client);
            Assert.False(Networking.Networking.IsPeerReady(peer));
            Assert.DoesNotContain($"peer ready {peer.Id}", listener.Events);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void StalledPeerTimesOutWithAStageSpecificReason()
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        var (listener, _, network) = ReadinessHost();
        Networking.Networking.Configure(new() { SceneLoadTimeoutSeconds = 0.1f });
        using FakePeer client = FakePeer.Join(network, "client");
        client.AutoReady = false;
        string? failure = null;
        client.Session.Ended += (_, message) => failure = message;
        try
        {
            Pump(() => client.Session.State == SessionState.Ended, client);
            Assert.Contains("Scene readiness timed out during Loading", failure);
            Assert.DoesNotContain(listener.Events, e => e.StartsWith("peer ready"));
            Assert.True(Networking.Networking.IsActive);
        }
        finally { Networking.Networking.Shutdown(); Networking.Networking.Configure(previous); }
    }

    [Fact]
    public void LocalReadyListenerCanEndTheSessionWithoutNotifyingLaterListeners()
    {
        Scene scene = CreateScene();
        GameObject first = CreateGameObject(), second = CreateGameObject();
        NetworkReadinessListener a = first.AddComponent<NetworkReadinessListener>();
        NetworkReadinessListener b = second.AddComponent<NetworkReadinessListener>();
        a.ReadyAction = () => Networking.Networking.Shutdown();
        scene.Add(first); scene.Add(second); MakeCurrent(scene);
        try
        {
            Networking.Networking.Host(new LocalTransport(new(), "host"));
            Assert.Null(Networking.Networking.Session);
            Assert.Equal(NetworkJoinState.Disconnected, Networking.Networking.JoinState);
            Assert.DoesNotContain("ready", b.Events);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void DisconnectCancelsTheSceneQueuedByTheJoin()
    {
        ReadinessHost();
        Scene original = Scene.Current;
        using NetWriter snapshot = new();
        Networking.Networking.WriteSnapshot(original, snapshot);
        Networking.Networking.Shutdown();
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.AutoReady = false;
        host.Session.PeerJoined += peer => host.Send(FakePeer.SceneSnapshot, w => w.WriteBytes(snapshot.WrittenSpan), peer);
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            // Receive the snapshot without running the end-of-frame scene activation.
            for (int i = 0; i < 1000 && !Scene.IsLoadPending; i++)
            {
                Networking.Networking.Receive(); Networking.Networking.Flush(); host.Session.Update();
                Thread.Sleep(1);
            }
            Assert.True(Scene.IsLoadPending);
            Assert.Equal(NetworkJoinState.Loading, Networking.Networking.JoinState);
            Assert.Same(original, Scene.Current);
            Networking.Networking.Shutdown();
            Assert.False(Scene.IsLoadPending);
            Scene.ProcessPendingLoad();
            Assert.Same(original, Scene.Current);
            Assert.Equal(NetworkJoinState.Disconnected, Networking.Networking.JoinState);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Theory]
    [InlineData(NetworkExecution.Inline, false)]
    [InlineData(NetworkExecution.Inline, true)]
    [InlineData(NetworkExecution.DedicatedThread, false)]
    [InlineData(NetworkExecution.DedicatedThread, true)]
    public void LocalSceneActivationBeforeFirstSnapshotDoesNotEndJoin(NetworkExecution execution, bool admitted)
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        Networking.Networking.Configure(new() { Execution = execution });
        ReadinessHost();
        using NetWriter snapshot = new();
        Networking.Networking.WriteSnapshot(Scene.Current, snapshot);
        Networking.Networking.Shutdown();
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.AutoReady = false;
        Scene local = CreateScene();
        Scene.Load(local); // The demo queues its initial scene before its first update starts joining.
        try
        {
            NetSession session = Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            if (admitted)
            {
                // Admit the client while leaving its local scene activation queued.
                for (int i = 0; i < 1000 && !Networking.Networking.IsActive; i++)
                {
                    Networking.Networking.Receive(); Networking.Networking.Flush(); host.Session.Update();
                    Thread.Sleep(1);
                }
                Assert.True(Networking.Networking.IsActive);
            }
            Scene.ProcessPendingLoad();
            Assert.Same(local, Scene.Current);
            Assert.Same(session, Networking.Networking.Session);
            Pump(() => Networking.Networking.JoinState == NetworkJoinState.WaitingForScene, host);

            NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);
            host.Send(FakePeer.SceneSnapshot, w => w.WriteBytes(snapshot.WrittenSpan), client);
            Pump(() => host.Has(FakePeer.SceneLoaded), host);
            Assert.NotSame(local, Scene.Current);
            Assert.Equal(NetworkJoinState.Synchronizing, Networking.Networking.JoinState);
            Assert.Same(session, Networking.Networking.Session);
        }
        finally { Networking.Networking.Shutdown(); Networking.Networking.Configure(previous); }
    }

    [Theory]
    [InlineData(NetworkExecution.Inline, false)]
    [InlineData(NetworkExecution.Inline, true)]
    [InlineData(NetworkExecution.DedicatedThread, false)]
    [InlineData(NetworkExecution.DedicatedThread, true)]
    public void LocalSceneReplacementAfterSnapshotStillEndsJoin(NetworkExecution execution, bool activateNetworkScene)
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        Networking.Networking.Configure(new() { Execution = execution });
        ReadinessHost();
        using NetWriter snapshot = new();
        Networking.Networking.WriteSnapshot(Scene.Current, snapshot);
        Networking.Networking.Shutdown();
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.AutoReady = false;
        host.Session.PeerJoined += peer => host.Send(FakePeer.SceneSnapshot, w => w.WriteBytes(snapshot.WrittenSpan), peer);
        string? failure = null;
        try
        {
            NetSession session = Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            session.Ended += (_, message) => failure = message;
            for (int i = 0; i < 1000 && !Scene.IsLoadPending; i++)
            {
                Networking.Networking.Receive(); Networking.Networking.Flush(); host.Session.Update();
                Thread.Sleep(1);
            }
            Assert.Equal(NetworkJoinState.Loading, Networking.Networking.JoinState);
            if (activateNetworkScene) Scene.ProcessPendingLoad();
            MakeCurrent(CreateScene());
            Assert.Null(Networking.Networking.Session);
            Assert.Equal("The network scene was replaced locally during the session.", failure);
        }
        finally { Networking.Networking.Shutdown(); Networking.Networking.Configure(previous); }
    }

    [Fact]
    public void ClientTimesOutWhenAdmittedWithoutAScene()
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        Networking.Networking.Configure(new() { SceneLoadTimeoutSeconds = 0.1f });
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        string? failure = null;
        try
        {
            NetSession session = Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            session.Ended += (_, message) => failure = message;
            Pump(() => Networking.Networking.JoinState == NetworkJoinState.WaitingForScene, host);
            Assert.True(Networking.Networking.IsActive);
            Assert.False(Networking.Networking.IsReady);
            Pump(() => Networking.Networking.Session == null, host);
            Assert.Contains("Scene readiness timed out during WaitingForScene", failure);
        }
        finally { Networking.Networking.Shutdown(); Networking.Networking.Configure(previous); }
    }

    [Fact]
    public void PeerReadyListenerMayEndTheSession()
    {
        var (listener, _, network) = ReadinessHost();
        listener.PeerReadyAction = _ => Networking.Networking.Shutdown();
        using FakePeer client = FakePeer.Join(network, "client");
        try
        {
            Pump(() => Networking.Networking.Session == null, client);
            Assert.Single(listener.Events.Where(e => e.StartsWith("peer ready")));
            Assert.False(Networking.Networking.IsReady);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void JoinProgressSeparatesTransportConnectionFromSessionAdmission()
    {
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        PendingJoin? pending = null;
        host.Session.JoinRequested += join => pending = join;
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => pending != null, host);
            Assert.Equal(NetworkJoinState.Joining, Networking.Networking.JoinState);
            Assert.False(Networking.Networking.IsActive);
            pending!.Accept();
            Pump(() => Networking.Networking.JoinState == NetworkJoinState.WaitingForScene, host);
            Assert.True(Networking.Networking.IsActive);
            Assert.False(Networking.Networking.IsReady);
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
