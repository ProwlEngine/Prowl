using System.Collections.Generic;

namespace Prowl.Recast.Detour
{
    public class DtBVItemZComparer : IComparer<DtBVItem>
    {
        public static readonly DtBVItemZComparer Shared = new DtBVItemZComparer();

        private DtBVItemZComparer()
        {
        }

        public int Compare(DtBVItem a, DtBVItem b)
        {
            return a.bmin.Z.CompareTo(b.bmin.Z);
        }
    }
}