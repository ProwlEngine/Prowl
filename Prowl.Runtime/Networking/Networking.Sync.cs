// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Warp;

namespace Prowl.Runtime.Networking;

// [Sync] properties. Changes are collected as they happen and sent once per tick, reliably: from the
// host to every client, or from a client to the host, which applies them and passes them on.
//
// A block of values, as in a Sync message, a snapshot or a spawn, is
// [varuint count] then per value [guid component][varuint slot][u32 ownership generation][bytes value, length prefixed].
// Ongoing Sync messages prefix this block with [i32 original writer][u64 state revision].
// The length lets a receiver skip values for components it doesn't have or won't accept.
//
// A network collection's value is its changes since the last tick, or all of it in a snapshot or spawn;
// NetCollection has the layout. Either way it travels, is relayed and is authorized like any other value.
public static partial class Networking
{
    private static readonly HashSet<(Component Component, uint Slot)> s_dirty = new();
    private static readonly NetWriter s_valueWriter = new();
    private static readonly HashSet<string> s_syncWarnings = new();
    private static readonly List<Action> s_notifications = new();
    private static int s_applying;
    private static ulong s_outgoingStateRevision;
    private static readonly Dictionary<(Guid Component, uint Slot, int Origin, uint Generation), ulong> s_stateRevisions = new();

    /// <summary>Goes up when a session ends, so what was queued for one is never sent in the next.</summary>
    internal static int SessionNumber { get; private set; }

    /// <summary>Queues a [Sync] value to be sent. False if its object is never networked, so it won't be.</summary>
    internal static bool MarkSyncDirty(Component component, uint slot)
    {
        if (component.GameObject.IsNetworkExcluded)
        {
            return false;
        }

        s_dirty.Add((component, slot));
        return true;
    }

    internal static void WarnSyncOnce(Component component, SyncMember sync, string problem)
    {
        TraceMember("SyncVar", "Rejected", Session?.LocalPeer, component, sync.Slot, false, problem, default);
        if (s_syncWarnings.Add($"{component.GetType().FullName}:{sync.Slot}:{problem}"))
        {
            Debug.LogWarning($"[Network] {component.GetType().Name}.{sync.Name} {problem}, so the change was dropped.");
        }
    }

    /// <summary>Whether the peer whose values are being applied may change this [Sync] property.</summary>
    private static bool AcceptSync(NetPeer sender, Component component, SyncFlags flags)
    {
        // The host is the authority over everything; a client only over what it owns.
        if (sender.IsHost)
        {
            return true;
        }

        return (flags & SyncFlags.FromHost) == 0 && OwnerOf(component.GameObject) == sender.Id;
    }

    private static void SendSyncChanges()
    {
        if (s_dirty.Count == 0)
        {
            return;
        }

        using NetWriter message = new(1024);
        int count = 0;
        using NetWriter values = new(1024);
        foreach ((Component component, uint slot) in s_dirty)
        {
            if (!component.IsDisposed && WriteValue(values, component, slot, true))
            {
                count++;
            }
        }

        s_dirty.Clear();

        if (count == 0)
        {
            return;
        }

        message.WriteInt32(Session!.LocalPeer!.Id);
        message.WriteUInt64(checked(++s_outgoingStateRevision));
        message.WriteVarUInt32((uint)count);
        message.WriteBytes(values.WrittenSpan);

        if (IsHost)
        {
            BroadcastEngine(EngineMessage.Sync, message.WrittenSpan);
        }
        else
        {
            SendEngine(Session.HostPeer!, EngineMessage.Sync, message.WrittenSpan);
        }
    }

    /// <summary>Writes one value: what changed since it was last sent if <paramref name="changes" />, else all of it.</summary>
    private static bool WriteValue(NetWriter output, Component component, uint slot, bool changes, bool rpc = false)
    {
        if (NetworkMembers.Of(component).Sync(slot) is not { } sync)
        {
            return false;
        }

        s_valueWriter.Reset();
        if (changes)
        {
            sync.WriteChanges(component, s_valueWriter);
            sync.ChangesSent(component);
        }
        else if (rpc)
        {
            sync.WriteRpc(component, s_valueWriter);
        }
        else
        {
            sync.Write(component, s_valueWriter);
        }

        output.WriteGuid(component.Identifier);
        output.WriteVarUInt32(slot);
        output.WriteUInt32(GenerationOf(component.GameObject));
        output.WriteBytesWithLength(s_valueWriter.WrittenSpan);
        TraceMember("SyncVar", "Serialize", Session?.LocalPeer, component, slot, false, "Serialized",
            s_valueWriter.WrittenSpan);
        return true;
    }

