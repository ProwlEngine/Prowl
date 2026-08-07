using System.Collections.Generic;

namespace Prowl.Recast.Detour
{
    public class DtBVItemXComparer : IComparer<DtBVItem>
    {
        public static readonly DtBVItemXComparer Shared = new DtBVItemXComparer();

        private DtBVItemXComparer()
        {
        }

        public int Compare(DtBVItem a, DtBVItem b)
        {
            return a.bmin.X.CompareTo(b.bmin.X);
        }
    }
}