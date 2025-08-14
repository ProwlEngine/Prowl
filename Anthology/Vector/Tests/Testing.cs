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

public class Float2x2Tests
{
    [Fact]
    public void Rotate_ZeroAngle_ReturnsIdentity()
    {
        var m = Float2x2.Rotate(0);
        var identity = new Float2x2(1, 0, 0, 1);
        TestHelpers.AssertMatrixEqual(identity, m);
    }

    [Fact]
    public void Rotate_90Degrees_ReturnsCorrectMatrix()
    {
        var m = Float2x2.Rotate((float)Maths.PI_2);
        // cos(pi/2)=0, sin(pi/2)=1
        // Expected: [0 -1]
        //           [1  0]
        var expected = new Float2x2(0, -1, 1, 0);
        TestHelpers.AssertMatrixEqual(expected, m);
    }

    [Fact]
    public void Scale_Uniform_ReturnsCorrectMatrix()
    {
        var m = Float2x2.Scale(2.5f);
        var expected = new Float2x2(2.5f, 0, 0, 2.5f);
        TestHelpers.AssertMatrixEqual(expected, m);
    }

    [Fact]
    public void Scale_NonUniformXY_ReturnsCorrectMatrix()
    {
        var m = Float2x2.Scale(2.0f, 3.0f);
        var expected = new Float2x2(2.0f, 0, 0, 3.0f);
        TestHelpers.AssertMatrixEqual(expected, m);
    }

    [Fact]
    public void Scale_NonUniformVector_ReturnsCorrectMatrix()
    {
        var v = new Float2(1.5f, 0.5f);
        var m = Float2x2.Scale(v);
        var expected = new Float2x2(1.5f, 0, 0, 0.5f);
        TestHelpers.AssertMatrixEqual(expected, m);
    }
}

public class Float3x3Tests
{
    [Fact]
    public void Constructor_FromFloat4x4_ExtractsUpperLeft()
    {
        var f4x4 = new Float4x4(
            1, 2, 3, 4,
            5, 6, 7, 8,
            9, 10, 11, 12,
            13, 14, 15, 16
        );
        var f3x3 = new Float3x3(f4x4);
        var expected = new Float3x3(
            1, 2, 3,
            5, 6, 7,
            9, 10, 11
        );
        TestHelpers.AssertMatrixEqual(expected, f3x3);
    }

    [Fact]
    public void Constructor_FromQuaternion_Identity()
    {
        var q = Quaternion.Identity;
        var m = new Float3x3(q);
        TestHelpers.AssertMatrixEqual(Float3x3.Identity, m);
    }

    [Fact]
    public void Constructor_FromQuaternion_RotateX90()
    {
        var q = Maths.RotateX(TestHelpers.PIOver2); // Quaternion rotation
        var m = new Float3x3(q);
        var expectedM = Float3x3.RotateX(TestHelpers.PIOver2); // Matrix rotation
        TestHelpers.AssertMatrixEqual(expectedM, m, 0.00001f);
    }


    [Fact]
    public void FromAxisAngle_RotateX90()
    {
        var m = Float3x3.FromAxisAngle(Float3.UnitX, TestHelpers.PIOver2);
        var expected = Float3x3.RotateX(TestHelpers.PIOver2);
        TestHelpers.AssertMatrixEqual(expected, m);
    }

    [Fact]
    public void RotateX_90Degrees()
    {
        var m = Float3x3.RotateX(TestHelpers.PIOver2);
        Maths.Sincos(TestHelpers.PIOver2, out float s, out float c); // c=0, s=1
        var expected = new Float3x3(1, 0, 0, 0, c, -s, 0, s, c);
        TestHelpers.AssertMatrixEqual(expected, m);
    }

    [Fact]
    public void RotateY_90Degrees()
    {
        var m = Float3x3.RotateY(TestHelpers.PIOver2);
        Maths.Sincos(TestHelpers.PIOver2, out float s, out float c); // c=0, s=1
        var expected = new Float3x3(c, 0, s, 0, 1, 0, -s, 0, c);
        TestHelpers.AssertMatrixEqual(expected, m);
    }

    [Fact]
    public void RotateZ_90Degrees()
    {
        var m = Float3x3.RotateZ(TestHelpers.PIOver2);
        Maths.Sincos(TestHelpers.PIOver2, out float s, out float c); // c=0, s=1
        var expected = new Float3x3(c, -s, 0, s, c, 0, 0, 0, 1);
        TestHelpers.AssertMatrixEqual(expected, m);
    }

    [Fact]
    public void Scale_Uniform()
    {
        var m = Float3x3.Scale(5.0f);
        var expected = new Float3x3(5, 0, 0, 0, 5, 0, 0, 0, 5);
        TestHelpers.AssertMatrixEqual(expected, m);
    }

    [Fact]
    public void Scale_NonUniformXYZ()
    {
        var m = Float3x3.Scale(1, 2, 3);
        var expected = new Float3x3(1, 0, 0, 0, 2, 0, 0, 0, 3);
        TestHelpers.AssertMatrixEqual(expected, m);
    }

    [Fact]
    public void Scale_NonUniformVector()
    {
        var m = Float3x3.Scale(new Float3(1, 2, 3));
        var expected = new Float3x3(1, 0, 0, 0, 2, 0, 0, 0, 3);
        TestHelpers.AssertMatrixEqual(expected, m);
    }

    [Fact]
    public void CreateLookRotation_Standard()
    {
        var forward = Float3.UnitZ;
        var up = Float3.UnitY;
        var m = Float3x3.CreateLookRotation(forward, up);
        // Expected: X=(1,0,0), Y=(0,1,0), Z=(0,0,1) (since view space looking down +Z has camera -Z)
        // The function calculates axes for a coordinate system where Z is forward.
        // zaxis = forward (0,0,1)
        // xaxis = cross(up, zaxis) = cross((0,1,0), (0,0,1)) = (1,0,0)
        // yaxis = cross(zaxis, xaxis) = cross((0,0,1), (1,0,0)) = (0,1,0)
        // Matrix columns are xaxis, yaxis, zaxis
        var expected = new Float3x3(
            1, 0, 0, // xaxis.X, yaxis.X, zaxis.X
            0, 1, 0, // xaxis.Y, yaxis.Y, zaxis.Y
            0, 0, 1  // xaxis.Z, yaxis.Z, zaxis.Z
        );
        TestHelpers.AssertMatrixEqual(expected, m);
    }

    [Fact]
    public void CreateLookRotationSafe_CollinearReturnsIdentity()
    {
        var forward = Float3.UnitY;
        var up = Float3.UnitY;
        var m = Float3x3.CreateLookRotationSafe(forward, up);
        TestHelpers.AssertMatrixEqual(Float3x3.Identity, m);
    }

