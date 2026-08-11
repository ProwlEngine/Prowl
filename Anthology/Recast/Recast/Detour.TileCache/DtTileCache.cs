/*
Copyright (c) 2009-2010 Mikko Mononen memon@inside.org
recast4j copyright (c) 2015-2019 Piotr Piastucki piotr@jtilia.org
Prowl.Recast Copyright (c) 2023-2024 Choi Ikpil ikpil@naver.com

This software is provided 'as-is', without any express or implied
warranty.  In no event will the authors be held liable for any damages
arising from the use of this software.
Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:
1. The origin of this software must not be misrepresented; you must not
 claim that you wrote the original software. If you use this software
 in a product, an acknowledgment in the product documentation would be
 appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be
 misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.
*/

using System;
using System.Collections.Generic;
using Prowl.Recast.Core;
using Prowl.Recast.Core.Numerics;
using Prowl.Recast.Detour.TileCache.Io;

namespace Prowl.Recast.Detour.TileCache
{
    using static DtDetour;

    public class DtTileCache
    {
        private int m_tileLutSize; // < Tile hash lookup size (must be pot).
        private int m_tileLutMask; // < Tile hash lookup mask.
        private readonly DtCompressedTile[] m_posLookup; // < Tile hash lookup.

        private DtCompressedTile m_nextFreeTile; // < Freelist of tiles.
        private readonly DtCompressedTile[] m_tiles; // < List of tiles. // TODO: (PP) replace with list

        private readonly int m_saltBits; // < Number of salt bits in the tile ID.
        private readonly int m_tileBits; // < Number of tile bits in the tile ID.
        private readonly DtNavMesh m_navmesh;

        private readonly DtTileCacheParams m_params;
        private readonly DtTileCacheStorageParams m_storageParams;

        private readonly DtTileCacheAlloc m_talloc;
        private readonly IRcCompressor m_tcomp;
        private readonly IDtTileCacheMeshProcess m_tmproc;

        /// Held for the life of the cache rather than made per tile: a context allocates
        /// thread-local timer state, and tile builds run one at a time on a cache.
        private readonly RcContext m_ctx = new RcContext();

        /// Neighbour layers a tile's border reads, by tile ref (#BorderLayer). Dropped whenever a
        /// tile or an obstacle changes, since both change what the layers say.
        private readonly Dictionary<long, DtTileCacheLayer> m_borderLayers = new Dictionary<long, DtTileCacheLayer>();
        private const int MaxCachedBorderLayers = 256;
        private readonly List<long> m_neighbourRefs = new List<long>();

        private readonly List<DtTileCacheObstacle> m_obstacles = new List<DtTileCacheObstacle>();
        private DtTileCacheObstacle m_nextFreeObstacle;

        private readonly List<DtObstacleRequest> m_reqs = new List<DtObstacleRequest>();

        /// Tiles queued for rebuild, oldest first. A queue rather than a list because Update()
        /// takes from the front: List.RemoveAt(0) shifts every remaining entry, which turns
        /// draining a batch into O(n²).
        private readonly Queue<long> m_update = new Queue<long>();

        /// Membership mirror of m_update, so queueing the tiles an obstacle touches is O(1) per
        /// tile instead of a linear scan of everything already queued.
        private readonly HashSet<long> m_updateSet = new HashSet<long>();

        public DtTileCache(in DtTileCacheParams option, DtTileCacheStorageParams storageParams, DtNavMesh navmesh, IRcCompressor tcomp, IDtTileCacheMeshProcess tmprocs)
        {
            m_params = option;
            m_storageParams = storageParams;
            m_navmesh = navmesh;
            m_talloc = new DtTileCacheAlloc(); // TODO: ikpil, improve pooling system
            m_tcomp = tcomp;
            m_tmproc = tmprocs;

            m_tileLutSize = DtUtils.NextPow2(m_params.maxTiles / 4);
            if (m_tileLutSize == 0)
            {
                m_tileLutSize = 1;
            }

            m_tileLutMask = m_tileLutSize - 1;
            m_tiles = new DtCompressedTile[m_params.maxTiles];
            m_posLookup = new DtCompressedTile[m_tileLutSize];
            for (int i = m_params.maxTiles - 1; i >= 0; --i)
            {
                m_tiles[i] = new DtCompressedTile(i);
                m_tiles[i].next = m_nextFreeTile;
                m_nextFreeTile = m_tiles[i];
            }

            m_tileBits = DtUtils.Ilog2(DtUtils.NextPow2(m_params.maxTiles));
            m_saltBits = Math.Min(31, 32 - m_tileBits);
            if (m_saltBits < 10)
            {
                throw new Exception("Too few salt bits: " + m_saltBits);
            }
        }

