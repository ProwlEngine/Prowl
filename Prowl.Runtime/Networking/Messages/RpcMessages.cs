// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Courier;
using Prowl.Ember;

namespace Prowl.Runtime.Networking;

/// <summary>
///     A call names its object by NetId, its component by its index among the object's networked components and its
///     method by its index among the type's RPCs. The same both ways, since the sender is the caller.
/// </summary>
internal readonly ref struct RpcCallMessage(uint netId, byte component, uint method, ReadOnlySpan<byte> arguments)
{
    internal readonly uint NetId = netId;
    internal readonly byte Component = component;
    internal readonly uint Method = method;
    [ReloadIgnore] internal readonly ReadOnlySpan<byte> Arguments = arguments;

    internal void Write(NetWriter writer)
    {
        WriteTarget(writer, NetId, Component, Method);
        writer.WriteBytesWithLength(Arguments);
    }

    internal static void WriteTarget(NetWriter writer, uint netId, byte component, uint method)
    {
        writer.WriteVarUInt32(netId);
        writer.WriteByte(component);
        writer.WriteVarUInt32(method);
    }

    internal static RpcCallMessage ReadUnframed(ref NetReader reader) =>
        new(reader.ReadVarUInt32(), reader.ReadByte(), reader.ReadVarUInt32(), reader.ReadBytes(reader.Remaining));

    internal static RpcCallMessage Read(ref NetReader reader) => new(
        reader.ReadVarUInt32(),
        reader.ReadByte(),
        reader.ReadVarUInt32(),
        reader.ReadBytesWithLength());
}
