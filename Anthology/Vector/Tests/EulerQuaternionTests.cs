namespace Prowl.Vector.Methods;

public class EulerQuaternionTests
{
    private const float Tol = TestHelpers.Tolerance;

    [Fact]
    public void EulerRadians_RoundTrip_ZYXr()
    {
        var angles = new Float3(70.1f, 343.8f, 30.3f);
        var q = Quaternion.FromEuler(angles);
        var recovered = Quaternion.ToEuler(q);
        TestHelpers.AssertFloat3Equal(angles, recovered, Tol);
    }
}
