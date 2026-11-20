using Prowl.Echo;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Courier;
using Prowl.Courier.Transports.Local;
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
    [SerializeIgnore] public bool StructuralEditApplied;
    [SerializeIgnore] public bool? WasNetworkedAtDespawn;
    public static Action<NetworkLifecycleListener>? SpawnedOnClient;
    public void OnNetworkSpawn()
    {
        HealthAtSpawn = GameObject.GetComponent<NetCollectionTestComponent>()?.Health;
        if (TryEditOnSpawn)
        {
            GameObject.AddComponent<NetTestComponent>();
            StructuralEditApplied = true;
        }
        Events.Add("spawn"); SpawnAction?.Invoke();
        SpawnedOnClient?.Invoke(this);
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
    [Fact]
    public void OwnershipAndShutdownRestoreAuthoredPhysics()
    {
        SceneInstance scene = CreateScene();
        GameObject root = CreateGameObject("Physics root");
        root.AddComponent<NetworkTransform>();
        root.Network.Networked = true;
        Rigidbody3D body = root.AddComponent<Rigidbody3D>();
        body.MotionType = Jitter2.Dynamics.MotionType.Dynamic;
        scene.Add(root);
        Bind(scene);
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            using FakePeer client = FakePeer.Join(network, "client");
            Pump(() => client.IsReady, client);
            NetPeer owner = Networking.Networking.Session!.Peers.Single(p => !p.IsLocal);
            root.Network.AssignOwnership(owner);
            Assert.Equal(Jitter2.Dynamics.MotionType.Kinematic, body.MotionType);
            root.Network.AssignOwnership(null);
            Assert.Equal(Jitter2.Dynamics.MotionType.Dynamic, body.MotionType);
            root.Network.AssignOwnership(owner);
            Networking.Networking.Shutdown();
            Assert.Equal(Jitter2.Dynamics.MotionType.Dynamic, body.MotionType);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    private (GameObject Root, NetworkLifecycleListener Listener, LocalNetwork Network) LifecycleHost()
    {
        SceneInstance scene = CreateScene();
        GameObject root = CreateGameObject("Lifecycle root");
        root.Network.Networked = true;
        NetworkLifecycleListener listener = root.AddComponent<NetworkLifecycleListener>();
        scene.Add(root); Bind(scene);
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
            Pump(() => client.Has(FakePeer.Scene), client);
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
        SceneInstance scene = CreateScene();
        GameObject root = CreateGameObject();
        NetworkLifecycleListener listener = root.AddComponent<NetworkLifecycleListener>();
        listener.SpawnAction = () => Networking.Networking.Shutdown();
        scene.Add(root); Bind(scene);
        try
        {
            Networking.Networking.Host(new LocalTransport(new(), "host"));
            root.SpawnBuilt();
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
            SceneInstance.Main.ReplaceContent(new SceneContent(), "Next", null);
            Assert.True(root.IsDisposed);
            Assert.True(listener.IsDisposed);
            Assert.Equal(new[] { "spawn", "authority gained", "authority lost", "despawn" }, listener.Events);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void NetworkedTreesAllowLocalStructuralChanges()
    {
        var (root, listener, _) = LifecycleHost();
        GameObject outside = CreateGameObject();
        SceneInstance.Main.Add(outside);
        try
        {
            NetTestComponent added = root.AddComponent<NetTestComponent>();
            Assert.True(root.RemoveComponent(added));
            Assert.True(added.IsDestroyQueued);
            root.AddComponent<NetTestComponent>();
            root.RemoveAll<NetTestComponent>();
            Assert.Null(root.GetComponent<NetTestComponent>());

            NetTestComponent disposed = root.AddComponent<NetTestComponent>();
            disposed.Dispose();
            Assert.True(disposed.IsDisposed);
            NetTestComponent destroyed = root.AddComponent<NetTestComponent>();
            destroyed.Destroy();
            EngineObject.ProcessDestroyed();
            Assert.True(destroyed.IsDisposed);

            Assert.True(outside.AddComponent(listener));
            Assert.Same(outside, listener.GameObject);
            Assert.True(root.SetParent(outside));
            Assert.True(root.SetParent(null!));
            Assert.True(outside.SetParent(root));
            Assert.True(outside.SetParent(null!));

            SceneInstance scene = root.Scene!;
            scene.Remove(root);
            scene.Add(root);
            Assert.Throws<InvalidOperationException>(() => root.Network.ServerOnly = true);
            Assert.False(root.Network.ServerOnly);
            root.Dispose();
            Assert.Equal(0, NetworkDiagnostics.NetworkObjectCount);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void TreesCanBeBuiltBeforeSpawnAndWholeRootsCanBeDespawned()
    {
        SceneInstance scene = CreateScene();
        Bind(scene);
        try
        {
            Networking.Networking.Host(new LocalTransport(new(), "host"));
            GameObject root = CreateGameObject(), child = CreateGameObject();
            child.SetParent(root);
            NetworkLifecycleListener listener = child.AddComponent<NetworkLifecycleListener>();
            scene.Add(root);
            root.SpawnBuilt();
            Assert.Equal(new[] { "spawn", "authority gained" }, listener.Events);
            Assert.NotNull(child.AddComponent<NetTestComponent>());
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
            Pump(() => client.Has(FakePeer.Scene), client);
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
        SceneInstance scene = CreateScene();
        GameObject first = CreateGameObject(), second = CreateGameObject();
        first.Network.Networked = second.Network.Networked = true;
        NetworkLifecycleListener a = first.AddComponent<NetworkLifecycleListener>();
        NetworkLifecycleListener b = second.AddComponent<NetworkLifecycleListener>();
        a.SpawnAction = () => Networking.Networking.Shutdown();
        scene.Add(first); scene.Add(second); Bind(scene);
        try
        {
            Networking.Networking.Host(new LocalTransport(new(), "host"));
            Assert.Null(Networking.Networking.Session);
            Assert.Empty(b.Events);
            Assert.Equal(0, NetworkDiagnostics.NetworkObjectCount);
            var sceneLoaded = (Delegate?)typeof(SceneInstance).GetField("Loaded",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null);
            Assert.DoesNotContain(sceneLoaded?.GetInvocationList() ?? [], d => d.Method.Name == "OnWorldLoaded");
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestartIsRejectedDuringTeardownAndWorksAfterItCompletes(bool nestedShutdown)
    {
        var (_, listener, _) = LifecycleHost();
        using LocalTransport next = new(new(), "next");
        Exception? restartFailure = null;
        listener.DespawnAction = () =>
        {
            if (nestedShutdown) Networking.Networking.Shutdown();
            restartFailure = Record.Exception(() => Networking.Networking.Host(next));
        };
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
    public void EndingSessionDuringAJoinSpawnCallbackEndsTheJoinCleanly()
    {
        SceneInstance original = CreateScene();
        GameObject root = CreateGameObject(); root.Network.Networked = true;
        NetworkLifecycleListener listener = root.AddComponent<NetworkLifecycleListener>();
        original.Add(root); Bind(original);
        byte[] snapshot;
        byte[][] spawns;
        LocalNetwork source = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(source, "host"));
            listener.EndSessionOnSpawn = true;
            (snapshot, _) = RecordJoin(source);
            using NetWriter written = new();
            NetworkTestScene.Current.WriteSpawn(root, -1, NetworkOrphaned.Destroy, written, NetworkGeneration(root),
                NetworkTestScene.Current.NetIdOf(root));
            spawns = [written.ToArray()];
        }
        finally { Networking.Networking.Shutdown(); }

        SceneInstance local = CreateScene();
        Bind(local);
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        SendJoin(host, snapshot, spawns);
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.Session == null, host);
            Assert.Equal(NetworkJoinState.Disconnected, Networking.Networking.JoinState);
            Assert.Same(local, SceneInstance.Main);
            NetworkLifecycleListener received = Find(local, root.Identifier).GetComponent<NetworkLifecycleListener>()!;
            Assert.Equal(new[] { "spawn", "despawn" }, received.Events);
            Assert.Same(root, Assert.Single(original.Objects, o => o.Identifier == root.Identifier));
            Assert.False(root.IsDisposed);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClientCanDestroyALocalNetworkTreeWithoutDespawningItOnTheHost(bool disposeImmediately)
    {
        SceneInstance original = CreateScene();
        GameObject parent = CreateGameObject("Parent"), root = CreateGameObject("Root");
        root.Network.Networked = true;
        root.AddComponent<NetworkLifecycleListener>();
        root.SetParent(parent);
        original.Add(parent); Bind(original);
        byte[] snapshot;
        byte[][] spawns;
        LocalNetwork source = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(source, "host"));
            (snapshot, spawns) = RecordJoin(source);
            Assert.Single(spawns);
        }
        finally { Networking.Networking.Shutdown(); }

        // The client keeps the parent it built itself, and the host's spawn takes the place of its own child.
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        SendJoin(host, snapshot, spawns);
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady, host);
            GameObject receivedParent = Find(SceneInstance.Main, parent.Identifier);
            GameObject receivedRoot = Find(SceneInstance.Main, root.Identifier);
            Assert.Same(receivedParent, receivedRoot.Parent);
            NetworkLifecycleListener listener = receivedRoot.GetComponent<NetworkLifecycleListener>()!;
            if (disposeImmediately) receivedParent.Dispose();
            else
            {
                receivedParent.Destroy();
                EngineObject.ProcessDestroyed();
            }
            Assert.True(receivedParent.IsDisposed);
            Assert.True(receivedRoot.IsDisposed);
            Assert.Equal(new[] { "spawn", "despawn" }, listener.Events);
            Assert.Equal(0, NetworkDiagnostics.NetworkObjectCount);
            Frame([host]);
            Assert.False(host.Has(FakePeer.Despawn));
            Assert.True(Networking.Networking.IsActive);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void MalformedSnapshotMetadataDisposesTheIncomingScene()
    {
        SceneInstance original = CreateScene();
        GameObject marker = CreateGameObject("Marker");
        original.Add(marker);
        Bind(original);
        using NetWriter snapshot = new();
        NetworkTestScene.Current.WriteScene(original, snapshot);
        NetReader header = new(snapshot.WrittenSpan);
        header.ReadByte(); header.ReadUInt64(); header.ReadBytesWithLength();
        using NetWriter malformed = new();
        malformed.WriteBytes(snapshot.WrittenSpan[..header.Position]);
        malformed.WriteVarUInt32(1); // One entry promised, but its metadata is missing.
        HashSet<SceneInstance> liveBefore = SceneInstance.All.ToHashSet();
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer => host.Send(FakePeer.Scene, w => w.WriteBytes(malformed.WrittenSpan), peer);
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.Session == null, host);
            Assert.All(SceneInstance.All, scene => Assert.Contains(scene, liveBefore));
            Assert.Same(marker, Assert.Single(original.Objects));
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AnObjectKeptAcrossLoadsIsNotDuplicatedWhenTheHostChangesScene()
    {
        AssetBackend? previous = AssetDatabase.Backend;
        MemoryAssetBackend backend = new();
        AssetDatabase.ClearForTests();
        AssetDatabase.Backend = backend;
        var authoring = new SceneInstance();
        GameObject manager = authoring.Create("Manager");
        manager.AddComponent<KeptAcrossLoadsComponent>();
        Guid managerId = manager.Identifier;
        Scene level = AssetDatabase.Get<Scene>(backend.Add(new Scene { Data = authoring.ActiveSection.CaptureTree() }, "Level.scene"))!;
        authoring.Dispose();

        // The host loads the same scene asset twice, so each scene message names it under a new generation.
        byte[] SceneNaming(ulong generation)
        {
            using NetWriter writer = new();
            writer.WriteByte(NetworkSystem.SceneMessageFormat);
            writer.WriteUInt64(generation);
            writer.WriteVarUInt32(1);
            new SectionHeader(1, "Level", true, level.AssetID).Write(writer);
            return writer.ToArray();
        }

        MakeCurrent(CreateScene());
        LocalNetwork network = new();
        FakePeer host = FakePeer.Host(network);
        SendJoin(host, SceneNaming(1));
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady && SceneInstance.Main.Objects.Any(o => o.Identifier == managerId), host);
            Assert.Contains(SceneInstance.Main.Persistent.Roots, p => p.Identifier == managerId);

            host.Session.SendRaw(host.Session.Peers.Single(p => !p.IsLocal), FakePeer.Scene, SceneNaming(2));
            Pump(() => NetworkTestScene.Current.SceneGeneration == 2 && Networking.Networking.IsReady, host);
            PumpFor(50, host);

            Assert.Single(SceneInstance.Main.Objects, o => o.Identifier == managerId);
            Assert.Single(SceneInstance.Main.Persistent.Roots, p => p.Identifier == managerId);
        }
        finally
        {
            Networking.Networking.Shutdown();
            host.Dispose();
            AssetDatabase.Backend = previous;
            AssetDatabase.ClearForTests();
        }
    }
}

public class KeptAcrossLoadsComponent : Component
{
    public override void OnEnable() => GameObject.Persistent = true;
}
