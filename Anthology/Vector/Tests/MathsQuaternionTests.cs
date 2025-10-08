namespace Prowl.Vector.Methods;

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
