// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Runtime.Diagnostics;
using Prowl.Warp;

namespace Prowl.Runtime;

// RPCs. A call is [guid component][u32 method id][arguments]. Clients send theirs to the host, which
// checks it against the method's own RpcMode and RpcFlags (never trusting the caller about them), runs
// it if it should and passes it on; calls from the host carry the original caller's id in front.
public static partial class Networking
{
    private static readonly NetWriter s_rpcWriter = new();
    private static readonly HashSet<string> s_rpcWarnings = new();

    // The incoming call being dispatched, so its prologue lets the body run instead of sending it again. It holds
    // for the whole call, so an override calling base.Method() runs the base body here too.
    private static (Component Component, uint Id)? s_incoming;

    // Set once the incoming call's arguments are read: from then on an exception is the body's, not the sender's.
    private static bool s_incomingRead;

    /// <summary>The peer whose RPC is running, while one that came over the network runs.</summary>
    internal static NetPeer? CurrentCaller { get; private set; }

    internal static bool BeginRpc(Component component, RpcMember rpc, out NetWriter writer)
    {
        writer = null!;
        RpcMode mode = rpc.Mode;

        if (s_incoming is { } incoming && incoming.Component == component && incoming.Id == rpc.Id)
            return false;

        if (!IsActive || component.GameObject.IsNetworkExcluded)
            return false;
        if (mode == RpcMode.Host && IsHost)
            return false;
        if (mode == RpcMode.Owner && HasControl(component.GameObject))
            return false;

        using FrameStatScope stats = FrameStats.Network.Scope();
        s_rpcWriter.Reset();
        s_rpcWriter.WriteGuid(component.Identifier);
        s_rpcWriter.WriteUInt32(rpc.Id);
        writer = s_rpcWriter;
        return true;
    }

    internal static bool SendRpc(Component component, RpcMember rpc, NetWriter writer)
    {
        using FrameStatScope stats = FrameStats.Network.Scope();
        NetSession session = Session!;
        (uint id, RpcMode mode, RpcFlags flags) = (rpc.Id, rpc.Mode, rpc.Flags);
        Delivery delivery = (flags & RpcFlags.Unreliable) != 0 ? Delivery.Unreliable : Delivery.Reliable;

        if ((flags & RpcFlags.HostOnly) != 0 && !IsHost)
        {
            WarnOnce(component, id, "can only be called by the host");
            return false;
        }
        if ((flags & RpcFlags.OwnerOnly) != 0 && !HasControl(component.GameObject))
        {
            WarnOnce(component, id, "can only be called by whoever controls the object");
            return false;
        }

        if (!IsHost)
        {
            SendEngine(session.HostPeer!, EngineMessage.RpcToHost, writer.WrittenSpan, delivery);
            TraceMember("RPC", "Send", session.HostPeer, component, id, true, $"{mode}, {delivery}", writer.WrittenSpan);
            return mode == RpcMode.Broadcast;
        }

        using NetWriter message = new(writer.Length + 8);
        message.WriteVarUInt32((uint)session.LocalPeer!.Id);
        message.WriteBytes(writer.WrittenSpan);
        TraceMember("RPC", "Send", null, component, id, true, $"{mode}, {delivery}", writer.WrittenSpan);

        switch (mode)
        {
            case RpcMode.Broadcast:
                BroadcastEngine(EngineMessage.RpcFromHost, message.WrittenSpan, delivery);
                return true;

            case RpcMode.Owner:
                // The prologue only sends when another peer controls it.
                if (GetOwner(component.GameObject) is { } owner)
                    SendEngine(owner, EngineMessage.RpcFromHost, message.WrittenSpan, delivery);
                return false;

            default:
                return true;
        }
    }

