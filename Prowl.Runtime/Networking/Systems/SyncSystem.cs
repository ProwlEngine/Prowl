// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

using Prowl.Courier;

using static Prowl.Runtime.Networking.NetworkSystem;

namespace Prowl.Runtime.Networking;

// [Sync] state, as Mirror's SyncVars: each networked component has a dirty bit per [Sync] member, and once a tick
// the changed members go out reliably and in order. The host writes each changed object once, into a record for its
// owner and one for everyone else, and sends each peer the one it gets. A client sends the host the FromOwner
// members of what it owns, and the host applies them and passes them on to everyone but that owner. A reliable RPC
// sends the pending changes first, on the same channel, so a call always arrives after the state set before it.
//
// A state message is [varuint count] then per object a record:
// [varuint NetId][varuint ownership generation][varuint64 component mask], then for each component in the mask
// [bytes, length prefixed]: [varuint64 member mask] and the value of each member in it, in member order.
// A spawn carries the same per component blocks for every component, with every member.
//
// The length lets a receiver skip a component it lacks. A record for an object it doesn't have is skipped whole.
// A record from a peer that doesn't own the object, or from an earlier ownership, is dropped.
//
// A network collection's value is its changes since they were last sent, or all of it in a spawn. It carries its
// own sequence number, so changes a spawn already held are read past, not applied twice.
internal sealed class SyncSystem(NetworkSystem scene) : IDisposable
{
    internal readonly List<Action> _notifications = new();
    internal readonly HashSet<(Type Type, uint Slot, string Problem)> _syncWarnings = new();
    internal int _applying;

    // Objects with dirty members, each once.
    private readonly List<NetworkObject> _dirty = new();
    private readonly List<NetworkObject> _flushing = new();

    // Records waiting to go to each peer, by peer id, with how many, and how much of the shared records is in them.
    private readonly Dictionary<int, (NetWriter Records, int Count, int SharedBytes, int SharedCount)> _batches = new();
    private readonly Stack<NetWriter> _writerPool = new();

    // Records every client gets, written once. A peer with records of its own takes these in between, in order.
    private readonly NetWriter _shared = new(4096);
    private readonly List<NetPeer> _sharedPeers = new();
    private int _sharedCount;
    private readonly NetWriter _value = new(256);
    private readonly NetWriter _observerValues = new(256), _ownerValues = new(256);
    private readonly NetWriter _observerBlocks = new(512), _ownerBlocks = new(512);
    private readonly NetWriter _record = new(512), _message = new(1024);

    /// <summary>How many objects have [Sync] changes not sent yet.</summary>
    internal int PendingCount => _dirty.Count;

    public void Dispose()
    {
        _value.Dispose();
        _observerValues.Dispose();
        _ownerValues.Dispose();
        _observerBlocks.Dispose();
        _ownerBlocks.Dispose();
        _record.Dispose();
        _message.Dispose();
        _shared.Dispose();
        foreach ((NetWriter records, _, _, _) in _batches.Values)
        {
            records.Dispose();
        }

        foreach (NetWriter writer in _writerPool)
        {
            writer.Dispose();
        }

        _batches.Clear();
        _writerPool.Clear();
    }

    /// <summary>Forgets every change not sent yet, as when the scene changes.</summary>
    internal void Clear()
    {
        foreach (NetworkObject entry in _dirty)
        {
            entry.StateDirty = false;
            entry.Dirty = null;
        }

        _dirty.Clear();
        foreach ((NetWriter records, _, _, _) in _batches.Values)
        {
            records.Reset();
            _writerPool.Push(records);
        }

        _batches.Clear();
        _shared.Reset();
        _sharedCount = 0;
        _spawnHooks.Clear();
        _references.Clear();
        _referenceOf.Clear();
    }

    internal void RemovePeer(int peerId)
    {
        if (_batches.Remove(peerId, out (NetWriter Records, int Count, int SharedBytes, int SharedCount) batch))
        {
            batch.Records.Reset();
            _writerPool.Push(batch.Records);
        }
    }

