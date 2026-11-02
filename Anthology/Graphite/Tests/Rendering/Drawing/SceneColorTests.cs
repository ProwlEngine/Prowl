using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

using Xunit;

using static Prowl.Graphite.Rendering.Tests.RendererHarness;

namespace Prowl.Graphite.Rendering.Tests;

// Transparent passes tagged "SceneColor" = "Read" sampling the opaque colour behind them through Graphite/SceneColor.glsl
[Collection("GPU Tests")]
public sealed class SceneColorTests : IDisposable
{
    // Colour from the world position, so a flipped or shifted read shows
    private const string BackdropSource = """
        Shader "Test/Backdrop"
        {
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
                        vec3 relative = Graphite_ObjectToRelative(vertexPosition);
                        vWorld = Graphite_RelativeToWorld(relative);
                        gl_Position = Graphite_RelativeToClip(relative);
                    }
                }
                Fragment
                {
                    #include "Graphite/Passes.glsl"
                    layout(location = 0) in vec3 vWorld;
                    layout(location = 0) out vec4 outColor;
                    void main()
                    {
                        Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), vec3(0.0), 1.0);
                        outColor = vec4(0.5 + vWorld.x / 8.0, 0.5 + vWorld.y / 8.0, 0.25, 1.0);
                    }
                }
                ENDGLSL
            }
        }
        """;

    // _Lod below zero reads the last mip, _Project finds the uv by projecting the position instead of from the fragment
    private const string GlassSource = """
        Shader "Test/Glass"
        {
            Properties
            {
                _Tint("Tint", Color) = (1, 1, 1, 1)
                _Lod("Lod", Float) = 0
                _Project("Project", Float) = 0
            }

            Pass
            {
                Tags { "LightMode" = "Forward" "RenderQueue" = "3000" "SceneColor" = "Read" }
                Cull Off
                ZWrite Off
                Blend SourceAlpha InverseSourceAlpha
                GLSLPROGRAM
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    layout(location = 0) out vec3 vRelative;
                    void main()
                    {
                        vRelative = Graphite_ObjectToRelative(vertexPosition);
                        gl_Position = Graphite_RelativeToClip(vRelative);
                    }
                }
                Fragment
                {
                    #include "Graphite/SceneColor.glsl"
                    uniform vec4 _Tint;
                    uniform float _Lod;
                    uniform float _Project;
                    layout(location = 0) in vec3 vRelative;
                    layout(location = 0) out vec4 outColor;
                    void main()
                    {
                        vec2 uv = _Project > 0.5 ? Graphite_RelativeToScreenUV(vRelative) : Graphite_ScreenUV();
                        float lod = _Lod < 0.0 ? Graphite_SceneColorMips() - 1.0 : _Lod;
                        outColor = vec4(Graphite_SampleSceneColor(uv, lod) * _Tint.rgb, 1.0);
                    }
                }
                ENDGLSL
            }
        }
        """;

    private static readonly Float3 s_tint = new(0.5f, 1, 2);

    private readonly RendererHarness _harness = new();
    private readonly GraphicsDevice _device;
    private readonly Renderer _renderer;
    private readonly RenderWorld _world;
    private readonly Shader _glass;
    private readonly GpuMesh _quad;
    private readonly List<RendererError> _errors;

