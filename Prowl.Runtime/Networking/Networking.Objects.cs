// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Runtime.Diagnostics;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Warp;

namespace Prowl.Runtime.Networking;

// Networked objects: which exist, who controls them, spawning and despawning.
public static partial class Networking
{
    private const int NoOwner = -1;
    private static uint s_nextGeneration;

    private static uint NextGeneration() => checked(++s_nextGeneration);

    private static uint NextNetId() => checked(++s_state.NextNetId);

    /// <summary>The GameObject or Component with this identifier in the replicated scene, or null.</summary>
    internal static EngineObject? FindByIdentifier(Guid id)
    {
        if (id == Guid.Empty || s_state.Scene == null)
        {
            return null;
        }

        if (s_state.Spawning != null && s_state.Spawning.TryGetValue(id, out EngineObject? spawning))
        {
            return spawning;
        }

        if (s_state.Index.TryGetValue(id, out EngineObject? found) && IsLive(found, id))
        {
            return found;
        }

        if (s_state.IndexFrame == s_frame)
        {
            return null;
        }

        s_state.IndexFrame = s_frame;
        s_state.Index.Clear();
        foreach (GameObject go in s_state.Scene.AllObjects)
        {
            Index(go);
        }

        return s_state.Index.TryGetValue(id, out found) && IsLive(found, id) ? found : null;
    }

    private static void Index(GameObject go)
    {
        s_state.Index[go.Identifier] = go;
        foreach (Component component in go.GetComponents<Component>())
        {
            s_state.Index[component.Identifier] = component;
        }
    }

    private static void IndexTree(GameObject root)
    {
        Index(root);
        foreach (GameObject child in root.GetChildrenDeep())
        {
            Index(child);
        }
    }

    private static bool IsLive(EngineObject obj, Guid id) => obj switch
    {
        GameObject go => !go.IsDisposed && go.Identifier == id && go.Scene == s_state.Scene,
        Component c => !c.IsDisposed && c.Identifier == id && c.GameObject.IsValid() && c.GameObject.Scene == s_state.Scene,
        _ => false
    };

    /// <summary>The networked object <paramref name="go" /> belongs to, if any.</summary>
    private static NetworkEntry? FindEntry(GameObject go)
    {
        GameObject? root = NetworkAccessor.RootOf(go);
        return root != null && s_state.Entries.TryGetValue(root.Identifier, out NetworkEntry? entry) &&
               entry.GameObject == root
            ? entry
            : null;
    }

    private static int OwnerOf(GameObject go) => FindEntry(go)?.Owner ?? NoOwner;

    internal static uint GenerationOf(GameObject go) => FindEntry(go)?.Generation ?? 0;

    internal static uint NetIdOf(GameObject go) => FindEntry(go)?.NetId ?? 0;

    internal static bool IsNetworked(GameObject go) => IsActive && FindEntry(go) is { TearingDown: false };

    internal static bool IsProxy(GameObject go) => IsActive && !HasControl(go);

    /// <summary>True if this peer drives <paramref name="go" />: it owns it, or it is the host and nobody does.</summary>
    internal static bool HasControl(GameObject go)
    {
        if (!IsActive)
        {
            return true;
        }

        int owner = OwnerOf(go);
        return owner == NoOwner ? IsHost : owner == Session!.LocalPeer!.Id;
    }

    internal static bool HasControl(Component component, SyncFlags flags) =>
        (flags & SyncFlags.FromHost) != 0 ? !IsActive || IsHost : HasControl(component.GameObject);

    internal static NetPeer? GetOwner(GameObject go)
    {
        if (!IsActive || FindEntry(go) is { TearingDown: true })
        {
            return null;
        }

        int owner = OwnerOf(go);
        return owner != NoOwner && Session!.TryGetPeer(owner, out NetPeer? peer) ? peer : null;
    }

    // --- Spawning ---------------------------------------------------------------------------------

