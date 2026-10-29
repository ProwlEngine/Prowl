// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Warp;

namespace Prowl.Runtime.Networking;

/// <summary>To host: call then optional RpcRecipientMessage. From host: RpcCallerMessage then call.</summary>
internal readonly ref struct RpcCallMessage(Guid componentId, uint methodId, ReadOnlySpan<byte> arguments)
{
    internal readonly Guid ComponentId = componentId;
    internal readonly uint MethodId = methodId;
    internal readonly ReadOnlySpan<byte> Arguments = arguments;

    internal void Write(NetWriter writer)
    {
        writer.WriteGuid(ComponentId);
        writer.WriteUInt32(MethodId);
        writer.WriteBytesWithLength(Arguments);
    }

    internal static void WriteTarget(NetWriter writer, Guid componentId, uint methodId)
    {
        writer.WriteGuid(componentId);
        writer.WriteUInt32(methodId);
    }

    internal static RpcCallMessage ReadUnframed(ref NetReader reader) =>
        new(reader.ReadGuid(), reader.ReadUInt32(), reader.ReadBytes(reader.Remaining));

    internal static RpcCallMessage Read(ref NetReader reader) => new(
        reader.ReadGuid(),
        reader.ReadUInt32(),
        reader.ReadBytesWithLength());
}

/// <summary>Same routing envelope as component RPCs; targets a generated static descriptor.</summary>
internal readonly ref struct StaticRpcMessage(string typeName, uint methodId, ReadOnlySpan<byte> arguments)
{
    internal readonly string TypeName = typeName;
    internal readonly uint MethodId = methodId;
    internal readonly ReadOnlySpan<byte> Arguments = arguments;

    internal void Write(NetWriter writer)
    {
        writer.WriteString(TypeName);
        writer.WriteUInt32(MethodId);
        writer.WriteBytesWithLength(Arguments);
    }

    internal static StaticRpcMessage Read(ref NetReader reader) => new(
        reader.ReadString(1024),
        reader.ReadUInt32(),
        reader.ReadBytesWithLength());
}

internal readonly record struct RpcCallerMessage(int CallerId)
{
    internal void Write(NetWriter writer) => writer.WriteVarUInt32((uint)CallerId);

    internal static RpcCallerMessage Read(ref NetReader reader) => new(
        checked((int)reader.ReadVarUInt32()));
}

/// <summary>Optional RPC-to-host trailer: absence means all eligible recipients, zero count means none.</summary>
internal readonly record struct RpcRecipientMessage(RpcRecipients? Recipients)
{
    internal void Write(NetWriter writer)
    {
        if (Recipients == null)
        {
            return;
        }

        writer.WriteVarUInt32((uint)Recipients.Ids.Count);
        foreach (int id in Recipients.Ids)
        {
            writer.WriteVarUInt32((uint)id);
        }
    }

    internal static RpcRecipientMessage Read(ref NetReader reader, int maxPeers)
    {
        if (reader.IsAtEnd)
        {
            return new RpcRecipientMessage(null);
        }

        uint count = reader.ReadVarUInt32();
        NetworkValidation.RecipientCount(count, maxPeers);

        HashSet<int> ids = new();
        for (uint i = 0; i < count; i++)
        {
            NetworkValidation.AddRecipient(ids, checked((int)reader.ReadVarUInt32()));
        }

        NetworkValidation.RecipientEnd(reader.IsAtEnd);

        return new RpcRecipientMessage(new RpcRecipients(ids));
    }
}
