// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;
using Prowl.Runtime.Networking.Enums;
using Prowl.Runtime.Networking.Objects;
using Prowl.Vector;

namespace Prowl.Runtime.Networking.Serialization;

internal static class NetworkSerialization
{
    [ThreadStatic] internal static bool WritingSnapshot;

    internal static bool Include(GameObject go) => !WritingSnapshot || NetworkAccessor.ModeOf(go) != NetworkMode.Never;

    internal static void Write(GameObject go, EchoObject output)
    {
        NetworkAccessor? network = go._network;
        if (network == null || (network.Mode == NetworkMode.Snapshot && network.OwnerTransfer == OwnerTransfer.Fixed &&
                                network.Orphaned == NetworkOrphaned.Destroy && network.Flags == NetworkFlags.None))
        {
            return;
        }

        EchoObject settings = EchoObject.NewCompound(4);
        settings.Add("Mode", new EchoObject((byte)network.Mode));
        settings.Add("OwnerTransfer", new EchoObject((byte)network.OwnerTransfer));
        settings.Add("Orphaned", new EchoObject((byte)network.Orphaned));
        settings.Add("Flags", new EchoObject((byte)network.Flags));
        output.Add("Network", settings);
    }

    internal static void Read(GameObject go, EchoObject input)
    {
        if (!input.TryGet("Network", out EchoObject? settings))
        {
            if (go._network is not { } existing)
            {
                return;
            }

            existing.Mode = NetworkMode.Snapshot;
            existing.OwnerTransfer = OwnerTransfer.Fixed;
            existing.Orphaned = NetworkOrphaned.Destroy;
            existing.Flags = NetworkFlags.None;
            return;
        }

        NetworkAccessor network = go.Network;
        network.Mode = (NetworkMode)(settings["Mode"]?.ByteValue ?? (byte)NetworkMode.Snapshot);
        network.OwnerTransfer = (OwnerTransfer)(settings["OwnerTransfer"]?.ByteValue ?? (byte)OwnerTransfer.Fixed);
        network.Orphaned = (NetworkOrphaned)(settings["Orphaned"]?.ByteValue ?? (byte)NetworkOrphaned.Destroy);
        network.Flags = (NetworkFlags)(settings["Flags"]?.ByteValue ?? (byte)NetworkFlags.None);
    }

    /// <summary>
    ///     For writing what clients receive: a reference to anything <see cref="NetworkMode.Never" /> is linked to
    ///     nothing instead of written by value, so a field pointing at it can't carry it along. Everything else
    ///     goes to <paramref name="inner" />, or is written as usual without one.
    /// </summary>
    internal sealed class SnapshotReferences(IExternalReferenceResolver? inner = null) : IExternalReferenceResolver
    {
        public object? GetReferenceKey(object value)
        {
            GameObject? owner = value switch
            {
                GameObject go => go,
                Component component => component.GameObject,
                Transform transform => transform.GameObject,
                _ => null
            };

            // Guid.Empty names no object, so it reads back as null without revealing which one it was.
            return owner.IsValid() && NetworkAccessor.IsExcludedFromNetwork(owner!)
                ? Guid.Empty
                : inner?.GetReferenceKey(value);
        }

        public object? ResolveReference(object key, Type targetType) => inner?.ResolveReference(key, targetType);
    }
}
