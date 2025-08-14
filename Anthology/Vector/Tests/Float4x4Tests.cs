namespace Prowl.Vector.Methods;

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
