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

        /// <summary>
        ///     The channel the call is sent on. Calls stay in order only with traffic on the same channel; the engine
        ///     uses channel 0, the default.
        /// </summary>
        /// <remarks>
        ///     Only reliable calls use it. With <see cref="RpcFlags.Unreliable" /> it is ignored and the call goes on
        ///     channel 0, since unreliable calls have no order to keep.
        /// </remarks>
        public byte Channel { get; set; }
    }

    /// <summary>Runs on everyone, the caller included.</summary>
    public sealed class BroadcastAttribute(RpcFlags flags = RpcFlags.None) : RpcAttribute(RpcMode.Broadcast, flags);

    /// <summary>Runs on the host only.</summary>
    public sealed class HostAttribute(RpcFlags flags = RpcFlags.None) : RpcAttribute(RpcMode.Host, flags);

    /// <summary>Runs on whoever controls the object: its owner, or the host when it has none.</summary>
    public sealed class OwnerAttribute(RpcFlags flags = RpcFlags.None) : RpcAttribute(RpcMode.Owner, flags);
}
