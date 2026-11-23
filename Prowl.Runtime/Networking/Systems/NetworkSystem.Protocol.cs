// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
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
            if (channel != 0 && kind is not (EngineMessage.RpcToHost or EngineMessage.RpcFromHost))
            {
                NetworkDiagnostics.Record(kind.ToString(), "Rejected", peer, Guid.Empty, $"Sent on channel {channel}");
                return;
            }

            if (IsHost && !peer.IsLocal && !AdmitClientMessage(peer, kind, reader.Remaining))
            {
                return;
            }

            NetworkStatistics.CountReceived(kind, peer, reader.Remaining);
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
                // Anything else is a bug on this side. Report it, but a client that keeps causing it is let go.
                NetworkDiagnostics.Record(kind.ToString(), "Failed", peer, Guid.Empty, e.Message);
                int failures = IsHost && !peer.IsLocal ? CountFailure(peer) : 0;
                if (failures <= LoggedFailures)
                {
                    Debug.LogError($"[Network] Handling a {kind} message from {peer.Name} failed: {e}");
                }

                if (failures >= MaxFailures && Session is { } session)
                {
                    session.Kick(peer, "Its messages kept failing on the host.");
                }
            }
        });

    // Failures a client's messages may cause before it is kicked, and how many of them are logged. A client that
    // has caused none for a while starts over.
    private const int MaxFailures = 10, LoggedFailures = 3;
    private const double FailureMemorySeconds = 60;

    private readonly Dictionary<int, (int Count, double At)> _failures = new();
    private readonly Dictionary<int, (double Tokens, double At)> _messageBudgets = new();

    private int CountFailure(NetPeer peer)
    {
        (int count, double at) = _failures.GetValueOrDefault(peer.Id);
        count = Now - at > FailureMemorySeconds ? 1 : count + 1;
        _failures[peer.Id] = (count, Now);
        return count;
    }

    /// <summary>
    ///     False, kicking or dropping the client, when its message is over the client size limit or it has sent faster
    ///     than its message budget refills.
    /// </summary>
    private bool AdmitClientMessage(NetPeer peer, EngineMessage kind, int size)
    {
        if (size > Configuration.MaxClientMessageSize)
        {
            throw new InvalidDataException(
                $"A {kind} message of {size} bytes is over the {Configuration.MaxClientMessageSize} a client may send.");
        }

        int rate = Configuration.MaxClientMessagesPerSecond;
        if (rate == 0)
        {
            return true;
        }

        double now = Now;
        (double tokens, double at) = _messageBudgets.TryGetValue(peer.Id, out (double Tokens, double At) budget)
            ? budget
            : (rate, now);
        tokens = Math.Min(rate, tokens + (now - at) * rate) - 1;
        _messageBudgets[peer.Id] = (tokens, now);
        if (tokens >= 0)
        {
            return true;
        }

        Session?.Kick(peer, $"It sent more than {rate} messages a second.");
        return false;
    }

    internal void ForgetPeerLimits(int peer)
    {
        _failures.Remove(peer);
        _messageBudgets.Remove(peer);
    }

    internal void SendEngine(NetPeer peer, EngineMessage kind, ReadOnlySpan<byte> payload,
        Delivery delivery = Delivery.Reliable, byte channel = 0)
    {
        if (IsHost && kind is EngineMessage.RpcFromHost && !HasLoadedScene(peer))
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
        NetworkStatistics.CountSent(kind, peer, payload.Length);
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
        if (delivery == Delivery.Unreliable || kind is EngineMessage.RpcFromHost)
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
        foreach (NetPeer peer in Session.Peers)
        {
            if (!peer.IsLocal && peer != except)
            {
                NetworkStatistics.CountSent(kind, peer, payload.Length);
            }
        }

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
                session.Kick(peer, "Your message limit is too small for networking.");
            }
        }
    }
}