    public SceneColorTests()
    {
        (_device, _renderer, _world, _errors) = (_harness.Device, _harness.Renderer, _harness.World, _harness.Errors);
        _glass = _renderer.LoadShader(ShaderParser.Parse(GlassSource));
        _quad = _harness.CreateQuad();
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) * Double4x4.CreateScale(2) }, [_renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(BackdropSource)))]);
    }

    public void Dispose() => _harness.Dispose();

    private RenderableHandle AddGlass(Shader shader, float lod = 0, bool project = false)
    {
        Material material = _renderer.CreateMaterial(shader);
        material.SetColor("_Tint", new Float4(s_tint.X, s_tint.Y, s_tint.Z, 1));
        if (shader.FindProperty("_Lod") >= 0)
        {
            material.SetFloat("_Lod", lod);
            material.SetFloat("_Project", project ? 1 : 0);
        }
        return _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 5)) }, [material]);
    }

    private RenderView View(int id = 1) => _harness.View(id);

    private Float4[] Frame(RenderView? view = null) => _harness.Frame(view ?? View());

    private static void AssertTinted(Float4[] behind, Float4[] seen, float tolerance = 2e-3f)
    {
        for (int i = 0; i < seen.Length; i++)
        {
            Float3 want = new Float3(behind[i].X, behind[i].Y, behind[i].Z) * s_tint;
            Float4 got = seen[i];
            Assert.True(MathF.Abs(got.X - want.X) < tolerance && MathF.Abs(got.Y - want.Y) < tolerance && MathF.Abs(got.Z - want.Z) < tolerance, $"pixel {i}: expected {want}, got {got}");
        }
    }

    private CameraFrame Camera(int id = 1) => _renderer.Frame.Camera(_harness.ViewOf(id))!;

    private ViewStats Drawn(int id = 1) => _renderer.LastFrameStats.View(_harness.ViewOf(id))!;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ATransparentPass_ReadsTheOpaqueColourBehindIt(bool project)
    {
        Float4[] behind = Frame();
        Assert.True(behind.Distinct().Count() > 16, "the backdrop shows too little");
        AddGlass(_glass, project: project);
        AssertTinted(behind, Frame());
    }

    [Fact]
    public void TheLastMip_HoldsTheMeanOfTheView()
    {
        Float4[] behind = Frame();
        AddGlass(_glass, lod: -1);
        Float3 mean = default;
        foreach (Float4 p in behind)
            mean += new Float3(p.X, p.Y, p.Z) / behind.Length;
        AssertTinted(behind.Select(_ => new Float4(mean.X, mean.Y, mean.Z, 1)).ToArray(), Frame());
    }

    [Fact]
    public void TheCopyRuns_OnlyForViewsWhoseTransparentPassesReadIt()
    {
        Shader plain = _renderer.LoadShader(ShaderParser.Parse(GlassSource.Replace("Test/Glass", "Test/PlainGlass").Replace(" \"SceneColor\" = \"Read\"", "")));
        Assert.True(_glass.ReadsSceneColor);
        Assert.False(plain.ReadsSceneColor);

        RenderableHandle other = AddGlass(plain);
        Frame();
        Assert.Null(Camera().SceneColor);

        _world.Remove(other);
        RenderableHandle reader = AddGlass(_glass);
        Frame();
        Assert.NotNull(Camera().SceneColor);
        Assert.True(Drawn().SceneColorUsed);

        // Once the reader is gone the view keeps its graph but records no copy
        _world.Remove(reader);
        Frame();
        Assert.False(Drawn().SceneColorUsed);
    }

    [Fact]
    public void LayeredViews_ReadTheirOwnLayer()
    {
        AddGlass(_glass, lod: 1);
        using Texture layers = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(Size, Size, 1, 2, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
        using Framebuffer layered = _device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, layers) { ViewCount = 2 });
        RenderView stereo = _harness.BareView(5);
        stereo.Target = layered;
        stereo.Sky = false;
        stereo.ClearColor = new Float4(0, 0, 0, 1);
        ViewLayer[] cameraLayers = new ViewLayer[2];
        for (int layer = 0; layer < 2; layer++)
            cameraLayers[layer] = new ViewLayer { View = Float4x4.Identity, Projection = RendererHarness.Projection, Position = new Double3(layer * 2 - 1, layer, 0) };
        stereo.Camera = new ViewCamera(cameraLayers);
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { stereo } });
        _device.WaitForIdle();
        Assert.Empty(_errors);

        for (int layer = 0; layer < 2; layer++)
        {
            using DeviceBuffer readback = _device.ResourceFactory.CreateBuffer(new BufferDescription(Size * Size * 16, BufferUsage.Staging));
            _device.Record(cmd => cmd.CopyTextureToBuffer(layers, readback, 0, TextureRegion.Whole(layers, 0, (uint)layer))).Wait();
            Float4[] got = _device.Map<Float4>(readback).ToArray();
            _device.Unmap(readback);

            RenderView mono = View(10 + layer);
            mono.Camera = new ViewCamera(stereo.Camera.Layers[layer]);
            Float4[] want = Frame(mono);
            Assert.True(want.Distinct().Count() > 16, $"layer {layer} shows too little");
            for (int i = 0; i < want.Length; i++)
                Assert.True(MathF.Abs(want[i].X - got[i].X) < 2e-3f && MathF.Abs(want[i].Y - got[i].Y) < 2e-3f && MathF.Abs(want[i].Z - got[i].Z) < 2e-3f, $"layer {layer} pixel {i}: mono {want[i]}, layered {got[i]}");
        }
    }
}
