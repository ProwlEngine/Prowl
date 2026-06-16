using System;

using Prowl.Vector;

using Prowl.Graphite.ShaderDef;

using Xunit;

namespace Prowl.Graphite.ShaderDef.Tests;


public class ParserTests
{
    const string FullShader = @"
Shader ""Example/Standard""
{
    // line comment
    Properties
    {
        _Color(""Tint"", Color) = (1, 0.5, 0.25, 1)
        _Rough(""Roughness"", Float) = 0.5
        _Count(""Count"", Integer) = 4
        _Mtx(""Matrix"", Matrix) = ((1,0,0,0)(0,1,0,0)(0,0,1,0)(0,0,0,1))
        _Albedo(""Albedo"", Texture2D) = ""white"" {}
    }

    Pass 0
    {
        Name ""Forward""
        Tags { ""LightMode"" = ""ForwardBase"" ""Queue"" = ""Geometry"" }

        Cull Back
        ZTest LessEqual
        ZWrite On
        Offset -1 -1   /* block comment */
        ColorMask RGBA
        Blend SourceAlpha InverseSourceAlpha

        Stencil
        {
            Ref 2
            Comp Equal
            Pass Keep
        }

        ShaderSource ""Standard.slang""
        {
            Vertex ""vsMain""
            Fragment ""fsMain""
        }
    }

    Fallback ""Hidden/Fallback""
}";


    [Fact]
    public void ParsesShaderNameAndFallback()
    {
        ParsedShader shader = ParsedShader.Parse(FullShader);

        Assert.Equal("Example/Standard", shader.Name);
        Assert.Equal("Hidden/Fallback", shader.Fallback);
    }


    [Fact]
    public void ParsesAllPropertyTypes()
    {
        ShaderProperty[] props = ParsedShader.Parse(FullShader).Properties!;

        Assert.Equal(5, props.Length);

        Assert.Equal("_Color", props[0].Name);
        Assert.Equal("Tint", props[0].DisplayName);
        Assert.Equal(ShaderPropertyType.Color, props[0].PropertyType);
        Assert.Equal(new Float4(1, 0.5f, 0.25f, 1), props[0].Value);

        Assert.Equal(ShaderPropertyType.Float, props[1].PropertyType);
        Assert.Equal(0.5f, props[1].Value.X);

        Assert.Equal(ShaderPropertyType.Integer, props[2].PropertyType);
        Assert.Equal(4f, props[2].Value.X);

        Assert.Equal(ShaderPropertyType.Matrix, props[3].PropertyType);
        Assert.Equal(1f, props[3].MatrixValue[0, 0]);
        Assert.Equal(1f, props[3].MatrixValue[3, 3]);

        Assert.Equal(ShaderPropertyType.Texture2D, props[4].PropertyType);
        Assert.Equal("white", props[4].TextureValue);
    }


    [Fact]
    public void ParsesPassNameAndTags()
    {
        ParsedPass pass = ParsedShader.Parse(FullShader).Passes![0];

        Assert.Equal("Forward", pass.Name);
        Assert.NotNull(pass.Tags);
        Assert.Equal("ForwardBase", pass.Tags!["LightMode"]);
        Assert.Equal("Geometry", pass.Tags!["Queue"]);
    }


    [Fact]
    public void ParsesRenderState()
    {
        ParsedPassState s = ParsedShader.Parse(FullShader).Passes![0].State;

        Assert.Equal(FaceCullMode.Back, s.CullMode);
        Assert.Equal(ComparisonKind.LessEqual, s.DepthFunc);
        Assert.True(s.DepthWriteMask);
        Assert.Equal(ColorWriteMask.All, s.WriteMask);
        Assert.Equal(BlendFactor.SourceAlpha, s.BlendSrcRgb);
        Assert.Equal(BlendFactor.InverseSourceAlpha, s.BlendDstRgb);
    }


    [Fact]
    public void ParsesNegativeOffset()
    {
        ParsedPassState s = ParsedShader.Parse(FullShader).Passes![0].State;

        Assert.True(s.EnablePolygonOffsetFill);
        Assert.Equal(-1f, s.PolygonOffsetFactor);
        Assert.Equal(-1f, s.PolygonOffsetUnits);
    }


    [Fact]
    public void ParsesStencilBlock()
    {
        ParsedPassState s = ParsedShader.Parse(FullShader).Passes![0].State;

        Assert.Equal(2, s.StencilFrontRef);
        Assert.Equal(2, s.StencilBackRef);
        Assert.Equal(ComparisonKind.Equal, s.StencilFrontFunc);
        Assert.Equal(StencilOperation.Keep, s.StencilFrontPassOp);
    }


    [Fact]
    public void ParsesShaderSource()
    {
        ParsedShaderSource src = ParsedShader.Parse(FullShader).Passes![0].Source;

        Assert.Equal("Standard.slang", src.ShaderSourceFile);
        Assert.Equal("vsMain", src.VertexEntrypoint);
        Assert.Equal("fsMain", src.FragmentEntrypoint);
    }


    [Fact]
    public void ColorMaskSubset_ParsesIndividualChannels()
    {
        ParsedShader shader = ParsedShader.Parse(MinimalShaderWith("ColorMask RG"));
        ColorWriteMask? mask = shader.Passes![0].State.WriteMask;

        Assert.Equal(ColorWriteMask.Red | ColorWriteMask.Green, mask);
    }


    [Fact]
    public void OptionalPropertiesBlock_DefaultsToEmpty()
    {
        ParsedShader shader = ParsedShader.Parse(MinimalShaderWith("ZWrite Off"));

        Assert.Empty(shader.Properties!);
        Assert.False(shader.Passes![0].State.DepthWriteMask);
    }


    [Fact]
    public void MalformedShader_Throws()
    {
        Assert.ThrowsAny<Exception>(() => ParsedShader.Parse("Shader \"Broken\" { Fallback }"));
    }


    // A minimal valid shader with a single render-state command injected into its pass.
    static string MinimalShaderWith(string renderState) => $@"
Shader ""Min""
{{
    Pass
    {{
        {renderState}
        ShaderSource ""s.slang""
        {{
            Vertex ""v""
            Fragment ""f""
        }}
    }}
    Fallback ""fb""
}}";
}
