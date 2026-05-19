using System.Numerics;
using System.Runtime.CompilerServices;

namespace Prowl.Unwrapper;

/// <summary>
/// Plain dense-vector kernels used by the conjugate-gradient loop.
/// Equivalent to the level-1 BLAS routines (copy, scale, axpy, dot) restricted to
/// the few we actually need. Vectorised with <see cref="Vector{T}"/> when the runtime supports it.
/// </summary>
internal static class VectorOps
{
    private static readonly int Lane = Vector<double>.Count;

    /// <summary>destination := source</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Assign(int n, double[] source, double[] destination)
    {
        System.Array.Copy(source, destination, n);
    }

    /// <summary>vector := scalar * vector</summary>
    public static void ScaleInPlace(int n, double scalar, double[] vector)
    {
        int i = 0;
        if (System.Numerics.Vector.IsHardwareAccelerated && n >= Lane)
        {
            var splat = new Vector<double>(scalar);
            int last = n - (n % Lane);
            for (; i < last; i += Lane)
            {
                var v = new Vector<double>(vector, i);
                (v * splat).CopyTo(vector, i);
            }
        }
        for (; i < n; ++i) vector[i] *= scalar;
    }

    /// <summary>y := y + a * x  (AXPY)</summary>
    public static void AddScaled(int n, double a, double[] x, double[] y)
    {
        int i = 0;
        if (System.Numerics.Vector.IsHardwareAccelerated && n >= Lane)
        {
            var splat = new Vector<double>(a);
            int last = n - (n % Lane);
            for (; i < last; i += Lane)
            {
                var vx = new Vector<double>(x, i);
                var vy = new Vector<double>(y, i);
                (vy + vx * splat).CopyTo(y, i);
            }
        }
        for (; i < n; ++i) y[i] += a * x[i];
    }

    /// <summary>Inner product of <paramref name="x"/> and <paramref name="y"/>.</summary>
    public static double InnerProduct(int n, double[] x, double[] y)
    {
        int i = 0;
        double s = 0.0;
        if (System.Numerics.Vector.IsHardwareAccelerated && n >= Lane)
        {
            var acc = Vector<double>.Zero;
            int last = n - (n % Lane);
            for (; i < last; i += Lane)
            {
                var vx = new Vector<double>(x, i);
                var vy = new Vector<double>(y, i);
                acc += vx * vy;
            }
            s = System.Numerics.Vector.Sum(acc);
        }
        for (; i < n; ++i) s += x[i] * y[i];
        return s;
    }
}