        private bool Contains(List<long> a, long v)
        {
            return a.Contains(v);
        }

        /// Encodes a tile id.
        private long EncodeTileId(int salt, int it)
        {
            return ((long)salt << m_tileBits) | (long)it;
        }

        /// Decodes a tile salt.
        private int DecodeTileIdSalt(long refs)
        {
            long saltMask = (1L << m_saltBits) - 1;
            return (int)((refs >> m_tileBits) & saltMask);
        }

        /// Decodes a tile id.
        private int DecodeTileIdTile(long refs)
        {
            long tileMask = (1L << m_tileBits) - 1;
            return (int)(refs & tileMask);
        }

        /// Encodes an obstacle id.
        private long EncodeObstacleId(int salt, int it)
        {
            return ((long)salt << 16) | (long)it;
        }

        /// Decodes an obstacle salt.
        private int DecodeObstacleIdSalt(long refs)
        {
            long saltMask = ((long)1 << 16) - 1;
            return (int)((refs >> 16) & saltMask);
        }

        /// Decodes an obstacle id.
        private int DecodeObstacleIdObstacle(long refs)
        {
            long tileMask = ((long)1 << 16) - 1;
            return (int)(refs & tileMask);
        }


        public DtCompressedTile GetTileByRef(long refs)
        {
            if (refs == 0)
            {
                return null;
            }

            int tileIndex = DecodeTileIdTile(refs);
            int tileSalt = DecodeTileIdSalt(refs);
            if (tileIndex >= m_params.maxTiles)
            {
                return null;
            }

            DtCompressedTile tile = m_tiles[tileIndex];
            if (tile.salt != tileSalt)
            {
                return null;
            }

            return tile;
        }

        public List<long> GetTilesAt(int tx, int ty)
        {
            List<long> tiles = new List<long>();
            GetTilesAt(tx, ty, tiles);
            return tiles;
        }

        /// The refs at a tile column, appended to @p tiles. Callers on the tile build path use
        /// this rather than the allocating overload: it runs eight times per tile built.
        public void GetTilesAt(int tx, int ty, List<long> tiles)
        {
            // Find tile based on hash.
            int h = ComputeTileHash(tx, ty, m_tileLutMask);
            DtCompressedTile tile = m_posLookup[h];
            while (tile != null)
            {
                if (tile.header != null && tile.header.tx == tx && tile.header.ty == ty)
                {
                    tiles.Add(GetTileRef(tile));
                }

                tile = tile.next;
            }
        }

        DtCompressedTile GetTileAt(int tx, int ty, int tlayer)
        {
            // Find tile based on hash.
            int h = ComputeTileHash(tx, ty, m_tileLutMask);
            DtCompressedTile tile = m_posLookup[h];
            while (tile != null)
            {
                if (tile.header != null && tile.header.tx == tx && tile.header.ty == ty && tile.header.tlayer == tlayer)
                {
                    return tile;
                }

                tile = tile.next;
            }

            return null;
        }

        public long GetTileRef(DtCompressedTile tile)
        {
            if (tile == null)
            {
                return 0;
            }

            int it = tile.index;
            return EncodeTileId(tile.salt, it);
        }

        public long GetObstacleRef(DtTileCacheObstacle ob)
        {
            if (ob == null)
            {
                return 0;
            }

            int idx = ob.index;
            return EncodeObstacleId(ob.salt, idx);
        }

        public DtTileCacheObstacle GetObstacleByRef(long refs)
        {
            if (refs == 0)
            {
                return null;
            }

            int idx = DecodeObstacleIdObstacle(refs);
            if (idx >= m_obstacles.Count)
            {
                return null;
            }

            DtTileCacheObstacle ob = m_obstacles[idx];
            int salt = DecodeObstacleIdSalt(refs);
            if (ob.salt != salt)
            {
                return null;
            }

            return ob;
        }

