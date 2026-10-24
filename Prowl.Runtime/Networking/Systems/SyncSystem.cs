// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Warp;

namespace Prowl.Runtime.Networking;

// [Sync] properties. Changes are collected as they happen and sent once per tick, reliably: from the
// host to every client, or from a client to the host, which applies them and passes them on. A reliable RPC
// or an ownership handoff sends them early, so they arrive ahead of it.
//
// A block of values, as in a Sync message, a snapshot or a spawn, is
// [varuint count] then per value [guid component][varuint slot][varuint ownership generation][bytes value, length prefixed].
// Ongoing Sync messages prefix this block with [i32 original writer]. The length lets a receiver skip values for
// components it doesn't have or won't accept.
//
// Each value has one writer at a time (its controller, or the host for FromHost ones), whose changes travel in
// order on one reliable channel, relayed in order by the host. So the newest value is simply the last to arrive;
// an earlier controller's late changes are told apart by their ownership generation.
//
// A network collection's value is its changes since the last tick, or all of it in a snapshot or spawn;
// NetCollection has the layout. Either way it travels, is relayed and is authorized like any other value.
internal sealed class SyncSystem(NetworkSystem scene) : IDisposable
{
    internal readonly HashSet<(Component Component, uint Slot)> Dirty = new();
    internal readonly List<Action> _notifications = new();
    internal readonly HashSet<string> _syncWarnings = new();
    internal readonly NetWriter _valueWriter = new();
    internal int _applying;
    public void Dispose() => _valueWriter.Dispose();

    internal static void NotifyChange(Action notification)
    {
        if (Networking.System is { } system)
        {
            system.Sync.Notify(notification);
        }
        else
        {
            notification();
        }
    }

    /// <summary>Queues a [Sync] value to be sent. False if its object is never networked, so it won't be.</summary>
    internal bool MarkSyncDirty(Component component, uint slot)
    {
        if (NetworkAccessor.IsExcludedFromNetwork(component.GameObject))
        {
            return false;
        }

        Dirty.Add((component, slot));
        return true;
    }

    internal void WarnSyncOnce(Component component, SyncMember sync, string problem)
    {
        NetworkDiagnostics.TraceMember("SyncVar", "Rejected", scene.Session?.LocalPeer, component, sync.Slot, false,
            problem, default);
        if (_syncWarnings.Add($"{component.GetType().FullName}:{sync.Slot}:{problem}"))
        {
            Debug.LogWarning($"[Network] {component.GetType().Name}.{sync.Name} {problem}, so the change was dropped.");
        }
    }

    /// <summary>Whether the peer whose values are being applied may change this [Sync] property.</summary>
    internal bool AcceptSync(NetPeer sender, Component component, SyncFlags flags)
    {
        // The host is the authority over everything; a client only over what it owns.
        if (sender.IsHost)
        {
            return true;
        }

        return (flags & SyncFlags.FromHost) == 0 && scene.OwnerOf(component.GameObject) == sender.Id;
    }

    internal void SendSyncChanges()
    {
        if (Dirty.Count == 0)
        {
            return;
        }

        using NetWriter message = new(1024);
        int count = 0;
        using NetWriter values = new(1024);
        foreach ((Component component, uint slot) in Dirty)
        {
            if (!component.IsDisposed && WriteValue(values, component, slot, true))
            {
                count++;
            }
        }

        Dirty.Clear();

        if (count == 0)
        {
            return;
        }

        new SyncMessage(scene.Session!.LocalPeer!.Id).Write(message);
        new SyncBlock((uint)count).Write(message);
        message.WriteBytes(values.WrittenSpan);

        if (scene.IsHost)
        {
            scene.BroadcastEngine(EngineMessage.Sync, message.WrittenSpan);
        }
        else
        {
            scene.SendEngine(scene.Session.HostPeer!, EngineMessage.Sync, message.WrittenSpan);
        }
    }

    /// <summary>Writes one value: what changed since it was last sent if <paramref name="changes" />, else all of it.</summary>
    internal bool WriteValue(NetWriter output, Component component, uint slot, bool changes)
    {
        if (NetworkMembers.Of(component).Sync(slot) is not { } sync)
        {
            return false;
        }

        _valueWriter.Reset();
        if (changes)
        {
            sync.WriteChanges(component, _valueWriter);
            sync.ChangesSent(component);
        }
        else
        {
            sync.Write(component, _valueWriter);
        }

        new SyncValue(component.Identifier, slot, scene.GenerationOf(component.GameObject),
            _valueWriter.WrittenSpan).Write(output);
        NetworkDiagnostics.TraceMember("SyncVar", "Serialize", scene.Session?.LocalPeer, component, slot, false,
            "Serialized",
            _valueWriter.WrittenSpan);
        return true;
    }

