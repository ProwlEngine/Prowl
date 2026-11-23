// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Courier;
using Prowl.Runtime.Diagnostics;
using Prowl.Runtime.Resources;

namespace Prowl.Runtime.Networking;

/// <summary>A section of the host's world as the scene message names it: no content, only what to load.</summary>
internal readonly record struct SectionHeader(uint Id, string Name, bool Active, Guid Source)
{
    internal void Write(NetWriter writer)
    {
        writer.WriteVarUInt32(Id);
        writer.WriteString(Name);
        writer.WriteBool(Active);
        writer.WriteGuid(Source);
    }

    internal static SectionHeader Read(ref NetReader reader) => new(
        reader.ReadVarUInt32(),
        reader.ReadString(1024),
        reader.ReadBool(),
        reader.ReadGuid());
}

// Joining and changing scenes. The host names the scene assets its sections came from, each
// client loads them itself and says it is ready, and the host then spawns every networked object the client may
// have between a started and a finished message. No scene content travels: a section built in code is built by
// every peer itself, and what is networked in it is spawned from a prefab or a factory.
internal sealed partial class NetworkSystem
{
    internal const byte SceneMessageFormat = 13;
    private const uint MaxSections = 4096;

    // The scenes' own networked objects this client has that are not spawned: never sent to it, or despawned since.
    // They stay for the host to spawn until the scene changes.
    internal readonly Dictionary<Guid, GameObject> _held = new();

    // Objects spawned before this client was ready, whose callbacks wait for the host's finished message.
    private readonly List<NetworkObject> _pendingSpawns = new();

    // The trees of the networked objects stored in each scene asset, by root, read once a session.
    private readonly Dictionary<SceneAsset, Dictionary<Guid, Guid[]>> _assetTrees = new(ReferenceEqualityComparer.Instance);

    /// <summary>Starts <paramref name="peers" /> loading the host's scenes.</summary>
    internal void SendScene(params NetPeer[] peers)
    {
        if (Session is null || ReplicatedWorld.IsNotValid() || peers.Length == 0)
        {
            return;
        }

        foreach (NetPeer peer in peers)
        {
            BeginPeerLoad(peer);
        }

        using FrameStatScope stats = FrameStats.Network.Scope();
        using NetWriter message = new();
        WriteScene(ReplicatedWorld!, message);
        foreach (NetPeer peer in peers)
        {
            SendEngine(peer, EngineMessage.Scene, message.WrittenSpan);
        }
    }

    /// <summary>The scene message: the sections of <paramref name="world" /> and the scene asset each came from.</summary>
    internal void WriteScene(Scene world, NetWriter output)
    {
        output.WriteByte(SceneMessageFormat);
        output.WriteUInt64(Math.Max(SceneGeneration, 1));
        SceneSection[] sections = [.. world.Sections];
        output.WriteVarUInt32((uint)sections.Length);
        foreach (SceneSection section in sections)
        {
            HeaderOf(section).Write(output);
        }
    }

    private SectionHeader HeaderOf(SceneSection section) => new(SectionId(section), section.Name, section.IsActive,
        AssetTrees(section.Source) == null ? Guid.Empty : section.Source!.AssetID);

    /// <summary>
    ///     The networked objects stored in <paramref name="source" />, each with the identifiers of its tree in build
    ///     order, or null when the section has no scene asset a client could load.
    /// </summary>
    private Dictionary<Guid, Guid[]>? AssetTrees(SceneAsset? source)
    {
        if (source.IsNotValid() || source!.Data == null)
        {
            return null;
        }

        if (!_assetTrees.TryGetValue(source, out Dictionary<Guid, Guid[]>? trees))
        {
            trees = new Dictionary<Guid, Guid[]>();
            SceneContent content = source.ReadContent();
            foreach (GameObject go in content.Objects)
            {
                if (go.IsValid() && NetworkAccessor.NetworkedOf(go))
                {
                    List<Guid> identifiers = new();
                    CollectIdentifiers(go, identifiers);
                    trees[go.Identifier] = [.. identifiers];
                }
            }

            content.DisposeObjects();
            _assetTrees[source] = trees;
        }

        return trees;
    }

    internal void OnScene(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost || !sender.IsHost)
        {
            return;
        }

        if (delivery != Delivery.Reliable)
        {
            throw new InvalidDataException("Scene messages must be reliable.");
        }

