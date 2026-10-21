// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.ComponentModel;

using Prowl.Runtime.Diagnostics;
using Prowl.Runtime.Networking;
using Prowl.Warp;

namespace Prowl.Runtime;

// The members below starting with __ are what Prowl's script compiler generates [Sync] properties and
// RPCs into, each with its descriptor (see NetworkMembers). They are not meant to be called by hand.
public abstract partial class Component
{
    /// <summary>
    /// True when this runs on a peer that doesn't control the object: it's driven from elsewhere, so
    /// don't move it or change its [Sync] properties here. False without a network session.
    /// </summary>
    public bool IsProxy => GameObject.IsProxy;

    /// <summary>
    /// Generated [Sync] setter: stores the value if this peer may change it, queues it to be sent, and runs
    /// the property's [Change] hook.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected void __Sync_Set<T>(ref T field, T value, SyncMember<T> member)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        T old = field;
        if (Networking.Networking.IsActive)
        {
            // A proxy's value belongs to whoever controls it, and the next update would undo the change anyway.
            using FrameStatScope stats = FrameStats.Network.Scope();
            if (!Networking.Networking.HasControl(this, member.Flags))
                return;

            field = value;
            Networking.Networking.MarkSyncDirty(this, member.Slot);
        }
        else
        {
            field = value;
        }

        member.Changed(this, old, value);
    }

    /// <summary>Generated getter of a [Sync] network collection: ties the collection to this component's property.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected TCollection __Sync_Collection<TCollection>(TCollection collection, SyncMember member) where TCollection : NetCollection
    {
        collection.Bind(this, member);
        return collection;
    }

    /// <summary>
    /// Generated RPC prologue. True if the call has to go over the network, with a writer for its
    /// arguments; false if the body should just run here (no session, an incoming call, or this peer is
    /// the only one meant to run it).
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected bool __Rpc_Begin(RpcMember member, out NetWriter writer) => Networking.Networking.BeginRpc(this, member, out writer);

    /// <summary>Generated: sends the call. True if the body should also run here.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected bool __Rpc_Send(RpcMember member, NetWriter writer) => Networking.Networking.SendRpc(this, member, writer);

    /// <summary>Generated: an incoming RPC's arguments are read, so what throws from here on is the body's.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected static void __Rpc_ArgumentsRead() => Networking.Networking.RpcArgumentsRead();
}
