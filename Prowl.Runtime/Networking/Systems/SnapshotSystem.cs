// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using Prowl.Courier;
using Prowl.Vector;

using static Prowl.Runtime.Networking.NetworkSystem;

namespace Prowl.Runtime.Networking;

// Snapshots carry transforms, unreliably. Every tick each peer is sent the transforms of what is moving, and the
// pose a still object came to rest at until it acknowledges it. A lost snapshot is never resent as such: a pose it
// held is still unacknowledged, so a later snapshot carries it, and objects on the move send theirs every tick anyway.
// [Sync] state does not travel here: it goes reliably, through SyncSystem.
//
// The receiver acknowledges each snapshot that asks for it, naming the objects in it that it couldn't place (not
// spawned there yet, say), which stay pending. A moving position goes as a step from the latest one the receiver
// acknowledged, so both ends keep a short history of positions by tick.
//
// The host relays a client's transforms: moving ones are passed on as they arrive, under the client's tick and
// clock, and the pose it came to rest at is owed to everyone else until they acknowledge it.
//
// Sending and receiving reuse their buffers, so a steady stream of snapshots allocates nothing per tick.
internal sealed class SnapshotSystem : IDisposable
{
    // Sent but unacknowledged snapshots remembered per peer. Older ones are forgotten, and their state resent.
    private const int MaxOutstanding = 256;

    // Snapshot ticks a position baseline stays usable for, and how far back a receiver keeps positions for them.
    private const uint MaxBaselineAge = 64;
    private const uint PositionHistoryTicks = 96;

    private readonly HashSet<uint> _ackedIds = new();
    private readonly List<Item> _items = new();
    private readonly NetWriter _itemBytes = new(4096);
    private readonly NetWriter _message = new(4096);
    private readonly List<(NetworkObject Entry, TransformUpdate Update)> _moving = new();
    private readonly Dictionary<int, PeerState> _peers = new();
    private readonly HashSet<uint> _rejectedObjects = new();
    private readonly List<(TransformGroup Group, NetworkObject Entry, TransformUpdate Update)> _relay = new();
    private readonly NetworkSystem _scene;
    private readonly List<NetworkObject> _scratchObjects = new();

    private readonly List<(uint NetId, uint Generation)> _scratchPositions = new();
    private readonly List<NetPeer> _sendTargets = new();
    private readonly Stack<SentSnapshot> _sentPool = new();
    private readonly List<NetPeer> _targets = new();

    // Never reset with the scene, so a late acknowledgement can't match a later scene's snapshot.
    private uint _nextSnapshotId = 1;

    private double PositionStep => GridPosition.Step(_scene.Configuration.PositionPrecision);

    public SnapshotSystem(NetworkSystem scene) => _scene = scene;

