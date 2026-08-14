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
using System.IO;
using Prowl.Recast.Core;
using Prowl.Recast.Core.Numerics;
using Prowl.Recast.Detour.TileCache.Io;
using Prowl.Recast;


namespace Prowl.Recast.Detour.TileCache
{
    public static class DtTileCacheBuilder
    {
        public const byte DT_TILECACHE_NULL_AREA = 0;
        public const byte DT_TILECACHE_WALKABLE_AREA = 63;
        public const int DT_TILECACHE_NULL_IDX = 0xffff;
        public const uint VERTEX_BUCKET_COUNT2 = (1 << 8);

        private static readonly int[] DirOffsetX = { -1, 0, 1, 0, };
        private static readonly int[] DirOffsetY = { 0, 1, 0, -1 };

        public static void BuildTileCacheRegions(DtTileCacheLayer layer, int walkableClimb)
        {
            int w = layer.header.width;
            int h = layer.header.height;

            Array.Fill(layer.regs, (byte)0xFF);
            int nsweeps = w;
            RcLayerSweepSpan[] sweeps = new RcLayerSweepSpan[nsweeps];
            for (int i = 0; i < sweeps.Length; i++)
            {
                sweeps[i] = new RcLayerSweepSpan();
            }

            // Partition walkable area into monotone regions.
            Span<byte> prevCount = stackalloc byte[256];
            byte regId = 0;

            for (int y = 0; y < h; ++y)
            {
                if (regId > 0)
                {
                    RcSpans.Fill<byte>(prevCount, 0, 0, regId);
                }

                // Memset(prevCount,0,Sizeof(char)*regId);
                int sweepId = 0;

                for (int x = 0; x < w; ++x)
                {
                    int idx = x + y * w;
                    if (layer.areas[idx] == DT_TILECACHE_NULL_AREA)
                        continue;

                    int sid = 0xff;

                    // -x
                    int xidx = (x - 1) + y * w;
                    if (x > 0 && IsConnected(layer, idx, xidx, walkableClimb))
                    {
                        if (layer.regs[xidx] != 0xff)
                            sid = layer.regs[xidx];
                    }

                    if (sid == 0xff)
                    {
                        sid = sweepId++;
                        sweeps[sid].nei = 0xff;
                        sweeps[sid].ns = 0;
                    }

                    // -y
                    int yidx = x + (y - 1) * w;
                    if (y > 0 && IsConnected(layer, idx, yidx, walkableClimb))
                    {
                        byte nr = layer.regs[yidx];
                        if (nr != 0xff)
                        {
                            // Set neighbour when first valid neighbour is
                            // encoutered.
                            if (sweeps[sid].ns == 0)
                                sweeps[sid].nei = nr;

                            if (sweeps[sid].nei == nr)
                            {
                                // Update existing neighbour
                                sweeps[sid].ns++;
                                prevCount[nr]++;
                            }
                            else
                            {
                                // This is hit if there is nore than one neighbour.
                                // Invalidate the neighbour.
                                sweeps[sid].nei = 0xff;
                            }
                        }
                    }

                    layer.regs[idx] = (byte)sid;
                }

                // Create unique ID.
                for (int i = 0; i < sweepId; ++i)
                {
                    // If the neighbour is set and there is only one continuous
                    // connection to it,
                    // the sweep will be merged with the previous one, else new
                    // region is created.
                    if (sweeps[i].nei != 0xff && prevCount[sweeps[i].nei] == sweeps[i].ns)
                    {
                        sweeps[i].id = sweeps[i].nei;
                    }
                    else
                    {
                        if (regId == 255)
                        {
                            // Region ID's overflow.
                            throw new Exception("Buffer too small");
                        }

                        sweeps[i].id = regId++;
                    }
                }

                // Remap local sweep ids to region ids.
                for (int x = 0; x < w; ++x)
                {
                    int idx = x + y * w;
                    if (layer.regs[idx] != 0xff)
                        layer.regs[idx] = sweeps[layer.regs[idx]].id;
                }
            }

            // Allocate and init layer regions.
            byte nregs = regId;
            DtLayerMonotoneRegion[] regs = new DtLayerMonotoneRegion[nregs];

            for (int i = 0; i < nregs; ++i)
            {
                regs[i] = new DtLayerMonotoneRegion();
                regs[i].regId = 0xff;
            }

            // Find region neighbours.
            for (int y = 0; y < h; ++y)
            {
                for (int x = 0; x < w; ++x)
                {
                    int idx = x + y * w;
                    byte ri = layer.regs[idx];
                    if (ri == 0xff)
                        continue;

                    // Update area.
                    regs[ri].area++;
                    regs[ri].areaId = layer.areas[idx];

                    // Update neighbours
                    int ymi = x + (y - 1) * w;
                    if (y > 0 && IsConnected(layer, idx, ymi, walkableClimb))
                    {
                        byte rai = layer.regs[ymi];
                        if (rai != 0xff && rai != ri)
                        {
                            AddUniqueLast(regs[ri].neis, ref regs[ri].nneis, rai);
                            AddUniqueLast(regs[rai].neis, ref regs[rai].nneis, ri);
                        }
                    }
                }
            }

            for (byte i = 0; i < nregs; ++i)
                regs[i].regId = i;

            for (int i = 0; i < nregs; ++i)
            {
                DtLayerMonotoneRegion reg = regs[i];

                int merge = -1;
                int mergea = 0;
                for (int j = 0; j < reg.nneis; ++j)
                {
                    byte nei = reg.neis[j];
                    DtLayerMonotoneRegion regn = regs[nei];
                    if (reg.regId == regn.regId)
                        continue;
                    if (reg.areaId != regn.areaId)
                        continue;
                    if (regn.area > mergea)
                    {
                        if (CanMerge(reg.regId, regn.regId, regs, nregs))
                        {
                            mergea = regn.area;
                            merge = nei;
                        }
                    }
                }

                if (merge != -1)
                {
                    int oldId = reg.regId;
                    byte newId = regs[merge].regId;
                    for (int j = 0; j < nregs; ++j)
                        if (regs[j].regId == oldId)
                            regs[j].regId = newId;
                }
            }

            // Compact ids.
            Span<byte> remap = stackalloc byte[256];
            // Find number of unique regions.
            regId = 0;
            for (int i = 0; i < nregs; ++i)
                remap[regs[i].regId] = 1;
            for (int i = 0; i < 256; ++i)
                if (remap[i] != 0)
                    remap[i] = regId++;
            // Remap ids.
            for (int i = 0; i < nregs; ++i)
                regs[i].regId = remap[regs[i].regId];

            layer.regCount = regId;

            for (int i = 0; i < w * h; ++i)
            {
                if (layer.regs[i] != 0xff)
                    layer.regs[i] = regs[layer.regs[i]].regId;
            }
        }

        public static void AddUniqueLast(byte[] a, ref byte an, byte v)
        {
            int n = an;
            if (n > 0 && a[n - 1] == v)
                return;
            a[an] = v;
            an++;
        }

        public static bool IsConnected(DtTileCacheLayer layer, int ia, int ib, int walkableClimb)
        {
            if (layer.areas[ia] != layer.areas[ib])
                return false;
            if (MathF.Abs(layer.heights[ia] - layer.heights[ib]) > walkableClimb)
                return false;
            return true;
        }

        public static bool CanMerge(int oldRegId, int newRegId, DtLayerMonotoneRegion[] regs, int nregs)
        {
            int count = 0;
            for (int i = 0; i < nregs; ++i)
            {
                DtLayerMonotoneRegion reg = regs[i];
                if (reg.regId != oldRegId)
                    continue;

                int nnei = reg.nneis;
                for (int j = 0; j < nnei; ++j)
                {
                    if (regs[reg.neis[j]].regId == newRegId)
                        count++;
                }
            }

            return count == 1;
        }

        public static void AppendVertex(DtTempContour cont, int x, int y, int z, int r)
        {
            // Try to merge with existing segments.
            if (cont.nverts > 1)
            {
                int pa = (cont.nverts - 2) * 4;
                int pb = (cont.nverts - 1) * 4;
                if (cont.verts[pb + 3] == r)
                {
                    if (cont.verts[pa] == cont.verts[pb] && cont.verts[pb] == x)
                    {
                        // The verts are aligned aling x-axis, update z.
                        cont.verts[pb + 1] = y;
                        cont.verts[pb + 2] = z;
                        return;
                    }
                    else if (cont.verts[pa + 2] == cont.verts[pb + 2]
                             && cont.verts[pb + 2] == z)
                    {
                        // The verts are aligned aling z-axis, update x.
                        cont.verts[pb] = x;
                        cont.verts[pb + 1] = y;
                        return;
                    }
                }
            }

            cont.verts.Add(x);
            cont.verts.Add(y);
            cont.verts.Add(z);
            cont.verts.Add(r);
            cont.nverts++;
        }

        public static int GetNeighbourReg(DtTileCacheLayer layer, int ax, int ay, int dir)
        {
            int w = layer.header.width;
            int ia = ax + ay * w;

            int con = layer.cons[ia] & 0xf;
            int portal = layer.cons[ia] >> 4;
            int mask = 1 << dir;

            if ((con & mask) == 0)
            {
                // No connection, return portal or hard edge.
                if ((portal & mask) != 0)
                    return 0xf8 + dir;
                return 0xff;
            }

            int bx = ax + GetDirOffsetX(dir);
            int by = ay + GetDirOffsetY(dir);
            int ib = bx + by * w;
            return layer.regs[ib];
        }

        public static int GetDirOffsetX(int dir)
        {
            return DirOffsetX[dir & 0x03];
        }

        public static int GetDirOffsetY(int dir)
        {
            return DirOffsetY[dir & 0x03];
        }

        public static void WalkContour(DtTileCacheLayer layer, int x, int y, DtTempContour cont)
        {
            int w = layer.header.width;
            int h = layer.header.height;

            cont.nverts = 0;
            cont.verts.Clear();

            int startX = x;
            int startY = y;
            int startDir = -1;

            for (int i = 0; i < 4; ++i)
            {
                int ndir = (i + 3) & 3;
                int rn = GetNeighbourReg(layer, x, y, ndir);
                if (rn != layer.regs[x + y * w])
                {
                    startDir = ndir;
                    break;
                }
            }

            if (startDir == -1)
                return;

            int dir = startDir;
            int maxIter = w * h;
            int iter = 0;
            while (iter < maxIter)
            {
                int rn = GetNeighbourReg(layer, x, y, dir);

                int nx = x;
                int ny = y;
                int ndir = dir;

                if (rn != layer.regs[x + y * w])
                {
                    // Solid edge.
                    int px = x;
                    int pz = y;
                    switch (dir)
                    {
                        case 0:
                            pz++;
                            break;
                        case 1:
                            px++;
                            pz++;
                            break;
                        case 2:
                            px++;
                            break;
                    }

                    // Try to merge with previous vertex.
                    AppendVertex(cont, px, layer.heights[x + y * w], pz, rn);
                    ndir = (dir + 1) & 0x3; // Rotate CW
                }
                else
                {
                    // Move to next.
                    nx = x + GetDirOffsetX(dir);
                    ny = y + GetDirOffsetY(dir);
                    ndir = (dir + 3) & 0x3; // Rotate CCW
                }

                if (iter > 0 && x == startX && y == startY && dir == startDir)
                    break;

                x = nx;
                y = ny;
                dir = ndir;

                iter++;
            }

            // Remove last vertex if it is duplicate of the first one.
            int pa = (cont.nverts - 1) * 4;
            int pb = 0;
            if (cont.verts[pa] == cont.verts[pb]
                && cont.verts[pa + 2] == cont.verts[pb + 2])
                cont.nverts--;
        }

        public static float DistancePtSeg(int x, int z, int px, int pz, int qx, int qz)
        {
            float pqx = qx - px;
            float pqz = qz - pz;
            float dx = x - px;
            float dz = z - pz;
            float d = pqx * pqx + pqz * pqz;
            float t = pqx * dx + pqz * dz;
            if (d > 0)
                t /= d;
            if (t < 0)
                t = 0;
            else if (t > 1)
                t = 1;

            dx = px + t * pqx - x;
            dz = pz + t * pqz - z;

            return dx * dx + dz * dz;
        }

