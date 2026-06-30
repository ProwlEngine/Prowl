using System.Runtime.CompilerServices;

namespace Prowl.Unwrapper;

/// <summary>
/// Floating-point comparisons used across the solver. Each comparison takes an explicit
/// epsilon so behaviour stays predictable when values are nearly equal.
/// </summary>
internal static class NumericHelpers
{
    public const double Tiny = 1e-10;
    public const double FloatTiny = 1e-6;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ApproxEqual(double x, double y, double eps) => System.Math.Abs(x - y) <= eps;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ApproxGreater(double x, double y, double eps) => System.Math.Abs(x - y) > eps && x > y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ApproxGreaterOrEqual(double x, double y, double eps) => System.Math.Abs(x - y) <= eps || x > y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ApproxLess(double x, double y, double eps) => !ApproxGreaterOrEqual(x, y, eps);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ApproxLessOrEqual(double x, double y, double eps) => !ApproxGreater(x, y, eps);
}
