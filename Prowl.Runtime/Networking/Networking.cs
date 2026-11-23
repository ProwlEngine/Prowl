// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Diagnostics;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Courier;

namespace Prowl.Runtime.Networking;

/// <summary>
///     Hosts or joins the engine's session: every peer loads the same scenes itself, and networked objects, their
///     [Sync] state and RPCs travel between them. Additional service connections use Courier directly.
/// </summary>
public static partial class Networking
{
    /// <summary>Message IDs from here up are reserved for engine replication.</summary>
    public const ushort EngineMessageIds = 0xFE00;

    internal static NetworkSystem? System { get; private set; }

    /// <summary>The current engine session, or null without one.</summary>
    public static NetSession? Session { get; private set; }

    private static NetworkInterest? s_interest;

    /// <summary>Host: decides which clients receive which networked objects. Null sends every object to every client.</summary>
    public static NetworkInterest? Interest
    {
        get => s_interest;
        set
        {
            if (ReferenceEquals(value, s_interest))
            {
                return;
            }

            s_interest = value;
            System?.OnInterestChanged();
        }
    }

    /// <summary>The bound IP port, or zero for a transport without an IP listener.</summary>
    public static int LocalPort => s_transport?.LocalPort ?? 0;

    /// <summary>Session admission completed. Use IsReady to check scene readiness.</summary>
    public static bool IsActive => Session?.State == SessionState.Active;

    /// <summary>True while this peer hosts the session: it is the authority, and plays in it too.</summary>
    public static bool IsHost => Session?.IsHost == true;

    /// <summary>True while this peer is in a session someone else hosts.</summary>
    public static bool IsClient => Session is { IsHost: false };

    /// <summary>
    ///     Sync and transform ticks per second, rounded to a whole number of fixed steps. RPCs flush every frame.
    ///     <see cref="Configure" /> sets it from <see cref="NetworkConfiguration.TickRate" />. Set it before the session
    ///     starts, the same on every peer.
    /// </summary>
    public static float TickRate { get; set; } = 30;

    /// <summary>
    ///     The least delay proxies show buffered transforms at. The buffer grows past it with the send interval and
    ///     the measured jitter. <see cref="Configure" /> sets it from
    ///     <see cref="NetworkConfiguration.InterpolationMilliseconds" />.
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

    /// <summary>How far this peer is towards playing. Gameplay starts at <see cref="NetworkJoinState.Ready" />.</summary>
    public static NetworkJoinState JoinState => System?.JoinState ?? NetworkJoinState.Disconnected;

    /// <summary>True once the scene is loaded and the host finished sending its spawns. Repeats for each host scene change.</summary>
    public static bool IsReady => System?.IsReady == true;

    /// <summary>Remote readiness is known on the host; clients know only their own readiness.</summary>
    public static bool IsPeerReady(NetPeer peer) => System?.IsPeerReady(peer) == true;

    /// <summary>
    ///     Hosts a session in the current scenes. Joining peers are told which scenes to load, load them from their own
    ///     assets, and receive the networked objects in them.
    /// </summary>
    public static NetSession Host(Transport transport, string name = "Host", NetSessionOptions? options = null)
    {
        BeginSession();
        NetworkSystem.ValidateMessageLimit(options);
        if (options is { SharePeerList: false })
        {
            throw new NotSupportedException(
                "The host passes RPCs and changes from one client on to the others, so clients need the peer list.");
        }

        using FrameStatScope stats = FrameStats.Network.Scope();
        NetSessionOptions session = options ?? s_configuration.CreateSessionOptions();
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
    ///     Host: builds <paramref name="prefab" /> into Scene.Main at a pose and spawns it. Clients build it from
    ///     their own copy of the prefab, so only [Sync] values, the root's transform and its network settings carry
    ///     over; other fields changed on the host don't. Without a session it is instantiated, and spawned once this
    ///     peer hosts with it in the world.
    /// </summary>
    public static GameObject SpawnPrefab(PrefabAsset prefab, Float3 position, Quaternion rotation, NetPeer? owner = null,
        NetworkOrphaned? orphaned = null)
    {
        ArgumentNullException.ThrowIfNull(prefab);
        if (System is { } system)
        {
            return system.SpawnPrefab(prefab, position, rotation, owner, orphaned);
        }

        GameObject? go = GameObject.Instantiate(prefab, Scene.Main, position, rotation);
        if (go.IsNotValid())
        {
            throw new ArgumentException($"'{prefab.Name}' could not be instantiated.", nameof(prefab));
        }

        go!.Network.MarkSpawned(orphaned);
        return go;
    }

    /// <summary>
    ///     Host: builds an object with the factory registered as <paramref name="factory" />, adds it to
    ///     Scene.Main and spawns it. Clients build theirs with their own factory of that name, given the same
    ///     <paramref name="data" />, so as with prefabs only [Sync] values, the root's transform and its network settings
    ///     carry over. Without a session it is built, and spawned once this peer hosts with it in the world.
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
        NetworkSystem.RememberFactory(go, factory, data);
        Scene.Main.Add(go);
        go.Network.MarkSpawned(orphaned);
        return go;
    }

