// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;

using Prowl.Courier;

namespace Prowl.Runtime.Networking;

/// <summary>Handles one kind of engine message.</summary>
internal delegate void EngineMessageHandler(NetPeer sender, ref NetReader reader, Delivery delivery);

internal sealed partial class NetworkSystem
{
    internal void RegisterEngineMessage(NetSession session, EngineMessage kind, EngineMessageHandler handler) =>
        session.OnRaw((ushort)kind, (peer, ref reader, delivery, channel) =>
        {
            if (NetworkDiagnostics.Enabled)
            {
                NetReader copy = reader;
                ReadOnlySpan<byte> payload = copy.ReadBytes(copy.Remaining);
                (string? Context, string? Name) description = NetworkDiagnostics.DescribePayload(this, kind, payload, peer);
                NetworkDiagnostics.Record(kind.ToString(), "In", peer, Guid.Empty, Describe(delivery, channel),
                    payload, description.Context, description.Name);
            }

            // Replication relies on everything but RPCs sharing channel 0, so one can't overtake another.
            if (channel != 0 && kind is not (EngineMessage.RpcToHost or EngineMessage.RpcFromHost
                    or EngineMessage.StaticRpcToHost or EngineMessage.StaticRpcFromHost))
            {
                NetworkDiagnostics.Record(kind.ToString(), "Rejected", peer, Guid.Empty, $"Sent on channel {channel}");
                return;
            }

            try
            {
                if (!CanReceiveEngineMessage(peer, kind))
                {
                    if (AwaitsActivation(kind))
                    {
                        EarlyRpcs.Add((peer, handler, reader.ReadBytes(reader.Remaining).ToArray(), delivery));
                    }

                    return;
                }

                handler(peer, ref reader, delivery);
            }
            catch (Exception e) when (e is OverflowException or NotSupportedException or FormatException
                                          or ArgumentException)
            {
                // A value that doesn't fit its type is a malformed message. Courier drops whoever sent it.
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
        Delivery delivery = Delivery.Reliable, byte channel = 0)
    {
        if (IsHost && kind is EngineMessage.RpcFromHost or EngineMessage.StaticRpcFromHost && !IsPeerReady(peer))
        {
            return;
        }

        if (IsHost && (_overLimitPeers.Contains(peer) || !FitsPeer(peer, kind, payload.Length)))
        {
            return;
        }

        // Unreliable is a preference: reliable delivery and Courier fragmentation work on every
        // session transport, including backends without datagrams or with smaller payload limits.
        if (delivery == Delivery.Unreliable &&
            (!Session!.Transport.Capabilities.Supports(delivery) ||
             (peer.Connection is { } connection &&
              (long)payload.Length + sizeof(ushort) > connection.GetMaxMessageSize(delivery))))
        {
            delivery = Delivery.Reliable;
        }

        channel = EngineChannel(channel, delivery);
        Session!.SendRaw(peer, (ushort)kind, payload, delivery, channel: channel);
        if (NetworkDiagnostics.Enabled)
        {
            (string? Context, string? Name) description = NetworkDiagnostics.DescribePayload(this, kind, payload, peer, true);
            NetworkDiagnostics.Record(kind.ToString(), "Out", peer, Guid.Empty, Describe(delivery, channel), payload,
                description.Context, description.Name);
        }
    }

    /// <summary>
    ///     A channel is a preference too: unreliable messages, and a transport with fewer channels, carry everything on
    ///     channel 0.
    /// </summary>
    private byte EngineChannel(byte channel, Delivery delivery) =>
        delivery == Delivery.Reliable && channel < Session!.Transport.Capabilities.Channels ? channel : (byte)0;

    private static string Describe(Delivery delivery, byte channel) =>
        channel == 0 ? delivery.ToString() : $"{delivery}, channel {channel}";

    internal void BroadcastEngine(EngineMessage kind, ReadOnlySpan<byte> payload,
        Delivery delivery = Delivery.Reliable, NetPeer? except = null, byte channel = 0)
    {
        if (delivery == Delivery.Unreliable || kind is EngineMessage.RpcFromHost or EngineMessage.StaticRpcFromHost)
        {
            foreach (NetPeer peer in Session!.Peers)
            {
                if (!peer.IsLocal && peer != except)
                {
                    SendEngine(peer, kind, payload, delivery, channel);
                }
            }

            return;
        }

        foreach (NetPeer peer in Session!.Peers)
        {
            if (!peer.IsLocal && peer != except)
            {
                FitsPeer(peer, kind, payload.Length);
            }
        }

        channel = EngineChannel(channel, delivery);
        Session!.BroadcastRaw((ushort)kind, payload, delivery, except, channel: channel);
        if (!NetworkDiagnostics.Enabled)
        {
            return;
        }

        (string? Context, string? Name) description = NetworkDiagnostics.DescribePayload(this, kind, payload, outgoing: true);
        foreach (NetPeer peer in Session.Peers)
        {
            if (!peer.IsLocal && peer != except)
            {
                NetworkDiagnostics.Record(kind.ToString(), "Out", peer, Guid.Empty, Describe(delivery, channel),
                    payload, description.Context, description.Name);
            }
        }
    }

    /// <summary>False, and the peer is queued to be kicked, when a message this size is more than it accepts.</summary>
    private bool FitsPeer(NetPeer peer, EngineMessage kind, int payloadLength)
    {
        // Courier adds a two-byte message id.
        if ((long)payloadLength + sizeof(ushort) <= peer.MaxMessageSize)
        {
            return true;
        }

        if (!_overLimitPeers.Contains(peer))
        {
            _overLimitPeers.Add(peer);
            Debug.LogWarning($"[Network] {peer.Name} accepts messages of at most {peer.MaxMessageSize} bytes, " +
                             $"too few for a {payloadLength + sizeof(ushort)} byte {kind} message, so it is removed.");
        }

        return false;
    }

    /// <summary>Kicks the peers whose message limit is too small for replication, outside any send loop.</summary>
    internal void KickOverLimitPeers()
    {
        if (_overLimitPeers.Count == 0)
        {
            return;
        }

        NetPeer[] peers = _overLimitPeers.ToArray();
        _overLimitPeers.Clear();
        foreach (NetPeer peer in peers)
        {
            if (Session is { IsHost: true, State: SessionState.Active } session &&
                session.TryGetPeer(peer.Id, out NetPeer? current) && current == peer)
            {
                session.Kick(peer, "Your message limit is too small for scene replication.");
            }
        }
    }
}
