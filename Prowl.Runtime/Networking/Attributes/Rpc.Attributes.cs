// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.Networking;

public static partial class Rpc
{
    /// <summary>Base for the RPC attributes.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public abstract class RpcAttribute : Attribute
    {
        private protected RpcAttribute(RpcMode mode, RpcFlags flags)
        {
            Mode = mode;
            Flags = flags;
        }

        public RpcMode Mode { get; }

        public RpcFlags Flags { get; }
    }

    /// <summary>Runs on everyone, the caller included.</summary>
    public sealed class BroadcastAttribute(RpcFlags flags = RpcFlags.None) : RpcAttribute(RpcMode.Broadcast, flags);

    /// <summary>Runs on the host only.</summary>
    public sealed class HostAttribute(RpcFlags flags = RpcFlags.None) : RpcAttribute(RpcMode.Host, flags);

    /// <summary>Runs on whoever controls the object: its owner, or the host when it has none.</summary>
    public sealed class OwnerAttribute(RpcFlags flags = RpcFlags.None) : RpcAttribute(RpcMode.Owner, flags);
}
