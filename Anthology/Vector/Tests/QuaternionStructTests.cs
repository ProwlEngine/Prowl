namespace Prowl.Vector.Methods;

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