        public long AddTile(byte[] data, int flags)
        {
            m_borderLayers.Clear();

            // Make sure the data is in right format.
            RcByteBuffer buf = new RcByteBuffer(data);
            buf.Order(m_storageParams.Order);
            DtTileCacheLayerHeader header = DtTileCacheLayerHeaderReader.Read(buf, m_storageParams.Compatibility);
            // Make sure the location is free.
            if (GetTileAt(header.tx, header.ty, header.tlayer) != null)
            {
                return 0;
            }

            // Allocate a tile.
            DtCompressedTile tile = null;
            if (m_nextFreeTile != null)
            {
                tile = m_nextFreeTile;
                m_nextFreeTile = tile.next;
                tile.next = null;
            }

            // Make sure we could allocate a tile.
            if (tile == null)
            {
                throw new Exception("Out of storage");
            }

            // Insert tile into the position lut.
            int h = ComputeTileHash(header.tx, header.ty, m_tileLutMask);
            tile.next = m_posLookup[h];
            m_posLookup[h] = tile;

            // Init tile.
            tile.header = header;
            tile.data = data;
            tile.compressed = Align4(buf.Position());
            tile.flags = flags;

            return GetTileRef(tile);
        }

        private int Align4(int i)
        {
            return (i + 3) & (~3);
        }

        public void RemoveTile(long refs)
        {
            m_borderLayers.Clear();

            if (refs == 0)
            {
                throw new Exception("Invalid tile ref");
            }

            int tileIndex = DecodeTileIdTile(refs);
            int tileSalt = DecodeTileIdSalt(refs);
            if (tileIndex >= m_params.maxTiles)
            {
                throw new Exception("Invalid tile index");
            }

            DtCompressedTile tile = m_tiles[tileIndex];
            if (tile.salt != tileSalt)
            {
                throw new Exception("Invalid tile salt");
            }

            // Remove tile from hash lookup.
            int h = ComputeTileHash(tile.header.tx, tile.header.ty, m_tileLutMask);
            DtCompressedTile prev = null;
            DtCompressedTile cur = m_posLookup[h];
            while (cur != null)
            {
                if (cur == tile)
                {
                    if (prev != null)
                    {
                        prev.next = cur.next;
                    }
                    else
                    {
                        m_posLookup[h] = cur.next;
                    }

                    break;
                }

                prev = cur;
                cur = cur.next;
            }

            tile.header = null;
            tile.data = null;
            tile.compressed = 0;
            tile.flags = 0;

            // Update salt, salt should never be zero.
            tile.salt = (tile.salt + 1) & ((1 << m_saltBits) - 1);
            if (tile.salt == 0)
            {
                tile.salt++;
            }

            // Add to free list.
            tile.next = m_nextFreeTile;
            m_nextFreeTile = tile;
        }

        // Cylinder obstacle
        public long AddObstacle(RcVec3f pos, float radius, float height)
        {
            DtTileCacheObstacle ob = AllocObstacle();
            ob.type = DtTileCacheObstacleType.DT_OBSTACLE_CYLINDER;

            ob.cylinder.pos = pos;
            ob.cylinder.radius = radius;
            ob.cylinder.height = height;

            return AddObstacleRequest(ob).refs;
        }

        // Aabb obstacle
        public long AddBoxObstacle(RcVec3f bmin, RcVec3f bmax)
        {
            DtTileCacheObstacle ob = AllocObstacle();
            ob.type = DtTileCacheObstacleType.DT_OBSTACLE_BOX;

            ob.box.bmin = bmin;
            ob.box.bmax = bmax;

            return AddObstacleRequest(ob).refs;
        }

        // Box obstacle: can be rotated in Y
        public long AddBoxObstacle(RcVec3f center, RcVec3f extents, float yRadians)
        {
            DtTileCacheObstacle ob = AllocObstacle();
            ob.type = DtTileCacheObstacleType.DT_OBSTACLE_ORIENTED_BOX;
            ob.orientedBox.center = center;
            ob.orientedBox.extents = extents;
            float coshalf = MathF.Cos(0.5f * yRadians);
            float sinhalf = MathF.Sin(-0.5f * yRadians);
            ob.orientedBox.rotAux[0] = coshalf * sinhalf;
            ob.orientedBox.rotAux[1] = coshalf * coshalf - 0.5f;
            return AddObstacleRequest(ob).refs;
        }

