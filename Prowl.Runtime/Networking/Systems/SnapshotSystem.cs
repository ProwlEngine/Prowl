// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

using Prowl.Warp;

using static Prowl.Runtime.Networking.NetworkSystem;

namespace Prowl.Runtime.Networking;

// Snapshots carry [Sync] values and transforms, unreliably. Every tick each peer is sent what it hasn't
// acknowledged yet: values changed since, and the pose a still object came to rest at. A lost snapshot is never
// resent as such; whatever it held is still unacknowledged, so a later snapshot carries it, current. Objects on the
// move send their transforms every tick anyway, so those aren't tracked. Network collections stay on the reliable
// Sync message, since their changes are operations that must arrive in order.
//
// The receiver acknowledges each snapshot it gets, naming the components and objects in it that it couldn't apply
// (not spawned there yet, say), which stay pending. Values are applied only if newer than the last applied from that
// sender, since unreliable snapshots can arrive out of order. Components are named by small ids the sender gives them
// per receiver, sent with their guid until a snapshot carrying it has been acknowledged.
//
// The host relays: a value it accepts from a client becomes the host's own change, owed to everyone but that client.
// A client's moving transforms are passed on as they arrive, under the client's tick and clock.
//
// Sending and receiving reuse their buffers, so a steady stream of snapshots allocates nothing per tick.
internal sealed class SnapshotSystem : IDisposable
{
    // Sent but unacknowledged snapshots remembered per peer. Older ones are forgotten, and their state resent.
    private const int MaxOutstanding = 256;

    private readonly List<(Component Component, SyncMember Sync)> _applied = new();
    private readonly HashSet<uint> _ackedIds = new();
    private readonly Func<Component, SyncMember, bool> _filter;
    private readonly List<Item> _items = new();
    private readonly NetWriter _itemBytes = new(4096);
    private readonly NetWriter _message = new(4096);
    private readonly List<(NetworkObject Entry, TransformUpdate Update)> _moving = new();
    private readonly Dictionary<int, PeerState> _peers = new();
    private readonly HashSet<Guid> _rejectedComponents = new();
    private readonly HashSet<uint> _rejectedObjects = new();
    private readonly List<(TransformGroup Group, NetworkObject Entry, TransformUpdate Update)> _relay = new();
    private readonly NetworkSystem _scene;
    private readonly List<NetworkObject> _scratchObjects = new();
    private readonly List<SyncKey> _scratchKeys = new();
    private readonly List<Component> _scratchComponents = new();
    private readonly List<NetPeer> _sendTargets = new();
    private readonly Stack<SentSnapshot> _sentPool = new();
    private readonly List<NetPeer> _targets = new();
    private readonly NetWriter _translated = new(4096);
    private readonly NetWriter _valueWriter = new();

    // What the filter of the values being applied compares against.
    private PeerState? _filterState;
    private uint _filterTick;

    public SnapshotSystem(NetworkSystem scene)
    {
        _scene = scene;
        _filter = Filter;
    }

    public void Dispose()
    {
        _valueWriter.Dispose();
        _itemBytes.Dispose();
        _message.Dispose();
        _translated.Dispose();
    }

    /// <summary>Forgets everything owed and received, as when the scene changes.</summary>
    internal void Clear()
    {
        foreach (PeerState state in _peers.Values)
        {
            Forget(state);
        }

        _peers.Clear();
    }

    internal void RemovePeer(int peerId)
    {
        if (_peers.Remove(peerId, out PeerState? state))
        {
            Forget(state);
        }
    }

    /// <summary>A scene snapshot or catch-up is carrying <paramref name="peer" /> everything it is owed.</summary>
    internal void Delivered(NetPeer peer)
    {
        if (_peers.TryGetValue(peer.Id, out PeerState? state))
        {
            state.Values.Clear();
            state.Poses.Clear();
            Forget(state);
        }
    }