    private static void OnRpcToHost(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (!IsHost)
            return;

        Guid id = reader.ReadGuid();
        uint method = reader.ReadUInt32();
        ReadOnlySpan<byte> arguments = reader.ReadBytes(reader.Remaining);

        if (FindByIdentifier(id) is not Component component || component.GameObject.IsNetworkExcluded
            || NetworkMembers.Of(component).Rpc(method) is not { } rpc)
        {
            NetworkDiagnostics.Record("RPC", "Rejected", sender, id, $"Unknown or excluded component/method #{method}", arguments);
            return;
        }

        (RpcMode mode, RpcFlags flags) = (rpc.Mode, rpc.Flags);
        bool senderControls = OwnerOf(component.GameObject) == sender.Id;
        if ((flags & RpcFlags.HostOnly) != 0 || ((flags & RpcFlags.OwnerOnly) != 0 && !senderControls))
        {
            TraceMember("RPC", "Rejected", sender, component, method, true, "Unauthorized caller", arguments);
            return;
        }
        TraceMember("RPC", "Receive", sender, component, method, true, $"{mode}, {delivery}", arguments);

        Delivery relayDelivery = (flags & RpcFlags.Unreliable) != 0 ? Delivery.Unreliable : Delivery.Reliable;
        switch (mode)
        {
            case RpcMode.Broadcast:
                Relay(sender, id, method, arguments, relayDelivery, to: null);
                Invoke(component, rpc, arguments, sender);
                break;

            case RpcMode.Host:
                Invoke(component, rpc, arguments, sender);
                break;

            case RpcMode.Owner:
                int owner = OwnerOf(component.GameObject);
                if (owner == NoOwner)
                    Invoke(component, rpc, arguments, sender);
                else if (owner != sender.Id && Session!.TryGetPeer(owner, out NetPeer? ownerPeer))
                    Relay(sender, id, method, arguments, relayDelivery, to: ownerPeer);
                break;
        }
    }

    private static void Relay(NetPeer caller, Guid id, uint method, ReadOnlySpan<byte> arguments, Delivery delivery, NetPeer? to)
    {
        using NetWriter message = new(arguments.Length + 32);
        message.WriteVarUInt32((uint)caller.Id);
        message.WriteGuid(id);
        message.WriteUInt32(method);
        message.WriteBytes(arguments);

        if (to != null)
            SendEngine(to, EngineMessage.RpcFromHost, message.WrittenSpan, delivery);
        else
            BroadcastEngine(EngineMessage.RpcFromHost, message.WrittenSpan, delivery, except: caller);
    }

    private static void OnRpcFromHost(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost)
            return;

        int callerId = (int)reader.ReadVarUInt32();
        Guid id = reader.ReadGuid();
        uint method = reader.ReadUInt32();
        ReadOnlySpan<byte> arguments = reader.ReadBytes(reader.Remaining);

        if (FindByIdentifier(id) is not Component component || NetworkMembers.Of(component).Rpc(method) is not { } rpc)
        {
            NetworkDiagnostics.Record("RPC", "Rejected", sender, id, $"Unknown component/method #{method}", arguments);
            return;
        }

        NetPeer caller = Session!.TryGetPeer(callerId, out NetPeer? peer) ? peer : sender;
        Invoke(component, rpc, arguments, caller);
    }

    private static void Invoke(Component component, RpcMember rpc, ReadOnlySpan<byte> arguments, NetPeer caller)
    {
        (Component, uint)? previousIncoming = s_incoming;
        bool previousRead = s_incomingRead;
        NetPeer? previousCaller = CurrentCaller;
        s_incoming = (component, rpc.Id);
        s_incomingRead = false;
        CurrentCaller = caller;
        TraceMember("RPC", "Invoke", caller, component, rpc.Id, true, "Dispatch", arguments);
        try
        {
            NetReader reader = new(arguments);
            rpc.Invoke(component, ref reader);
        }
        catch (Exception e) when (s_incomingRead)
        {
            // The game's own code threw. That's no reason to drop the caller, or to stop the session.
            Debug.LogError($"[Network] RPC {component.GetType().Name}.{rpc.Signature} threw: {e}");
        }
        catch (Exception e) when (e is not (EndOfStreamException or InvalidDataException))
        {
            throw new InvalidDataException($"RPC {component.GetType().Name}.{rpc.Signature} came with arguments that could not be read: {e.Message}", e);
        }
        finally
        {
            s_incoming = previousIncoming;
            s_incomingRead = previousRead;
            CurrentCaller = previousCaller;
        }
    }

    /// <summary>The incoming call's arguments are read and its body is about to run.</summary>
    internal static void RpcArgumentsRead() => s_incomingRead = true;

    private static void WarnOnce(Component component, uint id, string problem)
    {
        TraceMember("RPC", "Rejected", Session?.LocalPeer, component, id, true, problem, default);
        if (s_rpcWarnings.Add($"{component.GetType().FullName}:{id}:{problem}"))
            Debug.LogWarning($"[Network] An RPC on {component.GetType().Name} {problem}, so this call was dropped.");
    }
}
