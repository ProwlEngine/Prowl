// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Courier;

namespace Prowl.Runtime.Networking;

internal static class NetworkSerialization
{
    internal static void Write(GameObject go, EchoObject output)
    {
        NetworkAccessor? network = go._network;
        if (network == null || (!network.Networked && !network.ServerOnly &&
                                network.Orphaned == NetworkOrphaned.Destroy &&
                                network.Visibility == NetworkVisibility.Default))
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

            existing.LoadSharing(false, false);
            existing.Orphaned = NetworkOrphaned.Destroy;
            existing.Visibility = NetworkVisibility.Default;
            return;
        }

        NetworkAccessor network = go.Network;
        // Scenes and prefabs saved before Networked and ServerOnly hold the old mode: 1 was Never, 2 was Object.
        byte stored = settings["Mode"]?.ByteValue ?? 0;
        network.LoadSharing(settings["Networked"]?.BoolValue ?? stored == 2, settings["ServerOnly"]?.BoolValue ?? stored == 1);
        network.Orphaned = (NetworkOrphaned)(settings["Orphaned"]?.ByteValue ?? (byte)NetworkOrphaned.Destroy);
        // Scenes and prefabs saved with the old Manual visibility, 3, hide the object as it did.
        byte visibility = settings["Visibility"]?.ByteValue ?? (byte)NetworkVisibility.Default;
        network.Visibility = visibility == 3 ? NetworkVisibility.ForceHidden : (NetworkVisibility)visibility;
        // Older versions saved each behaviour's send interval here, in ticks. The components are read after this, so
        // the intervals wait for them.
        if (settings["ComponentIntervals"]?.List is { } intervals)
        {
            List<(Guid Component, int Index, int Ticks)> pending = new();
            foreach (EchoObject entry in intervals)
            {
                int index = entry["Index"]?.IntValue ?? -1;
                int ticks = entry["Ticks"]?.ByteValue ?? 1;
                Guid.TryParse(entry["Component"]?.StringValue, out Guid component);
                if (index >= 0 && ticks > 1)
                {
                    pending.Add((component, index, ticks));
                }
            }

            network.SetStoredIntervals(pending);
        }
    }
}
