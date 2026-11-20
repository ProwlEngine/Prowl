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

// Joining and changing scenes, as Mirror does it. The host names the scene assets its sections came from, each
// client loads them itself and says it is ready, and the host then spawns every networked object the client may
// have between a started and a finished message. No scene content travels: a section built in code is empty on
// clients, and its networked objects reach them as spawns like any other.
internal sealed partial class NetworkSystem
{
    internal const byte SceneMessageFormat = 13;
    private const uint MaxSections = 4096;

    // Networked objects of the scenes this client loaded, kept out of the world until the host spawns them.
    private readonly Dictionary<Guid, GameObject> _held = new();

    // Objects spawned before this client was ready, whose callbacks wait for the host's finished message.
    private readonly List<NetworkObject> _pendingSpawns = new();

    // The trees of the networked objects stored in each scene asset, by root, read once a session.
    private readonly Dictionary<Scene, Dictionary<Guid, Guid[]>> _assetTrees = new(ReferenceEqualityComparer.Instance);

    // Set while the host writes spawns for peers that just loaded the scenes, which hold the scene's own objects.
    private bool _sceneSpawns;

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
    internal void WriteScene(SceneInstance world, NetWriter output)
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
    private Dictionary<Guid, Guid[]>? AssetTrees(Scene? source)
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

        NetworkValidation.SnapshotFormat(reader.ReadByte());
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

        if (!SceneInstance.HasMain)
        {
            Networking.Shutdown("Joining needs a world bound as SceneInstance.Main to load into.");
            return;
        }

        SceneInstance world = SceneInstance.Main;
        SceneGeneration = sceneGeneration;
        LocalJoinState = NetworkJoinState.Loading;
        EarlyRpcs.Clear();
        ReadyDeadline = Now + Configuration.SceneLoadTimeoutSeconds;
        foreach (NetworkObject entry in Entries.Values.ToArray())
        {
            entry.NotifyDespawn(NetworkDespawnReason.SceneChanged);
            if (Session != session)
            {
                return;
            }
        }

        ReplicatedWorld = world;
        Entries.Clear();
        Sync.Clear();
        Snapshots.Clear();
        ForgetSections();
        DisposeHeld();
        _pendingSpawns.Clear();