        public static void SimplifyContour(DtTempContour cont, float maxError)
        {
            cont.poly.Clear();

            for (int i = 0; i < cont.nverts; ++i)
            {
                int j = (i + 1) % cont.nverts;
                // Check for start of a wall segment.
                int ra = j * 4 + 3;
                int rb = i * 4 + 3;
                if (cont.verts[ra] != cont.verts[rb])
                    cont.poly.Add(i);
            }

            if (cont.poly.Count < 2)
            {
                // If there is no transitions at all,
                // create some initial points for the simplification process.
                // Find lower-left and upper-right vertices of the contour.
                int llx = cont.verts[0];
                int llz = cont.verts[2];
                int lli = 0;
                int urx = cont.verts[0];
                int urz = cont.verts[2];
                int uri = 0;
                for (int i = 1; i < cont.nverts; ++i)
                {
                    int x = cont.verts[i * 4 + 0];
                    int z = cont.verts[i * 4 + 2];
                    if (x < llx || (x == llx && z < llz))
                    {
                        llx = x;
                        llz = z;
                        lli = i;
                    }

                    if (x > urx || (x == urx && z > urz))
                    {
                        urx = x;
                        urz = z;
                        uri = i;
                    }
                }

                cont.poly.Clear();
                cont.poly.Add(lli);
                cont.poly.Add(uri);
            }

            // Add points until all raw points are within
            // error tolerance to the simplified shape.
            for (int i = 0; i < cont.poly.Count;)
            {
                int ii = (i + 1) % cont.poly.Count;

                int ai = cont.poly[i];
                int ax = cont.verts[ai * 4];
                int az = cont.verts[ai * 4 + 2];

                int bi = cont.poly[ii];
                int bx = cont.verts[bi * 4];
                int bz = cont.verts[bi * 4 + 2];

                // Find maximum deviation from the segment.
                float maxd = 0;
                int maxi = -1;
                int ci, cinc, endi;

                // Traverse the segment in lexilogical order so that the
                // max deviation is calculated similarly when traversing
                // opposite segments.
                if (bx > ax || (bx == ax && bz > az))
                {
                    cinc = 1;
                    ci = (ai + cinc) % cont.nverts;
                    endi = bi;
                }
                else
                {
                    cinc = cont.nverts - 1;
                    ci = (bi + cinc) % cont.nverts;
                    endi = ai;
                }

                // Tessellate only outer edges or edges between areas.
                while (ci != endi)
                {
                    float d = DistancePtSeg(cont.verts[ci * 4], cont.verts[ci * 4 + 2], ax, az, bx, bz);
                    if (d > maxd)
                    {
                        maxd = d;
                        maxi = ci;
                    }

                    ci = (ci + cinc) % cont.nverts;
                }

                // If the max deviation is larger than accepted error,
                // add new point, else continue to next segment.
                if (maxi != -1 && maxd > (maxError * maxError))
                {
                    cont.poly.Insert(i + 1, maxi);
                }
                else
                {
                    ++i;
                }
            }

            // Remap vertices
            int start = 0;
            for (int i = 1; i < cont.poly.Count; ++i)
                if (cont.poly[i] < cont.poly[start])
                    start = i;

            cont.nverts = 0;
            for (int i = 0; i < cont.poly.Count; ++i)
            {
                int j = (start + i) % cont.poly.Count;
                int src = cont.poly[j] * 4;
                int dst = cont.nverts * 4;
                cont.verts[dst] = cont.verts[src];
                cont.verts[dst + 1] = cont.verts[src + 1];
                cont.verts[dst + 2] = cont.verts[src + 2];
                cont.verts[dst + 3] = cont.verts[src + 3];
                cont.nverts++;
            }
        }

        public static int GetCornerHeight(DtTileCacheLayer layer, int x, int y, int z, int walkableClimb, out bool shouldRemove)
        {
            int w = layer.header.width;
            int h = layer.header.height;

            int n = 0;

            int portal = 0xf;
            int height = 0;
            int preg = 0xff;
            bool allSameReg = true;

            for (int dz = -1; dz <= 0; ++dz)
            {
                for (int dx = -1; dx <= 0; ++dx)
                {
                    int px = x + dx;
                    int pz = z + dz;
                    if (px >= 0 && pz >= 0 && px < w && pz < h)
                    {
                        int idx = px + pz * w;
                        int lh = layer.heights[idx];
                        if (MathF.Abs(lh - y) <= walkableClimb && layer.areas[idx] != DT_TILECACHE_NULL_AREA)
                        {
                            height = Math.Max(height, (char)lh);
                            portal &= (layer.cons[idx] >> 4);
                            if (preg != 0xff && preg != layer.regs[idx])
                                allSameReg = false;
                            preg = layer.regs[idx];
                            n++;
                        }
                    }
                }
            }

            int portalCount = 0;
            for (int dir = 0; dir < 4; ++dir)
                if ((portal & (1 << dir)) != 0)
                    portalCount++;

            shouldRemove = false;
            if (n > 1 && portalCount == 1 && allSameReg)
            {
                shouldRemove = true;
            }

            return height;
        }

        // TODO: move this somewhere else, once the layer meshing is done.
        public static DtTileCacheContourSet BuildTileCacheContours(DtTileCacheAlloc alloc, DtTileCacheLayer layer,
            int walkableClimb, float maxError)
        {
            int w = layer.header.width;
            int h = layer.header.height;

            DtTileCacheContourSet lcset = new DtTileCacheContourSet();
            lcset.nconts = layer.regCount;
            lcset.conts = new DtTileCacheContour[lcset.nconts];
            for (int i = 0; i < lcset.nconts; i++)
            {
                lcset.conts[i] = new DtTileCacheContour();
            }

            // Allocate temp buffer for contour tracing.
            // TODO: @ikpil, improve pooling system
            // int maxTempVerts = (w + h) * 2 * 2; // Twice around the layer.
            // dtFixedArray<unsigned char> tempVerts(alloc, maxTempVerts*4);
            // if (!tempVerts)
            //     return DT_FAILURE | DT_OUT_OF_MEMORY;
	           //
            // dtFixedArray<unsigned short> tempPoly(alloc, maxTempVerts);
            // if (!tempPoly)
            //     return DT_FAILURE | DT_OUT_OF_MEMORY;

            DtTempContour temp = new DtTempContour();

            // Find contours.
            for (int y = 0; y < h; ++y)
            {
                for (int x = 0; x < w; ++x)
                {
                    int idx = x + y * w;
                    byte ri = layer.regs[idx];
                    if (ri == 0xff)
                        continue;

                    DtTileCacheContour cont = lcset.conts[ri];

                    if (cont.nverts > 0)
                        continue;

                    cont.reg = ri;
                    cont.area = layer.areas[idx];

                    WalkContour(layer, x, y, temp);

                    SimplifyContour(temp, maxError);

                    // Store contour.
                    cont.nverts = temp.nverts;
                    if (cont.nverts > 0)
                    {
                        cont.verts = new int[4 * temp.nverts];

                        for (int i = 0, j = temp.nverts - 1; i < temp.nverts; j = i++)
                        {
                            int dst = j * 4;
                            int v = j * 4;
                            int vn = i * 4;
                            int nei = temp.verts[vn + 3]; // The neighbour reg
                            // is
                            // stored at segment
                            // vertex of a
                            // segment.
                            int lh = GetCornerHeight(layer, temp.verts[v], temp.verts[v + 1], temp.verts[v + 2],
                                walkableClimb, out var shouldRemove);
                            cont.verts[dst + 0] = temp.verts[v];
                            cont.verts[dst + 1] = lh;
                            cont.verts[dst + 2] = temp.verts[v + 2];

                            // Store portal direction and remove status to the
                            // fourth component.
                            cont.verts[dst + 3] = 0x0f;
                            if (nei != 0xff && nei >= 0xf8)
                                cont.verts[dst + 3] = nei - 0xf8;
                            if (shouldRemove)
                                cont.verts[dst + 3] |= 0x80;
                        }
                    }
                }
            }

            return lcset;
        }


        public static int ComputeVertexHash2(int x, int y, int z)
        {
            uint h1 = 0x8da6b343; // Large multiplicative constants;
            uint h2 = 0xd8163841; // here arbitrarily chosen primes
            uint h3 = 0xcb1ab31f;
            uint n = h1 * (uint)x + h2 * (uint)y + h3 * (uint)z;
            return (int)(n & (VERTEX_BUCKET_COUNT2 - 1));
        }

        public static int AddVertex(int x, int y, int z, int[] verts, int[] firstVert, int[] nextVert, int nv)
        {
            int bucket = ComputeVertexHash2(x, 0, z);
            int i = firstVert[bucket];
            while (i != DT_TILECACHE_NULL_IDX)
            {
                int tv = i * 3;
                if (verts[tv] == x && verts[tv + 2] == z && (MathF.Abs(verts[tv + 1] - y) <= 2))
                    return i;
                i = nextVert[i]; // next
            }

            // Could not find, create new.
            i = nv;
            int v = i * 3;
            verts[v] = x;
            verts[v + 1] = y;
            verts[v + 2] = z;
            nextVert[i] = firstVert[bucket];
            firstVert[bucket] = i;
            return i;
        }

