using System.Collections.Generic;

namespace Prowl.Recast.Detour
{
    public class DtBVItemYComparer : IComparer<DtBVItem>
    {
        public static readonly DtBVItemYComparer Shared = new DtBVItemYComparer();

        private DtBVItemYComparer()
        {
        }

        public int Compare(DtBVItem a, DtBVItem b)
        {
            return a.bmin.Y.CompareTo(b.bmin.Y);
        }
    }
}