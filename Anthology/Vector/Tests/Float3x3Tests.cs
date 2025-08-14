namespace Prowl.Vector.Methods;

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
