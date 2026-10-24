// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Runtime.Diagnostics;
using Prowl.Warp;

using static Prowl.Runtime.Networking.NetworkSystem;

namespace Prowl.Runtime.Networking;

// RPCs. A call is [guid component][u32 method id][length-prefixed arguments]. Clients send theirs to the host,
// followed by the recipients they chose, and the host checks it against the method's own RpcMode and RpcFlags
// (never trusting the caller about them), runs it if it should and passes it on; calls from the host carry the
// original caller's id in front.
//
// A call carries no state. A reliable one sends the caller's pending Sync changes first, on the same ordered
// channel, so it arrives after them. An unreliable one promises no ordering with anything.
internal sealed partial class RpcSystem(NetworkSystem scene) : IDisposable
{
    internal readonly HashSet<string> _rpcWarnings = new();
    internal readonly NetWriter _rpcWriter = new();

    // The incoming call being dispatched, so its prologue lets the body run instead of sending it again. It holds
    // for the whole call, so an override calling base.Method() runs the base body here too.
    internal (Component Component, uint Id)? _incoming;

    // Set once the incoming call's arguments are read: from then on an exception is the body's, not the sender's.
    internal bool _incomingRead;

    /// <summary>The peer whose RPC is running, while one that came over the network runs.</summary>
    internal NetPeer? CurrentCaller { get; private set; }

    public void Dispose() => _rpcWriter.Dispose();

    internal bool BeginRpc(Component component, RpcMember rpc, out NetWriter writer)
    {
        writer = null!;
        RpcMode mode = rpc.Mode;

        if (_incoming is { } incoming && incoming.Component == component && incoming.Id == rpc.Id)
        {
            return false;
        }

        if (!scene.IsActive || NetworkAccessor.IsExcludedFromNetwork(component.GameObject))
        {
            return false;
        }

        if (mode == RpcMode.Host && scene.IsHost && !Rpc.HasFilters)
        {
            return false;
        }

        if (mode == RpcMode.Owner && scene.HasControl(component.GameObject) && (scene.IsHost || scene.IsReady) &&
            !Rpc.HasFilters)
        {
            return false;
        }

        using FrameStatScope stats = FrameStats.Network.Scope();
        _rpcWriter.Reset();
        RpcCallMessage.WriteTarget(_rpcWriter, component.Identifier, rpc.Id);
        writer = _rpcWriter;
        return true;
    }

    internal bool SendRpc(Component component, RpcMember rpc, NetWriter writer)
    {
        using FrameStatScope stats = FrameStats.Network.Scope();
        NetSession session = scene.Session!;
        if (!scene.IsHost && !scene.IsReady)
        {
            return false;
        }

        (uint id, RpcMode mode, RpcFlags flags) = (rpc.Id, rpc.Mode, rpc.Flags);
        Delivery delivery = (flags & RpcFlags.Unreliable) != 0 ? Delivery.Unreliable : Delivery.Reliable;

        if ((flags & RpcFlags.HostOnly) != 0 && !scene.IsHost)
        {
            WarnOnce(component, id, "can only be called by the host");
            return false;
        }

        if ((flags & RpcFlags.OwnerOnly) != 0 && !scene.HasControl(component.GameObject))
        {
            WarnOnce(component, id, "can only be called by whoever controls the object");
            return false;
        }

        RpcRecipients? recipients = Rpc.CaptureRecipients(session);
        bool localIncluded = RpcRecipients.Includes(recipients, session.LocalPeer!);
        if (recipients != null && recipients.Ids.Count == (localIncluded ? 1 : 0) && mode == RpcMode.Broadcast)
        {
            return localIncluded;
        }

        if ((mode == RpcMode.Host && scene.IsHost) || (mode == RpcMode.Owner && scene.HasControl(component.GameObject)))
        {
            return localIncluded;
        }

        if (mode == RpcMode.Host && !RpcRecipients.Includes(recipients, session.HostPeer!))
        {
            return false;
        }

        if (mode == RpcMode.Owner &&
            !RpcRecipients.Includes(recipients, scene.GetOwner(component.GameObject) ?? session.HostPeer!))
        {
            return false;
        }

        if (delivery == Delivery.Reliable)
        {
            scene.Sync.SendSyncChanges();
        }

        using NetWriter envelope = new(writer.Length + 32);
        NetReader raw = new(writer.WrittenSpan);
        RpcCallMessage.ReadUnframed(ref raw).Write(envelope);

        if (!scene.IsHost)
        {
            new RpcRecipientMessage(recipients).Write(envelope);
            scene.SendEngine(session.HostPeer!, EngineMessage.RpcToHost, envelope.WrittenSpan, delivery);
            NetworkDiagnostics.TraceMember("RPC", "Send", session.HostPeer, component, id, true, $"{mode}, {delivery}",
                writer.WrittenSpan);
            return mode == RpcMode.Broadcast && localIncluded;
        }

        using NetWriter message = new(writer.Length + 8);
        new RpcCallerMessage(session.LocalPeer!.Id).Write(message);
        message.WriteBytes(envelope.WrittenSpan);
        NetworkDiagnostics.TraceMember("RPC", "Send", null, component, id, true, $"{mode}, {delivery}",
            writer.WrittenSpan);

        switch (mode)
        {
            case RpcMode.Broadcast:
                SendRpcRecipients(EngineMessage.RpcFromHost, message.WrittenSpan, delivery, recipients);
                return localIncluded;

            case RpcMode.Owner:
                // The prologue only sends when another peer controls it.
                if (scene.GetOwner(component.GameObject) is { } owner && RpcRecipients.Includes(recipients, owner))
                {
                    scene.SendEngine(owner, EngineMessage.RpcFromHost, message.WrittenSpan, delivery);
                }

                return false;

            default:
                return true;
        }
    }