    /// <summary>A spawn is carrying <paramref name="peer" /> the values and pose of <paramref name="root" />'s tree.</summary>
    internal void Delivered(NetPeer peer, GameObject root)
    {
        if (!_peers.TryGetValue(peer.Id, out PeerState? state))
        {
            return;
        }

        _scratchKeys.Clear();
        foreach (SyncKey key in state.Values.Keys)
        {
            if (InTree(key.Component.GameObject, root))
            {
                _scratchKeys.Add(key);
            }
        }

        foreach (SyncKey key in _scratchKeys)
        {
            state.Values.Remove(key);
        }

        _scratchObjects.Clear();
        foreach (NetworkObject entry in state.Poses.Keys)
        {
            if (InTree(entry.GameObject, root))
            {
                _scratchObjects.Add(entry);
            }
        }

        foreach (NetworkObject entry in _scratchObjects)
        {
            state.Poses.Remove(entry);
        }
    }

    /// <summary>Hands every remembered sent snapshot back to the pool.</summary>
    private void Forget(PeerState state)
    {
        foreach (SentSnapshot sent in state.Sent.Values)
        {
            Release(sent);
        }

        state.Sent.Clear();
        state.SentOrder.Clear();
    }

    // --- Changes --------------------------------------------------------------------------------

    /// <summary>A [Sync] value changed here, or the host accepted it from a client: owe it to whoever doesn't control it.</summary>
    internal void MarkChanged(Component component, SyncMember sync)
    {
        uint tick = _scene.Tick + 1;
        int controller = ControllerOf(component, sync.Flags);
        SyncKey key = new(component, sync.Slot);
        foreach (NetPeer peer in Targets())
        {
            if (peer.Id == controller)
            {
                continue;
            }

            Dictionary<SyncKey, Pending> values = State(peer).Values;
            values[key] = values.TryGetValue(key, out Pending pending) ? pending with { Changed = tick } : new Pending(tick, 0);
        }
    }

    /// <summary>Whose changes to a value count: the host's for FromHost ones, else the object's owner, or the host.</summary>
    private int ControllerOf(Component component, SyncFlags flags) =>
        (flags & SyncFlags.FromHost) != 0 ? NetPeer.HostId : ControllerOf(component.GameObject);

    private int ControllerOf(GameObject go)
    {
        int owner = _scene.OwnerOf(go);
        return owner == NoOwner ? NetPeer.HostId : owner;
    }

    /// <summary>The peers this one sends snapshots to: every client from the host, the host from a client.</summary>
    private List<NetPeer> Targets()
    {
        _targets.Clear();
        NetSession session = _scene.Session!;
        if (_scene.IsHost)
        {
            foreach (NetPeer peer in session.Peers)
            {
                if (!peer.IsLocal)
                {
                    _targets.Add(peer);
                }
            }
        }
        else if (session.HostPeer is { } host)
        {
            _targets.Add(host);
        }

        return _targets;
    }

    private PeerState State(NetPeer peer)
    {
        if (!_peers.TryGetValue(peer.Id, out PeerState? state))
        {
            state = new PeerState();
            _peers.Add(peer.Id, state);
        }

        return state;
    }

    private static bool InTree(GameObject go, GameObject root) => go == root || go.IsChildOf(root);

    private uint Epoch => (uint)_scene.SceneGeneration;

    // --- Sending --------------------------------------------------------------------------------

    /// <summary>Takes this tick's transforms and sends every ready peer its snapshot.</summary>
    internal void Send()
    {
        uint tick = ++_scene.Tick;
        double now = _scene.Now;
        CaptureTransforms(tick, now);
        _sendTargets.Clear();
        _sendTargets.AddRange(Targets());
        foreach (NetPeer peer in _sendTargets)
        {
            if (_scene.Session != null && (!_scene.IsHost || _scene.IsPeerReady(peer)))
            {
                SendTo(peer, State(peer), tick, now);
            }
        }

        _moving.Clear();
        _scene.LastTransformTime = now;

        // Every so often, forget what was kept about components that are gone.
        if (tick % 64 == 0)
        {
            Prune();
        }
    }

