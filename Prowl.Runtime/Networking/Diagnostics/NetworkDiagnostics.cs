// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using Prowl.Courier;

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
    public static int PendingSyncCount => Networking.System?.Sync.PendingCount ?? 0;

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
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
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
            if (entry.GameObject.IsValid())
            {
                output.Add(Describe(scene, entry));
            }
        }
    }

    /// <summary>Live diagnostics of the networked object <paramref name="go" /> is or is part of, when it is spawned.</summary>
    public static bool TryGetObjectDiagnostic(GameObject go, out ObjectDiagnostic diagnostic)
    {
        diagnostic = default;
        if (Networking.System is not { } scene || scene.FindEntry(go) is not { } entry)
        {
            return false;
        }

        diagnostic = Describe(scene, entry);
        return true;
    }

    private static ObjectDiagnostic Describe(NetworkSystem scene, NetworkObject entry)
    {
        GameObject go = entry.GameObject;
        TransformSync? sync = entry.RootTransform is { } transform ? transform.Sync : null;

        // Only the host knows who else has the object.
        int observers = 0;
        if (scene.IsHost && scene.Session is { } session)
        {
            foreach (NetPeer peer in session.Peers)
            {
                if (!peer.IsLocal && scene.Sees(go, peer))
                {
                    observers++;
                }
            }
        }

        return new ObjectDiagnostic(go.Identifier, go.Name, entry.Owner, scene.HasControl(go), sync != null,
            sync?.Samples.Count ?? 0, sync?.LastReceivedTick ?? 0, entry.NetId, entry.Transforms.Length,
            entry.Components.Length, observers);
    }

    // Decode a separate reader into captured text. Never apply values or invoke an RPC for diagnostics.
    internal static (string? Context, string? Name) DescribePayload(NetworkSystem scene, EngineMessage kind,
        ReadOnlySpan<byte> payload, NetPeer? peer = null, bool outgoing = false)
    {
        if (kind is not (EngineMessage.Sync or EngineMessage.RpcToHost or EngineMessage.RpcFromHost
            or EngineMessage.Transform or EngineMessage.TransformAck))
        {
            return (null, null);
        }

        try
        {
            NetReader reader = new(payload);
            if (kind == EngineMessage.Transform)
            {
                return DescribeTransforms(scene, ref reader, peer, outgoing);
            }

            if (kind == EngineMessage.TransformAck)
            {
                return DescribeAck(ref reader);
            }

            if (kind is EngineMessage.RpcToHost or EngineMessage.RpcFromHost)
            {
                string caller = kind == EngineMessage.RpcFromHost
                    ? $"Caller ID: {RpcCallerMessage.Read(ref reader).CallerId}\n"
                    : "";
                RpcCallMessage message = RpcCallMessage.Read(ref reader);
                if (!scene.Rpcs.Resolve(message, out Component? component, out RpcMember? rpc))
                {
                    return (
                        $"{caller}Object: {message.NetId}\nComponent: {message.Component}\nUnknown RPC #{message.Method}",
                        $"Unknown RPC #{message.Method}");
                }

                return (
                    $"{caller}Object: {component.GameObject.Name}\nComponent: {component.GetType().Name}\nRPC: {rpc.Signature}\nRecipient: {rpc.Mode}",
                    $"{component.GetType().Name}.{rpc.Signature}");
            }

            return DescribeState(scene, ref reader);
        }
        catch (Exception)
        {
            // Diagnostic decoding must never change whether a network message succeeds.
            return ("Payload context unavailable", null);
        }
    }

    private static (string Context, string Name) DescribeTransforms(NetworkSystem scene, ref NetReader reader,
        NetPeer? peer, bool outgoing)
    {
        TransformBatchMessage header = TransformBatchMessage.Read(ref reader);
        StringBuilder context = new($"Transform batch #{header.Id}, tick {header.Tick}, scene epoch {header.Epoch}"
            + (header.Id == 0 ? " (no acknowledgement)" : ""));
        List<string> names = new();

        int transforms = 0;
        uint records = TransformBatchMessage.ReadRecordCount(ref reader);
        {
            for (uint i = 0; i < records; i++, transforms++)
            {
                TransformRecord record = TransformRecord.Read(ref reader);
                // Bound capture work and retained text, as for sync values.
                if (transforms < 32)
                {
                    string target = scene.Entries.TryGetNet(record.NetId, out NetworkObject? entry) &&
                                    entry.GameObject.IsValid()
                        ? entry.GameObject.Name
                        : $"Unknown object #{record.NetId}";
                    context.Append($"\n\nTransform: {target}\nTick: {header.Tick - record.TicksAgo}\nChannels: {record.Update.Channels}");
                }
            }
        }

        if (transforms > 32)
        {
            context.Append($"\n\n{transforms - 32} more transforms omitted");
        }

        return (context.ToString(), transforms == 0 ? "Empty" : transforms == 1 ? "1 transform" : $"{transforms} transforms");
    }

    // A state message: the objects it has records for, and which members of each component changed.
    private static (string Context, string Name) DescribeState(NetworkSystem scene, ref NetReader reader)
    {
        uint count = reader.ReadVarUInt32();
        StringBuilder context = new($"{count} object(s) with [Sync] changes");
        List<string> names = new();
        for (uint i = 0; i < count; i++)
        {
            uint netId = reader.ReadVarUInt32();
            reader.ReadVarUInt32();
            ulong components = reader.ReadVarUInt64();
            NetworkObject? entry = scene.Entries.TryGetNet(netId, out NetworkObject? found) ? found : null;

            // Bound capture work and retained text even for very large batches.
            bool shown = i < 32;
            if (shown)
            {
                context.Append($"\n\nObject: {(entry != null && entry.GameObject.IsValid() ? entry.GameObject.Name : $"Unknown object #{netId}")}");
            }

            while (components != 0)
            {
                int c = System.Numerics.BitOperations.TrailingZeroCount(components);
                components &= components - 1;
                NetReader block = new(reader.ReadBytesWithLength());
                if (!shown || entry == null || c >= entry.Components.Length || entry.Components[c].IsNotValid())
                {
                    continue;
                }

                Component component = entry.Components[c];
                NetworkMembers members = NetworkMembers.Of(component);
                ulong bits = block.ReadVarUInt64();
                while (bits != 0)
                {
                    int m = System.Numerics.BitOperations.TrailingZeroCount(bits);
                    bits &= bits - 1;
                    if (members.SyncAt(m) is not { } sync)
                    {
                        break;
                    }

                    string member = $"{component.GetType().Name}.{sync.Name}";
                    names.Add(member);
                    context.Append($"\nMember: {member}\nValue: {sync.FormatPayload(ref block)}");
                }
            }
        }

        if (count > 32)
        {
            context.Append($"\n\n{count - 32} more objects omitted");
        }

        return (context.ToString(), names.Count == 0 ? "No SyncVars" : JoinNames(names, names.Count));
    }

    private static (string Context, string Name) DescribeAck(ref NetReader reader)
    {
        uint ids = TransformAckMessage.ReadCount(ref reader);
        List<uint> acknowledged = new();
        for (uint i = 0; i < ids; i++)
        {
            acknowledged.Add(reader.ReadVarUInt32());
        }

        uint objects = TransformAckMessage.ReadCount(ref reader);
        string name = ids == 1 ? $"Transform batch #{acknowledged[0]}" : $"{ids} transform batches";
        if (objects > 0)
        {
            name += $", {objects} rejected";
        }

        return ($"Acknowledges: {string.Join(", ", acknowledged)}\nRejected objects: {objects}", name);
    }

    private static void AppendValue(StringBuilder context, List<string> names, Component? component, string id,
        uint slot, ReadOnlySpan<byte> value)
    {
        if (ReferenceEquals(component, null))
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
        uint LastReceivedTick,
        uint NetId = 0,
        int Transforms = 0,
        int Components = 0,
        int Observers = 0);
}