        NetworkValidation.SceneMessageFormat(reader.ReadByte());
        ulong sceneGeneration = reader.ReadUInt64();
        NetworkValidation.SceneGeneration(sceneGeneration);
        uint count = reader.ReadVarUInt32();
        if (count == 0 || count > MaxSections)
        {
            throw new InvalidDataException("The scene message names an invalid number of sections.");
        }

        List<SectionHeader> headers = new((int)count);
        for (uint i = 0; i < count; i++)
        {
            headers.Add(SectionHeader.Read(ref reader));
        }

        if (!reader.IsAtEnd)
        {
            throw new InvalidDataException("Scene message contains trailing bytes.");
        }

        NetSession? session = Session;
        if (sceneGeneration <= SceneGeneration)
        {
            return;
        }

        if (!Scene.HasMain)
        {
            Networking.Shutdown("Joining needs a world bound as Scene.Main to load into.");
            return;
        }

        Scene world = Scene.Main;
        SceneGeneration = sceneGeneration;
        LocalJoinState = NetworkJoinState.Loading;
        EarlyRpcs.Clear();
        ReadyDeadline = Now + Configuration.SceneLoadTimeoutSeconds;
        // Objects in the persistent section stay through the change, as they do on the host, which spawns them again.
        foreach (NetworkObject entry in Entries.Values.ToArray())
        {
            entry.AwaitingRespawn = ReplicatedWorld == world && entry.GameObject.IsValid() &&
                                    entry.GameObject.Section is { IsPersistent: true };
            if (entry.AwaitingRespawn)
            {
                continue;
            }

            entry.NotifyDespawn(NetworkDespawnReason.SceneChanged);
            if (Session != session)
            {
                return;
            }
        }

        ReplicatedWorld = world;
        Entries.RemoveWhere(e => !e.AwaitingRespawn);
        Sync.Clear(c => c.IsValid() && FindEntry(c.GameObject) is { AwaitingRespawn: true });
        Transforms.Clear();
        ForgetSections();
        DisposeHeld(world);

        // One kept through the change that never had its spawn callbacks still waits for them.
        _pendingSpawns.RemoveAll(e => !e.AwaitingRespawn);

        List<(SectionHeader Header, SceneAsset? Source, SceneContent Content)> loaded = new();
        try
        {
            foreach (SectionHeader header in headers)
            {
                if (header.Id != PersistentSectionId)
                {
                    SceneContent content = LoadSection(header, out SceneAsset? source);
                    loaded.Add((header, source, content));
                }
            }

            ApplyLoaded(world, loaded);
        }
        finally
        {
            // What the world took was swapped for empty content, so this only releases the rest.
            foreach ((_, _, SceneContent content) in loaded)
            {
                content.DisposeObjects();
            }
        }

        if (Session != session)
        {
            return;
        }

        DispatchNetworkListener(e => e.OnNetworkLoading());
        if (Session != session)
        {
            return;
        }

