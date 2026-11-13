// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

using Jitter2.Dynamics;

using Prowl.Echo;
using Prowl.Runtime.Diagnostics;
using Prowl.Runtime.Resources;
using Prowl.Courier;

namespace Prowl.Runtime.Networking;

/// <summary>One section of the host's world as a client receives it, with its objects still detached.</summary>
internal readonly record struct SectionData(uint Id, string Name, bool Active, Scene? Source, SceneContent Content);

/// <summary>The sections a snapshot carries. Disposing it disposes their objects, unless a world took them.</summary>
internal sealed class SnapshotSections(List<SectionData> sections) : IDisposable
{
    internal List<SectionData> Sections { get; } = sections;

    internal IEnumerable<GameObject> Objects => Sections.SelectMany(s => s.Content.Objects);

    public void Dispose()
    {
        foreach (SectionData section in Sections)
        {
            section.Content.DisposeObjects();
        }
    }
}

// Sharing the host's world with clients, section by section.
internal sealed partial class NetworkSystem
{
    /// <summary>The persistent section's id. The host numbers every other section as it loads.</summary>
    internal const uint PersistentSectionId = 0;

    private readonly Dictionary<SceneSection, uint> _sectionIds = new();
    private readonly Dictionary<uint, SceneSection> _sectionsById = new();
    private readonly HashSet<GameObject> _unloading = new(ReferenceEqualityComparer.Instance);
    private readonly List<uint> _removedSections = new();
    private uint _nextSectionId;
    private bool _applyingSections;

    /// <summary>The id the host gave <paramref name="section" />, given one now if it has none.</summary>
    internal uint SectionId(SceneSection section)
    {
        if (section.IsPersistent)
        {
            return PersistentSectionId;
        }

        if (!_sectionIds.TryGetValue(section, out uint id))
        {
            id = checked(++_nextSectionId);
            MapSection(id, section);
        }

        return id;
    }

    /// <summary>Where a spawned root goes on clients: its section's id plus one, or 0 for their active section.</summary>
    internal uint SectionSlot(GameObject go) =>
        go.Parent.IsNotValid() && go.Section is { } section && ReferenceEquals(section.Instance, ReplicatedWorld)
            ? SectionId(section) + 1
            : 0;

    internal SceneSection? SectionOf(uint id)
    {
        if (ReplicatedWorld.IsNotValid())
        {
            return null;
        }

        if (id == PersistentSectionId)
        {
            return ReplicatedWorld!.Persistent;
        }

        return _sectionsById.TryGetValue(id, out SceneSection? section) && section.IsLoaded ? section : null;
    }

    private void MapSection(uint id, SceneSection section)
    {
        _sectionIds[section] = id;
        _sectionsById[id] = section;
    }

    private void ForgetSections()
    {
        _sectionIds.Clear();
        _sectionsById.Clear();
        _unloading.Clear();
        _removedSections.Clear();
    }

    /// <summary>A section being unloaded goes away on clients as a whole, so its objects send no despawns.</summary>
    internal bool IsUnloading(GameObject go) => _unloading.Contains(go);

    /// <summary>
    ///     Refuses an additive load into the networked world of objects it holds already, which loading one scene twice
    ///     does. Networking knows each object by its identifier, so the world can hold each one only once.
    /// </summary>
    internal string? RefuseDuplicateObjects(SceneInstance world, Scene scene, SceneContent content)
    {
        if (!ReferenceEquals(world, ReplicatedWorld))
        {
            return null;
        }

        foreach (GameObject obj in content.Objects)
        {
            if (obj.IsValid() && world.FindObject(obj.Identifier).IsValid())
            {
                return $"'{scene.Name}' holds objects the networked world has already, as loading one scene twice does. " +
                       "A networked world can hold each object only once.";
            }
        }

        return null;
    }

    /// <summary>Sends a section the clients do not have yet before anything refers to it.</summary>
    private void EnsureAnnounced(SceneSection section)
    {
        if (!section.IsPersistent && !_sectionIds.ContainsKey(section) && ReferenceEquals(section.Instance, ReplicatedWorld))
        {
            OnHostSectionLoaded(section);
        }
    }

