// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;
using Prowl.Runtime.Diagnostics;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Warp;

namespace Prowl.Runtime.Networking;

// Networked objects: which exist, who controls them, spawning and despawning.
internal sealed partial class NetworkSystem
{
    internal const int NoOwner = -1;
    internal uint _nextGeneration;

    internal uint NextGeneration() => checked(++_nextGeneration);

    internal uint NextNetId() => checked(++NextNetIdValue);

    /// <summary>The GameObject or Component with this identifier in the replicated scene, or null.</summary>
    internal EngineObject? FindByIdentifier(Guid id)
    {
        if (id == Guid.Empty || ReplicatedScene == null)
        {
            return null;
        }

        if (Spawning != null && Spawning.TryGetValue(id, out EngineObject? spawning))
        {
            return spawning;
        }

        if (Index.TryGetValue(id, out EngineObject? found) && IsLive(found, id))
        {
            return found;
        }

        if (IndexFrame == Frame)
        {
            return null;
        }

        IndexFrame = Frame;
        Index.Clear();
        foreach (GameObject go in ReplicatedScene.AllObjects)
        {
            IndexObject(go);
        }

        return Index.TryGetValue(id, out found) && IsLive(found, id) ? found : null;
    }

    internal void IndexObject(GameObject go)
    {
        Index[go.Identifier] = go;
        foreach (Component component in go.GetComponents<Component>())
        {
            Index[component.Identifier] = component;
        }
    }

    internal void IndexTree(GameObject root)
    {
        IndexObject(root);
        foreach (GameObject child in root.GetChildrenDeep())
        {
            IndexObject(child);
        }
    }

    internal bool IsLive(EngineObject obj, Guid id) => obj switch
    {
        GameObject go => !go.IsDisposed && go.Identifier == id && go.Scene == ReplicatedScene,
        Component c => !c.IsDisposed && c.Identifier == id && c.GameObject.IsValid() &&
                       c.GameObject.Scene == ReplicatedScene,
        _ => false
    };

    /// <summary>The networked object <paramref name="go" /> belongs to, if any.</summary>
    internal NetworkObject? FindEntry(GameObject go)
    {
        GameObject? root = NetworkAccessor.RootOf(go);
        return root != null && Entries.TryGetValue(root.Identifier, out NetworkObject? entry) &&
               entry.GameObject == root
            ? entry
            : null;
    }

    internal int OwnerOf(GameObject go) => FindEntry(go)?.Owner ?? NoOwner;

    internal uint GenerationOf(GameObject go) => FindEntry(go)?.Generation ?? 0;

    internal uint NetIdOf(GameObject go) => FindEntry(go)?.NetId ?? 0;

    internal bool IsNetworked(GameObject go) => IsActive && FindEntry(go) is { TearingDown: false };

    internal bool IsProxy(GameObject go) => IsActive && !HasControl(go);

    /// <summary>True if this peer drives <paramref name="go" />: it owns it, or it is the host and nobody does.</summary>
    internal bool HasControl(GameObject go)
    {
        if (!IsActive)
        {
            return true;
        }

        int owner = OwnerOf(go);
        return owner == NoOwner ? IsHost : owner == Session!.LocalPeer!.Id;
    }

    internal bool HasControl(Component component, SyncFlags flags) =>
        (flags & SyncFlags.FromHost) != 0 ? !IsActive || IsHost : HasControl(component.GameObject);

    internal NetPeer? GetOwner(GameObject go)
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
    internal void Spawn(GameObject go, NetPeer? owner, NetworkOrphaned orphaned)
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

            if (go.Scene != ReplicatedScene)
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
        if (Entries.TryGetValue(go.Identifier, out NetworkObject? existing) && existing.GameObject == go)
        {
            existing.ChangeOwner(owner?.Id ?? NoOwner);
            return;
        }

        NetworkObject entry =
            new(this, go) { Owner = owner?.Id ?? NoOwner, Generation = NextGeneration(), NetId = NextNetId() };
        Entries.Add(entry);
        IndexTree(go);