    private void Prune()
    {
        foreach (PeerState state in _peers.Values)
        {
            _scratchKeys.Clear();
            foreach (SyncKey key in state.Applied.Keys)
            {
                if (key.Component.IsDisposed)
                {
                    _scratchKeys.Add(key);
                }
            }

            foreach (SyncKey key in _scratchKeys)
            {
                state.Applied.Remove(key);
            }

            _scratchComponents.Clear();
            foreach (Component component in state.ComponentIds.Keys)
            {
                if (component.IsDisposed)
                {
                    _scratchComponents.Add(component);
                }
            }

            foreach (Component component in _scratchComponents)
            {
                state.ComponentIds.Remove(component);
                state.DefinedComponents.Remove(component);
            }
        }
    }

    private void CaptureTransforms(uint tick, double now)
    {
        foreach (NetworkObject entry in _scene.Entries.Values)
        {
            GameObject go = entry.GameObject;
            if (!go.IsValid() || !go.Network.HasTransformSync || !_scene.HasControl(go) ||
                entry.TransformSync.Capture(go, now, _scene.LastTransformTime) is not { } update)
            {
                continue;
            }

            // The pose it came to rest at must arrive; poses on the way there only need to keep coming.
            if ((update.Channels & TransformChannels.ExactRotation) != 0)
            {
                Pend(entry, new PendingPose(tick, 0, _scene.Session!.LocalPeer!.Id, tick, now, update), -1);
            }
            else
            {
                _moving.Add((entry, update));
                ClearPose(entry);
            }
        }
    }

    private void Pend(NetworkObject entry, PendingPose pose, int except)
    {
        foreach (NetPeer peer in Targets())
        {
            if (peer.Id != except)
            {
                State(peer).Poses[entry] = pose;
            }
        }
    }

    /// <summary>It's moving again: nobody needs the pose it rested at any more.</summary>
    private void ClearPose(NetworkObject entry)
    {
        foreach (PeerState state in _peers.Values)
        {
            state.Poses.Remove(entry);
        }
    }

    /// <summary>Sends the moving transforms and whatever <paramref name="peer" /> is owed, in as many snapshots as it takes.</summary>
    private void SendTo(NetPeer peer, PeerState state, uint tick, double now)
    {
        if (peer.Connection is not { IsConnected: true } connection)
        {
            return;
        }

        uint resend = ResendTicks(peer);
        _items.Clear();
        _itemBytes.Reset();
        foreach ((NetworkObject entry, TransformUpdate update) in _moving)
        {
            AddRecord(tick, now, entry, update, null);
        }

        _scratchObjects.Clear();
        foreach ((NetworkObject entry, PendingPose pose) in state.Poses)
        {
            if (!entry.GameObject.IsValid() || !_scene.Entries.TryGetNet(entry.NetId, out NetworkObject? current) ||
                current != entry || ControllerOf(entry.GameObject) != pose.Controller)
            {
                _scratchObjects.Add(entry);
            }
            else if (pose.Changed > pose.LastSent || tick - pose.LastSent >= resend)
            {
                AddRecord(pose.ControllerTick, pose.SentAt, entry, pose.Update, entry);
            }
        }

        foreach (NetworkObject entry in _scratchObjects)
        {
            state.Poses.Remove(entry);
        }

        _scratchKeys.Clear();
        foreach ((SyncKey key, Pending pending) in state.Values)
        {
            Component component = key.Component;
            if (component.IsDisposed || NetworkAccessor.IsExcludedFromNetwork(component.GameObject) ||
                NetworkMembers.Of(component).Sync(key.Slot) is not { } sync ||
                (_scene.IsHost ? ControllerOf(component, sync.Flags) == peer.Id : !_scene.HasControl(component, sync.Flags)))
            {
                _scratchKeys.Add(key);
            }
            else if (pending.Changed > pending.LastSent || tick - pending.LastSent >= resend)
            {
                AddValue(state, key, component, sync);
            }
        }

        foreach (SyncKey key in _scratchKeys)
        {
            state.Values.Remove(key);
        }

        if (_items.Count > 0)
        {
            SendItems(peer, state, tick, connection);
        }
    }

