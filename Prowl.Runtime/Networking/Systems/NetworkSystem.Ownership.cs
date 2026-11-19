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
    ///     Whether <paramref name="component" /> sends its changed [Sync] members on <paramref name="tick" />: every
    ///     tick, or as far apart as its send interval, spread across ticks by its object's NetId.
    /// </summary>
    internal bool SendsStateOn(NetworkObject entry, Component component, uint tick)
    {
        int interval = NetworkAccessor.SendIntervalOf(component);
        return interval <= 1 || (tick + entry.NetId) % (uint)interval == 0;
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
        if (!reader.IsAtEnd || delivery != Delivery.Reliable || !Finite(velocity))
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
