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

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Prowl.Recast;
using Prowl.Recast.Core;
using Prowl.Recast.Core.Numerics;
using Prowl.Recast.Detour.TileCache.Io.Compress;
using Prowl.Recast.Geom;

namespace Prowl.Recast.Detour.TileCache;

public class DtTileCacheLayerBuilder
{
    private readonly IDtTileCacheCompressorFactory _compFactory;

    public DtTileCacheLayerBuilder(IDtTileCacheCompressorFactory compFactory)
    {
        _compFactory = compFactory;
    }

    public List<DtTileCacheLayerBuildResult> Build(IRcInputGeomProvider geom, RcConfig cfg, DtTileCacheStorageParams storageParams, int threads, int tw, int th)
    {
        if (threads == 1)
        {
            return BuildSingleThread(geom, cfg, storageParams, tw, th);
        }

        return BuildMultiThread(geom, cfg, storageParams, tw, th, threads);
    }

    private List<DtTileCacheLayerBuildResult> BuildSingleThread(IRcInputGeomProvider geom, RcConfig cfg, DtTileCacheStorageParams storageParams, int tw, int th)
    {
        var results = new List<DtTileCacheLayerBuildResult>();
        for (int y = 0; y < th; ++y)
        {
            for (int x = 0; x < tw; ++x)
            {
                DtTileCacheLayerBuildResult result = BuildTileCacheLayer(geom, cfg, x, y, storageParams);
                results.Add(result);
            }
        }

        return results;
    }


    private List<DtTileCacheLayerBuildResult> BuildMultiThread(IRcInputGeomProvider geom, RcConfig cfg, DtTileCacheStorageParams storageParams, int tw, int th, int threads)
    {
        var results = new List<Task<DtTileCacheLayerBuildResult>>();
        for (int y = 0; y < th; ++y)
        {
            for (int x = 0; x < tw; ++x)
            {
                int tx = x;
                int ty = y;
                Task<DtTileCacheLayerBuildResult> task = Task.Run(() => BuildTileCacheLayer(geom, cfg, tx, ty, storageParams));
                results.Add(task);
            }
        }

        return results
            .Select(x => x.Result)
            .ToList();
    }

    protected virtual RcHeightfieldLayerSet BuildHeightfieldLayerSet(IRcInputGeomProvider geom, RcConfig cfg, int tx, int ty)
    {
        RcBuilder rcBuilder = new RcBuilder();
        RcVec3f bmin = geom.GetMeshBoundsMin();
        RcVec3f bmax = geom.GetMeshBoundsMax();
        RcBuilderConfig builderCfg = new RcBuilderConfig(cfg, bmin, bmax, tx, ty);
        RcHeightfieldLayerSet lset = rcBuilder.BuildLayers(geom, builderCfg);
        return lset;
    }

    protected virtual DtTileCacheLayerBuildResult BuildTileCacheLayer(IRcInputGeomProvider geom, RcConfig cfg, int tx, int ty, DtTileCacheStorageParams storageParams)
    {
        RcHeightfieldLayerSet lset = BuildHeightfieldLayerSet(geom, cfg, tx, ty);
        List<byte[]> result = [];
        if (lset != null)
        {
            for (int i = 0; i < lset.layers.Length; ++i)
            {
                RcHeightfieldLayer layer = lset.layers[i];

                // Store header
                DtTileCacheLayerHeader header = new DtTileCacheLayerHeader
                {
                    magic = DtTileCacheLayerHeader.DT_TILECACHE_MAGIC,
                    version = DtTileCacheLayerHeader.DT_TILECACHE_VERSION,

                    // Tile layer location in the navmesh.
                    tx = tx,
                    ty = ty,
                    tlayer = i,
                    bmin = layer.bmin,
                    bmax = layer.bmax,

                    // Tile info.
                    width = layer.width,
                    height = layer.height,
                    minx = layer.minx,
                    maxx = layer.maxx,
                    miny = layer.miny,
                    maxy = layer.maxy,
                    hmin = layer.hmin,
                    hmax = layer.hmax
                };

                IRcCompressor comp = _compFactory.Create(storageParams.Compatibility ? 0 : 1);
                byte[] bytes = DtTileCacheBuilder.CompressTileCacheLayer(header, layer.heights, layer.areas, layer.cons, storageParams.Order, storageParams.Compatibility, comp);
                result.Add(bytes);
            }
        }

        return new DtTileCacheLayerBuildResult(tx, ty, result);
    }
}