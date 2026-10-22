// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.IO;

using Prowl.Warp;

namespace Prowl.Runtime.Networking;

// Only selected peer ids travel to the host. Predicates stay on the calling peer.
internal sealed class RpcRecipients(HashSet<int> ids)
{
    internal HashSet<int> Ids { get; } = ids;

    internal static bool Includes(RpcRecipients? recipients, NetPeer peer) =>
        recipients == null || recipients.Ids.Contains(peer.Id);

    internal static void Write(NetWriter writer, RpcRecipients? recipients)
    {
        if (recipients == null)
        {
            return;
        }

        writer.WriteVarUInt32((uint)recipients.Ids.Count);
        foreach (int id in recipients.Ids)
        {
            writer.WriteVarUInt32((uint)id);
        }
    }

    internal static RpcRecipients? Read(ref NetReader reader)
    {
        if (reader.IsAtEnd)
        {
            return null;
        }

        uint count = reader.ReadVarUInt32();
        if (count > (uint)Networking.Session!.Options.MaxPeers + 1)
        {
            throw new InvalidDataException("RPC recipient list exceeds the session peer limit.");
        }

        HashSet<int> ids = new();
        for (uint i = 0; i < count; i++)
        {
            if (!ids.Add(checked((int)reader.ReadVarUInt32())))
            {
                throw new InvalidDataException("RPC recipient list contains duplicate peers.");
            }
        }

        if (!reader.IsAtEnd)
        {
            throw new InvalidDataException("RPC recipient list contains trailing bytes.");
        }

        return new RpcRecipients(ids);
    }
}