    private void AddRecord(uint groupTick, double sentAt, NetworkObject entry, TransformUpdate update,
        NetworkObject? pose)
    {
        int offset = _itemBytes.Length;
        new TransformRecord(entry.NetId, entry.Generation, update).Write(_itemBytes);
        _items.Add(new Item(true, groupTick, sentAt, offset, _itemBytes.Length - offset, pose, default, null));
    }

    private void AddValue(PeerState state, SyncKey key, Component component, SyncMember sync)
    {
        if (!state.ComponentIds.TryGetValue(component, out uint id))
        {
            id = state.NextComponentId++;
            state.ComponentIds.Add(component, id);
        }

        // Named by its guid too, until the receiver is known to have that.
        bool define = !state.DefinedComponents.Contains(component);
        _valueWriter.Reset();
        sync.Write(component, _valueWriter);
        int offset = _itemBytes.Length;
        new SnapshotValue(id, define ? component.Identifier : null, key.Slot,
            _scene.GenerationOf(component.GameObject), _valueWriter.WrittenSpan).Write(_itemBytes);
        _items.Add(new Item(false, 0, 0, offset, _itemBytes.Length - offset, null, key, define ? component : null));
    }

    /// <summary>Splits the items into snapshots that fit the connection, and sends them.</summary>
    private void SendItems(NetPeer peer, PeerState? state, uint tick, Connection connection)
    {
        (Delivery delivery, int budget) = Budget(connection);
        int start = 0;
        while (start < _items.Count)
        {
            int size = SnapshotMessage.HeaderSize + 5 + 5, end = start;
            do
            {
                Item item = _items[end];
                bool newGroup = item.IsTransform && (end == start || !SameGroup(_items[end - 1], item));
                int itemSize = item.Length + (newGroup ? TransformGroup.HeaderSize : 0);
                if (end > start && size + itemSize > budget)
                {
                    break;
                }

                size += itemSize;
                end++;
            } while (end < _items.Count);

            SendPart(peer, state, tick, start, end, delivery);
            start = end;
        }
    }

    private static bool SameGroup(Item a, Item b) =>
        a.IsTransform && a.GroupTick == b.GroupTick && a.GroupSentAt == b.GroupSentAt;

    /// <summary>Sends items [start, end) as one snapshot, remembering what it owes if <paramref name="state" /> is given.</summary>
    private void SendPart(NetPeer peer, PeerState? state, uint tick, int start, int end, Delivery delivery)
    {
        // Only what's owed needs acknowledging; moving transforms are simply sent again next tick.
        uint id = 0;
        if (state != null)
        {
            SentSnapshot? sent = null;
            for (int i = start; i < end; i++)
            {
                Item item = _items[i];
                if (!item.IsTransform)
                {
                    sent ??= Rent(tick);
                    sent.Values.Add(item.Key);
                    state.Values[item.Key] = state.Values[item.Key] with { LastSent = tick };
                    if (item.Defines is { } component)
                    {
                        sent.Definitions.Add(component);
                    }
                }
                else if (item.Pose is { } entry)
                {
                    sent ??= Rent(tick);
                    sent.Poses.Add(entry);
                    state.Poses[entry] = state.Poses[entry] with { LastSent = tick };
                }
            }

            if (sent != null)
            {
                id = state.NextId++;
                if (state.NextId == 0)
                {
                    state.NextId = 1;
                }

                Remember(state, id, sent);
            }
        }

        _message.Reset();
        new SnapshotMessage(id, Epoch, tick).Write(_message);

        // Transform items come first, in runs that share a controller tick.
        int groups = 0, transforms = start;
        for (; transforms < end && _items[transforms].IsTransform; transforms++)
        {
            if (transforms == start || !SameGroup(_items[transforms - 1], _items[transforms]))
            {
                groups++;
            }
        }

        new TransformGroupBlock((uint)groups).Write(_message);
        for (int i = start; i < transforms;)
        {
            int runEnd = i + 1;
            while (runEnd < transforms && SameGroup(_items[i], _items[runEnd]))
            {
                runEnd++;
            }

            new TransformGroup(_items[i].GroupTick, _items[i].GroupSentAt, (uint)(runEnd - i)).Write(_message);
            for (; i < runEnd; i++)
            {
                _message.WriteBytes(_itemBytes.WrittenSpan.Slice(_items[i].Offset, _items[i].Length));
            }
        }

        _message.WriteVarUInt32((uint)(end - transforms));
        for (int i = transforms; i < end; i++)
        {
            _message.WriteBytes(_itemBytes.WrittenSpan.Slice(_items[i].Offset, _items[i].Length));
        }

        _scene.SendEngine(peer, EngineMessage.Snapshot, _message.WrittenSpan, delivery);
    }