    [Fact]
    public void CreateLookRotationSafe_ZeroForwardReturnsIdentity()
    {
        var m = Float3x3.CreateLookRotationSafe(Float3.Zero, Float3.UnitY);
        TestHelpers.AssertMatrixEqual(Float3x3.Identity, m);
    }

    [Fact]
    public void Transform_Float3_WithIdentity()
    {
        var m = Float3x3.Identity;
        var v = new Float3(1, 2, 3);
        var result = Maths.Mul(m, v);
        TestHelpers.AssertFloat3Equal(v, result);
    }

    [Fact]
    public void Transform_Float3_WithRotationX90()
    {
        var m = Float3x3.RotateX(TestHelpers.PIOver2);
        var v = new Float3(0, 1, 0); // Y-axis
                                     // Y-axis rotated 90 deg around X becomes Z-axis (0,0,1)
                                     // R_x(90) = [1  0  0]
                                     //           [0  0 -1]
                                     //           [0  1  0]
                                     // (0,1,0) -> (0*0 + 0*1 + 1*0, 0*0 + 0*1 + 1*0, 1*0 + 0*1 + 0*0) -> No.
                                     // c0=(1,0,0), c1=(0,c,s), c2=(0,-s,c) with c=0,s=1
                                     // c0=(1,0,0), c1=(0,0,1), c2=(0,-1,0)
                                     // m * v = c0*v.X + c1*v.Y + c2*v.Z
                                     //       = (1,0,0)*0 + (0,0,1)*1 + (0,-1,0)*0 = (0,0,1)
        var expected = new Float3(0, 0, 1);
        var result = Maths.Mul(m, v);
        TestHelpers.AssertFloat3Equal(expected, result, TestHelpers.Tolerance);
    }

    [Fact]
    public void Transform_Float3_WithScaling()
    {
        var m = Float3x3.Scale(2, 3, 4);
        var v = new Float3(1, 1, 1);
        var expected = new Float3(2, 3, 4);
        var result = Maths.Mul(m, v);
        TestHelpers.AssertFloat3Equal(expected, result);
    }
}

public class Float4x4Tests
{
    [Fact]
    public void Constructor_FromFloat3x3AndTranslation()
    {
        var r = Float3x3.RotateX(TestHelpers.PIOver2);
        var t = new Float3(1, 2, 3);
        var m = new Float4x4(r, t);

        var expected = Float4x4.RotateX(TestHelpers.PIOver2);
        expected.c3 = new Float4(t, 1.0f);

        TestHelpers.AssertMatrixEqual(expected, m);
    }

    [Fact]
    public void Constructor_FromQuaternionAndTranslation()
    {
        var q = Maths.RotateY(TestHelpers.PIOver2);
        var t = new Float3(4, 5, 6);
        var m = new Float4x4(q, t);

        var r3x3 = new Float3x3(q);
        var expected = new Float4x4(r3x3, t);

        TestHelpers.AssertMatrixEqual(expected, m);
    }

    [Fact]
    public void FromAxisAngle_RotateY90()
    {
        var m = Float4x4.FromAxisAngle(Float3.UnitY, TestHelpers.PIOver2);
        var r3x3 = Float3x3.RotateY(TestHelpers.PIOver2);
        var expected = new Float4x4(r3x3, Float3.Zero);
        TestHelpers.AssertMatrixEqual(expected, m);
    }

    [Theory]
    [InlineData(1.0f, 0.0f, 0.0f)] // RotateX
    [InlineData(0.0f, 1.0f, 0.0f)] // RotateY
    [InlineData(0.0f, 0.0f, 1.0f)] // RotateZ
    public void Rotate_Axis90Degrees(float x, float y, float z)
    {
        var angle = TestHelpers.PIOver2;
        Float4x4 m;
        Float3x3 r3x3;

        if (x == 1.0f) { m = Float4x4.RotateX(angle); r3x3 = Float3x3.RotateX(angle); }
        else if (y == 1.0f) { m = Float4x4.RotateY(angle); r3x3 = Float3x3.RotateY(angle); }
        else { m = Float4x4.RotateZ(angle); r3x3 = Float3x3.RotateZ(angle); }

        var expected = new Float4x4(r3x3, Float3.Zero);
        TestHelpers.AssertMatrixEqual(expected, m);
    }

    [Fact]
    public void CreateScale_Uniform()
    {
        var m = Float4x4.CreateScale(2.0f);
        var expected = new Float4x4(
            2, 0, 0, 0,
            0, 2, 0, 0,
            0, 0, 2, 0,
            0, 0, 0, 1);
        TestHelpers.AssertMatrixEqual(expected, m);
    }

    [Fact]
    public void CreateScale_NonUniformXYZ()
    {
        var m = Float4x4.CreateScale(2, 3, 4);
        var expected = new Float4x4(
            2, 0, 0, 0,
            0, 3, 0, 0,
            0, 0, 4, 0,
            0, 0, 0, 1);
        TestHelpers.AssertMatrixEqual(expected, m);
    }

    [Fact]
    public void CreateScale_NonUniformVector()
    {
        var m = Float4x4.CreateScale(new Float3(2, 3, 4));
        var expected = new Float4x4(
            2, 0, 0, 0,
            0, 3, 0, 0,
            0, 0, 4, 0,
            0, 0, 0, 1);
        TestHelpers.AssertMatrixEqual(expected, m);
    }

    [Fact]
    public void CreateTranslation_Valid()
    {
        var tVec = new Float3(10, 20, 30);
        var m = Float4x4.CreateTranslation(tVec);
        var expected = new Float4x4(
            1, 0, 0, 10,
            0, 1, 0, 20,
            0, 0, 1, 30,
            0, 0, 0, 1);
        TestHelpers.AssertMatrixEqual(expected, m);
    }

    [Fact]
    public void CreateTRS_Identity()
    {
        var t = Float3.Zero;
        var r = Quaternion.Identity;
        var s = new Float3(1, 1, 1);
        var m = Float4x4.CreateTRS(t, r, s);
        TestHelpers.AssertMatrixEqual(Float4x4.Identity, m);
    }