        using NetWriter message = new(4096);
        WriteSpawn(go, entry.Owner, entry.Orphaned, message, entry.Generation, entry.NetId);
        BroadcastEngine(EngineMessage.Spawn, message.WrittenSpan);
        entry.UpdatePhysics();
        entry.NotifySpawn();
    }

    /// <summary>A Spawn message: where the object goes, who owns it, the object itself and its [Sync] values.</summary>
    internal void WriteSpawn(GameObject go, int owner, NetworkOrphaned orphaned, NetWriter output,
        uint generation = 0, uint netId = 0)
    {
        using FrameStatScope stats = FrameStats.Network.Scope();
        EchoObject echo;
        NetworkSerialization.WritingSnapshot = true;
        try
        {
            // References out of the tree go by identifier and land on the client's own objects.
            echo = Serializer.Serialize(typeof(GameObject), go,
                new SerializationContext
                {
                    ExternalReferences =
                        new NetworkSerialization.SnapshotReferences(SceneReferenceResolver.ForTree(go))
                });
        }
        finally
        {
            NetworkSerialization.WritingSnapshot = false;
        }

        new SpawnMessage(go.Identifier, go.Parent.IsValid() ? go.Parent!.Identifier : Guid.Empty,
            owner, orphaned, generation, netId == 0 ? NextNetId() : netId, Compress(echo)).Write(output);

        List<GameObject> tree = [go, .. go.GetChildrenDeep()];
        Sync.WriteSyncState(output, tree);
    }

    internal void OnSpawn(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost || ReplicatedScene == null)
        {
            return;
        }

        NetSession? session = Session;
        Scene scene = ReplicatedScene;
        SpawnMessage message = SpawnMessage.Read(ref reader);
        Guid id = message.Identifier;
        Guid parentId = message.ParentId;
        int owner = message.OwnerId;
        NetworkOrphaned orphaned = message.Orphaned;
        uint generation = message.Generation, netId = message.NetId;
        EchoObject echo = Decompress(message.Snapshot);

        GameObject? go =
            GameObject.DeserializePreservingIdentifiers(echo, SceneReferenceResolver.ContextForLinking(scene));
        if (go.IsNotValid())
        {
            throw new InvalidDataException("The host sent a networked object that could not be read.");
        }

        bool attached = false;
        try
        {
            if (Session != session || ReplicatedScene != scene)
            {
                return;
            }

            if (go!.Identifier != id)
            {
                throw new InvalidDataException("A spawned object's identifier doesn't match its message.");
            }

            // Resolve initial Sync references within the new tree before enabling its components.
            Spawning = new Dictionary<Guid, EngineObject>();
            try
            {
                foreach (GameObject part in (List<GameObject>)[go, .. go.GetChildrenDeep()])
                {
                    Spawning[part.Identifier] = part;
                    foreach (Component component in part.GetComponents<Component>())
                    {
                        Spawning[component.Identifier] = component;
                    }
                }

                Sync.ApplySyncState(sender, ref reader, null, true, true);
            }
            finally
            {
                Spawning = null;
            }

            if (Session != session || ReplicatedScene != scene)
            {
                return;
            }

            NetworkObject entry = new(this, go) { Owner = owner, Generation = generation, NetId = netId };
            go.Network.ApplyOrphaned(orphaned);
            Entries.Add(entry);
            entry.UpdatePhysics();
            if (FindByIdentifier(parentId) is GameObject parent)
            {
                go.SetParent(parent, false);
            }
            else
            {
                scene.Add(go);
            }

            attached = true;
            if (Session != session || ReplicatedScene != scene)
            {
                return;
            }

            IndexTree(go);
            entry.NotifySpawn();
        }
        finally
        {
            // Scene/parent ownership starts only after successful attachment. Failed metadata,
            // Sync state, or attachment must release components and any resources they own.
            if (!attached)
            {
                if (Entries.TryGetValue(go!.Identifier, out NetworkObject? pending) && pending.GameObject == go)
                {
                    Entries.Remove(go.Identifier);
                }

                go.Dispose();
            }
        }
    }

    /// <summary>A GameObject was disposed. On the host, a networked object going away goes away everywhere.</summary>
    internal void OnGameObjectDisposed(GameObject go)
    {
        if (!IsHost || !Entries.TryGetValue(go.Identifier, out NetworkObject? entry) || entry.GameObject != go)
        {
            return;
        }

        using FrameStatScope stats = FrameStats.Network.Scope();
        Entries.Remove(go.Identifier);

        // A scene being swapped out is disabled first. Its objects aren't destroyed, they're replaced by the next snapshot.
        if (go.Scene is not { IsDisposed: false, IsActive: true } || !IsActive)
        {
            return;
        }

        using NetWriter message = new(32);
        new DespawnMessage(go.Identifier).Write(message);
        BroadcastEngine(EngineMessage.Despawn, message.WrittenSpan);
    }

    internal void OnDespawn(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost)
        {
            return;
        }

        Guid id = DespawnMessage.Read(ref reader).Identifier;
        if (Entries.Remove(id, out NetworkObject? entry) && entry.GameObject.IsValid())
        {
            entry.NotifyDespawn();
            entry.GameObject.Destroy();
        }
    }

    // --- Ownership --------------------------------------------------------------------------------

    internal void SetOwner(GameObject go, NetPeer? owner)
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

        if (!Entries.TryGetValue(go.Identifier, out NetworkObject? entry) || entry.GameObject != go ||
            entry.TearingDown)
        {
            throw new InvalidOperationException($"'{go.Name}' is not a spawned networked object.");
        }

        using FrameStatScope stats = FrameStats.Network.Scope();
        entry.ChangeOwner(owner?.Id ?? NoOwner);
    }

    internal void ValidateOwner(NetPeer? owner)
    {
        if (owner != null && (!Session!.TryGetPeer(owner.Id, out NetPeer? current) || !ReferenceEquals(current, owner)))
        {
            throw new ArgumentException("The owner must be a peer from the current session.", nameof(owner));
        }
    }

    internal void SetOrphaned(GameObject go, NetworkOrphaned orphaned)
    {
        if (!IsActive || FindEntry(go) is not { TearingDown: false } entry || entry.GameObject != go)
        {
            return;
        }

        if (!IsHost)
        {
            throw new InvalidOperationException("Only the host can change a spawned object's orphaned policy.");
        }

        using NetWriter message = new();
        new OrphanedMessage(go.Identifier, entry.Generation, orphaned).Write(message);
        BroadcastEngine(EngineMessage.Orphaned, message.WrittenSpan);
    }


    internal void OnOrphaned(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost || !sender.IsHost)
        {
            return;
        }

        OrphanedMessage message = OrphanedMessage.Read(ref reader);
        Guid id = message.Identifier;
        uint generation = message.Generation;
        NetworkOrphaned orphaned = message.Orphaned;
        if (!reader.IsAtEnd || delivery != Delivery.Reliable)
        {
            throw new InvalidDataException("Invalid orphaned policy message.");
        }

        if (Entries.TryGetValue(id, out NetworkObject? entry) && entry.Generation == generation)
        {
            entry.GameObject.Network.ApplyOrphaned(orphaned);
        }
    }

    internal void OnOwner(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost)
        {
            return;
        }

        OwnerMessage message = OwnerMessage.Read(ref reader);
        Guid id = message.Identifier;
        int owner = message.OwnerId;
        uint generation = message.Generation;
        if (Entries.TryGetValue(id, out NetworkObject? entry))
        {
            Float3 velocity = entry.TransferVelocity ?? entry.SampledVelocity();
            entry.ApplyOwner(owner, generation, velocity);
            entry.NotifyOwnership();
        }
    }

    internal void OnPeerLeft(NetPeer peer, SessionEndReason reason)
    {
        NetSession? session = Session;
        LoadingPeers.Remove(peer.Id);
        DispatchNetworkListener(e => e.OnPeerDisconnected(peer, reason));
        if (Session != session)
        {
            return;
        }

        List<NetworkObject> orphans = new();
        foreach (NetworkObject entry in Entries.Values)
        {
            if (entry.Owner == peer.Id)
            {
                orphans.Add(entry);
            }
        }

        foreach (NetworkObject entry in orphans)
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
                entry.ChangeOwner(NoOwner);
            }
        }
    }
}
