// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

using Prowl.Echo;
using Prowl.Runtime.Diagnostics;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Courier;

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
        if (id == Guid.Empty || ReplicatedWorld.IsNotValid())
        {
            return null;
        }

        if (Spawning != null && Spawning.TryGetValue(id, out EngineObject? spawning))
        {
            return spawning;
        }

        return ReplicatedWorld!.FindJoined(id);
    }

    /// <summary>The networked object <paramref name="go" /> belongs to, if any.</summary>
    internal NetworkObject? FindEntry(GameObject go)
    {
        GameObject? root = NetworkAccessor.RootOf(go);
        return !ReferenceEquals(root, null) && Entries.TryGetValue(root.Identifier, out NetworkObject? entry) &&
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
        NetworkAccessor.IsExcludedFromNetwork(component.GameObject) ||
        ((flags & SyncFlags.FromOwner) == 0 ? !IsActive || IsHost : HasControl(component.GameObject));

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
    internal void Spawn(GameObject go, NetPeer? owner, NetworkOrphaned orphaned, SpawnTemplate? template = null)
    {
        if (template == null && _despawnedTemplates.TryGetValue(go, out SpawnTemplate? remembered))
        {
            template = remembered;
        }

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

            if (go.Scene != ReplicatedWorld)
            {
                throw new InvalidOperationException(
                    $"'{go.Name}' has to be in the networked world, SceneInstance.Main, before it can be spawned.");
            }

            if (NetworkAccessor.IsExcludedFromNetwork(go))
            {
                throw new InvalidOperationException(
                    $"'{go.Name}' is under a server only object, so it can't be networked.");
            }

            // Asked of the parent, since this object may be marked networked already.
            if (go.Parent.IsValid() && go.Parent!.Network.Root is { } outer)
            {
                throw new InvalidOperationException(
                    $"'{go.Name}' is inside the networked object '{outer.Name}' already.");
            }

            if (FindEntry(go) is null && HiddenFromItsCopies(go, owner?.Id ?? NoOwner))
            {
                throw new InvalidOperationException(
                    $"'{go.Name}' is on clients already as part of the scene, so it can't be spawned hidden from any of them. " +
                    "Spawn a new object instead, or make it networked in the scene.");
            }

            if (go.Section is { } section)
            {
                EnsureAnnounced(section);
            }
        }

        Component[] components = template?.Components ?? NetworkComponents.Gather(go);
        if (components.Length > NetworkComponents.MaxPerObject)
        {
            throw new InvalidOperationException(
                $"'{go.Name}' has {components.Length} networked components, and a networked object can hold " +
                $"{NetworkComponents.MaxPerObject}. Split it into several networked objects.");
        }

        go.Network.Networked = true;
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

        bool heldAsContent = Configuration.InterestMode != InterestMode.None && HeldAsContent(go);
        NetworkObject entry = new(this, go)
        {
            Owner = owner?.Id ?? NoOwner, Generation = NextGeneration(), NetId = NextNetId(), Template = template,
            Components = components
        };
        Entries.Add(entry);
        InitVisibility(entry, heldAsContent);

        List<NetPeer> observers = new();
        foreach (NetPeer peer in Session!.Peers)
        {
            if (!peer.IsLocal && Sees(entry, peer))
            {
                observers.Add(peer);
            }
        }

        SendSpawn(entry, observers, false);
        entry.UpdatePhysics();
        entry.NotifySpawn();
    }

    /// <summary>
    ///     Builds <paramref name="prefab" /> into the networked world at a pose and spawns it. Clients build their copy
    ///     from the same prefab, so only its GUID, identifiers, root state and [Sync] values travel.
    /// </summary>
    internal GameObject SpawnPrefab(PrefabAsset prefab, Float3 position, Quaternion rotation, NetPeer? owner,
        NetworkOrphaned? orphaned)
    {
        CheckHostSpawn(owner);
        GameObject? go = GameObject.InstantiateDetached(prefab);
        if (go.IsNotValid())
        {
            throw new ArgumentException($"'{prefab.Name}' could not be instantiated.", nameof(prefab));
        }

        go!.Transform.Position = position;
        go.Transform.Rotation = rotation;
        return SpawnBuilt(go, prefab.AssetID, null, [], owner, orphaned);
    }

    /// <summary>Builds an object with a registered factory and spawns it. Clients build theirs with the same factory.</summary>
    internal GameObject SpawnFactory(string factory, Float3 position, Quaternion rotation, byte[] data, NetPeer? owner,
        NetworkOrphaned? orphaned)
    {
        CheckHostSpawn(owner);
        return SpawnBuilt(BuildWithFactory(factory, position, rotation, data), Guid.Empty, factory, data, owner, orphaned);
    }

    /// <summary>Builds an object with a registered factory, refusing one the factory already put in a scene.</summary>
    internal static GameObject BuildWithFactory(string factory, Float3 position, Quaternion rotation, byte[] data)
    {
        if (!NetworkFactories.TryGet(factory, out NetworkFactory build, out _))
        {
            throw new ArgumentException($"No network factory is registered as '{factory}'.", nameof(factory));
        }

        GameObject go = build(new NetworkSpawn(factory, position, rotation, data));
        if (go.IsNotValid())
        {
            throw new InvalidOperationException($"The network factory '{factory}' returned no object.");
        }

        if (go.Scene.IsValid())
        {
            throw new InvalidOperationException(
                $"The network factory '{factory}' must return an object in no scene yet; networking adds it.");
        }

        return go;
    }

    private void CheckHostSpawn(NetPeer? owner)
    {
        if (IsActive)
        {
            if (!IsHost)
            {
                throw new InvalidOperationException("Only the host can spawn networked objects.");
            }

            ValidateOwner(owner);
        }
    }

    private GameObject SpawnBuilt(GameObject go, Guid prefab, string? factory, byte[] data, NetPeer? owner,
        NetworkOrphaned? orphaned)
    {
        List<Guid> identifiers = new();
        CollectIdentifiers(go, identifiers);
        // Gathered as built, before the scene runs anything on it, which is the tree clients build too.
        Component[] components = NetworkComponents.Gather(go);
        SceneInstance.Main.Add(go);
        Spawn(go, owner, orphaned ?? go.Network.Orphaned,
            new SpawnTemplate(prefab, factory, data, [.. identifiers], components));
        return go;
    }

    /// <summary>The identifiers of a tree in the order a prefab builds it: each object, its components, then its children.</summary>
    internal static void CollectIdentifiers(GameObject go, List<Guid> identifiers)
    {
        identifiers.Add(go.Identifier);
        foreach (Component component in go.GetComponents<Component>())
        {
            identifiers.Add(component.Identifier);
        }

        foreach (GameObject child in go.Children)
        {
            CollectIdentifiers(child, identifiers);
        }
    }

    private static bool AssignIdentifiers(GameObject go, Guid[] identifiers, ref int next)
    {
        if (next >= identifiers.Length)
        {
            return false;
        }

        go.SetIdentifier(identifiers[next++]);
        foreach (Component component in go.GetComponents<Component>())
        {
            if (next >= identifiers.Length)
            {
                return false;
            }

            component.Identifier = identifiers[next++];
        }

        foreach (GameObject child in go.Children)
        {
            if (!AssignIdentifiers(child, identifiers, ref next))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     The spawn of <paramref name="go" /> by prefab or factory, or null when it was built neither way or its tree
    ///     has changed since.
    /// </summary>
    private TemplateSpawn? TemplateSpawnOf(GameObject go)
    {
        if (FindEntry(go) is not { Template: { } template } entry || entry.GameObject != go)
        {
            return null;
        }

        List<Guid> current = new(template.Identifiers.Length);
        CollectIdentifiers(go, current);
        if (!current.SequenceEqual(template.Identifiers))
        {
            return null;
        }

        Transform t = go.Transform;
        return new TemplateSpawn(template.Prefab, template.Factory, template.Data, template.Identifiers, t.LocalPosition,
            t.LocalRotation, t.LocalScale, go.Enabled, go.Network.Flags,
            (byte)go.Network.SendInterval, (byte)template.Components.Length, NetworkComponents.Hash(template.Components));
    }

    /// <summary>Builds a client's copy of a prefab or factory spawn, before it goes into the scene.</summary>
    private static GameObject BuildTemplate(ReadOnlySpan<byte> payload, out SpawnTemplate template)
    {
        NetReader reader = new(payload);
        TemplateSpawn spawn = TemplateSpawn.Read(ref reader);
        if (!reader.IsAtEnd)
        {
            throw new InvalidDataException("A template spawn contains trailing bytes.");
        }

        GameObject go;
        string source;
        if (spawn.Factory is { } factory)
        {
            source = $"factory '{factory}'";
            if (!NetworkFactories.TryGet(factory, out _, out _))
            {
                throw new InvalidDataException($"The host spawned from the {source}, which this peer has not registered.");
            }

            go = BuildWithFactory(factory, spawn.Position, spawn.Rotation, spawn.Data);
        }
        else
        {
            source = $"prefab {spawn.Prefab}";
            PrefabAsset? prefab = AssetDatabase.Load<PrefabAsset>(spawn.Prefab);
            if (prefab.IsNotValid() || prefab!.GameObjectData == null)
            {
                throw new InvalidDataException($"The host spawned the {source}, which this build does not have.");
            }

            GameObject? built = NetworkPrefabHandlers.For(spawn.Prefab).Spawn is { } handler
                ? handler(prefab)
                : GameObject.InstantiateDetached(prefab);
            if (built.IsNotValid() || built!.Scene.IsValid())
            {
                throw new InvalidOperationException(
                    $"Building the {source} gave no object, or one already in a scene; networking adds it.");
            }

            go = built;
        }

        int next = 0;
        if (!AssignIdentifiers(go, spawn.Identifiers, ref next) || next != spawn.Identifiers.Length)
        {
            go.Dispose();
            throw new InvalidDataException($"The host's {source} builds a different tree than this peer's.");
        }

        Component[] components = NetworkComponents.Gather(go);
        if (components.Length != spawn.ComponentCount || NetworkComponents.Hash(components) != spawn.ComponentHash)
        {
            string mine = NetworkComponents.Describe(components);
            go.Dispose();
            throw new InvalidDataException(
                $"The host's {source} has {spawn.ComponentCount} networked components and this peer's builds " +
                $"{components.Length} that don't match them ({mine}). Every peer has to build the same components.");
        }

        template = new SpawnTemplate(spawn.Prefab, spawn.Factory, spawn.Data, spawn.Identifiers, components);

        go.Transform.LocalPosition = spawn.Position;
        go.Transform.LocalRotation = spawn.Rotation;
        go.Transform.LocalScale = spawn.Scale;
        go.Enabled = spawn.Enabled;
        go.Network.Networked = true;
        go.Network.ApplyFlags(spawn.Flags);
        go.Network.ApplySendInterval(spawn.SendInterval);
        return go;
    }

    /// <summary>A Spawn message: where the object goes, who owns it, the object itself and its [Sync] values.</summary>
    internal void WriteSpawn(GameObject go, int owner, NetworkOrphaned orphaned, NetWriter output,
        uint generation = 0, uint netId = 0, bool replace = false)
    {
        using FrameStatScope stats = FrameStats.Network.Scope();
        Guid parent = go.Parent.IsValid() ? go.Parent!.Identifier : Guid.Empty;
        if (TemplateSpawnOf(go) is { } built)
        {
            using NetWriter payload = new(64 + built.Identifiers.Length * 16 + built.Data.Length);
            built.Write(payload);
            new SpawnMessage(go.Identifier, parent, SectionSlot(go), owner, orphaned, generation,
                netId == 0 ? NextNetId() : netId, payload.WrittenSpan, replace, Tick, true).Write(output);
            Sync.WriteSyncState(output, [go, .. go.GetChildrenDeep()]);
            return;
        }

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

        new SpawnMessage(go.Identifier, parent, SectionSlot(go), owner, orphaned, generation, netId == 0 ? NextNetId() : netId, Compress(echo), replace, Tick).Write(output);

        List<GameObject> tree = [go, .. go.GetChildrenDeep()];
        Sync.WriteSyncState(output, tree);
    }

    internal void OnSpawn(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost || ReferenceEquals(ReplicatedWorld, null))
        {
            return;
        }

        NetSession? session = Session;
        SceneInstance scene = ReplicatedWorld;
        SpawnMessage message = SpawnMessage.Read(ref reader);
        Guid id = message.Identifier;
        Guid parentId = message.ParentId;
        int owner = message.OwnerId;
        NetworkOrphaned orphaned = message.Orphaned;
        uint generation = message.Generation, netId = message.NetId;
        EchoObject? echo = message.Template ? null : Decompress(message.Snapshot);
        SerializationContext context = SceneReferenceResolver.ContextForLinking(scene);
        SpawnTemplate? template = null;
        GameObject? go = echo == null
            ? BuildTemplate(message.Snapshot, out template)
            : GameObject.DeserializePreservingIdentifiers(echo, context);
        if (go.IsNotValid())
        {
            throw new InvalidDataException("The host sent a networked object that could not be read.");
        }

        bool attached = false, adopted = false;
        List<(Component Component, SyncMember Sync)>? applied = null;
        try
        {
            if (Session != session || ReplicatedWorld != scene)
            {
                return;
            }

            if (go!.Identifier != id)
            {
                throw new InvalidDataException("A spawned object's identifier doesn't match its message.");
            }

            if (message.Replace)
            {
                ReplaceCopy(id);
                if (Session != session || ReplicatedWorld != scene)
                {
                    return;
                }
            }

            // An object the scene snapshot brought stays the same instance, so references to it keep working.
            if (!Entries.Contains(id) && FindByIdentifier(id) is GameObject existing)
            {
                existing.Network.Networked = true;
                existing.Network.ApplyFlags(go.Network.Flags);
                existing.Network.ApplySendInterval(go.Network.SendInterval);
                go.Dispose();
                go = existing;
                adopted = true;
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

                applied = message.Replace ? [] : null;
                Sync.ApplySyncState(sender, ref reader, null, true, true, accepted: applied);
            }
            finally
            {
                Spawning = null;
            }

            if (Session != session || ReplicatedWorld != scene)
            {
                return;
            }

            NetworkObject entry = new(this, go)
            {
                Owner = owner, Generation = generation, NetId = netId, Template = adopted ? null : template,
                Components = adopted || template == null ? NetworkComponents.Gather(go) : template.Components
            };
            go.Network.ApplyOrphaned(orphaned);
            Entries.Add(entry);
            if (applied != null)
            {
                Snapshots.Received(sender, applied, message.Tick);
                entry.TransformSync.ReceivedAt(message.Tick);
            }
            entry.UpdatePhysics();
            if (FindByIdentifier(parentId) is GameObject parent)
            {
                if (go.Parent != parent)
                {
                    go.SetParent(parent, false);
                }
            }
            else if (!adopted)
            {
                scene.Add(go, message.SectionSlot == 0 ? null : SectionOf(message.SectionSlot - 1));
            }

            attached = true;
            if (Session != session || ReplicatedWorld != scene)
            {
                return;
            }

            FillLinks(go);
            if (!adopted && echo != null)
            {
                CollectLinks(echo, context);
            }

            entry.NotifySpawn();
            ClaimPendingPlayer(entry);
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

                if (!adopted)
                {
                    go.Dispose();
                }
            }
        }
    }

    /// <summary>Removes the copy of an object the host is sending again, even one still being destroyed.</summary>
    private void ReplaceCopy(Guid id)
    {
        if (Entries.Remove(id, out NetworkObject? stale))
        {
            stale.NotifyDespawn(NetworkDespawnReason.OutOfInterest);
        }

        if (FindByIdentifier(id) is GameObject copy)
        {
            copy.Dispose();
        }
    }

    internal void BeforeObjectDisposed(GameObject go)
    {
        if (Entries.TryGetValue(go.Identifier, out NetworkObject? entry) && entry.GameObject == go)
        {
            entry.NotifyDespawn(IsUnloading(go) || (IsClient && _applyingSections)
                ? NetworkDespawnReason.SectionUnloaded
                : NetworkDespawnReason.Destroyed);
        }
    }

    /// <summary>A GameObject was disposed. On the host, a networked object going away goes away everywhere.</summary>
    internal void OnGameObjectDisposed(GameObject go)
    {
        if (VisibilityOverrides.Count > 0)
        {
            VisibilityOverrides.Remove(go);
        }

        ForgetSentContent(go);
        ForgetLinksFrom(go);

        if (!Entries.TryGetValue(go.Identifier, out NetworkObject? entry) || entry.GameObject != go)
        {
            return;
        }

        using FrameStatScope stats = FrameStats.Network.Scope();
        Entries.Remove(go.Identifier);
        Restricted.Remove(entry);
        ForgetInRange(entry);

        // A section being unloaded, or a world being torn down, goes away on clients as a whole.
        if (!IsHost || !IsActive || go.Scene != ReplicatedWorld || ReplicatedWorld!.State != SceneState.Running ||
            IsUnloading(go))
        {
            return;
        }

        using NetWriter message = new(32);
        new DespawnMessage(go.Identifier).Write(message);
        SendToObservers(entry, EngineMessage.Despawn, message.WrittenSpan);
    }

    // What despawned objects were built from, so spawning one again sends it the same way.
    private readonly ConditionalWeakTable<GameObject, SpawnTemplate> _despawnedTemplates = new();

    /// <summary>Host: takes a spawned object out of the network everywhere, leaving this peer's object as it is.</summary>
    internal void Despawn(GameObject go)
    {
        if (!IsActive)
        {
            return;
        }

        if (!IsHost)
        {
            throw new InvalidOperationException("Only the host can despawn networked objects.");
        }

        if (FindEntry(go) is not { TearingDown: false } entry || entry.GameObject != go)
        {
            throw new InvalidOperationException($"'{go.Name}' is not a spawned networked object.");
        }

        using FrameStatScope stats = FrameStats.Network.Scope();
        if (entry.Template != null)
        {
            _despawnedTemplates.AddOrUpdate(go, entry.Template);
        }

        NetSession session = Session!;
        entry.NotifyDespawn();
        if (Session != session || FindEntry(go) != entry)
        {
            return;
        }

        using NetWriter message = new(32);
        new DespawnMessage(go.Identifier).Write(message);
        SendToObservers(entry, EngineMessage.Despawn, message.WrittenSpan);
        foreach (NetPeer peer in session.Peers)
        {
            if (!peer.IsLocal)
            {
                Snapshots.Withdraw(peer, go);
            }
        }

        Entries.Remove(go.Identifier);
        Restricted.Remove(entry);
        ForgetInRange(entry);
        VisibilityOverrides.Remove(go);
    }

    internal void OnDespawn(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost)
        {
            return;
        }

        DespawnMessage message = DespawnMessage.Read(ref reader);
        if (Entries.Remove(message.Identifier, out NetworkObject? entry) && entry.GameObject.IsValid())
        {
            ForgetLinksFrom(entry.GameObject);
            entry.NotifyDespawn(message.Reason);
            if (entry.Template?.Factory is { } factory && NetworkFactories.TryGet(factory, out _, out Action<GameObject>? destroy) &&
                destroy != null && entry.GameObject.IsValid())
            {
                destroy(entry.GameObject);
            }
            else if (entry.Template is { Factory: null } template &&
                     NetworkPrefabHandlers.For(template.Prefab).Unspawn is { } unspawn && entry.GameObject.IsValid())
            {
                unspawn(entry.GameObject);
            }
            else if (entry.GameObject.IsValid())
            {
                entry.GameObject.Destroy();
            }
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
        SendToObservers(entry, EngineMessage.Orphaned, message.WrittenSpan);
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
        Snapshots.RemovePeer(peer.Id);
        ForgetPeerVisibility(peer.Id);
        ForgetPeerLimits(peer.Id);
        ForgetPlayerOf(peer.Id);
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
