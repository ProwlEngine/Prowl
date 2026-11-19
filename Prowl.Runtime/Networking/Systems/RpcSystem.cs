// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Runtime.Diagnostics;
using Prowl.Courier;

using static Prowl.Runtime.Networking.NetworkSystem;

namespace Prowl.Runtime.Networking;

// RPCs. A call is [guid component][u32 method id][length-prefixed arguments]. As Mirror's commands and client RPCs,
// a client only calls host RPCs: it sends the call to the host, which checks it against the method's own RpcMode
// and RpcFlags (never trusting the caller about them) and runs it. Broadcast, owner and target RPCs are the host's
// to call, and carry its id in front.
//
// A call carries no state. A reliable one sends the caller's pending Sync changes first, on the same ordered
// channel, so it arrives after them. An unreliable one promises no ordering with anything.
internal sealed partial class RpcSystem(NetworkSystem scene) : IDisposable
{
    internal readonly HashSet<string> _rpcWarnings = new();
    internal readonly NetWriter _rpcWriter = new();

    // Where the arguments of a call made here, or relayed, name engine objects.
    private readonly ReferenceSites _sites = new(), _relaySites = new();

    // The call whose body is running, received or made here, so its prologue runs the body instead of sending it
    // again. It holds for the whole body, so an override calling base.Method() runs the base body here too.
    internal (Component Component, uint Id)? _incoming;

    // Set once the incoming call's arguments are read: from then on an exception is the body's, not the sender's.
    internal bool _incomingRead;

    /// <summary>The peer whose RPC is running, while one that came over the network runs.</summary>
    internal NetPeer? CurrentCaller { get; private set; }

    public void Dispose()
    {
        _rpcWriter.Dispose();
        _staticWriter.Dispose();
        _sites.Dispose();
        _relaySites.Dispose();
        _staticSites.Dispose();
    }

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

        if (mode == RpcMode.Owner && scene.IsHost && scene.HasControl(component.GameObject) && !Rpc.HasFilters)
        {
            return false;
        }

