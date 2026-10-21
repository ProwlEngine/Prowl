// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Networking;

namespace NetworkingShowcase;

/// <summary>
/// Which station the host is showing, as a [Sync] value only the host sets. Clients go where it goes, so the
/// host walks everyone through the showcase.
/// </summary>
public class Tour : Component
{
    [Sync(SyncFlags.FromHost)] public int Station { get; set; }
}