    /// <summary>Writes every [Sync] value of every component on <paramref name="objects" />, for a snapshot or spawn.</summary>
    internal void WriteSyncState(NetWriter output, IEnumerable<GameObject> objects)
    {
        using NetWriter values = new(1024);
        int count = 0;
        foreach (GameObject go in objects)
        {
            if (NetworkAccessor.IsExcludedFromNetwork(go))
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

        new SyncBlock((uint)count).Write(output);
        output.WriteBytes(values.WrittenSpan);
    }

    internal void OnSync(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (!scene.IsHost && !sender.IsHost)
        {
            return;
        }

        NetSession? session = scene.Session;
        int originId = SyncMessage.Read(ref reader).OriginId;
        if (!sender.IsHost && originId != sender.Id)
        {
            throw new InvalidDataException("Invalid Sync origin.");
        }

        if (!scene.Session!.TryGetPeer(originId, out NetPeer? origin))
        {
            return;
        }

        using NetWriter? relay = scene.IsHost ? new NetWriter(1024) : null;
        int relayed = ApplySyncState(origin, ref reader, relay, false, true);

        // The host passes on what it accepted from a client to everyone else.
        if (scene.Session == session && relay != null && relayed > 0)
        {
            using NetWriter message = new(relay.Length + 8);
            new SyncMessage(originId).Write(message);
            new SyncBlock((uint)relayed).Write(message);
            message.WriteBytes(relay.WrittenSpan);
            scene.BroadcastEngine(EngineMessage.Sync, message.WrittenSpan, except: sender);
        }
    }

    /// <summary>
    ///     Applies a block of values from <paramref name="sender" />. Values it may not change are skipped.
    ///     Accepted ones are copied to <paramref name="relay" /> when given; returns how many were.
    ///     <paramref name="initial" /> for a snapshot or spawn, whose values change nothing that was running,
    ///     so nothing is notified. Otherwise notifications run once every value in the block is in place.
    /// </summary>
    internal int ApplySyncState(NetPeer sender, ref NetReader reader, NetWriter? relay, bool initial,
        bool requireEnd = false)
    {
        // Validate all framing and accepted values before changing live state or relays.
        // Collection validation only reads, so partial operations cannot leak out.
        ValidateSyncState(sender, reader, initial, requireEnd);
        int relayed = 0;
        uint count = SyncBlock.Read(ref reader).Count;
        int notificationsFrom = _notifications.Count;
        _applying++;
        try
        {
            for (uint i = 0; i < count; i++)
            {
                SyncValue message = SyncValue.Read(ref reader);
                Guid id = message.ComponentId;
                uint slot = message.Slot, generation = message.Generation;
                ReadOnlySpan<byte> value = message.Value;

                if (scene.FindByIdentifier(id) is not Component component ||
                    NetworkAccessor.IsExcludedFromNetwork(component.GameObject))
                {
                    NetworkDiagnostics.Record("SyncVar", "Rejected", sender, id,
                        $"Unknown or excluded component, slot #{slot}", value);
                    continue;
                }

                SyncMember? sync = NetworkMembers.Of(component).Sync(slot);
                bool accepted = sync != null && AcceptSync(sender, component, sync.Flags) &&
                                (initial || generation == scene.GenerationOf(component.GameObject));
                if (accepted)
                {
                    NetReader valueReader = new(value);
                    sync!.Read(component, ref valueReader, initial);
                }

                NetworkDiagnostics.TraceMember("SyncVar", accepted ? "Apply" : "Rejected", sender, component, slot,
                    false,
                    accepted ? "Applied" : "Unknown slot or unauthorized", value);
                if (accepted && relay != null)
                {
                    new SyncValue(id, slot, generation, value).Write(relay);
                    relayed++;
                }
            }
        }
        catch
        {
            _notifications.RemoveRange(notificationsFrom, _notifications.Count - notificationsFrom);
            throw;
        }
        finally
        {
            _applying--;
        }

        RunNotifications(notificationsFrom);
        return relayed;
    }

    internal void ValidateSyncState(NetPeer sender, NetReader reader, bool initial, bool requireEnd)
    {
        uint count = SyncBlock.Read(ref reader).Count;
        for (uint i = 0; i < count; i++)
        {
            SyncValue message = SyncValue.Read(ref reader);
            Guid id = message.ComponentId;
            uint slot = message.Slot, generation = message.Generation;
            NetReader value = new(message.Value);
            if (scene.FindByIdentifier(id) is not Component component ||
                NetworkAccessor.IsExcludedFromNetwork(component.GameObject) ||
                NetworkMembers.Of(component).Sync(slot) is not { } sync || !AcceptSync(sender, component, sync.Flags) ||
                (!initial && generation != scene.GenerationOf(component.GameObject)))
            {
                continue;
            }

            try
            {
                sync.Validate(component, ref value, initial);
            }
            catch (Exception e) when (e is ArgumentException or OverflowException or FormatException
                                          or IndexOutOfRangeException)
            {
                throw new InvalidDataException("Sync contains a malformed value.", e);
            }

            if (!value.IsAtEnd)
            {
                throw new InvalidDataException("Sync value contains trailing bytes.");
            }
        }

        if (requireEnd && !reader.IsAtEnd)
        {
            throw new InvalidDataException("Sync message contains trailing bytes.");
        }
    }

    /// <summary>
    ///     Runs game code that observes a received change: a [Change] hook, or a network collection's Changed
    ///     handler. While a block of values is being applied it waits for the rest, so it sees them all.
    /// </summary>
    internal void Notify(Action notification)
    {
        if (_applying > 0)
        {
            _notifications.Add(notification);
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
    internal void RunNotifications(int from)
    {
        try
        {
            for (int i = from; i < _notifications.Count; i++)
            {
                try
                {
                    _notifications[i]();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Network] A [Change] hook or Changed handler threw: {e}");
                }
            }
        }
        finally
        {
            _notifications.RemoveRange(from, _notifications.Count - from);
        }
    }
}
