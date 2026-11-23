// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

using Prowl.Runtime.Diagnostics;
using Prowl.Courier;

using static Prowl.Runtime.Networking.NetworkSystem;

namespace Prowl.Runtime.Networking;

// RPCs. A call is [NetId][component index][method index][length-prefixed arguments].
// A client only calls host RPCs: it sends the call to the host, which checks it against the method's own RpcMode
// and RpcFlags (never trusting the caller about them) and runs it. Broadcast, owner and target RPCs are the host's
// to call, and carry its id in front.
//
// A call carries no state. A reliable one sends the caller's pending Sync changes first, on the same ordered
// channel, so it arrives after them. An unreliable one promises no ordering with anything, and can arrive before
// the spawn of the object it names, where it is dropped. A client holds what arrives during a spawn run until the
// run's spawn callbacks have run.
internal sealed partial class RpcSystem(NetworkSystem scene) : IDisposable
{
    internal readonly HashSet<string> _rpcWarnings = new();
    internal readonly NetWriter _rpcWriter = new();

    // The call as it goes on the wire, built after the caller's state went out, so nothing runs while they are in use.
    private readonly NetWriter _envelope = new(256), _fromHost = new(256);

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
        _envelope.Dispose();
        _fromHost.Dispose();
        _staticWriter.Dispose();
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
        NetworkObject? entry = scene.FindEntry(component.GameObject);
        int index = entry?.IndexOf(component) ?? -1;
        RpcCallMessage.WriteTarget(_rpcWriter, index < 0 ? 0 : entry!.NetId, (byte)Math.Max(index, 0),
            (uint)NetworkMembers.Of(component).IndexOf(rpc));
        writer = _rpcWriter;
        return true;
    }

    internal bool SendRpc(Component component, RpcMember rpc, NetWriter writer, NetPeer? target = null)
    {
        using FrameStatScope stats = FrameStats.Network.Scope();
        NetSession session = scene.Session!;
        (uint id, RpcMode mode, RpcFlags flags) = (rpc.Id, rpc.Mode, rpc.Flags);
        if (!scene.IsHost && !scene.IsReady)
        {
            WarnOnce(component, id, "was called before this client finished joining. Call it from OnNetworkReady or later");
            return false;
        }

        Delivery delivery = (flags & RpcFlags.Unreliable) != 0 ? Delivery.Unreliable : Delivery.Reliable;

        if (scene.FindEntry(component.GameObject) is not { } networked || networked.IndexOf(component) < 0)
        {
            WarnOnce(component, id, "is on an object that is not networked, or was added to it after it spawned");
            return false;
        }

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

        if (mode == RpcMode.Target && target == null)
        {
            throw new ArgumentNullException(nameof(target), "A target RPC needs the peer to run on.");
        }

        RpcRecipients? recipients = WithoutOwner(Rpc.CaptureRecipients(session), component.GameObject, flags);
        bool localIncluded = RpcRecipients.Includes(recipients, session.LocalPeer!);
        if (mode == RpcMode.Target && (target!.IsLocal || !RpcRecipients.Includes(recipients, target)))
        {
            return target.IsLocal && localIncluded;
        }

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

        // The call is copied out of the shared argument writer before anything else runs: sending state below can run
        // game code that calls another RPC, which writes its own arguments there. Such a call inside this one takes
        // writers of its own.
        bool nested = _sending;
        NetWriter envelope = nested ? new NetWriter(writer.Length + 32) : _envelope;
        envelope.Reset();
        NetReader raw = new(writer.WrittenSpan);
        RpcCallMessage.ReadUnframed(ref raw).Write(envelope);
        _sending = true;
        try
        {
            return SendEnvelope(component, rpc, envelope, nested, networked, target, recipients, localIncluded, delivery);
        }
        finally
        {
            _sending = nested;
            if (nested)
            {
                envelope.Dispose();
            }
        }
    }

    // True while a call is being sent, so one made from inside it knows.
    private bool _sending;

    private bool SendEnvelope(Component component, RpcMember rpc, NetWriter envelope, bool nested, NetworkObject networked,
        NetPeer? target, RpcRecipients? recipients, bool localIncluded, Delivery delivery)
    {
        NetSession session = scene.Session!;
        (uint id, RpcMode mode) = (rpc.Id, rpc.Mode);

        // Only channel 0 keeps the call behind the Sync changes made before it, its own object's whatever their interval.
        if (delivery == Delivery.Reliable && rpc.Channel == 0)
        {
            scene.Sync.SendSyncChanges(networked);
        }

        if (!scene.IsHost)
        {
            new RpcRecipientMessage(recipients).Write(envelope);
            scene.SendEngine(session.HostPeer!, EngineMessage.RpcToHost, envelope.WrittenSpan, delivery,
                rpc.Channel);
            if (NetworkDiagnostics.Enabled)
            {
                NetworkDiagnostics.TraceMember("RPC", "Send", session.HostPeer, component, id, true, $"{mode}, {delivery}",
                    envelope.WrittenSpan);
            }

            return false;
        }

        using NetWriter? own = nested ? new NetWriter(envelope.Length + 8) : null;
        NetWriter message = own ?? _fromHost;
        message.Reset();
        new RpcCallerMessage(session.LocalPeer!.Id).Write(message);
        message.WriteBytes(envelope.WrittenSpan);
        if (NetworkDiagnostics.Enabled)
        {
            NetworkDiagnostics.TraceMember("RPC", "Send", null, component, id, true, $"{mode}, {delivery}",
                envelope.WrittenSpan);
        }

        switch (mode)
        {
            case RpcMode.Broadcast:
                SendRpcRecipients(EngineMessage.RpcFromHost, message.WrittenSpan, delivery, recipients,
                    channel: rpc.Channel, target: component.GameObject);
                return localIncluded;

            case RpcMode.Owner:
                // The prologue only sends when another peer controls it.
                if (scene.GetOwner(component.GameObject) is { } owner && RpcRecipients.Includes(recipients, owner))
                {
                    scene.SendEngine(owner, EngineMessage.RpcFromHost, message.WrittenSpan, delivery, rpc.Channel);
                }

                return false;

            case RpcMode.Target:
                if (scene.Sees(component.GameObject, target!))
                {
                    scene.SendEngine(target!, EngineMessage.RpcFromHost, message.WrittenSpan, delivery, rpc.Channel);
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
        ReadOnlySpan<byte> arguments = message.Arguments;
        RpcRecipients? recipients = RpcRecipientMessage.Read(ref reader, scene.Session!.Options.MaxPeers).Recipients;

        if (!Resolve(message, out Component? component, out RpcMember? rpc) ||
            NetworkAccessor.IsExcludedFromNetwork(component.GameObject))
        {
            NetworkDiagnostics.Record("RPC", "Rejected", sender, Guid.Empty, $"Unknown or excluded {Describe(message)}",
                arguments);
            return;
        }

        Guid id = component.Identifier;
        uint method = rpc.Id;
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

    internal void OnRpcFromHost(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (scene.IsHost || !sender.IsHost)
        {
            return;
        }

        int callerId = RpcCallerMessage.Read(ref reader).CallerId;
        RpcCallMessage message = RpcCallMessage.Read(ref reader);
        ReadOnlySpan<byte> arguments = message.Arguments;
        if (!reader.IsAtEnd)
        {
            throw new InvalidDataException("RPC contains trailing bytes.");
        }

        // A call naming an object this peer doesn't have is dropped.
        if (!Resolve(message, out Component? component, out RpcMember? rpc))
        {
            NetworkDiagnostics.Record("RPC", "Rejected", sender, Guid.Empty, $"Unknown {Describe(message)}", arguments);
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

    /// <summary>The component and RPC a call names, when this peer has them.</summary>
    internal bool Resolve(in RpcCallMessage message, [NotNullWhen(true)] out Component? component,
        [NotNullWhen(true)] out RpcMember? rpc)
    {
        component = null;
        rpc = null;
        if (!scene.Entries.TryGetNet(message.NetId, out NetworkObject? entry) ||
            message.Component >= entry.Components.Length || entry.Components[message.Component].IsNotValid())
        {
            return false;
        }

        component = entry.Components[message.Component];
        rpc = NetworkMembers.Of(component).RpcAt(message.Method);
        return rpc != null;
    }

    private static string Describe(in RpcCallMessage message) =>
        $"object {message.NetId}, component {message.Component}, method {message.Method}";

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
        RpcRecipients? recipients, NetPeer? except = null, byte channel = 0, GameObject? target = null)
    {
        foreach (NetPeer peer in scene.Session!.Peers)
        {
            if (!peer.IsLocal && peer != except && RpcRecipients.Includes(recipients, peer) &&
                (target.IsNotValid() || scene.Sees(target, peer)))
            {
                scene.SendEngine(peer, kind, payload, delivery, channel);
            }
        }
    }
}
