using System;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

using Xunit;

using static Prowl.Graphite.Rendering.Tests.RendererHarness;

namespace Prowl.Graphite.Rendering.Tests;

// A pass with a Stencil block masks what a later pass testing the same reference may draw
[Collection("GPU Tests")]
public sealed class StencilTests : IDisposable
{
    private static string Solid(string name, string state, string queue, string color) => $$"""
        Shader "Test/{{name}}"
        {
            Pass
            {
                Tags { "LightMode" = "Forward" "RenderQueue" = "{{queue}}" }
                Cull Off
                {{state}}
                GLSLPROGRAM
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    void main() { gl_Position = Graphite_RelativeToClip(Graphite_ObjectToRelative(vertexPosition)); }
                }
                Fragment
                {
                    #include "Graphite/Passes.glsl"
                    layout(location = 0) out vec4 outColor;
                    void main()
                    {
                        Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), vec3(0.0), 1.0);
                        outColor = {{color}};
                    }
                }
                ENDGLSL
            }
        }
        """;

    private readonly RendererHarness _harness = new();
    private readonly Renderer _renderer;
    private readonly RenderWorld _world;
    private readonly GpuMesh _quad;

    public StencilTests()
    {
        (_renderer, _world) = (_harness.Renderer, _harness.World);
        _quad = _harness.CreateQuad();
    }

    public void Dispose() => _harness.Dispose();

    private void Add(string source, Double4x4 transform)
        => _world.Add(new RenderableDesc { Mesh = _quad, Transform = transform }, [_renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(source)))]);

    private Float4[] Frame()
    {
        _harness.Submit(new FrameDesc { Views = new[] { _harness.View() } });
        return _harness.ReadTarget();
    }

    [Fact]
    public void AStencilEqualPass_DrawsOnlyWhereAMaskPassWroteTheReference()
    {
        Add(Solid("Backdrop", "", "2000", "vec4(1.0)"), Double4x4.CreateTranslation(new Double3(0, 0, 10)) * Double4x4.CreateScale(2));
        Add(Solid("Mask", "ZWrite Off\nBlend Zero One\nStencil { Ref 1 Comp Always Pass Replace }", "1990", "vec4(0.0)"), Double4x4.CreateTranslation(new Double3(-2, 0, 8)) * Double4x4.CreateScale(new Double3(0.5, 2, 1)));
        Add(Solid("Hidden", "Stencil { Ref 1 Comp Equal }", "2010", "vec4(1.0, 0.0, 0.0, 1.0)"), Double4x4.CreateTranslation(new Double3(0, 0, 5)) * Double4x4.CreateScale(2));

        Float4[] seen = Frame();

        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                Float4 p = seen[y * Size + x];
                if (x < Size / 2) Assert.True(p.X > 0.9f && p.Y < 0.1f, $"({x},{y}) inside the mask should be red, was {p}");
                else Assert.True(p.X > 0.9f && p.Y > 0.9f, $"({x},{y}) outside the mask should be the white backdrop, was {p}");
            }
    }
}
