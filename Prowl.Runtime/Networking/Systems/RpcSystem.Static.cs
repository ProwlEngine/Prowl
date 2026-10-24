// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;

using Prowl.Runtime.Networking.Attributes;
using Prowl.Runtime.Networking.Diagnostics;
using Prowl.Runtime.Networking.Enums;
using Prowl.Runtime.Networking.Messages;
using Prowl.Runtime.Networking.Utilities;
using Prowl.Runtime.Tasks;
using Prowl.Warp;

namespace Prowl.Runtime.Networking.Systems;

internal sealed partial class RpcSystem
{
    internal StaticRpcMember? _incomingStatic;

    internal bool BeginStaticRpc(StaticRpcMember rpc, out NetWriter writer)
    {
        writer = null!;
        if (_incomingStatic == rpc || !scene.IsActive)
        {
            return false;
        }

        MainThreadContext.AssertMainThread();
        if (rpc.Mode == RpcMode.Host && scene.IsHost && !Rpc.HasFilters)
        {
            return false;
        }

        writer = new NetWriter();
        return true;
    }

    internal bool SendStaticRpc(StaticRpcMember rpc, NetWriter arguments)
    {
        if (!scene.IsHost && (!scene.IsReady || (rpc.Flags & RpcFlags.HostOnly) != 0))
        {
            return false;
        }

        NetSession session = scene.Session!;
        RpcRecipients? recipients = Rpc.CaptureRecipients(session);
        bool localIncluded = RpcRecipients.Includes(recipients, session.LocalPeer!);
        if (recipients != null && recipients.Ids.Count == (localIncluded ? 1 : 0) && rpc.Mode == RpcMode.Broadcast)
        {
            return localIncluded;
        }

        if (rpc.Mode == RpcMode.Host && scene.IsHost)
        {
            return localIncluded;
        }

        if (rpc.Mode == RpcMode.Host && !RpcRecipients.Includes(recipients, session.HostPeer!))
        {
            return false;
        }

        using NetWriter message = new();
        if (scene.IsHost)
        {
            new RpcCallerMessage(scene.Session!.LocalPeer!.Id).Write(message);
        }

        WriteStaticCall(message, rpc, arguments.WrittenSpan);
        Delivery delivery = StaticRpcDelivery(rpc);
        if (scene.IsHost)
        {
            SendRpcRecipients(EngineMessage.StaticRpcFromHost, message.WrittenSpan, delivery, recipients);
        }
        else
        {
            new RpcRecipientMessage(recipients).Write(message);
            scene.SendEngine(session.HostPeer!, EngineMessage.StaticRpcToHost, message.WrittenSpan, delivery);
        }

        return rpc.Mode == RpcMode.Broadcast && localIncluded;
    }

    // Calls carry [declaring type + assembly simple name][method id][length-prefixed arguments].
    // The type selects generated descriptors, never arbitrary methods. A host relay prefixes the original caller.
    internal void WriteStaticCall(NetWriter output, StaticRpcMember rpc, ReadOnlySpan<byte> arguments) =>
        new StaticRpcMessage(rpc.TypeName, rpc.Id, arguments).Write(output);

    internal StaticRpcMember? ReadStaticCall(ref NetReader reader, out ReadOnlySpan<byte> arguments)
    {
        StaticRpcMessage message = StaticRpcMessage.Read(ref reader);
        arguments = message.Arguments;
        return StaticRpcMember.Find(message.TypeName, message.MethodId);
    }

    internal Delivery StaticRpcDelivery(StaticRpcMember rpc) =>
        (rpc.Flags & RpcFlags.Unreliable) != 0 ? Delivery.Unreliable : Delivery.Reliable;

    internal void OnStaticRpcToHost(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (!scene.IsHost)
        {
            return;
        }

        StaticRpcMember? rpc = ReadStaticCall(ref reader, out ReadOnlySpan<byte> arguments);
        RpcRecipients? recipients = RpcRecipientMessage.Read(ref reader, scene.Session!.Options.MaxPeers).Recipients;
        if (rpc == null || (rpc.Flags & RpcFlags.HostOnly) != 0)
        {
            return;
        }

        rpc.ValidateArguments(arguments);

        if (rpc.Mode == RpcMode.Broadcast)
        {
            using NetWriter relay = new();
            new RpcCallerMessage(sender.Id).Write(relay);
            WriteStaticCall(relay, rpc, arguments);
            SendRpcRecipients(EngineMessage.StaticRpcFromHost, relay.WrittenSpan, StaticRpcDelivery(rpc), recipients,
                sender);
        }

        if (RpcRecipients.Includes(recipients, scene.Session!.LocalPeer!))
        {
            InvokeStatic(rpc, arguments, sender);
        }
    }

    internal void OnStaticRpcFromHost(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (scene.IsHost || !sender.IsHost)
        {
            return;
        }

        int callerId = RpcCallerMessage.Read(ref reader).CallerId;
        StaticRpcMember? rpc = ReadStaticCall(ref reader, out ReadOnlySpan<byte> arguments);
        if (!reader.IsAtEnd)
        {
            throw new InvalidDataException("Static RPC contains trailing bytes.");
        }

        if (rpc == null || rpc.Mode != RpcMode.Broadcast || !scene.Session!.TryGetPeer(callerId, out NetPeer? caller) ||
            ((rpc.Flags & RpcFlags.HostOnly) != 0 && !caller!.IsHost))
        {
            return;
        }

        rpc.ValidateArguments(arguments);
        InvokeStatic(rpc, arguments, caller!);
    }

    internal void InvokeStatic(StaticRpcMember rpc, ReadOnlySpan<byte> arguments, NetPeer caller)
    {
        StaticRpcMember? previousIncoming = _incomingStatic;
        bool previousRead = _incomingRead;
        NetPeer? previousCaller = CurrentCaller;
        _incomingStatic = rpc;
        _incomingRead = false;
        CurrentCaller = caller;
        NetworkDiagnostics.Record("StaticRPC", "Invoke", caller, Guid.Empty, $"{rpc.TypeName}.{rpc.Signature}");
        try
        {
            NetReader reader = new(arguments);
            rpc.Invoke(ref reader);
        }
        catch (Exception e) when (_incomingRead)
        {
            Debug.LogError($"[Network] Static RPC {rpc.DeclaringType.FullName}.{rpc.Signature} threw: {e}");
        }
        catch (Exception e) when (e is not (EndOfStreamException or InvalidDataException))
        {
            throw new InvalidDataException($"Static RPC {rpc.Signature} arguments could not be read: {e.Message}", e);
        }
        finally
        {
            _incomingStatic = previousIncoming;
            _incomingRead = previousRead;
            CurrentCaller = previousCaller;
        }
    }
}
