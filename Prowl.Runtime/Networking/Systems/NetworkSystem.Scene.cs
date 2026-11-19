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
        Sync.Clear();
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

        SendScene(session.Peers.Where(peer => !peer.IsLocal).ToArray());
    }

    /// <summary>The objects of a section that may be networked: everything saved that is not under a server only object or a hidden root.</summary>
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
                NetworkAccessor.ServerOnlyOf(go) || NetworkSerialization.IsHiddenRoot(go))
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

}