    /// <summary>
    ///     Makes <paramref name="go" /> a networked object and, in a session, creates it on every client. Everything
    ///     is checked before anything changes. Spawning one that already is networked (a scene-placed one, say)
    ///     hands it to <paramref name="owner" /> instead.
    /// </summary>
    internal static void Spawn(GameObject go, NetPeer? owner, NetworkOrphaned orphaned)
    {
        if (!Enum.IsDefined(orphaned))
        {
            throw new ArgumentOutOfRangeException(nameof(orphaned));
        }

        if (IsActive)
        {
            if (!IsHost)
            {
                throw new InvalidOperationException("Only the host can spawn networked objects.");
            }

            ValidateOwner(owner);

            if (FindEntry(go) is { TearingDown: true })
            {
                throw new InvalidOperationException("An object being despawned cannot be spawned again.");
            }

            if (go.Scene != s_state.Scene)
            {
                throw new InvalidOperationException(
                    $"'{go.Name}' has to be in the current scene before it can be spawned.");
            }

            if (NetworkAccessor.IsExcludedFromNetwork(go))
            {
                throw new InvalidOperationException(
                    $"'{go.Name}' is under an object that is NetworkMode.Never, so it can't be networked.");
            }

            // Asked of the parent, since this object may be marked NetworkMode.Object already.
            if (go.Parent.IsValid() && go.Parent!.Network.Root is { } outer)
            {
                throw new InvalidOperationException(
                    $"'{go.Name}' is inside the networked object '{outer.Name}' already.");
            }
        }

        go.Network.Mode = NetworkMode.Object;
        go.Network.Orphaned = orphaned;
        if (!IsActive)
        {
            return;
        }

        using FrameStatScope stats = FrameStats.Network.Scope();
        if (s_state.Entries.TryGetValue(go.Identifier, out NetworkEntry? existing) && existing.GameObject == go)
        {
            ChangeOwner(existing, owner?.Id ?? NoOwner);
            return;
        }

        NetworkEntry entry =
            new(go) { Owner = owner?.Id ?? NoOwner, Orphaned = orphaned, Generation = NextGeneration(), NetId = NextNetId() };
        s_state.Entries.Add(entry);
        IndexTree(go);

        using NetWriter message = new(4096);
        WriteSpawn(go, entry.Owner, entry.Orphaned, message, entry.Generation, entry.NetId);
        BroadcastEngine(EngineMessage.Spawn, message.WrittenSpan);
        ControlChanged(go);
        NotifySpawn(entry);
    }

    /// <summary>A Spawn message: where the object goes, who owns it, the object itself and its [Sync] values.</summary>
    internal static void WriteSpawn(GameObject go, int owner, NetworkOrphaned orphaned, NetWriter output,
        uint generation = 0, uint netId = 0)
    {
        using FrameStatScope stats = FrameStats.Network.Scope();
        EchoObject echo;
        NetworkSerialization.WritingSnapshot = true;
        try
        {
            // References out of the tree go by identifier and land on the client's own objects.
            echo = Serializer.Serialize(typeof(GameObject), go, new SerializationContext
            {
                ExternalReferences = new NetworkSerialization.SnapshotReferences(SceneReferenceResolver.ForTree(go))
            });
        }
        finally
        {
            NetworkSerialization.WritingSnapshot = false;
        }

        output.WriteGuid(go.Identifier);
        output.WriteGuid(go.Parent.IsValid() ? go.Parent!.Identifier : Guid.Empty);
        output.WriteVarUInt32((uint)(owner + 1));
        output.WriteByte((byte)orphaned);
        output.WriteUInt32(generation);
        output.WriteVarUInt32(netId == 0 ? NextNetId() : netId);
        output.WriteBytesWithLength(Compress(echo));

        List<GameObject> tree = [go, .. go.GetChildrenDeep()];
        WriteSyncState(output, tree);
    }

    private static void OnSpawn(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost || s_state.Scene == null)
        {
            return;
        }