    // [Sync] references received, by the object each names, and what each member names. As Mirror's reference SyncVars
    // look their object up when read, a reference to an object that arrives later starts reading as that object.
    private readonly Dictionary<uint, List<(Component Component, SyncMember Sync, byte Index)>> _references = new();
    private readonly Dictionary<(Component Component, uint Slot), uint> _referenceOf = new();

    /// <summary>Remembers what a reference member was just told to name, or forgets it for a null or local value.</summary>
    internal void NoteReference(Component component, SyncMember sync, (uint NetId, byte Index) reference)
    {
        (Component, uint) key = (component, sync.Slot);
        if (_referenceOf.Remove(key, out uint previous) &&
            _references.TryGetValue(previous, out List<(Component Component, SyncMember Sync, byte Index)>? named))
        {
            named.RemoveAll(e => ReferenceEquals(e.Component, component) && e.Sync.Slot == sync.Slot);
            if (named.Count == 0)
            {
                _references.Remove(previous);
            }
        }

        if (reference.NetId == 0)
        {
            return;
        }

        _referenceOf[key] = reference.NetId;
        if (!_references.TryGetValue(reference.NetId, out named))
        {
            _references[reference.NetId] = named = new List<(Component, SyncMember, byte)>();
        }

        named.Add((component, sync, reference.Index));
    }

    /// <summary><paramref name="entry" /> arrived on this peer: the reference members naming it read as it from now.</summary>
    internal void ResolveReferencesTo(NetworkObject entry)
    {
        if (!_references.TryGetValue(entry.NetId, out List<(Component Component, SyncMember Sync, byte Index)>? named))
        {
            return;
        }

        for (int i = named.Count - 1; i >= 0; i--)
        {
            (Component component, SyncMember sync, byte index) = named[i];
            if (component.IsNotValid())
            {
                named.RemoveAt(i);
                _referenceOf.Remove((component, sync.Slot));
                continue;
            }

            object? target = index == NetSerializers.RootIndex ? entry.GameObject
                : index < entry.Components.Length ? entry.Components[index]
                : null;
            if (target != null)
            {
                sync.Resolve(component, target);
            }
        }

        if (named.Count == 0)
        {
            _references.Remove(entry.NetId);
        }
    }

    // Hooks for values that came with a spawn, held until the object's spawn callbacks are due.
    private readonly List<(Component Component, Action Hook)> _spawnHooks = new();

    internal static void NotifySpawnChange(Component component, Action hook)
    {
        if (Networking.System is { } system)
        {
            system.Sync._spawnHooks.Add((component, hook));
        }
        else
        {
            hook();
        }
    }

    /// <summary>Runs the hooks held for the values <paramref name="root" />'s tree was spawned with, in order.</summary>
    internal void RunSpawnHooks(GameObject root)
    {
        if (_spawnHooks.Count == 0)
        {
            return;
        }

        List<Action>? due = null;
        for (int i = 0; i < _spawnHooks.Count; i++)
        {
            (Component component, Action hook) = _spawnHooks[i];
            bool gone = component.IsNotValid();
            if (!gone && !IsUnder(component.GameObject, root))
            {
                continue;
            }

            if (!gone)
            {
                (due ??= new List<Action>()).Add(hook);
            }

            _spawnHooks.RemoveAt(i--);
        }

        if (due == null)
        {
            return;
        }

        foreach (Action hook in due)
        {
            Notify(hook);
        }
    }

    private static bool IsUnder(GameObject go, GameObject root)
    {
        for (GameObject? current = go; current.IsValid(); current = current.Parent)
        {
            if (ReferenceEquals(current, root))
            {
                return true;
            }
        }

        return false;
    }

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

