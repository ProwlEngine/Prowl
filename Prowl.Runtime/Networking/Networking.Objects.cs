// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;
using Prowl.Runtime.Diagnostics;
using Prowl.Vector;
using Prowl.Warp;

namespace Prowl.Runtime.Networking;

// Networked objects: which exist, who controls them, spawning and despawning.
public static partial class Networking
{
    private const int NoOwner = -1;
    private static uint s_nextGeneration;

    private static readonly EntryTable s_entries = new();

    // Identifier lookups for messages, over the replicated scene. Filled on demand and rebuilt on a miss,
    // at most once a frame, so a stream of unknown ids can't make every lookup walk the whole scene.
    private static readonly Dictionary<Guid, EngineObject> s_index = new();
    private static int s_indexFrame = -1;

    // The object a spawn is building, by identifier, until it joins the scene.
    private static Dictionary<Guid, EngineObject>? s_spawning;

    private static uint NextGeneration() => checked(++s_nextGeneration);

    /// <summary>The GameObject or Component with this identifier in the replicated scene, or null.</summary>
    internal static EngineObject? FindByIdentifier(Guid id)
    {
        if (id == Guid.Empty || s_scene == null)
        {
            return null;
        }

        if (s_spawning != null && s_spawning.TryGetValue(id, out EngineObject? spawning))
        {
            return spawning;
        }

        if (s_index.TryGetValue(id, out EngineObject? found) && IsLive(found, id))
        {
            return found;
        }

        if (s_indexFrame == s_frame)
        {
            return null;
        }

        s_indexFrame = s_frame;
        s_index.Clear();
        foreach (GameObject go in s_scene.AllObjects)
        {
            Index(go);
        }

        return s_index.TryGetValue(id, out found) && IsLive(found, id) ? found : null;
    }

    private static void Index(GameObject go)
    {
        s_index[go.Identifier] = go;
        foreach (Component component in go.GetComponents<Component>())
        {
            s_index[component.Identifier] = component;
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
        GameObject go => !go.IsDisposed && go.Identifier == id && go.Scene == s_scene,
        Component c => !c.IsDisposed && c.Identifier == id && c.GameObject.IsValid() && c.GameObject.Scene == s_scene,
        _ => false
    };

    /// <summary>The networked object <paramref name="go" /> belongs to, if any.</summary>
    private static NetworkEntry? FindEntry(GameObject go)
    {
        GameObject? root = go.NetworkRoot;
        return root != null && s_entries.TryGetValue(root.Identifier, out NetworkEntry? entry) &&
               entry.GameObject == root
            ? entry
            : null;
    }

    private static int OwnerOf(GameObject go) => FindEntry(go)?.Owner ?? NoOwner;

    internal static bool IsNetworked(GameObject go) => IsActive && FindEntry(go) != null;

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
        if (!IsActive)
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
        if (IsActive)
        {
            if (!IsHost)
            {
                throw new InvalidOperationException("Only the host can spawn networked objects.");
            }

            if (go.Scene != s_scene)
            {
                throw new InvalidOperationException(
                    $"'{go.Name}' has to be in the current scene before it can be spawned.");
            }

            if (go.IsNetworkExcluded)
            {
                throw new InvalidOperationException(
                    $"'{go.Name}' is under an object that is NetworkMode.Never, so it can't be networked.");
            }

            // Asked of the parent, since this object may be marked NetworkMode.Object already.
            if (go.Parent.IsValid() && go.Parent!.NetworkRoot is { } outer)
            {
                throw new InvalidOperationException(
                    $"'{go.Name}' is inside the networked object '{outer.Name}' already.");
            }
        }

        go.NetworkMode = NetworkMode.Object;
        go.NetworkOrphaned = orphaned;
        if (!IsActive)
        {
            return;
        }

        using FrameStatScope stats = FrameStats.Network.Scope();
        if (s_entries.TryGetValue(go.Identifier, out NetworkEntry? existing) && existing.GameObject == go)
        {
            existing.Orphaned = orphaned;
            ChangeOwner(existing, owner?.Id ?? NoOwner);
            return;
        }

        NetworkEntry entry =
            new(go) { Owner = owner?.Id ?? NoOwner, Orphaned = orphaned, Generation = NextGeneration() };
        s_entries.Add(entry);
        IndexTree(go);

        using NetWriter message = new(4096);
        WriteSpawn(go, entry.Owner, entry.Orphaned, message, entry.Generation);
        BroadcastEngine(EngineMessage.Spawn, message.WrittenSpan);
        ControlChanged(go);
    }

    /// <summary>A Spawn message: where the object goes, who owns it, the object itself and its [Sync] values.</summary>
    internal static void WriteSpawn(GameObject go, int owner, NetworkOrphaned orphaned, NetWriter output,
        uint generation = 0)
    {
        using FrameStatScope stats = FrameStats.Network.Scope();
        EchoObject echo;
        GameObject.WritingNetworkSnapshot = true;
        try
        {
            // References out of the tree go by identifier and land on the client's own objects.
            echo = Serializer.Serialize(typeof(GameObject), go, SceneReferenceResolver.ContextForTree(go));
        }
        finally
        {
            GameObject.WritingNetworkSnapshot = false;
        }

        output.WriteGuid(go.Identifier);
        output.WriteGuid(go.Parent.IsValid() ? go.Parent!.Identifier : Guid.Empty);
        output.WriteVarUInt32((uint)(owner + 1));
        output.WriteByte((byte)orphaned);
        output.WriteUInt32(generation);
        output.WriteBytesWithLength(Compress(echo));

        List<GameObject> tree = [go, .. go.GetChildrenDeep()];
        WriteSyncState(output, tree);
    }