    public void Dispose()
    {
        _itemBytes.Dispose();
        _message.Dispose();
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

    /// <summary><paramref name="peer" /> is loading the world again: nothing sent it before counts any more.</summary>
    internal void Delivered(NetPeer peer)
    {
        if (_peers.TryGetValue(peer.Id, out PeerState? state))
        {
            state.Poses.Clear();
            state.Baselines.Clear();
            state.SentPositions.Clear();
            Forget(state);
        }
    }

    /// <summary>A spawn is carrying <paramref name="peer" /> the pose of <paramref name="root" />'s tree.</summary>
    internal void Delivered(NetPeer peer, GameObject root)
    {
        if (!_peers.TryGetValue(peer.Id, out PeerState? state))
        {
            return;
        }

        foreach (GameObject go in TreeOf(root))
        {
            if (EntryAt(go) is { } entry)
            {
                state.Poses.Remove(entry);
            }
        }
    }

    private static List<GameObject> TreeOf(GameObject root) => [root, .. root.GetChildrenDeep()];

    /// <summary>The networked object rooted at <paramref name="go" />, if it is one.</summary>
    private NetworkObject? EntryAt(GameObject go) =>
        _scene.Entries.TryGetValue(go.Identifier, out NetworkObject? entry) && entry.GameObject == go ? entry : null;

    /// <summary><paramref name="peer" /> no longer has <paramref name="root" />'s tree: forgets what it was owed and knew of it.</summary>
    internal void Withdraw(NetPeer peer, GameObject root)
    {
        if (!_peers.TryGetValue(peer.Id, out PeerState? state))
        {
            return;
        }

        Delivered(peer, root);
        foreach (GameObject go in TreeOf(root))
        {
            if (EntryAt(go) is { } entry)
            {
                state.SentPositions.Remove(entry);
                state.Baselines.Remove(entry);
            }

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

    /// <summary>Whether <paramref name="tick" /> is one the object sends on, spread across ticks by its NetId.</summary>
    private static bool SendsOn(NetworkObject? entry, uint tick)
    {
        int interval = entry == null ? 1 : NetworkAccessor.SendIntervalOf(entry.GameObject);
        return interval <= 1 || (tick + entry!.NetId) % (uint)interval == 0;
    }

    private uint Epoch => (uint)_scene.SceneGeneration;

    // --- Sending --------------------------------------------------------------------------------

    /// <summary>Takes this tick's transforms and sends every ready peer its snapshot.</summary>
    internal void Send()
    {
        // The network tick the game sees, and never the same twice, so receivers can order what each peer sends.
        uint tick = _scene.Tick = Math.Max(_scene.Tick + 1, _scene.NetworkTick);
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
            _scratchObjects.Clear();
            _scratchObjects.AddRange(state.SentPositions.Keys.Where(entry => !IsCurrent(entry)));
            foreach (NetworkObject entry in _scratchObjects)
            {
                state.SentPositions.Remove(entry);
                state.Baselines.Remove(entry);
            }

            _scratchPositions.Clear();
            foreach ((uint netId, uint generation) in state.ReceivedPositions.Keys)
            {
                if (!_scene.Entries.TryGetNet(netId, out NetworkObject? entry) || entry.Generation != generation)
                {
                    _scratchPositions.Add((netId, generation));
                }
            }

            foreach ((uint NetId, uint Generation) key in _scratchPositions)
            {
                state.ReceivedPositions.Remove(key);
            }

        }
    }

    private void CaptureTransforms(uint tick, double now)
    {
        foreach (NetworkObject entry in _scene.Entries.Values)
        {
            GameObject go = entry.GameObject;
            if (!go.IsValid() || !SendsOn(entry, tick) || !go.Network.HasTransformSync || !_scene.HasControl(go))
            {
                continue;
            }

            double previous = NetworkAccessor.SendIntervalOf(go) > 1 ? entry.LastCaptureTime : _scene.LastTransformTime;
            entry.LastCaptureTime = now;
            if (entry.TransformSync.Capture(go, now, previous) is not { } update)
            {
                continue;
            }

            // The pose it came to rest at must arrive; poses on the way there only need to keep coming.
            if ((update.Channels & TransformChannels.Exact) != 0)
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
            if (peer.Id != except && Sees(entry, peer))
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
            if (Sees(entry, peer))
            {
                AddRecord(tick, now, entry, update, null, state, tick);
            }
        }

        _scratchObjects.Clear();
        foreach ((NetworkObject entry, PendingPose pose) in state.Poses)
        {
            if (!entry.GameObject.IsValid() || !_scene.Entries.TryGetNet(entry.NetId, out NetworkObject? current) ||
                current != entry || ControllerOf(entry.GameObject) != pose.Controller || !Sees(entry, peer))
            {
                _scratchObjects.Add(entry);
            }
            else if (pose.Changed > pose.LastSent || tick - pose.LastSent >= resend)
            {
                AddRecord(pose.ControllerTick, pose.SentAt, entry, pose.Update, entry, state, tick);
            }
        }

        foreach (NetworkObject entry in _scratchObjects)
        {
            state.Poses.Remove(entry);
        }

        if (_items.Count > 0)
        {
            SendItems(peer, state, tick, connection);
        }
    }

    /// <summary>Adds a transform record, its moving position as a step from the latest one the peer acknowledged.</summary>
    private void AddRecord(uint groupTick, double sentAt, NetworkObject entry, TransformUpdate update,
        NetworkObject? pose, PeerState state, uint tick)
    {
        int offset = _itemBytes.Length;
        TransformRecord record = new(entry.NetId, entry.Generation, update);
        GridPosition grid = default;
        if (record.HasGridPosition)
        {
            grid = GridPosition.Of(update.Position, PositionStep);
            record = record with { Grid = grid };
            if (state.Baselines.TryGetValue(entry, out (uint Tick, GridPosition Grid) baseline) &&
                tick - baseline.Tick is > 0 and < MaxBaselineAge)
            {
                record = record with { BaselineAge = tick - baseline.Tick, Grid = grid - baseline.Grid };
            }

            PositionsOf(state.SentPositions, entry).Add(tick, grid);
        }

        record.Write(_itemBytes);
        _items.Add(new Item(groupTick, sentAt, offset, _itemBytes.Length - offset, pose,
            record.HasGridPosition ? entry : null, grid));
    }

    private static PositionHistory PositionsOf<TKey>(Dictionary<TKey, PositionHistory> histories, TKey key)
        where TKey : notnull
    {
        if (!histories.TryGetValue(key, out PositionHistory? history))
        {
            histories[key] = history = new PositionHistory();
        }

        return history;
    }

    private bool IsCurrent(NetworkObject entry) => entry.GameObject.IsValid() &&
                                                  _scene.Entries.TryGetNet(entry.NetId, out NetworkObject? current) &&
                                                  current == entry;

    /// <summary>Splits the items into snapshots that fit the connection, and sends them.</summary>
    private void SendItems(NetPeer peer, PeerState? state, uint tick, Connection connection)
    {
        (Delivery delivery, int budget) = Budget(peer, connection);
        int start = 0;
        while (start < _items.Count)
        {
            int size = SnapshotMessage.HeaderSize + 5 + 5, end = start;
            do
            {
                Item item = _items[end];
                bool newGroup = end == start || !SameGroup(_items[end - 1], item);
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

    private static bool SameGroup(Item a, Item b) => a.GroupTick == b.GroupTick && a.GroupSentAt == b.GroupSentAt;

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
                if (item.Pose is { } entry)
                {
                    sent ??= Rent(tick);
                    sent.Poses.Add(entry);
                    state.Poses[entry] = state.Poses[entry] with { LastSent = tick };
                }
                else if (item.Moved is { } moved)
                {
                    sent ??= Rent(tick);
                    sent.Positions.Add((moved, tick, item.Grid));
                }
            }

            if (sent != null)
            {
                id = _nextSnapshotId++;
                if (_nextSnapshotId == 0)
                {
                    _nextSnapshotId = 1;
                }

                Remember(state, id, sent);
            }
        }

        _message.Reset();
        new SnapshotMessage(id, Epoch, tick).Write(_message);

        // The records go in runs that share a controller tick.
        int groups = 0;
        for (int i = start; i < end; i++)
        {
            if (i == start || !SameGroup(_items[i - 1], _items[i]))
            {
                groups++;
            }
        }

        new TransformGroupBlock((uint)groups).Write(_message);
        for (int i = start; i < end;)
        {
            int runEnd = i + 1;
            while (runEnd < end && SameGroup(_items[i], _items[runEnd]))
            {
                runEnd++;
            }

            new TransformGroup(_items[i].GroupTick, _items[i].GroupSentAt, (uint)(runEnd - i)).Write(_message);
            for (; i < runEnd; i++)
            {
                _message.WriteBytes(_itemBytes.WrittenSpan.Slice(_items[i].Offset, _items[i].Length));
            }
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
        sent.Poses.Clear();
        sent.Positions.Clear();
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
    private (Delivery Delivery, int Budget) Budget(NetPeer peer, Connection connection)
    {
        int limit = peer.MaxMessageSize;
        Delivery delivery = Delivery.Reliable;
        if (_scene.Session!.Transport.Capabilities.Supports(Delivery.Unreliable) &&
            connection.GetMaxMessageSize(Delivery.Unreliable) >= SnapshotMessage.MinMessageSize)
        {
            delivery = Delivery.Unreliable;
            limit = Math.Min(limit, connection.GetMaxMessageSize(Delivery.Unreliable));
        }

        // Courier adds a two-byte message id.
        return (delivery, limit - sizeof(ushort));
    }

    /// <summary>Ticks to wait for an acknowledgement before sending owed state again: a round trip, and a little.</summary>
    private static uint ResendTicks(NetPeer peer)
    {
        double interval = Networking.TickInterval;
        double roundTrip = peer.Connection?.RoundTripTime.TotalSeconds ?? 0;
        return (uint)Math.Max(2, Math.Ceiling((roundTrip * 1.25 + interval) / interval));
    }

    /// <summary>Acknowledges, to each peer that sent any, the snapshots received since the last call.</summary>
    internal void SendAcks()
    {
        NetSession session = _scene.Session!;
        foreach ((int peerId, PeerState state) in _peers)
        {
            // Rejections only travel with an acknowledgement, so without one they are dropped.
            if (state.Received.Count == 0 || !session.TryGetPeer(peerId, out NetPeer? peer))
            {
                state.Received.Clear();
                state.RejectedObjects.Clear();
                continue;
            }

            _message.Reset();
            SnapshotAckMessage.WriteIds(_message, CollectionsMarshal.AsSpan(state.Received));
            _message.WriteVarUInt32((uint)state.RejectedObjects.Count);
            foreach (uint netId in state.RejectedObjects)
            {
                _message.WriteVarUInt32(netId);
            }

            state.Received.Clear();
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
                TransformRecord record = TransformRecord.Read(ref reader);
                (uint netId, uint generation, TransformUpdate update, _, _) = record;
                if (!_scene.Entries.TryGetNet(netId, out NetworkObject? entry) || !entry.GameObject.IsValid() ||
                    entry.Generation != generation)
                {
                    state.RejectedObjects.Add(netId);
                    continue;
                }

                if (record.HasGridPosition)
                {
                    if (ResolvePosition(state, header.Tick, record) is not { } position)
                    {
                        state.RejectedObjects.Add(netId);
                        continue;
                    }

                    update = update with { Position = position };
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

        if (!reader.IsAtEnd)
        {
            throw new InvalidDataException("Snapshot contains trailing bytes.");
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

    /// <summary>The position a record carries, or null when it steps from a position this peer doesn't have.</summary>
    private Float3? ResolvePosition(PeerState state, uint tick, TransformRecord record)
    {
        PositionHistory history = PositionsOf(state.ReceivedPositions, (record.NetId, record.Generation));
        GridPosition grid = record.Grid;
        if (record.BaselineAge != 0)
        {
            if (!history.TryGet(tick - record.BaselineAge, out GridPosition baseline))
            {
                return null;
            }

            grid = baseline + grid;
        }

        history.Add(tick, grid);
        return grid.ToFloat3(PositionStep);
    }

    /// <summary>
    ///     Passes a client's transforms on. Moving ones go straight out, as before; the pose it came to rest at is
    ///     owed to everyone else until they acknowledge it.
    /// </summary>
    private void Relay(NetPeer sender)
    {
        bool moving = false;
        foreach ((TransformGroup group, NetworkObject entry, TransformUpdate update) in _relay)
        {
            if ((update.Channels & TransformChannels.Exact) != 0)
            {
                Pend(entry, new PendingPose(_scene.Tick + 1, 0, sender.Id, group.Tick, group.SentAt, update),
                    sender.Id);
            }
            else
            {
                ClearPose(entry);
                moving = true;
            }
        }

        if (!moving)
        {
            return;
        }

        _sendTargets.Clear();
        _sendTargets.AddRange(Targets());
        foreach (NetPeer peer in _sendTargets)
        {
            if (peer != sender && _scene.IsPeerReady(peer) && peer.Connection is { IsConnected: true } connection)
            {
                PeerState state = State(peer);
                AddRelayRecords(peer, state);
                if (_items.Count > 0)
                {
                    SendItems(peer, state, _scene.Tick, connection);
                }
            }
        }
    }

    /// <summary>The moving transforms being relayed that <paramref name="peer" /> has.</summary>
    private void AddRelayRecords(NetPeer peer, PeerState state)
    {
        _items.Clear();
        _itemBytes.Reset();
        foreach ((TransformGroup group, NetworkObject entry, TransformUpdate update) in _relay)
        {
            if ((update.Channels & TransformChannels.Exact) == 0 && Sees(entry, peer))
            {
                AddRecord(group.Tick, group.SentAt, entry, update, null, state, _scene.Tick);
            }
        }
    }

    internal void OnAck(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        _ackedIds.Clear();
        _rejectedObjects.Clear();
        uint count = SnapshotAckMessage.ReadCount(ref reader, 1);
        for (uint i = 0; i < count; i++)
        {
            _ackedIds.Add(reader.ReadVarUInt32());
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

            // A pose the peer couldn't place stays owed, and goes again at once; the rest is done if nothing changed since.
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

            // A tick that carried the object twice is no baseline: the peer may hold either.
            foreach ((NetworkObject entry, uint tick, GridPosition grid) in sent.Positions)
            {
                if (!_rejectedObjects.Contains(entry.NetId) &&
                    state.SentPositions.TryGetValue(entry, out PositionHistory? history) &&
                    history.TryGet(tick, out GridPosition latest) && latest == grid &&
                    (!state.Baselines.TryGetValue(entry, out (uint Tick, GridPosition Grid) baseline) ||
                     (int)(tick - baseline.Tick) > 0))
                {
                    state.Baselines[entry] = (tick, grid);
                }
            }

            Release(sent);
        }

        // The peer couldn't place these, so their next positions go whole.
        foreach (uint netId in _rejectedObjects)
        {
            if (_scene.Entries.TryGetNet(netId, out NetworkObject? entry))
            {
                state.Baselines.Remove(entry);
            }
        }
    }

    // --- State ------------------------------------------------------------------------------------

    /// <summary>A rest pose owed to a peer, under the tick and clock of the controller that took it.</summary>
    private readonly record struct PendingPose(
        uint Changed,
        uint LastSent,
        int Controller,
        uint ControllerTick,
        double SentAt,
        TransformUpdate Update);

    /// <summary>The poses and positions one sent snapshot carried that wait on its acknowledgement. Pooled.</summary>
    private sealed class SentSnapshot
    {
        public readonly List<NetworkObject> Poses = new();
        public readonly List<(NetworkObject Entry, uint Tick, GridPosition Grid)> Positions = new();
        public uint Tick;
    }

    /// <summary>
    ///     One transform record of a snapshot, its bytes at [Offset, Offset + Length) of the item buffer, in its
    ///     controller's group.
    /// </summary>
    private readonly record struct Item(
        uint GroupTick,
        double GroupSentAt,
        int Offset,
        int Length,
        NetworkObject? Pose,
        NetworkObject? Moved = null,
        GridPosition Grid = default);

    /// <summary>What this peer owes another, and has received from it.</summary>
    private sealed class PeerState
    {
        public readonly Dictionary<NetworkObject, PendingPose> Poses = new();
        public readonly List<uint> Received = new();
        public readonly HashSet<uint> RejectedObjects = new();
        public readonly Dictionary<uint, SentSnapshot> Sent = new();
        public readonly Queue<uint> SentOrder = new();

        // Positions sent the other by snapshot tick and the latest it acknowledged, and positions received from it.
        public readonly Dictionary<NetworkObject, PositionHistory> SentPositions = new();
        public readonly Dictionary<NetworkObject, (uint Tick, GridPosition Grid)> Baselines = new();
        public readonly Dictionary<(uint NetId, uint Generation), PositionHistory> ReceivedPositions = new();
    }

    /// <summary>Recent grid positions of one object by snapshot tick. A tick that carried two different ones is no baseline.</summary>
    private sealed class PositionHistory
    {
        private readonly List<(uint Tick, GridPosition Grid, bool Ambiguous)> _positions = new();
        private uint _newest;

        public void Add(uint tick, GridPosition grid)
        {
            if (_positions.Count == 0 || (int)(tick - _newest) > 0)
            {
                _newest = tick;
            }

            if (_newest - tick >= PositionHistoryTicks)
            {
                return;
            }

            for (int i = _positions.Count - 1; i >= 0; i--)
            {
                (uint at, GridPosition held, bool ambiguous) = _positions[i];
                if (_newest - at >= PositionHistoryTicks)
                {
                    _positions.RemoveAt(i);
                }
                else if (at == tick)
                {
                    _positions[i] = (at, held, ambiguous || held != grid);
                    return;
                }
            }

            _positions.Add((tick, grid, false));
        }

        public bool TryGet(uint tick, out GridPosition grid)
        {
            foreach ((uint at, GridPosition held, bool ambiguous) in _positions)
            {
                if (at == tick && !ambiguous)
                {
                    grid = held;
                    return true;
                }
            }

            grid = default;
            return false;
        }
    }
}
