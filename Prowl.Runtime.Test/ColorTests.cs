// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public class ColorTests
{
    [Fact]
    public void Grayscale_Calculation_Is_Correct()
    {
        var color = new Color(0.5f, 0.5f, 0.5f, 1f);
        Assert.Equal(0.5f, color.Grayscale);
    }

    [Fact]
    public void Indexer_Get_Returns_Correct_Value()
    {
        var color = new Color(0.1f, 0.2f, 0.3f, 0.4f);
        Assert.Equal(0.1f, color[0]);
        Assert.Equal(0.2f, color[1]);
        Assert.Equal(0.3f, color[2]);
        Assert.Equal(0.4f, color[3]);
    }

    [Fact]
    public void Indexer_Set_Sets_Correct_Value()
    {
        var color = new Color(0.1f, 0.2f, 0.3f, 0.4f);
        color[0] = 0.5f;
        Assert.Equal(0.5f, color.R);
    }

    [Fact]
    public void Indexer_Set_Throws_Exception_For_Invalid_Index()
    {
        var color = new Color(0.1f, 0.2f, 0.3f, 0.4f);
        Assert.Throws<IndexOutOfRangeException>(() => color[4] = 0.5f);
    }

    [Fact]
    public void Lerp_Returns_Correct_Value()
    {
        var color1 = new Color(0f, 0f, 0f, 1f);
        var color2 = new Color(1f, 1f, 1f, 1f);
        var lerpColor = Color.Lerp(color1, color2, 0.5f);
        Assert.Equal(new Color(0.5f, 0.5f, 0.5f, 1f), lerpColor);
    }

    [Theory]
    [InlineData(0, 0, 0, 1, 0, 0, 0, 1)]
    [InlineData(1, 1, 1, 1, 1, 1, 1, 1)]
    [InlineData(0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f)]
    public void Equality_Operator_Works_Correctly(float r1, float g1, float b1, float a1, float r2, float g2, float b2,
        float a2)
    {
        var color1 = new Color(r1, g1, b1, a1);
        var color2 = new Color(r2, g2, b2, a2);
        Assert.Equal(r1 == r2 && g1 == g2 && b1 == b2 && a1 == a2, color1 == color2);
    }

    [Theory]
    [InlineData(0, 0, 0, 1, 0, 0, 0, 1)]
    [InlineData(1, 1, 1, 1, 0, 0, 0, 1)]
    [InlineData(0.5f, 0.5f, 0.5f, 0.5f, 1, 1, 1, 1)]
    public void Inequality_Operator_Works_Correctly(float r1, float g1, float b1, float a1, float r2, float g2,
        float b2, float a2)
    {
        var color1 = new Color(r1, g1, b1, a1);
        var color2 = new Color(r2, g2, b2, a2);
        Assert.Equal(r1 != r2 || g1 != g2 || b1 != b2 || a1 != a2, color1 != color2);
    }

    [Theory]
    [InlineData(0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 2)]
    [InlineData(1, 1, 1, 1, 0, 0, 0, 1, 1, 1, 1, 2)]
    [InlineData(0.5f, 0.5f, 0.5f, 0.5f, 1, 1, 1, 1, 1.5f, 1.5f, 1.5f, 1.5f)]
    public void Addition_Operator_Works_Correctly(float r1, float g1, float b1, float a1, float r2, float g2, float b2,
        float a2, float r3, float g3, float b3, float a3)
    {
        var color1 = new Color(r1, g1, b1, a1);
        var color2 = new Color(r2, g2, b2, a2);
        var result = color1 + color2;
        Assert.Equal(new Color(r3, g3, b3, a3), result);
    }

    [Theory]
    [InlineData(0, 0, 0, 1, 2, 0, 0, 0, 0.5f)]
    [InlineData(1, 1, 1, 1, 2, 0.5f, 0.5f, 0.5f, 0.5f)]
    [InlineData(0.5f, 0.5f, 0.5f, 0.5f, 2, 0.25f, 0.25f, 0.25f, 0.25f)]
    public void Division_Operator_Works_Correctly(float r1, float g1, float b1, float a1, float divisor, float r2, float g2, float b2, float a2)
    {
        var color1 = new Color(r1, g1, b1, a1);
        var result = color1 / divisor;
        Assert.Equal(new Color(r2, g2, b2, a2), result);
    }

    [Theory]
    [InlineData(0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1)]
    [InlineData(1, 1, 1, 1, 0, 0, 0, 1, 0, 0, 0, 1)]
    [InlineData(0.5f, 0.5f, 0.5f, 0.5f, 1, 1, 1, 0.5f, 0.5f, 0.5f, 0.5f, 0.25f)]
    public void Multiplication_Operator_Works_Correctly(float r1, float g1, float b1, float a1, float r2, float g2,
        float b2, float a2, float r3, float g3, float b3, float a3)
    {
        var color1 = new Color(r1, g1, b1, a1);
        var color2 = new Color(r2, g2, b2, a2);
        var result = color1 * color2;
        Assert.Equal(new Color(r3, g3, b3, a3), result);
    }

    [Theory]
    [InlineData(0, 0, 0, 1, 2, 0, 0, 0, 2)]
    [InlineData(1, 1, 1, 1, 2, 2, 2, 2, 2)]
    [InlineData(0.5f, 0.5f, 0.5f, 0.5f, 2, 1, 1, 1, 1)]
    public void Multiplication_By_Scalar_Works_Correctly(float r1, float g1, float b1, float a1, float scalar, float r2,
        float g2, float b2, float a2)
    {
        var color1 = new Color(r1, g1, b1, a1);
        var result = color1 * scalar;
        Assert.Equal(new Color(r2, g2, b2, a2), result);
    }

    [Theory]
    [InlineData(0, 0, 0, 1, 2, 0, 0, 0, 2)]
    [InlineData(1, 1, 1, 1, 2, 2, 2, 2, 2)]
    [InlineData(0.5f, 0.5f, 0.5f, 0.5f, 2, 1, 1, 1, 1)]
    public void Multiplication_By_Scalar_From_Left_Works_Correctly(float r1, float g1, float b1, float a1, float scalar,
        float r2, float g2, float b2, float a2)
    {
        var color1 = new Color(r1, g1, b1, a1);
        var result = color1 * scalar;
        Assert.Equal(new Color(r2, g2, b2, a2), result);
    }

    [Theory]
    [InlineData(0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0)]
    [InlineData(1, 1, 1, 1, 0, 0, 0, 1, 1, 1, 1, 0)]
    [InlineData(0.5f, 0.5f, 0.5f, 0.5f, 1, 1, 1, 1, -0.5f, -0.5f, -0.5f, -0.5f)]
    public void Subtraction_Operator_Works_Correctly(float r1, float g1, float b1, float a1, float r2, float g2,
        float b2, float a2, float r3, float g3, float b3, float a3)
    {
        var color1 = new Color(r1, g1, b1, a1);
        var color2 = new Color(r2, g2, b2, a2);
        var result = color1 - color2;
        Assert.Equal(new Color(r3, g3, b3, a3), result);
    }

    [Theory]
    [InlineData(0, 0, 0, 1, "RGBA(0, 0, 0, 1)")]
    [InlineData(1, 1, 1, 1, "RGBA(1, 1, 1, 1)")]
    [InlineData(0.5f, 0.5f, 0.5f, 0.5f, "RGBA(0.5, 0.5, 0.5, 0.5)")]
    public void ToString_Returns_Correct_Value(float r, float g, float b, float a, string expected)
    {
        var color = new Color(r, g, b, a);
        Assert.Equal(expected, color.ToString());
    }
}
