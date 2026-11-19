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

        Sync.ValidateSyncState(sender, new NetReader(message.Values), false, true);
        entry.ApplyHandoffPoses(poses, sender);
        Snapshots.ApplyHandoffValues(sender, message.Values);
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
        SendToObservers(entry, EngineMessage.NetworkFlags, message.WrittenSpan);
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

    internal void SetSendInterval(GameObject go, int interval)
    {
        if (!IsActive || FindEntry(go) is not { TearingDown: false } entry)
        {
            return;
        }

        MainThreadContext.AssertMainThread();
        if (!IsHost)
        {
            throw new InvalidOperationException("Only the host can change a spawned object's send interval.");
        }

        using NetWriter message = new();
        new SendIntervalMessage(go.Identifier, entry.Generation, (byte)interval).Write(message);
        SendToObservers(entry, EngineMessage.SendInterval, message.WrittenSpan);
    }

    internal void OnSendInterval(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost || !sender.IsHost)
        {
            return;
        }

        SendIntervalMessage message = SendIntervalMessage.Read(ref reader);
        if (message.Interval == 0 || !reader.IsAtEnd || delivery != Delivery.Reliable)
        {
            throw new InvalidDataException("Invalid send interval message.");
        }

        if (FindByIdentifier(message.Identifier) is GameObject go && FindEntry(go) is { TearingDown: false } entry &&
            entry.Generation == message.Generation)
        {
            go.Network.ApplySendInterval(message.Interval);
        }
    }
}
