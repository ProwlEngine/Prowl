using Prowl.Echo;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Warp;
using Prowl.Warp.Transports.Local;
using Xunit;

namespace Prowl.Runtime.Test;

public class NetworkLifecycleListener : Component, INetworkEvents
{
    [SerializeIgnore] public List<string> Events = new();
    [SerializeIgnore] public Action? SpawnAction;
    [SerializeIgnore] public Action? LostAction;
    [SerializeIgnore] public Action? DespawnAction;
    [SerializeIgnore] public int? HealthAtSpawn;
    public bool TryEditOnSpawn;
    public bool EndSessionOnSpawn;
    [SerializeIgnore] public bool StructuralEditRejected;
    [SerializeIgnore] public bool? WasNetworkedAtDespawn;
    public void OnNetworkSpawn()
    {
        HealthAtSpawn = GameObject.GetComponent<NetCollectionTestComponent>()?.Health;
        if (TryEditOnSpawn)
        {
            try { GameObject.AddComponent<NetTestComponent>(); }
            catch (InvalidOperationException) { StructuralEditRejected = true; }
        }
        Events.Add("spawn"); SpawnAction?.Invoke();
        if (EndSessionOnSpawn) Networking.Networking.Shutdown();
    }
    public void OnNetworkDespawn()
    {
        WasNetworkedAtDespawn = GameObject.Network.Active;
        Events.Add("despawn");
        DespawnAction?.Invoke();
    }
    public void OnNetworkAuthorityGained() => Events.Add("authority gained");
    public void OnNetworkAuthorityLost() { Events.Add("authority lost"); LostAction?.Invoke(); }
    public void OnNetworkOwnerChanged(NetPeer? previousOwner, NetPeer? owner) =>
        Events.Add($"owner {previousOwner?.Id ?? -1} -> {owner?.Id ?? -1}");
}

