using System;
using System.Linq;
using System.Runtime.InteropServices;

using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;
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
                        Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), 1.0, 0.0);
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

    private const int Size = 16;

    // Orthographic and reversed Z: z from 0 (depth 1) to 100 (depth 0), so a surface at z = 10 has depth 0.9
    private static readonly Float4x4 s_projection = new(new Float4(0.25f, 0, 0, 0), new Float4(0, 0.25f, 0, 0), new Float4(0, 0, -0.01f, 0), new Float4(0, 0, 1, 1));

    private readonly GraphicsDevice _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
    private readonly GlslShaderCompiler _compiler = new();
    private readonly Texture _target;
    private readonly Framebuffer _framebuffer;
    private readonly DeviceBuffer _readback;
    private readonly Renderer _renderer;
    private readonly RenderWorld _world;
    private readonly GpuMesh _quad;

    public SceneDepthTests()
    {
        _target = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(Size, Size, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
        _framebuffer = _device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, _target));
        _readback = _device.ResourceFactory.CreateBuffer(new BufferDescription(Size * Size * 16, BufferUsage.Staging));
        _renderer = new Renderer(_device, _compiler, new RendererOptions { Threading = RenderThreading.Inline, Deterministic = true, PageVertexWords = 1 << 16, PageIndices = 1 << 16 });
        _world = _renderer.CreateWorld();
        _world.Environment = new EnvironmentDesc { AmbientColor = default, Fog = default };

        Float3[] positions = [new(-4, -4, 0), new(4, -4, 0), new(4, 4, 0), new(-4, 4, 0)];
        _quad = _renderer.CreateMesh(new MeshData
        {
            VertexCount = 4,
            Streams = [new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() }],
            Indices = new uint[] { 0, 1, 2, 0, 2, 3 },
        });
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) * Double4x4.CreateScale(2) }, [_renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(BackdropSource)))]);
    }

    public void Dispose()
    {
        _renderer.Dispose();
        _readback.Dispose();
        _framebuffer.Dispose();
        _target.Dispose();
        _compiler.Dispose();
        _device.Dispose();
    }

    private Float4[] Frame()
    {
        ViewDesc view = new() { World = _world, ViewId = 1, Target = _framebuffer, Sky = false, ClearColor = new Float4(0, 0, 0, 1) };
        view.Layers[0] = new ViewLayer { View = Float4x4.Identity, Projection = s_projection };
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { view } });
        _device.WaitForIdle();
        _device.Record(cmd => cmd.CopyTextureToBuffer(_target, _readback, 0, TextureRegion.Whole(_target, 0, 0))).Wait();
        Float4[] pixels = _device.Map<Float4>(_readback).ToArray();
        _device.Unmap(_readback);
        return pixels;
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
        Assert.True(_renderer.Frame.Camera(1)!.SceneDepthUsed);
        Assert.All(seen, p => Assert.InRange(p.X, 0.9f - 1e-3f, 0.9f + 1e-3f));
    }
}
