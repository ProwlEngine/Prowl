// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Prowl.Warp;

namespace Prowl.Runtime;

/// <summary>Main-thread network diagnostics. Capture is opt-in and retains no game object references.</summary>
public static class NetworkDiagnostics
{
    public const int Capacity = 1024;
    public readonly record struct Entry(long Sequence, DateTime Time, string Kind, string Direction,
        string Peer, Guid Target, string Detail, int PayloadBytes, string Preview);

    private static readonly Entry[] s_entries = new Entry[Capacity];
    private static int s_start, s_count;
    private static long s_sequence;

    /// <summary>Enables event capture, independently of the transport's packet counters.</summary>
    public static bool Enabled { get; set; }
    public static int Count => s_count;
    public static long Overwritten { get; private set; }

    /// <summary>Returns a captured entry, oldest first.</summary>
    public static Entry GetEntry(int index)
    {
        if ((uint)index >= (uint)s_count) throw new ArgumentOutOfRangeException(nameof(index));
        return s_entries[(s_start + index) % Capacity];
    }

    /// <summary>Clears history; sequence numbers remain unique across clears and sessions.</summary>
    public static void Clear()
    {
        Array.Clear(s_entries);
        s_start = s_count = 0;
        Overwritten = 0;
    }

    /// <summary>Formats a generated SyncVar diagnostic without allowing user ToString failures to affect replication.</summary>
    public static string FormatValue(object? value)
    {
        try
        {
            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
            if (value == null) text = "null";
            return text.Length <= 160 ? text : text[..160] + "…";
        }
        catch { return "<value unavailable>"; }
    }

    internal static void Record(string kind, string direction, NetPeer? peer, Guid target, string detail,
        ReadOnlySpan<byte> payload = default)
    {
        if (!Enabled) return;
        var entry = new Entry(++s_sequence, DateTime.UtcNow, kind, direction, peer?.ToString() ?? "Session",
            target, detail, payload.Length, Convert.ToHexString(payload[..Math.Min(payload.Length, 64)]));
        if (s_count == Capacity)
        {
            s_entries[s_start] = entry;
            s_start = (s_start + 1) % Capacity;
            Overwritten++;
        }
        else s_entries[(s_start + s_count++) % Capacity] = entry;
    }
}

public static partial class Networking
{
    public readonly record struct ObjectDiagnostic(Guid Identifier, string Name, int OwnerId, bool Controlled,
        bool SyncTransform, int BufferedSamples, uint LastReceivedTick);

    public static int NetworkObjectCount => s_entries.Count;
    public static int PendingSyncCount => s_dirty.Count;

    /// <summary>Copies live object diagnostics without keeping scene objects alive.</summary>
    public static void GetObjectDiagnostics(List<ObjectDiagnostic> output)
    {
        output.Clear();
        foreach (NetworkEntry entry in s_entries.Values)
        {
            GameObject go = entry.GameObject;
            if (!go.IsValid()) continue;
            output.Add(new(go.Identifier, go.Name, entry.Owner, HasControl(go), go.NetworkSyncTransform,
                entry.Samples.Count, entry.LastReceivedTick));
        }
    }

    private static void RegisterEngineMessage(NetSession session, EngineMessage kind, RawMessageHandler handler)
    {
        session.OnRaw((ushort)kind, (NetPeer peer, ref NetReader reader, Delivery delivery) =>
        {
            if (NetworkDiagnostics.Enabled)
            {
                NetReader copy = reader;
                NetworkDiagnostics.Record(kind.ToString(), "In", peer, Guid.Empty, delivery.ToString(),
                    copy.ReadBytes(copy.Remaining));
            }
            try
            {
                handler(peer, ref reader, delivery);
            }
            catch (Exception e) when (e is OverflowException or NotSupportedException or FormatException or ArgumentException)
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
    }

    private static void SendEngine(NetPeer peer, EngineMessage kind, ReadOnlySpan<byte> payload, Delivery delivery = Delivery.Reliable)
    {
        Session!.SendRaw(peer, (ushort)kind, payload, delivery);
        if (NetworkDiagnostics.Enabled)
            NetworkDiagnostics.Record(kind.ToString(), "Out", peer, Guid.Empty, delivery.ToString(), payload);
    }

    private static void BroadcastEngine(EngineMessage kind, ReadOnlySpan<byte> payload,
        Delivery delivery = Delivery.Reliable, NetPeer? except = null)
    {
        Session!.BroadcastRaw((ushort)kind, payload, delivery, except);
        if (!NetworkDiagnostics.Enabled) return;
        foreach (NetPeer peer in Session.Peers)
            if (!peer.IsLocal && peer != except)
                NetworkDiagnostics.Record(kind.ToString(), "Out", peer, Guid.Empty, delivery.ToString(), payload);
    }

    private static void TraceMember(string kind, string direction, NetPeer? peer, Component component, uint id,
        bool rpc, string result, ReadOnlySpan<byte> payload)
    {
        if (!NetworkDiagnostics.Enabled) return;
        NetworkMembers members = NetworkMembers.Of(component);
        SyncMember? sync = rpc ? null : members.Sync(id);
        string member = (rpc ? members.Rpc(id)?.Signature : sync?.Name) ?? $"#{id}";
        string? value = sync?.FormatValue(component);
        NetworkDiagnostics.Record(kind, direction, peer, component.Identifier,
            $"{component.GameObject.Name} / {component.GetType().Name}.{member} — {result}{(value == null ? "" : $" (local value: {value})")}", payload);
    }
}