    /// <summary>
    ///     Host: spawns an object every peer can build itself: an instance of a prefab
    ///     that is in Scene.Main, or an object despawned before. Anything else throws, since clients would
    ///     have nothing to build it from, and one that is spawned already is left as it is, with a warning. Without
    ///     a session it is only marked networked.
    /// </summary>
    public static void Spawn(GameObject go, NetPeer? owner = null, NetworkOrphaned? orphaned = null)
    {
        ArgumentNullException.ThrowIfNull(go);
        if (System is { } system)
        {
            system.Spawn(go, owner, orphaned ?? go.Network.Orphaned);
        }
        else
        {
            go.Network.MarkSpawned(orphaned);
        }
    }

    /// <summary>
    ///     Registers a factory under <paramref name="name" /> for <see cref="SpawnFactory" />, replacing any by that name.
    ///     Every peer registers the same factories. A client hands an object leaving the network to
    ///     <paramref name="destroy" />, if given, instead of destroying it, so it can be pooled.
    /// </summary>
    public static void RegisterFactory(string name, NetworkFactory build, Action<GameObject>? destroy = null) =>
        NetworkFactories.Register(name, build, destroy);

    /// <summary>
    ///     Sends an <see cref="INetworkSerialize" /> component's state at the next tick. Only the
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

    /// <summary>
    ///     The spawned networked object with this <see cref="NetworkAccessor.NetId" /> on this peer, or null when it
    ///     has none: the id is the same on every peer, so it names an object in a message of the game's own.
    /// </summary>
    public static GameObject? Find(uint netId) =>
        netId != 0 && System is { } system && system.Entries.TryGetNet(netId, out NetworkObject? entry) &&
        !entry.TearingDown && entry.GameObject.IsValid()
            ? entry.GameObject
            : null;

    /// <summary>This peer's player object, or null. Set by the host with <see cref="AddPlayer" />.</summary>
    public static GameObject? LocalPlayer => System?.LocalPlayer;

    /// <summary>
    ///     Host: makes <paramref name="player" /> the player of <paramref name="peer" />:
    ///     spawned owned by them, or handed to them if already spawned. Throws on a client,
    ///     for a peer that has a player already, and for an object that is another peer's player. While it is their
    ///     player, the object can't be given to anyone else.
    /// </summary>
    public static void AddPlayer(NetPeer peer, GameObject player)
    {
        ArgumentNullException.ThrowIfNull(peer);
        ArgumentNullException.ThrowIfNull(player);
        System?.AddPlayer(peer, player, false);
    }

    /// <summary>
    ///     Host: gives <paramref name="peer" /> another player in place of the one they have.
    ///     The old one stays spawned and theirs, as an ordinary object.
    /// </summary>
    public static void ReplacePlayer(NetPeer peer, GameObject player)
    {
        ArgumentNullException.ThrowIfNull(peer);
        ArgumentNullException.ThrowIfNull(player);
        System?.AddPlayer(peer, player, true);
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
    ///     Host: takes a spawned object out of the network. Clients despawn it, and this peer
    ///     keeps it, to pool or to spawn again with <see cref="Spawn" />, which sends it the way it was built.
    /// </summary>
    public static void Despawn(GameObject go)
    {
        ArgumentNullException.ThrowIfNull(go);
        System?.Despawn(go);
    }

    /// <summary>
    ///     On a client, builds the instances of <paramref name="prefab" /> the host spawns with <paramref name="spawn" />
    ///     and takes them back with <paramref name="unspawn" /> when the host despawns them, instead of instantiating
    ///     and destroying them, so they can be pooled. The host builds its own instances itself, and neither runs when
    ///     the session ends. The tree <paramref name="spawn" /> returns must be the prefab's, in no scene yet.
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
        NetworkSystem.ValidateMessageLimit(options);
        using FrameStatScope stats = FrameStats.Network.Scope();
        NetSessionOptions session = options ?? s_configuration.CreateSessionOptions();
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
}
