using System.Collections.Generic;

namespace Prowl.Recast.Detour.TileCache;

public class DtTempContour
{
    public List<int> verts;
    public int nverts;
    public List<int> poly;

    public DtTempContour()
    {
        verts = [];
        nverts = 0;
        poly = [];
    }
}