// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;

using Prowl.Runtime.Tasks;
using Prowl.Vector;
using Prowl.Warp;

namespace Prowl.Runtime.Networking;

internal sealed partial class NetworkSystem
{
    internal void SetOwnerTransfer(GameObject go, OwnerTransfer policy)
    {
        if (!IsActive || FindEntry(go) is not { TearingDown: false } entry || entry.GameObject != go)
        {
            return;
        }

        if (!IsHost)
        {
            throw new InvalidOperationException(
                "Only the host can change a spawned object's ownership transfer policy.");
        }

        using NetWriter message = new();
        new OwnerTransferMessage(go.Identifier, entry.Generation, policy).Write(message);
        BroadcastEngine(EngineMessage.OwnerTransfer, message.WrittenSpan);
    }

    internal void OnOwnerTransfer(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost || !sender.IsHost)
        {
            return;
        }

        OwnerTransferMessage message = OwnerTransferMessage.Read(ref reader);
        Guid id = message.Identifier;
        uint generation = message.Generation;
        OwnerTransfer policy = message.Policy;
        if (!Enum.IsDefined(policy) || !reader.IsAtEnd || delivery != Delivery.Reliable)
        {
            throw new InvalidDataException("Invalid ownership transfer policy.");
        }

        if (Entries.TryGetValue(id, out NetworkObject? entry) && entry.Generation == generation)
        {
            entry.GameObject.Network.ApplyOwnerTransfer(policy);
        }
    }

    internal bool TransferOwnership(GameObject go, bool take)
    {
        MainThreadContext.AssertMainThread();
        if (!IsReady || FindEntry(go) is not { TearingDown: false } entry || !entry.GameObject.IsValid())
        {
            return false;
        }

        NetSession session = Session!;
        int local = session.LocalPeer!.Id;
        if (take ? entry.Owner == local : entry.Owner == NoOwner)
        {
            return false;
        }

        if (!IsHost && (take ? entry.GameObject.Network.OwnerTransfer != OwnerTransfer.Takeover : entry.Owner != local))
        {
            return false;
        }

        if (IsHost)
        {
            entry.ChangeOwner(take ? local : NoOwner);
            return true;
        }

        using NetWriter message = new(), poses = new();
        if (!take)
        {
            // Ahead of the request, so the host has this peer's final values before it hands the object on.
            Sync.SendSyncChanges();
            entry.WriteHandoffPoses(poses, false);
        }

        new OwnershipRequestMessage(entry.GameObject.Identifier, entry.Generation, take,
            take ? Float3.Zero : entry.OwnershipVelocity(), poses.WrittenSpan).Write(message);
        SendEngine(session.HostPeer!, EngineMessage.OwnershipRequest, message.WrittenSpan);
        return true;
    }

    internal void OnOwnershipRequest(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (!IsHost || !IsPeerReady(sender))
        {
            return;
        }

        OwnershipRequestMessage message = OwnershipRequestMessage.Read(ref reader);
        Guid id = message.Identifier;
        uint generation = message.Generation;
        bool take = message.Take;
        Float3 velocity = message.Velocity;
        ReadOnlySpan<byte> poses = message.Poses;
        if (!reader.IsAtEnd || delivery != Delivery.Reliable || !Finite(velocity) || (take && !poses.IsEmpty))
        {
            throw new InvalidDataException("Invalid ownership transfer request.");
        }

        if (!Entries.TryGetValue(id, out NetworkObject? entry) || entry.TearingDown ||
            entry.Generation != generation ||
            !entry.GameObject.IsValid())
        {
            return;
        }

        if (take)
        {
            if (entry.GameObject.Network.OwnerTransfer != OwnerTransfer.Takeover || entry.Owner == sender.Id)
            {
                return;
            }
        }
        else
        {
            if (entry.Owner != sender.Id)
            {
                return;
            }

            entry.ApplyHandoffPoses(poses, sender);
            entry.TransferVelocity = velocity;
        }

        entry.ChangeOwner(take ? sender.Id : NoOwner);
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

    internal void SetNetworkFlags(GameObject go, NetworkFlags flags)
    {
        if (!IsActive || FindEntry(go) is not { TearingDown: false } entry)
        {
            return;
        }

        MainThreadContext.AssertMainThread();
        if (!IsHost)
        {
            throw new InvalidOperationException("Only the host can change a spawned object's network flags.");
        }

        ApplyNetworkFlags(go, flags, entry);
        using NetWriter message = new();
        new NetworkFlagsMessage(go.Identifier, entry.Generation, flags).Write(message);
        BroadcastEngine(EngineMessage.NetworkFlags, message.WrittenSpan);
    }

    internal void OnNetworkFlags(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost || !sender.IsHost)
        {
            return;
        }

        NetworkFlagsMessage message = NetworkFlagsMessage.Read(ref reader);
        Guid id = message.Identifier;
        uint generation = message.Generation;
        NetworkFlags flags = message.Flags;
        if (!NetworkAccessor.ValidFlags(flags) || !reader.IsAtEnd || delivery != Delivery.Reliable)
        {
            throw new InvalidDataException("Invalid network flags message.");
        }

        if (FindByIdentifier(id) is GameObject go && FindEntry(go) is { TearingDown: false } entry &&
            entry.Generation == generation)
        {
            ApplyNetworkFlags(go, flags, entry);
        }
    }

    internal void ApplyNetworkFlags(GameObject go, NetworkFlags flags, NetworkObject entry)
    {
        go.Network.ApplyFlags(flags);
        if (go != entry.GameObject)
        {
            return;
        }

        // Old samples must not restore values from a previously enabled channel.
        entry.TransformSync.Reset();
    }
}
