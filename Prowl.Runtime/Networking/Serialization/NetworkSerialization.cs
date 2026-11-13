// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Vector;
using Prowl.Courier;

namespace Prowl.Runtime.Networking;

/// <summary>A [Sync] value written to clients, with the networked roots its references were masked for, or named.</summary>
internal readonly record struct WrittenValue(Component Component, uint Slot, GameObject[] Masked, GameObject[] Linked);

internal static class NetworkSerialization
{
    [ThreadStatic] internal static bool WritingSnapshot;

    // Networked roots the receiver of what is being written does not have.
    [ThreadStatic] internal static HashSet<GameObject>? HiddenRoots;

    // Host: the clients receiving what is being written, when it goes to some but not all of them.
    [ThreadStatic] internal static NetPeer? Receiver;
    [ThreadStatic] internal static IReadOnlyList<NetPeer>? Receivers;

    // Host: [Sync] values written with references, for the snapshot system to remember per client.
    [ThreadStatic] internal static List<WrittenValue>? WrittenValues;

    // Host: the networked roots the [Sync] value being written was masked for, or named, while collecting.
    [ThreadStatic] internal static bool CollectingRoots;
    [ThreadStatic] private static List<GameObject>? s_maskedRoots, s_linkedRoots;

    // Host: secret roots a link was emptied for although some receivers have them, while splitting.
    [ThreadStatic] private static bool s_splitting;
    [ThreadStatic] internal static List<GameObject>? PartlyHidden;

    // Network collection changes are written once for everyone, so their references go unmasked.
    [ThreadStatic] internal static bool KeepReferences;

    internal static List<GameObject> MaskedRoots => s_maskedRoots ??= new List<GameObject>();

    internal static List<GameObject> LinkedRoots => s_linkedRoots ??= new List<GameObject>();

    internal static bool Include(GameObject go) =>
        !WritingSnapshot || (NetworkAccessor.ModeOf(go) != NetworkMode.Never && !IsHiddenRoot(go));

    internal static bool IsHiddenRoot(GameObject go) => HiddenRoots?.Contains(go) == true;

    internal static bool InHiddenTree(GameObject go) =>
        HiddenRoots is { Count: > 0 } roots && NetworkAccessor.RootOf(go) is { } root && roots.Contains(root);