    private SentSnapshot Rent(uint tick)
    {
        SentSnapshot sent = _sentPool.Count > 0 ? _sentPool.Pop() : new SentSnapshot();
        sent.Tick = tick;
        return sent;
    }

    private void Release(SentSnapshot sent)
    {
        sent.Values.Clear();
        sent.Poses.Clear();
        sent.Definitions.Clear();
        _sentPool.Push(sent);
    }

    private void Remember(PeerState state, uint id, SentSnapshot sent)
    {
        if (state.Sent.Remove(id, out SentSnapshot? replaced))
        {
            Release(replaced);
        }

        state.Sent.Add(id, sent);
        state.SentOrder.Enqueue(id);
        while (state.SentOrder.Count > MaxOutstanding)
        {
            if (state.Sent.Remove(state.SentOrder.Dequeue(), out SentSnapshot? forgotten))
            {
                Release(forgotten);
            }
        }
    }

    /// <summary>Unreliable when the connection can carry a whole transform record in one packet, else reliable.</summary>
    private (Delivery Delivery, int Budget) Budget(Connection connection)
    {
        int limit = _scene.Session!.Options.MaxMessageSize;
        Delivery delivery = Delivery.Reliable;
        if (_scene.Session.Transport.Capabilities.Supports(Delivery.Unreliable) &&
            connection.GetMaxMessageSize(Delivery.Unreliable) >= SnapshotMessage.MinMessageSize)
        {
            delivery = Delivery.Unreliable;
            limit = Math.Min(limit, connection.GetMaxMessageSize(Delivery.Unreliable));
        }

        // Warp adds a two-byte message id.
        return (delivery, limit - sizeof(ushort));
    }

    /// <summary>Ticks to wait for an acknowledgement before sending owed state again: a round trip, and a little.</summary>
    private static uint ResendTicks(NetPeer peer)
    {
        double interval = 1.0 / Math.Max(Networking.TickRate, 1f);
        double roundTrip = peer.Connection?.RoundTripTime.TotalSeconds ?? 0;
        return (uint)Math.Max(2, Math.Ceiling((roundTrip * 1.25 + interval) / interval));
    }

    /// <summary>Acknowledges, to each peer that sent any, the snapshots received since the last call.</summary>
    internal void SendAcks()
    {
        NetSession session = _scene.Session!;
        foreach ((int peerId, PeerState state) in _peers)
        {
            if (state.Received.Count == 0 || !session.TryGetPeer(peerId, out NetPeer? peer))
            {
                continue;
            }

            _message.Reset();
            SnapshotAckMessage.WriteIds(_message, CollectionsMarshal.AsSpan(state.Received));
            _message.WriteVarUInt32((uint)state.RejectedComponents.Count);
            foreach (Guid id in state.RejectedComponents)
            {
                _message.WriteGuid(id);
            }

            _message.WriteVarUInt32((uint)state.RejectedObjects.Count);
            foreach (uint netId in state.RejectedObjects)
            {
                _message.WriteVarUInt32(netId);
            }

            state.Received.Clear();
            state.RejectedComponents.Clear();
            state.RejectedObjects.Clear();
            _scene.SendEngine(peer, EngineMessage.SnapshotAck, _message.WrittenSpan, Delivery.Unreliable);
        }
    }