    /// <summary>
    ///     Marks a [Sync] member changed, to go out with the next state. False if its component is not part of a
    ///     networked object, so it won't be.
    /// </summary>
    internal bool MarkSyncDirty(Component component, uint slot)
    {
        if (scene.FindEntry(component.GameObject) is not { TearingDown: false } entry)
        {
            return false;
        }

        int index = entry.IndexOf(component);
        NetworkMembers members = NetworkMembers.Of(component);
        int member = members.Sync(slot) is { } sync ? members.IndexOf(sync) : -1;
        if (index < 0 || member < 0)
        {
            return false;
        }

        (entry.Dirty ??= new ulong[entry.Components.Length])[index] |= 1UL << member;
        if (!entry.StateDirty)
        {
            entry.StateDirty = true;
            _dirty.Add(entry);
        }

        return true;
    }

    internal void WarnSyncOnce(Component component, SyncMember sync, string problem)
    {
        NetworkDiagnostics.TraceMember("SyncVar", "Rejected", scene.Session?.LocalPeer, component, sync.Slot, false,
            problem, default);
        if (_syncWarnings.Add((component.GetType(), sync.Slot, problem)))
        {
            Debug.LogWarning($"[Network] {component.GetType().Name}.{sync.Name} {problem}, so the change was dropped.");
        }
    }

    /// <summary>Whether <paramref name="sender" /> may change a [Sync] member of an object.</summary>
    internal bool AcceptSync(NetPeer sender, NetworkObject entry, SyncFlags flags) =>
        // The host is the authority over everything; a client only over the FromOwner members of what it owns.
        sender.IsHost || ((flags & SyncFlags.FromOwner) != 0 && entry.Owner == sender.Id);

    // --- Sending --------------------------------------------------------------------------------------

    /// <summary>
    ///     Sends every [Sync] change made since the last time, and whatever was queued to pass on. Changes to
    ///     <paramref name="urgent" /> go whatever its components' send intervals, for a call that must arrive after them.
    /// </summary>
    internal void SendSyncChanges(NetworkObject? urgent = null)
    {
        if (_dirty.Count == 0 && _batches.Count == 0 && _sharedCount == 0)
        {
            return;
        }

        if (scene.Session is not { } session || !scene.IsActive)
        {
            Clear();
            return;
        }

        _flushing.Clear();
        _flushing.AddRange(_dirty);
        _dirty.Clear();
        uint tick = scene.Tick;
        foreach (NetworkObject entry in _flushing)
        {
            entry.StateDirty = false;
            if (entry.Dirty is not { } masks)
            {
                continue;
            }

            if (!entry.GameObject.IsValid() || entry.TearingDown || scene.FindEntry(entry.GameObject) != entry)
            {
                entry.Dirty = null;
                continue;
            }

            if (WriteChanged(entry, masks, tick, session, entry == urgent))
            {
                // Members whose send interval hasn't come round stay marked.
                entry.StateDirty = true;
                _dirty.Add(entry);
            }
        }

        _flushing.Clear();
        SendBatches(session);
    }

