using System;
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

        /// The region partitioner.s working buffers, a quarter of a build.s allocation on their own.
        internal readonly RcRegionScratch Regions = new RcRegionScratch();

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

        // Grow-only working buffers for the biggest arrays a tile build makes. They are sized by the
        // tile's cell and span counts, which barely move from tile to tile, so after the first build
        // a scratch stops allocating them: measured at 28% of a build's total allocation, and every
        // carve pays it again. Only the used prefix of each is ever read — nothing on this path
        // iterates one by Length — so an oversized buffer is as good as an exact one.
        private int[] m_gridHeights = Array.Empty<int>();
        private int[] m_gridAreas = Array.Empty<int>();
        private RcCompactCell[] m_chfCells = Array.Empty<RcCompactCell>();
        private RcCompactSpan[] m_chfSpans = Array.Empty<RcCompactSpan>();
        private int[] m_chfAreas = Array.Empty<int>();
        private RcCompactHeightfield m_chf;

        internal int[] GridHeights(int size) => Grow(ref m_gridHeights, size);

        internal int[] GridAreas(int size) => Grow(ref m_gridAreas, size);

        internal RcCompactCell[] ChfCells(int size) => Grow(ref m_chfCells, size);

        internal RcCompactSpan[] ChfSpans(int size) => Grow(ref m_chfSpans, size);

        internal int[] ChfAreas(int size) => Grow(ref m_chfAreas, size);

        /// The heightfield object itself, reused. Every field the tile-cache path reads is written
        /// before it is read — the converter sets all of them bar bmin/bmax, which stay at zero as
        /// they did on a fresh one, and the distance field and regions assign theirs each build.
        internal RcCompactHeightfield Chf
        {
            get
            {
                if (m_chf == null)
                {
                    m_chf = new RcCompactHeightfield();
                }

                return m_chf;
            }
        }

        private static T[] Grow<T>(ref T[] buffer, int size)
        {
            if (buffer.Length < size)
            {
                buffer = new T[size];
            }

            return buffer;
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
