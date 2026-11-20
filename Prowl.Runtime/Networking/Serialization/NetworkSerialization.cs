// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Vector;
using Prowl.Courier;

namespace Prowl.Runtime.Networking;

internal static class NetworkSerialization
{
    [ThreadStatic] internal static bool WritingSnapshot;

    internal static bool Include(GameObject go) =>
        !WritingSnapshot || !NetworkAccessor.ServerOnlyOf(go);

    internal static void Write(GameObject go, EchoObject output)
    {
        NetworkAccessor? network = go._network;
        if (network == null || (!network.Networked && !network.ServerOnly &&
                                network.Orphaned == NetworkOrphaned.Destroy &&
                                network.Visibility == NetworkVisibility.Default && network.CullDistance == 0 &&
                                network.Groups.IsEmpty && !network.HasComponentIntervals))
        {
            return;
        }

        EchoObject settings = EchoObject.NewCompound(6);
        if (network.Networked)
        {
            settings.Add("Networked", new EchoObject(true));
        }

        if (network.ServerOnly)
        {
            settings.Add("ServerOnly", new EchoObject(true));
        }

        settings.Add("Orphaned", new EchoObject((byte)network.Orphaned));
        if (network.Visibility != NetworkVisibility.Default)
        {
            settings.Add("Visibility", new EchoObject((byte)network.Visibility));
        }

        if (network.CullDistance != 0)
        {
            settings.Add("CullDistance", new EchoObject(network.CullDistance));
        }

        if (!network.Groups.IsEmpty)
        {
            settings.Add("Groups", new EchoObject(network.Groups.Mask));
        }

        if (network.HasComponentIntervals)
        {
            // By each component's place among the object's own, which is how they are stored too.
            EchoObject intervals = EchoObject.NewList();
            List<Component> components = [.. go.GetComponents<Component>()];
            for (int i = 0; i < components.Count; i++)
            {
                int interval = NetworkAccessor.SendIntervalOf(components[i]);
                if (interval > 1)
                {
                    EchoObject entry = EchoObject.NewCompound(2);
                    entry.Add("Index", new EchoObject(i));
                    entry.Add("Ticks", new EchoObject((byte)interval));
                    intervals.ListAdd(entry);
                }
            }

            settings.Add("ComponentIntervals", intervals);
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

            existing.Networked = false;
            existing.ServerOnly = false;
            existing.ClearComponentIntervals();
            existing.Orphaned = NetworkOrphaned.Destroy;
            existing.Visibility = NetworkVisibility.Default;
            existing.CullDistance = 0;
            existing.Groups = NetworkGroups.None;
            return;
        }

        NetworkAccessor network = go.Network;
        // Scenes and prefabs saved before Networked and ServerOnly hold the old mode: 1 was Never, 2 was Object.
        byte stored = settings["Mode"]?.ByteValue ?? 0;
        network.Networked = settings["Networked"]?.BoolValue ?? stored == 2;
        network.ServerOnly = settings["ServerOnly"]?.BoolValue ?? stored == 1;
        network.Orphaned = (NetworkOrphaned)(settings["Orphaned"]?.ByteValue ?? (byte)NetworkOrphaned.Destroy);
        network.Visibility = (NetworkVisibility)(settings["Visibility"]?.ByteValue ?? (byte)NetworkVisibility.Default);
        float cullDistance = settings["CullDistance"]?.FloatValue ?? 0;
        network.CullDistance = float.IsFinite(cullDistance) && cullDistance > 0 ? cullDistance : 0;
        network.Groups = new NetworkGroups(settings["Groups"]?.ULongValue ?? 0);
        network.ClearComponentIntervals();
        if (settings["ComponentIntervals"]?.List is { } intervals)
        {
            // The object's components are read after this, so the intervals wait for them by index.
            List<(int Index, int Ticks)> pending = new();
            foreach (EchoObject entry in intervals)
            {
                int index = entry["Index"]?.IntValue ?? -1;
                int ticks = entry["Ticks"]?.ByteValue ?? 1;
                if (index >= 0 && ticks > 1)
                {
                    pending.Add((index, ticks));
                }
            }

            network.SetStoredIntervals(pending);
        }
    }

    /// <summary>
    ///     For writing a tree clients receive: a reference to anything server only is written as nothing, so a field
    ///     pointing at it can't carry it along. Everything else goes to <paramref name="inner" />, or is written as
    ///     usual without one.
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
            }

            return inner?.GetReferenceKey(value);
        }

        public object? ResolveReference(object key, Type targetType) => inner?.ResolveReference(key, targetType);
    }
}
