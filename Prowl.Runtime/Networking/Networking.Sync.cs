// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Warp;

namespace Prowl.Runtime.Networking;

// [Sync] properties. Changes are collected as they happen and sent once per tick, reliably: from the
// host to every client, or from a client to the host, which applies them and passes them on.
//
// A block of values, as in a Sync message, a snapshot or a spawn, is
// [varuint count] then per value [guid component][varuint slot][bytes value, length prefixed].
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

    // The peer whose values are being applied.
    private static NetPeer? s_syncSender;

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
    private static bool AcceptSync(Component component, SyncFlags flags)
    {
        NetPeer? sender = s_syncSender;
        if (sender == null)
        {
            return false;
        }

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
    private static bool WriteValue(NetWriter output, Component component, uint slot, bool changes)
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
        else
        {
            sync.Write(component, s_valueWriter);
        }

        output.WriteGuid(component.Identifier);
        output.WriteVarUInt32(slot);
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

    private static void OnSync(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (!IsHost && !sender.IsHost)
        {
            return;
        }

        using NetWriter? relay = IsHost ? new NetWriter(1024) : null;
        int relayed = ApplySyncState(sender, ref reader, relay, false);

        // The host passes on what it accepted from a client to everyone else.
        if (relay != null && relayed > 0)
        {
            using NetWriter message = new(relay.Length + 8);
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
    private static int ApplySyncState(NetPeer sender, ref NetReader reader, NetWriter? relay, bool initial)
    {
        int relayed = 0;
        uint count = reader.ReadVarUInt32();
        NetPeer? previous = s_syncSender;
        s_syncSender = sender;
        int notificationsFrom = s_notifications.Count;
        s_applying++;
        try
        {
            for (uint i = 0; i < count; i++)
            {
                Guid id = reader.ReadGuid();
                uint slot = reader.ReadVarUInt32();
                ReadOnlySpan<byte> value = reader.ReadBytesWithLength();

                if (FindByIdentifier(id) is not Component component || component.GameObject.IsNetworkExcluded)
                {
                    NetworkDiagnostics.Record("SyncVar", "Rejected", sender, id,
                        $"Unknown or excluded component, slot #{slot}", value);
                    continue;
                }

                SyncMember? sync = NetworkMembers.Of(component).Sync(slot);
                bool accepted = sync != null && AcceptSync(component, sync.Flags);
                if (accepted)
                {
                    NetReader valueReader = new(value);
                    sync!.Read(component, ref valueReader, initial);
                }

                TraceMember("SyncVar", accepted ? "Apply" : "Rejected", sender, component, slot, false,
                    accepted ? "Applied" : "Unknown slot or unauthorized", value);
                if (accepted && relay != null)
                {
                    relay.WriteGuid(id);
                    relay.WriteVarUInt32(slot);
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
            s_syncSender = previous;
            s_applying--;
        }

        RunNotifications(notificationsFrom);
        return relayed;
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
