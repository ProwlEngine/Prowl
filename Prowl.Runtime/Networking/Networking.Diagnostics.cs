// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using Prowl.Warp;

namespace Prowl.Runtime.Networking;

/// <summary>Main-thread network diagnostics. Capture is opt-in and retains no game object references.</summary>
public static class NetworkDiagnostics
{
    public const int Capacity = 1024;

    private static readonly Entry[] s_entries = new Entry[Capacity];
    private static int s_start;
    private static long s_sequence;

    /// <summary>Enables event capture, independently of the transport's packet counters.</summary>
    public static bool Enabled { get; set; }

    public static int Count { get; private set; }

    public static long Overwritten { get; private set; }

    /// <summary>Returns a captured entry, oldest first.</summary>
    public static Entry GetEntry(int index)
    {
        if ((uint)index >= (uint)Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return s_entries[(s_start + index) % Capacity];
    }

    /// <summary>Clears history; sequence numbers remain unique across clears and sessions.</summary>
    public static void Clear()
    {
        Array.Clear(s_entries);
        s_start = Count = 0;
        Overwritten = 0;
    }

    /// <summary>Formats a generated SyncVar diagnostic without allowing user ToString failures to affect replication.</summary>
    public static string FormatValue(object? value)
    {
        try
        {
            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
            if (value == null)
            {
                text = "null";
            }

            return text.Length <= 160 ? text : text[..160] + "…";
        }
        catch
        {
            return "<value unavailable>";
        }
    }

    internal static void Record(string kind, string direction, NetPeer? peer, Guid target, string detail,
        ReadOnlySpan<byte> payload = default, string? context = null, string? name = null)
    {
        if (!Enabled)
        {
            return;
        }

        Entry entry = new(++s_sequence, DateTime.UtcNow, kind, direction, peer?.ToString() ?? "Session",
            target, detail, payload.Length, Convert.ToHexString(payload[..Math.Min(payload.Length, 64)]))
        {
            Context = context, Name = name
        };
        if (Count == Capacity)
        {
            s_entries[s_start] = entry;
            s_start = (s_start + 1) % Capacity;
            Overwritten++;
        }
        else
        {
            s_entries[(s_start + Count++) % Capacity] = entry;
        }
    }

    public readonly record struct Entry(
        long Sequence,
        DateTime Time,
        string Kind,
        string Direction,
        string Peer,
        Guid Target,
        string Detail,
        int PayloadBytes,
        string Preview)
    {
        public string? Context { get; init; }
        public string? Name { get; init; }
    }
}

public static partial class Networking
{
    public static int NetworkObjectCount => s_entries.Count;
    public static int PendingSyncCount => s_dirty.Count;

    /// <summary>Copies live object diagnostics without keeping scene objects alive.</summary>
    public static void GetObjectDiagnostics(List<ObjectDiagnostic> output)
    {
        output.Clear();
        foreach (NetworkEntry entry in s_entries.Values)
        {
            GameObject go = entry.GameObject;
            if (!go.IsValid())
            {
                continue;
            }

            output.Add(new ObjectDiagnostic(go.Identifier, go.Name, entry.Owner, HasControl(go),
                go.Network.HasTransformSync,
                entry.Samples.Count, entry.LastReceivedTick));
        }
    }