        private DtObstacleRequest AddObstacleRequest(DtTileCacheObstacle ob)
        {
            DtObstacleRequest req = new DtObstacleRequest(DtObstacleRequestAction.REQUEST_ADD, GetObstacleRef(ob));
            m_reqs.Add(req);
            return req;
        }

        public void RemoveObstacle(long refs)
        {
            if (refs == 0)
            {
                return;
            }

            DtObstacleRequest req = new DtObstacleRequest(DtObstacleRequestAction.REQUEST_REMOVE, refs);
            m_reqs.Add(req);
        }

        private DtTileCacheObstacle AllocObstacle()
        {
            DtTileCacheObstacle o = m_nextFreeObstacle;
            if (o == null)
            {
                o = new DtTileCacheObstacle(m_obstacles.Count);
                m_obstacles.Add(o);
            }
            else
            {
                m_nextFreeObstacle = o.next;
            }

            o.state = DtObstacleState.DT_OBSTACLE_PROCESSING;
            o.touched.Clear();
            o.pending.Clear();
            o.next = null;
            return o;
        }

        public int GetObstacleCount()
        {
            return m_obstacles.Count;
        }

        public DtTileCacheObstacle GetObstacle(int i)
        {
            if (0 > i || i >= m_obstacles.Count)
            {
                return null;
            }

            return m_obstacles[i];
        }

        private DtStatus QueryTiles(RcVec3f bmin, RcVec3f bmax, List<long> results, ref int ntouched)
        {
            results.Clear();

            int n = 0;
            
            float tw = m_params.width * m_params.cs;
            float th = m_params.height * m_params.cs;
            int tx0 = (int)MathF.Floor((bmin.X - m_params.orig.X) / tw);
            int tx1 = (int)MathF.Floor((bmax.X - m_params.orig.X) / tw);
            int ty0 = (int)MathF.Floor((bmin.Z - m_params.orig.Z) / th);
            int ty1 = (int)MathF.Floor((bmax.Z - m_params.orig.Z) / th);
            for (int ty = ty0; ty <= ty1; ++ty)
            {
                for (int tx = tx0; tx <= tx1; ++tx)
                {
                    List<long> tiles = GetTilesAt(tx, ty);
                    foreach (long i in tiles)
                    {
                        DtCompressedTile tile = m_tiles[DecodeTileIdTile(i)];
                        RcVec3f tbmin = new RcVec3f();
                        RcVec3f tbmax = new RcVec3f();
                        CalcTightTileBounds(tile.header, ref tbmin, ref tbmax);
                        if (DtUtils.OverlapBounds(bmin, bmax, tbmin, tbmax))
                        {
                            results.Add(i);
                            n++;
                        }
                    }
                }
            }

            ntouched = n;
            return DtStatus.DT_SUCCESS;
        }

        /**
         * Updates the tile cache by rebuilding tiles touched by unfinished obstacle requests.
         *
         * @return Returns true if the tile cache is fully up to date with obstacle requests and tile rebuilds. If the tile
         *         cache is up to date another (immediate) call to update will have no effect; otherwise another call will
         *         continue processing obstacle requests and tile rebuilds.
         */
        public bool Update() => Update(1);

