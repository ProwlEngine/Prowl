// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Diagnostics;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Courier;

namespace Prowl.Runtime.Networking;

/// <summary>Hosts or joins the engine's replicated scene session. Additional service connections use Courier directly.</summary>
public static partial class Networking
{
    /// <summary>Message IDs from here up are reserved for engine replication.</summary>
    public const ushort EngineMessageIds = 0xFE00;

    internal static NetworkSystem? System { get; private set; }

    /// <summary>The current engine session, or null without one.</summary>
    public static NetSession? Session { get; private set; }

    /// <summary>Who receives which networked objects by distance, while hosting. Null otherwise.</summary>
    public static NetworkInterest? Interest => System is { IsHost: true } system ? system.Interest : null;

    /// <summary>The bound IP port, or zero for a transport without an IP listener.</summary>
    public static int LocalPort => s_transport?.LocalPort ?? 0;

    /// <summary>Session admission completed. Use IsReady to check scene readiness.</summary>
    public static bool IsActive => Session?.State == SessionState.Active;

    public static bool IsHost => Session?.IsHost == true;
    public static bool IsClient => Session is { IsHost: false };

    /// <summary>Sync and transform ticks per second, rounded to a whole number of fixed steps. RPCs flush every frame.</summary>
    public static float TickRate { get; set; } = 30;

    /// <summary>
    ///     The least delay proxies show buffered transforms at. The buffer grows past it with the send interval and
    ///     the measured jitter.
    /// </summary>
    public static TimeSpan InterpolationDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    ///     The network tick this peer last completed, one every <see cref="TickInterval" /> of the networked world's fixed
    ///     steps. A client follows the host's count, so a tick names the same moment on every peer, give or take latency.
    /// </summary>
    public static uint Tick => System?.NetworkTick ?? 0;

    /// <summary>The host's simulation time in seconds since the session began. A client reads its estimate of it.</summary>
    public static double ServerTime => System?.ServerTime ?? 0;

    /// <summary>Seconds per network tick: <see cref="TickRate" /> rounded to a whole number of fixed steps.</summary>
    public static double TickInterval => NetworkSystem.TickInterval;

    public static NetworkJoinState JoinState => System?.JoinState ?? NetworkJoinState.Disconnected;

    /// <summary>True after scene activation and catch-up. Repeats for each host scene change.</summary>
    public static bool IsReady => System?.IsReady == true;

    /// <summary>Remote readiness is known on the host; clients know only their own readiness.</summary>
    public static bool IsPeerReady(NetPeer peer) => System?.IsPeerReady(peer) == true;

    /// <summary>Hosts a session and shares the current scene with joining peers.</summary>
    public static NetSession Host(Transport transport, string name = "Host", NetSessionOptions? options = null)
    {
        BeginSession();
        NetworkSystem.ValidateSceneMessageLimit(options);
        if (options is { SharePeerList: false })
        {
            throw new NotSupportedException(
                "Scene replication relays RPCs and changes from one client to the others, so clients need the peer list.");
        }

        using FrameStatScope stats = FrameStats.Network.Scope();
        NetSessionOptions session = WithSchema(options);
        PrepareTransport(transport);
        try
        {
            if (s_transport is not ThreadedTransport worker)
            {
                return AttachSession(NetSession.Host(transport, name, session));
            }

            worker.Listen();
            return AttachSession(NetSession.CreateHost(worker, name, session));
        }
        catch
        {
            Shutdown();
            throw;
        }
    }

    /// <summary>
    ///     Host: builds <paramref name="prefab" /> into SceneInstance.Main at a pose and spawns it. Clients build it from
    ///     their own copy of the prefab, so only [Sync] values, the root's transform and its network settings carry
    ///     over; other fields changed on the host don't. Without a session it is only instantiated.
    /// </summary>
    public static GameObject SpawnPrefab(PrefabAsset prefab, Float3 position, Quaternion rotation, NetPeer? owner = null,
        NetworkOrphaned? orphaned = null)
    {
        ArgumentNullException.ThrowIfNull(prefab);
        if (System is { } system)
        {
            return system.SpawnPrefab(prefab, position, rotation, owner, orphaned);
        }

        GameObject? go = GameObject.Instantiate(prefab, SceneInstance.Main, position, rotation);
        if (go.IsNotValid())
        {
            throw new ArgumentException($"'{prefab.Name}' could not be instantiated.", nameof(prefab));
        }

        go!.Network.SpawnSerialized(owner, orphaned);
        return go;
    }

    /// <summary>
    ///     Host: builds an object with the factory registered as <paramref name="factory" />, adds it to
    ///     SceneInstance.Main and spawns it. Clients build theirs with their own factory of that name, given the same
    ///     <paramref name="data" />, so as with prefabs only [Sync] values, the root's transform and its network settings
    ///     carry over. Without a session it is only built.
    /// </summary>
    public static GameObject SpawnFactory(string factory, Float3 position, Quaternion rotation, byte[]? data = null,
        NetPeer? owner = null, NetworkOrphaned? orphaned = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(factory);
        data ??= [];
        if (System is { } system)
        {
            return system.SpawnFactory(factory, position, rotation, data, owner, orphaned);
        }

        GameObject go = NetworkSystem.BuildWithFactory(factory, position, rotation, data);
        SceneInstance.Main.Add(go);
        go.Network.SpawnSerialized(owner, orphaned);
        return go;
    }

