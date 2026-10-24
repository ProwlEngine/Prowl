// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;

using Prowl.Warp;

namespace Prowl.Runtime.Networking;

internal sealed partial class NetworkSystem
{
    internal void RegisterEngineMessage(NetSession session, EngineMessage kind, RawMessageHandler handler) =>
        session.OnRaw((ushort)kind, (peer, ref reader, delivery) =>
        {
            if (NetworkDiagnostics.Enabled)
            {
                NetReader copy = reader;
                ReadOnlySpan<byte> payload = copy.ReadBytes(copy.Remaining);
                (string? Context, string? Name) description = NetworkDiagnostics.DescribePayload(this, kind, payload);
                NetworkDiagnostics.Record(kind.ToString(), "In", peer, Guid.Empty, delivery.ToString(),
                    payload, description.Context, description.Name);
            }

            try
            {
                if (!CanReceiveEngineMessage(peer, kind))
                {
                    return;
                }

                handler(peer, ref reader, delivery);
            }
            catch (Exception e) when (e is OverflowException or NotSupportedException or FormatException
                                          or ArgumentException)
            {
                // A value that doesn't fit its type is a malformed message. Warp drops whoever sent it.
                throw new InvalidDataException($"A {kind} message could not be read: {e.Message}", e);
            }
            catch (Exception e) when (e is not (EndOfStreamException or InvalidDataException))
            {
                // Anything else is a bug on this side, not the sender's fault. Report it and keep the session going.
                NetworkDiagnostics.Record(kind.ToString(), "Failed", peer, Guid.Empty, e.Message);
                Debug.LogError($"[Network] Handling a {kind} message from {peer.Name} failed: {e}");
            }
        });

    internal void SendEngine(NetPeer peer, EngineMessage kind, ReadOnlySpan<byte> payload,
        Delivery delivery = Delivery.Reliable)
    {
        if (IsHost && kind is EngineMessage.RpcFromHost or EngineMessage.StaticRpcFromHost && !IsPeerReady(peer))
        {
            return;
        }

        // Unreliable is a preference: reliable delivery and Warp fragmentation work on every
        // session transport, including backends without datagrams or with smaller payload limits.
        if (delivery == Delivery.Unreliable &&
            (!Session!.Transport.Capabilities.Supports(delivery) ||
             (peer.Connection is { } connection &&
              (long)payload.Length + sizeof(ushort) > connection.GetMaxMessageSize(delivery))))
        {
            delivery = Delivery.Reliable;
        }

        Session!.SendRaw(peer, (ushort)kind, payload, delivery);
        if (NetworkDiagnostics.Enabled)
        {
            (string? Context, string? Name) description = NetworkDiagnostics.DescribePayload(this, kind, payload);
            NetworkDiagnostics.Record(kind.ToString(), "Out", peer, Guid.Empty, delivery.ToString(), payload,
                description.Context, description.Name);
        }
    }

    internal void BroadcastEngine(EngineMessage kind, ReadOnlySpan<byte> payload,
        Delivery delivery = Delivery.Reliable, NetPeer? except = null)
    {
        if (delivery == Delivery.Unreliable || kind is EngineMessage.RpcFromHost or EngineMessage.StaticRpcFromHost)
        {
            foreach (NetPeer peer in Session!.Peers)
            {
                if (!peer.IsLocal && peer != except)
                {
                    SendEngine(peer, kind, payload, delivery);
                }
            }

            return;
        }

        Session!.BroadcastRaw((ushort)kind, payload, delivery, except);
        if (!NetworkDiagnostics.Enabled)
        {
            return;
        }

        (string? Context, string? Name) description = NetworkDiagnostics.DescribePayload(this, kind, payload);
        foreach (NetPeer peer in Session.Peers)
        {
            if (!peer.IsLocal && peer != except)
            {
                NetworkDiagnostics.Record(kind.ToString(), "Out", peer, Guid.Empty, delivery.ToString(), payload,
                    description.Context, description.Name);
            }
        }
    }
}