    [Fact]
    public void CreateTRS_Combined()
    {
        var t = new Float3(10, 20, 30);
        var r = Maths.RotateY(TestHelpers.PIOver2); // 90 deg rot around Y
        var s = new Float3(2, 1, 0.5f);

        var m = Float4x4.CreateTRS(t, r, s);

        // Manual construction for verification: Scale, then Rotate, then Translate
        // S = diag(2, 1, 0.5, 1)
        // R = RotY(pi/2) = [0 0 1 0; 0 1 0 0; -1 0 0 0; 0 0 0 1]
        // T = Translate(10,20,30)
        // M = T * R * S
        // R*S = [0*2  0*1  1*0.5  0;
        //        0*2  1*1  0*0.5  0;
        //       -1*2  0*1  0*0.5  0;
        //        0    0    0     1]
        //     = [0   0  0.5  0;
        //        0   1   0   0;
        //       -2   0   0   0;
        //        0   0   0   1]
        // T * (R*S)
        //   c0: (0,0,-2,0) * scale.X for rotation.c0, etc.
        //   This is from the TRS code:
        //   Float3x3 rMat = new Float3x3(rotation); => cosY=0, sinY=1
        //   rMat = [0 0 -1; 0 1 0; 1 0 0] if it's [c -s; s c] style for Y leading to Z' = X, X' = -Z
        //   Actually, code is: RotateY(angle) -> (c,0,s, 0,1,0, -s,0,c) for Float3x3
        //   rMat = [0 0 1; 0 1 0; -1 0 0] (col major)
        //   c0_final = (rMat.c0.X * s.X, rMat.c0.Y * s.X, rMat.c0.Z * s.X, 0)
        //            = (0 * 2, 0 * 2, -1 * 2, 0) = (0, 0, -2, 0)
        //   c1_final = (rMat.c1.X * s.Y, rMat.c1.Y * s.Y, rMat.c1.Z * s.Y, 0)
        //            = (0 * 1, 1 * 1, 0 * 1, 0) = (0, 1, 0, 0)
        //   c2_final = (rMat.c2.X * s.Z, rMat.c2.Y * s.Z, rMat.c2.Z * s.Z, 0)
        //            = (1 * 0.5, 0 * 0.5, 0 * 0.5, 0) = (0.5, 0, 0, 0)
        //   c3_final = (t.X, t.Y, t.Z, 1) = (10,20,30,1)

        var expected = new Float4x4(
            new Float4(0, 0, -2, 0),
            new Float4(0, 1, 0, 0),
            new Float4(0.5f, 0, 0, 0),
            new Float4(10, 20, 30, 1)
        );
        TestHelpers.AssertMatrixEqual(expected, m);
    }

    [Fact]
    public void CreateLookAt_Standard()
    {
        var eye = new Float3(0, 0, -5); // Camera 5 units back on Z
        var target = Float3.Zero;
        var up = Float3.UnitY;
        var m = Float4x4.CreateLookAt(eye, target, up);

        // zaxis = normalize(target - eye) = normalize(0,0,5) = (0,0,1)
        // xaxis = normalize(cross(up, zaxis)) = normalize(cross(0,1,0 ; 0,0,1)) = normalize(1,0,0) = (1,0,0)
        // yaxis = cross(zaxis, xaxis) = cross(0,0,1 ; 1,0,0) = (0,1,0)
        // Expected Matrix (View Matrix - transforms world to view):
        // [ xaxis.X  yaxis.X  zaxis.X  0 ]   [  1  0  0  0 ]
        // [ xaxis.Y  yaxis.Y  zaxis.Y  0 ] = [  0  1  0  0 ]
        // [ xaxis.Z  yaxis.Z  zaxis.Z  0 ]   [  0  0  1  0 ]
        // [ -dot(xaxis,eye) -dot(yaxis,eye) -dot(zaxis,eye)  1 ]
        // -dot(xaxis,eye) = -dot((1,0,0), (0,0,-5)) = 0
        // -dot(yaxis,eye) = -dot((0,1,0), (0,0,-5)) = 0
        // -dot(zaxis,eye) = -dot((0,0,1), (0,0,-5)) = 5
        // The matrix is column major.
        // c0=(xaxis.X, xaxis.Y, xaxis.Z, -dot(xaxis,eye)) -> No, this is OpenGL style.
        // Code is:
        // c0 = (xaxis.X, yaxis.X, zaxis.X, 0) = (1,0,0,0)
        // c1 = (xaxis.Y, yaxis.Y, zaxis.Y, 0) = (0,1,0,0)
        // c2 = (xaxis.Z, yaxis.Z, zaxis.Z, 0) = (0,0,1,0)
        // c3 = (-dot(xaxis,eye), -dot(yaxis,eye), -dot(zaxis,eye), 1) = (0,0,5,1)

        var expected = new Float4x4(
            new Float4(1, 0, 0, 0),
            new Float4(0, 1, 0, 0),
            new Float4(0, 0, 1, 0),
            new Float4(0, 0, 5, 1)
        );
        TestHelpers.AssertMatrixEqual(expected, m);
    }

    [Fact]
    public void CreateOrtho_Simple()
    {
        float width = 10, height = 20, near = 0.1f, far = 100f;
        var m = Float4x4.CreateOrtho(width, height, near, far);

        // Calls CreateOrthoOffCenter(-5, 5, -10, 10, 0.1, 100)
        // r_l = 10, t_b = 20, f_n = 99.9
        // c0.X = 2/10 = 0.2
        // c1.Y = 2/20 = 0.1
        // c2.Z = 1/99.9
        // c3.X = -(5-5)/10 = 0
        // c3.Y = -(10-10)/20 = 0
        // c3.Z = -0.1/99.9
        var expected = Float4x4.Identity;
        expected.c0.X = 2.0f / width;
        expected.c1.Y = 2.0f / height;
        expected.c2.Z = 1.0f / (far - near);
        expected.c3.X = 0;
        expected.c3.Y = 0;
        expected.c3.Z = -near / (far - near);
        TestHelpers.AssertMatrixEqual(expected, m);
    }

    [Fact]
    public void CreateOrthoOffCenter_Valid()
    {
        float l = -2, r = 3, b = -1, t = 4, n = 1, f = 10;
        var m = Float4x4.CreateOrthoOffCenter(l, r, b, t, n, f);

        float r_l = r - l; // 5
        float t_b = t - b; // 5
        float f_n = f - n; // 9

        var expected = Float4x4.Identity;
        expected.c0.X = 2.0f / r_l;         // 2/5 = 0.4
        expected.c1.Y = 2.0f / t_b;         // 2/5 = 0.4
        expected.c2.Z = 1.0f / f_n;         // 1/9
        expected.c3.X = -(r + l) / r_l;     // -(1)/5 = -0.2
        expected.c3.Y = -(t + b) / t_b;     // -(3)/5 = -0.6
        expected.c3.Z = -n / f_n;           // -1/9
        TestHelpers.AssertMatrixEqual(expected, m);
    }

    [Fact]
    public void CreateOrthoOffCenter_InvalidRangeReturnsIdentity()
    {
        var m = Float4x4.CreateOrthoOffCenter(1, 1, 0, 1, 0, 1); // r_l = 0
        TestHelpers.AssertMatrixEqual(Float4x4.Identity, m);
    }

    [Fact]
    public void CreatePerspectiveFov_Valid()
    {
        float fovY = TestHelpers.PIOver2; // 90 degrees
        float aspect = 16.0f / 9.0f;
        float near = 0.1f;
        float far = 1000.0f;

        var m = Float4x4.CreatePerspectiveFov(fovY, aspect, near, far);

        float yScale = 1.0f / MathF.Tan(fovY * 0.5f); // tan(pi/4) = 1, so yScale = 1
        float xScale = yScale / aspect; // 1 / (16/9) = 9/16

        var expected = Float4x4.Zero;
        expected.c0.X = xScale;
        expected.c1.Y = yScale;
        expected.c2.Z = far / (far - near);
        expected.c2.W = 1.0f;
        expected.c3.Z = -near * far / (far - near);

        TestHelpers.AssertMatrixEqual(expected, m);
    }

