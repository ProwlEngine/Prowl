// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Runtime.Tasks;
using Prowl.Warp;

namespace Prowl.Runtime.Networking;

public static partial class Rpc
{
    [ThreadStatic] private static FilterScope? s_filter;

    /// <summary>Restricts RPC recipients, including local execution, to matching peers until disposed.
    /// Scopes nest by intersection, must be disposed in reverse order, and must not cross an await.</summary>
    public static IDisposable FilterInclude(Func<NetPeer, bool> predicate) => new FilterScope(predicate, include: true);

    /// <summary>Excludes matching peers from RPC recipients, including local execution, until disposed.
    /// Applies to calls made in this scope; it does not change ordinary state replication or RPC permissions.</summary>
    public static IDisposable FilterExclude(Func<NetPeer, bool> predicate) => new FilterScope(predicate, include: false);

    internal static bool HasFilters => s_filter != null;

    internal static RpcRecipients? CaptureRecipients(NetSession session)
    {
        FilterScope? filter = s_filter;
        if (filter == null) return null;
        MainThreadContext.AssertMainThread();
        HashSet<int> recipients = new();
        foreach (NetPeer peer in session.Peers.ToArray())
        {
            bool allowed = true;
            for (FilterScope? current = filter; current != null; current = current.Parent)
            {
                if (current.Predicate(peer) != current.Include)
                {
                    allowed = false;
                    break;
                }
            }
            if (allowed) recipients.Add(peer.Id);
        }
        if (Networking.Session != session)
            throw new InvalidOperationException("The session changed while evaluating RPC recipient filters.");
        return new RpcRecipients(recipients);
    }

    private sealed class FilterScope : IDisposable
    {
        internal readonly FilterScope? Parent;
        internal readonly Func<NetPeer, bool> Predicate;
        internal readonly bool Include;
        private bool _disposed;

        internal FilterScope(Func<NetPeer, bool> predicate, bool include)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            MainThreadContext.AssertMainThread();
            Parent = s_filter;
            Predicate = predicate;
            Include = include;
            s_filter = this;
        }

        public void Dispose()
        {
            if (_disposed) return;
            MainThreadContext.AssertMainThread();
            if (s_filter != this) throw new InvalidOperationException("RPC filter scopes must be disposed in reverse order.");
            s_filter = Parent;
            _disposed = true;
        }
    }
}
