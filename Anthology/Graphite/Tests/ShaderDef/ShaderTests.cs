using System;
using System.Linq;

using Prowl.Graphite.ShaderDef.Compiler;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.ShaderDef.Tests;

// End-to-end coverage of ShaderParser.Parse composing all components together.
public class ShaderTests
{
    const string FullShader = """
        Shader "Example/Standard"
        {
            // line comment
            Properties
            {
                _Color("Tint", Color) = (1, 0.5, 0.25, 1)
                _Albedo("Albedo", Texture2D) = "white" {}
                _Rough("Rough", Float) = 0.25 [Range(0, -1.5)] [HideInInspector] [ShowIf(_SURFACE_CUTOUT)]
                _Dir("Dir", Vector) = (1, 2, 3, 4)
                _Pan("Pan", Vector2) = (1, 2)
                _Pos("Pos", Vector3) = (1, 2, 3)
                _Basis("Basis", Matrix) = ((1,0,0,0)(0,1,0,0)(0,0,1,0)(0,0,0,1))
                _Probes("Probes", TextureCubemapArray) = "white" {}
                _Tinted("Tinted", Color) [Gamma]
            }

            Pass 0
            {
                Name "Forward"
                Tags { "LightMode" = "ForwardBase" }

                Cull Back
                ZTest LessEqual
                Offset -1 -1   /* block comment */

                Stencil { Ref 2 Comp Equal Pass Keep }

                GLSLPROGRAM
                float4 vsMain() : SV_Position { return 0; }
                float4 fsMain() : SV_Target { return 1; }
                ENDGLSL
            }

            Pass 1
            {
                Name "Shadow"
                GLSLPROGRAM
                void shadowMain() {}
                ENDGLSL
            }

            Fallback "Hidden/Fallback"
        }
        """;

    [Fact]
    public void SurfaceBlocks_ParseAsData_WithoutAnyRendererLibrary()
    {
        ShaderDefinition shader = ShaderParser.Parse("""
            Shader "Example/Rim"
            {
                Properties { _RimColor("Rim", Color) = (1, 1, 1, 1) }
                Surface "Lit"
                {
                    GLSLPROGRAM
                    void Surface(inout PBR_Params p, Graphite_SurfaceData i) { }
                    ENDGLSL
                }
            }
            """);

        Assert.Equal("Lit", shader.Surface!.Model);
        Assert.Contains("void Surface", shader.Surface.Code);
        Assert.Empty(shader.Passes!);
        Assert.Single(shader.Properties!);
    }

    [Fact]
    public void ParsesNamePropertiesPassesAndFallback()
    {
        ShaderDefinition shader = Parse.Shader(FullShader);

        Assert.Equal("Example/Standard", shader.Name);
        Assert.Equal("Hidden/Fallback", shader.Fallback);
        Assert.Equal(9, shader.Properties!.Length);
        Assert.Equal(["Forward", "Shadow"], shader.Passes!.Select(p => p.Name));
        Assert.Equal(new Float4(1, 0.5f, 0.25f, 1), shader.Properties[0].Value);
        Assert.Empty(shader.Properties[0].Attributes);

        ShaderProperty rough = shader.Properties.Single(p => p.Name == "_Rough");
        Assert.Equal(ShaderPropertyType.Float, rough.PropertyType);
        Assert.Equal(0.25f, rough.Value.X);
        Assert.Equal(["Range(0, -1.5)", "HideInInspector", "ShowIf(_SURFACE_CUTOUT)"], rough.Attributes);
        Assert.Equal(["Gamma"], shader.Properties.Single(p => p.Name == "_Tinted").Attributes);
        Assert.Equal(new Float4(1, 2, 3, 4), shader.Properties.Single(p => p.Name == "_Dir").Value);
        Assert.Equal(new Float4(1, 2, 0, 0), shader.Properties.Single(p => p.Name == "_Pan").Value);
        Assert.Equal(new Float4(1, 2, 3, 0), shader.Properties.Single(p => p.Name == "_Pos").Value);
        Assert.Throws<ParseException>(() => Parse.Property("""_V("V", Vector2) = (1, 2, 3)"""));

        ShaderProperty basis = shader.Properties.Single(p => p.Name == "_Basis");
        Assert.Equal(ShaderPropertyType.Matrix, basis.PropertyType);
        Assert.Equal(1f, basis.MatrixValue[0, 0]);
        Assert.Equal(1f, basis.MatrixValue[1, 1]);
        Assert.Equal(1f, basis.MatrixValue[3, 3]);
        Assert.Equal(0f, basis.MatrixValue[0, 1]);

        ShaderProperty probes = shader.Properties.Single(p => p.Name == "_Probes");
        Assert.Equal(ShaderPropertyType.TextureCubemapArray, probes.PropertyType);
        Assert.Equal("white", probes.TextureValue);
    }
}