    [Theory]
    [InlineData(0.0f, 1.0f, 0.1f, 100.0f)] // Zero FOV
    [InlineData(MathF.PI, 1.0f, 0.1f, 100.0f)] // PI FOV
    [InlineData(TestHelpers.PIOver2, 0.0f, 0.1f, 100.0f)] // Zero aspect
    [InlineData(TestHelpers.PIOver2, 1.0f, 0.0f, 100.0f)] // Zero near
    [InlineData(TestHelpers.PIOver2, 1.0f, 0.1f, 0.0f)] // Zero far
    [InlineData(TestHelpers.PIOver2, 1.0f, 100.0f, 0.1f)] // Near >= Far
    public void CreatePerspectiveFov_InvalidParameters_ReturnsIdentity(float fov, float aspect, float near, float far)
    {
        var m = Float4x4.CreatePerspectiveFov(fov, aspect, near, far);
        TestHelpers.AssertMatrixEqual(Float4x4.Identity, m);
    }

    [Fact]
    public void Transform_Point_Float3_WithIdentity() // Assuming Float3 is treated as point (w=1)
    {
        var m = Float4x4.Identity;
        var v = new Float3(1, 2, 3);
        var v4 = new Float4(v, 1.0f); // Convert to Float4 point
        var result4 = Maths.Mul(m, v4);
        TestHelpers.AssertFloat4Equal(new Float4(1, 2, 3, 1), result4);
    }

    [Fact]
    public void Transform_Point_Float3_WithTranslation()
    {
        var tVec = new Float3(10, 20, 30);
        var m = Float4x4.CreateTranslation(tVec);
        var v = new Float3(1, 2, 3);
        var v4 = new Float4(v, 1.0f);
        var result4 = Maths.Mul(m, v4);
        // (1+10, 2+20, 3+30, 1)
        var expected = new Float4(11, 22, 33, 1);
        TestHelpers.AssertFloat4Equal(expected, result4);
    }

    [Fact]
    public void Transform_Direction_Float3_WithTranslation_NoEffect() // Assuming Float3 is treated as direction (w=0)
    {
        var tVec = new Float3(10, 20, 30);
        var m = Float4x4.CreateTranslation(tVec);
        var v = new Float3(1, 2, 3);
        var v4 = new Float4(v, 0.0f); // Convert to Float4 direction
        var result4 = Maths.Mul(m, v4);
        // Translation should not affect directions
        var expected = new Float4(1, 2, 3, 0);
        TestHelpers.AssertFloat4Equal(expected, result4);
    }

    [Fact]
    public void Transform_Point_Float3_WithRotationY90()
    {
        var m = Float4x4.RotateY(TestHelpers.PIOver2);
        var v = new Float3(1, 0, 0); // X-axis
        var v4 = new Float4(v, 1.0f);
        // X-axis rotated 90 deg around Y becomes -Z axis (0,0,-1) for point
        var expected = new Float4(0, 0, -1, 1);
        var result4 = Maths.Mul(m, v4);
        TestHelpers.AssertFloat4Equal(expected, result4, TestHelpers.Tolerance);
    }

    [Fact]
    public void Transform_Direction_Float3_WithRotationY90()
    {
        var m = Float4x4.RotateY(TestHelpers.PIOver2);
        var v = new Float3(1, 0, 0); // X-axis direction
        var v4 = new Float4(v, 0.0f);
        var expected = new Float4(0, 0, -1, 0);
        var result4 = Maths.Mul(m, v4);
        TestHelpers.AssertFloat4Equal(expected, result4, TestHelpers.Tolerance);
    }

    [Fact]
    public void Transform_Point_Float3_WithScaling()
    {
        var m = Float4x4.CreateScale(2, 3, 4);
        var v = new Float3(1, 1, 1);
        var v4 = new Float4(v, 1.0f);
        var expected = new Float4(2, 3, 4, 1); // w remains 1 after scaling
        var result4 = Maths.Mul(m, v4);
        TestHelpers.AssertFloat4Equal(expected, result4);
    }

    [Fact]
    public void Transform_Direction_Float3_WithScaling()
    {
        var m = Float4x4.CreateScale(2, 3, 4);
        var v = new Float3(1, 1, 1);
        var v4 = new Float4(v, 0.0f);
        var expected = new Float4(2, 3, 4, 0); // w remains 0
        var result4 = Maths.Mul(m, v4);
        TestHelpers.AssertFloat4Equal(expected, result4);
    }

    [Fact]
    public void Transform_Float4_WithTRS()
    {
        var t = new Float3(10, 0, 0);
        var r = Maths.RotateY(TestHelpers.PIOver2); // Rotates X to -Z
        var s = new Float3(2, 1, 1);
        var m = Float4x4.CreateTRS(t, r, s);

        var point = new Float4(1, 0, 0, 1); // Point on X-axis

        // 1. Scale: (1,0,0,1) * S(2,1,1) -> (2,0,0,1)
        // 2. Rotate by Y(PI/2): (2,0,0,1) maps to (0,0,-2,1)
        //    (TRS code uses r.c0 * scale.X etc which is column major multiply)
        //    Let's trace `Maths.Mul(m, point)`
        //    m.c0 = (0,0,-2,0), m.c1 = (0,1,0,0), m.c2 = (1,0,0,0), m.c3 = (10,0,0,1) from previous TRS test
        //    result = m.c0*point.X + m.c1*point.Y + m.c2*point.Z + m.c3*point.W
        //           = (0,0,-2,0)*1 + (0,1,0,0)*0 + (1,0,0,0)*0 + (10,0,0,1)*1
        //           = (0,0,-2,0) + (10,0,0,1) = (10, 0, -2, 1)
        var expected = new Float4(10, 0, -2, 1);
        var result = Maths.Mul(m, point);
        TestHelpers.AssertFloat4Equal(expected, result, TestHelpers.Tolerance);


        var direction = new Float4(1, 0, 0, 0); // Direction along X-axis
                                                // 1. Scale: (1,0,0,0) * S(2,1,1) -> (2,0,0,0)
                                                // 2. Rotate by Y(PI/2): (2,0,0,0) maps to (0,0,-2,0)
                                                // 3. Translate: No effect on direction (w=0)
                                                //    result = m.c0*direction.X + m.c1*direction.Y + m.c2*direction.Z + m.c3*direction.W
                                                //           = (0,0,-2,0)*1 + (0,1,0,0)*0 + (1,0,0,0)*0 + (10,0,0,1)*0
                                                //           = (0,0,-2,0)
        var expectedDir = new Float4(0, 0, -2, 0);
        var resultDir = Maths.Mul(m, direction);
        TestHelpers.AssertFloat4Equal(expectedDir, resultDir, TestHelpers.Tolerance);
    }