        public static void BuildMeshAdjacency(int[] polys, int npolys, int[] verts, int nverts, DtTileCacheContourSet lcset,
            int maxVertsPerPoly)
        {
            // Based on code by Eric Lengyel from:
            // http://www.terathon.com/code/edges.php

            int maxEdgeCount = npolys * maxVertsPerPoly;

            int[] firstEdge = new int[nverts + maxEdgeCount];
            int nextEdge = nverts;
            int edgeCount = 0;

            RcEdge[] edges = new RcEdge[maxEdgeCount];
            for (int i = 0; i < maxEdgeCount; i++)
            {
                edges[i] = new RcEdge();
            }

            for (int i = 0; i < nverts; i++)
                firstEdge[i] = DT_TILECACHE_NULL_IDX;

            for (int i = 0; i < npolys; ++i)
            {
                int t = i * maxVertsPerPoly * 2;
                for (int j = 0; j < maxVertsPerPoly; ++j)
                {
                    if (polys[t + j] == DT_TILECACHE_NULL_IDX)
                        break;
                    int v0 = polys[t + j];
                    int v1 = (j + 1 >= maxVertsPerPoly || polys[t + j + 1] == DT_TILECACHE_NULL_IDX)
                        ? polys[t]
                        : polys[t + j + 1];
                    if (v0 < v1)
                    {
                        RcEdge edge = edges[edgeCount];
                        edge.vert[0] = v0;
                        edge.vert[1] = v1;
                        edge.poly[0] = i;
                        edge.polyEdge[0] = j;
                        edge.poly[1] = i;
                        edge.polyEdge[1] = 0xff;
                        // Insert edge
                        firstEdge[nextEdge + edgeCount] = firstEdge[v0];
                        firstEdge[v0] = (short)edgeCount;
                        edgeCount++;
                    }
                }
            }

            for (int i = 0; i < npolys; ++i)
            {
                int t = i * maxVertsPerPoly * 2;
                for (int j = 0; j < maxVertsPerPoly; ++j)
                {
                    if (polys[t + j] == DT_TILECACHE_NULL_IDX)
                        break;
                    int v0 = polys[t + j];
                    int v1 = (j + 1 >= maxVertsPerPoly || polys[t + j + 1] == DT_TILECACHE_NULL_IDX)
                        ? polys[t]
                        : polys[t + j + 1];
                    if (v0 > v1)
                    {
                        bool found = false;
                        for (int e = firstEdge[v1]; e != DT_TILECACHE_NULL_IDX; e = firstEdge[nextEdge + e])
                        {
                            RcEdge edge = edges[e];
                            if (edge.vert[1] == v0 && edge.poly[0] == edge.poly[1])
                            {
                                edge.poly[1] = i;
                                edge.polyEdge[1] = j;
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            // Matching edge not found, it is an open edge, add it.
                            RcEdge edge = edges[edgeCount];
                            edge.vert[0] = v1;
                            edge.vert[1] = v0;
                            edge.poly[0] = (short)i;
                            edge.polyEdge[0] = (short)j;
                            edge.poly[1] = (short)i;
                            edge.polyEdge[1] = 0xff;
                            // Insert edge
                            firstEdge[nextEdge + edgeCount] = firstEdge[v1];
                            firstEdge[v1] = (short)edgeCount;
                            edgeCount++;
                        }
                    }
                }
            }

            // Mark portal edges.
            for (int i = 0; i < lcset.nconts; ++i)
            {
                DtTileCacheContour cont = lcset.conts[i];
                if (cont.nverts < 3)
                    continue;

                for (int j = 0, k = cont.nverts - 1; j < cont.nverts; k = j++)
                {
                    int va = k * 4;
                    int vb = j * 4;
                    int dir = cont.verts[va + 3] & 0xf;
                    if (dir == 0xf)
                        continue;

                    if (dir == 0 || dir == 2)
                    {
                        // Find matching vertical edge
                        int x = cont.verts[va];
                        int zmin = cont.verts[va + 2];
                        int zmax = cont.verts[vb + 2];
                        if (zmin > zmax)
                        {
                            int tmp = zmin;
                            zmin = zmax;
                            zmax = tmp;
                        }

                        for (int m = 0; m < edgeCount; ++m)
                        {
                            RcEdge e = edges[m];
                            // Skip connected edges.
                            if (e.poly[0] != e.poly[1])
                                continue;
                            int eva = e.vert[0] * 3;
                            int evb = e.vert[1] * 3;
                            if (verts[eva] == x && verts[evb] == x)
                            {
                                int ezmin = verts[eva + 2];
                                int ezmax = verts[evb + 2];
                                if (ezmin > ezmax)
                                {
                                    int tmp = ezmin;
                                    ezmin = ezmax;
                                    ezmax = tmp;
                                }

                                if (OverlapRangeExl(zmin, zmax, ezmin, ezmax))
                                {
                                    // Reuse the other polyedge to store dir.
                                    e.polyEdge[1] = dir;
                                }
                            }
                        }
                    }
                    else
                    {
                        // Find matching vertical edge
                        int z = cont.verts[va + 2];
                        int xmin = cont.verts[va];
                        int xmax = cont.verts[vb];
                        if (xmin > xmax)
                        {
                            int tmp = xmin;
                            xmin = xmax;
                            xmax = tmp;
                        }

                        for (int m = 0; m < edgeCount; ++m)
                        {
                            RcEdge e = edges[m];
                            // Skip connected edges.
                            if (e.poly[0] != e.poly[1])
                                continue;
                            int eva = e.vert[0] * 3;
                            int evb = e.vert[1] * 3;
                            if (verts[eva + 2] == z && verts[evb + 2] == z)
                            {
                                int exmin = verts[eva];
                                int exmax = verts[evb];
                                if (exmin > exmax)
                                {
                                    int tmp = exmin;
                                    exmin = exmax;
                                    exmax = tmp;
                                }

                                if (OverlapRangeExl(xmin, xmax, exmin, exmax))
                                {
                                    // Reuse the other polyedge to store dir.
                                    e.polyEdge[1] = dir;
                                }
                            }
                        }
                    }
                }
            }

            // Store adjacency
            for (int i = 0; i < edgeCount; ++i)
            {
                RcEdge e = edges[i];
                if (e.poly[0] != e.poly[1])
                {
                    int p0 = e.poly[0] * maxVertsPerPoly * 2;
                    int p1 = e.poly[1] * maxVertsPerPoly * 2;
                    polys[p0 + maxVertsPerPoly + e.polyEdge[0]] = e.poly[1];
                    polys[p1 + maxVertsPerPoly + e.polyEdge[1]] = e.poly[0];
                }
                else if (e.polyEdge[1] != 0xff)
                {
                    int p0 = e.poly[0] * maxVertsPerPoly * 2;
                    polys[p0 + maxVertsPerPoly + e.polyEdge[0]] = 0x8000 | (short)e.polyEdge[1];
                }
            }
        }

        public static bool OverlapRangeExl(int amin, int amax, int bmin, int bmax)
        {
            return (amin >= bmax || amax <= bmin) ? false : true;
        }

        public static int Prev(int i, int n)
        {
            return i - 1 >= 0 ? i - 1 : n - 1;
        }

        public static int Next(int i, int n)
        {
            return i + 1 < n ? i + 1 : 0;
        }

        public static int Area2(int[] verts, int a, int b, int c)
        {
            return (verts[b] - verts[a]) * (verts[c + 2] - verts[a + 2])
                   - (verts[c] - verts[a]) * (verts[b + 2] - verts[a + 2]);
        }

        // Returns true iff c is strictly to the left of the directed
        // line through a to b.
        public static bool Left(int[] verts, int a, int b, int c)
        {
            return Area2(verts, a, b, c) < 0;
        }

        public static bool LeftOn(int[] verts, int a, int b, int c)
        {
            return Area2(verts, a, b, c) <= 0;
        }

        public static bool Collinear(int[] verts, int a, int b, int c)
        {
            return Area2(verts, a, b, c) == 0;
        }

        // Returns true iff ab properly intersects cd: they share
        // a point interior to both segments. The properness of the
        // intersection is ensured by using strict leftness.
        public static bool IntersectProp(int[] verts, int a, int b, int c, int d)
        {
            // Eliminate improper cases.
            if (Collinear(verts, a, b, c) || Collinear(verts, a, b, d) || Collinear(verts, c, d, a)
                || Collinear(verts, c, d, b))
                return false;

            return (Left(verts, a, b, c) ^ Left(verts, a, b, d)) && (Left(verts, c, d, a) ^ Left(verts, c, d, b));
        }

        // Returns T iff (a,b,c) are collinear and point c lies
        // on the closed segment ab.
        public static bool Between(int[] verts, int a, int b, int c)
        {
            if (!Collinear(verts, a, b, c))
                return false;
            // If ab not vertical, check betweenness on x; else on y.
            if (verts[a] != verts[b])
                return ((verts[a] <= verts[c]) && (verts[c] <= verts[b]))
                       || ((verts[a] >= verts[c]) && (verts[c] >= verts[b]));
            else
                return ((verts[a + 2] <= verts[c + 2]) && (verts[c + 2] <= verts[b + 2]))
                       || ((verts[a + 2] >= verts[c + 2]) && (verts[c + 2] >= verts[b + 2]));
        }

        // Returns true iff segments ab and cd intersect, properly or improperly.
        public static bool Intersect(int[] verts, int a, int b, int c, int d)
        {
            if (IntersectProp(verts, a, b, c, d))
                return true;
            else if (Between(verts, a, b, c) || Between(verts, a, b, d) || Between(verts, c, d, a)
                     || Between(verts, c, d, b))
                return true;
            else
                return false;
        }

        public static bool Vequal(int[] verts, int a, int b)
        {
            return verts[a] == verts[b] && verts[a + 2] == verts[b + 2];
        }

        // Returns T iff (v_i, v_j) is a proper internal *or* external
        // diagonal of P, *ignoring edges incident to v_i and v_j*.
        public static bool Diagonalie(int i, int j, int n, int[] verts, int[] indices)
        {
            int d0 = (indices[i] & 0x7fff) * 4;
            int d1 = (indices[j] & 0x7fff) * 4;

            // For each edge (k,k+1) of P
            for (int k = 0; k < n; k++)
            {
                int k1 = Next(k, n);
                // Skip edges incident to i or j
                if (!((k == i) || (k1 == i) || (k == j) || (k1 == j)))
                {
                    int p0 = (indices[k] & 0x7fff) * 4;
                    int p1 = (indices[k1] & 0x7fff) * 4;

                    if (Vequal(verts, d0, p0) || Vequal(verts, d1, p0) || Vequal(verts, d0, p1) || Vequal(verts, d1, p1))
                        continue;

                    if (Intersect(verts, d0, d1, p0, p1))
                        return false;
                }
            }

            return true;
        }

        // Returns true iff the diagonal (i,j) is strictly internal to the
        // polygon P in the neighborhood of the i endpoint.
        public static bool InCone(int i, int j, int n, int[] verts, int[] indices)
        {
            int pi = (indices[i] & 0x7fff) * 4;
            int pj = (indices[j] & 0x7fff) * 4;
            int pi1 = (indices[Next(i, n)] & 0x7fff) * 4;
            int pin1 = (indices[Prev(i, n)] & 0x7fff) * 4;

            // If P[i] is a convex vertex [ i+1 left or on (i-1,i) ].
            if (LeftOn(verts, pin1, pi, pi1))
                return Left(verts, pi, pj, pin1) && Left(verts, pj, pi, pi1);
            // Assume (i-1,i,i+1) not collinear.
            // else P[i] is reflex.
            return !(LeftOn(verts, pi, pj, pi1) && LeftOn(verts, pj, pi, pin1));
        }

        // Returns T iff (v_i, v_j) is a proper internal
        // diagonal of P.
        public static bool Diagonal(int i, int j, int n, int[] verts, int[] indices)
        {
            return InCone(i, j, n, verts, indices) && Diagonalie(i, j, n, verts, indices);
        }

        public static int Triangulate(int n, int[] verts, int[] indices, int[] tris)
        {
            int ntris = 0;
            int dst = 0; // tris;
            // The last bit of the index is used to indicate if the vertex can be
            // removed.
            for (int i = 0; i < n; i++)
            {
                int i1 = Next(i, n);
                int i2 = Next(i1, n);
                if (Diagonal(i, i2, n, verts, indices))
                    indices[i1] |= 0x8000;
            }

            while (n > 3)
            {
                int minLen = -1;
                int mini = -1;
                for (int mi = 0; mi < n; mi++)
                {
                    int mi1 = Next(mi, n);
                    if ((indices[mi1] & 0x8000) != 0)
                    {
                        int p0 = (indices[mi] & 0x7fff) * 4;
                        int p2 = (indices[Next(mi1, n)] & 0x7fff) * 4;

                        int dx = verts[p2] - verts[p0];
                        int dz = verts[p2 + 2] - verts[p0 + 2];
                        int len = dx * dx + dz * dz;
                        if (minLen < 0 || len < minLen)
                        {
                            minLen = len;
                            mini = mi;
                        }
                    }
                }

                if (mini == -1)
                {
                    // Should not happen.
                    /*
                     * Printf("mini == -1 ntris=%d n=%d\n", ntris, n); for (int i = 0; i < n; i++) { Printf("%d ",
                     * indices[i] & 0x0fffffff); } Printf("\n");
                     */
                    return -ntris;
                }

                int i = mini;
                int i1 = Next(i, n);
                int i2 = Next(i1, n);

                tris[dst++] = indices[i] & 0x7fff;
                tris[dst++] = indices[i1] & 0x7fff;
                tris[dst++] = indices[i2] & 0x7fff;
                ntris++;

                // Removes P[i1] by copying P[i+1]...P[n-1] left one index.
                n--;
                for (int k = i1; k < n; k++)
                    indices[k] = indices[k + 1];

                if (i1 >= n)
                    i1 = 0;
                i = Prev(i1, n);
                // Update diagonal flags.
                if (Diagonal(Prev(i, n), i1, n, verts, indices))
                    indices[i] |= 0x8000;
                else
                    indices[i] &= 0x7fff;

                if (Diagonal(i, Next(i1, n), n, verts, indices))
                    indices[i1] |= 0x8000;
                else
                    indices[i1] &= 0x7fff;
            }

            // Append the remaining triangle.
            tris[dst++] = indices[0] & 0x7fff;
            tris[dst++] = indices[1] & 0x7fff;
            tris[dst++] = indices[2] & 0x7fff;
            ntris++;

            return ntris;
        }

        public static int CountPolyVerts(int[] polys, int p, int maxVertsPerPoly)
        {
            for (int i = 0; i < maxVertsPerPoly; ++i)
                if (polys[p + i] == DT_TILECACHE_NULL_IDX)
                    return i;
            return maxVertsPerPoly;
        }

        public static bool Uleft(int[] verts, int a, int b, int c)
        {
            return (verts[b] - verts[a]) * (verts[c + 2] - verts[a + 2])
                - (verts[c] - verts[a]) * (verts[b + 2] - verts[a + 2]) < 0;
        }

        /// @param[in] allowCollinear  Accept unions that are only weakly convex. Off for the
        ///                            general merge loop, on for sliver absorption, whose whole
        ///                            purpose is to remove edges along straightened borders. A
        ///                            corner that comes out exactly straight bothers nothing
        ///                            downstream: Detour's point-in-polygon tests are inclusive,
        ///                            adjacency matches exact vertex pairs, and detail hulls
        ///                            already carry collinear vertices wherever an edge was
        ///                            tessellated.
        /// @param[in] maxMergedVerts  Vertex cap for the union; defaults to the per-polygon
        ///                            budget. Sliver absorption raises it to merge past the
        ///                            budget, on the promise of cutting the union back into two
        ///                            polygons that fit.
        public static int GetPolyMergeValue(int[] polys, int pa, int pb, int[] verts, out int ea, out int eb,
            int maxVertsPerPoly, bool allowCollinear = false, int maxMergedVerts = -1)
        {
            ea = 0;
            eb = 0;

            int na = CountPolyVerts(polys, pa, maxVertsPerPoly);
            int nb = CountPolyVerts(polys, pb, maxVertsPerPoly);

            // If the merged polygon would be too big, do not merge.
            if (na + nb - 2 > (maxMergedVerts > 0 ? maxMergedVerts : maxVertsPerPoly))
                return -1;

            // Check if the polygons share an edge.
            ea = -1;
            eb = -1;

            for (int i = 0; i < na; ++i)
            {
                int va0 = polys[pa + i];
                int va1 = polys[pa + (i + 1) % na];
                if (va0 > va1)
                {
                    (va0, va1) = (va1, va0);
                }

                for (int j = 0; j < nb; ++j)
                {
                    int vb0 = polys[pb + j];
                    int vb1 = polys[pb + (j + 1) % nb];
                    if (vb0 > vb1)
                    {
                        (vb0, vb1) = (vb1, vb0);
                    }

                    if (va0 == vb0 && va1 == vb1)
                    {
                        ea = i;
                        eb = j;
                        break;
                    }
                }
            }

            // No common edge, cannot merge.
            if (ea == -1 || eb == -1)
                return -1;

            // Check to see if the merged polygon would be convex.
            int va, vb, vc;

            va = polys[pa + (ea + na - 1) % na];
            vb = polys[pa + ea];
            vc = polys[pb + (eb + 2) % nb];
            if (!(allowCollinear ? LeftOn(verts, va * 3, vb * 3, vc * 3) : Uleft(verts, va * 3, vb * 3, vc * 3)))
                return -1;

            va = polys[pb + (eb + nb - 1) % nb];
            vb = polys[pb + eb];
            vc = polys[pa + (ea + 2) % na];
            if (!(allowCollinear ? LeftOn(verts, va * 3, vb * 3, vc * 3) : Uleft(verts, va * 3, vb * 3, vc * 3)))
                return -1;

            va = polys[pa + ea];
            vb = polys[pa + (ea + 1) % na];

            int dx = verts[va * 3 + 0] - verts[vb * 3 + 0];
            int dy = verts[va * 3 + 2] - verts[vb * 3 + 2];

            return (dx * dx) + (dy * dy);
        }

        public static void MergePolys(int[] polys, int pa, int pb, int ea, int eb, int maxVertsPerPoly)
        {
            int[] tmp = new int[maxVertsPerPoly * 2];

            int na = CountPolyVerts(polys, pa, maxVertsPerPoly);
            int nb = CountPolyVerts(polys, pb, maxVertsPerPoly);

            // Merge polygons.
            Array.Fill(tmp, DT_TILECACHE_NULL_IDX);
            int n = 0;
            // Add pa
            for (int i = 0; i < na - 1; ++i)
                tmp[n++] = polys[pa + (ea + 1 + i) % na];
            // Add pb
            for (int i = 0; i < nb - 1; ++i)
                tmp[n++] = polys[pb + (eb + 1 + i) % nb];
            RcArrays.Copy(tmp, 0, polys, pa, maxVertsPerPoly);
        }

        public static int PushFront(int v, List<int> arr)
        {
            arr.Insert(0, v);
            return arr.Count;
        }

        public static int PushBack(int v, List<int> arr)
        {
            arr.Add(v);
            return arr.Count;
        }

        public static bool CanRemoveVertex(DtTileCachePolyMesh mesh, int rem)
        {
            // Count number of polygons to remove.
            int maxVertsPerPoly = mesh.nvp;
            int numRemainingEdges = 0;
            for (int i = 0; i < mesh.npolys; ++i)
            {
                int p = i * mesh.nvp * 2;
                int nv = CountPolyVerts(mesh.polys, p, maxVertsPerPoly);
                int numRemoved = 0;
                int numVerts = 0;
                for (int j = 0; j < nv; ++j)
                {
                    if (mesh.polys[p + j] == rem)
                    {
                        numRemoved++;
                    }

                    numVerts++;
                }

                if (numRemoved != 0)
                {
                    numRemainingEdges += numVerts - (numRemoved + 1);
                }
            }

            // There would be too few edges remaining to create a polygon.
            // This can happen for example when a tip of a triangle is marked
            // as deletion, but there are no other polys that share the vertex.
            // In this case, the vertex should not be removed.
            if (numRemainingEdges <= 2)
                return false;

            // Find edges which share the removed vertex.
            List<int> edges = new List<int>();
            int nedges = 0;

            for (int i = 0; i < mesh.npolys; ++i)
            {
                int p = i * mesh.nvp * 2;
                int nv = CountPolyVerts(mesh.polys, p, maxVertsPerPoly);

                // Collect edges which touches the removed vertex.
                for (int j = 0, k = nv - 1; j < nv; k = j++)
                {
                    if (mesh.polys[p + j] == rem || mesh.polys[p + k] == rem)
                    {
                        // Arrange edge so that a=rem.
                        int a = mesh.polys[p + j], b = mesh.polys[p + k];
                        if (b == rem)
                        {
                            int tmp = a;
                            a = b;
                            b = tmp;
                        }

                        // Check if the edge exists
                        bool exists = false;
                        for (int m = 0; m < nedges; ++m)
                        {
                            int e = m * 3;
                            if (edges[e + 1] == b)
                            {
                                // Exists, increment vertex share count.
                                edges[e + 2] = edges[e + 2] + 1;
                                exists = true;
                            }
                        }

                        // Add new edge.
                        if (!exists)
                        {
                            edges.Add(a);
                            edges.Add(b);
                            edges.Add(1);
                            nedges++;
                        }
                    }
                }
            }

            // There should be no more than 2 open edges.
            // This catches the case that two non-adjacent polygons
            // share the removed vertex. In that case, do not remove the vertex.
            int numOpenEdges = 0;
            for (int i = 0; i < nedges; ++i)
            {
                if (edges[i * 3 + 2] < 2)
                    numOpenEdges++;
            }

            if (numOpenEdges > 2)
                return false;

            return true;
        }

        public static void RemoveVertex(DtTileCachePolyMesh mesh, int rem, int maxTris)
        {
            // Count number of polygons to remove.
            int maxVertsPerPoly = mesh.nvp;

            int nedges = 0;
            List<int> edges = new List<int>();
            int nhole = 0;
            List<int> hole = new List<int>();
            List<int> harea = new List<int>();

            for (int i = 0; i < mesh.npolys; ++i)
            {
                int p = i * maxVertsPerPoly * 2;
                int nv = CountPolyVerts(mesh.polys, p, maxVertsPerPoly);
                bool hasRem = false;
                for (int j = 0; j < nv; ++j)
                {
                    if (mesh.polys[p + j] == rem)
                    {
                        hasRem = true;
                    }
                }

                if (hasRem)
                {
                    // Collect edges which does not touch the removed vertex.
                    for (int j = 0, k = nv - 1; j < nv; k = j++)
                    {
                        if (mesh.polys[p + j] != rem && mesh.polys[p + k] != rem)
                        {
                            edges.Add(mesh.polys[p + k]);
                            edges.Add(mesh.polys[p + j]);
                            edges.Add(mesh.areas[i]);
                            nedges++;
                        }
                    }

                    // Remove the polygon.
                    int p2 = (mesh.npolys - 1) * maxVertsPerPoly * 2;
                    RcArrays.Copy(mesh.polys, p2, mesh.polys, p, maxVertsPerPoly);
                    Array.Fill(mesh.polys, DT_TILECACHE_NULL_IDX, p + maxVertsPerPoly, maxVertsPerPoly);
                    mesh.areas[i] = mesh.areas[mesh.npolys - 1];
                    mesh.npolys--;
                    --i;
                }
            }

            // Remove vertex.
            for (int i = rem; i < mesh.nverts - 1; ++i)
            {
                mesh.verts[i * 3 + 0] = mesh.verts[(i + 1) * 3 + 0];
                mesh.verts[i * 3 + 1] = mesh.verts[(i + 1) * 3 + 1];
                mesh.verts[i * 3 + 2] = mesh.verts[(i + 1) * 3 + 2];
            }

            mesh.nverts--;

            // Adjust indices to match the removed vertex layout.
            for (int i = 0; i < mesh.npolys; ++i)
            {
                int p = i * maxVertsPerPoly * 2;
                int nv = CountPolyVerts(mesh.polys, p, maxVertsPerPoly);
                for (int j = 0; j < nv; ++j)
                {
                    if (mesh.polys[p + j] > rem)
                    {
                        mesh.polys[p + j]--;
                    }
                }
            }

            for (int i = 0; i < nedges; ++i)
            {
                if (edges[i * 3] > rem)
                    edges[i * 3] = edges[i * 3] - 1;
                if (edges[i * 3 + 1] > rem)
                    edges[i * 3 + 1] = edges[i * 3 + 1] - 1;
            }

            if (nedges == 0)
                return;

            // Start with one vertex, keep appending connected
            // segments to the start and end of the hole.
            nhole = PushBack(edges[0], hole);
            PushBack(edges[2], harea);

            while (nedges != 0)
            {
                bool match = false;

                for (int i = 0; i < nedges; ++i)
                {
                    int ea = edges[i * 3];
                    int eb = edges[i * 3 + 1];
                    int a = edges[i * 3 + 2];
                    bool add = false;
                    if (hole[0] == eb)
                    {
                        // The segment matches the beginning of the hole boundary.
                        nhole = PushFront(ea, hole);
                        PushFront(a, harea);
                        add = true;
                    }
                    else if (hole[nhole - 1] == ea)
                    {
                        // The segment matches the end of the hole boundary.
                        nhole = PushBack(eb, hole);
                        PushBack(a, harea);
                        add = true;
                    }

                    if (add)
                    {
                        // The edge segment was added, remove it.
                        edges[i * 3] = edges[(nedges - 1) * 3];
                        edges[i * 3 + 1] = edges[(nedges - 1) * 3] + 1;
                        edges[i * 3 + 2] = edges[(nedges - 1) * 3] + 2;
                        --nedges;
                        match = true;
                        --i;
                    }
                }

                if (!match)
                    break;
            }

            int[] tris = new int[nhole * 3];
            int[] tverts = new int[nhole * 4];
            int[] tpoly = new int[nhole];

            // Generate temp vertex array for triangulation.
            for (int i = 0; i < nhole; ++i)
            {
                int pi = hole[i];
                tverts[i * 4 + 0] = mesh.verts[pi * 3 + 0];
                tverts[i * 4 + 1] = mesh.verts[pi * 3 + 1];
                tverts[i * 4 + 2] = mesh.verts[pi * 3 + 2];
                tverts[i * 4 + 3] = 0;
                tpoly[i] = i;
            }

            // Triangulate the hole.
            int ntris = Triangulate(nhole, tverts, tpoly, tris);
            if (ntris < 0)
            {
                // TODO: issue warning!
                ntris = -ntris;
            }

            int[] polys = new int[ntris * maxVertsPerPoly];
            int[] pareas = new int[ntris];

            // Build initial polygons.
            int npolys = 0;
            Array.Fill(polys, DT_TILECACHE_NULL_IDX, 0, ntris * maxVertsPerPoly);
            for (int j = 0; j < ntris; ++j)
            {
                int t = j * 3;
                if (tris[t] != tris[t + 1] && tris[t] != tris[t + 2] && tris[t + 1] != tris[t + 2])
                {
                    polys[npolys * maxVertsPerPoly + 0] = hole[tris[t]];
                    polys[npolys * maxVertsPerPoly + 1] = hole[tris[t + 1]];
                    polys[npolys * maxVertsPerPoly + 2] = hole[tris[t + 2]];
                    pareas[npolys] = harea[tris[t]];
                    npolys++;
                }
            }

            if (npolys == 0)
                return;

            // Merge polygons.
            if (maxVertsPerPoly > 3)
            {
                for (;;)
                {
                    // Find best polygons to merge.
                    int bestMergeVal = 0;
                    int bestPa = 0, bestPb = 0, bestEa = 0, bestEb = 0;

                    for (int j = 0; j < npolys - 1; ++j)
                    {
                        int pj = j * maxVertsPerPoly;
                        for (int k = j + 1; k < npolys; ++k)
                        {
                            int pk = k * maxVertsPerPoly;
                            int v = GetPolyMergeValue(polys, pj, pk, mesh.verts, out var ea, out var eb, maxVertsPerPoly);
                            if (v > bestMergeVal)
                            {
                                bestMergeVal = v;
                                bestPa = j;
                                bestPb = k;
                                bestEa = ea;
                                bestEb = eb;
                            }
                        }
                    }

                    if (bestMergeVal > 0)
                    {
                        // Found best, merge.
                        int pa = bestPa * maxVertsPerPoly;
                        int pb = bestPb * maxVertsPerPoly;
                        MergePolys(polys, pa, pb, bestEa, bestEb, maxVertsPerPoly);
                        RcArrays.Copy(polys, (npolys - 1) * maxVertsPerPoly, polys, pb, maxVertsPerPoly);
                        pareas[bestPb] = pareas[npolys - 1];
                        npolys--;
                    }
                    else
                    {
                        // Could not merge any polygons, stop.
                        break;
                    }
                }
            }

            // Store polygons.
            for (int i = 0; i < npolys; ++i)
            {
                if (mesh.npolys >= maxTris)
                    break;

                int p = mesh.npolys * maxVertsPerPoly * 2;
                Array.Fill(mesh.polys, DT_TILECACHE_NULL_IDX, p, maxVertsPerPoly * 2);

                for (int j = 0; j < maxVertsPerPoly; ++j)
                    mesh.polys[p + j] = polys[i * maxVertsPerPoly + j];

                mesh.areas[mesh.npolys] = pareas[i];
                mesh.npolys++;
                if (mesh.npolys > maxTris)
                {
                    throw new Exception("Buffer too small");
                }
            }
        }

        public static DtTileCachePolyMesh BuildTileCachePolyMesh(DtTileCacheContourSet lcset, int maxVertsPerPoly)
        {
            int maxVertices = 0;
            int maxTris = 0;
            int maxVertsPerCont = 0;
            for (int i = 0; i < lcset.nconts; ++i)
            {
                // Skip null contours.
                if (lcset.conts[i].nverts < 3)
                    continue;
                maxVertices += lcset.conts[i].nverts;
                maxTris += lcset.conts[i].nverts - 2;
                maxVertsPerCont = Math.Max(maxVertsPerCont, lcset.conts[i].nverts);
            }

            // TODO: warn about too many vertices?

            DtTileCachePolyMesh mesh = new DtTileCachePolyMesh(maxVertsPerPoly);

            int[] vflags = new int[maxVertices];

            mesh.verts = new int[maxVertices * 3];
            mesh.polys = new int[maxTris * maxVertsPerPoly * 2];
            mesh.areas = new int[maxTris];
            // Just allocate and clean the mesh flags array. The user is resposible
            // for filling it.
            mesh.flags = new int[maxTris];

            mesh.nverts = 0;
            mesh.npolys = 0;

            Array.Fill(mesh.polys, DT_TILECACHE_NULL_IDX);

            int[] firstVert = new int[VERTEX_BUCKET_COUNT2];
            for (int i = 0; i < VERTEX_BUCKET_COUNT2; ++i)
                firstVert[i] = DT_TILECACHE_NULL_IDX;

            int[] nextVert = new int[maxVertices];
            int[] indices = new int[maxVertsPerCont];
            int[] tris = new int[maxVertsPerCont * 3];
            int[] polys = new int[maxVertsPerCont * maxVertsPerPoly];

            for (int i = 0; i < lcset.nconts; ++i)
            {
                DtTileCacheContour cont = lcset.conts[i];

                // Skip null contours.
                if (cont.nverts < 3)
                    continue;

                // Triangulate contour
                for (int j = 0; j < cont.nverts; ++j)
                    indices[j] = j;

                int ntris = Triangulate(cont.nverts, cont.verts, indices, tris);
                if (ntris <= 0)
                {
                    // TODO: issue warning!
                    ntris = -ntris;
                }

                // Add and merge vertices.
                for (int j = 0; j < cont.nverts; ++j)
                {
                    int v = j * 4;
                    indices[j] = AddVertex(cont.verts[v], cont.verts[v + 1], cont.verts[v + 2], mesh.verts, firstVert,
                        nextVert, mesh.nverts);
                    mesh.nverts = Math.Max(mesh.nverts, indices[j] + 1);
                    if ((cont.verts[v + 3] & 0x80) != 0)
                    {
                        // This vertex should be removed.
                        vflags[indices[j]] = 1;
                    }
                }

                // Build initial polygons.
                int npolys = 0;
                Array.Fill(polys, DT_TILECACHE_NULL_IDX);
                for (int j = 0; j < ntris; ++j)
                {
                    int t = j * 3;
                    if (tris[t] != tris[t + 1] && tris[t] != tris[t + 2] && tris[t + 1] != tris[t + 2])
                    {
                        polys[npolys * maxVertsPerPoly + 0] = indices[tris[t]];
                        polys[npolys * maxVertsPerPoly + 1] = indices[tris[t + 1]];
                        polys[npolys * maxVertsPerPoly + 2] = indices[tris[t + 2]];
                        npolys++;
                    }
                }

                if (npolys == 0)
                    continue;

                // Merge polygons.
                if (maxVertsPerPoly > 3)
                {
                    for (;;)
                    {
                        // Find best polygons to merge.
                        int bestMergeVal = 0;
                        int bestPa = 0, bestPb = 0, bestEa = 0, bestEb = 0;

                        for (int j = 0; j < npolys - 1; ++j)
                        {
                            int pj = j * maxVertsPerPoly;
                            for (int k = j + 1; k < npolys; ++k)
                            {
                                int pk = k * maxVertsPerPoly;
                                int v = GetPolyMergeValue(polys, pj, pk, mesh.verts, out var ea, out var eb, maxVertsPerPoly);
                                if (v > bestMergeVal)
                                {
                                    bestMergeVal = v;
                                    bestPa = j;
                                    bestPb = k;
                                    bestEa = ea;
                                    bestEb = eb;
                                }
                            }
                        }

                        if (bestMergeVal > 0)
                        {
                            // Found best, merge.
                            int pa = bestPa * maxVertsPerPoly;
                            int pb = bestPb * maxVertsPerPoly;
                            MergePolys(polys, pa, pb, bestEa, bestEb, maxVertsPerPoly);
                            RcArrays.Copy(polys, (npolys - 1) * maxVertsPerPoly, polys, pb, maxVertsPerPoly);
                            npolys--;
                        }
                        else
                        {
                            // Could not merge any polygons, stop.
                            break;
                        }
                    }
                }

                // Store polygons.
                for (int j = 0; j < npolys; ++j)
                {
                    int p = mesh.npolys * maxVertsPerPoly * 2;
                    int q = j * maxVertsPerPoly;
                    for (int k = 0; k < maxVertsPerPoly; ++k)
                        mesh.polys[p + k] = polys[q + k];
                    mesh.areas[mesh.npolys] = cont.area;
                    mesh.npolys++;
                    if (mesh.npolys > maxTris)
                        throw new Exception("Buffer too small");
                }
            }

            // Remove edge vertices.
            for (int i = 0; i < mesh.nverts; ++i)
            {
                if (vflags[i] != 0)
                {
                    if (!CanRemoveVertex(mesh, i))
                        continue;
                    RemoveVertex(mesh, i, maxTris);
                    // Remove vertex
                    // Note: mesh.nverts is already decremented inside
                    // RemoveVertex()!
                    for (int j = i; j < mesh.nverts; ++j)
                        vflags[j] = vflags[j + 1];
                    --i;
                }
            }

            // Absorb slivers. After vertex removal, whose hole re-triangulation would otherwise
            // put back what this takes out, and before adjacency, which the merges change.
            AbsorbSliverPolys(mesh, maxVertsPerPoly);

            // Calculate adjacency.
            BuildMeshAdjacency(mesh.polys, mesh.npolys, mesh.verts, mesh.nverts, lcset, maxVertsPerPoly);

            return mesh;
        }

        /// XZ width in voxels under which a polygon counts as a sliver worth merging away.
        /// Observed slivers are 0.3-0.7 voxels across; three also catches the wedge polygons
        /// that taper to a pinched tip at a seam corner, whose forced near-vertical facets and
        /// sampling margins otherwise leak through every later stage. Matching too eagerly
        /// costs nothing but a chunkier polygon, since merging never changes which ground is
        /// walkable.
        private const int SliverWidthVoxels = 3;

        /// @par
        ///
        /// Merges sliver polygons into a neighbour of the same area, deleting the interior edge
        /// that pens them in; where every union overflows the vertex budget, merges past it and
        /// cuts the union back into two polygons that fit. A strip a fraction of a voxel wide
        /// triangulates into nothing but a near-vertical facet, since its correctly-placed corners
        /// take the whole rise along its length across almost no width.
        ///
        /// Runs on the stored mesh, not one contour's scratch polygons: a sliver is frequently a
        /// region to itself and has no partner inside its own contour.
        ///
        /// Only the shape of the walkable surface changes, never its extent: no vertex moves and
        /// the outline is untouched, so tile portals still match their neighbours edge for edge.
        private static void AbsorbSliverPolys(DtTileCachePolyMesh mesh, int maxVertsPerPoly)
        {
            if (maxVertsPerPoly <= 3)
                return;

            int[] ring = new int[maxVertsPerPoly * 2];
            int[] bestRing = new int[maxVertsPerPoly * 2];
            int[] half = new int[maxVertsPerPoly];

            // Which polygons are slivers, kept rather than re-derived: a merge changes the shape
            // of exactly one polygon and moves one other into the freed slot, so every other
            // answer still holds.
            bool[] isSliver = new bool[mesh.npolys];
            for (int i = 0; i < mesh.npolys; ++i)
                isSliver[i] = IsSliverPoly(mesh, i, maxVertsPerPoly, SliverWidthVoxels);

            for (;;)
            {
                // Best in-budget merge over every sliver. The highest merge value is the longest
                // shared edge, which is the interior wall most worth deleting.
                int bestMergeVal = 0;
                int bestPa = 0, bestPb = 0, bestEa = 0, bestEb = 0;

                for (int i = 0; i < mesh.npolys; ++i)
                {
                    if (!isSliver[i])
                        continue;

                    for (int j = 0; j < mesh.npolys; ++j)
                    {
                        // Regions mean nothing to Detour, so merging across one is fine and is
                        // the point; areas are what a query filter reads and must not blend.
                        if (j == i || mesh.areas[j] != mesh.areas[i])
                            continue;

                        int v = GetPolyMergeValue(mesh.polys, i * maxVertsPerPoly * 2, j * maxVertsPerPoly * 2,
                            mesh.verts, out var ea, out var eb, maxVertsPerPoly, allowCollinear: true);
                        if (v > bestMergeVal)
                        {
                            bestMergeVal = v;
                            bestPa = i;
                            bestPb = j;
                            bestEa = ea;
                            bestEb = eb;
                        }
                    }
                }

                if (bestMergeVal > 0)
                {
                    // Merge into the lower slot, so the compaction below cannot move the polygon
                    // just merged into. The per-contour merge loop gets the same invariant from
                    // searching ordered pairs; this pass searches both directions and has to
                    // restore it.
                    if (bestPa > bestPb)
                    {
                        (bestPa, bestPb) = (bestPb, bestPa);
                        (bestEa, bestEb) = (bestEb, bestEa);
                    }

                    MergePolys(mesh.polys, bestPa * maxVertsPerPoly * 2, bestPb * maxVertsPerPoly * 2,
                        bestEa, bestEb, maxVertsPerPoly);

                    int last = mesh.npolys - 1;
                    if (bestPb != last)
                    {
                        RcArrays.Copy(mesh.polys, last * maxVertsPerPoly * 2, mesh.polys, bestPb * maxVertsPerPoly * 2,
                            maxVertsPerPoly * 2);
                        mesh.areas[bestPb] = mesh.areas[last];
                        mesh.flags[bestPb] = mesh.flags[last];
                        isSliver[bestPb] = isSliver[last];
                    }

                    isSliver[bestPa] = IsSliverPoly(mesh, bestPa, maxVertsPerPoly, SliverWidthVoxels);
                    mesh.npolys--;
                    continue;
                }

                // Every remaining sliver's unions overflow the vertex budget (a sliver plus a
                // full hexagon is the common case). Merge past the budget and cut the union in
                // two along the diagonal that leaves the narrower half widest. Both halves must
                // clear the sliver threshold — by the same exact arithmetic the hunt above uses,
                // or the pass would chase its own output — so every cut retires a sliver for
                // good, which is also why this terminates. Polygon count is unchanged: two in,
                // one union, two out.
                double bestScore = -1;
                int splitPa = -1, splitPb = 0, bestS = 0, bestT = 0, bestN = 0;

                for (int i = 0; i < mesh.npolys; ++i)
                {
                    if (!isSliver[i])
                        continue;

                    for (int j = 0; j < mesh.npolys; ++j)
                    {
                        if (j == i || mesh.areas[j] != mesh.areas[i])
                            continue;

                        int v = GetPolyMergeValue(mesh.polys, i * maxVertsPerPoly * 2, j * maxVertsPerPoly * 2,
                            mesh.verts, out var ea, out var eb, maxVertsPerPoly, allowCollinear: true,
                            maxMergedVerts: maxVertsPerPoly * 2 - 2);
                        if (v <= 0)
                            continue;

                        int n = BuildMergedRing(mesh.polys, i * maxVertsPerPoly * 2, j * maxVertsPerPoly * 2,
                            ea, eb, maxVertsPerPoly, ring);
                        if (FindRingSplit(ring, n, mesh.verts, maxVertsPerPoly, half, out int s, out int t, out double score)
                            && score > bestScore)
                        {
                            bestScore = score;
                            splitPa = i;
                            splitPb = j;
                            bestS = s;
                            bestT = t;
                            bestN = n;
                            RcArrays.Copy(ring, 0, bestRing, 0, n);
                        }
                    }
                }

                if (splitPa < 0)
                    break;

                WriteRingRange(mesh.polys, splitPa * maxVertsPerPoly * 2, bestRing, bestS, bestT, bestN, maxVertsPerPoly);
                WriteRingRange(mesh.polys, splitPb * maxVertsPerPoly * 2, bestRing, bestT, bestS, bestN, maxVertsPerPoly);
                isSliver[splitPa] = IsSliverPoly(mesh, splitPa, maxVertsPerPoly, SliverWidthVoxels);
                isSliver[splitPb] = IsSliverPoly(mesh, splitPb, maxVertsPerPoly, SliverWidthVoxels);
            }
        }

        private static bool IsSliverPoly(DtTileCachePolyMesh mesh, int poly, int maxVertsPerPoly, int widthVoxels)
        {
            int p = poly * maxVertsPerPoly * 2;
            int n = CountPolyVerts(mesh.polys, p, maxVertsPerPoly);
            return n >= 3 && IsSliverRing(mesh.polys, p, n, mesh.verts, widthVoxels);
        }

        /// Whether a polygon ring covers a strip narrower than @p widthVoxels: twice the XZ area
        /// over the longest edge is the width of that strip.
        private static bool IsSliverRing(int[] polys, int p, int n, int[] verts, int widthVoxels)
        {
            RingSpan(polys, p, n, verts, out long area2, out long maxEdgeSq);
            return area2 * area2 < (long)widthVoxels * widthVoxels * maxEdgeSq;
        }

        /// Twice the XZ area of a vertex ring and its longest squared edge. Integer voxel maths
        /// like the rest of the builder, widened to long because the squared area overflows an
        /// int on a full layer.
        private static void RingSpan(int[] ring, int p, int n, int[] verts, out long area2, out long maxEdgeSq)
        {
            area2 = 0;
            maxEdgeSq = 0;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                int a = ring[p + j] * 3;
                int b = ring[p + i] * 3;
                area2 += ((long)verts[a] * verts[b + 2]) - ((long)verts[b] * verts[a + 2]);

                long dx = verts[b] - verts[a];
                long dz = verts[b + 2] - verts[a + 2];
                maxEdgeSq = Math.Max(maxEdgeSq, (dx * dx) + (dz * dz));
            }

            area2 = Math.Abs(area2);
        }