    /// <summary>Writes every [Sync] value of every component on <paramref name="objects" />, for a snapshot or spawn.</summary>
    private static void WriteSyncState(NetWriter output, IEnumerable<GameObject> objects)
    {
        using NetWriter values = new(1024);
        int count = 0;
        foreach (GameObject go in objects)
        {
            if (go.IsNetworkExcluded)
            {
                continue;
            }

            foreach (Component component in go.GetComponents<Component>())
            {
                foreach (SyncMember sync in NetworkMembers.Of(component).Syncs)
                {
                    if (WriteValue(values, component, sync.Slot, false))
                    {
                        count++;
                    }
                }
            }
        }

        output.WriteVarUInt32((uint)count);
        output.WriteBytes(values.WrittenSpan);
    }

    private static void ClearStateRevisions(GameObject root)
    {
        HashSet<Guid> identifiers = new();
        foreach (GameObject go in root.GetChildrenDeep().Prepend(root))
        {
            identifiers.Add(go.Identifier);
            foreach (Component part in go.GetComponents())
                identifiers.Add(part.Identifier);
        }
        foreach (var key in s_stateRevisions.Keys.ToArray())
            if (identifiers.Contains(key.Component))
                s_stateRevisions.Remove(key);
    }

    private static void OnSync(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (!IsHost && !sender.IsHost)
        {
            return;
        }

        NetSession? session = Session;
        int originId = reader.ReadInt32();
        ulong revision = reader.ReadUInt64();
        if ((!sender.IsHost && originId != sender.Id) || revision == 0)
            throw new InvalidDataException("Invalid Sync origin.");
        if (!Session!.TryGetPeer(originId, out NetPeer? origin))
            return;
        using NetWriter? relay = IsHost ? new NetWriter(1024) : null;
        int relayed = ApplySyncState(origin, ref reader, relay, false, revision, requireEnd: true);

        // The host passes on what it accepted from a client to everyone else.
        if (Session == session && relay != null && relayed > 0)
        {
            using NetWriter message = new(relay.Length + 8);
            message.WriteInt32(originId);
            message.WriteUInt64(revision);
            message.WriteVarUInt32((uint)relayed);
            message.WriteBytes(relay.WrittenSpan);
            BroadcastEngine(EngineMessage.Sync, message.WrittenSpan, except: sender);
        }
    }