        NetSession? session = Session;
        Scene scene = s_state.Scene;
        Guid id = reader.ReadGuid();
        Guid parentId = reader.ReadGuid();
        int owner = (int)reader.ReadVarUInt32() - 1;
        NetworkOrphaned orphaned = ReadOrphaned(ref reader);
        uint generation = reader.ReadUInt32();
        uint netId = ReadNetId(ref reader);
        EchoObject echo = Decompress(reader.ReadBytesWithLength());

        GameObject? go =
            GameObject.DeserializePreservingIdentifiers(echo, SceneReferenceResolver.ContextForLinking(scene));
        if (go.IsNotValid())
        {
            throw new InvalidDataException("The host sent a networked object that could not be read.");
        }

        bool attached = false;
        try
        {
            if (Session != session || s_state.Scene != scene)
            {
                return;
            }

            if (go!.Identifier != id)
            {
                throw new InvalidDataException("A spawned object's identifier doesn't match its message.");
            }

            // Resolve initial Sync references within the new tree before enabling its components.
            s_state.Spawning = new Dictionary<Guid, EngineObject>();
            try
            {
                foreach (GameObject part in (List<GameObject>)[go, .. go.GetChildrenDeep()])
                {
                    s_state.Spawning[part.Identifier] = part;
                    foreach (Component component in part.GetComponents<Component>())
                    {
                        s_state.Spawning[component.Identifier] = component;
                    }
                }

                ApplySyncState(sender, ref reader, null, true, requireEnd: true);
            }
            finally
            {
                s_state.Spawning = null;
            }

            if (Session != session || s_state.Scene != scene)
            {
                return;
            }

            NetworkEntry entry = new(go) { Owner = owner, Orphaned = orphaned, Generation = generation, NetId = netId };
            go.Network.ApplyOrphaned(orphaned);
            s_state.Entries.Add(entry);
            ControlChanged(go);
            if (FindByIdentifier(parentId) is GameObject parent)
            {
                go.SetParent(parent, false);
            }
            else
            {
                scene.Add(go);
            }

            attached = true;
            if (Session != session || s_state.Scene != scene)
            {
                return;
            }

            IndexTree(go);
            NotifySpawn(entry);
        }
        finally
        {
            // Scene/parent ownership starts only after successful attachment. Failed metadata,
            // Sync state, or attachment must release components and any resources they own.
            if (!attached)
            {
                if (s_state.Entries.TryGetValue(go!.Identifier, out NetworkEntry? pending) && pending.GameObject == go)
                {
                    s_state.Entries.Remove(go.Identifier);
                }

                go.Dispose();
            }
        }
    }

    /// <summary>A GameObject was disposed. On the host, a networked object going away goes away everywhere.</summary>
    internal static void OnGameObjectDisposed(GameObject go)
    {
        if (!IsHost || !s_state.Entries.TryGetValue(go.Identifier, out NetworkEntry? entry) || entry.GameObject != go)
        {
            return;
        }

        using FrameStatScope stats = FrameStats.Network.Scope();
        s_state.Entries.Remove(go.Identifier);

        // A scene being swapped out is disabled first. Its objects aren't destroyed, they're replaced by the next snapshot.
        if (go.Scene is not { IsDisposed: false, IsActive: true } || !IsActive)
        {
            return;
        }

        using NetWriter message = new(32);
        message.WriteGuid(go.Identifier);
        BroadcastEngine(EngineMessage.Despawn, message.WrittenSpan);
    }

    private static void OnDespawn(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost)
        {
            return;
        }

        Guid id = reader.ReadGuid();
        if (s_state.Entries.Remove(id, out NetworkEntry? entry) && entry.GameObject.IsValid())
        {
            NotifyDespawn(entry);
            entry.GameObject.Destroy();
        }
    }

    // --- Ownership --------------------------------------------------------------------------------

    internal static void SetOwner(GameObject go, NetPeer? owner)
    {
        if (!IsActive)
        {
            return;
        }

        if (!IsHost)
        {
            throw new InvalidOperationException("Only the host can change who owns a networked object.");
        }

        ValidateOwner(owner);

        if (!s_state.Entries.TryGetValue(go.Identifier, out NetworkEntry? entry) || entry.GameObject != go ||
            entry.TearingDown)
        {
            throw new InvalidOperationException($"'{go.Name}' is not a spawned networked object.");
        }

        using FrameStatScope stats = FrameStats.Network.Scope();
        ChangeOwner(entry, owner?.Id ?? NoOwner);
    }

    private static void ValidateOwner(NetPeer? owner)
    {
        if (owner != null && (!Session!.TryGetPeer(owner.Id, out NetPeer? current) || !ReferenceEquals(current, owner)))
        {
            throw new ArgumentException("The owner must be a peer from the current session.", nameof(owner));
        }
    }

    internal static void SetOrphaned(GameObject go, NetworkOrphaned orphaned)
    {
        if (!IsActive || FindEntry(go) is not { TearingDown: false } entry || entry.GameObject != go)
        {
            return;
        }

        if (!IsHost)
        {
            throw new InvalidOperationException("Only the host can change a spawned object's orphaned policy.");
        }

        entry.Orphaned = orphaned;
        using NetWriter message = new();
        message.WriteGuid(go.Identifier);
        message.WriteUInt32(entry.Generation);
        message.WriteByte((byte)orphaned);
        BroadcastEngine(EngineMessage.Orphaned, message.WrittenSpan);
    }

    private static uint ReadNetId(ref NetReader reader)
    {
        uint netId = reader.ReadVarUInt32();
        return netId != 0 ? netId : throw new InvalidDataException("A networked object came without a network id.");
    }

    private static NetworkOrphaned ReadOrphaned(ref NetReader reader)
    {
        NetworkOrphaned orphaned = (NetworkOrphaned)reader.ReadByte();
        if (!Enum.IsDefined(orphaned))
        {
            throw new InvalidDataException("Invalid network orphaned policy.");
        }

        return orphaned;
    }

    private static void OnOrphaned(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost || !sender.IsHost)
        {
            return;
        }

        Guid id = reader.ReadGuid();
        uint generation = reader.ReadUInt32();
        NetworkOrphaned orphaned = ReadOrphaned(ref reader);
        if (!reader.IsAtEnd || delivery != Delivery.Reliable)
        {
            throw new InvalidDataException("Invalid orphaned policy message.");
        }

        if (s_state.Entries.TryGetValue(id, out NetworkEntry? entry) && entry.Generation == generation)
        {
            entry.Orphaned = orphaned;
            entry.GameObject.Network.ApplyOrphaned(orphaned);
        }
    }

    private static void ChangeOwner(NetworkEntry entry, int owner)
    {
        if (entry.Owner == owner)
        {
            return;
        }

        Float3 velocity = entry.TransferVelocity ?? OwnershipVelocity(entry);
        entry.TransferVelocity = null;
        SendOwnershipState(entry, velocity);
        entry.Owner = owner;
        entry.Generation = NextGeneration();
        entry.TransformSync.Reset();
        ControlChanged(entry.GameObject, velocity);

        using NetWriter message = new(32);
        message.WriteGuid(entry.GameObject.Identifier);
        message.WriteVarUInt32((uint)(owner + 1));
        message.WriteUInt32(entry.Generation);
        BroadcastEngine(EngineMessage.Owner, message.WrittenSpan);
        NotifyOwnership(entry);
    }

    private static void OnOwner(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost)
        {
            return;
        }

        Guid id = reader.ReadGuid();
        int owner = (int)reader.ReadVarUInt32() - 1;
        uint generation = reader.ReadUInt32();
        if (s_state.Entries.TryGetValue(id, out NetworkEntry? entry))
        {
            Float3 velocity = entry.TransferVelocity ?? SampledVelocity(entry);
            entry.TransferVelocity = null;
            entry.Owner = owner;
            entry.Generation = generation;
            entry.TransformSync.Reset();
            ControlChanged(entry.GameObject, velocity);
            NotifyOwnership(entry);
        }
    }

    private static void OnPeerLeft(NetPeer peer, SessionEndReason reason)
    {
        NetSession? session = Session;
        s_state.PeerReadiness.Remove(peer.Id);
        DispatchNetworkListener(e => e.OnPeerDisconnected(peer, reason));
        if (Session != session)
        {
            return;
        }

        List<NetworkEntry> orphans = new();
        foreach (NetworkEntry entry in s_state.Entries.Values)
        {
            if (entry.Owner == peer.Id)
            {
                orphans.Add(entry);
            }
        }

        foreach (NetworkEntry entry in orphans)
        {
            if (Session != session || !IsActive)
            {
                break;
            }

            if (FindEntry(entry.GameObject) != entry)
            {
                continue;
            }

            if (entry.Orphaned == NetworkOrphaned.Destroy && entry.GameObject.IsValid())
            {
                entry.GameObject.Destroy();
            }
            else
            {
                ChangeOwner(entry, NoOwner);
            }
        }
    }

    /// <summary>What the session knows about one networked object (a <see cref="NetworkMode.Object" /> root).</summary>
    private sealed class NetworkEntry(GameObject gameObject)
    {
        public readonly HashSet<Component> AuthorityListeners = new();
        public readonly HashSet<Component> SpawnListeners = new();
        public readonly TransformSync TransformSync = new();
        public uint Generation;
        public bool HasLocalAuthority;

        /// <summary>The host-assigned number messages use for this object in place of its identifier.</summary>
        public uint NetId;
        public int NotifiedOwnerId = NoOwner;
        public NetworkOrphaned Orphaned;
        public int Owner = NoOwner;
        public bool SpawnNotified;
        public bool TearingDown;
        public Float3? TransferVelocity;
        public GameObject GameObject { get; } = gameObject;
    }

    /// <summary>The session's networked objects, by identifier and by <see cref="NetworkEntry.NetId" />.</summary>
    private sealed class EntryTable
    {
        private readonly Dictionary<Guid, NetworkEntry> _byId = new();
        private readonly Dictionary<uint, NetworkEntry> _byNetId = new();

        public int Count => _byId.Count;

        public IEnumerable<NetworkEntry> Values => _byId.Values;

        public void Add(NetworkEntry entry)
        {
            Remove(entry.GameObject.Identifier);
            _byId[entry.GameObject.Identifier] = entry;
            if (entry.NetId != 0)
            {
                _byNetId[entry.NetId] = entry;
            }
        }

        public bool Contains(Guid id) => _byId.ContainsKey(id);

        public bool TryGetValue(Guid id, [NotNullWhen(true)] out NetworkEntry? entry) => _byId.TryGetValue(id, out entry);

        public bool TryGetNet(uint netId, [NotNullWhen(true)] out NetworkEntry? entry) =>
            _byNetId.TryGetValue(netId, out entry);

        public bool Remove(Guid id) => Remove(id, out _);

        public bool Remove(Guid id, [NotNullWhen(true)] out NetworkEntry? entry)
        {
            if (!_byId.Remove(id, out entry))
            {
                return false;
            }

            if (_byNetId.TryGetValue(entry.NetId, out NetworkEntry? indexed) && indexed == entry)
            {
                _byNetId.Remove(entry.NetId);
            }

            return true;
        }

        public void RemoveWhere(Func<NetworkEntry, bool> predicate)
        {
            foreach (NetworkEntry entry in _byId.Values.Where(predicate).ToArray())
            {
                Remove(entry.GameObject.Identifier);
            }
        }

        public void Clear()
        {
            _byId.Clear();
            _byNetId.Clear();
        }
    }
}
