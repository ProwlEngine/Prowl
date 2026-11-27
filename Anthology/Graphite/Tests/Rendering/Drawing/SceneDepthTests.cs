using System;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

// Transparent passes tagged "SceneDepth" = "Read" fetching the opaque depth behind them through Graphite/SceneDepth.glsl, and which views copy their depth and colour
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

    private const string ColorGlassSource = """
        Shader "Test/ColorGlass"
        {
            Pass
            {
                Tags { "LightMode" = "Forward" "RenderQueue" = "3000" "SceneColor" = "Read" }
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
                    #include "Graphite/SceneColor.glsl"
                    layout(location = 0) out vec4 outColor;
                    void main() { outColor = vec4(Graphite_SampleSceneColor(Graphite_ScreenUV(), 0.0), 1.0); }
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

    private Float4[] Frame(int id = 1)
    {
        _harness.Submit(new FrameDesc { Views = new[] { _harness.View(id) } });
        return _harness.ReadTarget();
    }

    [Fact]
    public void ATransparentPass_FetchesTheOpaqueDepthBehindIt_AndOnlyViewsReadingItCopyIt()
    {
        Frame();
        Assert.Null(_renderer.Frame.Camera(_harness.ViewOf(1))!.SceneDepth);

        Shader glass = _renderer.LoadShader(ShaderParser.Parse(GlassSource));
        Assert.True(glass.ReadsSceneDepth);
        RenderableHandle depthReader = _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 5)) }, [_renderer.CreateMaterial(glass)]);

        Float4[] seen = Frame();
        Assert.True(_renderer.LastFrameStats.View(_harness.ViewOf(1))!.SceneDepthUsed);
        Assert.All(seen, p => Assert.InRange(p.X, 0.9f - 1e-3f, 0.9f + 1e-3f));

        CameraFrame Camera(int id = 1) => _renderer.Frame.Camera(_harness.ViewOf(id))!;
        ViewStats Drawn(int id = 1) => _renderer.LastFrameStats.View(_harness.ViewOf(id))!;

        Shader colorReading = _renderer.LoadShader(ShaderParser.Parse(ColorGlassSource));
        Shader plain = _renderer.LoadShader(ShaderParser.Parse(ColorGlassSource.Replace("Test/ColorGlass", "Test/PlainGlass").Replace(" \"SceneColor\" = \"Read\"", "")));
        Assert.True(colorReading.ReadsSceneColor);
        Assert.False(plain.ReadsSceneColor);

        RenderableHandle other = _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 5)) }, [_renderer.CreateMaterial(plain)]);
        Frame();
        Assert.Null(Camera().SceneColor);

        _world.Remove(other);
        RenderableHandle colorReader = _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 5)) }, [_renderer.CreateMaterial(colorReading)]);
        Frame();
        Assert.NotNull(Camera().SceneColor);
        Assert.True(Drawn().SceneColorUsed);

        // Once the reader is gone the view keeps its graph but records no copy
        _world.Remove(colorReader);
        _world.Remove(depthReader);
        Frame();
        Assert.False(Drawn().SceneColorUsed);

        // Only views drawing a transmissive surface copy their colour and depth and draw back depth, volumes alone the
        // latter, and LITE views none of it
        Material transmissive = _renderer.CreateMaterial(_renderer.Standard.Lit);
        transmissive.Surface = new SurfaceSettings(SurfaceType.Transparent);
        _world.Add(new RenderableDesc { Mesh = _harness.CreateQuad(normals: true), Transform = Double4x4.CreateTranslation(new Double3(0, 0, 5)) }, [transmissive]);
        Frame(2);
        Assert.False(Drawn(2).SceneColorUsed || Drawn(2).SceneDepthUsed || Drawn(2).BackDepthUsed);
        Assert.Null(Camera(2).SceneColor);
        Assert.Null(Camera(2).BackDepth);

        transmissive.SetFloat("transmission_weight", 1);
        Frame(2);
        Assert.True(Drawn(2).SceneColorUsed && Drawn(2).SceneDepthUsed && Drawn(2).BackDepthUsed);
        Assert.NotNull(Camera(2).SceneColor);
        Assert.NotNull(Camera(2).BackDepth);

        transmissive.SetInt("geometry_thin_walled", 1);
        Frame(2);
        Assert.True(Drawn(2).SceneColorUsed);
        Assert.False(Drawn(2).BackDepthUsed);

        transmissive.SetInt("geometry_thin_walled", 0);
        _world.Globals.Set(GlobalAxis.PbrTier, "LITE");
        Frame(2);
        Assert.False(Drawn(2).SceneColorUsed || Drawn(2).SceneDepthUsed || Drawn(2).BackDepthUsed);
    }
}
