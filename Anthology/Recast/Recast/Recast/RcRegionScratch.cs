using System;
using System.Collections.Generic;

namespace Prowl.Recast
{
    /// The working buffers one <see cref="RcRegions.BuildRegions"/> call needs, kept so a caller
    /// that partitions heightfield after heightfield — a tile cache carving a tile per frame, a bake
    /// walking a tile grid — stops re-allocating them. Measured at roughly a quarter of a tile
    /// build's total allocation.
    ///
    /// Grow-only, and a buffer grown for a larger heightfield is handed back for a smaller one, so
    /// nothing may read past the span count it was asked for.
    /// One per thread: a partitioner run holds this for its duration, so two at once need two.
    public sealed class RcRegionScratch
    {
        private int[] m_srcReg = Array.Empty<int>();
        private int[] m_srcDist = Array.Empty<int>();
        private List<List<RcLevelStackEntry>> m_lvlStacks;
        private List<RcLevelStackEntry> m_stack;

        /// Zeroed to <paramref name="spanCount"/>: the partitioner reads 0 as "no region assigned
        /// yet", so ids left in a longer buffer's tail would seed regions that do not exist.
        internal int[] SrcReg(int spanCount) => Cleared(ref m_srcReg, spanCount);

        /// Zeroed for symmetry with <see cref="SrcReg"/> rather than out of need — every path that
        /// reads a distance wrote it in the same statement that made its region id positive.
        internal int[] SrcDist(int spanCount) => Cleared(ref m_srcDist, spanCount);

        /// The per-level stacks, emptied. The partitioner does refill each one before reading it,
        /// but only because its first pass through the level loop is the one that sorts — an
        /// ordering three methods deep. Emptying them here costs nothing (the entry is a struct, so
        /// Clear just resets a count) and makes a stale entry impossible rather than unreachable.
        internal List<List<RcLevelStackEntry>> LevelStacks()
        {
            if (m_lvlStacks == null)
            {
                m_lvlStacks = new List<List<RcLevelStackEntry>>(RcRegions.NB_STACKS);
                for (int i = 0; i < RcRegions.NB_STACKS; ++i)
                {
                    m_lvlStacks.Add(new List<RcLevelStackEntry>(256));
                }
            }

            for (int i = 0; i < m_lvlStacks.Count; ++i)
            {
                m_lvlStacks[i].Clear();
            }

            return m_lvlStacks;
        }

        /// The flood stack. Every user of it clears before filling, so this does not.
        internal List<RcLevelStackEntry> Stack()
        {
            if (m_stack == null)
            {
                m_stack = new List<RcLevelStackEntry>(256);
            }

            return m_stack;
        }

        private static int[] Cleared(ref int[] buffer, int size)
        {
            if (buffer.Length < size)
            {
                buffer = new int[size];
                return buffer;
            }

            Array.Clear(buffer, 0, size);
            return buffer;
        }
    }
}
