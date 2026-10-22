// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;

using Prowl.Warp;

namespace Prowl.Runtime.Networking;

/// <summary>
///     Base of <see cref="NetList{T}" />, <see cref="NetDictionary{TKey,TValue}" /> and <see cref="NetSet{T}" />:
///     collections that, as a <see cref="SyncAttribute" /> property, send each change rather than their whole
///     contents.
/// </summary>
/// <remarks>
///     <para>
///         Declare one get-only, as in <c>[Sync] public NetList&lt;int&gt; Scores { get; } = new();</c>. Whoever
///         controls the object changes it; anywhere else, changes are ignored (with a warning, once). Changes
///         are sent at the next network tick, in order, and late joiners get the whole collection. Without a
///         network session, or before it belongs to a component, it's an ordinary collection.
///     </para>
///     <para>
///         <c>Changed</c> fires for each change, both where it's made and where it's received, but not for the
///         contents that come with a scene snapshot or spawn: those are in place before the component starts.
///         Received changes are raised once the whole update they came in is applied, one after another, like
///         [Change] hooks. Items are sent as they were when added, so changing an item object afterwards isn't
///         sent. Replace it.
///     </para>
/// </remarks>
public abstract class NetCollection
{
    private const byte Whole = 0, Changes = 1, RpcState = 2;
    private uint _changeCount;

    // The changes not sent yet, already written, the first of them being number _changesFrom.
    private NetWriter? _changes;
    private uint _changesFrom;
    private int _changesSession;
    private SyncMember? _member;

    private Component? _owner;

    // How many changes this peer's contents include: each one sent from here or applied from elsewhere.
    // Each peer counts the same changes, so a peer can tell the ones its contents already have. Those
    // turn up when the whole collection is sent to a joiner while changes are still on their way.
    private uint _sequence;

    public abstract int Count { get; }

    /// <summary>The component this collection is a [Sync] property of, once it's been used as one.</summary>
    internal void Bind(Component component, SyncMember member)
    {
        if (ReferenceEquals(_owner, component))
        {
            _member = member;
            return;
        }

        if (_owner != null)
        {
            throw new InvalidOperationException(
                $"This {GetType().Name} is already {_owner.GetType().Name}.{_member!.Name}. A network collection can only be one [Sync] property.");
        }

        (_owner, _member) = (component, member);
    }

    /// <summary>
    ///     Called by each change before it's made. False if this peer may not make it, so it must be dropped.
    ///     Otherwise <paramref name="record" /> is where to write the change for sending, or null if it doesn't
    ///     go anywhere.
    /// </summary>
    private protected bool BeginChange(out NetWriter? record)
    {
        record = null;
        if (_owner == null || _member == null || !Networking.IsActive)
        {
            return true;
        }

        if (!Networking.HasControl(_owner, _member.Flags))
        {
            Networking.WarnSyncOnce(_owner, _member, "was changed on a peer that doesn't control it");
            return false;
        }

        if (!Networking.MarkSyncDirty(_owner, _member.Slot))
        {
            return true;
        }

        // What a previous session left unsent goes nowhere now.
        if (_changes == null || _changesSession != Networking.SessionNumber)
        {
            (_changes ??= new NetWriter()).Reset();
            _changeCount = 0;
            _changesSession = Networking.SessionNumber;
        }

        if (_changeCount == 0)
        {
            _changesFrom = _sequence;
        }

        _changeCount++;
        _sequence++;
        record = _changes;
        return true;
    }

    internal void WriteAll(NetWriter writer)
    {
        writer.WriteByte(Whole);
        writer.WriteVarUInt32(_sequence);
        WriteItems(writer);
    }

    internal void WriteChanges(NetWriter writer)
    {
        bool pending = _changes != null && _changesSession == Networking.SessionNumber;
        writer.WriteByte(Changes);
        writer.WriteVarUInt32(pending ? _changesFrom : _sequence);
        writer.WriteVarUInt32(pending ? _changeCount : 0);
        if (pending)
        {
            writer.WriteBytes(_changes!.WrittenSpan);
        }
    }

    internal void ClearChanges()
    {
        _changes?.Reset();
        _changeCount = 0;
    }

    // Include pending operations for callbacks, plus a complete recovery state. Neither is consumed:
    // peers outside the RPC's recipients still need their ordinary tick update.
    internal void WriteRpc(NetWriter writer)
    {
        using NetWriter changes = new(), whole = new();
        WriteChanges(changes);
        WriteAll(whole);
        writer.WriteByte(RpcState);
        writer.WriteBytesWithLength(changes.WrittenSpan);
        writer.WriteBytesWithLength(whole.WrittenSpan);
    }