        /// The union ring of two polygons sharing edge @p ea / @p eb, laid out the way MergePolys
        /// lays it out — first polygon's vertices from past the shared edge around, then the
        /// second's — except into caller scratch, because here the union may exceed the
        /// per-polygon budget. Returns the ring's vertex count.
        private static int BuildMergedRing(int[] polys, int pa, int pb, int ea, int eb, int maxVertsPerPoly, int[] ring)
        {
            int na = CountPolyVerts(polys, pa, maxVertsPerPoly);
            int nb = CountPolyVerts(polys, pb, maxVertsPerPoly);
            int n = 0;
            for (int i = 0; i < na - 1; ++i)
                ring[n++] = polys[pa + (ea + 1 + i) % na];
            for (int i = 0; i < nb - 1; ++i)
                ring[n++] = polys[pb + (eb + 1 + i) % nb];
            return n;
        }

        /// The diagonal cutting @p ring into the two most usable polygons: both inside the vertex
        /// budget, neither a sliver, and the narrower of the two as wide as any cut can make it.
        /// The ring is convex — the merge checked its two seams and every other corner came from
        /// a convex polygon unchanged — so any diagonal lies inside it and no intersection tests
        /// are needed. False when every cut leaves a sliver behind.
        private static bool FindRingSplit(int[] ring, int n, int[] verts, int maxVertsPerPoly, int[] half,
            out int s, out int t, out double score)
        {
            s = 0;
            t = 0;
            score = -1;
            for (int a = 0; a < n - 2; ++a)
            {
                for (int b = a + 2; b < n; ++b)
                {
                    if (a == 0 && b == n - 1)
                        continue; // consecutive around the wrap: a ring edge, not a diagonal

                    if (b - a + 1 > maxVertsPerPoly || n - (b - a) + 1 > maxVertsPerPoly)
                        continue;

                    if (!TryHalf(ring, a, b, n, verts, half, out double w1)
                        || !TryHalf(ring, b, a, n, verts, half, out double w2))
                        continue;

                    double d = Math.Min(w1, w2);
                    if (d > score)
                    {
                        score = d;
                        s = a;
                        t = b;
                    }
                }
            }

            return score >= 0;
        }