    /// <summary>True if a client receiving what is being written does not have the networked object <paramref name="go" /> is in.</summary>
    internal static bool HiddenFromReceivers(GameObject go)
    {
        if (InHiddenTree(go))
        {
            return true;
        }

        if ((Receiver == null && Receivers == null) || Networking.System is not { Restricted.Count: > 0 } system ||
            system.FindEntry(go)?.HiddenFrom is not { Count: > 0 } hidden)
        {
            return false;
        }

        if (Receiver != null)
        {
            return hidden.Contains(Receiver.Id);
        }

        for (int i = 0; i < Receivers!.Count; i++)
        {
            if (hidden.Contains(Receivers[i].Id))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     True if the networked object <paramref name="go" /> is in is kept from clients by a rule, not by distance:
    ///     Manual, OwnerOnly, or hidden from someone with HideFrom. Links to those never carry its identifier.
    /// </summary>
    internal static bool IsSecret(GameObject go) =>
        NetworkAccessor.RootOf(go) is { } root &&
        (root.Network.Visibility is NetworkVisibility.Manual or NetworkVisibility.OwnerOnly ||
         Networking.System?.IsForcedHidden(root) == true);

    /// <summary>The identifier to write for an engine reference: Guid.Empty for an object a receiver lacks.</summary>
    internal static Guid ReferenceId(GameObject go, Guid id)
    {
        if (KeepReferences || id == Guid.Empty)
        {
            return id;
        }

        bool hidden = HiddenFromReceivers(go);
        if (CollectingRoots && Networking.System is { IsHost: true } system && system.FindEntry(go) is { GameObject: var root })
        {
            List<GameObject> roots = hidden ? MaskedRoots : LinkedRoots;
            if (!roots.Contains(root))
            {
                roots.Add(root);
            }
        }

        return hidden ? Guid.Empty : id;
    }

    // Notes a secret root a link was emptied for, when some of the receivers do have it.
    private static void NotePartlyHidden(GameObject go)
    {
        if (!s_splitting || Receivers is not { Count: > 1 } receivers || Networking.System is not { } system ||
            system.FindEntry(go) is not { } entry)
        {
            return;
        }

        for (int i = 0; i < receivers.Count; i++)
        {
            if (NetworkSystem.Sees(entry, receivers[i]))
            {
                PartlyHidden ??= new List<GameObject>();
                if (!PartlyHidden.Contains(entry.GameObject))
                {
                    PartlyHidden.Add(entry.GameObject);
                }

                return;
            }
        }
    }

    /// <summary>
    ///     Sets who what is written goes to, and where its [Sync] values with references are collected, until disposed.
    ///     With <paramref name="split" />, secret links emptied for only some receivers are noted in <see cref="PartlyHidden" />.
    /// </summary>
    internal readonly ref struct WritingFor
    {
        private readonly NetPeer? _receiver;
        private readonly IReadOnlyList<NetPeer>? _receivers;
        private readonly List<WrittenValue>? _values;
        private readonly bool _splitting;
        private readonly List<GameObject>? _partlyHidden;

        internal WritingFor(NetPeer? receiver, IReadOnlyList<NetPeer>? receivers, List<WrittenValue>? values, bool split = false)
        {
            (_receiver, _receivers, _values, _splitting, _partlyHidden) = (Receiver, Receivers, WrittenValues, s_splitting, PartlyHidden);
            (Receiver, Receivers, WrittenValues, s_splitting, PartlyHidden) = (receiver, receivers, values, split, null);
        }

        public void Dispose() =>
            (Receiver, Receivers, WrittenValues, s_splitting, PartlyHidden) = (_receiver, _receivers, _values, _splitting, _partlyHidden);
    }

    internal static void Write(GameObject go, EchoObject output)
    {
        NetworkAccessor? network = go._network;
        if (network == null || (network.Mode == NetworkMode.Snapshot && network.OwnerTransfer == OwnerTransfer.Fixed &&
                                network.Orphaned == NetworkOrphaned.Destroy && network.Flags == NetworkFlags.None &&
                                network.Visibility == NetworkVisibility.Default && network.CullDistance == 0))
        {
            return;
        }

        EchoObject settings = EchoObject.NewCompound(6);
        settings.Add("Mode", new EchoObject((byte)network.Mode));
        settings.Add("OwnerTransfer", new EchoObject((byte)network.OwnerTransfer));
        settings.Add("Orphaned", new EchoObject((byte)network.Orphaned));
        settings.Add("Flags", new EchoObject((byte)network.Flags));
        if (network.Visibility != NetworkVisibility.Default)
        {
            settings.Add("Visibility", new EchoObject((byte)network.Visibility));
        }

        if (network.CullDistance != 0)
        {
            settings.Add("CullDistance", new EchoObject(network.CullDistance));
        }

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
            existing.Visibility = NetworkVisibility.Default;
            existing.CullDistance = 0;
            return;
        }

        NetworkAccessor network = go.Network;
        network.Mode = (NetworkMode)(settings["Mode"]?.ByteValue ?? (byte)NetworkMode.Snapshot);
        network.OwnerTransfer = (OwnerTransfer)(settings["OwnerTransfer"]?.ByteValue ?? (byte)OwnerTransfer.Fixed);
        network.Orphaned = (NetworkOrphaned)(settings["Orphaned"]?.ByteValue ?? (byte)NetworkOrphaned.Destroy);
        network.Flags = (NetworkFlags)(settings["Flags"]?.ByteValue ?? (byte)NetworkFlags.None);
        network.Visibility = (NetworkVisibility)(settings["Visibility"]?.ByteValue ?? (byte)NetworkVisibility.Default);
        float cullDistance = settings["CullDistance"]?.FloatValue ?? 0;
        network.CullDistance = float.IsFinite(cullDistance) && cullDistance > 0 ? cullDistance : 0;
    }

    /// <summary>
    ///     For writing what clients receive: a reference to anything <see cref="NetworkMode.Never" />, or in a networked
    ///     object a receiver lacks, is linked instead of written by value, so a field pointing at it can't carry it along.
    ///     The link names the object only when distance is all that keeps it away, so the client can fill it in once the
    ///     object arrives. Everything else goes to <paramref name="inner" />, or is written as usual without one.
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

            if (owner.IsValid())
            {
                // Guid.Empty names no object, so it reads back as null without revealing which one it was.
                if (NetworkAccessor.IsExcludedFromNetwork(owner!))
                {
                    return Guid.Empty;
                }

                if (HiddenFromReceivers(owner!))
                {
                    if (!IsSecret(owner!))
                    {
                        return value is Component part ? part.Identifier : owner!.Identifier;
                    }

                    NotePartlyHidden(owner!);
                    return Guid.Empty;
                }
            }

            return inner?.GetReferenceKey(value);
        }

        public object? ResolveReference(object key, Type targetType) => inner?.ResolveReference(key, targetType);
    }
}