    internal abstract NetCollection CreateEmptyCopy();

    internal void Validate(ref NetReader reader, bool initial = false)
    {
        NetCollection copy = CreateEmptyCopy();
        using NetWriter state = new();
        WriteAll(state);
        NetReader baseline = new(state.WrittenSpan);
        copy.Read(ref baseline, true);
        copy.Read(ref reader, initial);
    }

    /// <summary>Applies what <see cref="WriteAll" /> or <see cref="WriteChanges" /> wrote on another peer.</summary>
    internal void Read(ref NetReader reader, bool initial)
    {
        byte kind = reader.ReadByte();
        if (initial && kind != Whole)
        {
            throw new InvalidDataException("Initial collection state must contain complete contents.");
        }

        if (kind == RpcState)
        {
            ReadRpcState(ref reader);
            return;
        }

        uint sequence = reader.ReadVarUInt32();
        if (kind == Whole)
        {
            ReadItems(ref reader);
            _sequence = sequence;
            ClearChanges();
            return;
        }

        if (kind != Changes)
        {
            throw new InvalidDataException($"Unknown network collection payload {kind}.");
        }

        uint count = reader.ReadVarUInt32();
        if (sequence > _sequence)
        {
            // Changes this peer never had. The channel is reliable and ordered, so this means a bug, not loss.
            Debug.LogWarning(
                $"[Network] {Describe()} missed {sequence - _sequence} change(s); it may now differ from the sender's.");
            _sequence = sequence;
        }

        for (uint i = 0; i < count; i++)
        {
            // Changes before this peer's count are already in its contents; they're read past, not applied.
            bool apply = sequence + i == _sequence;
            ReadChange(ref reader, apply);
            if (apply)
            {
                _sequence++;
            }
        }
    }

    private void ReadRpcState(ref NetReader reader)
    {
        ReadOnlySpan<byte> delta = reader.ReadBytesWithLength();
        ReadOnlySpan<byte> whole = reader.ReadBytesWithLength();
        NetReader header = new(delta);
        if (header.ReadByte() != Changes || whole.IsEmpty || whole[0] != Whole)
        {
            throw new InvalidDataException("Invalid RPC collection state.");
        }

        uint from = header.ReadVarUInt32();
        if (from <= _sequence)
        {
            NetReader operations = new(delta);
            Read(ref operations, false);
            if (!operations.IsAtEnd)
            {
                throw new InvalidDataException("Trailing collection operation bytes.");
            }
        }

        // Missing an earlier unreliable prerequisite makes its operations unsafe to replay. The
        // complete contents still recover the right state without inventing historical callbacks.
        NetReader contents = new(whole);
        Read(ref contents, true);
        if (!contents.IsAtEnd)
        {
            throw new InvalidDataException("Trailing collection state bytes.");
        }
    }

    /// <summary>Writes the count, then every item.</summary>
    private protected abstract void WriteItems(NetWriter writer);

    /// <summary>Replaces the contents with what <see cref="WriteItems" /> wrote, without raising events.</summary>
    private protected abstract void ReadItems(ref NetReader reader);

    /// <summary>Reads one change written at <see cref="BeginChange" />, and applies it if <paramref name="apply" />.</summary>
    private protected abstract void ReadChange(ref NetReader reader, bool apply);

    /// <summary>Raises a <c>Changed</c> event: right away for a change made here, after the update for a received one.</summary>
    private protected static void Notify<TChange>(Action<TChange>? handler, TChange change, bool received)
    {
        if (handler == null)
        {
            return;
        }

        if (received)
        {
            Networking.Notify(() => handler(change));
        }
        else
        {
            handler(change);
        }
    }

    /// <summary>A received change that doesn't fit this peer's contents is skipped, with a warning.</summary>
    private protected void Mismatch(string change) =>
        Debug.LogWarning($"[Network] {Describe()} received {change}, which doesn't fit its contents; skipped.");

    internal string Describe()
    {
        string name = _member == null ? GetType().Name : $"{_owner!.GetType().Name}.{_member.Name}";
        return $"{name} ({Count} items)";
    }

    /// <summary>A short description of a payload, for diagnostics.</summary>
    internal static string DescribePayload(ref NetReader reader)
    {
        byte kind = reader.ReadByte();
        uint sequence = reader.ReadVarUInt32();
        uint count = reader.ReadVarUInt32();
        return kind == Whole ? $"{count} items, after change #{sequence}" : $"{count} change(s) from #{sequence}";
    }
}
