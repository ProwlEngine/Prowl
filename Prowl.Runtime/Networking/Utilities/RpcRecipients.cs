// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Warp;

namespace Prowl.Runtime.Networking.Utilities;

// Only selected peer ids travel to the host. Predicates stay on the calling peer.
internal sealed class RpcRecipients(HashSet<int> ids)
{
    internal HashSet<int> Ids { get; } = ids;

    internal static bool Includes(RpcRecipients? recipients, NetPeer peer) =>
        recipients == null || recipients.Ids.Contains(peer.Id);
}