        /// Measures the sub-ring from @p from to @p to inclusive, wrapping. False when the piece
        /// is itself a sliver, judged by the same exact arithmetic the absorption pass hunts
        /// with; the width comes back as a double only to rank cuts.
        private static bool TryHalf(int[] ring, int from, int to, int n, int[] verts, int[] half, out double width)
        {
            int count = ((to - from) + n) % n + 1;
            for (int i = 0; i < count; ++i)
                half[i] = ring[(from + i) % n];

            RingSpan(half, 0, count, verts, out long area2, out long maxEdgeSq);
            width = maxEdgeSq > 0 ? area2 / Math.Sqrt(maxEdgeSq) : 0;
            return area2 * area2 >= (long)SliverWidthVoxels * SliverWidthVoxels * maxEdgeSq;
        }

        /// Writes the sub-ring from @p from to @p to inclusive (wrapping) into a polygon slot's
        /// vertex half, padding the remainder with the null index. The adjacency half is left
        /// alone: it is computed after this pass.
        private static void WriteRingRange(int[] polys, int p, int[] ring, int from, int to, int n, int maxVertsPerPoly)
        {
            int count = ((to - from) + n) % n + 1;
            for (int k = 0; k < maxVertsPerPoly; ++k)
                polys[p + k] = k < count ? ring[(from + k) % n] : DT_TILECACHE_NULL_IDX;
        }