    /// <summary>
    ///     Applies a block of values from <paramref name="sender" />. Values it may not change are skipped.
    ///     Accepted ones are copied to <paramref name="relay" /> when given; returns how many were.
    ///     <paramref name="initial" /> for a snapshot or spawn, whose values change nothing that was running,
    ///     so nothing is notified. Otherwise notifications run once every value in the block is in place.
    /// </summary>
    private static int ApplySyncState(NetPeer sender, ref NetReader reader, NetWriter? relay, bool initial, ulong revision = 0, bool requireEnd = false)
    {
        // Validate all framing and accepted values before changing live state, revisions, or relays.
        // Collection validation reads into a detached copy, so partial operations cannot leak out.
        ValidateSyncState(sender, reader, initial, revision, requireEnd);
        int relayed = 0;
        uint count = reader.ReadVarUInt32();
        int notificationsFrom = s_notifications.Count;
        s_applying++;
        try
        {
            for (uint i = 0; i < count; i++)
            {
                Guid id = reader.ReadGuid();
                uint slot = reader.ReadVarUInt32();
                uint generation = reader.ReadUInt32();
                ReadOnlySpan<byte> value = reader.ReadBytesWithLength();

                if (FindByIdentifier(id) is not Component component || component.GameObject.IsNetworkExcluded)
                {
                    NetworkDiagnostics.Record("SyncVar", "Rejected", sender, id,
                        $"Unknown or excluded component, slot #{slot}", value);
                    continue;
                }

                SyncMember? sync = NetworkMembers.Of(component).Sync(slot);
                bool accepted = sync != null && AcceptSync(sender, component, sync.Flags) &&
                    (initial || generation == GenerationOf(component.GameObject));
                var revisionKey = (component.Identifier, slot, sender.Id, generation);
                if (accepted && revision != 0 && s_stateRevisions.GetValueOrDefault(revisionKey) >= revision)
                    accepted = false;
                if (accepted)
                {
                    NetReader valueReader = new(value);
                    sync!.Read(component, ref valueReader, initial);
                    if (revision != 0)
                        s_stateRevisions[revisionKey] = revision;
                }

                TraceMember("SyncVar", accepted ? "Apply" : "Rejected", sender, component, slot, false,
                    accepted ? "Applied" : "Unknown slot or unauthorized", value);
                if (accepted && relay != null)
                {
                    relay.WriteGuid(id);
                    relay.WriteVarUInt32(slot);
                    relay.WriteUInt32(generation);
                    relay.WriteBytesWithLength(value);
                    relayed++;
                }
            }
        }
        catch
        {
            s_notifications.RemoveRange(notificationsFrom, s_notifications.Count - notificationsFrom);
            throw;
        }
        finally
        {
            s_applying--;
        }

        RunNotifications(notificationsFrom);
        return relayed;
    }

    private static void ValidateSyncState(NetPeer sender, NetReader reader, bool initial, ulong revision, bool requireEnd)
    {
        uint count = reader.ReadVarUInt32();
        // Each record needs a guid, slot, generation, and value length, even when it is skipped.
        if (count > reader.Remaining / 22)
            throw new InvalidDataException("Sync count exceeds the remaining data.");
        HashSet<(Guid, uint, uint)>? seen = revision == 0 ? null : new();
        for (uint i = 0; i < count; i++)
        {
            Guid id = reader.ReadGuid();
            uint slot = reader.ReadVarUInt32();
            uint generation = reader.ReadUInt32();
            NetReader value = new(reader.ReadBytesWithLength());
            if (FindByIdentifier(id) is not Component component || component.GameObject.IsNetworkExcluded ||
                NetworkMembers.Of(component).Sync(slot) is not { } sync || !AcceptSync(sender, component, sync.Flags) ||
                (!initial && generation != GenerationOf(component.GameObject)))
                continue;
            // Match application: only the first accepted value for a slot advances this revision.
            if (revision != 0 && (s_stateRevisions.GetValueOrDefault((id, slot, sender.Id, generation)) >= revision ||
                !seen!.Add((id, slot, generation))))
                continue;
            try
            {
                sync.Validate(component, ref value, initial);
            }
            catch (Exception e) when (e is ArgumentException or OverflowException or FormatException or IndexOutOfRangeException)
            {
                throw new InvalidDataException("Sync contains a malformed value.", e);
            }
            if (!value.IsAtEnd)
                throw new InvalidDataException("Sync value contains trailing bytes.");
        }
        if (requireEnd && !reader.IsAtEnd)
            throw new InvalidDataException("Sync message contains trailing bytes.");
    }

    /// <summary>
    ///     Runs game code that observes a received change: a [Change] hook, or a network collection's Changed
    ///     handler. While a block of values is being applied it waits for the rest, so it sees them all.
    /// </summary>
    internal static void Notify(Action notification)
    {
        if (s_applying > 0)
        {
            s_notifications.Add(notification);
        }
        else
        {
            notification();
        }
    }

    /// <summary>
    ///     Runs the queued notifications from <paramref name="from" /> on, in order. The game's code throwing is logged,
    ///     not passed on.
    /// </summary>
    private static void RunNotifications(int from)
    {
        try
        {
            for (int i = from; i < s_notifications.Count; i++)
            {
                try
                {
                    s_notifications[i]();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Network] A [Change] hook or Changed handler threw: {e}");
                }
            }
        }
        finally
        {
            s_notifications.RemoveRange(from, s_notifications.Count - from);
        }
    }
}