        /**
         * As {@link #Update()}, but rebuilds up to @p maxTiles tiles in this call instead of one.
         *
         * A carve touching many tiles otherwise takes one frame per tile to reach the navmesh, and
         * no new obstacle request is processed while any rebuild is still pending, so a large batch
         * stalls everything behind it. Callers that can afford the frame time raise the budget.
         *
         * @param maxTiles Maximum tiles to rebuild this call. Values below 1 are treated as 1.
         */
        public bool Update(int maxTiles)
        {
            if (maxTiles < 1)
            {
                maxTiles = 1;
            }

            if (0 == m_update.Count)
            {
                // Cached neighbour layers carry the obstacles cut into them, which these
                // requests are about to change.
                if (m_reqs.Count > 0)
                {
                    m_borderLayers.Clear();
                }

                // Process requests.
                foreach (DtObstacleRequest req in m_reqs)
                {
                    int idx = DecodeObstacleIdObstacle(req.refs);
                    if (idx >= m_obstacles.Count)
                    {
                        continue;
                    }

                    DtTileCacheObstacle ob = m_obstacles[idx];
                    int salt = DecodeObstacleIdSalt(req.refs);
                    if (ob.salt != salt)
                    {
                        continue;
                    }

                    if (req.action == DtObstacleRequestAction.REQUEST_ADD)
                    {
                        // Find touched tiles. Widened by the seam border, because a tile reads
                        // that far into its neighbours: a tile the obstacle only reaches through
                        // its border still has to rebuild, or its seam keeps describing ground
                        // the neighbour has already cut away.
                        RcVec3f bmin = new RcVec3f();
                        RcVec3f bmax = new RcVec3f();
                        GetObstacleBounds(ob, ref bmin, ref bmax);
                        float reach = DtTileCacheBuilder.SeamBorder * m_params.cs;
                        bmin.X -= reach;
                        bmin.Z -= reach;
                        bmax.X += reach;
                        bmax.Z += reach;

                        int ntouched = 0;
                        QueryTiles(bmin, bmax, ob.touched, ref ntouched);
                        // Add tiles to update list.
                        ob.pending.Clear();
                        foreach (var j in ob.touched)
                        {
                            if (m_updateSet.Add(j))
                            {
                                m_update.Enqueue(j);
                            }

                            ob.pending.Add(j);
                        }
                    }
                    else if (req.action == DtObstacleRequestAction.REQUEST_REMOVE)
                    {
                        // Prepare to remove obstacle.
                        ob.state = DtObstacleState.DT_OBSTACLE_REMOVING;
                        // Add tiles to update list.
                        ob.pending.Clear();
                        foreach (long j in ob.touched)
                        {
                            if (m_updateSet.Add(j))
                            {
                                m_update.Enqueue(j);
                            }

                            ob.pending.Add(j);
                        }
                    }
                }

                m_reqs.Clear();
            }

            // Process updates
            for (int n = 0; n < maxTiles && 0 < m_update.Count; ++n)
            {
                long refs = m_update.Dequeue();
                m_updateSet.Remove(refs);
                // Build mesh
                BuildNavMeshTile(refs);

                // Update obstacle states.
                for (int i = 0; i < m_obstacles.Count; ++i)
                {
                    DtTileCacheObstacle ob = m_obstacles[i];
                    if (ob.state == DtObstacleState.DT_OBSTACLE_PROCESSING
                        || ob.state == DtObstacleState.DT_OBSTACLE_REMOVING)
                    {
                        // Remove handled tile from pending list.
                        ob.pending.Remove(refs);

                        // If all pending tiles processed, change state.
                        if (0 == ob.pending.Count)
                        {
                            if (ob.state == DtObstacleState.DT_OBSTACLE_PROCESSING)
                            {
                                ob.state = DtObstacleState.DT_OBSTACLE_PROCESSED;
                            }
                            else if (ob.state == DtObstacleState.DT_OBSTACLE_REMOVING)
                            {
                                ob.state = DtObstacleState.DT_OBSTACLE_EMPTY;
                                // Update salt, salt should never be zero.
                                ob.salt = (ob.salt + 1) & ((1 << 16) - 1);
                                if (ob.salt == 0)
                                {
                                    ob.salt++;
                                }

                                // Return obstacle to free list.
                                ob.next = m_nextFreeObstacle;
                                m_nextFreeObstacle = ob;
                            }
                        }
                    }
                }
            }

            return 0 == m_update.Count && 0 == m_reqs.Count;
        }