    /// <summary>
    ///     Writes the dirty members of <paramref name="entry" /> that are due and queues them for whoever gets them.
    ///     True if some stay dirty for a later tick.
    /// </summary>
    private bool WriteChanged(NetworkObject entry, ulong[] masks, uint tick, NetSession session, bool urgent)
    {
        bool host = scene.IsHost, waiting = false;
        ulong observerComponents = 0, ownerComponents = 0;
        _observerBlocks.Reset();
        _ownerBlocks.Reset();
        for (int c = 0; c < masks.Length; c++)
        {
            ulong bits = masks[c];
            if (bits == 0)
            {
                continue;
            }

            Component component = entry.Components[c];
            if (component.IsNotValid())
            {
                masks[c] = 0;
                continue;
            }

            if (!urgent && !scene.SendsStateOn(entry, c, tick))
            {
                waiting = true;
                continue;
            }

            scene.NoteStateSent(entry, c, tick);
            masks[c] = 0;
            NetworkMembers members = NetworkMembers.Of(component);
            ulong observerBits = 0, ownerBits = 0;
            _observerValues.Reset();
            _ownerValues.Reset();
            while (bits != 0)
            {
                int m = BitOperations.TrailingZeroCount(bits);
                ulong bit = 1UL << m;
                bits &= ~bit;
                if (members.SyncAt(m) is not { } sync || !WriteMember(component, sync, false))
                {
                    continue;
                }

                // Only the owner gets an owner only member. The owner wrote its own, so it gets those back from nobody.
                if ((sync.Flags & SyncFlags.ToOwner) == 0)
                {
                    observerBits |= bit;
                    _observerValues.WriteBytes(_value.WrittenSpan);
                }

                if (host && ((sync.Flags & SyncFlags.FromOwner) == 0 || entry.Owner == NoOwner))
                {
                    ownerBits |= bit;
                    _ownerValues.WriteBytes(_value.WrittenSpan);
                }
            }

            if (observerBits != 0)
            {
                observerComponents |= 1UL << c;
                WriteBlock(_observerBlocks, observerBits, _observerValues);
            }

            if (ownerBits != 0)
            {
                ownerComponents |= 1UL << c;
                WriteBlock(_ownerBlocks, ownerBits, _ownerValues);
            }
        }

        if (!host)
        {
            // What a client changed is what it controls, and it all goes to the host.
            if (observerComponents != 0)
            {
                Queue(NetPeer.HostId, entry, observerComponents, _observerBlocks.WrittenSpan);
            }

            return waiting;
        }

        if (observerComponents == 0 && ownerComponents == 0)
        {
            return waiting;
        }

        // The host's own object that nobody is kept from: every client gets the same record.
        if (entry.Owner == NoOwner && entry.HiddenFrom is not { Count: > 0 })
        {
            if (observerComponents != 0)
            {
                WriteRecord(_shared, entry, observerComponents, _observerBlocks.WrittenSpan);
                _sharedCount++;
            }

            return waiting;
        }

        foreach (NetPeer peer in session.Peers)
        {
            if (peer.IsLocal || !Sees(entry, peer))
            {
                continue;
            }

            if (peer.Id == entry.Owner)
            {
                if (ownerComponents != 0)
                {
                    Queue(peer.Id, entry, ownerComponents, _ownerBlocks.WrittenSpan);
                }
            }
            else if (observerComponents != 0)
            {
                Queue(peer.Id, entry, observerComponents, _observerBlocks.WrittenSpan);
            }
        }

        return waiting;
    }

    private static void WriteBlock(NetWriter blocks, ulong members, NetWriter values)
    {
        int size = VarSize(members) + values.Length;
        blocks.WriteVarUInt32((uint)size);
        blocks.WriteVarUInt64(members);
        blocks.WriteBytes(values.WrittenSpan);
    }

    private static int VarSize(ulong value)
    {
        int size = 1;
        while (value >= 0x80)
        {
            value >>= 7;
            size++;
        }

        return size;
    }

    /// <summary>Writes one member's value into the scratch writer: its changes, or all of it. False if it can't be written.</summary>
    private bool WriteMember(Component component, SyncMember sync, bool whole)
    {
        _value.Reset();
        try
        {
            if (whole)
            {
                sync.Write(component, _value);
            }
            else
            {
                sync.WriteChanges(component, _value);
                sync.ChangesSent(component);
            }
        }
        catch (Exception e)
        {
            WarnSyncOnce(component, sync, $"could not be serialized ({e.Message})");
            return false;
        }

        if (NetworkDiagnostics.Enabled)
        {
            NetworkDiagnostics.TraceMember("SyncVar", "Serialize", scene.Session?.LocalPeer, component, sync.Slot, false,
                "Serialized", _value.WrittenSpan);
        }

        return true;
    }

