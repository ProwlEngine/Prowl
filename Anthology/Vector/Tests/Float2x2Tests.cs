namespace Prowl.Vector.Methods;

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