    // --- Receiving ------------------------------------------------------------------------------

    internal void OnSnapshot(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (!_scene.IsHost && !sender.IsHost)
        {
            return;
        }

        SnapshotMessage header = SnapshotMessage.Read(ref reader);
        if (header.Epoch != Epoch)
        {
            // From another scene: its ids mean nothing here. Unacknowledged, its state stays owed.
            return;
        }

        PeerState state = State(sender);
        double now = _scene.Now;
        _relay.Clear();
        uint groups = TransformGroupBlock.Read(ref reader).Count;
        for (uint g = 0; g < groups; g++)
        {
            TransformGroup group = TransformGroup.Read(ref reader);
            for (uint i = 0; i < group.Count; i++)
            {
                (uint netId, uint generation, TransformUpdate update) = TransformRecord.Read(ref reader);
                if (!_scene.Entries.TryGetNet(netId, out NetworkObject? entry) || !entry.GameObject.IsValid() ||
                    entry.Generation != generation)
                {
                    state.RejectedObjects.Add(netId);
                    continue;
                }

                // From a client, only for what it owns.
                if (entry.GameObject.Network.HasTransformSync && !_scene.HasControl(entry.GameObject) &&
                    (sender.IsHost || entry.Owner == sender.Id) &&
                    entry.TransformSync.Receive(entry.GameObject, group.Tick, group.SentAt, now, update) is
                        { } accepted && _scene.IsHost)
                {
                    _relay.Add((group, entry, accepted));
                }
            }
        }

        // Values name components by id; spell them out as a SyncBlock for the shared validation and apply.
        ReadValues(state, ref reader);
        NetReader translated = new(_translated.WrittenSpan);
        NetSession? session = _scene.Session;
        _applied.Clear();
        _filterState = state;
        _filterTick = header.Tick;
        try
        {
            _scene.Sync.ApplySyncState(sender, ref translated, null, false, true, _filter, _applied,
                state.RejectedComponents);
        }
        finally
        {
            _filterState = null;
        }

        if (_scene.Session != session)
        {
            return;
        }

        foreach ((Component component, SyncMember sync) in _applied)
        {
            state.Applied[new SyncKey(component, sync.Slot)] = header.Tick;
            if (_scene.IsHost)
            {
                MarkChanged(component, sync);
            }
        }

        if (header.Id != 0)
        {
            state.Received.Add(header.Id);
        }

        if (_relay.Count > 0)
        {
            Relay(sender);
        }
    }

    /// <summary>Reads a snapshot's values into <see cref="_translated" /> as a SyncBlock, learning the ids it defines.</summary>
    private void ReadValues(PeerState state, ref NetReader reader)
    {
        uint count = reader.ReadVarUInt32();
        if (count > (uint)reader.Remaining / SnapshotValue.MinimumSize)
        {
            throw new InvalidDataException("Snapshot value count exceeds the remaining data.");
        }

        _translated.Reset();
        new SyncBlock(count).Write(_translated);
        for (uint i = 0; i < count; i++)
        {
            SnapshotValue value = SnapshotValue.Read(ref reader);
            Guid component;
            if (value.Definition is { } definition)
            {
                component = definition;
                state.ReceivedIds[value.ComponentId] = definition;
            }
            else if (!state.ReceivedIds.TryGetValue(value.ComponentId, out component))
            {
                throw new InvalidDataException("A snapshot names a component it never defined.");
            }

            new SyncValue(component, value.Slot, value.Generation, value.Value).Write(_translated);
        }

        if (!reader.IsAtEnd)
        {
            throw new InvalidDataException("Snapshot contains trailing bytes.");
        }
    }

    /// <summary>Values newer than the last applied from this sender; on a client, not ones it controls itself.</summary>
    private bool Filter(Component component, SyncMember sync) =>
        (!_filterState!.Applied.TryGetValue(new SyncKey(component, sync.Slot), out uint last) ||
         (int)(_filterTick - last) > 0) &&
        (_scene.IsHost || !_scene.HasControl(component, sync.Flags));