    private void Queue(int peerId, NetworkObject entry, ulong components, ReadOnlySpan<byte> blocks)
    {
        ref (NetWriter Records, int Count, int SharedBytes, int SharedCount) batch =
            ref CollectionsMarshal.GetValueRefOrAddDefault(_batches, peerId, out bool exists);
        if (!exists)
        {
            batch = (_writerPool.Count > 0 ? _writerPool.Pop() : new NetWriter(1024), 0, 0, 0);
        }

        TakeShared(ref batch);
        WriteRecord(batch.Records, entry, components, blocks);
        batch.Count++;
    }

    private static void WriteRecord(NetWriter records, NetworkObject entry, ulong components, ReadOnlySpan<byte> blocks)
    {
        records.WriteVarUInt32(entry.NetId);
        records.WriteVarUInt32(entry.Generation);
        records.WriteVarUInt64(components);
        records.WriteBytes(blocks);
    }

    /// <summary>Brings a peer's own records up to date with the shared ones written so far, keeping their order.</summary>
    private void TakeShared(ref (NetWriter Records, int Count, int SharedBytes, int SharedCount) batch)
    {
        if (batch.SharedBytes < _shared.Length)
        {
            batch.Records.WriteBytes(_shared.WrittenSpan.Slice(batch.SharedBytes));
            batch.Count += _sharedCount - batch.SharedCount;
            batch.SharedBytes = _shared.Length;
            batch.SharedCount = _sharedCount;
        }
    }

    private void SendBatches(NetSession session)
    {
        if (_batches.Count == 0 && _sharedCount == 0)
        {
            return;
        }

        if (_sharedCount > 0)
        {
            // A peer with no records of its own gets the shared ones as they are, in a message built once.
            _sharedPeers.Clear();
            foreach (NetPeer peer in session.Peers)
            {
                if (!peer.IsLocal && !_batches.ContainsKey(peer.Id))
                {
                    _sharedPeers.Add(peer);
                }
            }

            if (_sharedPeers.Count > 0)
            {
                _message.Reset();
                _message.WriteVarUInt32((uint)_sharedCount);
                _message.WriteBytes(_shared.WrittenSpan);
                foreach (NetPeer peer in _sharedPeers)
                {
                    scene.SendEngine(peer, EngineMessage.Sync, _message.WrittenSpan);
                }
            }
        }

        foreach (int peerId in _batches.Keys)
        {
            ref (NetWriter Records, int Count, int SharedBytes, int SharedCount) batch =
                ref CollectionsMarshal.GetValueRefOrNullRef(_batches, peerId);
            bool remote = session.TryGetPeer(peerId, out NetPeer? peer) && !peer!.IsLocal;
            if (remote && peerId != NetPeer.HostId)
            {
                TakeShared(ref batch);
            }

            if (remote && batch.Count > 0)
            {
                _message.Reset();
                _message.WriteVarUInt32((uint)batch.Count);
                _message.WriteBytes(batch.Records.WrittenSpan);
                scene.SendEngine(peer!, EngineMessage.Sync, _message.WrittenSpan);
            }

            batch.Records.Reset();
            _writerPool.Push(batch.Records);
        }

        _batches.Clear();
        _shared.Reset();
        _sharedCount = 0;
    }

    /// <summary>
    ///     Queues the whole value of every member of <paramref name="entry" /> that <paramref name="which" /> picks for
    ///     one peer: the owner only ones for a new owner, or the ones a past owner wrote, as the host has them.
    /// </summary>
    internal void QueueMembers(NetPeer peer, NetworkObject entry, Func<SyncMember, bool> which)
    {
        ulong components = 0;
        _observerBlocks.Reset();
        for (int c = 0; c < entry.Components.Length; c++)
        {
            Component component = entry.Components[c];
            if (component.IsNotValid())
            {
                continue;
            }

            NetworkMembers members = NetworkMembers.Of(component);
            ulong bits = 0;
            _observerValues.Reset();
            for (int m = 0; m < members.SyncCount; m++)
            {
                SyncMember sync = members.SyncAt(m)!;
                if (which(sync) && WriteMember(component, sync, true))
                {
                    bits |= 1UL << m;
                    _observerValues.WriteBytes(_value.WrittenSpan);
                }
            }

            if (bits != 0)
            {
                components |= 1UL << c;
                WriteBlock(_observerBlocks, bits, _observerValues);
            }
        }

        if (components != 0)
        {
            Queue(peer.Id, entry, components, _observerBlocks.WrittenSpan);
        }
    }