    [Fact]
    public void Transform_Float4_WithPerspectiveProjection()
    {
        // A simple perspective projection
        var m = Float4x4.CreatePerspectiveFov(TestHelpers.PIOver2, 1.0f, 1.0f, 100.0f);
        // yScale = 1/tan(PI/4) = 1, xScale = 1
        // m.c0.X = 1
        // m.c1.Y = 1
        // m.c2.Z = 100/(99)
        // m.c2.W = 1
        // m.c3.Z = -1*100/(99)

        var pointInView = new Float4(1, 1, -10, 1); // A point in front of camera (assuming standard view: -Z is forward)
                                                    // The projection matrices are for left-handed system (Z forward).
                                                    // CreatePerspectiveFov maps Z to [0,1]
                                                    // Puts Z_eye into W_clip

        // If pointInView is (x_eye, y_eye, z_eye, 1)
        // Transformed point (x_clip, y_clip, z_clip, w_clip)
        // x_clip = pointInView.X * m.c0.X = 1 * 1 = 1
        // y_clip = pointInView.Y * m.c1.Y = 1 * 1 = 1
        // z_clip = pointInView.Z * m.c2.Z + pointInView.W * m.c3.Z = -10 * (100/99) + 1 * (-100/99)
        //        = -1000/99 - 100/99 = -1100/99
        // w_clip = pointInView.Z * m.c2.W = -10 * 1 = -10
        // Note: for perspective, w_clip becomes z_eye (or -z_eye depending on convention)
        // The provided matrix setup:
        // result.c0.X = xScale;
        // result.c1.Y = yScale;
        // result.c2.Z = farPlane / (farPlane - nearPlane);
        // result.c2.W = 1.0f; // Puts Z_eye into W_clip
        // result.c3.Z = -nearPlane * farPlane / (farPlane - nearPlane);

        // So w_clip = pointInView.Z * m.c2.W (if c2.W is the multiplier for Z to W)
        // If c2.W = 1.0f, then w_clip should take pointInView.Z for the W component.
        // Let's recalculate using Maths.Mul
        // m = [1 0  0         0]
        //     [0 1  0         0]
        //     [0 0  100/99   -100/99]
        //     [0 0  1         0]
        // (Column major, so m.c2.W is the [3,2] element, which is 1.0)
        // m.c0 = (1,0,0,0)
        // m.c1 = (0,1,0,0)
        // m.c2 = (0,0,100/99,1)
        // m.c3 = (0,0,-100/99,0)
        //
        // result = m.c0*px + m.c1*py + m.c2*pz + m.c3*pw
        //        = (1,0,0,0)*1 + (0,1,0,0)*1 + (0,0,100/99,1)*(-10) + (0,0,-100/99,0)*1
        //        = (1,0,0,0) + (0,1,0,0) + (0,0,-1000/99,-10) + (0,0,-100/99,0)
        //        = (1, 1, -1100/99, -10)

        var result = Maths.Mul(m, pointInView);
        var expected = new Float4(1.0f * pointInView.X, 1.0f * pointInView.Y,
                                 (100.0f / 99.0f) * pointInView.Z + (-100.0f / 99.0f) * pointInView.W,
                                 1.0f * pointInView.Z); // w_clip = z_eye

        TestHelpers.AssertFloat4Equal(expected, result, TestHelpers.Tolerance);
    }
}

public class MathsMatrixOperationsTests // For Inverse, Transpose from Maths.cs
{
    [Fact]
    public void Inverse_Float2x2_Valid()
    {
        var m = new Float2x2(4, 7, 2, 6); // [4 7; 2 6] -> c0=(4,2), c1=(7,6)
                                          // constructor (m00, m01, m10, m11)
                                          // m00=4, m01=7, m10=2, m11=6
        var invM = Maths.Inverse(m);
        // det = 4*6 - 7*2 = 24 - 14 = 10
        // invDet = 0.1
        // Expected: 0.1 * [6 -7; -2 4] = [0.6 -0.7; -0.2 0.4]
        var expected = new Float2x2(0.6f, -0.7f, -0.2f, 0.4f);
        TestHelpers.AssertMatrixEqual(expected, invM);

        // Check M * M_inv = I
        var identity = Maths.Mul(m, invM);
        TestHelpers.AssertMatrixEqual(Float2x2.Identity, identity, 0.0001f);
    }

    [Fact]
    public void Inverse_Float3x3_Valid()
    {
        var m = new Float3x3(1, 2, 3, 0, 1, 4, 5, 6, 0);
        var invM = Maths.Inverse(m);
        // det = 1*(0-24) - 2*(0-20) + 3*(0-5) = -24 + 40 - 15 = 1
        // Manual cofactor calculation...
        // C00 = -24, C01 = 20, C02 = -5
        // C10 = 18,  C11 = -15, C12 = 4
        // C20 = 5,   C21 = -4,  C22 = 1
        // Adjoint = Transpose of Cofactors
        // Inv = Adjoint / det (det=1)
        var expected = new Float3x3(
            -24, 18, 5,
             20, -15, -4,
             -5, 4, 1);
        TestHelpers.AssertMatrixEqual(expected, invM);
        TestHelpers.AssertMatrixEqual(Float3x3.Identity, Maths.Mul(m, invM), 0.0001f);
    }

    [Fact]
    public void Inverse_Float4x4_Translation()
    {
        var m = Float4x4.CreateTranslation(new Float3(1, 2, 3));
        var invM = Maths.Inverse(m);
        var expected = Float4x4.CreateTranslation(new Float3(-1, -2, -3));
        TestHelpers.AssertMatrixEqual(expected, invM);
        TestHelpers.AssertMatrixEqual(Float4x4.Identity, Maths.Mul(m, invM), 0.0001f);
    }

    [Fact]
    public void Transpose_Float2x2()
    {
        var m = new Float2x2(1, 2, 3, 4); // c0=(1,3), c1=(2,4)
        var tm = Maths.Transpose(m);
        // Expected: c0=(1,2), c1=(3,4)
        var expected = new Float2x2(1, 3, 2, 4);
        TestHelpers.AssertMatrixEqual(expected, tm);
    }

    [Fact]
    public void Transpose_Float3x3()
    {
        var m = new Float3x3(1, 2, 3, 4, 5, 6, 7, 8, 9);
        var tm = Maths.Transpose(m);
        var expected = new Float3x3(1, 4, 7, 2, 5, 8, 3, 6, 9);
        TestHelpers.AssertMatrixEqual(expected, tm);
    }

    [Fact]
    public void Transpose_Float4x4()
    {
        var m = new Float4x4(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16);
        var tm = Maths.Transpose(m);
        var expected = new Float4x4(1, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15, 4, 8, 12, 16);
        TestHelpers.AssertMatrixEqual(expected, tm);
    }
}

