using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Prowl.Recast.Core
{
    public static class RcDebug
    {
        [Conditional("DEBUG")]
        public static void Assert(bool condition)
        {
            Debug.Assert(condition);
        }
        
        [Conditional("DEBUG")]
        public static void Assert<T>(T obj) where T : class
        {
            Debug.Assert(null != obj);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UnusedRef<T>(ref T _)
        {
            // ..
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Unused<T>(T _)
        {
            // ..
        }
    }
}