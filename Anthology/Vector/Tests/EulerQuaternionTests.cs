namespace Prowl.Vector.Methods;

public class EulerQuaternionTests
{
    private const float Tol = TestHelpers.Tolerance;

    [Fact]
    public void FromEuler_RotX90_XYZr()
    {
        var angles = new Float3(TestHelpers.PIOver2, 0, 0);
        var q = Maths.FromEuler(angles, EulerOrder.XYZr);
        var expected = Maths.RotateX(TestHelpers.PIOver2);
        TestHelpers.AssertQuaternionRotationallyEqual(expected, q, Tol);
    }

    [Fact]
    public void EulerRadians_RoundTrip_ZYXr()
    {
        var angles = new Float3(0.1f, -0.2f, 0.3f);
        var q = Maths.FromEuler(angles, EulerOrder.ZYXr);
        var recovered = Maths.ToEuler(q, EulerOrder.ZYXr);
        TestHelpers.AssertFloat3Equal(angles, recovered, Tol);
    }

    [Fact]
    public void ToEulerDegrees_RotZ90_XYZr()
    {
        var q = Maths.RotateZ(TestHelpers.PIOver2);
        var anglesDeg = Maths.ToEulerDegrees(q, EulerOrder.XYZr);
        TestHelpers.AssertFloat3Equal(new Float3(0f, 0f, 90f), anglesDeg, Tol);
    }

    [Fact]
    public void QuaternionEulerAnglesProperty_RoundTrip()
    {
        var angles = new Float3(10f, 20f, 30f);
        var q = Quaternion.Identity;
        q.eulerAngles = angles;
        TestHelpers.AssertFloat3Equal(angles, q.eulerAngles, Tol);
        var expected = Maths.FromEulerDegrees(angles, EulerOrder.ZXYr);
        TestHelpers.AssertQuaternionRotationallyEqual(expected, q, Tol);
    }
}
