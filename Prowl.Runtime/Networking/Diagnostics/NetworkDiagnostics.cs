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

    /// <summary>Changes whenever entries are recorded or cleared, so a viewer can skip unchanged frames.</summary>
    public static long Version { get; private set; }
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
        Version++;
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
        Version++;
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
    // The peer is who the message went to or came from, which snapshot component ids are relative to.
    internal static (string? Context, string? Name) DescribePayload(NetworkSystem scene, EngineMessage kind,
        ReadOnlySpan<byte> payload, NetPeer? peer = null, bool outgoing = false)
    {
        if (kind is not (EngineMessage.Sync or EngineMessage.RpcToHost or EngineMessage.RpcFromHost
            or EngineMessage.Snapshot or EngineMessage.SnapshotAck))
        {
            return (null, null);
        }

        try
        {
            NetReader reader = new(payload);
            if (kind == EngineMessage.Snapshot)
            {
                return DescribeSnapshot(scene, ref reader, peer, outgoing);
            }

            if (kind == EngineMessage.SnapshotAck)
            {
                return DescribeAck(ref reader);
            }

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
                AppendValue(context, names, scene.FindByIdentifier(message.ComponentId) as Component,
                    message.ComponentId.ToString(), message.Slot, message.Value);
            }

            if (count > 32)
            {
                context.Append($"\n\n{count - 32} more values omitted");
            }

            return (context.ToString(), count == 0 ? "No SyncVars" : JoinNames(names, (int)count));
        }
        catch (Exception)
        {
            // Diagnostic decoding must never change whether a network message succeeds.
            return ("Payload context unavailable", null);
        }
    }

    private static (string Context, string Name) DescribeSnapshot(NetworkSystem scene, ref NetReader reader,
        NetPeer? peer, bool outgoing)
    {
        SnapshotMessage header = SnapshotMessage.Read(ref reader);
        StringBuilder context = new($"Snapshot #{header.Id}, tick {header.Tick}, scene epoch {header.Epoch}"
            + (header.Id == 0 ? " (no acknowledgement)" : ""));
        List<string> names = new();

        int transforms = 0;
        uint groups = TransformGroupBlock.Read(ref reader).Count;
        for (uint g = 0; g < groups; g++)
        {
            TransformGroup group = TransformGroup.Read(ref reader);
            for (uint i = 0; i < group.Count; i++, transforms++)
            {
                TransformRecord record = TransformRecord.Read(ref reader);
                // Bound capture work and retained text, as for sync values.
                if (transforms < 32)
                {
                    string target = scene.Entries.TryGetNet(record.NetId, out NetworkObject? entry) &&
                                    entry.GameObject.IsValid()
                        ? entry.GameObject.Name
                        : $"Unknown object #{record.NetId}";
                    context.Append($"\n\nTransform: {target}\nTick: {group.Tick}\nChannels: {record.Update.Channels}");
                }
            }
        }

        if (transforms > 32)
        {
            context.Append($"\n\n{transforms - 32} more transforms omitted");
        }

        uint count = reader.ReadVarUInt32();
        for (uint i = 0; i < Math.Min(count, 32u); i++)
        {
            SnapshotValue value = SnapshotValue.Read(ref reader);
            Component? component = value.Definition is { } guid
                ? scene.FindByIdentifier(guid) as Component
                : SnapshotComponent(scene, peer, value.ComponentId, outgoing);
            AppendValue(context, names, component, value.Definition?.ToString() ?? $"#{value.ComponentId}", value.Slot,
                value.Value);
        }

        if (count > 32)
        {
            context.Append($"\n\n{count - 32} more values omitted");
        }

        string transformText = transforms == 1 ? "1 transform" : $"{transforms} transforms";
        string name = (transforms, count) switch
        {
            (0, 0) => "Empty",
            (_, 0) => transformText,
            (0, _) => JoinNames(names, (int)count),
            _ => $"{transformText}, {JoinNames(names, (int)count)}"
        };
        return (context.ToString(), name);
    }

    // An id this peer gave when sending, or one the other peer defined when receiving.
    private static Component? SnapshotComponent(NetworkSystem scene, NetPeer? peer, uint id, bool outgoing)
    {
        if (peer == null || !scene.Snapshots.TryGetComponentIds(peer, out var given, out var received))
        {
            return null;
        }

        if (!outgoing)
        {
            return received.TryGetValue(id, out Guid guid) ? scene.FindByIdentifier(guid) as Component : null;
        }

        foreach ((Component component, uint value) in given)
        {
            if (value == id)
            {
                return component;
            }
        }

        return null;
    }

    private static (string Context, string Name) DescribeAck(ref NetReader reader)
    {
        uint ids = SnapshotAckMessage.ReadCount(ref reader, 1);
        List<uint> acknowledged = new();
        for (uint i = 0; i < ids; i++)
        {
            acknowledged.Add(reader.ReadVarUInt32());
        }

        uint components = SnapshotAckMessage.ReadCount(ref reader, 16);
        for (uint i = 0; i < components; i++)
        {
            reader.ReadGuid();
        }

        uint objects = SnapshotAckMessage.ReadCount(ref reader, 1);
        string name = ids == 1 ? $"Snapshot #{acknowledged[0]}" : $"{ids} snapshots";
        if (components + objects > 0)
        {
            name += $", {components + objects} rejected";
        }

        return ($"Acknowledges: {string.Join(", ", acknowledged)}\nRejected components: {components}\nRejected objects: {objects}",
            name);
    }

    private static void AppendValue(StringBuilder context, List<string> names, Component? component, string id,
        uint slot, ReadOnlySpan<byte> value)
    {
        if (component == null)
        {
            context.Append($"\n\nComponent: {id}\nUnknown SyncVar #{slot}");
            if (names.Count < 2)
            {
                names.Add($"Unknown SyncVar #{slot}");
            }

            return;
        }

        SyncMember? sync = NetworkMembers.Of(component).Sync(slot);
        string property = $"{component.GetType().Name}.{sync?.Name ?? $"#{slot}"}";
        if (names.Count < 2)
        {
            names.Add(property);
        }

        context.Append($"\n\nObject: {component.GameObject.Name}\nProperty: {property}");
        if (sync != null)
        {
            NetReader valueReader = new(value);
            context.Append($"\nValue: {sync.FormatPayload(ref valueReader)}");
        }
    }

    private static string JoinNames(List<string> names, int count) =>
        string.Join(", ", names) + (count > 2 ? $" (+{count - 2} more)" : "");

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
