using System.Collections.Generic;

namespace Prowl.Recast.Detour.TileCache
{
    public class DtTempContour
    {
        public List<int> verts;
        public int nverts;
        public List<int> poly;

        public DtTempContour()
        {
            verts = new List<int>();
            nverts = 0;
            poly = new List<int>();
        }
    }
}