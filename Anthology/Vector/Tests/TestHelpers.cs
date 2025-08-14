using Prowl.Vector;

using Xunit;

namespace Prowl.Vector.Methods;

internal class TestHelpers
{
    public const float Tolerance = 0.0001f; // A small tolerance for float comparisons

    public static void AssertFloatEqual(float expected, float actual, float? tolerance = null)
    {
        Assert.Equal(expected, actual, tolerance ?? Tolerance);
    }

    public static void AssertFloat2Equal(Float2 expected, Float2 actual, float? tolerance = null)
    {
        float tol = tolerance ?? Tolerance;
        Assert.Equal(expected.X, actual.X, tol);
        Assert.Equal(expected.Y, actual.Y, tol);
    }

    public static void AssertFloat3Equal(Float3 expected, Float3 actual, float? tolerance = null)
    {
        float tol = tolerance ?? Tolerance;
        Assert.Equal(expected.X, actual.X, tol);
        Assert.Equal(expected.Y, actual.Y, tol);
        Assert.Equal(expected.Z, actual.Z, tol);
    }

    public static void AssertFloat4Equal(Float4 expected, Float4 actual, float? tolerance = null)
    {
        float tol = tolerance ?? Tolerance;
        Assert.Equal(expected.X, actual.X, tol);
        Assert.Equal(expected.Y, actual.Y, tol);
        Assert.Equal(expected.Z, actual.Z, tol);
        Assert.Equal(expected.W, actual.W, tol);
    }

    public static void AssertQuaternionEqual(Quaternion expected, Quaternion actual, float? tolerance = null)
    {
        // Uses the Quaternion's own ApproximatelyEquals or direct component check
        // Adjust if specific rotational equivalence (q vs -q) is needed for a test
        float tol = tolerance ?? Tolerance;
        Assert.Equal(expected.X, actual.X, tol);
        Assert.Equal(expected.Y, actual.Y, tol);
        Assert.Equal(expected.Z, actual.Z, tol);
        Assert.Equal(expected.W, actual.W, tol);
    }
    public static void AssertQuaternionRotationallyEqual(Quaternion expected, Quaternion actual, float? tolerance = null)
    {
        float tol = tolerance ?? Tolerance;
        // Check if q1 is close to q2 or -q2
        bool directMatch = Maths.Abs(expected.X - actual.X) <= tol &&
                           Maths.Abs(expected.Y - actual.Y) <= tol &&
                           Maths.Abs(expected.Z - actual.Z) <= tol &&
                           Maths.Abs(expected.W - actual.W) <= tol;

        bool negatedMatch = Maths.Abs(expected.X + actual.X) <= tol &&
                            Maths.Abs(expected.Y + actual.Y) <= tol &&
                            Maths.Abs(expected.Z + actual.Z) <= tol &&
                            Maths.Abs(expected.W + actual.W) <= tol;

        Assert.True(directMatch || negatedMatch, $"Quaternion {actual} is not rotationally equivalent to {expected}");
    }


    public static void AssertMatrixEqual(Float2x2 expected, Float2x2 actual, float? tolerance = null)
    {
        float tol = tolerance ?? Tolerance;
        AssertFloat2Equal(expected.c0, actual.c0, tol);
        AssertFloat2Equal(expected.c1, actual.c1, tol);
    }

    public static void AssertMatrixEqual(Float3x3 expected, Float3x3 actual, float? tolerance = null)
    {
        float tol = tolerance ?? Tolerance;
        AssertFloat3Equal(expected.c0, actual.c0, tol);
        AssertFloat3Equal(expected.c1, actual.c1, tol);
        AssertFloat3Equal(expected.c2, actual.c2, tol);
    }

    public static void AssertMatrixEqual(Float4x4 expected, Float4x4 actual, float? tolerance = null)
    {
        float tol = tolerance ?? Tolerance;
        AssertFloat4Equal(expected.c0, actual.c0, tol);
        AssertFloat4Equal(expected.c1, actual.c1, tol);
        AssertFloat4Equal(expected.c2, actual.c2, tol);
        AssertFloat4Equal(expected.c3, actual.c3, tol);
    }

    public const float PI = (float)Maths.PI;
    public const float PIOver2 = (float)Maths.PI / 2.0f;
}