    /// <summary>
    ///     Passes a client's transforms on. Moving ones go straight out, as before; the pose it came to rest at is
    ///     owed to everyone else until they acknowledge it.
    /// </summary>
    private void Relay(NetPeer sender)
    {
        _items.Clear();
        _itemBytes.Reset();
        foreach ((TransformGroup group, NetworkObject entry, TransformUpdate update) in _relay)
        {
            if ((update.Channels & TransformChannels.ExactRotation) != 0)
            {
                Pend(entry, new PendingPose(_scene.Tick + 1, 0, sender.Id, group.Tick, group.SentAt, update),
                    sender.Id);
            }
            else
            {
                ClearPose(entry);
                AddRecord(group.Tick, group.SentAt, entry, update, null);
            }
        }

        if (_items.Count == 0)
        {
            return;
        }

        _sendTargets.Clear();
        _sendTargets.AddRange(Targets());
        foreach (NetPeer peer in _sendTargets)
        {
            if (peer != sender && _scene.IsPeerReady(peer) && peer.Connection is { IsConnected: true } connection)
            {
                SendItems(peer, null, _scene.Tick, connection);
            }
        }
    }

    internal void OnAck(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        _ackedIds.Clear();
        _rejectedComponents.Clear();
        _rejectedObjects.Clear();
        uint count = SnapshotAckMessage.ReadCount(ref reader, 1);
        for (uint i = 0; i < count; i++)
        {
            _ackedIds.Add(reader.ReadVarUInt32());
        }

        uint components = SnapshotAckMessage.ReadCount(ref reader, 16);
        for (uint i = 0; i < components; i++)
        {
            _rejectedComponents.Add(reader.ReadGuid());
        }

        uint objects = SnapshotAckMessage.ReadCount(ref reader, 1);
        for (uint i = 0; i < objects; i++)
        {
            _rejectedObjects.Add(reader.ReadVarUInt32());
        }

        if (!reader.IsAtEnd)
        {
            throw new InvalidDataException("Snapshot acknowledgement contains trailing bytes.");
        }

        if (!_peers.TryGetValue(sender.Id, out PeerState? state))
        {
            return;
        }

        foreach (uint id in _ackedIds)
        {
            if (!state.Sent.Remove(id, out SentSnapshot? sent))
            {
                continue;
            }

            // The receiver knows these ids now, whether or not their values could be applied.
            foreach (Component component in sent.Definitions)
            {
                state.DefinedComponents.Add(component);
            }

            // Whatever couldn't be applied stays owed, and goes again at once; the rest is done if nothing changed since.
            foreach (SyncKey key in sent.Values)
            {
                if (!state.Values.TryGetValue(key, out Pending pending))
                {
                    continue;
                }

                if (_rejectedComponents.Contains(key.Component.Identifier))
                {
                    state.Values[key] = pending with { LastSent = 0 };
                }
                else if (pending.Changed <= sent.Tick)
                {
                    state.Values.Remove(key);
                }
            }

            foreach (NetworkObject entry in sent.Poses)
            {
                if (!state.Poses.TryGetValue(entry, out PendingPose pose))
                {
                    continue;
                }

                if (_rejectedObjects.Contains(entry.NetId))
                {
                    state.Poses[entry] = pose with { LastSent = 0 };
                }
                else if (pose.Changed <= sent.Tick)
                {
                    state.Poses.Remove(entry);
                }
            }

            Release(sent);
        }
    }

    // --- Ownership handoff ----------------------------------------------------------------------

