// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

using Prowl.Recast.Core.Numerics;
using Prowl.Recast.Detour;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// A registered navmesh inside a <see cref="NavMeshWorld"/>: the instantiated Detour navmesh,
/// its query pool, and the lock that lets queries run from any thread while tile mutations
/// (rebakes, partial rebuilds) exclude them. Obtained from
/// <see cref="NavMeshWorld.AddNavMeshData"/>; advanced users can reach the raw Detour objects
/// through <see cref="NativeNavMesh"/>.
/// </summary>
public sealed class NavMeshInstance
{
    internal NavMeshData Data;
    internal DtNavMesh Mesh;
    internal readonly ReaderWriterLockSlim Lock = new(LockRecursionPolicy.NoRecursion);
    internal readonly ConcurrentBag<DtNavMeshQuery> QueryPool = new();

    // Set when work is queued into the cache (an obstacle request, a tile swap), cleared once
    // the pump drains it. Only flagged instances are pumped, so a freshly registered instance
    // (every tile seeded synchronously) starts clean, and a surface nothing ever carves costs
    // nothing per frame. Main-thread only, like registration itself.
    internal bool CachePending;

    internal NavMeshInstance(NavMeshData data, Prowl.Recast.Detour.TileCache.DtTileCache tileCache,
        NavMeshTileBuilder.ProwlTileCacheMeshProcess tileCacheLinks)
    {
        Data = data;
        Mesh = tileCache.GetNavMesh();
        TileCache = tileCache;
        TileCacheLinks = tileCacheLinks;
    }

    /// <summary>The link set this instance's cache re-injects whenever it rebuilds a tile.
    /// Mutate under the instance write lock and rebuild the affected tiles afterwards — see
    /// <see cref="NavMeshSurface.RebuildLinkTiles"/>.</summary>
    internal NavMeshTileBuilder.ProwlTileCacheMeshProcess TileCacheLinks { get; }

    /// <summary>The TileCache backing this instance, for advanced use. <see cref="NavMeshWorld.Update"/>
    /// pumps its incremental tile rebuilds, but only for instances known to have pending work, and
    /// DtTileCache cannot be asked whether it has any. Carve through <see cref="AddBoxObstacle"/> and
    /// friends, or mutate through <see cref="NavMeshWorld.MutateTileCache"/>. Code that queues on this
    /// handle directly must call <see cref="MarkCachePending"/> or the request waits forever.</summary>
    public Prowl.Recast.Detour.TileCache.DtTileCache TileCache { get; }

    /// <summary>Tell the pump this cache has work waiting. Only needed after queuing on
    /// <see cref="TileCache"/> directly. Main thread only.</summary>
    public void MarkCachePending() => CachePending = true;

    /// <summary>How many obstacles this navmesh may carve at once.</summary>
    public int MaxObstacles => TileCache.GetParams().maxObstacles;

    /// <summary>Queue an upright cylinder carve standing on <paramref name="basePosition"/>. The tiles
    /// rebuild over the following frames. Zero when the obstacle pool is full. Main thread only.</summary>
    public long AddCylinderObstacle(Float3 basePosition, float radius, float height)
    {
        long obstacleRef = TileCache.AddObstacle(ToRc(basePosition), radius, height);
        if (obstacleRef != 0) CachePending = true;
        return obstacleRef;
    }

    /// <summary>Queue a box carve rotated about the vertical axis by <paramref name="yawRadians"/>.
    /// Zero when the obstacle pool is full. Main thread only.</summary>
    public long AddBoxObstacle(Float3 center, Float3 halfExtents, float yawRadians)
    {
        long obstacleRef = TileCache.AddBoxObstacle(ToRc(center), ToRc(halfExtents), yawRadians);
        if (obstacleRef != 0) CachePending = true;
        return obstacleRef;
    }

    /// <summary>Queue the removal of a carve returned by one of the add methods. Main thread only.</summary>
    public void RemoveObstacle(long obstacleRef)
    {
        if (obstacleRef == 0) return;
        TileCache.RemoveObstacle(obstacleRef);
        CachePending = true;
    }

    private static RcVec3f ToRc(Float3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

    /// <summary>The agent type this navmesh was built for.</summary>
    public NavMeshAgentTypeId AgentTypeId => Data.Settings.AgentTypeId;

    /// <summary>The asset this instance was created from.</summary>
    public NavMeshData NavMeshData => Data;

    /// <summary>The underlying Detour navmesh, owned by <see cref="TileCache"/>. Advanced use;
    /// mutating it directly bypasses the query locking and desyncs it from the cache that built
    /// it — prefer <see cref="NavMeshWorld.MutateTileCache"/> for tile changes.</summary>
    public DtNavMesh NativeNavMesh => Mesh;

    // The mesh's traversable off-mesh connections by link id, built lazily and invalidated on
    // mutation — turns per-link lookups (every NavMeshLink at scene load, and again per frame
    // while one is selected) into O(1) after a single O(tiles) pass. A link Detour could not
    // attach is absent, so "contains" means usable rather than merely present, and a catch-up
    // retries one that failed instead of taking the stub for success. Main thread only.
    private Dictionary<int, NavMeshConnection>? _connections;

    internal void InvalidateLinkIds() => _connections = null;

    // ReaderWriterLockSlim owns kernel wait handles that only Dispose releases, and disposing one
    // while a thread is inside it throws on that thread. Users: one for the registration plus one
    // per lease or mutation; the last out disposes, and a count that reached zero cannot be revived,
    // so a worker can never enter a disposed lock.
    private int _users = 1;

    private volatile bool _retired;

    /// <summary>Unregistered: nothing queued against this instance can still land.</summary>
    internal bool Retired => _retired;

    internal bool TryAcquire()
    {
        if (_retired) return false;
        int users = Volatile.Read(ref _users);
        while (users > 0)
        {
            int seen = Interlocked.CompareExchange(ref _users, users + 1, users);
            if (seen == users) return true;
            users = seen;
        }
        return false;
    }

    internal void Release()
    {
        if (Interlocked.Decrement(ref _users) == 0)
            Lock.Dispose();
    }

    /// <summary>Unregistration, from the lock's point of view: stop admitting queries, wait out
    /// the ones already inside, poison the pool, and drop the registration's own hold.</summary>
    internal void Retire()
    {
        _retired = true;

        Lock.EnterWriteLock();
        QueryPool.Clear();
        Lock.ExitWriteLock();

        Release();
    }

    /// <summary>Whether the mesh holds a traversable connection stamped with the given link id
    /// (see <see cref="NavMeshLink.LinkId"/>). Main thread.</summary>
    public bool ContainsLinkId(int linkId) => Connections.ContainsKey(linkId);

    /// <summary>The connection the mesh holds for a link id — where its endpoints actually
    /// snapped to, which is not necessarily where the component put them. False when the link
    /// never attached. Main thread.</summary>
    public bool TryGetConnection(int linkId, out NavMeshConnection connection)
        => Connections.TryGetValue(linkId, out connection);

    private Dictionary<int, NavMeshConnection> Connections
    {
        get
        {
            if (_connections != null) return _connections;

            _connections = [];
            for (int t = 0; t < Mesh.GetMaxTiles(); t++)
            {
                DtMeshTile? tile = Mesh.GetTile(t);
                if (tile?.data?.offMeshCons == null) continue;
                foreach (DtOffMeshConnection con in tile.data.offMeshCons)
                    if (con.userId != 0 && NavMeshConnection.TryFrom(tile, con, out NavMeshConnection connection))
                        _connections[con.userId] = connection;
            }
            return _connections;
        }
    }
}