    private static void OnSpawn(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost || s_scene == null)
        {
            return;
        }

        Guid id = reader.ReadGuid();
        Guid parentId = reader.ReadGuid();
        int owner = (int)reader.ReadVarUInt32() - 1;
        NetworkOrphaned orphaned = (NetworkOrphaned)reader.ReadByte();
        uint generation = reader.ReadUInt32();
        EchoObject echo = Decompress(reader.ReadBytesWithLength());

        GameObject? go =
            GameObject.DeserializePreservingIdentifiers(echo, SceneReferenceResolver.ContextForLinking(s_scene));
        if (go.IsNotValid())
        {
            throw new InvalidDataException("The host sent a networked object that could not be read.");
        }

        if (go!.Identifier != id)
        {
            throw new InvalidDataException("A spawned object's identifier doesn't match its message.");
        }

        // Its [Sync] values go in before it joins the scene, so its components are enabled with them. Until
        // then, what it's made of is found by identifier from here, for values that refer to its own parts.
        s_spawning = new Dictionary<Guid, EngineObject>();
        foreach (GameObject part in (List<GameObject>)[go, .. go.GetChildrenDeep()])
        {
            s_spawning[part.Identifier] = part;
            foreach (Component component in part.GetComponents<Component>())
            {
                s_spawning[component.Identifier] = component;
            }
        }

        try
        {
            ApplySyncState(sender, ref reader, null, true);
        }
        finally
        {
            s_spawning = null;
        }

        if (FindByIdentifier(parentId) is GameObject parent)
        {
            go.SetParent(parent, false);
        }
        else
        {
            s_scene.Add(go);
        }

        s_entries.Add(new NetworkEntry(go) { Owner = owner, Orphaned = orphaned, Generation = generation });
        IndexTree(go);
        ControlChanged(go);
    }

    /// <summary>A GameObject was disposed. On the host, a networked object going away goes away everywhere.</summary>
    internal static void OnGameObjectDisposed(GameObject go)
    {
        if (!IsHost || !s_entries.TryGetValue(go.Identifier, out NetworkEntry? entry) || entry.GameObject != go)
        {
            return;
        }

        using FrameStatScope stats = FrameStats.Network.Scope();
        s_entries.Remove(go.Identifier);

        // A scene being swapped out is disabled first. Its objects aren't destroyed, they're replaced by the next snapshot.
        if (go.Scene is not { IsActive: true } || !IsActive)
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
        if (s_entries.Remove(id, out NetworkEntry? entry) && entry.GameObject.IsValid())
        {
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

        if (!s_entries.TryGetValue(go.Identifier, out NetworkEntry? entry) || entry.GameObject != go)
        {
            throw new InvalidOperationException($"'{go.Name}' is not a spawned networked object.");
        }

        using FrameStatScope stats = FrameStats.Network.Scope();
        ChangeOwner(entry, owner?.Id ?? NoOwner);
    }

    private static void ChangeOwner(NetworkEntry entry, int owner)
    {
        if (entry.Owner == owner)
        {
            return;
        }

        Float3 velocity = SampledVelocity(entry);
        entry.Owner = owner;
        entry.Generation = NextGeneration();
        entry.HasSent = false;
        entry.HasReceived = false;
        entry.Samples.Clear();
        entry.Timeline.Clear();
        ControlChanged(entry.GameObject, velocity);

        using NetWriter message = new(32);
        message.WriteGuid(entry.GameObject.Identifier);
        message.WriteVarUInt32((uint)(owner + 1));
        message.WriteUInt32(entry.Generation);
        BroadcastEngine(EngineMessage.Owner, message.WrittenSpan);
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
        if (s_entries.TryGetValue(id, out NetworkEntry? entry))
        {
            Float3 velocity = SampledVelocity(entry);
            entry.Owner = owner;
            entry.Generation = generation;
            entry.HasSent = false;
            entry.HasReceived = false;
            entry.Samples.Clear();
            entry.Timeline.Clear();
            ControlChanged(entry.GameObject, velocity);
        }
    }

    private static void OnPeerLeft(NetPeer peer, SessionEndReason reason)
    {
        List<NetworkEntry> orphans = new();
        foreach (NetworkEntry entry in s_entries.Values)
        {
            if (entry.Owner == peer.Id)
            {
                orphans.Add(entry);
            }
        }

        foreach (NetworkEntry entry in orphans)
        {
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
        public readonly List<TransformSample> Samples = new();
        public readonly TransformTimeline Timeline = new();
        public uint Generation;
        public bool HasReceived;

        // Transform sync: what this side last sent, and what it has received from whoever controls it.
        public bool HasSent;
        public uint LastReceivedTick;
        public double LastSentTime;
        public NetworkOrphaned Orphaned;
        public int Owner = NoOwner;
        public Float3 SentPosition;
        public Quaternion SentRotation;
        public Float3 SentScale;
        public GameObject GameObject { get; } = gameObject;
    }

    private sealed class EntryTable : Dictionary<Guid, NetworkEntry>
    {
        public void Add(NetworkEntry entry) => this[entry.GameObject.Identifier] = entry;

        public bool Contains(Guid id) => ContainsKey(id);

        public void RemoveWhere(Func<NetworkEntry, bool> predicate)
        {
            List<Guid>? doomed = null;
            foreach ((Guid id, NetworkEntry entry) in this)
            {
                if (predicate(entry))
                {
                    (doomed ??= new List<Guid>()).Add(id);
                }
            }

            if (doomed != null)
            {
                foreach (Guid id in doomed)
                {
                    Remove(id);
                }
            }
        }
    }
}
