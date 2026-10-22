// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;

using Prowl.Runtime.Tasks;
using Prowl.Vector;
using Prowl.Warp;

namespace Prowl.Runtime.Networking;

public static partial class Networking
{
    internal static void SetOwnerTransfer(GameObject go, OwnerTransfer policy)
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
        message.WriteGuid(go.Identifier);
        message.WriteUInt32(entry.Generation);
        message.WriteByte((byte)policy);
        BroadcastEngine(EngineMessage.OwnerTransfer, message.WrittenSpan);
    }

    private static void OnOwnerTransfer(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost || !sender.IsHost)
        {
            return;
        }

        Guid id = reader.ReadGuid();
        uint generation = reader.ReadUInt32();
        OwnerTransfer policy = (OwnerTransfer)reader.ReadByte();
        if (!Enum.IsDefined(policy) || !reader.IsAtEnd || delivery != Delivery.Reliable)
        {
            throw new InvalidDataException("Invalid ownership transfer policy.");
        }

        if (s_entries.TryGetValue(id, out NetworkEntry? entry) && entry.Generation == generation)
        {
            entry.GameObject.ApplyOwnerTransfer(policy);
        }
    }

    internal static bool TransferOwnership(GameObject go, bool take)
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

        if (!IsHost && (take ? entry.GameObject.OwnerTransfer != OwnerTransfer.Takeover : entry.Owner != local))
        {
            return false;
        }

        if (IsHost)
        {
            ChangeOwner(entry, take ? local : NoOwner);
            return true;
        }

        using NetWriter message = new(), state = new();
        message.WriteGuid(entry.GameObject.Identifier);
        message.WriteUInt32(entry.Generation);
        message.WriteBool(take);
        NetSerializers.WriteFloat3(message, take ? Float3.Zero : OwnershipVelocity(entry));
        if (!take)
        {
            WriteOwnershipFrames(entry.GameObject, state, false);
        }

        message.WriteBytesWithLength(state.WrittenSpan);
        SendEngine(session.HostPeer!, EngineMessage.OwnershipRequest, message.WrittenSpan);
        return true;
    }

    private static void OnOwnershipRequest(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (!IsHost || !IsPeerReady(sender))
        {
            return;
        }

        Guid id = reader.ReadGuid();
        uint generation = reader.ReadUInt32();
        bool take = reader.ReadBool();
        Float3 velocity = NetSerializers.ReadFloat3(ref reader);
        ReadOnlySpan<byte> state = reader.ReadBytesWithLength();
        if (!reader.IsAtEnd || delivery != Delivery.Reliable || !Finite(velocity) || (take && !state.IsEmpty))
        {
            throw new InvalidDataException("Invalid ownership transfer request.");
        }

        if (!s_entries.TryGetValue(id, out NetworkEntry? entry) || entry.TearingDown ||
            entry.Generation != generation ||
            !entry.GameObject.IsValid())
        {
            return;
        }

        if (take)
        {
            if (entry.GameObject.OwnerTransfer != OwnerTransfer.Takeover || entry.Owner == sender.Id)
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

            if (!ProcessRpcStates(entry.GameObject, state, sender, false) ||
                !ProcessRpcStates(entry.GameObject, state, sender, true, true))
            {
                return;
            }

            entry.TransferVelocity = velocity;
        }

        ChangeOwner(entry, take ? sender.Id : NoOwner);
    }

    private static Float3 OwnershipVelocity(NetworkEntry entry) => HasControl(entry.GameObject) &&
                                                                   entry.GameObject.GetComponent<Rigidbody3D>() is
                                                                       { } body
        ? body.LinearVelocity
        : SampledVelocity(entry);

    private static void WriteOwnershipFrames(GameObject root, NetWriter output, bool includeAll)
    {
        using NetWriter frame = new();
        WriteRpcState(root, frame, includeAll);
        output.WriteVarUInt32(1);
        output.WriteBytesWithLength(frame.WrittenSpan);
    }

    private static void SendOwnershipState(NetworkEntry entry, Float3 velocity)
    {
        // A forced takeover starts from the host's latest accepted state, without interpolation delay.
        if (!HasControl(entry.GameObject) && entry.Samples.Count > 0)
        {
            TransformSample sample = entry.Samples[^1];
            ApplyNetworkTransform(entry.GameObject, sample.Position, sample.Rotation, sample.Scale);
        }

        using NetWriter state = new(), message = new();
        WriteOwnershipFrames(entry.GameObject, state, true);
        message.WriteGuid(entry.GameObject.Identifier);
        message.WriteUInt32(entry.Generation);
        NetSerializers.WriteFloat3(message, velocity);
        message.WriteBytesWithLength(state.WrittenSpan);
        BroadcastEngine(EngineMessage.OwnershipState, message.WrittenSpan);
    }

    private static void OnOwnershipState(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost || !sender.IsHost)
        {
            return;
        }

        Guid id = reader.ReadGuid();
        uint generation = reader.ReadUInt32();
        Float3 velocity = NetSerializers.ReadFloat3(ref reader);
        ReadOnlySpan<byte> state = reader.ReadBytesWithLength();
        if (!reader.IsAtEnd || delivery != Delivery.Reliable || !Finite(velocity))
        {
            throw new InvalidDataException("Invalid ownership handoff state.");
        }

        if (!s_entries.TryGetValue(id, out NetworkEntry? entry) || entry.Generation != generation)
        {
            return;
        }

        if (ProcessRpcStates(entry.GameObject, state, sender, false) &&
            ProcessRpcStates(entry.GameObject, state, sender, true, true))
        {
            entry.TransferVelocity = velocity;
        }
    }
}
