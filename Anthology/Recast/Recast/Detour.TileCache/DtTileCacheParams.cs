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

using Prowl.Recast.Core.Numerics;

namespace Prowl.Recast.Detour.TileCache
{
    public struct DtTileCacheParams
    {
        public RcVec3f orig;
        public float cs, ch;
        public int width, height;
        public float walkableHeight;
        public float walkableRadius;
        public float walkableClimb;
        public float maxSimplificationError;

        /// Spacing of the height samples used to give each tile's polygons height detail, in world
        /// units, and how far the detail may sit from the sampled surface before it is subdivided.
        /// Leave detailSampleDist at 0 to build tiles without detail, in which case Detour treats
        /// every polygon as flat between its own corners.
        public float detailSampleDist;
        public float detailSampleMaxError;

        /// Partition tiles with a watershed over a distance field and contour them with the
        /// standard builder — hole-aware, connectivity-based corner heights, long edges split —
        /// instead of the cache's classic monotone sweep, whose row-band regions come out a
        /// fraction of a voxel wide on sloped ground and whose unsplit contours polygonize open
        /// ground into fans that reach across the tile. Tiles stacking several vertical layers
        /// keep the classic path either way: the standard contourer cannot see the other layers.
        public bool watershedPartition;

        /// Watershed tuning, used only when @p watershedPartition is set: regions smaller than
        /// minRegionArea cells are culled, regions smaller than mergeRegionArea cells are merged
        /// into their neighbours, and contour edges longer than maxEdgeLen voxels are split.
        public int minRegionArea;
        public int mergeRegionArea;
        public int maxEdgeLen;

        public int maxTiles;
        public int maxObstacles;
    }
}