        using FrameStatScope stats = FrameStats.Network.Scope();
        _rpcWriter.Reset();
        RpcCallMessage.WriteTarget(_rpcWriter, component.Identifier, rpc.Id);
        writer = _rpcWriter;
        RecordReferences(_sites, writer, writer.Length);
        return true;
    }

    // Host: notes where the arguments about to be written name engine objects.
    private void RecordReferences(ReferenceSites sites, NetWriter writer, int start)
    {
        sites.Begin(writer, start);
        NetSerializers.WrittenSites = scene.IsHost ? sites : null;
    }

    internal bool SendRpc(Component component, RpcMember rpc, NetWriter writer, NetPeer? target = null)
    {
        using FrameStatScope stats = FrameStats.Network.Scope();
        ReferenceSites? sites = ReferenceSites.Finish(writer);
        NetSession session = scene.Session!;
        if (!scene.IsHost && !scene.IsReady)
        {
            return false;
        }

        (uint id, RpcMode mode, RpcFlags flags) = (rpc.Id, rpc.Mode, rpc.Flags);
        Delivery delivery = (flags & RpcFlags.Unreliable) != 0 ? Delivery.Unreliable : Delivery.Reliable;

        if (!scene.IsHost && (mode != RpcMode.Host || (flags & RpcFlags.HostOnly) != 0))
        {
            WarnOnce(component, id, "can only be called by the host");
            return false;
        }

        if (!scene.IsHost && (flags & RpcFlags.AnyCaller) == 0 && !scene.HasControl(component.GameObject))
        {
            WarnOnce(component, id, "can only be called by whoever controls the object, unless it takes RpcFlags.AnyCaller");
            return false;
        }

        if (mode == RpcMode.Target && (target == null || target.IsLocal))
        {
            return true;
        }

        RpcRecipients? recipients = WithoutOwner(Rpc.CaptureRecipients(session), component.GameObject, flags);
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

        // Only channel 0 keeps the call behind the Sync changes made before it.
        if (delivery == Delivery.Reliable && rpc.Channel == 0)
        {
            scene.Sync.SendSyncChanges();
        }

        using NetWriter envelope = new(writer.Length + 32);
        NetReader raw = new(writer.WrittenSpan);
        RpcCallMessage.ReadUnframed(ref raw).Write(envelope);

        if (!scene.IsHost)
        {
            new RpcRecipientMessage(recipients).Write(envelope);
            scene.SendEngine(session.HostPeer!, EngineMessage.RpcToHost, envelope.WrittenSpan, delivery,
                rpc.Channel);
            if (NetworkDiagnostics.Enabled)
            {
                NetworkDiagnostics.TraceMember("RPC", "Send", session.HostPeer, component, id, true, $"{mode}, {delivery}",
                    writer.WrittenSpan);
            }

            return false;
        }

        using NetWriter message = new(writer.Length + 8);
        new RpcCallerMessage(session.LocalPeer!.Id).Write(message);
        message.WriteBytes(envelope.WrittenSpan);
        if (NetworkDiagnostics.Enabled)
        {
            NetworkDiagnostics.TraceMember("RPC", "Send", null, component, id, true, $"{mode}, {delivery}",
                writer.WrittenSpan);
        }

        // The arguments end the message.
        int arguments = message.Length - (writer.Length - (sites?.Start ?? 0));
        switch (mode)
        {
            case RpcMode.Broadcast:
                SendRpcRecipients(EngineMessage.RpcFromHost, message.WrittenSpan, delivery, recipients,
                    channel: rpc.Channel, target: component.GameObject, sites: sites, arguments: arguments);
                return localIncluded;

            case RpcMode.Owner:
                // The prologue only sends when another peer controls it.
                if (scene.GetOwner(component.GameObject) is { } owner && RpcRecipients.Includes(recipients, owner))
                {
                    scene.SendEngine(owner, EngineMessage.RpcFromHost,
                        Masked(sites, owner, null, message.WrittenSpan, arguments), delivery, rpc.Channel);
                }

                return false;

            case RpcMode.Target:
                if (scene.Sees(component.GameObject, target!))
                {
                    scene.SendEngine(target!, EngineMessage.RpcFromHost,
                        Masked(sites, target!, null, message.WrittenSpan, arguments), delivery, rpc.Channel);
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

        if (!scene.Sees(component.GameObject, sender))
        {
            NetworkDiagnostics.Record("RPC", "Rejected", sender, id, $"Object not sent to the caller, method #{method}",
                arguments);
            return;
        }

        (RpcMode mode, RpcFlags flags) = (rpc.Mode, rpc.Flags);
        bool senderControls = scene.OwnerOf(component.GameObject) == sender.Id;
        if ((flags & RpcFlags.HostOnly) != 0 || mode != RpcMode.Host ||
            ((flags & RpcFlags.AnyCaller) == 0 && !senderControls))
        {
            NetworkDiagnostics.TraceMember("RPC", "Rejected", sender, component, method, true, "Unauthorized caller",
                arguments);
            return;
        }

        if (NetworkDiagnostics.Enabled)
        {
            NetworkDiagnostics.TraceMember("RPC", "Receive", sender, component, method, true, $"{mode}, {delivery}",
                arguments);
        }

        NetReader validation = new(arguments);
        rpc.ValidateArguments(component, ref validation);
        if (RpcRecipients.Includes(recipients, scene.Session!.LocalPeer!))
        {
            Invoke(component, rpc, arguments, sender);
        }
    }

    /// <summary>The recipients, less the object's owner when the RPC takes <see cref="RpcFlags.ExcludeOwner" />.</summary>
    private RpcRecipients? WithoutOwner(RpcRecipients? recipients, GameObject go, RpcFlags flags)
    {
        if ((flags & RpcFlags.ExcludeOwner) == 0 || scene.GetOwner(go) is not { } owner)
        {
            return recipients;
        }

        HashSet<int> ids = new();
        if (recipients != null)
        {
            ids.UnionWith(recipients.Ids);
        }
        else
        {
            foreach (NetPeer peer in scene.Session!.Peers)
            {
                ids.Add(peer.Id);
            }
        }
        ids.Remove(owner.Id);
        return new RpcRecipients(ids);
    }

    // The call as peer may receive it, its references to objects it lacks made empty.
    private ReadOnlySpan<byte> Masked(ReferenceSites? sites, NetPeer peer, NetPeer? caller, ReadOnlySpan<byte> payload,
        int arguments) =>
        sites == null ? payload : sites.For(scene, peer, caller, payload, arguments);

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
        using NetSerializers.ReadingFrom references = new(scene.RemoteClient(caller));
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

    internal RpcBodyScope EnterBody(Component component, uint id)
    {
        RpcBodyScope scope = new(this, _incoming);
        _incoming = (component, id);
        return scope;
    }

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
        RpcRecipients? recipients, NetPeer? except = null, byte channel = 0, GameObject? target = null,
        ReferenceSites? sites = null, int arguments = 0, NetPeer? caller = null)
    {
        foreach (NetPeer peer in scene.Session!.Peers)
        {
            if (!peer.IsLocal && peer != except && RpcRecipients.Includes(recipients, peer) &&
                (target.IsNotValid() || scene.Sees(target, peer)))
            {
                scene.SendEngine(peer, kind, Masked(sites, peer, caller, payload, arguments), delivery, channel);
            }
        }
    }
}