        /// The height a layer cell carries where the layer has no surface. Layers are built with a
        /// span under 255 voxels, which is what leaves this value free to mean "nothing here".
        internal const int NoSurface = 0xFF;

        /// How many cells of the neighbouring tiles' layers a tile borrows around its edge when
        /// it meshes. The standard tiled pipeline rasterizes past the tile edge for the same
        /// reason: with the seam's surroundings present on both sides, corner heights, contours
        /// and height detail at the seam are computed from the same world cells by both tiles,
        /// and the seam closes by construction. Two cells covers everything that reads across
        /// the boundary — corner neighbourhoods and detail height patches alike.
        public const int SeamBorder = 2;

        /// A tile's layer widened by #SeamBorder cells of its neighbours' layers, heights
        /// rebased into this tile's units. -1 marks no surface — rebased neighbour heights can
        /// take any value, so the layer's own 0xFF sentinel is not safe here.
        public class DtTileBorderGrid
        {
            public readonly int width;   // core width, without the border
            public readonly int height;  // core height, without the border
            public readonly int border;
            public readonly int[] heights;
            public readonly int[] areas;

            public DtTileBorderGrid(int width, int height, int border)
            {
                this.width = width;
                this.height = height;
                this.border = border;
                int gw = width + border * 2, gh = height + border * 2;
                heights = new int[gw * gh];
                areas = new int[gw * gh];
                Array.Fill(heights, -1);
            }

            /// Index by core cell coordinates; the border lives at -border..-1 and width..width+border-1.
            public int Index(int x, int z) => (x + border) + (z + border) * (width + border * 2);
        }

        /// @par
        ///
        /// Partitions and contours a tile with the standard pipeline instead of the cache's
        /// classic monotone sweep: a watershed over a distance field for regions, and
        /// rcBuildContours for outlines — which handles regions that enclose holes (the classic
        /// walker traces one loop per region and silently drops the rest), takes corner heights
        /// through span connectivity rather than any cell within climbing reach, and splits
        /// contour edges longer than @p DtTileCacheParams.maxEdgeLen so open ground polygonizes
        /// into compact local polygons instead of fans that reach across the tile.
        ///
        /// Returns null when the tile needs the classic path instead: when any interior cell
        /// carries a portal to another vertical layer — the heightfield here holds one layer, so
        /// the standard contourer cannot place the mandatory vertices those seams need — or when
        /// the contourer rejects the region set outright.
        ///
        /// Tile seams still stitch: portal edges lie exactly on the tile boundary lines, where
        /// simplification cannot move them, and each converted edge takes its portal direction
        /// from the layer's own connection bits.
        public static DtTileCacheContourSet BuildTileCacheContoursWatershed(RcContext ctx, DtTileCacheLayer layer,
            RcCompactHeightfield chf, in DtTileCacheParams cacheParams)
        {
            if (HasInteriorLayerPortals(layer))
                return null;

            RcContourSet rcset;
            try
            {
                RcRegions.BuildDistanceField(ctx, chf);
                RcRegions.BuildRegions(ctx, chf, cacheParams.minRegionArea, cacheParams.mergeRegionArea);
                rcset = RcContours.BuildContours(ctx, chf, cacheParams.maxSimplificationError,
                    cacheParams.maxEdgeLen, RcBuildContoursFlags.RC_CONTOUR_TESS_WALL_EDGES);
            }
            catch (Exception e)
            {
                // "Multiple outlines" / "bad outline": over-aggressive simplification made a
                // contour self-overlap. Rare, and the classic path still produces a usable tile.
                ctx.Warn("BuildTileCacheContoursWatershed: falling back to monotone partitioning: " + e.Message);
                return null;
            }

            var lcset = new DtTileCacheContourSet();
            lcset.nconts = rcset.conts.Count;
            lcset.conts = new DtTileCacheContour[lcset.nconts];

            for (int i = 0; i < rcset.conts.Count; i++)
            {
                RcContour src = rcset.conts[i];
                var cont = new DtTileCacheContour();
                cont.nverts = src.nverts;
                // Nothing downstream keys on the region id — polygons carry their contour's
                // area, and adjacency matches vertices — so a watershed id past the byte just
                // wraps harmlessly.
                cont.reg = unchecked((byte)src.reg);
                cont.area = (byte)src.area;
                cont.verts = new int[src.nverts * 4];

                for (int v = 0; v < src.nverts; v++)
                {
                    int n = (v + 1) % src.nverts;
                    // Vertex coordinates come out core-relative — the standard contourer
                    // subtracts the border itself — and their heights already agree with the
                    // neighbouring tile's, because the border spans put the same world cells
                    // under both tiles' corner rules.
                    cont.verts[v * 4 + 0] = src.verts[v * 4 + 0];
                    cont.verts[v * 4 + 1] = src.verts[v * 4 + 1];
                    cont.verts[v * 4 + 2] = src.verts[v * 4 + 2];
                    // The classic format keeps a segment's portal direction on its first vertex;
                    // 0x0f is "not a portal". No 0x80 removal flags: the standard contourer
                    // already places only the vertices it means to keep.
                    cont.verts[v * 4 + 3] = PortalDir(layer,
                        src.verts[v * 4 + 0], src.verts[v * 4 + 2],
                        src.verts[n * 4 + 0], src.verts[n * 4 + 2]);
                }

                lcset.conts[i] = cont;
            }

            return lcset;
        }

        /// Whether any cell away from the tile boundary carries a portal bit — a seam to another
        /// vertical layer of the same tile. Boundary cells' bits point at neighbouring tiles and
        /// are expected.
        private static bool HasInteriorLayerPortals(DtTileCacheLayer layer)
        {
            int w = layer.header.width, h = layer.header.height;
            for (int z = 1; z < h - 1; ++z)
                for (int x = 1; x < w - 1; ++x)
                    if ((layer.cons[x + z * w] >> 4) != 0)
                        return true;
            return false;
        }

        /// The portal direction of one contour edge, or 0x0f for none: the edge must lie on a
        /// tile boundary line, and a cell it runs along must actually connect onward through the
        /// layer's portal bits — the map's perimeter sits on the same lines with nothing beyond.
        private static int PortalDir(DtTileCacheLayer layer, int ax, int az, int bx, int bz)
        {
            int w = layer.header.width, h = layer.header.height;

            int dir;
            if (ax == 0 && bx == 0) dir = 0;
            else if (az == h && bz == h) dir = 1;
            else if (ax == w && bx == w) dir = 2;
            else if (az == 0 && bz == 0) dir = 3;
            else return 0x0f;

            // The cells the edge runs along, one row or column inside the boundary line.
            int lo, hi;
            if (dir == 0 || dir == 2)
            {
                lo = Math.Min(az, bz);
                hi = Math.Max(az, bz);
            }
            else
            {
                lo = Math.Min(ax, bx);
                hi = Math.Max(ax, bx);
            }

            for (int k = lo; k < hi; ++k)
            {
                int x = dir == 0 ? 0 : dir == 2 ? w - 1 : k;
                int z = dir == 3 ? 0 : dir == 1 ? h - 1 : k;
                if (((layer.cons[x + z * w] >> 4) & (1 << dir)) != 0)
                    return dir;
            }

            return 0x0f;
        }

        /// @par
        ///
        /// Gives the tile's polygons height detail, so Detour reads heights off the surface rather
        /// than off each polygon's own corners. Without it a polygon is flat between its corners,
        /// which is exact on a floor or a ramp and wrong by a wide margin on anything that curves,
        /// where one polygon can cover a whole hillside.
        ///
        /// The layer is a per-cell height grid in the same coordinates as the polygon vertices, so
        /// it is presented to rcBuildPolyMeshDetail as a one-span-per-cell compact heightfield.
        /// Writes the detail mesh into @p option; leaves it untouched if the layer is level, since
        /// the corners already describe the surface there.
        ///
        /// @param[in]      ctx           The build context.
        /// @param[in]      layer         The layer the tile was contoured from.
        /// @param[in]      chf           The layer as a compact heightfield (#ToCompactHeightfield),
        ///                               shared with the partition stage rather than rebuilt.
        /// @param[in,out]  option        Tile parameters carrying the polygon mesh.
        /// @param[in]      cacheParams   The cache's parameters, for the sampling settings.
        public static void BuildTileCacheDetailMesh(RcContext ctx, DtTileCacheLayer layer, RcCompactHeightfield chf,
            DtNavMeshCreateParams option, in DtTileCacheParams cacheParams)
        {
            if (option.polyCount == 0 || cacheParams.detailSampleDist <= 0 || IsLevel(layer))
                return;

            RcPolyMesh mesh = ToPolyMesh(option);
            mesh.maxEdgeError = cacheParams.maxSimplificationError;
            // Polygon vertices are core-relative while the heightfield carries the seam
            // border; the detail builder offsets its heightfield reads by this, exactly as the
            // standard pipeline does.
            mesh.borderSize = chf.borderSize;

            // Both tiles at a seam draw the seam's one canonical polyline (#RcSeamProfileSet):
            // polygon corners on a seam line snap to it, and the detail builder fills each
            // seam edge with its knots instead of sampling.
            RcSeamProfileSet seams = RcSeamProfileSet.Build(chf, cacheParams.detailSampleMaxError);
            if (seams != null)
                SnapSeamCorners(option, chf, seams);

            // Interior samples at half the outline spacing: cache polygons are small enough that
            // at one shared spacing most clear neither the extent bar nor the grid's edge margin,
            // leaving their interiors to whatever their outlines span. Outline spacing stays as
            // configured — each extra outline vertex on a barely-not-a-sliver polygon costs a
            // steep facet.
            RcPolyMeshDetail dmesh = RcMeshDetails.BuildPolyMeshDetail(ctx, mesh, chf,
                cacheParams.detailSampleDist, cacheParams.detailSampleMaxError, seams,
                cacheParams.detailSampleDist * 0.5f);
            if (dmesh == null)
                return;

            // The detail builder lifts every vertex it emits, corner copies included, one cell
            // height; the built tile discards those copies and reads corners from the polygon
            // vertices instead. Align them to what the tile will render, or the passes below
            // judge facets a step away from the ones agents and the scene view get.
            for (int i = 0; i < dmesh.nmeshes; ++i)
            {
                int vb = dmesh.meshes[i * 4 + 0];
                int npoly = CountPolyVerts(option.polys, i * option.nvp * 2, option.nvp);
                for (int v = 0; v < npoly; ++v)
                {
                    int corner = option.polys[i * option.nvp * 2 + v];
                    dmesh.verts[(vb + v) * 3 + 1] = option.bmin.Y + option.verts[corner * 3 + 1] * option.ch;
                }
            }

            FlipDetailPleats(option, dmesh);

            option.detailMeshes = dmesh.meshes;
            option.detailVerts = dmesh.verts;
            option.detailVertsCount = dmesh.nverts;
            option.detailTris = dmesh.tris;
            option.detailTriCount = dmesh.ntris;
        }

        /// Puts every polygon corner on a seam line onto the seam's canonical polyline, so both
        /// tiles hang their surfaces from the same heights along it. Corner heights are whole
        /// cell heights, so a corner between two knots rounds onto the polyline rather than
        /// landing on it; the knots either side are shared and exact, and #RcSeamProfileSet's
        /// pins hold that rounding inside the corner's own cell. Corners the profile has
        /// nothing for — a gap, or another walkable surface's stretch — keep their contoured
        /// height.
        private static void SnapSeamCorners(DtNavMeshCreateParams option, RcCompactHeightfield chf, RcSeamProfileSet seams)
        {
            int coreW = chf.width - chf.borderSize * 2;
            int coreH = chf.height - chf.borderSize * 2;
            float climb = chf.walkableClimb * chf.ch;
            for (int v = 0; v < option.vertCount; ++v)
            {
                int x = option.verts[v * 3 + 0];
                int z = option.verts[v * 3 + 2];
                if (x != 0 && x != coreW && z != 0 && z != coreH)
                    continue;

                float yWorld = option.verts[v * 3 + 1] * chf.ch;
                if (seams.TryEval(x * chf.cs, z * chf.cs, climb, yWorld, out float sy))
                    option.verts[v * 3 + 1] = (int)MathF.Round(sy / chf.ch);
            }
        }

        /// A facet steeper than this is treated as a pleat worth re-triangulating: tan 50°. The
        /// walk limit anything sensible bakes with is 45°, so real ground stays below this and
        /// only artifacts stand above it.
        private const double PleatTanSlope = 1.19;

