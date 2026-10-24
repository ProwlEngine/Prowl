// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;

using Prowl.Runtime.Tasks;
using Prowl.Warp;

namespace Prowl.Runtime.Networking;

public static partial class Networking
{
    internal static void SetNetworkFlags(GameObject go, NetworkFlags flags)
    {
        if (!IsActive || FindEntry(go) is not { TearingDown: false } entry)
            return;
        MainThreadContext.AssertMainThread();
        if (!IsHost)
            throw new InvalidOperationException("Only the host can change a spawned object's network flags.");

        ApplyNetworkFlags(go, flags, entry);
        using NetWriter message = new();
        message.WriteGuid(go.Identifier);
        message.WriteUInt32(entry.Generation);
        message.WriteByte((byte)flags);
        BroadcastEngine(EngineMessage.NetworkFlags, message.WrittenSpan);
    }

    private static void OnNetworkFlags(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost || !sender.IsHost) return;
        Guid id = reader.ReadGuid();
        uint generation = reader.ReadUInt32();
        NetworkFlags flags = (NetworkFlags)reader.ReadByte();
        if (!NetworkAccessor.ValidFlags(flags) || !reader.IsAtEnd || delivery != Delivery.Reliable)
            throw new InvalidDataException("Invalid network flags message.");

        if (FindByIdentifier(id) is GameObject go && FindEntry(go) is { TearingDown: false } entry &&
            entry.Generation == generation)
            ApplyNetworkFlags(go, flags, entry);
    }

    private static void ApplyNetworkFlags(GameObject go, NetworkFlags flags, NetworkEntry entry)
    {
        go.Network.ApplyFlags(flags);
        if (go != entry.GameObject) return;
        // Old samples must not restore values from a previously enabled channel.
        entry.TransformSync.Reset();
    }
}
