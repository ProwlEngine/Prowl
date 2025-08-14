namespace Prowl.Vector.Methods;

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