    internal void OnRpcToHost(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (!scene.IsHost)
        {
            return;
        }

        RpcCallMessage message = RpcCallMessage.Read(ref reader);
        Guid id = message.ComponentId;
        uint method = message.MethodId;
        ReadOnlySpan<byte> arguments = message.Arguments;
        RpcRecipients? recipients = RpcRecipientMessage.Read(ref reader, scene.Session!.Options.MaxPeers).Recipients;

        if (scene.FindByIdentifier(id) is not Component component || NetworkAccessor.IsExcludedFromNetwork(
                                                                      component.GameObject)
                                                                  || NetworkMembers.Of(component).Rpc(method) is not
                                                                      { } rpc)
        {
            NetworkDiagnostics.Record("RPC", "Rejected", sender, id, $"Unknown or excluded component/method #{method}",
                arguments);
            return;
        }

        (RpcMode mode, RpcFlags flags) = (rpc.Mode, rpc.Flags);
        bool senderControls = scene.OwnerOf(component.GameObject) == sender.Id;
        if ((flags & RpcFlags.HostOnly) != 0 || ((flags & RpcFlags.OwnerOnly) != 0 && !senderControls))
        {
            NetworkDiagnostics.TraceMember("RPC", "Rejected", sender, component, method, true, "Unauthorized caller",
                arguments);
            return;
        }

        NetworkDiagnostics.TraceMember("RPC", "Receive", sender, component, method, true, $"{mode}, {delivery}",
            arguments);

        // Validate on the host even when only another owner will execute the call.
        NetReader validation = new(arguments);
        rpc.ValidateArguments(component, ref validation);

        Delivery relayDelivery = (flags & RpcFlags.Unreliable) != 0 ? Delivery.Unreliable : Delivery.Reliable;
        switch (mode)
        {
            case RpcMode.Broadcast:
                Relay(sender, id, method, arguments, relayDelivery, null, recipients);
                if (RpcRecipients.Includes(recipients, scene.Session!.LocalPeer!))
                {
                    Invoke(component, rpc, arguments, sender);
                }

                break;

            case RpcMode.Host:
                if (RpcRecipients.Includes(recipients, scene.Session!.LocalPeer!))
                {
                    Invoke(component, rpc, arguments, sender);
                }

                break;

            case RpcMode.Owner:
                int owner = scene.OwnerOf(component.GameObject);
                if (owner == NoOwner)
                {
                    if (RpcRecipients.Includes(recipients, scene.Session!.LocalPeer!))
                    {
                        Invoke(component, rpc, arguments, sender);
                    }
                }
                else if (owner != sender.Id && scene.Session!.TryGetPeer(owner, out NetPeer? ownerPeer))
                {
                    Relay(sender, id, method, arguments, relayDelivery, ownerPeer, recipients);
                }

                break;
        }
    }