        /// @par
        ///
        /// Re-triangulates knife-pleats out of the detail mesh: triangles penned into the strip
        /// between three nearly collinear outline vertices, whose near-zero footprint takes the
        /// whole height change along the run and draws as a streak of wall on smooth ground.
        /// Every vertex is already at the right height — only the connectivity is wrong — so
        /// flipping the interior edge re-covers the same quad with two triangles that have real
        /// footprint and real slope.
        ///
        /// Hull edges are never re-cut, so the surface along polygon boundaries — where
        /// neighbours must tell the same story — is untouched, and no vertex is added, moved or
        /// removed; only the interior diagonals change.
        ///
        /// A polygon whose detail vertices all sit on its boundary ring is re-cut outright to the
        /// ring triangulation with the flattest worst facet (#RetriangulateRing), because single
        /// flips cannot get there: around a collinear run every path to it climbs through steeper
        /// intermediates and a greedy pass parks. Interior samples give flips room to work, and
        /// each accepted flip strictly flattens the steeper of its pair, so the passes settle.
        private static void FlipDetailPleats(DtNavMeshCreateParams option, RcPolyMeshDetail dmesh)
        {
            int maxRing = 0;
            for (int i = 0; i < dmesh.nmeshes; ++i)
                maxRing = Math.Max(maxRing, dmesh.meshes[i * 4 + 1]);
            int[] ring = new int[maxRing];
            RingDpScratch dp = null;

            for (int i = 0; i < dmesh.nmeshes; ++i)
            {
                int vb = dmesh.meshes[i * 4 + 0];
                int tb = dmesh.meshes[i * 4 + 2];
                int ntris = dmesh.meshes[i * 4 + 3];

                double worst = 0;
                for (int t = 0; t < ntris; ++t)
                    worst = Math.Max(worst, TriTanSlope(dmesh, vb, tb + t));
                if (worst <= PleatTanSlope)
                    continue;

                int npoly = CountPolyVerts(option.polys, i * option.nvp * 2, option.nvp);
                if (TryBuildHullRing(dmesh, i, npoly, ring, out int ringLen))
                {
                    dp ??= new RingDpScratch();
                    dp.EnsureCapacity(ringLen);
                    RetriangulateRing(dmesh, i, ring, ringLen, worst, dp);
                    continue;
                }

                for (int pass = 0; pass < 8; ++pass)
                {
                    bool improved = false;
                    for (int t = 0; t < ntris; ++t)
                    {
                        if (TriTanSlope(dmesh, vb, tb + t) > PleatTanSlope)
                            improved |= TryFlipPleat(dmesh, vb, tb, ntris, t);
                    }

                    if (!improved)
                        break;
                }
            }
        }

        /// Orders a polygon's detail vertices around its boundary: corners in polygon order,
        /// each followed by the edge-tessellation vertices that lie on its outgoing edge, sorted
        /// along it. False when any vertex sits off the boundary — an interior sample — because
        /// then the ring alone does not describe the polygon and re-cutting it would drop real
        /// surface data.
        private static bool TryBuildHullRing(RcPolyMeshDetail dmesh, int poly, int npoly, int[] ring, out int ringLen)
        {
            int vb = dmesh.meshes[poly * 4 + 0];
            int nverts = dmesh.meshes[poly * 4 + 1];
            int ntris = dmesh.meshes[poly * 4 + 3];
            ringLen = 0;
            if (npoly < 3 || nverts < npoly || ntris != nverts - 2)
                return false;

            // Which hull edge carries each added vertex, and how far along. Edge vertices were
            // made by lerping along the edge, so the fit is tight; anything that misses every
            // edge is an interior sample.
            const double tol = 0.01;
            Span<int> onEdge = nverts - npoly <= 128 ? stackalloc int[nverts - npoly] : new int[nverts - npoly];
            Span<double> along = nverts - npoly <= 128 ? stackalloc double[nverts - npoly] : new double[nverts - npoly];
            for (int a = npoly; a < nverts; ++a)
            {
                int va = (vb + a) * 3;
                double px = dmesh.verts[va], pz = dmesh.verts[va + 2];

                int bestEdge = -1;
                double bestDist = tol, bestT = 0;
                for (int e = 0; e < npoly; ++e)
                {
                    int e0 = (vb + e) * 3;
                    int e1 = (vb + (e + 1) % npoly) * 3;
                    double ax = dmesh.verts[e0], az = dmesh.verts[e0 + 2];
                    double bx = dmesh.verts[e1], bz = dmesh.verts[e1 + 2];
                    double dx = bx - ax, dz = bz - az;
                    double lenSq = dx * dx + dz * dz;
                    if (lenSq < 1e-12) continue;
                    double t = ((px - ax) * dx + (pz - az) * dz) / lenSq;
                    if (t <= 0 || t >= 1) continue;
                    double ox = ax + t * dx - px, oz = az + t * dz - pz;
                    double dist = Math.Sqrt(ox * ox + oz * oz);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestEdge = e;
                        bestT = t;
                    }
                }

                if (bestEdge < 0)
                    return false;
                onEdge[a - npoly] = bestEdge;
                along[a - npoly] = bestT;
            }

            for (int e = 0; e < npoly; ++e)
            {
                ring[ringLen++] = e;
                // Insertion order along the edge; counts are tiny.
                int start = ringLen;
                for (int a = 0; a < nverts - npoly; ++a)
                {
                    if (onEdge[a] != e) continue;
                    int at = ringLen++;
                    while (at > start && along[ring[at - 1] - npoly] > along[a])
                    {
                        ring[at] = ring[at - 1];
                        at--;
                    }
                    ring[at] = npoly + a;
                }
            }

            return ringLen == nverts;
        }

        /// Re-cuts a boundary ring to the triangulation with the flattest steepest facet, by
        /// interval dynamic programming over the ring — small rings, and only polygons that
        /// contain a pleat get here. Ring edges are all boundary and are all kept; only the
        /// diagonals are chosen. Leaves the mesh alone unless the optimum actually beats the
        /// triangulation it arrived with.
        private static void RetriangulateRing(RcPolyMeshDetail dmesh, int poly, int[] ring, int n, double currentWorst,
            RingDpScratch scratch)
        {
            int vb = dmesh.meshes[poly * 4 + 0];
            int tb = dmesh.meshes[poly * 4 + 2];

            double[] best = scratch.Best;
            int[] pick = scratch.Pick;
            int stride = scratch.Stride;
            for (int len = 2; len < n; ++len)
            {
                for (int i = 0; i + len < n; ++i)
                {
                    int j = i + len;
                    double b = double.MaxValue;
                    int bk = -1;
                    for (int k = i + 1; k < j; ++k)
                    {
                        double v = Math.Max(TanSlope(dmesh, vb, ring[i], ring[k], ring[j]),
                            Math.Max(best[i * stride + k], best[k * stride + j]));
                        if (v < b)
                        {
                            b = v;
                            bk = k;
                        }
                    }

                    best[i * stride + j] = b;
                    pick[i * stride + j] = bk;
                }
            }

            if (best[n - 1] >= currentWorst)
                return;

            const int hull = 0x1; // DT_DETAIL_EDGE_BOUNDARY
            int dst = tb * 4;
            List<int> stack = scratch.Stack;
            stack.Clear();
            stack.Add(0);
            stack.Add(n - 1);
            while (stack.Count > 0)
            {
                int j = stack[^1];
                int i = stack[^2];
                stack.RemoveRange(stack.Count - 2, 2);
                if (j - i < 2) continue;
                int k = pick[i * stride + j];

                dmesh.tris[dst++] = ring[i];
                dmesh.tris[dst++] = ring[k];
                dmesh.tris[dst++] = ring[j];
                dmesh.tris[dst++] = (k == i + 1 ? hull : 0)
                    | (j == k + 1 ? hull << 2 : 0)
                    | (i == 0 && j == n - 1 ? hull << 4 : 0);

                stack.Add(i);
                stack.Add(k);
                stack.Add(k);
                stack.Add(j);
            }
        }

        /// Working set for #RetriangulateRing's interval DP, sized once for the largest ring in a
        /// tile: two n×n tables in flat arrays and the walk's own stack.
        private sealed class RingDpScratch
        {
            public double[] Best = [];
            public int[] Pick = [];
            public readonly List<int> Stack = [];
            public int Stride;

            public void EnsureCapacity(int n)
            {
                if (Stride < n)
                {
                    Stride = n;
                    Best = new double[n * n];
                    Pick = new int[n * n];
                    return;
                }

                // Intervals shorter than two edges are never written but are read as the zero
                // base case, so the rows this ring will use start clear.
                for (int i = 0; i < n; ++i)
                    Array.Clear(Best, i * Stride, n);
            }
        }

        /// Tangent of one detail triangle's slope against level ground — the horizontal over the
        /// vertical of its normal. Degenerate footprints read as vertical.
        private static double TriTanSlope(RcPolyMeshDetail dmesh, int vb, int tri)
        {
            int t = tri * 4;
            return TanSlope(dmesh, vb, dmesh.tris[t], dmesh.tris[t + 1], dmesh.tris[t + 2]);
        }

        private static double TanSlope(RcPolyMeshDetail dmesh, int vb, int ia, int ib, int ic)
        {
            int a = (vb + ia) * 3;
            int b = (vb + ib) * 3;
            int c = (vb + ic) * 3;

            double ux = dmesh.verts[b] - dmesh.verts[a], uy = dmesh.verts[b + 1] - dmesh.verts[a + 1], uz = dmesh.verts[b + 2] - dmesh.verts[a + 2];
            double wx = dmesh.verts[c] - dmesh.verts[a], wy = dmesh.verts[c + 1] - dmesh.verts[a + 1], wz = dmesh.verts[c + 2] - dmesh.verts[a + 2];

            double nx = uy * wz - uz * wy;
            double ny = uz * wx - ux * wz;
            double nz = ux * wy - uy * wx;

            double horizontal = Math.Sqrt(nx * nx + nz * nz);
            return Math.Abs(ny) < 1e-12 ? double.PositiveInfinity : horizontal / Math.Abs(ny);
        }

        /// Twice the XZ area of a detail triangle, signed. Not DtUtils.TriArea2D: only the sign is
        /// read here, and these accumulate in double so the near-degenerate slivers this pass
        /// exists to judge do not decide their own winding by float rounding.
        private static double SignedArea2XZ(RcPolyMeshDetail dmesh, int vb, int a, int b, int c)
        {
            int va = (vb + a) * 3, vc2 = (vb + b) * 3, vd = (vb + c) * 3;
            return ((double)dmesh.verts[vc2] - dmesh.verts[va]) * (dmesh.verts[vd + 2] - dmesh.verts[va + 2])
                 - ((double)dmesh.verts[vd] - dmesh.verts[va]) * (dmesh.verts[vc2 + 2] - dmesh.verts[va + 2]);
        }

        /// Flips the first edge of pleat triangle @p t whose flip helps: the edge must be
        /// interior (a hull flag on either side pins it), shared with another of the polygon's
        /// triangles, the enclosing quad convex — both replacement triangles keep the winding —
        /// and the steeper of the pair must come out flatter than it went in.
        private static bool TryFlipPleat(RcPolyMeshDetail dmesh, int vb, int tb, int ntris, int t)
        {
            int ta = (tb + t) * 4;
            for (int k = 0; k < 3; ++k)
            {
                if (((dmesh.tris[ta + 3] >> (k * 2)) & 0x3) != 0)
                    continue; // hull edge: the boundary with the neighbouring polygon stays as-is

                int p = dmesh.tris[ta + k];
                int q = dmesh.tris[ta + (k + 1) % 3];
                int r = dmesh.tris[ta + (k + 2) % 3];

                // The partner triangle walks the shared edge the other way.
                for (int u = 0; u < ntris; ++u)
                {
                    if (u == t)
                        continue;

                    int tu = (tb + u) * 4;
                    int m = -1;
                    for (int e = 0; e < 3; ++e)
                    {
                        if (dmesh.tris[tu + e] == q && dmesh.tris[tu + (e + 1) % 3] == p)
                        {
                            m = e;
                            break;
                        }
                    }

                    if (m < 0)
                        continue;
                    if (((dmesh.tris[tu + 3] >> (m * 2)) & 0x3) != 0)
                        break; // the same edge hull-flagged from the other side

                    int s = dmesh.tris[tu + (m + 2) % 3];

                    // Convexity of the quad p-s-q-r, as winding: both replacements must turn the
                    // same way as the originals.
                    double sign = SignedArea2XZ(dmesh, vb, p, q, r);
                    double a1 = SignedArea2XZ(dmesh, vb, p, s, r);
                    double a2 = SignedArea2XZ(dmesh, vb, s, q, r);
                    if (sign == 0 || Math.Sign(a1) != Math.Sign(sign) || Math.Sign(a2) != Math.Sign(sign))
                        break;

                    double oldWorst = Math.Max(TriTanSlope(dmesh, vb, tb + t), TriTanSlope(dmesh, vb, tb + u));

                    int fQR = (dmesh.tris[ta + 3] >> (((k + 1) % 3) * 2)) & 0x3;
                    int fRP = (dmesh.tris[ta + 3] >> (((k + 2) % 3) * 2)) & 0x3;
                    int fPS = (dmesh.tris[tu + 3] >> (((m + 1) % 3) * 2)) & 0x3;
                    int fSQ = (dmesh.tris[tu + 3] >> (((m + 2) % 3) * 2)) & 0x3;

                    // Both triangles verbatim, to restore if the flip does not pay.
                    int wasA0 = dmesh.tris[ta], wasA1 = dmesh.tris[ta + 1], wasA2 = dmesh.tris[ta + 2], wasAf = dmesh.tris[ta + 3];
                    int wasU0 = dmesh.tris[tu], wasU1 = dmesh.tris[tu + 1], wasU2 = dmesh.tris[tu + 2], wasUf = dmesh.tris[tu + 3];

                    dmesh.tris[ta] = p;
                    dmesh.tris[ta + 1] = s;
                    dmesh.tris[ta + 2] = r;
                    dmesh.tris[ta + 3] = fPS | (0 << 2) | (fRP << 4);

                    dmesh.tris[tu] = s;
                    dmesh.tris[tu + 1] = q;
                    dmesh.tris[tu + 2] = r;
                    dmesh.tris[tu + 3] = fSQ | (fQR << 2) | (0 << 4);

                    if (Math.Max(TriTanSlope(dmesh, vb, tb + t), TriTanSlope(dmesh, vb, tb + u)) < oldWorst)
                        return true;

                    // No flatter than before: put both back exactly as stored, so the edge walk
                    // above still visits every edge it has not tried yet.
                    dmesh.tris[ta] = wasA0;
                    dmesh.tris[ta + 1] = wasA1;
                    dmesh.tris[ta + 2] = wasA2;
                    dmesh.tris[ta + 3] = wasAf;
                    dmesh.tris[tu] = wasU0;
                    dmesh.tris[tu + 1] = wasU1;
                    dmesh.tris[tu + 2] = wasU2;
                    dmesh.tris[tu + 3] = wasUf;
                    break;
                }
            }

            return false;
        }

