using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.ShaderDef.Tests;


public class PropertyTests
{
    [Fact]
    public void ParsesNameDisplayNameAndType()
    {
        ShaderProperty p = Parse.Property("""_Tint("Tint Color", Color) = (1, 1, 1, 1)""");

        Assert.Equal("_Tint", p.Name);
        Assert.Equal("Tint Color", p.DisplayName);
        Assert.Equal(ShaderPropertyType.Color, p.PropertyType);
    }


    [Fact]
    public void Float_ParsesScalarIntoX()
    {
        ShaderProperty p = Parse.Property("""_R("Rough", Float) = 0.25""");

        Assert.Equal(ShaderPropertyType.Float, p.PropertyType);
        Assert.Equal(0.25f, p.Value.X);
    }


    [Fact]
    public void Float_ParsesNegative()
    {
        ShaderProperty p = Parse.Property("""_B("Bias", Float) = -1.5""");

        Assert.Equal(-1.5f, p.Value.X);
    }


    [Fact]
    public void Integer_StoredAsFloat()
    {
        ShaderProperty p = Parse.Property("""_C("Count", Integer) = 7""");

        Assert.Equal(ShaderPropertyType.Integer, p.PropertyType);
        Assert.Equal(7f, p.Value.X);
    }


    [Theory]
    [InlineData("Color")]
    [InlineData("Vector")]
    public void ColorOrVector_ParsesFourComponents(string type)
    {
        ShaderProperty p = Parse.Property($$"""_V("V", {{type}}) = (1, 2, 3, 4)""");

        Assert.Equal(new Float4(1, 2, 3, 4), p.Value);
    }


    [Fact]
    public void Vector_ParsesNegativeComponents()
    {
        ShaderProperty p = Parse.Property("""_V("V", Vector) = (-1, -2, -3, -4)""");

        Assert.Equal(new Float4(-1, -2, -3, -4), p.Value);
    }


    [Fact]
    public void Matrix_ParsesColumnsAsIdentity()
    {
        ShaderProperty p = Parse.Property("""_M("M", Matrix) = ((1,0,0,0)(0,1,0,0)(0,0,1,0)(0,0,0,1))""");

        Assert.Equal(ShaderPropertyType.Matrix, p.PropertyType);
        Assert.Equal(1f, p.MatrixValue[0, 0]);
        Assert.Equal(1f, p.MatrixValue[1, 1]);
        Assert.Equal(1f, p.MatrixValue[3, 3]);
        Assert.Equal(0f, p.MatrixValue[0, 1]);
    }


    [Theory]
    [InlineData("Texture2D", ShaderPropertyType.Texture2D)]
    [InlineData("Texture2DArray", ShaderPropertyType.Texture2DArray)]
    [InlineData("Texture3D", ShaderPropertyType.Texture3D)]
    [InlineData("TextureCubemap", ShaderPropertyType.TextureCubemap)]
    [InlineData("TextureCubemapArray", ShaderPropertyType.TextureCubemapArray)]
    public void Texture_ParsesTypeAndDefaultName(string type, ShaderPropertyType expected)
    {
        ShaderProperty p = Parse.Property($$"""_T("Tex", {{type}}) = "white" {}""");

        Assert.Equal(expected, p.PropertyType);
        Assert.Equal("white", p.TextureValue);
    }


    [Fact]
    public void OverflowingInteger_ThrowsParseException()
    {
        ParseException ex = Assert.Throws<ParseException>(
            () => Parse.Property("""_C("Count", Integer) = 99999999999999999999"""));

        Assert.Contains("integer", ex.Message);
    }


    [Fact]
    public void MissingName_ReportsPropertyName()
    {
        ParseException ex = Assert.Throws<ParseException>(
            () => Parse.Property("""("X", Float) = 1"""));

        Assert.Contains("property name", ex.Message);
    }


    [Fact]
    public void MissingOpenParen_ReportsLiteralSymbol()
    {
        // The diagnostic should name the literal '(' rather than the token kind "OpenParen".
        ParseException ex = Assert.Throws<ParseException>(
            () => Parse.Property("""_X "X", Float) = 1"""));

        Assert.Contains("'('", ex.Message);
    }


    [Fact]
    public void Float_GivenVector_ReportsExpectedShape()
    {
        ParseException ex = Assert.Throws<ParseException>(
            () => Parse.Property("""_R("Rough", Float) = (1, 2, 3, 4)"""));

        Assert.Contains("Float property expects a scalar", ex.Message);
    }


    [Fact]
    public void Vector_GivenScalar_ReportsExpectedShape()
    {
        ParseException ex = Assert.Throws<ParseException>(
            () => Parse.Property("""_V("V", Vector) = 0.5"""));

        Assert.Contains("Vector property expects a 4-component", ex.Message);
    }


    [Fact]
    public void Texture_GivenNumber_ReportsExpectedShape()
    {
        ParseException ex = Assert.Throws<ParseException>(
            () => Parse.Property("""_T("Tex", Texture2D) = 3"""));

        Assert.Contains("Texture2D property expects a texture name", ex.Message);
    }


    [Fact]
    public void NoDefaultValue_LeavesDefaults()
    {
        ShaderProperty p = Parse.Property("""_X("X", Float)""");

        Assert.Equal(Float4.Zero, p.Value);
        Assert.Equal("", p.TextureValue);
    }
}