public partial class NetworkingTests
{
    private (GameObject Root, NetworkLifecycleListener Listener, LocalNetwork Network) LifecycleHost()
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject("Lifecycle root");
        root.Network.Mode = NetworkMode.Object;
        NetworkLifecycleListener listener = root.AddComponent<NetworkLifecycleListener>();
        scene.Add(root); Scene.Load(scene); Scene.ProcessPendingLoad();
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        return (root, listener, network);
    }

    [Fact]
    public void NetworkLifecycleDistinguishesAuthorityOwnershipAndComponentEnable()
    {
        var (root, listener, network) = LifecycleHost();
        try
        {
            Assert.Equal(new[] { "spawn", "authority gained" }, listener.Events);
            listener.Enabled = false;
            using FakePeer client = FakePeer.Join(network, "client");
            Pump(() => client.Has(FakePeer.SceneSnapshot), client);
            NetPeer owner = Networking.Networking.Session!.Peers.Single(p => !p.IsLocal);
            root.Network.AssignOwnership(owner);
            root.Network.AssignOwnership(owner); // No-op.
            root.Network.AssignOwnership(null);
            Networking.Networking.Shutdown();
            Assert.Equal(new[] { "spawn", "authority gained", "authority lost", $"owner -1 -> {owner.Id}",
                $"owner {owner.Id} -> -1", "authority gained", "authority lost", "despawn" }, listener.Events);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void HostDestructionNotifiesOnceBeforeComponentsAreDisposed()
    {
        var (root, listener, _) = LifecycleHost();
        try
        {
            root.Dispose();
            root.Dispose();
            Assert.Equal(new[] { "spawn", "authority gained", "authority lost", "despawn" }, listener.Events);
            Assert.False(listener.WasNetworkedAtDespawn);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void NetworkSpawnCallbackMayEndTheSession()
    {
        Scene scene = CreateScene();
        GameObject root = CreateGameObject();
        NetworkLifecycleListener listener = root.AddComponent<NetworkLifecycleListener>();
        listener.SpawnAction = () => Networking.Networking.Shutdown();
        scene.Add(root); Scene.Load(scene); Scene.ProcessPendingLoad();
        try
        {
            Networking.Networking.Host(new LocalTransport(new(), "host"));
            root.Network.Spawn();
            Assert.Equal(new[] { "spawn", "despawn" }, listener.Events);
            Assert.Null(Networking.Networking.Session);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void SceneReplacementStopsOutgoingNetworkObjects()
    {
        var (root, listener, _) = LifecycleHost();
        try
        {
            Scene.Load(CreateScene()); Scene.ProcessPendingLoad();
            Assert.True(root.IsDisposed);
            Assert.True(listener.IsDisposed);
            Assert.Equal(new[] { "spawn", "authority gained", "authority lost", "despawn" }, listener.Events);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void StructuralChangesAreRejectedBeforeAnyMutation()
    {
        var (root, listener, _) = LifecycleHost();
        GameObject outside = CreateGameObject();
        try
        {
            Assert.Throws<InvalidOperationException>(() => root.AddComponent<NetTestComponent>());
            Assert.Throws<InvalidOperationException>(() => root.RemoveComponent(listener));
            Assert.Throws<InvalidOperationException>(() => root.RemoveAll<NetworkLifecycleListener>());
            Assert.Throws<InvalidOperationException>(() => listener.Destroy());
            Assert.Throws<InvalidOperationException>(() => listener.Dispose());
            Assert.Throws<InvalidOperationException>(() => root.SetParent(outside));
            Assert.Throws<InvalidOperationException>(() => outside.SetParent(root));
            Assert.Throws<InvalidOperationException>(() => outside.AddComponent(listener));
            Assert.Throws<InvalidOperationException>(() => root.Scene!.Remove(root));
            Assert.Throws<InvalidOperationException>(() => root.Network.Mode = NetworkMode.Never);
            Assert.Same(root, listener.GameObject);
            Assert.False(listener.IsDisposed);
            Assert.False(listener.IsDestroyQueued);
            Assert.Null(root.Parent);
            Assert.Null(outside.Parent);
            Assert.Null(root.GetComponent<NetTestComponent>());
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void TreesCanBeBuiltBeforeSpawnAndWholeRootsCanBeDespawned()
    {
        Scene scene = CreateScene();
        Scene.Load(scene); Scene.ProcessPendingLoad();
        try
        {
            Networking.Networking.Host(new LocalTransport(new(), "host"));
            GameObject root = CreateGameObject(), child = CreateGameObject();
            child.SetParent(root);
            NetworkLifecycleListener listener = child.AddComponent<NetworkLifecycleListener>();
            scene.Add(root);
            root.Network.Spawn();
            Assert.Equal(new[] { "spawn", "authority gained" }, listener.Events);
            Assert.Throws<InvalidOperationException>(() => child.Destroy());
            Assert.Throws<InvalidOperationException>(() => child.AddComponent<NetTestComponent>());
            root.Destroy(); EngineObject.ProcessDestroyed();
            Assert.Equal(new[] { "spawn", "authority gained", "authority lost", "despawn" }, listener.Events);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void EndingSessionDuringAuthorityLossDoesNotDuplicateLifecycleEvents()
    {
        var (root, listener, network) = LifecycleHost();
        try
        {
            using FakePeer client = FakePeer.Join(network, "client");
            Pump(() => client.Has(FakePeer.SceneSnapshot), client);
            listener.LostAction = () => Networking.Networking.Shutdown();
            root.Network.AssignOwnership(Networking.Networking.Session!.Peers.Single(p => !p.IsLocal));
            Assert.Equal(new[] { "spawn", "authority gained", "authority lost", "despawn" }, listener.Events);
            Assert.Null(Networking.Networking.Session);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void EndingSessionDuringSceneRegistrationDoesNotRegisterRemainingObjects()
    {
        Scene scene = CreateScene();
        GameObject first = CreateGameObject(), second = CreateGameObject();
        first.Network.Mode = second.Network.Mode = NetworkMode.Object;
        NetworkLifecycleListener a = first.AddComponent<NetworkLifecycleListener>();
        NetworkLifecycleListener b = second.AddComponent<NetworkLifecycleListener>();
        a.SpawnAction = () => Networking.Networking.Shutdown();
        scene.Add(first); scene.Add(second); Scene.Load(scene); Scene.ProcessPendingLoad();
        try
        {
            Networking.Networking.Host(new LocalTransport(new(), "host"));
            Assert.Null(Networking.Networking.Session);
            Assert.Empty(b.Events);
            var entries = (System.Collections.IDictionary)typeof(Networking.Networking)
                .GetField("s_entries", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .GetValue(null)!;
            Assert.Empty(entries);
            var sceneLoaded = (Delegate?)typeof(Scene).GetField("OnSceneLoaded",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null);
            Assert.DoesNotContain(sceneLoaded?.GetInvocationList() ?? [], d => d.Method.Name == "OnHostSceneLoaded");
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void RestartIsRejectedDuringTeardownAndWorksAfterItCompletes()
    {
        var (_, listener, _) = LifecycleHost();
        using LocalTransport next = new(new(), "next");
        Exception? restartFailure = null;
        listener.DespawnAction = () => restartFailure = Record.Exception(() => Networking.Networking.Host(next));
        try
        {
            Networking.Networking.Shutdown();
            Assert.IsType<InvalidOperationException>(restartFailure);
            Assert.Null(Networking.Networking.Session);
            listener.DespawnAction = null;
            Networking.Networking.Host(next);
            Assert.True(Networking.Networking.IsActive);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void EndingSessionDuringSnapshotSpawnDoesNotActivateIncomingScene()
    {
        Scene original = CreateScene();
        GameObject root = CreateGameObject(); root.Network.Mode = NetworkMode.Object;
        NetworkLifecycleListener listener = root.AddComponent<NetworkLifecycleListener>();
        original.Add(root); Scene.Load(original); Scene.ProcessPendingLoad();
        byte[] snapshot;
        try
        {
            Networking.Networking.Host(new LocalTransport(new(), "host"));
            listener.EndSessionOnSpawn = true;
            using NetWriter writer = new();
            Networking.Networking.WriteSnapshot(original, writer);
            snapshot = writer.ToArray();
        }
        finally { Networking.Networking.Shutdown(); }

        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer => host.Send(FakePeer.SceneSnapshot, w => w.WriteBytes(snapshot), peer);
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.Session == null, host);
            Assert.Same(original, Scene.Current);
            Assert.False(Scene.IsLoadPending);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void MalformedSnapshotMetadataDisposesTheIncomingScene()
    {
        Scene original = CreateScene();
        Scene.Load(original);
        Scene.ProcessPendingLoad();
        using NetWriter snapshot = new();
        Networking.Networking.WriteSnapshot(original, snapshot);
        NetReader header = new(snapshot.WrittenSpan);
        header.ReadByte(); header.ReadUInt64(); header.ReadString(); header.ReadBytesWithLength();
        using NetWriter malformed = new();
        malformed.WriteBytes(snapshot.WrittenSpan[..header.Position]);
        malformed.WriteVarUInt32(1); // One entry promised, but its metadata is missing.
        HashSet<Scene> liveBefore = Scene.Live.ToHashSet();
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer => host.Send(FakePeer.SceneSnapshot, w => w.WriteBytes(malformed.WrittenSpan), peer);
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.Session == null, host);
            Assert.All(Scene.Live, scene => Assert.Contains(scene, liveBefore));
            Assert.Same(original, Scene.Current);
            Assert.False(Scene.IsLoadPending);
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
