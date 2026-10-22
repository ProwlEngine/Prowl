// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;

using Prowl.Runtime.Diagnostics;
using Prowl.Runtime.Tasks;
using Prowl.Warp;

namespace Prowl.Runtime.Networking;

public static partial class Networking
{
    private static StaticRpcMember? s_incomingStatic;

    internal static bool BeginStaticRpc(StaticRpcMember rpc, out NetWriter writer)
    {
        writer = null!;
        if (s_incomingStatic == rpc || !IsActive) return false;
        MainThreadContext.AssertMainThread();
        if (rpc.Mode == RpcMode.Host && IsHost) return false;
        writer = new NetWriter();
        return true;
    }

    internal static bool SendStaticRpc(StaticRpcMember rpc, NetWriter arguments)
    {
        if (!IsHost && (!IsReady || (rpc.Flags & RpcFlags.HostOnly) != 0)) return false;

        using NetWriter message = new();
        if (IsHost) message.WriteVarUInt32((uint)Session!.LocalPeer!.Id);
        WriteStaticCall(message, rpc, arguments.WrittenSpan);
        Delivery delivery = StaticRpcDelivery(rpc);
        if (IsHost)
            BroadcastEngine(EngineMessage.StaticRpcFromHost, message.WrittenSpan, delivery);
        else
            SendEngine(Session!.HostPeer!, EngineMessage.StaticRpcToHost, message.WrittenSpan, delivery);
        return rpc.Mode == RpcMode.Broadcast;
    }

    // Calls carry [declaring type + assembly simple name][method id][length-prefixed arguments].
    // The type selects generated descriptors, never arbitrary methods. A host relay prefixes the original caller.
    private static void WriteStaticCall(NetWriter output, StaticRpcMember rpc, ReadOnlySpan<byte> arguments)
    {
        output.WriteString(rpc.TypeName);
        output.WriteUInt32(rpc.Id);
        output.WriteBytesWithLength(arguments);
    }

    private static StaticRpcMember? ReadStaticCall(ref NetReader reader, out ReadOnlySpan<byte> arguments)
    {
        string typeName = reader.ReadString(1024);
        uint method = reader.ReadUInt32();
        arguments = reader.ReadBytesWithLength();
        if (!reader.IsAtEnd) throw new InvalidDataException("Static RPC contains trailing bytes.");
        return StaticRpcMember.Find(typeName, method);
    }

    private static Delivery StaticRpcDelivery(StaticRpcMember rpc) =>
        (rpc.Flags & RpcFlags.Unreliable) != 0 ? Delivery.Unreliable : Delivery.Reliable;

    private static void OnStaticRpcToHost(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (!IsHost) return;
        StaticRpcMember? rpc = ReadStaticCall(ref reader, out ReadOnlySpan<byte> arguments);
        if (rpc == null || (rpc.Flags & RpcFlags.HostOnly) != 0) return;
        rpc.ValidateArguments(arguments);

        if (rpc.Mode == RpcMode.Broadcast)
        {
            using NetWriter relay = new();
            relay.WriteVarUInt32((uint)sender.Id);
            WriteStaticCall(relay, rpc, arguments);
            BroadcastEngine(EngineMessage.StaticRpcFromHost, relay.WrittenSpan, StaticRpcDelivery(rpc), sender);
        }
        InvokeStatic(rpc, arguments, sender);
    }

    private static void OnStaticRpcFromHost(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost || !sender.IsHost) return;
        int callerId = checked((int)reader.ReadVarUInt32());
        StaticRpcMember? rpc = ReadStaticCall(ref reader, out ReadOnlySpan<byte> arguments);
        if (rpc == null || rpc.Mode != RpcMode.Broadcast || !Session!.TryGetPeer(callerId, out NetPeer? caller) ||
            ((rpc.Flags & RpcFlags.HostOnly) != 0 && !caller!.IsHost))
            return;
        rpc.ValidateArguments(arguments);
        InvokeStatic(rpc, arguments, caller!);
    }

    private static void InvokeStatic(StaticRpcMember rpc, ReadOnlySpan<byte> arguments, NetPeer caller)
    {
        StaticRpcMember? previousIncoming = s_incomingStatic;
        bool previousRead = s_incomingRead;
        NetPeer? previousCaller = CurrentCaller;
        s_incomingStatic = rpc;
        s_incomingRead = false;
        CurrentCaller = caller;
        NetworkDiagnostics.Record("StaticRPC", "Invoke", caller, Guid.Empty, $"{rpc.TypeName}.{rpc.Signature}");
        try
        {
            NetReader reader = new(arguments);
            rpc.Invoke(ref reader);
        }
        catch (Exception e) when (s_incomingRead)
        {
            Debug.LogError($"[Network] Static RPC {rpc.DeclaringType.FullName}.{rpc.Signature} threw: {e}");
        }
        catch (Exception e) when (e is not (EndOfStreamException or InvalidDataException))
        {
            throw new InvalidDataException($"Static RPC {rpc.Signature} arguments could not be read: {e.Message}", e);
        }
        finally
        {
            s_incomingStatic = previousIncoming;
            s_incomingRead = previousRead;
            CurrentCaller = previousCaller;
        }
    }
}
