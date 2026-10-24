// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Globalization;
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
    public static int NetworkObjectCount => Networking.System?.Entries.Count ?? 0;
    public static int PendingSyncCount => Networking.System?.Sync.Dirty.Count ?? 0;

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

    /// <summary>Copies live object diagnostics without keeping scene objects alive.</summary>
    public static void GetObjectDiagnostics(List<ObjectDiagnostic> output)
    {
        output.Clear();
        if (Networking.System is not { } scene)
        {
            return;
        }

        foreach (NetworkObject entry in scene.Entries.Values)
        {
            GameObject go = entry.GameObject;
            if (!go.IsValid())
            {
                continue;
            }

            output.Add(new ObjectDiagnostic(go.Identifier, go.Name, entry.Owner, scene.HasControl(go),
                go.Network.HasTransformSync, entry.TransformSync.Samples.Count, entry.TransformSync.LastReceivedTick));
        }
    }

    // Decode a separate reader into captured text. Never apply values or invoke an RPC for diagnostics.
    internal static (string? Context, string? Name) DescribePayload(NetworkSystem scene, EngineMessage kind,
        ReadOnlySpan<byte> payload)
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
                string caller = kind == EngineMessage.RpcFromHost
                    ? $"Caller ID: {RpcCallerMessage.Read(ref reader).CallerId}\n"
                    : "";
                RpcCallMessage message = RpcCallMessage.Read(ref reader);
                Guid id = message.ComponentId;
                uint method = message.MethodId;
                if (scene.FindByIdentifier(id) is not Component component)
                {
                    return ($"{caller}Component: {id}\nUnknown RPC #{method}", $"Unknown RPC #{method}");
                }

                RpcMember? rpc = NetworkMembers.Of(component).Rpc(method);
                return (
                    $"{caller}Object: {component.GameObject.Name}\nComponent: {component.GetType().Name}\nRPC: {rpc?.Signature ?? $"#{method}"}"
                    + (rpc == null ? "" : $"\nRecipient: {rpc.Mode}"),
                    $"{component.GetType().Name}.{rpc?.Signature ?? $"#{method}"}");
            }

            SyncMessage.Read(ref reader); // Original state writer, preserved by host relaying.
            uint count = SyncBlock.Read(ref reader).Count;
            StringBuilder context = new($"{count} SyncVar value(s)");
            List<string> names = new();
            // Bound capture work and retained text even for very large sync batches.
            for (uint i = 0; i < Math.Min(count, 32u); i++)
            {
                SyncValue message = SyncValue.Read(ref reader);
                Guid id = message.ComponentId;
                uint slot = message.Slot;
                ReadOnlySpan<byte> value = message.Value;
                if (scene.FindByIdentifier(id) is not Component component)
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

    internal static void TraceMember(string kind, string direction, NetPeer? peer, Component component, uint id,
        bool rpc, string result, ReadOnlySpan<byte> payload)
    {
        if (!Enabled)
        {
            return;
        }

        NetworkMembers members = NetworkMembers.Of(component);
        SyncMember? sync = rpc ? null : members.Sync(id);
        string member = (rpc ? members.Rpc(id)?.Signature : sync?.Name) ?? $"#{id}";
        string? value = sync?.FormatValue(component);
        Record(kind, direction, peer, component.Identifier,
            $"{component.GetType().Name}.{member}: {result}", payload,
            $"Object: {component.GameObject.Name}\nComponent: {component.GetType().Name}\n{(rpc ? "RPC" : "Property")}: {member}"
            + (value == null ? "" : $"\nLocal value: {value}"), $"{component.GetType().Name}.{member}");
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

    public readonly record struct ObjectDiagnostic(
        Guid Identifier,
        string Name,
        int OwnerId,
        bool Controlled,
        bool SyncTransform,
        int BufferedSamples,
        uint LastReceivedTick);
}
