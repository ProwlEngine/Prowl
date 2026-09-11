using System.Collections.Generic;

using Prowl.Recast.Core;

namespace Prowl.Recast.Detour.TileCache
{
    /// The working set one tile build needs to itself. A cache holds one of these for its own
    /// builds; a caller meshing several tiles at once (registration, which starts from a full set
    /// of compressed layers) gives each thread its own and hands them to
    /// <see cref="DtTileCache.BuildTileMeshData"/>.
    ///
    /// Everything here is either stateful per build (the context's timers, the allocator) or a
    /// cache that only pays off across consecutive builds (the neighbour layers). The compressor
    /// and the obstacle list are NOT here: those are read-only during a build and shared.
    public sealed class DtTileCacheBuildScratch
    {
        /// Bounded rather than grown without limit: a world of thousands of tiles would otherwise
        /// hold every one of them decompressed for the life of the cache.
        private const int MaxCachedBorderLayers = 256;

        /// A context allocates thread-local timer state, so it cannot be shared across threads.
        internal readonly RcContext Ctx = new RcContext();

        internal readonly DtTileCacheAlloc Alloc = new DtTileCacheAlloc();

        /// Neighbour layers a tile's border reads, by tile ref. A bake meshes every tile and each
        /// reads its eight neighbours, so without this every layer is decompressed nine times over.
        /// Dropped whenever a tile or an obstacle changes, since both change what the layers say.
        internal readonly Dictionary<long, DtTileCacheLayer> BorderLayers = new Dictionary<long, DtTileCacheLayer>();

        internal readonly List<long> NeighbourRefs = new List<long>();

        private DtTileCache m_owner;
        private int m_epoch = -1;

        /// Drops the neighbour layers when they can no longer be trusted: a different cache (refs
        /// are per-cache, so one cache's ref names another's tile), or the same cache after a tile
        /// or obstacle changed what its layers say. Without this, holding a scratch across an
        /// AddTile or a carve would silently build seams from the layers as they used to be.
        internal void SyncTo(DtTileCache owner, int epoch)
        {
            if (ReferenceEquals(m_owner, owner) && m_epoch == epoch)
            {
                return;
            }

            BorderLayers.Clear();
            m_owner = owner;
            m_epoch = epoch;
        }

        internal void RememberBorderLayer(long refs, DtTileCacheLayer layer)
        {
            if (BorderLayers.Count >= MaxCachedBorderLayers)
            {
                BorderLayers.Clear();
            }

            BorderLayers[refs] = layer;
        }
    }
}