        OnNetworkSceneActivated();
    }

    /// <summary>
    ///     This peer's own copy of a section the host named: read from its scene asset, with the networked objects
    ///     held back until the host spawns them, or empty for a section the host built in code.
    /// </summary>
    private SceneContent LoadSection(SectionHeader header, out SceneAsset? source)
    {
        source = null;
        if (header.Source == Guid.Empty)
        {
            return new SceneContent();
        }

        source = AssetDatabase.Get(header.Source) as SceneAsset;
        if (source.IsNotValid() || source!.Data == null)
        {
            throw new InvalidDataException(
                $"The host's section '{header.Name}' comes from a scene this build does not have.");
        }

        SceneContent content = source.ReadContent();
        try
        {
            HoldNetworkedObjects(content);
            return content;
        }
        catch
        {
            content.DisposeObjects();
            throw;
        }
    }

    // Takes the networked objects out of freshly read content, and drops what only the host has.
    private void HoldNetworkedObjects(SceneContent content)
    {
        List<GameObject> held = new();
        foreach (GameObject root in content.Roots.ToArray())
        {
            Visit(root);
        }

        if (held.Count > 0)
        {
            HashSet<GameObject> taken = new(ReferenceEqualityComparer.Instance);
            foreach (GameObject go in held)
            {
                go.SetParent(null!, false);
                taken.Add(go);
                foreach (GameObject part in go.GetChildrenDeep())
                {
                    taken.Add(part);
                }

                if (_held.Remove(go.Identifier, out GameObject? stale))
                {
                    stale.Dispose();
                }

                _held[go.Identifier] = go;
            }

            content.Objects = [.. content.Objects.Where(o => !taken.Contains(o))];
        }

        content.Resync();

        void Visit(GameObject go)
        {
            if (NetworkAccessor.ServerOnlyOf(go))
            {
                go.Dispose();
                return;
            }

            if (NetworkAccessor.NetworkedOf(go))
            {
                held.Add(go);
                return;
            }

            foreach (GameObject child in go.Children.ToArray())
            {
                Visit(child);
            }
        }
    }

    /// <summary>A held scene object, taken for the spawn that names it, or null.</summary>
    private GameObject? TakeHeld(Guid id) => _held.Remove(id, out GameObject? go) && go.IsValid() ? go : null;

    /// <summary>A scene object the host despawned is switched off and kept, since the host may spawn it again.</summary>
    private void Hold(GameObject go)
    {
        go.Enabled = false;
        _held[go.Identifier] = go;
    }

    /// <summary>Lets go of the held scene objects, but for the ones in the persistent section of <paramref name="keeping" />.</summary>
    private void DisposeHeld(Scene? keeping = null)
    {
        foreach ((Guid id, GameObject go) in _held.ToArray())
        {
            if (go.IsValid() && !ReferenceEquals(keeping, null) && go.Scene == keeping && go.Section is { IsPersistent: true })
            {
                continue;
            }

            _held.Remove(id);
            if (go.IsValid())
            {
                go.Dispose();
            }
        }
    }

    /// <summary>Replaces every section of <paramref name="world" /> but the persistent one with the ones just loaded.</summary>
    private void ApplyLoaded(Scene world, List<(SectionHeader Header, SceneAsset? Source, SceneContent Content)> sections)
    {
        _applyingSections = true;
        try
        {
            // A load the client queued before joining would replace the host's sections again.
            world.PendingLoad?.Cancel();
            // What is held and still in the world was kept through the change: only fresh copies are incoming.
            HashSet<Guid> incoming = [.. _held.Where(h => h.Value.Scene.IsNotValid()).Select(h => h.Key)];
            foreach ((_, _, SceneContent content) in sections)
            {
                incoming.UnionWith(content.IdentifierMap().Keys);
            }

            ReleasePreservedCopies(world, incoming);

            // A section the host built in code has nothing to load, so this peer keeps its own section of that name:
            // a level built in code is built by every peer.
            SceneSection[] mine = [.. world.Sections.Where(s => !s.IsPersistent)];
            HashSet<SceneSection> kept = new(ReferenceEqualityComparer.Instance);
            SceneSection?[] own = new SceneSection?[sections.Count];
            for (int i = 0; i < sections.Count; i++)
            {
                if (sections[i].Header.Source == Guid.Empty &&
                    OwnSection(mine, sections[i].Header.Name, kept) is { } found)
                {
                    own[i] = found;
                    kept.Add(found);
                }
            }

            if (kept.Count > 0)
            {
                foreach (SceneSection old in mine)
                {
                    if (!kept.Contains(old))
                    {
                        world.UnloadNow(old);
                    }
                }
            }

            // Each section's content leaves the list once the world has it, so a failure part way disposes only the rest.
            SceneSection? active = null;
            bool replaced = kept.Count > 0;
            for (int i = 0; i < sections.Count; i++)
            {
                (SectionHeader header, SceneAsset? source, SceneContent content) = sections[i];
                sections[i] = (header, source, new SceneContent());
                SceneSection section = own[i] ?? (replaced
                    ? world.AddContent(content, header.Name, source)
                    : world.ReplaceContent(content, header.Name, source));
                replaced = true;
                MapSection(header.Id, section);
                if (header.Active)
                {
                    active = section;
                }
            }

            if (active != null)
            {
                world.ActiveSection = active;
            }
        }
        finally
        {
            _applyingSections = false;
        }
    }

    // This peer's own section built in code under a name, not yet standing in for one of the host's.
    private static SceneSection? OwnSection(IEnumerable<SceneSection> sections, string name,
        HashSet<SceneSection> taken)
    {
        foreach (SceneSection section in sections)
        {
            if (!section.IsPersistent && !taken.Contains(section) && section.Source.IsNotValid() && section.Name == name)
            {
                return section;
            }
        }

        return null;
    }

    /// <summary>Persistent objects the incoming scenes bring again go with the sections they replace instead.</summary>
    internal static void ReleasePreservedCopies(Scene world, HashSet<Guid> incoming)
    {
        foreach (GameObject kept in world.Persistent.Roots.ToArray())
        {
            if (kept.IsValid() && incoming.Contains(kept.Identifier))
            {
                kept.MoveTo(world.ActiveSection);
            }
        }
    }

    /// <summary>Tells every client about a section the host loaded additively, then spawns its networked objects.</summary>
    internal void OnHostSectionLoaded(SceneSection section)
    {
        if (Session is not { IsHost: true, State: SessionState.Active } session)
        {
            return;
        }

        using FrameStatScope stats = FrameStats.Network.Scope();
        SendSectionRemovals();
        SectionId(section);
        SceneSection? previous = _sectionBeingSent;
        _sectionBeingSent = section;
        try
        {
            RegisterSceneObjects(section.Instance, SectionObjects(section));
        }
        finally
        {
            _sectionBeingSent = previous;
        }

        if (Session != session)
        {
            return;
        }

        using NetWriter message = new();
        message.WriteUInt64(SceneGeneration);
        HeaderOf(section).Write(message);
        NetPeer[] peers = [.. session.Peers.Where(p => !p.IsLocal)];
        foreach (NetPeer peer in peers)
        {
            SendEngine(peer, EngineMessage.SectionAdd, message.WrittenSpan);
        }

        // A peer still loading gets them with everything else once it is ready.
        NetPeer[] ready = [.. peers.Where(IsPeerReady)];
        NetworkObject[] entries = [.. Entries.Values.Where(e => e.GameObject.IsValid() && !e.TearingDown &&
            ReferenceEquals(e.GameObject.Section, section))];
        foreach (NetworkObject entry in entries)
        {
            NetPeer[] seeing = [.. ready.Where(p => Sees(entry, p))];
            if (Session != session)
            {
                return;
            }

            if (seeing.Length > 0 && entry.GameObject.IsValid())
            {
                SendSpawn(entry, seeing, true);
            }
        }

        if (Session != session)
        {
            return;
        }

        // What only an owner gets follows its spawn as state, and has to be there before the spawns finish.
        Sync.SendSyncChanges();
        foreach (NetPeer peer in ready)
        {
            SendSceneGeneration(peer, EngineMessage.SpawnFinished);
        }

        DispatchSectionListener(section, e => e.OnNetworkLoading());
        if (Session == session && IsReady)
        {
            DispatchSectionListener(section, e => e.OnNetworkReady());
        }
    }

    internal void OnSectionAdd(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost || !sender.IsHost || ReplicatedWorld.IsNotValid())
        {
            return;
        }

        if (delivery != Delivery.Reliable)
        {
            throw new InvalidDataException("Sections must be sent reliably.");
        }

        ulong generation = reader.ReadUInt64();
        SectionHeader header = SectionHeader.Read(ref reader);
        if (!reader.IsAtEnd)
        {
            throw new InvalidDataException("Section message contains trailing bytes.");
        }

        if (generation != SceneGeneration)
        {
            return;
        }

        if (header.Id == PersistentSectionId || SectionOf(header.Id) != null)
        {
            throw new InvalidDataException("The host sent a malformed section.");
        }

        NetSession? session = Session;
        Scene world = ReplicatedWorld!;
        SceneSection? section = header.Source == Guid.Empty
            ? OwnSection(world.Sections, header.Name, [.. _sectionIds.Keys])
            : null;
        if (section == null)
        {
            SceneContent content = LoadSection(header, out SceneAsset? source);
            _applyingSections = true;
            try
            {
                section = world.AddContent(content, header.Name, source);
            }
            catch
            {
                content.DisposeObjects();
                throw;
            }
            finally
            {
                _applyingSections = false;
            }
        }

        MapSection(header.Id, section);
        if (header.Active)
        {
            world.ActiveSection = section;
        }

        DispatchSectionListener(section, e => e.OnNetworkLoading());
        if (Session == session && IsReady)
        {
            DispatchSectionListener(section, e => e.OnNetworkReady());
        }
    }

    /// <summary>The networked components and transforms of a scene object's tree that its scene asset stores, by identifier.</summary>
    private static (Component[] Components, NetworkTransform[] Transforms) AsStored(GameObject go, Guid[] stored)
    {
        HashSet<Guid> identifiers = [.. stored];
        return ([.. NetworkComponents.Gather(go).Where(c => identifiers.Contains(c.Identifier))],
            [.. NetworkComponents.GatherTransforms(go).Where(t => identifiers.Contains(t.Identifier))]);
    }

    /// <summary>For tests, which build their scenes in code: what clients build a scene object from that no asset stores.</summary>
    internal static Func<GameObject, SpawnTemplate?>? UnstoredSceneObjects;

    /// <summary>Registers networked objects the host's world holds, which belong to the host until it spawns or hands them out.</summary>
    internal void RegisterSceneObjects(Scene world, IEnumerable<GameObject> objects)
    {
        NetSession? session = Session;
        foreach (GameObject go in objects.ToArray())
        {
            if (Session != session || !IsActive || ReplicatedWorld != world)
            {
                break;
            }

            if (!NetworkAccessor.NetworkedOf(go) || NetworkAccessor.IsExcludedFromNetwork(go) ||
                Entries.Contains(go.Identifier))
            {
                continue;
            }

            if (go.Network.Root is { } root && root != go)
            {
                Debug.LogWarning(
                    $"'{go.Name}' is marked networked inside the networked object '{root.Name}', so it is part of that " +
                    "object. A networked object can't hold another.");
                continue;
            }

            // An object its scene asset stores is one clients hold too, so its spawn only names it. What is networked
            // of it is what the asset stores: it may have run and added to itself here, and no client's copy has.
            Guid[]? stored = go.Section is { } section ? AssetTrees(section.Source)?.GetValueOrDefault(go.Identifier) : null;
            Component[] components = NetworkComponents.Gather(go);
            NetworkTransform[]? transforms = null;
            if (stored != null)
            {
                (components, transforms) = AsStored(go, stored);
            }

            if (components.Length > NetworkComponents.MaxPerObject)
            {
                Debug.LogError(
                    $"'{go.Name}' has {components.Length} networked components, and a networked object can hold " +
                    $"{NetworkComponents.MaxPerObject}, so it is not networked. Split it into several networked objects.");
                continue;
            }

            SpawnTemplate? template;
            try
            {
                template = stored != null
                    ? new SpawnTemplate(Guid.Empty, null, [], stored, components, true, transforms)
                    : BuiltTemplateOf(go) ?? UnstoredSceneObjects?.Invoke(go);
            }
            catch (InvalidOperationException e)
            {
                Debug.LogError($"{e.Message} It is not networked.");
                continue;
            }

            if (template == null)
            {
                Debug.LogWarning(
                    $"'{go.Name}' is marked networked but is not stored in a scene asset, a prefab instance or built by " +
                    "a factory, so clients have nothing to build it from and it is not networked. Spawn it with " +
                    "SpawnPrefab or SpawnFactory instead.");
                continue;
            }

            NetworkTransform[] moving = template.Transforms ?? NetworkComponents.GatherTransforms(go);
            if (moving.Length > NetworkComponents.MaxTransformsPerObject)
            {
                Debug.LogError(
                    $"'{go.Name}' has {moving.Length} NetworkTransforms, and a networked object can hold " +
                    $"{NetworkComponents.MaxTransformsPerObject}, so it is not networked. Split it into several networked objects.");
                continue;
            }

            NetworkObject entry = new(this, go)
            {
                Generation = NextGeneration(), NetId = NextNetId(), Components = template.Components, Template = template
            };
            entry.SetTransforms(moving);
            Entries.Add(entry);
            InitVisibility(entry);
            entry.NotifySpawn();
        }
    }

    /// <summary>The peer loaded the host's scenes: it gets every networked object it may have, then goes ready.</summary>
    internal void OnClientReady(NetPeer peer, ref NetReader reader, Delivery delivery)
    {
        ulong generation = ReadSceneGeneration(ref reader, delivery);
        if (!TryGetLoadingPeer(peer, generation, NetworkJoinState.Loading, out PeerReadiness? state))
        {
            return;
        }

        state!.Stage = NetworkJoinState.Synchronizing;
        Transforms.Restart(peer);
        SendSceneGeneration(peer, EngineMessage.SpawnStarted);

        // Every object it may have goes in one go, between the started and finished messages.
        NetSession session = Session!;
        foreach (NetworkObject entry in Entries.Values.ToArray())
        {
            if (!entry.GameObject.IsValid() || entry.TearingDown || FindEntry(entry.GameObject) != entry ||
                !Sees(entry, peer))
            {
                continue;
            }

            SendSpawn(entry, [peer], true);
            if (Session != session || !StillLoading(peer, state))
            {
                return;
            }
        }

        // What only an owner gets follows its spawn as state, and has to be there before the spawns finish.
        Sync.SendSyncChanges();
        if (Session != session || !StillLoading(peer, state))
        {
            return;
        }

        // Queued first, so RPCs from ready callbacks here arrive after the client's own ready notification.
        state.Stage = NetworkJoinState.Ready;
        SendSceneGeneration(peer, EngineMessage.SpawnFinished);
        if (PlayerOf(peer).IsValid())
        {
            SendPlayer(peer);
        }

        DispatchNetworkListener(e =>
        {
            if (IsPeerReady(peer))
            {
                e.OnPeerReady(peer);
            }
        });
    }

    private bool StillLoading(NetPeer peer, PeerReadiness state) =>
        Session is { } session && session.TryGetPeer(peer.Id, out NetPeer? current) && ReferenceEquals(current, peer) &&
        LoadingPeers.TryGetValue(peer.Id, out PeerReadiness? now) && ReferenceEquals(now, state);

    internal void OnSpawnStarted(NetPeer peer, ref NetReader reader, Delivery delivery) =>
        ReadSceneGeneration(ref reader, delivery);

    /// <summary>The host has sent every object of a run: callbacks held back run now.</summary>
    internal void OnSpawnFinished(NetPeer peer, ref NetReader reader, Delivery delivery)
    {
        ulong generation = ReadSceneGeneration(ref reader, delivery);
        if (!IsClient || !peer.IsHost || generation != SceneGeneration || ReplicatedWorld.IsNotValid())
        {
            return;
        }

        // An added section's run, on a peer already playing, held nothing back.
        if (LocalJoinState != NetworkJoinState.Synchronizing)
        {
            return;
        }

        NetSession? session = Session;

        // What was kept through the scene change and not spawned again is gone on the host, or hidden from this peer.
        foreach (NetworkObject entry in Entries.Values.Where(e => e.AwaitingRespawn).ToArray())
        {
            if (Entries.Remove(entry.GameObject.Identifier))
            {
                entry.NotifyDespawn(NetworkDespawnReason.SceneChanged);
                if (Session != session)
                {
                    return;
                }

                ReleaseCopy(entry);
            }
        }

        NetworkObject[] pending = [.. _pendingSpawns];
        _pendingSpawns.Clear();
        foreach (NetworkObject entry in pending)
        {
            if (Session != session)
            {
                return;
            }

            if (Entries.TryGetValue(entry.GameObject.Identifier, out NetworkObject? current) && current == entry)
            {
                Sync.RunSpawnHooks(entry.GameObject);
                entry.NotifySpawn();
                ClaimPendingPlayer(entry);
            }
        }

        if (Session != session)
        {
            return;
        }

        Sync.RunHeldChanges();
        if (Session != session)
        {
            return;
        }

        SetLocalReady();
        RunEarlyRpcs();
    }

    /// <summary>Whether a spawn's callbacks run as it arrives, or wait for the host's finished message.</summary>
    private void NotifyOrHold(NetworkObject entry)
    {
        if (IsClient && !IsReady)
        {
            _pendingSpawns.Add(entry);
            return;
        }

        Sync.RunSpawnHooks(entry.GameObject);
        entry.NotifySpawn();
        ClaimPendingPlayer(entry);
    }

    /// <summary>A call the host sent while this peer's spawns were still arriving, held until the last one has.</summary>
    internal bool AwaitsActivation(EngineMessage kind) =>
        IsClient && LocalJoinState == NetworkJoinState.Synchronizing &&
        kind is EngineMessage.RpcFromHost or EngineMessage.StaticRpcFromHost;
}
