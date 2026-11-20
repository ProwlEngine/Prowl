// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
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
    private readonly List<Moving> _moving = new();
    private readonly List<TransformUpdate> _movingUpdates = new();
    private readonly List<uint> _movingAges = new();

    // Clients' moving transforms taken since the last tick, the latest of each, to pass on with the next one.
    private readonly List<(NetworkTransform Transform, TransformUpdate Update, double TakenAt, int Sender)> _relayed = new();

    // This peer's clock as the snapshots being built are sent, which their records count back from.
    private double _sendTime;

    // Each moving transform's record as last written this tick, and the baseline age it was written for: peers that
    // acknowledged the same tick get the same bytes.
    private readonly List<(uint Age, int Offset, int Length)> _movingRecords = new();
    private readonly NetWriter _movingBytes = new(4096);
    private readonly Dictionary<int, PeerState> _peers = new();
    private readonly HashSet<uint> _rejectedObjects = new();
    private readonly List<(uint Tick, double SentAt, NetworkTransform Transform, TransformUpdate Update)> _relay = new();
    private readonly NetworkSystem _scene;
    private readonly List<NetworkTransform> _scratchTransforms = new();

    private readonly List<(uint NetId, uint Generation, byte Index)> _scratchPositions = new();
    private readonly List<NetPeer> _sendTargets = new();
    private readonly Stack<SentSnapshot> _sentPool = new();
    private readonly List<NetPeer> _targets = new();

    /// <summary>Off, moving positions always go whole and ask for no acknowledgement. For measuring what the baselines save.</summary>
    internal static bool AcknowledgedBaselines = true;

    // Never reset with the scene, so a late acknowledgement can't match a later scene's snapshot.
    private uint _nextSnapshotId = 1;

    // Each transform has a slot in every peer's sent positions, kept side by side so a peer's are walked in order.
    private readonly List<NetworkTransform?> _slotOwners = new();
    private readonly Stack<int> _freeSlots = new();
    private uint _stamp;

    // The grid step is a decimal conversion, so it is worked out once for each precision and not for each record.
    private float _stepPrecision = float.NaN;
    private double _step;

    private double PositionStep
    {
        get
        {
            float precision = _scene.Configuration.PositionPrecision;
            if (precision != _stepPrecision)
            {
                _stepPrecision = precision;
                _step = GridPosition.Step(precision);
            }

            return _step;
        }
    }

    public SnapshotSystem(NetworkSystem scene) => _scene = scene;

    public void Dispose()
    {
        _itemBytes.Dispose();
        _message.Dispose();
        _movingBytes.Dispose();
    }

    /// <summary>Forgets everything owed and received, as when the scene changes.</summary>
    internal void Clear()
    {
        foreach (PeerState state in _peers.Values)
        {
            Forget(state);
        }

        _peers.Clear();
        ForgetRelayed();
    }

    private void ForgetRelayed()
    {
        foreach ((NetworkTransform transform, _, _, _) in _relayed)
        {
            transform.RelayIndex = -1;
        }

        _relayed.Clear();
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
            Array.Clear(state.Positions);
            state.Ambiguous.Clear();
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

        if (_scene.FindEntry(root) is { } entry)
        {
            foreach (NetworkTransform transform in entry.Transforms)
            {
                state.Poses.Remove(transform);
            }
        }
    }

    /// <summary><paramref name="peer" /> no longer has <paramref name="root" />'s tree: forgets what it was owed and knew of it.</summary>
    internal void Withdraw(NetPeer peer, GameObject root)
    {
        if (!_peers.TryGetValue(peer.Id, out PeerState? state) || _scene.FindEntry(root) is not { } entry)
        {
            return;
        }

        foreach (NetworkTransform transform in entry.Transforms)
        {
            state.Poses.Remove(transform);
            int slot = transform.SnapshotSlot;
            if (slot >= 0 && slot < state.Positions.Length)
            {
                state.Positions[slot] = default;
            }
        }
    }

    /// <summary>The transform's slot, given one the first time it is asked for.</summary>
    private int SlotOf(NetworkTransform transform)
    {
        int slot = transform.SnapshotSlot;
        if (slot >= 0 && slot < _slotOwners.Count && ReferenceEquals(_slotOwners[slot], transform))
        {
            return slot;
        }

        if (_freeSlots.Count > 0)
        {
            slot = _freeSlots.Pop();
            _slotOwners[slot] = transform;
        }
        else
        {
            slot = _slotOwners.Count;
            _slotOwners.Add(transform);
        }

        transform.SnapshotSlot = slot;
        transform.SnapshotStamp = ++_stamp;
        return slot;
    }

    /// <summary>What <paramref name="state" />'s peer was last sent of a transform's position, empty if nothing yet.</summary>
    private ref SentPosition PositionFor(NetworkTransform transform, PeerState state) =>
        ref PositionAt(SlotOf(transform), transform.SnapshotStamp, state);

    private ref SentPosition PositionAt(int slot, uint stamp, PeerState state)
    {
        if (state.Positions.Length <= slot)
        {
            Array.Resize(ref state.Positions, Math.Max(_slotOwners.Count, state.Positions.Length * 2));
        }

        ref SentPosition position = ref state.Positions[slot];
        if (position.Stamp != stamp)
        {
            position = new SentPosition { Stamp = stamp };
        }

        return ref position;
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

    private static int ControllerOf(NetworkObject entry) => entry.Owner == NoOwner ? NetPeer.HostId : entry.Owner;

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

    /// <summary>Whether <paramref name="tick" /> is one the transform sends on, spread across ticks by its object's NetId.</summary>
    private static bool SendsOn(NetworkTransform transform, NetworkObject entry, uint tick)
    {
        int interval = transform.Interval;
        return interval <= 1 || (tick + entry.NetId) % (uint)interval == 0;
    }

    private uint Epoch => (uint)_scene.SceneGeneration;

    // --- Sending --------------------------------------------------------------------------------

    /// <summary>Takes this tick's transforms and sends every ready peer its snapshot.</summary>
    internal void Send()
    {
        // The network tick the game sees, and never the same twice, so receivers can order what each peer sends.
        uint tick = _scene.Tick = Math.Max(_scene.Tick + 1, _scene.NetworkTick);
        double now = _sendTime = _scene.Now;
        CaptureTransforms(tick, now);
        AddRelayed();
        _sendTargets.Clear();
        _sendTargets.AddRange(Targets());
        foreach (NetPeer peer in _sendTargets)
        {
            if (_scene.Session != null && (!_scene.IsHost || _scene.IsPeerReady(peer)))
            {
                SendTo(peer, State(peer), tick, now);
            }
        }

        ClearMoving();
        _scene.LastTransformTime = now;

        // Every so often, forget what was kept about components that are gone.
        if (tick % 64 == 0)
        {
            Prune();
        }
    }

    private void Prune()
    {
        for (int slot = 0; slot < _slotOwners.Count; slot++)
        {
            if (_slotOwners[slot] is { } owner && (owner.SnapshotSlot != slot || !IsCurrent(owner)))
            {
                _slotOwners[slot] = null;
                _freeSlots.Push(slot);
            }
        }

        foreach (PeerState state in _peers.Values)
        {
            uint now = _scene.Tick;
            state.Ambiguous.RemoveWhere(sent => now - sent.Tick >= PositionHistoryTicks);

            _scratchPositions.Clear();
            foreach ((uint NetId, uint Generation, byte Index) key in state.ReceivedPositions.Keys)
            {
                if (!_scene.Entries.TryGetNet(key.NetId, out NetworkObject? entry) || entry.Generation != key.Generation ||
                    key.Index >= entry.Transforms.Length)
                {
                    _scratchPositions.Add(key);
                }
            }

            foreach ((uint NetId, uint Generation, byte Index) key in _scratchPositions)
            {
                state.ReceivedPositions.Remove(key);
            }
        }
    }

    private void CaptureTransforms(uint tick, double now)
    {
        foreach (NetworkObject entry in _scene.Entries.Values)
        {
            NetworkTransform[] transforms = entry.Transforms;
            if (transforms.Length == 0 || !entry.GameObject.IsValid() || !_scene.HasControl(entry))
            {
                continue;
            }

            foreach (NetworkTransform transform in transforms)
            {
                if (transform.IsNotValid() || !transform.Syncs || !SendsOn(transform, entry, tick))
                {
                    continue;
                }

                double previous = transform.Interval > 1 ? transform.LastCaptureTime : _scene.LastTransformTime;
                transform.LastCaptureTime = now;
                if (transform.Sync.Capture(transform.Moved, now, previous) is not { } update)
                {
                    continue;
                }

                // The pose it came to rest at must arrive; poses on the way there only need to keep coming.
                if ((update.Channels & TransformChannels.Exact) != 0)
                {
                    Pend(transform, new PendingPose(tick, 0, _scene.Session!.LocalPeer!.Id, tick, now, update), -1);
                }
                else
                {
                    bool gridded = (update.Channels & (TransformChannels.Position | TransformChannels.Exact)) ==
                                   TransformChannels.Position;
                    AddMoving(transform, update, now);
                    ClearPose(transform);
                }
            }
        }
    }

    private void Pend(NetworkTransform transform, PendingPose pose, int except)
    {
        foreach (NetPeer peer in Targets())
        {
            if (peer.Id != except && Sees(transform.Entry, peer))
            {
                State(peer).Poses[transform] = pose;
                transform.PoseOwed = true;
            }
        }
    }

    /// <summary>It's moving again: nobody needs the pose it rested at any more.</summary>
    private void ClearPose(NetworkTransform transform)
    {
        if (!transform.PoseOwed)
        {
            return;
        }

        transform.PoseOwed = false;
        foreach (PeerState state in _peers.Values)
        {
            state.Poses.Remove(transform);
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
        AddMovingRecords(peer, state, tick);

        _scratchTransforms.Clear();
        foreach ((NetworkTransform transform, PendingPose pose) in state.Poses)
        {
            if (!IsCurrent(transform) || ControllerOf(transform.Entry!) != pose.Controller ||
                !Sees(transform.Entry, peer))
            {
                _scratchTransforms.Add(transform);
            }
            else if (pose.Changed > pose.LastSent || tick - pose.LastSent >= resend)
            {
                AddRecord(pose.ControllerTick, pose.SentAt, transform, pose.Update, transform, state, tick);
            }
        }

        foreach (NetworkTransform transform in _scratchTransforms)
        {
            state.Poses.Remove(transform);
        }

        if (_items.Count > 0)
        {
            SendItems(peer, state, tick, connection);
        }
    }

    /// <summary>
    ///     Lists a moving transform to go to every peer that has it but <paramref name="except" />, under its
    ///     controller's tick and clock.
    /// </summary>
    private void AddMoving(NetworkTransform transform, in TransformUpdate update, double takenAt, int except = -1)
    {
        bool gridded = (update.Channels & (TransformChannels.Position | TransformChannels.Exact)) == TransformChannels.Position;
        _moving.Add(new Moving(transform, transform.Entry!.HiddenFrom, SlotOf(transform), transform.SnapshotStamp, gridded,
            gridded ? GridPosition.Of(update.Position, PositionStep) : default, except));
        _movingUpdates.Add(update);
        _movingAges.Add(AgeOf(takenAt));
        _movingRecords.Add(default);
    }

    private void ClearMoving()
    {
        _moving.Clear();
        _movingUpdates.Clear();
        _movingAges.Clear();
        _movingRecords.Clear();
        _movingBytes.Reset();
    }

    /// <summary>Adds the listed moving transforms that <paramref name="peer" /> has to the items being sent it.</summary>
    private void AddMovingRecords(NetPeer peer, PeerState state, uint tick)
    {
        Span<Moving> moving = CollectionsMarshal.AsSpan(_moving);
        Span<(uint Age, int Offset, int Length)> written = CollectionsMarshal.AsSpan(_movingRecords);
        for (int i = 0; i < moving.Length; i++)
        {
            if (moving[i].Except != peer.Id && (moving[i].Hidden is not { } hidden || !hidden.Contains(peer.Id)))
            {
                AddMovingRecord(tick, i, in moving[i], ref written[i], state);
            }
        }
    }

    /// <summary>Adds one listed moving transform, reusing the record written for another peer with the same baseline.</summary>
    private void AddMovingRecord(uint tick, int index, in Moving moving,
        ref (uint Age, int Offset, int Length) written, PeerState state)
    {
        NetworkTransform transform = moving.Transform;
        GridPosition grid = moving.Grid, step = grid;
        uint age = 0;
        bool gridded = moving.Gridded;
        if (gridded)
        {
            ref SentPosition sent = ref PositionAt(moving.Slot, moving.Stamp, state);
            if (AcknowledgedBaselines && sent.HasBaseline && tick - sent.BaselineTick is > 0 and < MaxBaselineAge)
            {
                age = tick - sent.BaselineTick;
                step = grid - sent.Baseline;
            }

            NoteSent(ref sent, transform, tick, grid, state);
        }

        int offset = _itemBytes.Length;
        if (written.Length != 0 && written.Age == age)
        {
            _itemBytes.WriteBytes(_movingBytes.WrittenSpan.Slice(written.Offset, written.Length));
        }
        else
        {
            NetworkObject entry = transform.Entry!;
            new TransformRecord(entry.NetId, entry.Generation, _movingUpdates[index], age, step, transform.Index,
                AgeMilliseconds: _movingAges[index]).Write(_itemBytes);
            written = (age, _movingBytes.Length, _itemBytes.Length - offset);
            _movingBytes.WriteBytes(_itemBytes.WrittenSpan.Slice(offset));
        }

        _items.Add(new Item(offset, _itemBytes.Length - offset, null, gridded ? transform : null, grid));
    }

    /// <summary>Remembers the position a peer is being sent. A tick that carries a transform at two positions is no baseline: the peer may hold either.</summary>
    private static void NoteSent(ref SentPosition sent, NetworkTransform transform, uint tick, GridPosition grid, PeerState state)
    {
        if (sent.HasLast && sent.LastTick == tick)
        {
            if (sent.Last != grid)
            {
                state.Ambiguous.Add((transform, tick));
            }
        }
        else
        {
            sent.HasLast = true;
            sent.LastTick = tick;
            sent.Last = grid;
        }
    }

    /// <summary>Adds a transform record, its moving position as a step from the latest one the peer acknowledged.</summary>
    private void AddRecord(uint takenTick, double takenAt, NetworkTransform transform, in TransformUpdate update,
        NetworkTransform? pose, PeerState state, uint tick, GridPosition? known = null)
    {
        NetworkObject entry = transform.Entry!;
        int offset = _itemBytes.Length;
        GridPosition grid = default, step = default;
        uint age = 0;
        bool gridded = (update.Channels & (TransformChannels.Position | TransformChannels.Exact)) == TransformChannels.Position;
        if (gridded)
        {
            step = grid = known ?? GridPosition.Of(update.Position, PositionStep);
            ref SentPosition sent = ref PositionFor(transform, state);
            if (AcknowledgedBaselines && sent.HasBaseline && tick - sent.BaselineTick is > 0 and < MaxBaselineAge)
            {
                age = tick - sent.BaselineTick;
                step = grid - sent.Baseline;
            }

            NoteSent(ref sent, transform, tick, grid, state);
        }

        new TransformRecord(entry.NetId, entry.Generation, update, age, step, transform.Index,
            (uint)Math.Max(0, (int)(tick - takenTick)), AgeOf(takenAt)).Write(_itemBytes);
        _items.Add(new Item(offset, _itemBytes.Length - offset, pose, gridded ? transform : null, grid));
    }

    private static PositionHistory PositionsOf((uint NetId, uint Generation, byte Index) key,
        Dictionary<(uint NetId, uint Generation, byte Index), PositionHistory> histories)
    {
        if (!histories.TryGetValue(key, out PositionHistory? history))
        {
            histories[key] = history = new PositionHistory();
        }

        return history;
    }

    /// <summary>True while the transform is still one of a networked object this peer has.</summary>
    private bool IsCurrent(NetworkTransform transform) =>
        transform.IsValid() && transform.Entry is { } entry && entry.GameObject.IsValid() &&
        _scene.Entries.TryGetNet(entry.NetId, out NetworkObject? current) && current == entry &&
        transform.Index < entry.Transforms.Length && ReferenceEquals(entry.Transforms[transform.Index], transform);

    /// <summary>How long before the snapshots being sent a pose was taken, in the milliseconds a record carries.</summary>
    private uint AgeOf(double takenAt) =>
        (uint)Math.Clamp(Math.Round((_sendTime - takenAt) * 1000), 0, TransformRecord.MaxAgeMilliseconds);

    /// <summary>Splits the items into snapshots that fit the connection, and sends them.</summary>
    private void SendItems(NetPeer peer, PeerState? state, uint tick, Connection connection)
    {
        (Delivery delivery, int budget) = Budget(peer, connection);
        int start = 0;
        while (start < _items.Count)
        {
            int size = SnapshotMessage.HeaderSize + 5, end = start;
            do
            {
                int itemSize = _items[end].Length;
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
                if (item.Pose is { } pose)
                {
                    sent ??= Rent(tick);
                    sent.Poses.Add(pose);
                    state.Poses[pose] = state.Poses[pose] with { LastSent = tick };
                }
                else if (item.Moved is { } moved && AcknowledgedBaselines)
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
        new SnapshotMessage(id, Epoch, tick, _sendTime).Write(_message);
        _message.WriteVarUInt32((uint)(end - start));

        // Items are written one after another, so the records are one stretch of the item buffer.
        Item first = _items[start], last = _items[end - 1];
        _message.WriteBytes(_itemBytes.WrittenSpan.Slice(first.Offset, last.Offset + last.Length - first.Offset));

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
        // What the host passes on is put on its own clock: the sender's, moved by the least delay seen from it.
        double delay = now - header.Time;
        state.ClockOffset = state.HasClock ? Math.Min(delay, state.ClockOffset + ClockRelax) : delay;
        state.HasClock = true;

        uint records = SnapshotMessage.ReadRecordCount(ref reader);
        {
            for (uint i = 0; i < records; i++)
            {
                TransformRecord record = TransformRecord.Read(ref reader);
                uint takenTick = header.Tick - record.TicksAgo;
                double takenAt = Math.Max(0, header.Time - record.AgeMilliseconds / 1000.0);
                (uint netId, uint generation, TransformUpdate update) = (record.NetId, record.Generation, record.Update);
                if (!_scene.Entries.TryGetNet(netId, out NetworkObject? entry) || !entry.GameObject.IsValid() ||
                    entry.Generation != generation || record.Index >= entry.Transforms.Length ||
                    entry.Transforms[record.Index].IsNotValid())
                {
                    state.RejectedObjects.Add(netId);
                    continue;
                }

                NetworkTransform transform = entry.Transforms[record.Index];
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
                if (transform.Syncs && !_scene.HasControl(entry) &&
                    (sender.IsHost || entry.Owner == sender.Id) &&
                    transform.Sync.Receive(transform.Moved, takenTick, takenAt, now, update) is
                        { } accepted && _scene.IsHost)
                {
                    _relay.Add((takenTick, takenAt, transform, accepted));
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
            Relay(sender, state.ClockOffset, now);
        }
    }

    /// <summary>The position a record carries, or null when it steps from a position this peer doesn't have.</summary>
    private Float3? ResolvePosition(PeerState state, uint tick, TransformRecord record)
    {
        PositionHistory history = PositionsOf((record.NetId, record.Generation, record.Index), state.ReceivedPositions);
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
    ///     Passes a client's transforms on. Moving ones go out with the host's next tick, the latest of each, as
    ///     Mirror's server sends at its own interval. The pose one came to rest at is owed to everyone else until they
    ///     acknowledge it.
    /// </summary>
    private void Relay(NetPeer sender, double clockOffset, double now)
    {
        foreach ((uint _, double sentAt, NetworkTransform transform, TransformUpdate update) in _relay)
        {
            double takenAt = Math.Min(now, sentAt + clockOffset);
            int index = transform.RelayIndex;
            bool waiting = index >= 0 && index < _relayed.Count && ReferenceEquals(_relayed[index].Transform, transform) &&
                           _relayed[index].Sender == sender.Id;
            if ((update.Channels & TransformChannels.Exact) != 0)
            {
                // The pose it came to rest at replaces a moving one still waiting to go.
                if (waiting)
                {
                    _relayed[index] = _relayed[index] with { Sender = NoSender };
                    transform.RelayIndex = -1;
                }

                Pend(transform, new PendingPose(_scene.Tick + 1, 0, sender.Id, _scene.Tick + 1, takenAt, update), sender.Id);
                continue;
            }

            ClearPose(transform);
            if (waiting)
            {
                // What an update takes fills in the channels it left out, so the newer one carries the older one's too.
                _relayed[index] = (transform, Merge(_relayed[index].Update, update), takenAt, sender.Id);
            }
            else
            {
                transform.RelayIndex = _relayed.Count;
                _relayed.Add((transform, update, takenAt, sender.Id));
            }
        }
    }

    /// <summary>
    ///     A later moving update with what an earlier one carried that it does not. A teleport in either stays, under
    ///     the number its sender gave it, and takes the place of a resume, which a record can't carry beside it.
    /// </summary>
    private static TransformUpdate Merge(in TransformUpdate earlier, in TransformUpdate later)
    {
        TransformChannels channels = later.Channels | earlier.Channels;
        bool laterResumed = (later.Channels & TransformChannels.Resumed) != 0;
        bool laterTeleported = (later.Channels & TransformChannels.Teleport) != 0;
        if ((channels & TransformChannels.Teleport) != 0)
        {
            channels &= ~TransformChannels.Resumed;
        }

        return later with
        {
            Channels = channels,
            StillFor = (channels & TransformChannels.Resumed) == 0 ? 0 : laterResumed ? later.StillFor : earlier.StillFor,
            Teleport = laterTeleported ? later.Teleport : earlier.Teleport,
        };
    }

    // A waiting transform nobody sent any more: its rest pose took its place.
    private const int NoSender = int.MinValue;

    // How far a peer's clock offset may rise with each snapshot, so it follows drift without following jitter.
    private const double ClockRelax = 0.0002;

    /// <summary>Lists the clients' moving transforms taken since the last tick, each for everyone but who sent it.</summary>
    private void AddRelayed()
    {
        foreach ((NetworkTransform transform, TransformUpdate update, double takenAt, int sender) in _relayed)
        {
            if (sender == NoSender)
            {
                continue;
            }

            transform.RelayIndex = -1;
            if (IsCurrent(transform) && ControllerOf(transform.Entry!) == sender)
            {
                AddMoving(transform, update, takenAt, sender);
            }
        }

        _relayed.Clear();
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
            foreach (NetworkTransform transform in sent.Poses)
            {
                if (!state.Poses.TryGetValue(transform, out PendingPose pose))
                {
                    continue;
                }

                if (transform.Entry is { } owner && _rejectedObjects.Contains(owner.NetId))
                {
                    state.Poses[transform] = pose with { LastSent = 0 };
                }
                else if (pose.Changed <= sent.Tick)
                {
                    state.Poses.Remove(transform);
                }
            }

            bool ambiguous = state.Ambiguous.Count > 0, rejected = _rejectedObjects.Count > 0;
            foreach ((NetworkTransform transform, uint tick, GridPosition grid) in sent.Positions)
            {
                if (transform.Entry is not { } owner || (rejected && _rejectedObjects.Contains(owner.NetId)) ||
                    (ambiguous && state.Ambiguous.Contains((transform, tick))))
                {
                    continue;
                }

                ref SentPosition position = ref PositionFor(transform, state);
                if (position.HasLast && (!position.HasBaseline || (int)(tick - position.BaselineTick) > 0))
                {
                    position.HasBaseline = true;
                    position.BaselineTick = tick;
                    position.Baseline = grid;
                }
            }

            Release(sent);
        }

        // The peer couldn't place these, so their next positions go whole.
        foreach (uint netId in _rejectedObjects)
        {
            if (_scene.Entries.TryGetNet(netId, out NetworkObject? entry))
            {
                foreach (NetworkTransform transform in entry.Transforms)
                {
                    PositionFor(transform, state).HasBaseline = false;
                }
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

    /// <summary>One of this tick's moving transforms, as much of it as sending it to each peer reads.</summary>
    private readonly record struct Moving(
        NetworkTransform Transform,
        HashSet<int>? Hidden,
        int Slot,
        uint Stamp,
        bool Gridded,
        GridPosition Grid,
        int Except);

    /// <summary>The poses and positions one sent snapshot carried that wait on its acknowledgement. Pooled.</summary>
    private sealed class SentSnapshot
    {
        public readonly List<NetworkTransform> Poses = new();
        public readonly List<(NetworkTransform Transform, uint Tick, GridPosition Grid)> Positions = new();
        public uint Tick;
    }

    /// <summary>
    ///     One transform record of a snapshot, its bytes at [Offset, Offset + Length) of the item buffer, in its
    ///     controller's group.
    /// </summary>
    private readonly record struct Item(
        int Offset,
        int Length,
        NetworkTransform? Pose,
        NetworkTransform? Moved = null,
        GridPosition Grid = default);

    /// <summary>What this peer owes another, and has received from it.</summary>
    private sealed class PeerState
    {
        public readonly Dictionary<NetworkTransform, PendingPose> Poses = new();
        public readonly List<uint> Received = new();
        public readonly HashSet<uint> RejectedObjects = new();
        public readonly Dictionary<uint, SentSnapshot> Sent = new();
        public readonly Queue<uint> SentOrder = new();

        // What it was last sent of each transform's position, by the transform's slot.
        public SentPosition[] Positions = [];

        // How far behind this peer's clock the other's runs, for putting what it sends on this one.
        public double ClockOffset;
        public bool HasClock;

        // The ticks that carried a transform at two positions, and positions received from the peer.
        public readonly HashSet<(NetworkTransform Transform, uint Tick)> Ambiguous = new();
        public readonly Dictionary<(uint NetId, uint Generation, byte Index), PositionHistory> ReceivedPositions = new();
    }


    /// <summary>Recent grid positions of one transform by snapshot tick. A tick that carried two different ones is no baseline.</summary>
    private sealed class PositionHistory
    {
        private const byte Empty = 0, Held = 1, Ambiguous = 2;
        private readonly (uint Tick, GridPosition Grid, byte State)[] _slots = new (uint, GridPosition, byte)[PositionHistoryTicks];
        private bool _any;
        private uint _newest;

        public void Add(uint tick, GridPosition grid)
        {
            if (!_any || (int)(tick - _newest) > 0)
            {
                _any = true;
                _newest = tick;
            }

            if (_newest - tick >= PositionHistoryTicks)
            {
                return;
            }

            ref (uint Tick, GridPosition Grid, byte State) slot = ref _slots[tick % PositionHistoryTicks];
            if (slot.State != Empty && slot.Tick == tick)
            {
                if (slot.Grid != grid)
                {
                    slot.State = Ambiguous;
                }
            }
            else
            {
                slot = (tick, grid, Held);
            }
        }

        public bool TryGet(uint tick, out GridPosition grid)
        {
            ref (uint Tick, GridPosition Grid, byte State) slot = ref _slots[tick % PositionHistoryTicks];
            if (slot.State == Held && slot.Tick == tick && _newest - tick < PositionHistoryTicks)
            {
                grid = slot.Grid;
                return true;
            }

            grid = default;
            return false;
        }
    }
}

/// <summary>A transform's position as last sent a peer, and the latest one that peer acknowledged.</summary>
internal struct SentPosition
{
    public uint Stamp;
    public bool HasLast, HasBaseline;
    public uint LastTick, BaselineTick;
    public GridPosition Last, Baseline;
}