        public void BuildNavMeshTile(long refs)
        {
            int idx = DecodeTileIdTile(refs);
            if (idx > m_params.maxTiles)
            {
                throw new Exception("Invalid tile index");
            }

            DtCompressedTile tile = m_tiles[idx];
            int salt = DecodeTileIdSalt(refs);
            if (tile.salt != salt)
            {
                throw new Exception("Invalid tile salt");
            }

            int walkableClimbVx = (int)(m_params.walkableClimb / m_params.ch);

            // Decompress tile layer data.
            DtTileCacheLayer layer = DecompressTile(tile);

            // Rasterize obstacles.
            MarkObstacles(layer, tile.header.bmin, refs);

            // Build navmesh. Both new stages read a bordered compact heightfield whose border
            // cells come from the neighbouring tiles' layers, so each computes the seam from the
            // same world cells the neighbour computes it from — how the standard tiled pipeline
            // keeps seams closed, and what the compressed layer format discards. Neither stage
            // asked for means neither the border nor the heightfield is worth building.
            // Watershed partitioning declines multi-layer tiles and degenerate region sets
            // (null), which fall back to the classic monotone path.
            RcCompactHeightfield chf = null;
            if (m_params.watershedPartition || m_params.detailSampleDist > 0)
            {
                DtTileCacheBuilder.DtTileBorderGrid grid = BuildBorderGrid(tile, layer, DtTileCacheBuilder.SeamBorder);
                chf = DtTileCacheBuilder.ToCompactHeightfield(grid, m_params);
            }

            DtTileCacheContourSet lcset = m_params.watershedPartition
                ? DtTileCacheBuilder.BuildTileCacheContoursWatershed(m_ctx, layer, chf, m_params)
                : null;
            if (lcset == null)
            {
                DtTileCacheBuilder.BuildTileCacheRegions(layer, walkableClimbVx);
                lcset = DtTileCacheBuilder.BuildTileCacheContours(m_talloc, layer, walkableClimbVx, m_params.maxSimplificationError);
            }

            DtTileCachePolyMesh polyMesh = DtTileCacheBuilder.BuildTileCachePolyMesh(lcset, m_navmesh.GetMaxVertsPerPoly());

            // Early out if the mesh tile is empty.
            if (polyMesh.npolys == 0)
            {
                m_navmesh.RemoveTile(m_navmesh.GetTileRefAt(tile.header.tx, tile.header.ty, tile.header.tlayer));
                return;
            }

            DtNavMeshCreateParams option = new DtNavMeshCreateParams();
            option.verts = polyMesh.verts;
            option.vertCount = polyMesh.nverts;
            option.polys = polyMesh.polys;
            option.polyAreas = polyMesh.areas;
            option.polyFlags = polyMesh.flags;
            option.polyCount = polyMesh.npolys;
            option.nvp = m_navmesh.GetMaxVertsPerPoly();
            option.walkableHeight = m_params.walkableHeight;
            option.walkableRadius = m_params.walkableRadius;
            option.walkableClimb = m_params.walkableClimb;
            option.tileX = tile.header.tx;
            option.tileZ = tile.header.ty;
            option.tileLayer = tile.header.tlayer;
            option.cs = m_params.cs;
            option.ch = m_params.ch;
            option.buildBvTree = false;
            option.bmin = tile.header.bmin;
            option.bmax = tile.header.bmax;
            if (chf != null)
                DtTileCacheBuilder.BuildTileCacheDetailMesh(m_ctx, layer, chf, option, m_params);
            if (m_tmproc != null)
            {
                m_tmproc.Process(option);
            }

            DtMeshData meshData = DtNavMeshBuilder.CreateNavMeshData(option);
            // Remove existing tile.
            m_navmesh.RemoveTile(m_navmesh.GetTileRefAt(tile.header.tx, tile.header.ty, tile.header.tlayer));
            // Add new tile, or leave the location empty. if (navData) { // Let the
            if (meshData != null)
            {
                m_navmesh.AddTile(meshData, 0, 0, out var result);
            }
        }