    private static void RegisterEngineMessage(NetSession session, EngineMessage kind, RawMessageHandler handler) =>
        session.OnRaw((ushort)kind, (peer, ref reader, delivery) =>
        {
            if (NetworkDiagnostics.Enabled)
            {
                NetReader copy = reader;
                ReadOnlySpan<byte> payload = copy.ReadBytes(copy.Remaining);
                (string? Context, string? Name) description = DescribePayload(kind, payload);
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

    private static void SendEngine(NetPeer peer, EngineMessage kind, ReadOnlySpan<byte> payload,
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
            (string? Context, string? Name) description = DescribePayload(kind, payload);
            NetworkDiagnostics.Record(kind.ToString(), "Out", peer, Guid.Empty, delivery.ToString(), payload,
                description.Context, description.Name);
        }
    }

    private static void BroadcastEngine(EngineMessage kind, ReadOnlySpan<byte> payload,
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

        (string? Context, string? Name) description = DescribePayload(kind, payload);
        foreach (NetPeer peer in Session.Peers)
        {
            if (!peer.IsLocal && peer != except)
            {
                NetworkDiagnostics.Record(kind.ToString(), "Out", peer, Guid.Empty, delivery.ToString(), payload,
                    description.Context, description.Name);
            }
        }
    }

    // Decode a separate reader into captured text. Never apply values or invoke an RPC for diagnostics.
    private static (string? Context, string? Name) DescribePayload(EngineMessage kind, ReadOnlySpan<byte> payload)
    {
        if (kind is not (EngineMessage.Sync or EngineMessage.RpcToHost or EngineMessage.RpcFromHost))
        {
            return (null, null);
        }

        try
        {
            NetReader reader = new(payload);
            if (kind is EngineMessage.RpcToHost or EngineMessage.RpcFromHost)
            {
                string caller = kind == EngineMessage.RpcFromHost ? $"Caller ID: {reader.ReadVarUInt32()}\n" : "";
                Guid id = reader.ReadGuid();
                uint method = reader.ReadUInt32();
                if (FindByIdentifier(id) is not Component component)
                {
                    return ($"{caller}Component: {id}\nUnknown RPC #{method}", $"Unknown RPC #{method}");
                }

                RpcMember? rpc = NetworkMembers.Of(component).Rpc(method);
                return (
                    $"{caller}Object: {component.GameObject.Name}\nComponent: {component.GetType().Name}\nRPC: {rpc?.Signature ?? $"#{method}"}"
                    + (rpc == null ? "" : $"\nRecipient: {rpc.Mode}"),
                    $"{component.GetType().Name}.{rpc?.Signature ?? $"#{method}"}");
            }

            reader.ReadInt32(); // Original state writer, preserved by host relaying.
            reader.ReadUInt64();
            uint count = reader.ReadVarUInt32();
            StringBuilder context = new($"{count} SyncVar value(s)");
            List<string> names = new();
            // Bound capture work and retained text even for very large sync batches.
            for (uint i = 0; i < Math.Min(count, 32u); i++)
            {
                Guid id = reader.ReadGuid();
                uint slot = reader.ReadVarUInt32();
                reader.ReadUInt32(); // Ownership generation.
                ReadOnlySpan<byte> value = reader.ReadBytesWithLength();
                if (FindByIdentifier(id) is not Component component)
                {
                    context.Append($"\n\nComponent: {id}\nUnknown SyncVar #{slot}");
                    if (i < 2)
                    {
                        names.Add($"Unknown SyncVar #{slot}");
                    }

                    continue;
                }

                SyncMember? sync = NetworkMembers.Of(component).Sync(slot);
                if (i < 2)
                {
                    names.Add($"{component.GetType().Name}.{sync?.Name ?? $"#{slot}"}");
                }

                NetReader valueReader = new(value);
                context.Append(
                    $"\n\nObject: {component.GameObject.Name}\nProperty: {component.GetType().Name}.{sync?.Name ?? $"#{slot}"}");
                if (sync != null)
                {
                    context.Append($"\nValue: {sync.FormatPayload(ref valueReader)}");
                }
            }

            if (count > 32)
            {
                context.Append($"\n\n{count - 32} more values omitted");
            }

            string name = count == 0 ? "No SyncVars" : string.Join(", ", names);
            if (count > 2)
            {
                name += $" (+{count - 2} more)";
            }

            return (context.ToString(), name);
        }
        catch (Exception)
        {
            // Diagnostic decoding must never change whether a network message succeeds.
            return ("Payload context unavailable", null);
        }
    }

    private static void TraceMember(string kind, string direction, NetPeer? peer, Component component, uint id,
        bool rpc, string result, ReadOnlySpan<byte> payload)
    {
        if (!NetworkDiagnostics.Enabled)
        {
            return;
        }

        NetworkMembers members = NetworkMembers.Of(component);
        SyncMember? sync = rpc ? null : members.Sync(id);
        string member = (rpc ? members.Rpc(id)?.Signature : sync?.Name) ?? $"#{id}";
        string? value = sync?.FormatValue(component);
        NetworkDiagnostics.Record(kind, direction, peer, component.Identifier,
            $"{component.GetType().Name}.{member}: {result}", payload,
            $"Object: {component.GameObject.Name}\nComponent: {component.GetType().Name}\n{(rpc ? "RPC" : "Property")}: {member}"
            + (value == null ? "" : $"\nLocal value: {value}"), $"{component.GetType().Name}.{member}");
    }

    public readonly record struct ObjectDiagnostic(
        Guid Identifier,
        string Name,
        int OwnerId,
        bool Controlled,
        bool SyncTransform,
        int BufferedSamples,
        uint LastReceivedTick);
}