        List<(SectionHeader Header, Scene? Source, SceneContent Content)> loaded = new();
        try
        {
            foreach (SectionHeader header in headers)
            {
                if (header.Id != PersistentSectionId)
                {
                    SceneContent content = LoadSection(header, out Scene? source);
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
    private SceneContent LoadSection(SectionHeader header, out Scene? source)
    {
        source = null;
        if (header.Source == Guid.Empty)
        {
            return new SceneContent();
        }

        source = AssetDatabase.Get(header.Source) as Scene;
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

    // Scene objects the host never spawned for this peer: destroyed there, hidden from it, or not in its copy.
    private void DisposeHeld()
    {
        foreach (GameObject go in _held.Values.ToArray())
        {
            if (go.IsValid())
            {
                go.Dispose();
            }
        }

        _held.Clear();
    }

    /// <summary>Replaces every section of <paramref name="world" /> but the persistent one with the ones just loaded.</summary>
    private void ApplyLoaded(SceneInstance world, List<(SectionHeader Header, Scene? Source, SceneContent Content)> sections)
    {
        _applyingSections = true;
        try
        {
            // A load the client queued before joining would replace the host's sections again.
            world.PendingLoad?.Cancel();
            HashSet<Guid> incoming = [.. _held.Keys];
            foreach ((_, _, SceneContent content) in sections)
            {
                incoming.UnionWith(content.IdentifierMap().Keys);
            }

            ReleasePreservedCopies(world, incoming);

            // A section the host built in code has nothing to load, so this peer keeps its own section of that name:
            // as in Mirror, a level built in code is built by every peer.
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
                (SectionHeader header, Scene? source, SceneContent content) = sections[i];
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
    internal static void ReleasePreservedCopies(SceneInstance world, HashSet<Guid> incoming)
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

        // A peer still loading gets them with everything else once it is ready, and one in the middle of its join
        // run has them added to the end of it.
        NetPeer[] ready = [.. peers.Where(IsPeerReady)];
        NetworkObject[] entries = [.. Entries.Values.Where(e => e.GameObject.IsValid() && !e.TearingDown &&
            ReferenceEquals(e.GameObject.Section, section))];
        foreach (PeerReadiness joining in LoadingPeers.Values)
        {
            if (joining.Run is { } run)
            {
                joining.Run = [.. run, .. entries];
            }
        }
        _sceneSpawns = true;
        try
        {
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
        }
        finally
        {
            _sceneSpawns = false;
        }

        if (Session != session)
        {
            return;
        }

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
        SceneInstance world = ReplicatedWorld!;
        SceneSection? section = header.Source == Guid.Empty
            ? OwnSection(world.Sections, header.Name, [.. _sectionIds.Keys])
            : null;
        if (section == null)
        {
            SceneContent content = LoadSection(header, out Scene? source);
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

    /// <summary>Registers networked objects the host's world holds, which belong to the host until it spawns or hands them out.</summary>
    internal void RegisterSceneObjects(SceneInstance world, IEnumerable<GameObject> objects)
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

            Component[] components = NetworkComponents.Gather(go);
            if (components.Length > NetworkComponents.MaxPerObject)
            {
                Debug.LogError(
                    $"'{go.Name}' has {components.Length} networked components, and a networked object can hold " +
                    $"{NetworkComponents.MaxPerObject}, so it is not networked. Split it into several networked objects.");
                continue;
            }

            // An object its scene asset stores is one clients hold too, so a spawn can name it instead of carrying it.
            Guid[]? stored = go.Section is { } section ? AssetTrees(section.Source)?.GetValueOrDefault(go.Identifier) : null;
            NetworkObject entry = new(this, go)
            {
                Generation = NextGeneration(), NetId = NextNetId(), Components = components,
                Template = stored == null ? null : new SpawnTemplate(Guid.Empty, null, [], stored, components, true)
            };
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
        Snapshots.Delivered(peer);
        SendSceneGeneration(peer, EngineMessage.SpawnStarted);
        state.Run = [.. Entries.Values];
        ContinueJoin(peer, state);
    }

    /// <summary>
    ///     Sends the next spawns of a peer's join run, as many as this tick's budget and the peer's send queue allow, and
    ///     finishes the run after the last.
    /// </summary>
    private void ContinueJoin(NetPeer peer, PeerReadiness state)
    {
        NetSession session = Session!;
        NetworkObject[] run = state.Run!;
        int budget = Configuration.JoinSpawnBytesPerTick;
        int sent = 0;
        bool any = false;
        _sceneSpawns = true;
        try
        {
            while (state.Next < run.Length)
            {
                if (any && ((budget > 0 && sent >= budget) || SendQueueIsFilling(peer)))
                {
                    // Still making progress, so the load is not what is timing out.
                    state.Deadline = Now + Configuration.SceneLoadTimeoutSeconds;
                    return;
                }

                NetworkObject entry = run[state.Next++];
                if (!entry.GameObject.IsValid() || entry.TearingDown || FindEntry(entry.GameObject) != entry ||
                    !Sees(entry, peer))
                {
                    continue;
                }

                _lastSpawnBytes = 0;
                SendSpawn(entry, [peer], true);
                sent += _lastSpawnBytes;
                any = true;
                if (Session != session || !StillLoading(peer, state))
                {
                    return;
                }
            }
        }
        finally
        {
            _sceneSpawns = false;
        }

        state.Run = null;

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

    // The bytes of the spawn message written last, for pacing a join.
    private int _lastSpawnBytes;

    private static bool SendQueueIsFilling(NetPeer peer) =>
        peer.Connection is { } connection && connection.MaxPendingSendBytes != long.MaxValue &&
        connection.PendingSendBytes > connection.MaxPendingSendBytes / 2;

    private bool StillLoading(NetPeer peer, PeerReadiness state) =>
        Session is { } session && session.TryGetPeer(peer.Id, out NetPeer? current) && ReferenceEquals(current, peer) &&
        LoadingPeers.TryGetValue(peer.Id, out PeerReadiness? now) && ReferenceEquals(now, state);

    internal void OnSpawnStarted(NetPeer peer, ref NetReader reader, Delivery delivery) =>
        ReadSceneGeneration(ref reader, delivery);

    /// <summary>The host has sent every object of a run: callbacks held back run now, and scene objects it left out go.</summary>
    internal void OnSpawnFinished(NetPeer peer, ref NetReader reader, Delivery delivery)
    {
        ulong generation = ReadSceneGeneration(ref reader, delivery);
        if (!IsClient || !peer.IsHost || generation != SceneGeneration || ReplicatedWorld.IsNotValid())
        {
            return;
        }

        DisposeHeld();

        // An added section's run, on a peer already playing, held nothing back.
        if (LocalJoinState != NetworkJoinState.Synchronizing)
        {
            return;
        }

        NetSession? session = Session;
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

    /// <summary>A call the host sent once this peer was ready, arriving on another channel ahead of the finished message.</summary>
    internal bool AwaitsActivation(EngineMessage kind) =>
        IsClient && LocalJoinState == NetworkJoinState.Synchronizing &&
        kind is EngineMessage.RpcFromHost or EngineMessage.StaticRpcFromHost;
}