        /// Cuts every obstacle that reaches @p refs out of that tile's layer. Kept separate from
        /// the tile build because a tile's border reads its neighbours' layers, which must carry
        /// the same obstacles the neighbour itself will cut when it rebuilds.
        private void MarkObstacles(DtTileCacheLayer layer, RcVec3f bmin, long refs)
        {
            for (int i = 0; i < m_obstacles.Count; ++i)
            {
                DtTileCacheObstacle ob = m_obstacles[i];
                if (ob.state == DtObstacleState.DT_OBSTACLE_EMPTY || ob.state == DtObstacleState.DT_OBSTACLE_REMOVING)
                {
                    continue;
                }

                if (!Contains(ob.touched, refs))
                {
                    continue;
                }

                if (ob.type == DtTileCacheObstacleType.DT_OBSTACLE_CYLINDER)
                {
                    DtTileCacheBuilder.MarkCylinderArea(layer, bmin, m_params.cs, m_params.ch, ob.cylinder.pos, ob.cylinder.radius, ob.cylinder.height, 0);
                }
                else if (ob.type == DtTileCacheObstacleType.DT_OBSTACLE_BOX)
                {
                    DtTileCacheBuilder.MarkBoxArea(layer, bmin, m_params.cs, m_params.ch, ob.box.bmin, ob.box.bmax, 0);
                }
                else if (ob.type == DtTileCacheObstacleType.DT_OBSTACLE_ORIENTED_BOX)
                {
                    DtTileCacheBuilder.MarkBoxArea(layer, bmin, m_params.cs, m_params.ch, ob.orientedBox.center, ob.orientedBox.extents, ob.orientedBox.rotAux, 0);
                }
            }
        }

        /// A neighbour's layer as a tile's border reads it: decompressed, with the obstacles that
        /// reach it already cut out, and kept until the tiles or obstacles change. A bake meshes
        /// every tile, and each of those reads its eight neighbours, so without this every layer
        /// is decompressed nine times over.
        private DtTileCacheLayer BorderLayer(long refs, DtCompressedTile tile)
        {
            if (m_borderLayers.TryGetValue(refs, out DtTileCacheLayer cached))
                return cached;

            DtTileCacheLayer layer = DecompressTile(tile);
            MarkObstacles(layer, tile.header.bmin, refs);
            // Bounded rather than grown without limit: a world of thousands of tiles would
            // otherwise hold every one of them decompressed for the life of the cache.
            if (m_borderLayers.Count >= MaxCachedBorderLayers)
                m_borderLayers.Clear();

            m_borderLayers[refs] = layer;
            return layer;
        }

        /// This tile's layer widened by a border of the neighbouring tiles' cells — the data the
        /// standard tiled pipeline gets by rasterizing past the tile edge, and the compressed
        /// layer format discards. With it, corner heights, contours and height detail at a seam
        /// are computed from the same world cells on both sides, and the seam closes by
        /// construction. Border cells with no neighbour stay empty, which is exactly a map
        /// perimeter. A neighbour stacking several vertical layers contributes, per cell, the
        /// layer nearest this tile's own surface at the adjacent edge.
        private DtTileCacheBuilder.DtTileBorderGrid BuildBorderGrid(DtCompressedTile tile, DtTileCacheLayer layer, int border)
        {
            int w = layer.header.width, h = layer.header.height;
            var grid = new DtTileCacheBuilder.DtTileBorderGrid(w, h, border);

            for (int z = 0; z < h; ++z)
            {
                for (int x = 0; x < w; ++x)
                {
                    int src = x + z * w;
                    int dst = grid.Index(x, z);
                    grid.heights[dst] = layer.heights[src] == DtTileCacheBuilder.NoSurface ? -1 : layer.heights[src];
                    grid.areas[dst] = layer.areas[src];
                }
            }

            // Our reference height beside each border cell, for choosing between a neighbour's
            // vertical layers: the own-side edge cell nearest it.
            int OwnRef(int gx, int gz)
            {
                int cx = Math.Clamp(gx, 0, w - 1);
                int cz = Math.Clamp(gz, 0, h - 1);
                int v = layer.heights[cx + cz * w];
                return v == DtTileCacheBuilder.NoSurface ? -1 : v;
            }

            for (int dtx = -1; dtx <= 1; ++dtx)
            {
                for (int dty = -1; dty <= 1; ++dty)
                {
                    if (dtx == 0 && dty == 0)
                        continue;

                    m_neighbourRefs.Clear();
                    GetTilesAt(tile.header.tx + dtx, tile.header.ty + dty, m_neighbourRefs);
                    foreach (long r in m_neighbourRefs)
                    {
                        DtCompressedTile nt = GetTileByRef(r);
                        if (nt?.header == null || nt.header.width != w || nt.header.height != h)
                            continue;
                        DtTileCacheLayer nl = BorderLayer(r, nt);
                        // Layer heights are relative to their own layer's base; rebase into ours.
                        int off = (int)Math.Round((nl.header.bmin.Y - layer.header.bmin.Y) / m_params.ch);

                        // The strip of our border this neighbour covers, in our cell coordinates.
                        int gx0 = dtx < 0 ? -border : dtx > 0 ? w : 0;
                        int gx1 = dtx < 0 ? 0 : dtx > 0 ? w + border : w;
                        int gz0 = dty < 0 ? -border : dty > 0 ? h : 0;
                        int gz1 = dty < 0 ? 0 : dty > 0 ? h + border : h;

                        for (int gz = gz0; gz < gz1; ++gz)
                        {
                            for (int gx = gx0; gx < gx1; ++gx)
                            {
                                int nx = gx - dtx * w;
                                int nz = gz - dty * h;
                                int raw = nl.heights[nx + nz * w];
                                if (raw == DtTileCacheBuilder.NoSurface)
                                    continue;

                                int cand = raw + off;
                                int dst = grid.Index(gx, gz);
                                int ownRef = OwnRef(gx, gz);
                                if (grid.heights[dst] < 0
                                    || (ownRef >= 0 && Math.Abs(cand - ownRef) < Math.Abs(grid.heights[dst] - ownRef)))
                                {
                                    grid.heights[dst] = cand;
                                    grid.areas[dst] = nl.areas[nx + nz * w];
                                }
                            }
                        }
                    }
                }
            }

            return grid;
        }

