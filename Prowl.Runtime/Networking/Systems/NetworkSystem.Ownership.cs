// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;

using Prowl.Runtime.Tasks;
using Prowl.Vector;
using Prowl.Courier;

namespace Prowl.Runtime.Networking;

internal sealed partial class NetworkSystem
{
    /// <summary>
    ///     After an owner change: the new owner gets the owner only values, and the last one gets what it wrote back
    ///     as the host has it, since changes of its own still on their way were dropped.
    /// </summary>
    internal void SendOwnerState(NetworkObject entry, int previous)
    {
        if (!IsHost || Session is not { } session)
        {
            return;
        }

        if (session.TryGetPeer(entry.Owner, out NetPeer? owner) && !owner!.IsLocal && Sees(entry, owner))
        {
            Sync.QueueMembers(owner, entry, static sync => (sync.Flags & SyncFlags.ToOwner) != 0);
        }

        if (session.TryGetPeer(previous, out NetPeer? last) && !last!.IsLocal && Sees(entry, last))
        {
            Sync.QueueMembers(last, entry, static sync => (sync.Flags & SyncFlags.FromOwner) != 0);
        }
    }

    /// <summary>
    ///     Whether the component at <paramref name="index" /> of <paramref name="entry" /> sends its changed [Sync]
    ///     members on <paramref name="tick" />: every tick, or once its send interval has passed since it last did.
    /// </summary>
    internal bool SendsStateOn(NetworkObject entry, int index, uint tick)
    {
        int interval = NetworkAccessor.SendIntervalOf(entry.Components[index]);
        if (interval <= 1)
        {
            return true;
        }

        entry.StateSent ??= new (uint, bool)[entry.Components.Length];
        ref (uint Tick, bool Set) sent = ref entry.StateSent[index];
        return IntervalPassed(ref sent.Tick, ref sent.Set, entry.NetId, (uint)interval, tick, false);
    }

    /// <summary>The component at <paramref name="index" /> sent its changes on <paramref name="tick" />.</summary>
    internal void NoteStateSent(NetworkObject entry, int index, uint tick)
    {
        if (entry.StateSent is { } sent)
        {
            sent[index] = (tick, true);
        }
    }

    /// <summary>
    ///     True once <paramref name="interval" /> ticks have passed since <paramref name="last" />. Ticks can skip
    ///     values, so it counts from the last turn and does not wait for a tick of its own. The first turn is spread
    ///     across ticks by the object's NetId. <paramref name="take" /> marks the turn as taken.
    /// </summary>
    internal static bool IntervalPassed(ref uint last, ref bool set, uint netId, uint interval, uint tick, bool take)
    {
        if (!set)
        {
            uint phase = (tick + netId) % interval;
            last = tick - (phase == 0 ? interval : phase);
            set = true;
        }

        if (tick - last < interval)
        {
            return false;
        }

        if (take)
        {
            last = tick;
        }

        return true;
    }

    internal void OnOwnershipState(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost || !sender.IsHost)
        {
            return;
        }

        OwnershipStateMessage message = OwnershipStateMessage.Read(ref reader);
        Guid id = message.Identifier;
        uint generation = message.Generation;
        Float3 velocity = message.Velocity;
        ReadOnlySpan<byte> poses = message.Poses;
        if (!reader.IsAtEnd || delivery != Delivery.Reliable || !NetworkValidation.Finite(velocity))
        {
            throw new InvalidDataException("Invalid ownership handoff state.");
        }

        if (!Entries.TryGetValue(id, out NetworkObject? entry) || entry.Generation != generation)
        {
            return;
        }

        entry.ApplyHandoffPoses(poses, sender);
        entry.TransferVelocity = velocity;
    }
}
