using System;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

using Xunit;

using Vector3 = System.Numerics.Vector3;

namespace Prowl.Graphite.Rendering.Tests;

// Graphite/PBR/Energy.glsl reads the tables every view binds, filtered from texel centres, and its Turquin helpers
// agree with the CPU's.
public sealed partial class LightingTests
{
    private const string TablesSource = """
        Shader "Test/Tables"
        {
            Properties
            {
                _Eta("Eta", Float) = 1.5
                _Mode("Mode", Float) = 0
            }

            Pass
            {
                Tags { "LightMode" = "Forward" }
                Cull Off
                GLSLPROGRAM
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    layout(location = 0) out vec3 vWorld;
                    void main()
                    {
                        vWorld = Graphite_ObjectToRelative(vertexPosition);
                        gl_Position = Graphite_RelativeToClip(vWorld);
                    }
                }
                Fragment
                {
                    #include "Graphite/PBR/Energy.glsl"
                    #include "Graphite/Passes.glsl"
                    uniform float _Eta;
                    uniform float _Mode;
                    layout(location = 0) in vec3 vWorld;
                    layout(location = 0) out vec4 outColor;

                    void main()
                    {
                        // Pixel x of 16 gives NdotV x / 15 and pixel y r, so both ends of each axis are drawn
                        vec2 t = clamp(((vWorld.xy * 0.25 + 1.0) * 8.0 - 0.5) / 15.0, 0.0, 1.0);
                        float NdotV = t.x, r = t.y;
                        vec3 dfg = PBR_Dfg(NdotV, r);
                        if (_Mode > 1.5)
                            outColor = vec4(PBR_AnisotropicDfg(NdotV, r, _Eta, 0.37), 0.0);
                        else if (_Mode > 0.5)
                            outColor = vec4(PBR_TurquinMetal(dfg, vec3(1.0), vec3(1.0), 1.0).r,
                                            PBR_TurquinMetal(dfg, vec3(0.6), vec3(0.8), 0.7).r,
                                            PBR_TurquinDielectric(dfg, NdotV, r, _Eta),
                                            PBR_DielectricE(NdotV, r, 1.0));
                        else
                            outColor = vec4(dfg, PBR_DielectricE(NdotV, r, _Eta));
                        Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), vec3(0.0), 1.0);
                    }
                }
                ENDGLSL
            }
        }
        """;

    private Float4[] TablesFrame(float eta, float mode)
    {
        Material material = _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(TablesSource)));
        material.SetFloat("_Eta", eta);
        material.SetFloat("_Mode", mode);
        AddQuad(material);
        return Frame();
    }

    private static (double NdotV, double R) TableCoord(int pixel)
    {
        Vector3 p = PixelPosition(pixel % Size, pixel / Size);
        return (Math.Clamp(((p.X * 0.25 + 1) * 8 - 0.5) / 15, 0, 1), Math.Clamp(((p.Y * 0.25 + 1) * 8 - 0.5) / 15, 0, 1));
    }

    private static void AssertNear(double expected, float got, string what, int pixel)
        => Assert.True(Math.Abs(expected - got) <= 1e-3 + 1e-3 * Math.Abs(expected), $"pixel {pixel} {what}: expected {expected}, got {got}");

    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.07f)]
    [InlineData(1.5f)]
    [InlineData(2.4f)]
    public void EnergyTables_SampleAsTheCpuFiltersThem(float eta)
    {
        ushort[] dfg = EnergyTables.EmbeddedDfg(), dielectric = EnergyTables.EmbeddedDielectric();
        Float4[] pixels = TablesFrame(eta, 0);
        for (int i = 0; i < pixels.Length; i++)
        {
            (double mu, double r) = TableCoord(i);
            Vector3 want = EnergyTables.LookupDfg(dfg, mu, r);
            AssertNear(want.X, pixels[i].X, "scale", i);
            AssertNear(want.Y, pixels[i].Y, "bias", i);
            AssertNear(want.Z, pixels[i].Z, "F82", i);
            AssertNear(EnergyTables.LookupDielectric(dielectric, mu, r, eta), pixels[i].W, "dielectric E", i);
        }
    }

    [Fact]
    public void EnergyTables_TurquinHelpersMatchTheCpu()
    {
        const float eta = 1.5f;
        ushort[] dfg = EnergyTables.EmbeddedDfg(), dielectric = EnergyTables.EmbeddedDielectric();
        Float4[] pixels = TablesFrame(eta, 1);
        for (int i = 0; i < pixels.Length; i++)
        {
            (double mu, double r) = TableCoord(i);
            Vector3 table = EnergyTables.LookupDfg(dfg, mu, r);
            AssertNear(1, pixels[i].X, "white metal", i);
            AssertNear(EnergyTables.TurquinMetal(table, 0.6, 0.8, 0.7), pixels[i].Y, "tinted metal", i);
            AssertNear(EnergyTables.TurquinDielectric(table, EnergyTables.LookupDielectric(dielectric, mu, r, eta), eta), pixels[i].Z, "dielectric", i);
            Assert.True(pixels[i].W == 0, $"pixel {i}: E at eta 1 is {pixels[i].W}");
        }
    }

    // The anisotropic table, filtered within an anisotropy level's block of azimuths and blended between levels
    [Theory]
    [InlineData(0f)]
    [InlineData(0.3f)]
    [InlineData(0.81f)]
    [InlineData(1f)]
    public void TheAnisotropicTable_SamplesAsTheCpuFiltersIt(float anisotropy)
    {
        ushort[] table = EnergyTables.EmbeddedAnisotropic();
        Float4[] pixels = TablesFrame(anisotropy, 2);
        for (int i = 0; i < pixels.Length; i++)
        {
            (double mu, double r) = TableCoord(i);
            Vector3 want = EnergyTables.LookupAnisotropic(table, mu, r, anisotropy, 0.37);
            AssertNear(want.X, pixels[i].X, "scale", i);
            AssertNear(want.Y, pixels[i].Y, "bias", i);
            AssertNear(want.Z, pixels[i].Z, "F82", i);
        }
    }
}
