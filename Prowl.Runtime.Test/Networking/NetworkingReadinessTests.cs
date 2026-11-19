using Prowl.Echo;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Courier;
using Prowl.Courier.Transports.Local;
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
        WasActiveAtReady = GameObject.Scene!.State == SceneState.Running;
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
        SceneInstance scene = CreateScene();
        GameObject root = CreateGameObject("Ready actor");
        root.Network.Networked = true;
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
        SceneInstance scene = CreateScene();
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

    [Theory]
    [InlineData(NetworkExecution.Inline, false)]
    [InlineData(NetworkExecution.Inline, true)]
    [InlineData(NetworkExecution.DedicatedThread, false)]
    [InlineData(NetworkExecution.DedicatedThread, true)]
    public void LocalSceneActivationBeforeFirstSnapshotDoesNotEndJoin(NetworkExecution execution, bool admitted)
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        Networking.Networking.Configure(new() { Execution = execution });
        var (_, state, source) = ReadinessHost();
        Guid actor = state.GameObject.Identifier;
        byte[] snapshot;
        byte[][] spawns;
        try
        {
            (snapshot, spawns) = RecordJoin(source);
        }
        finally { Networking.Networking.Shutdown(); }
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.AutoReady = false;
        SceneInstance local = CreateScene();
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
            // The demo brings up its own world while joining, before the host's first snapshot.
            Bind(local);
            Assert.Same(session, Networking.Networking.Session);
            Pump(() => Networking.Networking.JoinState == NetworkJoinState.WaitingForScene, host);

            NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);
            host.Send(FakePeer.SceneSnapshot, w => w.WriteBytes(snapshot), client);
            Pump(() => host.Has(FakePeer.Ready), host);
            Assert.Same(local, SceneInstance.Main);
            Assert.Equal(NetworkJoinState.Synchronizing, Networking.Networking.JoinState);
            Assert.Same(session, Networking.Networking.Session);

            // The host's networked object lands in the world the client brought up itself.
            ulong generation = SceneGenerationOf(snapshot);
            host.Send(FakePeer.SpawnStarted, w => w.WriteUInt64(generation), client);
            foreach (byte[] spawn in spawns) host.Session.SendRaw(client, FakePeer.Spawn, spawn);
            host.Send(FakePeer.SpawnFinished, w => w.WriteUInt64(generation), client);
            Pump(() => Networking.Networking.IsReady, host);
            Assert.Same(local, SceneInstance.Main);
            Assert.Contains(local.Objects, o => o.Identifier == actor);
        }
        finally { Networking.Networking.Shutdown(); Networking.Networking.Configure(previous); }
    }

    [Theory]
    [InlineData(NetworkExecution.Inline, false)]
    [InlineData(NetworkExecution.Inline, true)]
    [InlineData(NetworkExecution.DedicatedThread, false)]
    [InlineData(NetworkExecution.DedicatedThread, true)]
    public void LocalSceneReplacementAfterSnapshotStillEndsJoin(NetworkExecution execution, bool replaceContent)
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        Networking.Networking.Configure(new() { Execution = execution });
        ReadinessHost();
        using NetWriter snapshot = new();
        NetworkTestScene.Current.WriteScene(SceneInstance.Main, snapshot);
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
            for (int i = 0; i < 1000 && Networking.Networking.JoinState != NetworkJoinState.Synchronizing; i++)
            {
                Networking.Networking.Receive(); Networking.Networking.Flush(); host.Session.Update();
                Thread.Sleep(1);
            }
            Assert.Equal(NetworkJoinState.Synchronizing, Networking.Networking.JoinState);
            if (replaceContent) SceneInstance.Main.ReplaceContent(new SceneContent(), "Local", null);
            else MakeCurrent(CreateScene());
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