public class QuaternionStructTests
{
    [Fact]
    public void Quaternion_Identity_IsCorrect()
    {
        TestHelpers.AssertQuaternionEqual(new Quaternion(0, 0, 0, 1), Quaternion.Identity);
    }

    [Fact]
    public void Quaternion_Constructor_XYZW()
    {
        var q = new Quaternion(1, 2, 3, 4);
        Assert.Equal(1, q.X);
        Assert.Equal(2, q.Y);
        Assert.Equal(3, q.Z);
        Assert.Equal(4, q.W);
    }

    [Fact]
    public void Quaternion_Constructor_Float4()
    {
        var f4 = new Float4(1, 2, 3, 4);
        var q = new Quaternion(f4);
        TestHelpers.AssertQuaternionEqual(new Quaternion(1, 2, 3, 4), q);
    }

    [Fact]
    public void Quaternion_Constructor_VectorScalar()
    {
        var v3 = new Float3(1, 2, 3);
        var q = new Quaternion(v3, 4);
        TestHelpers.AssertQuaternionEqual(new Quaternion(1, 2, 3, 4), q);
    }

    [Fact]
    public void Quaternion_ApproximatelyEquals_TrueForCloseQuaternions()
    {
        var q1 = new Quaternion(1.00000f, 2.00000f, 3.00000f, 4.00000f);
        var q2 = new Quaternion(1.00001f, 2.00001f, 3.00001f, 4.00001f);
        Assert.True(q1.ApproximatelyEquals(q2, 0.0001f));
    }

    [Fact]
    public void Quaternion_ApproximatelyEquals_FalseForDistantQuaternions()
    {
        var q1 = new Quaternion(1.0f, 2.0f, 3.0f, 4.0f);
        var q2 = new Quaternion(1.1f, 2.1f, 3.1f, 4.1f);
        Assert.False(q1.ApproximatelyEquals(q2, 0.001f));
    }
}


public class MathsQuaternionTests
{
    private const float Tol = TestHelpers.Tolerance;

    [Fact]
    public void FromMatrix_Float3x3_Identity()
    {
        var m = Float3x3.Identity;
        var q = Maths.FromMatrix(m);
        TestHelpers.AssertQuaternionEqual(Quaternion.Identity, q, Tol);
    }

    [Fact]
    public void FromMatrix_Float3x3_RotX90()
    {
        var m = Float3x3.RotateX(TestHelpers.PIOver2);
        var q = Maths.FromMatrix(m);
        var expectedQ = Maths.RotateX(TestHelpers.PIOver2);
        TestHelpers.AssertQuaternionRotationallyEqual(expectedQ, q, Tol);
    }

    [Fact]
    public void FromMatrix_Float4x4_Identity()
    {
        var m = Float4x4.Identity;
        var q = Maths.FromMatrix(m);
        TestHelpers.AssertQuaternionEqual(Quaternion.Identity, q, Tol);
    }

    [Fact]
    public void FromMatrix_Float4x4_RotY90WithTranslation()
    {
        var m = Float4x4.RotateY(TestHelpers.PIOver2);
        m = Maths.Mul(Float4x4.CreateTranslation(new Float3(1, 2, 3)), m); // Add translation

        var q = Maths.FromMatrix(m); // Should only use rotation part
        var expectedQ = Maths.RotateY(TestHelpers.PIOver2);
        TestHelpers.AssertQuaternionRotationallyEqual(expectedQ, q, Tol);
    }

    [Fact]
    public void Conjugate_SimpleQuaternion()
    {
        var q = new Quaternion(1, 2, 3, 4);
        var conjugate = Maths.Conjugate(q);
        TestHelpers.AssertQuaternionEqual(new Quaternion(-1, -2, -3, 4), conjugate);
    }

    [Fact]
    public void Inverse_UnitQuaternion()
    {
        var q = Maths.Normalize(new Quaternion(1, 2, 3, 4));
        var inverse = Maths.Inverse(q);
        var conjugate = Maths.Conjugate(q);
        TestHelpers.AssertQuaternionEqual(conjugate, inverse, Tol); // For unit quaternion, inverse is conjugate
    }

    [Fact]
    public void Inverse_NonUnitQuaternion()
    {
        var q = new Quaternion(1, 0, 0, 1); // Length sqrt(2)
        // q_inv = conjugate(q) / lengthSq(q) = (-1,0,0,1) / 2 = (-0.5, 0, 0, 0.5)
        var inverse = Maths.Inverse(q);
        var expected = new Quaternion(-0.5f, 0, 0, 0.5f);
        TestHelpers.AssertQuaternionEqual(expected, inverse, Tol);

        // Check q * q_inv = Identity
        var product = Maths.Mul(q, inverse);
        TestHelpers.AssertQuaternionEqual(Quaternion.Identity, product, Tol);
    }

    [Fact]
    public void Dot_OrthogonalQuaternions()
    {
        var q1 = new Quaternion(1, 0, 0, 0);
        var q2 = new Quaternion(0, 1, 0, 0);
        Assert.Equal(0, Maths.Dot(q1, q2), Tol);
    }

    [Fact]
    public void Dot_SameQuaternion()
    {
        var q = new Quaternion(1, 2, 3, 4);
        Assert.Equal(1 * 1 + 2 * 2 + 3 * 3 + 4 * 4, Maths.Dot(q, q), Tol); // 1+4+9+16 = 30
    }

    [Fact]
    public void Length_LengthSquared()
    {
        var q = new Quaternion(1, 2, 2, 0); // Length = sqrt(1+4+4) = sqrt(9) = 3
        Assert.Equal(9, Maths.LengthSquared(q), Tol);
        Assert.Equal(3, Maths.Length(q), Tol);
    }

    [Fact]
    public void Normalize_ValidQuaternion()
    {
        var q = new Quaternion(1, 2, 3, 4);
        var nq = Maths.Normalize(q);
        Assert.Equal(1.0f, Maths.Length(nq), Tol);
    }

    [Fact]
    public void NormalizeSafe_ZeroQuaternion_ReturnsIdentity()
    {
        var q = new Quaternion(0, 0, 0, 0);
        var nq = Maths.NormalizeSafe(q);
        TestHelpers.AssertQuaternionEqual(Quaternion.Identity, nq);
    }

    [Fact]
    public void NormalizeSafe_ZeroQuaternion_ReturnsDefault()
    {
        var q = new Quaternion(0, 0, 0, 0);
        var defaultQ = new Quaternion(1, 0, 0, 0); // Arbitrary default
        var nq = Maths.NormalizeSafe(q, defaultQ);
        TestHelpers.AssertQuaternionEqual(defaultQ, nq);
    }