    internal void Relay(NetPeer caller, Guid id, uint method, ReadOnlySpan<byte> arguments, Delivery delivery,
        NetPeer? to, RpcRecipients? recipients)
    {
        // The caller's Sync changes were relayed as they arrived, ahead of this; send the host's own the same way.
        if (delivery == Delivery.Reliable)
        {
            scene.Sync.SendSyncChanges();
        }

        using NetWriter message = new(arguments.Length + 32);
        new RpcCallerMessage(caller.Id).Write(message);
        new RpcCallMessage(id, method, arguments).Write(message);

        if (to != null)
        {
            if (RpcRecipients.Includes(recipients, to))
            {
                scene.SendEngine(to, EngineMessage.RpcFromHost, message.WrittenSpan, delivery);
            }
        }
        else
        {
            SendRpcRecipients(EngineMessage.RpcFromHost, message.WrittenSpan, delivery, recipients, caller);
        }
    }

    internal void OnRpcFromHost(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (scene.IsHost)
        {
            return;
        }

        int callerId = RpcCallerMessage.Read(ref reader).CallerId;
        RpcCallMessage message = RpcCallMessage.Read(ref reader);
        Guid id = message.ComponentId;
        uint method = message.MethodId;
        ReadOnlySpan<byte> arguments = message.Arguments;
        if (!reader.IsAtEnd)
        {
            throw new InvalidDataException("RPC contains trailing bytes.");
        }

        if (scene.FindByIdentifier(id) is not Component component ||
            NetworkMembers.Of(component).Rpc(method) is not { } rpc)
        {
            NetworkDiagnostics.Record("RPC", "Rejected", sender, id, $"Unknown component/method #{method}", arguments);
            return;
        }

        if (!scene.Session!.TryGetPeer(callerId, out NetPeer? caller))
        {
            return;
        }

        NetReader validation = new(arguments);
        rpc.ValidateArguments(component, ref validation);
        Invoke(component, rpc, arguments, caller);
    }

    internal void Invoke(Component component, RpcMember rpc, ReadOnlySpan<byte> arguments, NetPeer caller)
    {
        (Component, uint)? previousIncoming = _incoming;
        bool previousRead = _incomingRead;
        NetPeer? previousCaller = CurrentCaller;
        _incoming = (component, rpc.Id);
        _incomingRead = false;
        CurrentCaller = caller;
        NetworkDiagnostics.TraceMember("RPC", "Invoke", caller, component, rpc.Id, true, "Dispatch", arguments);
        try
        {
            NetReader reader = new(arguments);
            rpc.Invoke(component, ref reader);
        }
        catch (Exception e) when (_incomingRead)
        {
            // The game's own code threw. That's no reason to drop the caller, or to stop the session.
            Debug.LogError($"[Network] RPC {component.GetType().Name}.{rpc.Signature} threw: {e}");
        }
        catch (Exception e) when (e is not (EndOfStreamException or InvalidDataException))
        {
            throw new InvalidDataException(
                $"RPC {component.GetType().Name}.{rpc.Signature} came with arguments that could not be read: {e.Message}",
                e);
        }
        finally
        {
            _incoming = previousIncoming;
            _incomingRead = previousRead;
            CurrentCaller = previousCaller;
        }
    }

    /// <summary>The incoming call's arguments are read and its body is about to run.</summary>
    internal void RpcArgumentsRead() => _incomingRead = true;

    internal void WarnOnce(Component component, uint id, string problem)
    {
        NetworkDiagnostics.TraceMember("RPC", "Rejected", scene.Session?.LocalPeer, component, id, true, problem,
            default);
        if (_rpcWarnings.Add($"{component.GetType().FullName}:{id}:{problem}"))
        {
            Debug.LogWarning($"[Network] An RPC on {component.GetType().Name} {problem}, so this call was dropped.");
        }
    }

    internal void SendRpcRecipients(EngineMessage kind, ReadOnlySpan<byte> payload, Delivery delivery,
        RpcRecipients? recipients, NetPeer? except = null)
    {
        foreach (NetPeer peer in scene.Session!.Peers)
        {
            if (!peer.IsLocal && peer != except && RpcRecipients.Includes(recipients, peer))
            {
                scene.SendEngine(peer, kind, payload, delivery);
            }
        }
    }
}