    /// <summary>
    ///     The [Sync] values in <paramref name="entry" />'s tree, for a handoff: all of them from the host, or the ones
    ///     this peer controls for a release. They go reliably with it, since once ownership moves on, snapshots
    ///     wouldn't carry them: the old owner's would be turned away, and the new owner isn't sent what it controls.
    /// </summary>
    internal void WriteHandoffValues(NetWriter output, NetworkObject entry, bool includeAll)
    {
        using NetWriter values = new(1024);
        uint count = 0;
        foreach (GameObject go in (List<GameObject>)[entry.GameObject, .. entry.GameObject.GetChildrenDeep()])
        {
            if (NetworkAccessor.IsExcludedFromNetwork(go) || _scene.FindEntry(go) != entry)
            {
                continue;
            }

            foreach (Component component in go.GetComponents<Component>())
            {
                foreach (SyncMember sync in NetworkMembers.Of(component).Syncs)
                {
                    if (sync.IsCollection || (!includeAll && !_scene.HasControl(component, sync.Flags)))
                    {
                        continue;
                    }

                    _valueWriter.Reset();
                    sync.Write(component, _valueWriter);
                    new SyncValue(component.Identifier, sync.Slot, entry.Generation, _valueWriter.WrittenSpan)
                        .Write(values);
                    count++;
                }
            }
        }

        new SyncBlock(count).Write(output);
        output.WriteBytes(values.WrittenSpan);
    }

    /// <summary>Applies handed-over values. On the host, a releasing client's are then owed to everyone else.</summary>
    internal void ApplyHandoffValues(NetPeer sender, ReadOnlySpan<byte> data)
    {
        NetReader reader = new(data);
        List<(Component Component, SyncMember Sync)> applied = new();
        _scene.Sync.ApplySyncState(sender, ref reader, null, false, true, accepted: applied);
        if (!_scene.IsHost)
        {
            return;
        }

        foreach ((Component component, SyncMember sync) in applied)
        {
            MarkChanged(component, sync);
        }
    }

    // --- State ------------------------------------------------------------------------------------

    private readonly record struct SyncKey(Component Component, uint Slot);

    /// <summary>A value owed to a peer: the tick it last changed, and the tick it last went out.</summary>
    private readonly record struct Pending(uint Changed, uint LastSent);

    /// <summary>A rest pose owed to a peer, under the tick and clock of the controller that took it.</summary>
    private readonly record struct PendingPose(
        uint Changed,
        uint LastSent,
        int Controller,
        uint ControllerTick,
        double SentAt,
        TransformUpdate Update);

    /// <summary>What one sent snapshot owed, and the component ids it defined. Pooled.</summary>
    private sealed class SentSnapshot
    {
        public readonly List<Component> Definitions = new();
        public readonly List<NetworkObject> Poses = new();
        public readonly List<SyncKey> Values = new();
        public uint Tick;
    }

    /// <summary>
    ///     One entry of a snapshot, its bytes at [Offset, Offset + Length) of the item buffer: a transform record in its
    ///     controller's group, or a SnapshotValue.
    /// </summary>
    private readonly record struct Item(
        bool IsTransform,
        uint GroupTick,
        double GroupSentAt,
        int Offset,
        int Length,
        NetworkObject? Pose,
        SyncKey Key,
        Component? Defines);

    /// <summary>What this peer owes another, and has received from it.</summary>
    private sealed class PeerState
    {
        public readonly Dictionary<SyncKey, uint> Applied = new();

        // Ids this peer gave components for the other, and the ones it knows the other has.
        public readonly Dictionary<Component, uint> ComponentIds = new();
        public readonly HashSet<Component> DefinedComponents = new();
        public readonly Dictionary<NetworkObject, PendingPose> Poses = new();
        public readonly List<uint> Received = new();

        // Ids the other gave components, as it defined them.
        public readonly Dictionary<uint, Guid> ReceivedIds = new();
        public readonly HashSet<Guid> RejectedComponents = new();
        public readonly HashSet<uint> RejectedObjects = new();
        public readonly Dictionary<uint, SentSnapshot> Sent = new();
        public readonly Queue<uint> SentOrder = new();
        public readonly Dictionary<SyncKey, Pending> Values = new();
        public uint NextComponentId = 1;
        public uint NextId = 1;
    }
}
