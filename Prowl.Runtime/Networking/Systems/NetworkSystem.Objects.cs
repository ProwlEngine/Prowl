// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

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

    // An object that is not networked is nobody else's to drive.
    internal bool IsProxy(GameObject go) => IsActive && FindEntry(go) is { } entry && !HasControl(entry);

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

    /// <summary>The same for a networked object this peer has, without looking it up.</summary>
    internal bool HasControl(NetworkObject entry) =>
        !IsActive || (entry.Owner == NoOwner ? IsHost : entry.Owner == Session!.LocalPeer!.Id);

    /// <summary>Whether this peer may set a [Sync] member with these flags. One on an object nobody else has is always its own.</summary>
    internal bool HasControl(Component component, SyncFlags flags) =>
        !IsActive || NetworkAccessor.IsExcludedFromNetwork(component.GameObject) ||
        FindEntry(component.GameObject) is not { } entry ||
        ((flags & SyncFlags.FromOwner) == 0 ? IsHost : HasControl(entry));

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
    ///     is checked before anything changes. One that is spawned already is left as it is, with a warning, as
    ///     Mirror's Spawn does.
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

            Sync.RefuseWhileWriting("An object can't be spawned");
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

            if (go.Section is { } section)
            {
                EnsureAnnounced(section);
            }
        }

        if (IsActive && Entries.TryGetValue(go.Identifier, out NetworkObject? existing) && existing.GameObject == go)
        {
            Debug.LogWarning($"'{go.Name}' is spawned already. AssignOwnership changes who owns it.");
            return;
        }

        if (IsActive && (template ??= BuiltTemplateOf(go)) == null)
        {
            throw new InvalidOperationException(
                $"'{go.Name}' is not a prefab instance, was not built by a factory and is not stored in a scene asset, " +
                "so clients have nothing to build it from. Spawn it with SpawnPrefab or SpawnFactory.");
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
        NetworkObject entry = new(this, go)
        {
            Owner = owner?.Id ?? NoOwner, Generation = NextGeneration(), NetId = NextNetId(), Template = template!,
            Components = components
        };
        entry.SetTransforms(template!.Transforms ?? NetworkComponents.GatherTransforms(go));
        Entries.Add(entry);
        ForgetViewpoints(entry.Owner);
        InitVisibility(entry);

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

            Sync.RefuseWhileWriting("An object can't be spawned");
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
        NetworkTransform[] transforms = NetworkComponents.GatherTransforms(go);
        SceneInstance.Main.Add(go);
        Spawn(go, owner, orphaned ?? go.Network.Orphaned,
            new SpawnTemplate(prefab, factory, data, [.. identifiers], components, false, transforms));
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
    ///     A template for an object built outside a session: by a factory, or as an instance of a prefab, which
    ///     GameObject.Instantiate gives too. Null for anything else.
    /// </summary>
    private static SpawnTemplate? BuiltTemplateOf(GameObject go)
    {
        if (s_builtOffline.TryGetValue(go, out SpawnTemplate? built))
        {
            return built;
        }

        if (!go.IsPrefabInstance)
        {
            return null;
        }

        List<Guid> identifiers = new();
        CollectIdentifiers(go, identifiers);
        return new SpawnTemplate(go.PrefabAssetId, null, [], [.. identifiers], NetworkComponents.Gather(go));
    }

    /// <summary>The spawn of <paramref name="entry" />: what every peer builds it from, and its root's state now.</summary>
    private static TemplateSpawn TemplateSpawnOf(NetworkObject entry)
    {
        SpawnTemplate template = entry.Template;
        Transform t = entry.GameObject.Transform;
        return new TemplateSpawn(template.Prefab, template.Factory, template.Data, template.Identifiers, t.LocalPosition,
            t.LocalRotation, t.LocalScale, entry.GameObject.Enabled,
            (byte)template.Components.Length, NetworkComponents.Hash(template.Components),
            template.Scene);
    }

    /// <summary>Builds a client's copy of a prefab or factory spawn, before it goes into the scene.</summary>
    private GameObject BuildTemplate(Guid id, ReadOnlySpan<byte> payload, out SpawnTemplate template)
    {
        NetReader reader = new(payload);
        TemplateSpawn spawn = TemplateSpawn.Read(ref reader);
        if (!reader.IsAtEnd)
        {
            throw new InvalidDataException("A template spawn contains trailing bytes.");
        }

        GameObject go;
        string source;
        if (spawn.Scene)
        {
            source = $"scene object {id}";
            if (TakeHeld(id) is not { } held)
            {
                throw new InvalidDataException(
                    $"The host spawned the {source}, which this peer's copy of the scene does not hold.");
            }

            go = held;
        }
        else if (spawn.Factory is { } factory)
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

        template = new SpawnTemplate(spawn.Prefab, spawn.Factory, spawn.Data, spawn.Identifiers, components, spawn.Scene);

        go.Transform.LocalPosition = spawn.Position;
        go.Transform.LocalRotation = spawn.Rotation;
        go.Transform.LocalScale = spawn.Scale;
        go.Enabled = spawn.Enabled;
        go.Network.Networked = true;
        return go;
    }

    /// <summary>A Spawn message: where the object goes, who owns it, what builds it and its [Sync] values.</summary>
    internal void WriteSpawn(NetworkObject entry, NetWriter output, bool replace = false)
    {
        using FrameStatScope stats = FrameStats.Network.Scope();
        GameObject go = entry.GameObject;
        Guid parent = go.Parent.IsValid() ? go.Parent!.Identifier : Guid.Empty;
        TemplateSpawn built = TemplateSpawnOf(entry);
        using NetWriter payload = new(64 + built.Identifiers.Length * 16 + built.Data.Length);
        built.Write(payload);
        new SpawnMessage(go.Identifier, parent, SectionSlot(go), entry.Owner, entry.Orphaned, entry.Generation,
            entry.NetId, payload.WrittenSpan, replace, Tick).Write(output);
        WriteChildPoses(entry, output);
        Sync.WriteSpawnState(output, entry.Components);
    }

    // Each transform that moves a child sends where that child is, as Mirror's NetworkTransform writes its target
    // into a spawn. The root's own pose is in the template spawn.
    private static void WriteChildPoses(NetworkObject entry, NetWriter output)
    {
        NetworkTransform[] transforms = entry.Transforms;
        output.WriteByte((byte)transforms.Length);
        foreach (NetworkTransform transform in transforms)
        {
            bool child = transform.IsValid() && !ReferenceEquals(transform.Moved, entry.GameObject);
            output.WriteBool(child);
            if (child)
            {
                Transform t = transform.Moved.Transform;
                NetSerializers.WriteFloat3(output, t.LocalPosition);
                NetSerializers.WriteQuaternion(output, t.LocalRotation);
                NetSerializers.WriteFloat3(output, t.LocalScale);
            }
        }
    }

    /// <summary>
    ///     A spawn for an object this peer has already, as Mirror's FindOrSpawnObject finds one by its net id: it keeps
    ///     the object and takes the spawn's pose, owner and state, with no spawn callbacks.
    /// </summary>
    private void Respawn(NetworkObject entry, scoped SpawnMessage message, NetPeer sender, ref NetReader reader)
    {
        NetReader payload = new(message.Payload);
        TemplateSpawn spawn = TemplateSpawn.Read(ref payload);
        GameObject go = entry.GameObject;
        go.Transform.LocalPosition = spawn.Position;
        go.Transform.LocalRotation = spawn.Rotation;
        go.Transform.LocalScale = spawn.Scale;
        go.Enabled = spawn.Enabled;
        ReadChildPoses(ref reader, entry.Transforms);
        Sync.ApplySpawnState(sender, entry.Components, ref reader);
        entry.AwaitingRespawn = false;
        go.Network.ApplyOrphaned(message.Orphaned);
        foreach (NetworkTransform transform in entry.Transforms)
        {
            transform.Sync.ClearHistory();
            transform.Sync.ReceivedAt(message.Tick);
        }

        if (entry.Owner != message.OwnerId)
        {
            entry.ApplyOwner(message.OwnerId, message.Generation, entry.SampledVelocity());
            entry.NotifyOwnership();
        }
        else
        {
            entry.Generation = message.Generation;
        }
    }

    private static void ReadChildPoses(ref NetReader reader, NetworkTransform[] transforms)
    {
        int count = reader.ReadByte();
        if (count != transforms.Length)
        {
            throw new InvalidDataException(
                $"A spawn names {count} NetworkTransforms and this peer's object has {transforms.Length}. Every peer " +
                "has to build the same ones.");
        }

        foreach (NetworkTransform transform in transforms)
        {
            if (reader.ReadBool())
            {
                Float3 position = NetSerializers.ReadFloat3(ref reader);
                Quaternion rotation = NetSerializers.ReadQuaternion(ref reader);
                Float3 scale = NetSerializers.ReadFloat3(ref reader);
                NetworkValidation.Transform(0, position, rotation, scale);
                transform.Apply(position, rotation, scale);
            }
        }
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
        if (Entries.TryGetValue(id, out NetworkObject? current) && current.NetId == netId && current.GameObject.IsValid())
        {
            Respawn(current, message, sender, ref reader);
            return;
        }

        GameObject go = BuildTemplate(id, message.Payload, out SpawnTemplate template);
        bool attached = false, adopted = false;
        Component[] components = [];
        NetworkTransform[] transforms = [];
        try
        {
            if (Session != session || ReplicatedWorld != scene)
            {
                return;
            }

            if (go.Identifier != id)
            {
                throw new InvalidDataException("A spawned object's identifier doesn't match its message.");
            }

            if (message.Replace)
            {
                ReplaceCopy(id, go);
                if (Session != session || ReplicatedWorld != scene)
                {
                    return;
                }
            }

            // An object this peer holds already stays the same instance, so references to it keep working. One on its
            // way out, despawned this frame, makes room for the new copy instead.
            if (!Entries.Contains(id) && FindByIdentifier(id) is GameObject existing && existing != go)
            {
                if (existing.IsDestroyQueued)
                {
                    existing.Dispose();
                }
                else
                {
                    existing.Network.Networked = true;
                    go.Dispose();
                    go = existing;
                    adopted = true;
                }
            }

            // What the new tree holds, by identifier, for the references its state names.
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

                components = adopted ? NetworkComponents.Gather(go) : template.Components;
                transforms = NetworkComponents.GatherTransforms(go);
                ReadChildPoses(ref reader, transforms);
                Sync.ApplySpawnState(sender, components, ref reader);
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
                Owner = owner, Generation = generation, NetId = netId,
                Template = adopted ? template with { Components = components } : template, Components = components
            };
            entry.SetTransforms(transforms);

            go.Network.ApplyOrphaned(orphaned);
            Entries.Add(entry);
            Sync.ResolveReferencesTo(entry);
            if (message.Replace)
            {
                foreach (NetworkTransform transform in entry.Transforms)
                {
                    transform.Sync.ReceivedAt(message.Tick);
                }
            }
            entry.UpdatePhysics();
            if (FindByIdentifier(parentId) is GameObject parent)
            {
                if (go.Parent != parent)
                {
                    go.SetParent(parent, false);
                }
            }
            else if (!adopted && go.Scene.IsNotValid())
            {
                scene.Add(go, message.SectionSlot == 0 ? null : SectionOf(message.SectionSlot - 1));
            }

            attached = true;
            if (Session != session || ReplicatedWorld != scene)
            {
                return;
            }

            NotifyOrHold(entry);
        }
        finally
        {
            // Scene/parent ownership starts only after successful attachment. Failed metadata,
            // Sync state, or attachment must release components and any resources they own.
            if (!attached)
            {
                if (Entries.TryGetValue(go.Identifier, out NetworkObject? pending) && pending.GameObject == go)
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
    private void ReplaceCopy(Guid id, GameObject incoming)
    {
        if (Entries.Remove(id, out NetworkObject? stale))
        {
            stale.NotifyDespawn(NetworkDespawnReason.OutOfInterest);
        }

        if (FindByIdentifier(id) is GameObject copy && copy != incoming)
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

    // The factory behind each object built outside a session, for the session that later hosts it.
    private static readonly ConditionalWeakTable<GameObject, SpawnTemplate> s_builtOffline = new();

    /// <summary>Notes the factory that built <paramref name="go" /> outside a session, as built, before the scene runs anything on it.</summary>
    internal static void RememberFactory(GameObject go, string factory, byte[] data)
    {
        List<Guid> identifiers = new();
        CollectIdentifiers(go, identifiers);
        s_builtOffline.AddOrUpdate(go, new SpawnTemplate(Guid.Empty, factory, data, [.. identifiers],
            NetworkComponents.Gather(go), false, NetworkComponents.GatherTransforms(go)));
    }

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
        _despawnedTemplates.AddOrUpdate(go, entry.Template);

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
                Transforms.Withdraw(peer, go);
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
            entry.NotifyDespawn(message.Reason);
            if (entry.Template.Scene)
            {
                Hold(entry.GameObject);
            }
            else if (entry.Template.Factory is { } factory && NetworkFactories.TryGet(factory, out _, out Action<GameObject>? destroy) &&
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

        Sync.RefuseWhileWriting("An object's owner can't be changed");
        ValidateOwner(owner);

        if (!Entries.TryGetValue(go.Identifier, out NetworkObject? entry) || entry.GameObject != go ||
            entry.TearingDown)
        {
            throw new InvalidOperationException($"'{go.Name}' is not a spawned networked object.");
        }

        if (PlayerPeerOf(entry) is { } player && player != (owner ?? Session!.LocalPeer!).Id)
        {
            throw new InvalidOperationException(
                $"'{go.Name}' is a player object, which stays its peer's while it is their player. Give that peer another player first.");
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
        Transforms.RemovePeer(peer.Id);
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
