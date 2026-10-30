using System;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

// Transparent passes tagged "SceneDepth" = "Read" fetching the opaque depth behind them through Graphite/SceneDepth.glsl
[Collection("GPU Tests")]
public sealed class SceneDepthTests : IDisposable
{
    private const string BackdropSource = """
        Shader "Test/DepthBackdrop"
        {
            Pass
            {
                Tags { "LightMode" = "Forward" }
                Cull Off
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
                        outColor = vec4(1.0);
                    }
                }
                ENDGLSL
            }
        }
        """;

    private const string GlassSource = """
        Shader "Test/DepthGlass"
        {
            Pass
            {
                Tags { "LightMode" = "Forward" "RenderQueue" = "3000" "SceneDepth" = "Read" }
                Cull Off
                ZWrite Off
                GLSLPROGRAM
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    void main() { gl_Position = Graphite_RelativeToClip(Graphite_ObjectToRelative(vertexPosition)); }
                }
                Fragment
                {
                    #include "Graphite/SceneDepth.glsl"
                    layout(location = 0) out vec4 outColor;
                    void main() { outColor = vec4(Graphite_FetchSceneDepth(Graphite_ScreenUV()), 0.0, 0.0, 1.0); }
                }
                ENDGLSL
            }
        }
        """;

    private readonly RendererHarness _harness = new();
    private readonly Renderer _renderer;
    private readonly RenderWorld _world;
    private readonly GpuMesh _quad;

    public SceneDepthTests()
    {
        (_renderer, _world) = (_harness.Renderer, _harness.World);
        _quad = _harness.CreateQuad();

        // At z = 10, depth 0.9 through the shared projection
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) * Double4x4.CreateScale(2) }, [_renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(BackdropSource)))]);
    }

    public void Dispose() => _harness.Dispose();

    private Float4[] Frame()
    {
        _harness.Submit(new FrameDesc { Views = new[] { _harness.View() } });
        return _harness.ReadTarget();
    }

    [Fact]
    public void ATransparentPass_FetchesTheOpaqueDepthBehindIt_AndOnlyViewsReadingItCopyIt()
    {
        Frame();
        Assert.Null(_renderer.Frame.Camera(1)!.SceneDepth);

        Shader glass = _renderer.LoadShader(ShaderParser.Parse(GlassSource));
        Assert.True(glass.ReadsSceneDepth);
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 5)) }, [_renderer.CreateMaterial(glass)]);

        Float4[] seen = Frame();
        Assert.True(_renderer.LastFrameStats.View(1)!.SceneDepthUsed);
        Assert.All(seen, p => Assert.InRange(p.X, 0.9f - 1e-3f, 0.9f + 1e-3f));
    }
}