    /// <summary>A root the host moved into another section moves on clients too.</summary>
    internal void OnRootMoved(GameObject root, SceneSection section)
    {
        if (Session is not { IsHost: true, State: SessionState.Active } || !ReferenceEquals(section.Instance, ReplicatedWorld) ||
            NetworkAccessor.IsExcludedFromNetwork(root))
        {
            return;
        }

        EnsureAnnounced(section);
        using NetWriter message = new(32);
        new SectionMoveMessage(root.Identifier, SectionId(section)).Write(message);
        SendToObservers(FindEntry(root) is { } entry && entry.GameObject == root ? entry : null,
            EngineMessage.SectionMove, message.WrittenSpan);
    }

    internal void OnSectionMove(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost || !sender.IsHost)
        {
            return;
        }

        SectionMoveMessage message = SectionMoveMessage.Read(ref reader);
        if (delivery != Delivery.Reliable || !reader.IsAtEnd)
        {
            throw new InvalidDataException("Invalid section move.");
        }

        if (SectionOf(message.SectionId) is { } section && FindByIdentifier(message.Identifier) is GameObject root &&
            root.Parent.IsNotValid() && !ReferenceEquals(root.Section, section))
        {
            root.MoveTo(section);
        }
    }

    internal void OnWorldLoaded(SceneLoadedArgs loaded)
    {
        if (_applyingSections || ReplicatedWorld.IsNotValid() || !ReferenceEquals(loaded.Section.Instance, ReplicatedWorld))
        {
            return;
        }

        if (IsHost)
        {
            if (loaded.Additive)
            {
                OnHostSectionLoaded(loaded.Section);
            }
            else
            {
                OnHostSceneLoaded();
            }
        }
        else if (IsClient && !loaded.Additive)
        {
            Networking.Shutdown("The network scene was replaced locally during the session.");
        }
    }

    /// <summary>A host that binds another world as Main shares that world instead, like a load replacing every section.</summary>
    internal void OnMainChanged(SceneInstance? previous, SceneInstance? current)
    {
        if (current.IsNotValid() || current == ReplicatedWorld)
        {
            return;
        }

        if (IsClient)
        {
            if (ReplicatedWorld.IsValid())
            {
                Networking.Shutdown("The network scene was replaced locally during the session.");
            }

            return;
        }

        if (!IsHost)
        {
            return;
        }

        ForgetSections();
        ReplicatedWorld = current;
        OnHostSceneLoaded();
    }

    internal void OnWorldUnloading(SceneSection section)
    {
        if (!ReferenceEquals(section.Instance, ReplicatedWorld) || !_sectionIds.Remove(section, out uint id))
        {
            return;
        }

        _sectionsById.Remove(id);
        if (IsHost)
        {
            foreach (GameObject root in section.Roots)
            {
                Collect(root);
            }

            _removedSections.Add(id);
        }
        else if (!_applyingSections && section.Instance.State == SceneState.Running)
        {
            Networking.Shutdown("The network scene was replaced locally during the session.");
        }

        void Collect(GameObject go)
        {
            _unloading.Add(go);
            foreach (GameObject child in go.Children)
            {
                Collect(child);
            }
        }
    }

    /// <summary>Tells clients about the sections the host unloaded, once their objects are gone.</summary>
    internal void SendSectionRemovals()
    {
        _unloading.Clear();
        if (_removedSections.Count == 0)
        {
            return;
        }

        uint[] removed = [.. _removedSections];
        _removedSections.Clear();
        if (Session is not { IsHost: true, State: SessionState.Active })
        {
            return;
        }

        foreach (uint id in removed)
        {
            using NetWriter message = new(16);
            new SectionRemoveMessage(SceneGeneration, id).Write(message);
            BroadcastEngine(EngineMessage.SectionRemove, message.WrittenSpan);
        }
    }

    internal void OnHostSceneLoaded()
    {
        if (Session is not { IsHost: true, State: SessionState.Active } session || ReplicatedWorld.IsNotValid())
        {
            return;
        }

        // Objects in the persistent section keep their network state; the rest went with the sections the load replaced.
        using FrameStatScope stats = FrameStats.Network.Scope();
        SceneInstance world = ReplicatedWorld!;
        _unloading.Clear();
        _removedSections.Clear();
        foreach (SceneSection section in world.Sections)
        {
            SectionId(section);
        }

        SceneGeneration++;
        ClearViewpoints();
        LocalJoinState = NetworkJoinState.Loading;
        Index.Clear();
        Sync.Dirty.Clear();
        Snapshots.Clear();
        foreach (NetworkObject entry in Entries.Values.ToArray())
        {
            if (!entry.GameObject.IsValid() || entry.GameObject.Scene != world)
            {
                entry.NotifyDespawn(NetworkDespawnReason.SceneChanged);
            }

            if (Session != session)
            {
                return;
            }
        }

        Entries.RemoveWhere(e => !e.GameObject.IsValid() || e.GameObject.Scene != world);
        Restricted.RemoveWhere(e => !Entries.TryGetValue(e.GameObject.Identifier, out NetworkObject? kept) || kept != e);
        RegisterSceneObjects(world, world.Objects);

        if (Session != session)
        {
            return;
        }

        DispatchNetworkListener(e => e.OnNetworkLoading());
        if (Session != session)
        {
            return;
        }

        SetLocalReady();
        if (Session != session)
        {
            return;
        }

        SendSnapshot(session.Peers.Where(peer => !peer.IsLocal).ToArray());
    }

    /// <summary>Sends a section the host loaded additively to every client, which adds it to its own world.</summary>
    internal void OnHostSectionLoaded(SceneSection section)
    {
        if (Session is not { IsHost: true, State: SessionState.Active } session)
        {
            return;
        }

        using FrameStatScope stats = FrameStats.Network.Scope();
        SendSectionRemovals();
        SectionId(section);
        NetworkObject[] entries;
        SceneSection? previous = _sectionBeingSent;
        _sectionBeingSent = section;
        try
        {
            RegisterSceneObjects(section.Instance, SectionObjects(section));
            if (Session != session)
            {
                return;
            }

            entries = SectionEntries(section);
            if (!SendSection(section, session))
            {
                return;
            }
        }
        finally
        {
            _sectionBeingSent = previous;
        }

        foreach (NetPeer peer in session.Peers)
        {
            if (peer.IsLocal)
            {
                continue;
            }

            foreach (NetworkObject entry in entries)
            {
                if (Sees(entry, peer))
                {
                    Snapshots.Delivered(peer, entry.GameObject);
                }
            }
        }

        DispatchSectionListener(section, e => e.OnNetworkLoading());
        if (Session == session && IsReady)
        {
            DispatchSectionListener(section, e => e.OnNetworkReady());
        }
    }

    /// <summary>
    ///     Sends a section to each client without the networked objects it does not have, nor links to any it lacks
    ///     elsewhere. False if the session ended.
    /// </summary>
    private bool SendSection(SceneSection section, NetSession session)
    {
        if (Restricted.Count > 0)
        {
            return SendPerPeer(EngineMessage.SectionAdd, [.. session.Peers.Where(p => !p.IsLocal)],
                peer => HiddenRoots(peer), message => WriteSection(section, message), session);
        }

        List<WrittenValue> written = new();
        using NetWriter message = new(64 * 1024);
        using (new NetworkSerialization.WritingFor(null, null, written))
        {
            WriteSection(section, message);
        }

        if (Session != session)
        {
            return false;
        }

        BroadcastEngine(EngineMessage.SectionAdd, message.WrittenSpan);
        foreach (NetPeer peer in session.Peers)
        {
            if (!peer.IsLocal)
            {
                Snapshots.Wrote(peer, written);
            }
        }

        return true;
    }

    /// <summary>Writes the message once for each distinct set of objects peers do not have. False if the session ended.</summary>
    private bool SendPerPeer(EngineMessage kind, NetPeer[] peers, Func<NetPeer, HashSet<GameObject>?> hiddenFor,
        Action<NetWriter> write, NetSession session)
    {
        List<(HashSet<GameObject>? Hidden, List<NetPeer> Peers)> groups = [];
        foreach (NetPeer peer in peers)
        {
            HashSet<GameObject>? hidden = hiddenFor(peer);
            int group = groups.FindIndex(g => g.Hidden == null ? hidden == null : hidden != null && g.Hidden.SetEquals(hidden));
            if (group < 0)
            {
                groups.Add((hidden, [peer]));
            }
            else
            {
                groups[group].Peers.Add(peer);
            }
        }

        foreach ((HashSet<GameObject>? hidden, List<NetPeer> members) in groups)
        {
            if (!SendWritten(kind, members, hidden, write, session))
            {
                return false;
            }
        }

        return true;
    }

    private bool SendWritten(EngineMessage kind, IEnumerable<NetPeer> peers, HashSet<GameObject>? hidden,
        Action<NetWriter> write, NetSession session)
    {
        List<WrittenValue> written = new();
        using NetWriter message = new(64 * 1024);
        using (new NetworkSerialization.WritingFor(null, null, written))
        {
            WithoutRoots(hidden, () => write(message));
        }

        // Writing runs components' serialization callbacks, which could end the session.
        if (Session != session)
        {
            return false;
        }

        foreach (NetPeer peer in peers)
        {
            SendEngine(peer, kind, message.WrittenSpan);
            Snapshots.Wrote(peer, written);
        }

        return true;
    }

    /// <summary>Starts <paramref name="peers" /> loading the world, from one snapshot written for all that have every object.</summary>
    internal void SendSnapshot(params NetPeer[] peers)
    {
        if (Session is not { } session || ReplicatedWorld.IsNotValid() || peers.Length == 0)
        {
            return;
        }

        foreach (NetPeer peer in peers)
        {
            BeginPeerLoad(peer);
        }

        using FrameStatScope stats = FrameStats.Network.Scope();
        SceneInstance world = ReplicatedWorld!;
        SendPerPeer(EngineMessage.SceneSnapshot, peers, peer => HiddenRoots(peer), message => WriteSnapshot(world, message),
            session);
    }

    /// <summary>
    ///     Writes <paramref name="world" /> as clients receive it: every section, minus anything that is
    ///     <see cref="NetworkMode.Never" />, then the networked objects' owners and every [Sync] value.
    /// </summary>
    internal void WriteSnapshot(SceneInstance world, NetWriter output)
    {
        using FrameStatScope stats = FrameStats.Network.Scope();
        new SceneSnapshotMessage(SceneSnapshotMessage.CurrentFormat, Math.Max(SceneGeneration, 1),
            Compress(WriteSections(world.Sections))).Write(output);

        WriteObjectStates(output, NetworkSerialization.HiddenRoots == null ? Entries.Values : [.. Entries.Values.Where(Shown)]);
        Sync.WriteSyncState(output, world.Objects);
    }

    private static bool Shown(NetworkObject entry) => !NetworkSerialization.IsHiddenRoot(entry.GameObject);

    /// <summary>Writes one section as clients receive it, then its networked objects' owners and [Sync] values.</summary>
    internal void WriteSection(SceneSection section, NetWriter output)
    {
        using FrameStatScope stats = FrameStats.Network.Scope();
        new SectionAddMessage(SceneGeneration, Compress(WriteSections([section], linkOutside: true))).Write(output);
        WriteObjectStates(output, SectionEntries(section));
        Sync.WriteSyncState(output, SectionObjects(section));
    }

    private NetworkObject[] SectionEntries(SceneSection section) =>
        [.. Entries.Values.Where(e => e.GameObject.IsValid() && ReferenceEquals(e.GameObject.Section, section) && Shown(e))];

    private static void WriteObjectStates(NetWriter output, IReadOnlyCollection<NetworkObject> entries)
    {
        new ObjectStateBlock((uint)entries.Count).Write(output);
        foreach (NetworkObject entry in entries)
        {
            new SnapshotObjectMessage(entry.GameObject.Identifier, entry.Owner, entry.Orphaned,
                entry.Generation, entry.NetId).Write(output);
        }
    }

    /// <summary>
    ///     A header for each section, and the objects of all of them in one tree, so a link from one section into
    ///     another still holds on the client. With <paramref name="linkOutside" /> a link to an object outside these
    ///     sections goes by identifier, for the client to resolve in its own world.
    /// </summary>
    internal EchoObject WriteSections(IEnumerable<SceneSection> sections, bool linkOutside = false)
    {
        EchoObject headers = EchoObject.NewList();
        List<SceneContent> contents = [];
        foreach (SceneSection section in sections)
        {
            Scene? source = section.Source;
            EchoObject header = EchoObject.NewCompound(4);
            header.Add("Id", new EchoObject((int)SectionId(section)));
            header.Add("Name", new EchoObject(section.Name));
            header.Add("Active", new EchoObject(section.IsActive));
            header.Add("Source", new EchoObject(ReferenceEquals(source, null) ? string.Empty : source.AssetID.ToString()));
            headers.ListAdd(header);
            List<GameObject> objects = SectionObjects(section);
            NoteSentObjects(objects);
            contents.Add(new SceneContent
            {
                Objects = [.. objects],
                Environment = section.Environment,
                Lighting = section.Lighting,
                LightmapBake = section.LightmapBake
            });
        }

        EchoObject tree = null!;
        NetworkSerialization.WritingSnapshot = true;
        try
        {
            SerializationContext context = linkOutside
                ? new() { ExternalReferences = new NetworkSerialization.SnapshotReferences(SceneReferenceResolver.ForTrees(contents.SelectMany(c => c.Roots))) }
                : SnapshotContext();
            AsSetUp(() => tree = Serializer.Serialize(typeof(SceneContent[]), contents.ToArray(), context));
        }
        finally
        {
            NetworkSerialization.WritingSnapshot = false;
        }

        EchoObject root = EchoObject.NewCompound(2);
        root.Add("Sections", headers);
        root.Add("Contents", tree);
        return root;
    }

    /// <summary>The objects of a section clients receive: everything saved that is not under a NetworkMode.Never object or a hidden root.</summary>
    internal static List<GameObject> SectionObjects(SceneSection section)
    {
        List<GameObject> objects = [];
        foreach (GameObject root in section.Roots)
        {
            Collect(root);
        }

        return objects;

        void Collect(GameObject go)
        {
            if ((go.HideFlags & (HideFlags.DontSave | HideFlags.HideAndDontSave)) != 0 ||
                NetworkAccessor.ModeOf(go) == NetworkMode.Never || NetworkSerialization.IsHiddenRoot(go))
            {
                return;
            }

            objects.Add(go);
            foreach (GameObject child in go.Children)
            {
                if (!child.IsDisposed)
                {
                    Collect(child);
                }
            }
        }
    }

    /// <summary>Reads the sections <see cref="WriteSections" /> wrote, as detached content.</summary>
    /// <exception cref="InvalidDataException">The sections can't be read.</exception>
    internal List<SectionData> ReadSections(EchoObject root, SceneInstance? linkTo = null)
    {
        SerializationContext context = linkTo.IsValid()
            ? new() { ExternalReferences = new NetworkSerialization.SnapshotReferences(new SceneReferenceResolver { ResolveIn = linkTo }) }
            : SnapshotContext();
        SceneContent?[]? contents = null;
        try
        {
            EchoObject? tree = root["Contents"];
            List<EchoObject>? headers = root["Sections"]?.List;
            contents = tree == null
                ? null
                : GameObject.PreservingIdentifiersWhile(() => Serializer.Deserialize<SceneContent?[]>(tree, context));
            if (tree != null && contents != null)
            {
                CollectLinks(tree, context);
            }
            if (contents == null || headers == null || headers.Count != contents.Length || contents.Length == 0)
            {
                throw new InvalidDataException("The host's sections could not be read.");
            }

            List<SectionData> sections = new(contents.Length);
            HashSet<uint> ids = [];
            for (int i = 0; i < contents.Length; i++)
            {
                EchoObject header = headers[i];
                SceneContent? content = contents[i];
                int? id = header.TagType == EchoType.Compound ? header["Id"]?.IntValue : null;
                bool active = header["Active"]?.BoolValue ?? false;
                if (content == null || id is not { } number || number < 0 || !ids.Add((uint)number) ||
                    (active && number == PersistentSectionId))
                {
                    throw new InvalidDataException("The host sent a malformed section.");
                }

                Scene? source = Guid.TryParse(header["Source"]?.StringValue, out Guid sourceId)
                    ? AssetDatabase.Get(sourceId) as Scene
                    : null;
                sections.Add(new SectionData((uint)number, header["Name"]?.StringValue ?? string.Empty, active, source, content));
            }

            return sections;
        }
        catch (Exception e)
        {
            foreach (SceneContent? content in contents ?? [])
            {
                content?.DisposeObjects();
            }

            // Anything wrong with what the host sent is malformed data, which ends the session.
            if (e is InvalidDataException)
            {
                throw;
            }

            throw new InvalidDataException($"The host's sections could not be read: {e.Message}", e);
        }
    }

    /// <summary>Reads a snapshot's sections, leaving the reader on the network state after them.</summary>
    /// <exception cref="InvalidDataException">The snapshot can't be read.</exception>
    internal SnapshotSections ReadSnapshot(ref NetReader reader) => ReadSnapshot(ref reader, out _);

    internal SnapshotSections ReadSnapshot(ref NetReader reader, out ulong generation)
    {
        using FrameStatScope stats = FrameStats.Network.Scope();
        SceneSnapshotMessage message = SceneSnapshotMessage.Read(ref reader);
        generation = message.Generation;
        SnapshotSections snapshot = new(ReadSections(Decompress(message.Sections)));
        if (!snapshot.Sections.Any(s => s.Id != PersistentSectionId))
        {
            snapshot.Dispose();
            throw new InvalidDataException("The host's snapshot has no section to load.");
        }

        return snapshot;
    }

    private static Dictionary<Guid, EngineObject> Incoming(List<SectionData> sections)
    {
        Dictionary<Guid, EngineObject> incoming = new();
        foreach (SectionData section in sections)
        {
            foreach ((Guid id, EngineObject obj) in section.Content.IdentifierMap())
            {
                incoming.TryAdd(id, obj);
            }
        }

        return incoming;
    }

    internal void OnSceneSnapshot(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost || !sender.IsHost)
        {
            return;
        }

        if (delivery != Delivery.Reliable)
        {
            throw new InvalidDataException("Scene snapshots must be reliable.");
        }

        NetSession? session = Session;
        // Releases whatever the world did not take, with its physics and rendering resources.
        using SnapshotSections snapshot = ReadSnapshot(ref reader, out ulong sceneGeneration);
        List<SectionData> sections = snapshot.Sections;
        if (Session != session || sceneGeneration <= SceneGeneration)
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
        CatchUpApplied = false;
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
        Index.Clear();
        IndexFrame = -1;
        Entries.Clear();
        Sync.Dirty.Clear();
        Snapshots.Clear();
        ForgetSections();

        // The incoming objects stay detached until the end, so they resolve through Spawning, spawn callbacks included.
        Spawning = Incoming(sections);
        try
        {
            uint count = ObjectStateBlock.Read(ref reader).Count;
            for (uint i = 0; i < count; i++)
            {
                SnapshotObjectMessage state = SnapshotObjectMessage.Read(ref reader);
                if (FindByIdentifier(state.Identifier) is GameObject go)
                {
                    go.Network.ApplyOrphaned(state.Orphaned);
                    NetworkObject entry = new(this, go)
                    {
                        Owner = state.OwnerId, Generation = state.Generation, NetId = state.NetId
                    };
                    Entries.Add(entry);
                    entry.UpdatePhysics();
                }
            }

            Sync.ApplySyncState(sender, ref reader, null, true);
            foreach (NetworkObject entry in Entries.Values.ToArray())
            {
                entry.NotifySpawn();
                if (Session != session)
                {
                    return;
                }
            }
        }
        finally
        {
            Spawning = null;
        }

        if (!reader.IsAtEnd)
        {
            throw new InvalidDataException("Scene snapshot contains trailing bytes.");
        }

        ApplySections(world, sections);
        IndexFrame = -1;
        if (Session != session)
        {
            return;
        }

        FillAllLinks();

        DispatchNetworkListener(e => e.OnNetworkLoading());
        if (Session != session)
        {
            return;
        }

        OnNetworkSceneActivated();
    }

    /// <summary>Replaces every section of <paramref name="world" /> but the persistent one with the host's, at once.</summary>
    private void ApplySections(SceneInstance world, List<SectionData> sections)
    {
        _applyingSections = true;
        try
        {
            // A load the client queued before joining would replace the host's sections again.
            world.PendingLoad?.Cancel();
            ReleasePreservedCopies(world, [.. Incoming(sections).Keys]);

            // Each section's content leaves the list once the world has it, so a failure part way disposes only the rest.
            SceneSection? active = null;
            bool replaced = false;
            for (int i = 0; i < sections.Count; i++)
            {
                SectionData data = sections[i];
                if (data.Id == PersistentSectionId)
                {
                    continue;
                }

                sections[i] = data with { Content = new SceneContent() };
                SceneSection section = replaced
                    ? world.AddContent(data.Content, data.Name, data.Source)
                    : world.ReplaceContent(data.Content, data.Name, data.Source);
                replaced = true;
                MapSection(data.Id, section);
                if (data.Active)
                {
                    active = section;
                }
            }

            for (int i = 0; i < sections.Count; i++)
            {
                SectionData data = sections[i];
                if (data.Id != PersistentSectionId)
                {
                    continue;
                }

                sections[i] = data with { Content = new SceneContent() };
                foreach (GameObject root in data.Content.Roots.ToArray())
                {
                    world.Add(root, world.Persistent);
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

    /// <summary>Persistent objects the incoming snapshot brings again go with the sections it replaces instead.</summary>
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

        NetSession? session = Session;
        SceneInstance world = ReplicatedWorld!;
        SectionAddMessage message = SectionAddMessage.Read(ref reader);
        if (message.Generation != SceneGeneration)
        {
            return;
        }

        List<SectionData> sections = ReadSections(Decompress(message.Sections), world);
        List<NetworkObject> added = [];
        bool applied = false, notified = false;
        try
        {
            if (sections.Count != 1 || sections[0].Id == PersistentSectionId || SectionOf(sections[0].Id) != null)
            {
                throw new InvalidDataException("The host sent a malformed section.");
            }

            SectionData data = sections[0];
            Spawning = Incoming(sections);
            try
            {
                uint count = ObjectStateBlock.Read(ref reader).Count;
                for (uint i = 0; i < count; i++)
                {
                    SnapshotObjectMessage state = SnapshotObjectMessage.Read(ref reader);
                    if (!Entries.Contains(state.Identifier) && FindByIdentifier(state.Identifier) is GameObject go)
                    {
                        go.Network.ApplyOrphaned(state.Orphaned);
                        NetworkObject entry = new(this, go)
                        {
                            Owner = state.OwnerId, Generation = state.Generation, NetId = state.NetId
                        };
                        Entries.Add(entry);
                        added.Add(entry);
                        entry.UpdatePhysics();
                    }
                }

                Sync.ApplySyncState(sender, ref reader, null, true, true);
                if (Session != session || ReplicatedWorld != world)
                {
                    return;
                }

                notified = true;
                foreach (NetworkObject entry in added)
                {
                    entry.NotifySpawn();
                    if (Session != session)
                    {
                        return;
                    }
                }
            }
            finally
            {
                Spawning = null;
            }

            SceneSection section;
            _applyingSections = true;
            try
            {
                section = world.AddContent(data.Content, data.Name, data.Source);
            }
            finally
            {
                _applyingSections = false;
            }

            applied = true;
            MapSection(data.Id, section);
            if (data.Active)
            {
                world.ActiveSection = section;
            }

            foreach (GameObject root in section.Roots)
            {
                IndexTree(root);
            }

            FillAllLinks();
            DispatchSectionListener(section, e => e.OnNetworkLoading());
            if (Session == session && IsReady)
            {
                DispatchSectionListener(section, e => e.OnNetworkReady());
            }
        }
        finally
        {
            if (!applied)
            {
                foreach (NetworkObject entry in added)
                {
                    if (Entries.TryGetValue(entry.GameObject.Identifier, out NetworkObject? current) && current == entry)
                    {
                        if (notified && Session == session)
                        {
                            entry.NotifyDespawn();
                        }

                        Entries.Remove(entry.GameObject.Identifier);
                    }
                }

                foreach (SectionData section in sections)
                {
                    section.Content.DisposeObjects();
                }
            }
        }
    }

    internal void OnSectionRemove(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost || !sender.IsHost)
        {
            return;
        }

        SectionRemoveMessage message = SectionRemoveMessage.Read(ref reader);
        if (delivery != Delivery.Reliable || !reader.IsAtEnd)
        {
            throw new InvalidDataException("Invalid section removal.");
        }

        if (message.Generation != SceneGeneration || message.SectionId == PersistentSectionId ||
            SectionOf(message.SectionId) is not { } section)
        {
            return;
        }

        _sectionIds.Remove(section);
        _sectionsById.Remove(message.SectionId);
        SceneInstance world = section.Instance;
        _applyingSections = true;
        try
        {
            // A world keeps at least one section, so the last one gives way to an empty one.
            if (world.Sections.Count(s => !s.IsPersistent) < 2)
            {
                world.ReplaceContent(new SceneContent(), section.Name, null);
            }
            else
            {
                world.UnloadNow(section);
            }
        }
        finally
        {
            _applyingSections = false;
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

            if (NetworkAccessor.ModeOf(go) == NetworkMode.Object && go.Network.Root == go &&
                !NetworkAccessor.IsExcludedFromNetwork(go) &&
                !Entries.Contains(go.Identifier))
            {
                NetworkObject entry = new(this, go) { Generation = NextGeneration(), NetId = NextNetId() };
                Entries.Add(entry);
                InitVisibility(entry);
                entry.NotifySpawn();
            }
        }
    }

    // Writing, a reference to a NetworkMode.Never object becomes an empty link; reading, that link becomes null.
    internal SerializationContext SnapshotContext() =>
        new() { ExternalReferences = new NetworkSerialization.SnapshotReferences() };

    internal byte[] Compress(EchoObject echo)
    {
        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.Fastest, true))
        using (BinaryWriter writer = new(zlib))
        {
            // SnapshotDecoder validates this fixed-width wire layout before Echo reads it.
            echo.WriteToBinary(writer,
                new BinarySerializationOptions { EncodingMode = BinaryEncodingMode.Performance });
        }

        return compressed.ToArray();
    }

    internal EchoObject Decompress(ReadOnlySpan<byte> data)
    {
        try
        {
            return SnapshotDecoder.Decode(data, Configuration);
        }
        catch (Exception e) when (e is not InvalidDataException)
        {
            // Reported to the session as malformed data, which ends it, rather than escaping into the game loop.
            throw new InvalidDataException($"Network data could not be read: {e.Message}", e);
        }
    }

    internal void AsSetUp(Action write)
    {
        List<Rigidbody3D> switched = new();
        foreach (NetworkObject entry in Entries.Values)
        foreach (KeyValuePair<Rigidbody3D, MotionType> body in entry.ProxyBodies)
        {
            if (body.Key.IsValid())
            {
                body.Key.MotionType = body.Value;
                switched.Add(body.Key);
            }
        }

        try
        {
            write();
        }
        finally
        {
            foreach (Rigidbody3D body in switched)
            {
                body.MotionType = MotionType.Kinematic;
            }
        }
    }
}
