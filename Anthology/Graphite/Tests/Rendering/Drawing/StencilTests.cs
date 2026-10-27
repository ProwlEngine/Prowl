using System;
using System.Runtime.InteropServices;

using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;
using Prowl.Vector;

using Xunit;

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
                        Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), 1.0, 0.0);
                        outColor = {{color}};
                    }
                }
                ENDGLSL
            }
        }
        """;

    private const int Size = 16;

    private static readonly Float4x4 s_projection = new(new Float4(0.25f, 0, 0, 0), new Float4(0, 0.25f, 0, 0), new Float4(0, 0, -0.01f, 0), new Float4(0, 0, 1, 1));

    private readonly GraphicsDevice _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
    private readonly GlslShaderCompiler _compiler = new();
    private readonly Texture _target;
    private readonly Framebuffer _framebuffer;
    private readonly DeviceBuffer _readback;
    private readonly Renderer _renderer;
    private readonly RenderWorld _world;
    private readonly GpuMesh _quad;

    public StencilTests()
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

    private void Add(string source, Double4x4 transform)
        => _world.Add(new RenderableDesc { Mesh = _quad, Transform = transform }, [_renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(source)))]);

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
