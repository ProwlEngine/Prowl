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

    /// <summary>Runs on everyone who has the object, the host included, as Mirror's ClientRpc. Only the host calls it.</summary>
    public sealed class BroadcastAttribute(RpcFlags flags = RpcFlags.None) : RpcAttribute(RpcMode.Broadcast, flags);

    /// <summary>
    ///     Runs on the host only, as Mirror's Command. The one kind a client calls: on an object it controls, or on any
    ///     it has with <see cref="RpcFlags.AnyCaller" />.
    /// </summary>
    public sealed class HostAttribute(RpcFlags flags = RpcFlags.None) : RpcAttribute(RpcMode.Host, flags);

    /// <summary>Runs on whoever controls the object: its owner, or the host when it has none. Only the host calls it.</summary>
    public sealed class OwnerAttribute(RpcFlags flags = RpcFlags.None) : RpcAttribute(RpcMode.Owner, flags);

    /// <summary>
    ///     Runs on the one peer its first parameter, a <c>NetPeer</c>, names, as Mirror's TargetRpc. Only the host calls
    ///     it; on the peer it runs on, that parameter is the peer itself.
    /// </summary>
    public sealed class TargetAttribute(RpcFlags flags = RpcFlags.None) : RpcAttribute(RpcMode.Target, flags);
}