    /// <summary>
    ///     Writes the state a spawn carries: a block for each of <paramref name="components" /> with every member all
    ///     peers get. Owner only members follow to the owner on their own.
    /// </summary>
    internal void WriteSpawnState(NetWriter output, Component[] components)
    {
        output.WriteByte((byte)components.Length);
        foreach (Component component in components)
        {
            NetworkMembers members = NetworkMembers.Of(component);
            ulong bits = 0;
            _observerValues.Reset();
            for (int m = 0; m < members.SyncCount; m++)
            {
                SyncMember sync = members.SyncAt(m)!;
                if ((sync.Flags & SyncFlags.ToOwner) == 0 && WriteMember(component, sync, true))
                {
                    bits |= 1UL << m;
                    _observerValues.WriteBytes(_value.WrittenSpan);
                }
            }

            WriteBlock(output, bits, _observerValues);
        }
    }

    // --- Receiving ------------------------------------------------------------------------------------

    internal void OnSync(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (!scene.IsHost && !sender.IsHost)
        {
            return;
        }

        if (delivery != Delivery.Reliable)
        {
            throw new InvalidDataException("State must be sent reliably.");
        }

        using NetSerializers.ReadingFrom references = new(scene.RemoteClient(sender));

        // Everything is checked before anything is applied, so a malformed message changes nothing.
        NetReader check = reader;
        uint count = check.ReadVarUInt32();
        if (count > check.Remaining)
        {
            throw new InvalidDataException("State message names more records than it holds.");
        }

        for (uint i = 0; i < count; i++)
        {
            ReadRecord(sender, ref check, false);
        }

        if (!check.IsAtEnd)
        {
            throw new InvalidDataException("State message contains trailing bytes.");
        }

        reader.ReadVarUInt32();
        int notificationsFrom = _notifications.Count;
        _applying++;
        try
        {
            for (uint i = 0; i < count; i++)
            {
                ReadRecord(sender, ref reader, true);
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
    }

    // Reads one object's record: checking it, or applying what the sender may change and passing that on.
    private void ReadRecord(NetPeer sender, ref NetReader reader, bool apply)
    {
        uint netId = reader.ReadVarUInt32(), generation = reader.ReadVarUInt32();
        ulong components = reader.ReadVarUInt64();

        // An object this peer doesn't have, or one whose ownership moved on since, is read past.
        NetworkObject? entry = scene.Entries.TryGetNet(netId, out NetworkObject? found) && !found.TearingDown &&
                               found.Generation == generation && (sender.IsHost || found.Owner == sender.Id)
            ? found
            : null;
        if (apply && found == null && NetworkDiagnostics.Enabled)
        {
            NetworkDiagnostics.Record("SyncVar", "Rejected", sender, Guid.Empty, $"Unknown object {netId}", default);
        }

        bool relay = apply && scene.IsHost && entry != null;
        ulong relayComponents = 0;
        if (relay)
        {
            _observerBlocks.Reset();
        }

        while (components != 0)
        {
            int c = BitOperations.TrailingZeroCount(components);
            components &= components - 1;
            ReadOnlySpan<byte> bytes = reader.ReadBytesWithLength();
            if (entry == null)
            {
                // The object is here but not the sender's to change: say which component it tried for.
                if (apply && NetworkDiagnostics.Enabled && found != null && c < found.Components.Length)
                {
                    NetworkDiagnostics.Record("SyncVar", "Rejected", sender, found.Components[c].Identifier,
                        "Not the sender's to change, or from an earlier ownership", bytes);
                }

                continue;
            }

            if (c >= entry.Components.Length)
            {
                throw new InvalidDataException("State names a component its object doesn't have.");
            }

            Component component = entry.Components[c];
            if (component.IsNotValid())
            {
                continue;
            }

            ulong accepted = ReadBlock(sender, entry, component, bytes, apply, false, relay ? _observerValues : null);
            if (relay && accepted != 0)
            {
                relayComponents |= 1UL << c;
                WriteBlock(_observerBlocks, accepted, _observerValues);
            }
        }

        // The host passes on what it took from a client to everyone else who has the object.
        if (relay && relayComponents != 0)
        {
            foreach (NetPeer peer in scene.Session!.Peers)
            {
                if (!peer.IsLocal && peer != sender && Sees(entry!, peer))
                {
                    Queue(peer.Id, entry!, relayComponents, _observerBlocks.WrittenSpan);
                }
            }
        }
    }

    /// <summary>
    ///     Reads one component's block: its member mask and their values. Members the sender may change are applied
    ///     when <paramref name="apply" />, the rest read past. Returns the members taken, with their bytes copied to
    ///     <paramref name="taken" /> when given.
    /// </summary>
    private ulong ReadBlock(NetPeer sender, NetworkObject? entry, Component component, ReadOnlySpan<byte> bytes,
        bool apply, bool initial, NetWriter? taken)
    {
        NetworkMembers members = NetworkMembers.Of(component);
        NetReader block = new(bytes);
        ulong bits = block.ReadVarUInt64(), accepted = 0;
        taken?.Reset();
        while (bits != 0)
        {
            int m = BitOperations.TrailingZeroCount(bits);
            bits &= bits - 1;
            if (members.SyncAt(m) is not { } sync)
            {
                throw new InvalidDataException("State names a member its component doesn't have.");
            }

            // A spawn's state is the host's own, so all of it is taken.
            bool take = entry == null || AcceptSync(sender, entry, sync.Flags);
            int start = block.Position;
            try
            {
                if (apply && take)
                {
                    sync.Read(component, ref block, initial);
                }
                else
                {
                    sync.Validate(component, ref block, initial);
                }
            }
            catch (Exception e) when (e is ArgumentException or OverflowException or FormatException
                                          or IndexOutOfRangeException)
            {
                throw new InvalidDataException("State contains a malformed value.", e);
            }

            if (!take)
            {
                continue;
            }

            accepted |= 1UL << m;
            ReadOnlySpan<byte> value = bytes[start..block.Position];
            taken?.WriteBytes(value);
            if (apply && NetworkDiagnostics.Enabled)
            {
                NetworkDiagnostics.TraceMember("SyncVar", "Apply", sender, component, sync.Slot, false, "Applied", value);
            }
        }

        if (!block.IsAtEnd)
        {
            throw new InvalidDataException("State value contains trailing bytes.");
        }

        return accepted;
    }

    /// <summary>
    ///     Applies the state a spawn carries to <paramref name="components" />, once all of it checks out. It changes
    ///     nothing that was running, so hooks for it are held until the object's spawn callbacks are due.
    /// </summary>
    internal void ApplySpawnState(NetPeer sender, Component[] components, ref NetReader reader)
    {
        using NetSerializers.ReadingFrom references = new(scene.RemoteClient(sender));
        NetReader check = reader;
        ReadSpawnState(sender, components, ref check, false);
        if (!check.IsAtEnd)
        {
            throw new InvalidDataException("Spawn contains trailing bytes.");
        }

        int notificationsFrom = _notifications.Count;
        _applying++;
        try
        {
            ReadSpawnState(sender, components, ref reader, true);
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
    }

    private void ReadSpawnState(NetPeer sender, Component[] components, ref NetReader reader, bool apply)
    {
        int count = reader.ReadByte();
        if (count != components.Length)
        {
            throw new InvalidDataException(
                $"A spawn carries state for {count} networked components and this peer's object has {components.Length}.");
        }

        for (int c = 0; c < count; c++)
        {
            ReadBlock(sender, null, components[c], reader.ReadBytesWithLength(), apply, true, null);
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