        public DtTileCacheLayer DecompressTile(DtCompressedTile tile)
        {
            DtTileCacheLayer layer = DtTileCacheBuilder.DecompressTileCacheLayer(m_tcomp, tile.data, m_storageParams.Order, m_storageParams.Compatibility);
            return layer;
        }

        void CalcTightTileBounds(DtTileCacheLayerHeader header, ref RcVec3f bmin, ref RcVec3f bmax)
        {
            float cs = m_params.cs;
            bmin.X = header.bmin.X + header.minx * cs;
            bmin.Y = header.bmin.Y;
            bmin.Z = header.bmin.Z + header.miny * cs;
            bmax.X = header.bmin.X + (header.maxx + 1) * cs;
            bmax.Y = header.bmax.Y;
            bmax.Z = header.bmin.Z + (header.maxy + 1) * cs;
        }

        public void GetObstacleBounds(DtTileCacheObstacle ob, ref RcVec3f bmin, ref RcVec3f bmax)
        {
            if (ob.type == DtTileCacheObstacleType.DT_OBSTACLE_CYLINDER)
            {
                bmin.X = ob.cylinder.pos.X - ob.cylinder.radius;
                bmin.Y = ob.cylinder.pos.Y;
                bmin.Z = ob.cylinder.pos.Z - ob.cylinder.radius;
                bmax.X = ob.cylinder.pos.X + ob.cylinder.radius;
                bmax.Y = ob.cylinder.pos.Y + ob.cylinder.height;
                bmax.Z = ob.cylinder.pos.Z + ob.cylinder.radius;
            }
            else if (ob.type == DtTileCacheObstacleType.DT_OBSTACLE_BOX)
            {
                bmin = ob.box.bmin;
                bmax = ob.box.bmax;
            }
            else if (ob.type == DtTileCacheObstacleType.DT_OBSTACLE_ORIENTED_BOX)
            {
                float maxr = 1.41f * Math.Max(ob.orientedBox.extents.X, ob.orientedBox.extents.Z);
                bmin.X = ob.orientedBox.center.X - maxr;
                bmax.X = ob.orientedBox.center.X + maxr;
                bmin.Y = ob.orientedBox.center.Y - ob.orientedBox.extents.Y;
                bmax.Y = ob.orientedBox.center.Y + ob.orientedBox.extents.Y;
                bmin.Z = ob.orientedBox.center.Z - maxr;
                bmax.Z = ob.orientedBox.center.Z + maxr;
            }
        }

        public ref readonly DtTileCacheParams GetParams()
        {
            return ref m_params;
        }

        public IRcCompressor GetCompressor()
        {
            return m_tcomp;
        }

        public int GetTileCount()
        {
            return m_params.maxTiles;
        }

        public DtCompressedTile GetTile(int i)
        {
            return m_tiles[i];
        }

        public DtNavMesh GetNavMesh()
        {
            return m_navmesh;
        }
    }
}