    [Fact]
    public void Mul_QuaternionQuaternion_ConcatenatesRotations()
    {
        // Rotate 90 deg around X, then 90 deg around Y (world space)
        var rotX90 = Maths.RotateX(TestHelpers.PIOver2);
        var rotY90 = Maths.RotateY(TestHelpers.PIOver2);

        // Order: b then a. So, rotX90 then rotY90 means Mul(rotY90, rotX90)
        var combined = Maths.Mul(rotY90, rotX90);

        // Apply to a vector: (1,0,0) --X90--> (0,0,1) --Y90--> (1,0,0)
        // Actually, (1,0,0) --X90--> (0,0,1) {x=1,y=0,z=0} -> {x'=x, y'= -z, z'=y} -> (1,0,0) -> (1, -(0), 0) = (1,0,0) No.
        // X-axis (1,0,0). Rotate by X90: still (1,0,0). No, this is vector rotation.
        // X-axis (1,0,0) rotated by X90 -> (1,0,0)
        // Y-axis (0,1,0) rotated by X90 -> (0,0,1) (Y maps to Z)
        // Z-axis (0,0,1) rotated by X90 -> (0,-1,0) (Z maps to -Y)

        // (0,1,0) --rotX90--> (0,0,1) --rotY90--> (1,0,0)
        var v = Float3.UnitY;
        var v_after_X = Maths.Mul(rotX90, v); // (0,0,1)
        var v_after_XY = Maths.Mul(rotY90, v_after_X); // (1,0,0)

        var v_combined = Maths.Mul(combined, v);
        TestHelpers.AssertFloat3Equal(v_after_XY, v_combined, Tol);
    }

    [Fact]
    public void Mul_QuaternionFloat3_RotateVector()
    {
        var q = Maths.RotateY(TestHelpers.PIOver2); // Rotate 90 deg around Y
        var v = new Float3(1, 0, 0); // X-axis
        // Expected: X-axis rotated 90 deg around Y becomes -Z axis (0,0,-1) if right-handed
        // (c,0,s,0,1,0,-s,0,c) * (1,0,0) -> (c,0,-s)
        // qY(pi/2) -> x=0, y=sin(pi/4), z=0, w=cos(pi/4)
        // This is the Prowl.Vector specific formula:
        // qVec = (0, sin(pi/4), 0)
        // t = 2 * cross(qVec, v) = 2 * cross((0,s,0), (1,0,0)) = 2 * (0,0,-s) = (0,0,-2s)
        // v + q.W * t + cross(qVec, t)
        // = (1,0,0) + c*(0,0,-2s) + cross((0,s,0), (0,0,-2s))
        // = (1,0,0) + (0,0,-2cs) + (-2s^2,0,0)
        // = (1-2s^2, 0, -2cs)
        // Since s=c=1/sqrt(2) for pi/4:
        // = (1-2*(1/2), 0, -2*(1/2)) = (0,0,-1)
        var rotatedV = Maths.Mul(q, v);
        TestHelpers.AssertFloat3Equal(new Float3(0, 0, -1), rotatedV, Tol);
    }

    [Fact]
    public void AxisAngle_CreateRotation()
    {
        var axis = Float3.UnitZ;
        float angle = TestHelpers.PIOver2;
        var q = Maths.AxisAngle(axis, angle);

        // Expected: sin(angle/2)*axis_z, cos(angle/2) for W
        // angle/2 = pi/4. sin(pi/4) = cos(pi/4) = 1/sqrt(2)
        float s = MathF.Sin(angle * 0.5f);
        float c = MathF.Cos(angle * 0.5f);
        TestHelpers.AssertQuaternionEqual(new Quaternion(0, 0, s, c), q, Tol);
    }

    [Theory]
    [InlineData(1.0f, 0.0f, 0.0f)] // RotateX
    [InlineData(0.0f, 1.0f, 0.0f)] // RotateY
    [InlineData(0.0f, 0.0f, 1.0f)] // RotateZ
    public void Rotate_Axis(float x, float y, float z)
    {
        float angle = TestHelpers.PIOver2;
        Quaternion q;
        float s = MathF.Sin(angle * 0.5f);
        float c = MathF.Cos(angle * 0.5f);

        if (x == 1.0f) { q = Maths.RotateX(angle); TestHelpers.AssertQuaternionEqual(new Quaternion(s, 0, 0, c), q, Tol); }
        else if (y == 1.0f) { q = Maths.RotateY(angle); TestHelpers.AssertQuaternionEqual(new Quaternion(0, s, 0, c), q, Tol); }
        else { q = Maths.RotateZ(angle); TestHelpers.AssertQuaternionEqual(new Quaternion(0, 0, s, c), q, Tol); }
    }

    [Fact]
    public void LookRotation_Standard()
    {
        var forward = Float3.UnitZ;
        var up = Float3.UnitY;
        var q = Maths.LookRotation(forward, up);

        // If forward is Z and up is Y, should be identity rotation.
        TestHelpers.AssertQuaternionEqual(Quaternion.Identity, q, Tol);

        // Look down -Z
        forward = -Float3.UnitZ;
        q = Maths.LookRotation(forward, up);
        var expectedQ = Maths.RotateY(TestHelpers.PI); // Rotate 180 deg around Y
        TestHelpers.AssertQuaternionRotationallyEqual(expectedQ, q, Tol);
    }

    [Fact]
    public void LookRotationSafe_CollinearForwardUp()
    {
        var forward = Float3.UnitY;
        var up = Float3.UnitY; // Collinear
        // Behavior for collinear depends on implementation, might return Identity or a specific rotation.
        // The current code has a specific fallback for Z > 0.99999f etc.
        // if (LengthSquared(right) < Epsilon * Epsilon) // Collinear case
        //    return forward.Z > 0.99999f ? Quaternion.Identity : (forward.Z < -0.99999f ? RotateY((float)Maths.PI) : AxisAngle(Float3.UnitX, (float)Maths.PI * 0.5f * (forward.Y > 0 ? 1 : -1)));

        // Case 1: forward = (0,0,1), up = (0,0,1) -> right is zero. forward.Z > 0.99.. -> Identity
        var q1 = Maths.LookRotationSafe(Float3.UnitZ, Float3.UnitZ);
        TestHelpers.AssertQuaternionEqual(Quaternion.Identity, q1, Tol);

        // Case 2: forward = (0,0,-1), up = (0,0,-1) -> right is zero. forward.Z < -0.99.. -> RotateY(PI)
        var q2 = Maths.LookRotationSafe(-Float3.UnitZ, -Float3.UnitZ);
        TestHelpers.AssertQuaternionRotationallyEqual(Maths.RotateY(MathF.PI), q2, Tol);

        // Case 3: forward = (0,1,0), up = (0,1,0) -> right is zero. forward.Y > 0. AxisAngle(UnitX, PI/2)
        var q3 = Maths.LookRotationSafe(Float3.UnitY, Float3.UnitY);
        TestHelpers.AssertQuaternionRotationallyEqual(Maths.AxisAngle(Float3.UnitX, MathF.PI * 0.5f), q3, Tol);
    }