        /// True when every cell the layer has a surface for sits at one height, so the polygons are
        /// already the surface and detail would restate a flat plane. Level tiles are the common
        /// case in a built environment, and this runs on every tile build rather than once.
        private static bool IsLevel(DtTileCacheLayer layer)
        {
            int height = -1;
            for (int i = 0; i < layer.heights.Length; i++)
            {
                if (layer.heights[i] == NoSurface) continue;
                if (height < 0) height = layer.heights[i];
                else if (layer.heights[i] != height) return false;
            }

            return true;
        }

        /// The bordered tile grid as the compact heightfield the standard builders read: one
        /// span per cell with a surface. The partition, contour and detail stages all walk it:
        /// regions and contours check areas themselves, and the detail builder floods each
        /// polygon's height patch across span connections, so unwalkable surface gets spans
        /// too, and neighbours connect on the step between them — what a heightfield built
        /// from geometry would hold, border included.
        ///
        /// Spans pack densely, exactly like a real compact heightfield: array-wide passes index by
        /// span, so padded slots would leak their sentinel values into the aggregates those passes
        /// take.
        public static RcCompactHeightfield ToCompactHeightfield(DtTileBorderGrid grid, in DtTileCacheParams cacheParams)
        {
            int b = grid.border;
            int width = grid.width + b * 2, depth = grid.height + b * 2;

            int spanCount = 0;
            for (int i = 0; i < width * depth; ++i)
                if (grid.heights[i] >= 0)
                    spanCount++;

            RcCompactHeightfield chf = new RcCompactHeightfield();
            chf.width = width;
            chf.height = depth;
            chf.borderSize = b;
            chf.spanCount = spanCount;
            chf.walkableHeight = (int)MathF.Ceiling(cacheParams.walkableHeight / cacheParams.ch);
            chf.walkableClimb = (int)MathF.Floor(cacheParams.walkableClimb / cacheParams.ch);
            chf.cs = cacheParams.cs;
            chf.ch = cacheParams.ch;
            chf.cells = new RcCompactCell[width * depth];
            chf.spans = new RcCompactSpan[spanCount];
            chf.areas = new int[spanCount];

            RcCompactSpanBuilder span = RcCompactSpanBuilder.NewBuilder();
            int cur = 0;
            for (int z = 0; z < depth; ++z)
            {
                for (int x = 0; x < width; ++x)
                {
                    int i = x + z * width;
                    if (grid.heights[i] < 0)
                    {
                        chf.cells[i] = new RcCompactCell(cur, 0);
                        continue;
                    }

                    chf.cells[i] = new RcCompactCell(cur, 1);
                    chf.areas[cur] = grid.areas[i];

                    span.y = grid.heights[i];
                    span.h = chf.walkableHeight;
                    span.con = 0;
                    // One span per cell, so the neighbour a connection points at is always span 0.
                    for (int dir = 0; dir < 4; ++dir)
                        RcRecast.SetCon(span, dir,
                            Steps(grid.heights, width, depth, x, z, dir, chf.walkableClimb) ? 0 : RcRecast.RC_NOT_CONNECTED);
                    chf.spans[cur] = new RcCompactSpan(span);
                    cur++;
                }
            }

            return chf;
        }

        /// Whether a cell's neighbour in one direction has a surface within climbing distance of
        /// it, which is what a compact heightfield means by connected.
        private static bool Steps(int[] heights, int width, int depth, int x, int z, int dir, int walkableClimb)
        {
            int nx = x + RcRecast.GetDirOffsetX(dir);
            int nz = z + RcRecast.GetDirOffsetY(dir);
            if (nx < 0 || nz < 0 || nx >= width || nz >= depth)
                return false;

            int neighbour = heights[nx + nz * width];
            return neighbour >= 0 && Math.Abs(neighbour - heights[x + z * width]) <= walkableClimb;
        }

        /// The tile's polygons as the mesh the detail builder walks. Regions stay at
        /// RC_MULTIPLE_REGS because the cache keeps no region ids: that sends the builder down its
        /// seed-from-the-polygon-center path, which starts at the cell nearest a corner, walks in
        /// to the middle and floods the height patch from there, rather than gathering heights by
        /// a region id the cache cannot supply.
        private static RcPolyMesh ToPolyMesh(DtNavMeshCreateParams option)
        {
            RcPolyMesh mesh = new RcPolyMesh();
            mesh.verts = option.verts;
            mesh.polys = option.polys;
            mesh.areas = option.polyAreas;
            mesh.flags = option.polyFlags;
            mesh.regs = new int[option.polyCount];
            mesh.nverts = option.vertCount;
            mesh.npolys = option.polyCount;
            mesh.maxpolys = option.polyCount;
            mesh.nvp = option.nvp;
            mesh.bmin = option.bmin;
            mesh.bmax = option.bmax;
            mesh.cs = option.cs;
            mesh.ch = option.ch;
            return mesh;
        }

        public static void MarkCylinderArea(DtTileCacheLayer layer, RcVec3f orig, float cs, float ch, RcVec3f pos, float radius, float height, byte areaId)
        {
            RcVec3f bmin = new RcVec3f();
            RcVec3f bmax = new RcVec3f();
            bmin.X = pos.X - radius;
            bmin.Y = pos.Y;
            bmin.Z = pos.Z - radius;
            bmax.X = pos.X + radius;
            bmax.Y = pos.Y + height;
            bmax.Z = pos.Z + radius;
            float r2 = RcMath.Sqr(radius / cs + 0.5f);

            int w = layer.header.width;
            int h = layer.header.height;
            float ics = 1.0f / cs;
            float ich = 1.0f / ch;

            float px = (pos.X - orig.X) * ics;
            float pz = (pos.Z - orig.Z) * ics;

            int minx = (int)MathF.Floor((bmin.X - orig.X) * ics);
            int miny = (int)MathF.Floor((bmin.Y - orig.Y) * ich);
            int minz = (int)MathF.Floor((bmin.Z - orig.Z) * ics);
            int maxx = (int)MathF.Floor((bmax.X - orig.X) * ics);
            int maxy = (int)MathF.Floor((bmax.Y - orig.Y) * ich);
            int maxz = (int)MathF.Floor((bmax.Z - orig.Z) * ics);

            if (maxx < 0)
                return;
            if (minx >= w)
                return;
            if (maxz < 0)
                return;
            if (minz >= h)
                return;

            if (minx < 0)
                minx = 0;
            if (maxx >= w)
                maxx = w - 1;
            if (minz < 0)
                minz = 0;
            if (maxz >= h)
                maxz = h - 1;

            for (int z = minz; z <= maxz; ++z)
            {
                for (int x = minx; x <= maxx; ++x)
                {
                    float dx = x + 0.5f - px;
                    float dz = z + 0.5f - pz;
                    if (dx * dx + dz * dz > r2)
                        continue;
                    int y = layer.heights[x + z * w];
                    if (y < miny || y > maxy)
                        continue;
                    layer.areas[x + z * w] = areaId;
                }
            }
        }

        public static void MarkBoxArea(DtTileCacheLayer layer, RcVec3f orig, float cs, float ch, RcVec3f bmin, RcVec3f bmax, byte areaId)
        {
            int w = layer.header.width;
            int h = layer.header.height;
            float ics = 1.0f / cs;
            float ich = 1.0f / ch;

            int minx = (int)MathF.Floor((bmin.X - orig.X) * ics);
            int miny = (int)MathF.Floor((bmin.Y - orig.Y) * ich);
            int minz = (int)MathF.Floor((bmin.Z - orig.Z) * ics);
            int maxx = (int)MathF.Floor((bmax.X - orig.X) * ics);
            int maxy = (int)MathF.Floor((bmax.Y - orig.Y) * ich);
            int maxz = (int)MathF.Floor((bmax.Z - orig.Z) * ics);

            if (maxx < 0)
                return;
            if (minx >= w)
                return;
            if (maxz < 0)
                return;
            if (minz >= h)
                return;

            if (minx < 0)
                minx = 0;
            if (maxx >= w)
                maxx = w - 1;
            if (minz < 0)
                minz = 0;
            if (maxz >= h)
                maxz = h - 1;

            for (int z = minz; z <= maxz; ++z)
            {
                for (int x = minx; x <= maxx; ++x)
                {
                    int y = layer.heights[x + z * w];
                    if (y < miny || y > maxy)
                        continue;
                    layer.areas[x + z * w] = areaId;
                }
            }
        }

        public static byte[] CompressTileCacheLayer(IRcCompressor comp, DtTileCacheLayer layer, RcByteOrder order, bool cCompatibility)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            DtTileCacheLayerHeaderWriter hw = new DtTileCacheLayerHeaderWriter();
            try
            {
                hw.Write(bw, layer.header, order, cCompatibility);
                int gridSize = layer.header.width * layer.header.height;
                byte[] buffer = new byte[gridSize * 3];
                for (int i = 0; i < gridSize; i++)
                {
                    buffer[i] = (byte)layer.heights[i];
                    buffer[gridSize + i] = (byte)layer.areas[i];
                    buffer[gridSize * 2 + i] = (byte)layer.cons[i];
                }

                var compressed = comp.Compress(buffer);
                bw.Write(compressed);
                return ms.ToArray();
            }
            catch (IOException e)
            {
                throw new Exception(e.Message, e);
            }
        }

        public static byte[] CompressTileCacheLayer(DtTileCacheLayerHeader header, int[] heights, int[] areas, int[] cons, RcByteOrder order, bool cCompatibility, IRcCompressor comp)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            DtTileCacheLayerHeaderWriter hw = new DtTileCacheLayerHeaderWriter();
            try
            {
                hw.Write(bw, header, order, cCompatibility);
                int gridSize = header.width * header.height;
                byte[] buffer = new byte[gridSize * 3];
                for (int i = 0; i < gridSize; i++)
                {
                    buffer[i] = (byte)heights[i];
                    buffer[gridSize + i] = (byte)areas[i];
                    buffer[gridSize * 2 + i] = (byte)cons[i];
                }

                var compressed = comp.Compress(buffer);
                bw.Write(compressed);
                return ms.ToArray();
            }
            catch (IOException e)
            {
                throw new Exception(e.Message, e);
            }
        }

        public static DtTileCacheLayer DecompressTileCacheLayer(IRcCompressor comp, byte[] compressed, RcByteOrder order, bool cCompatibility)
        {
            RcByteBuffer buf = new RcByteBuffer(compressed);
            buf.Order(order);
            DtTileCacheLayer layer = new DtTileCacheLayer();
            try
            {
                layer.header = DtTileCacheLayerHeaderReader.Read(buf, cCompatibility);
            }
            catch (IOException e)
            {
                throw new Exception(e.Message, e);
            }

            int gridSize = layer.header.width * layer.header.height;
            byte[] grids = comp.Decompress(compressed, buf.Position(), compressed.Length - buf.Position(), gridSize * 3);
            layer.heights = new byte[gridSize];
            layer.areas = new byte[gridSize];
            layer.cons = new byte[gridSize];
            layer.regs = new byte[gridSize];
            for (int i = 0; i < gridSize; i++)
            {
                layer.heights[i] = (byte)(grids[i] & 0xFF);
                layer.areas[i] = (byte)(grids[i + gridSize] & 0xFF);
                layer.cons[i] = (byte)(grids[i + gridSize * 2] & 0xFF);
            }

            return layer;
        }

        public static void MarkBoxArea(DtTileCacheLayer layer, RcVec3f orig, float cs, float ch, RcVec3f center, RcVec3f extents,
            float[] rotAux, byte areaId)
        {
            int w = layer.header.width;
            int h = layer.header.height;
            float ics = 1.0f / cs;
            float ich = 1.0f / ch;

            float cx = (center.X - orig.X) * ics;
            float cz = (center.Z - orig.Z) * ics;

            float maxr = 1.41f * Math.Max(extents.X, extents.Z);
            int minx = (int)MathF.Floor(cx - maxr * ics);
            int maxx = (int)MathF.Floor(cx + maxr * ics);
            int minz = (int)MathF.Floor(cz - maxr * ics);
            int maxz = (int)MathF.Floor(cz + maxr * ics);
            int miny = (int)MathF.Floor((center.Y - extents.Y - orig.Y) * ich);
            int maxy = (int)MathF.Floor((center.Y + extents.Y - orig.Y) * ich);

            if (maxx < 0)
                return;
            if (minx >= w)
                return;
            if (maxz < 0)
                return;
            if (minz >= h)
                return;

            if (minx < 0)
                minx = 0;
            if (maxx >= w)
                maxx = w - 1;
            if (minz < 0)
                minz = 0;
            if (maxz >= h)
                maxz = h - 1;

            float xhalf = extents.X * ics + 0.5f;
            float zhalf = extents.Z * ics + 0.5f;
            for (int z = minz; z <= maxz; ++z)
            {
                for (int x = minx; x <= maxx; ++x)
                {
                    float x2 = 2.0f * (x - cx);
                    float z2 = 2.0f * (z - cz);
                    float xrot = rotAux[1] * x2 + rotAux[0] * z2;
                    if (xrot > xhalf || xrot < -xhalf)
                        continue;
                    float zrot = rotAux[1] * z2 - rotAux[0] * x2;
                    if (zrot > zhalf || zrot < -zhalf)
                        continue;
                    int y = layer.heights[x + z * w];
                    if (y < miny || y > maxy)
                        continue;
                    layer.areas[x + z * w] = areaId;
                }
            }
        }
    }
}