    /// <summary>
    ///     Host: spawns an object already in the networked world by serializing its whole tree for each peer. Slow, and
    ///     not recommended: prefer <see cref="SpawnPrefab" /> or <see cref="SpawnFactory" />, which send only what
    ///     changed from what every peer can build itself. Spawning an object already spawned assigns its owner.
    /// </summary>
    public static void SpawnGameObject(GameObject go, NetPeer? owner = null, NetworkOrphaned? orphaned = null)
    {
        ArgumentNullException.ThrowIfNull(go);
        go.Network.SpawnSerialized(owner, orphaned);
    }

    /// <summary>
    ///     Registers a factory under <paramref name="name" /> for <see cref="SpawnFactory" />, replacing any by that name.
    ///     Every peer registers the same factories. A client hands an object leaving the network to
    ///     <paramref name="destroy" />, if given, instead of destroying it, so it can be pooled.
    /// </summary>
    public static void RegisterFactory(string name, NetworkFactory build, Action<GameObject>? destroy = null) =>
        NetworkFactories.Register(name, build, destroy);

    /// <summary>
    ///     Sends an <see cref="INetworkSerialize" /> component's state at the next tick, as Mirror's SetDirty. Only the
    ///     peer that writes it may; anywhere else the change is dropped with a warning.
    /// </summary>
    public static void SetDirty(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (component is not INetworkSerialize)
        {
            throw new ArgumentException($"{component.GetType().Name} doesn't implement INetworkSerialize.", nameof(component));
        }

        if (!IsActive || NetworkMembers.Of(component).Sync(NetworkSerializeMember.CustomSlot) is not { } member)
        {
            return;
        }

        if (!System!.HasControl(component, member.Flags))
        {
            System.Sync.WarnSyncOnce(component, member, "was marked dirty on a peer that doesn't write it");
            return;
        }

        System.Sync.MarkSyncDirty(component, member.Slot);
    }

    /// <summary>This peer's player object, or null. Set by the host with <see cref="AddPlayer" />.</summary>
    public static GameObject? LocalPlayer => System?.LocalPlayer;

    /// <summary>
    ///     Host: makes <paramref name="player" /> the player of <paramref name="peer" />, as Mirror's
    ///     AddPlayerForConnection: spawned owned by them, or handed to them if already spawned. Replaces any player they had.
    /// </summary>
    public static void AddPlayer(NetPeer peer, GameObject player)
    {
        ArgumentNullException.ThrowIfNull(peer);
        ArgumentNullException.ThrowIfNull(player);
        System?.AddPlayer(peer, player);
    }

    /// <summary>Host: <paramref name="peer" /> no longer has a player. Its old player stays spawned.</summary>
    public static void RemovePlayer(NetPeer peer)
    {
        ArgumentNullException.ThrowIfNull(peer);
        System?.RemovePlayer(peer);
    }

    /// <summary>Host: the player of <paramref name="peer" />, or null.</summary>
    public static GameObject? PlayerOf(NetPeer peer)
    {
        ArgumentNullException.ThrowIfNull(peer);
        return System?.PlayerOf(peer);
    }

    /// <summary>
    ///     Host: takes a spawned object out of the network, as Mirror's UnSpawn does. Clients despawn it, and this peer
    ///     keeps it, to pool or spawn again later; one built from a prefab or factory spawns again the same way.
    /// </summary>
    public static void Despawn(GameObject go)
    {
        ArgumentNullException.ThrowIfNull(go);
        System?.Despawn(go);
    }

    /// <summary>
    ///     Builds this peer's instances of <paramref name="prefab" /> with <paramref name="spawn" /> and takes them back
    ///     with <paramref name="unspawn" /> when they leave the network, instead of instantiating and destroying them, so
    ///     they can be pooled. The tree <paramref name="spawn" /> returns must be the prefab's, in no scene yet.
    /// </summary>
    public static void RegisterPrefab(PrefabAsset prefab, Func<PrefabAsset, GameObject>? spawn = null,
        Action<GameObject>? unspawn = null)
    {
        ArgumentNullException.ThrowIfNull(prefab);
        NetworkPrefabHandlers.Register(prefab.AssetID, spawn, unspawn);
    }

    /// <summary>Removes the handlers registered for <paramref name="prefab" />. False if there were none.</summary>
    public static bool UnregisterPrefab(PrefabAsset prefab)
    {
        ArgumentNullException.ThrowIfNull(prefab);
        return NetworkPrefabHandlers.Unregister(prefab.AssetID);
    }

    /// <summary>Removes the factory registered as <paramref name="name" />. False if there was none.</summary>
    public static bool UnregisterFactory(string name) => NetworkFactories.Unregister(name);

    /// <summary>Joins a host and loads its scene in place of the local scene.</summary>
    public static NetSession Join(Transport transport, string address, string name, ReadOnlySpan<byte> token = default,
        NetSessionOptions? options = null)
    {
        BeginSession();
        NetworkSystem.ValidateSceneMessageLimit(options);
        using FrameStatScope stats = FrameStats.Network.Scope();
        NetSessionOptions session = WithSchema(options);
        PrepareTransport(transport);
        try
        {
            return AttachSession(s_transport is not ThreadedTransport worker
                ? NetSession.Join(transport, address, name, token, session)
                : NetSession.CreateClient(worker.Connect(address), name, token, session));
        }
        catch
        {
            Shutdown();
            throw;
        }
    }

    private static NetSessionOptions WithSchema(NetSessionOptions? options)
    {
        NetSessionOptions session = options ?? s_configuration.CreateSessionOptions();
        session.AppVersion = NetworkSchema.AppVersion(session.AppVersion, s_configuration.ValidatedCopy().PositionPrecision);
        return session;
    }
}