    [Fact]
    public void LookRotationSafe_ZeroForwardReturnsIdentity()
    {
        var q = Maths.LookRotationSafe(Float3.Zero, Float3.UnitY);
        TestHelpers.AssertQuaternionEqual(Quaternion.Identity, q);
    }

    [Fact]
    public void Nlerp_InterpolatesAndNormalizes()
    {
        var q1 = Quaternion.Identity;
        var q2 = Maths.RotateX(TestHelpers.PIOver2);

        var q_t0 = Maths.Nlerp(q1, q2, 0);
        TestHelpers.AssertQuaternionEqual(q1, q_t0, Tol);
        Assert.Equal(1.0f, Maths.Length(q_t0), Tol);

        var q_t1 = Maths.Nlerp(q1, q2, 1);
        TestHelpers.AssertQuaternionEqual(q2, q_t1, Tol);
        Assert.Equal(1.0f, Maths.Length(q_t1), Tol);

        var q_t05 = Maths.Nlerp(q1, q2, 0.5f);
        Assert.Equal(1.0f, Maths.Length(q_t05), Tol);
        // Check if it's on the path (dot product check or specific values)
        // For t=0.5, with q1=id, q2=rotX(pi/2) = (sin(pi/4),0,0,cos(pi/4))
        // Linear part: (0.5*sin(pi/4), 0, 0, 0.5*(1+cos(pi/4))) then normalized.
    }

    [Fact]
    public void Slerp_InterpolatesCorrectly()
    {
        var q1 = Quaternion.Identity;
        var q2 = Maths.RotateX(TestHelpers.PIOver2); // (s,0,0,c) where s=c=~0.707 for PI/4

        var q_t0 = Maths.Slerp(q1, q2, 0);
        TestHelpers.AssertQuaternionEqual(q1, q_t0, Tol);

        var q_t1 = Maths.Slerp(q1, q2, 1);
        TestHelpers.AssertQuaternionEqual(q2, q_t1, Tol);

        var q_t05 = Maths.Slerp(q1, q2, 0.5f); // Should be RotX(PI/4)
        var expected_t05 = Maths.RotateX(TestHelpers.PIOver2 * 0.5f);
        TestHelpers.AssertQuaternionRotationallyEqual(expected_t05, q_t05, Tol);
    }

    [Fact]
    public void Slerp_ShortestPath()
    {
        var q1 = Quaternion.Identity;
        var q2_long = new Quaternion(-MathF.Sin(TestHelpers.PIOver2 * 0.5f), 0, 0, -MathF.Cos(TestHelpers.PIOver2 * 0.5f)); // -RotX(PI/2) effectively
        var q2_short = Maths.RotateX(TestHelpers.PIOver2);

        // Slerp should take the shortest path, so slerping to q2_long should be like slerping to q2_short
        var q_t05_long = Maths.Slerp(q1, q2_long, 0.5f);
        var expected_t05 = Maths.RotateX(TestHelpers.PIOver2 * 0.5f);
        TestHelpers.AssertQuaternionRotationallyEqual(expected_t05, q_t05_long, Tol);
    }

    [Fact]
    public void Slerp_VeryCloseQuaternions_UsesNlerp()
    {
        var q1 = Maths.RotateX(0.00001f);
        var q2 = Maths.RotateX(0.00002f);

        // This will trigger the dot > 0.9995f path in Slerp
        var q_slerp = Maths.Slerp(q1, q2, 0.5f);
        var q_nlerp = Maths.Nlerp(q1, Maths.Dot(q1, q2) < 0 ? new Quaternion(-q2.X, -q2.Y, -q2.Z, -q2.W) : q2, 0.5f); // Mimic Slerp's q2Adjusted

        TestHelpers.AssertQuaternionRotationallyEqual(q_nlerp, q_slerp, 0.0001f); // Tolerance might need adjustment
    }


    [Fact]
    public void Angle_BetweenQuaternions()
    {
        var q1 = Quaternion.Identity;
        var q2 = Maths.RotateZ(TestHelpers.PIOver2);
        Assert.Equal(TestHelpers.PIOver2, Maths.Angle(q1, q2), Tol);

        var q3 = Maths.RotateZ(TestHelpers.PI);
        Assert.Equal(TestHelpers.PI, Maths.Angle(q1, q3), Tol);

        // Angle between q and -q should be 0 for rotation purposes, but Angle() might give PI if not normalized for comparison.
        // Maths.Angle uses Abs(Dot(q1,q2)), so it should be PI.
        // Min(Abs(dot), 1.0f) * 2.0f; dot between Identity and RotateZ(PI) is -1. Abs(-1)=1. Acos(1)=0. 0*2 = 0. This is not right.
        // Oh, Angle is between q1 and q2. q2 = (0,0,sin(pi/2),cos(pi/2)) = (0,0,1,0)
        // Dot(q1,q2) = Dot((0,0,0,1), (0,0,1,0)) = 0. Acos(0) = PI/2. (PI/2)*2 = PI. This IS correct.

        // Angle between q and -q (same rotation)
        // Let q = Identity (0,0,0,1). -q = (0,0,0,-1)
        // Dot(q, -q) = -1. Abs(Dot) = 1. Acos(1) = 0. Angle = 0. Correct.
        var q4 = Maths.RotateZ(TestHelpers.PI); // (0,0,sin(pi/2),cos(pi/2)) = (0,0,1,0)
        var q5 = new Quaternion(-q4.X, -q4.Y, -q4.Z, -q4.W); // (-0,-0,-1,-0)
        Assert.Equal(0, Maths.Angle(q4, q5), Tol);
    }

    [Fact]
    public void Forward_Up_Right_Vectors()
    {
        // Identity rotation
        var q_id = Quaternion.Identity;
        TestHelpers.AssertFloat3Equal(new Float3(0, 0, 1), Maths.Forward(q_id), Tol);
        TestHelpers.AssertFloat3Equal(new Float3(0, 1, 0), Maths.Up(q_id), Tol);
        TestHelpers.AssertFloat3Equal(new Float3(1, 0, 0), Maths.Right(q_id), Tol);

        // Rotate 90 deg around Y: X->-Z, Y->Y, Z->X
        var q_rotY90 = Maths.RotateY(TestHelpers.PIOver2);
        TestHelpers.AssertFloat3Equal(new Float3(1, 0, 0), Maths.Forward(q_rotY90), Tol); // Original Z (0,0,1) maps to (1,0,0)
        TestHelpers.AssertFloat3Equal(new Float3(0, 1, 0), Maths.Up(q_rotY90), Tol);    // Original Y (0,1,0) maps to (0,1,0)
        TestHelpers.AssertFloat3Equal(new Float3(0, 0, -1), Maths.Right(q_rotY90), Tol);  // Original X (1,0,0) maps to (0,0,-1)
    }
}
