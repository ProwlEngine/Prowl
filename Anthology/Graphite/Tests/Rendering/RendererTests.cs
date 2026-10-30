using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

[Collection("GPU Tests")]
public sealed class RendererTests : IDisposable
{
    private const string Source = """
        Shader "Test/Unlit"
        {
            Properties
            {
                _Color("Tint", Color) = (1, 1, 1, 1)
            }

            Pass
            {
                Tags { "LightMode" = "Prepass" }
                Cull Back
                GLSLPROGRAM
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    void main() { gl_Position = Graphite_ObjectToClip(vertexPosition); }
                }
                Fragment
                {
                    void main() { }
                }
                ENDGLSL
            }

            Pass
            {
                Tags { "LightMode" = "Forward" }
                Cull Back
                GLSLPROGRAM
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    void main() { gl_Position = Graphite_ObjectToClip(vertexPosition); }
                }
                Fragment
                {
                    #include "Graphite/Passes.glsl"
                    #include "Graphite/Core.glsl"
                    uniform vec4 _Color;
                    layout(location = 0) out vec4 outColor;
                    void main() { Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), vec3(0.0), 1.0); outColor = _Color; }
                }
                ENDGLSL
            }
        }
        """;

    private const string GlassSource = """
        Shader "Test/Glass"
        {
            Properties
            {
                _Color("Tint", Color) = (1, 1, 1, 0.5)
            }

            Pass
            {
                Tags { "LightMode" = "Forward" "RenderQueue" = "3000" }
                Cull Back
                ZWrite Off
                Blend SourceAlpha InverseSourceAlpha
                GLSLPROGRAM
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    void main() { gl_Position = Graphite_ObjectToClip(vertexPosition); }
                }
                Fragment
                {
                    #include "Graphite/Core.glsl"
                    uniform vec4 _Color;
                    layout(location = 0) out vec4 outColor;
                    void main() { outColor = _Color; }
                }
                ENDGLSL
            }
        }
        """;

    private const uint Size = 16;

    private readonly GraphicsDevice _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
    private readonly GlslShaderCompiler _compiler = new();
    private readonly Texture _target;
    private readonly Framebuffer _framebuffer;
    private readonly DeviceBuffer _readback;
    private Renderer _renderer = null!;
    private RenderWorld _world = null!;
    private Shader _shader = null!;
    private GpuMesh _quad = null!;
    private readonly List<RendererError> _errors = new();

    // Orthographic and reversed Z: sees x and y from -4 to 4, and z from 0 (depth 1) to 100 (depth 0).
    private static readonly Float4x4 s_projection = new(new Float4(0.25f, 0, 0, 0), new Float4(0, 0.25f, 0, 0), new Float4(0, 0, -0.01f, 0), new Float4(0, 0, 1, 1));

    public RendererTests()
    {
        _target = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(Size, Size, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget | TextureUsage.Sampled));
        _framebuffer = _device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, _target));
        _readback = _device.ResourceFactory.CreateBuffer(new BufferDescription(Size * Size * 4, BufferUsage.Staging));
        Start(RenderThreading.Inline);
    }

    private void Start(RenderThreading threading)
    {
        _renderer = new Renderer(_device, _compiler, new RendererOptions { Threading = threading, Deterministic = threading == RenderThreading.Inline, PageVertexWords = 1 << 16, PageIndices = 1 << 16 });
        _renderer.Error += _errors.Add;
        _world = _renderer.CreateWorld();
        _shader = _renderer.LoadShader(ShaderParser.Parse(Source));
        Float3[] positions = [new(-0.5f, -0.5f, 0), new(0.5f, -0.5f, 0), new(0.5f, 0.5f, 0), new(-0.5f, 0.5f, 0)];
        _quad = _renderer.CreateMesh(new MeshData
        {
            VertexCount = 4,
            Streams = [new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() }],
            Indices = new uint[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 },
            SubMeshes = [new SubMesh { IndexStart = 0, IndexCount = 12, Topology = PrimitiveTopology.TriangleList }],
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

    private Material Colored(float r, float g, float b)
    {
        Material material = _renderer.CreateMaterial(_shader);
        material.SetColor("_Color", new Float4(r, g, b, 1));
        return material;
    }

    private RenderableHandle Add(Material material, Double3 position, Float3? scale = null)
        => _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(position) * (Double4x4)Float4x4.CreateScale(scale ?? new Float3(2, 2, 2)) }, [material]);

    private Color32[] Frame(Double3 camera = default)
    {
        ViewDesc view = new() { World = _world, ViewId = 1, Target = _framebuffer, ClearColor = new Float4(0, 0, 0, 1) };
        view.Layers[0] = new ViewLayer { View = Float4x4.Identity, Projection = s_projection, Position = camera };
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { view } });
        _renderer.WaitForRenderThread();
        Assert.DoesNotContain(_errors, e => e.Message.StartsWith("The frame failed"));
        _device.WaitForIdle();
        _device.Record(cmd => cmd.CopyTextureToBuffer(_target, _readback, 0, TextureRegion.Whole(_target, 0, 0))).Wait();
        Color32[] pixels = _device.Map<Color32>(_readback).ToArray();
        _device.Unmap(_readback);
        return pixels;
    }

    private static Color32 At(Color32[] pixels, int x, int y) => pixels[y * Size + x];

    [Fact]
    public void DrawCallGroups_NameTheRenderableAndMaterialTheyDraw()
    {
        Material material = Colored(1, 0, 0);
        material.Name = "Red";
        RenderableHandle handle = _world.Add(new RenderableDesc { Mesh = _quad, Name = "Quad", Transform = Double4x4.CreateScale(new Double3(2, 2, 2)) }, [material]);
        PayloadSink sink = new();
        ViewDesc view = new() { World = _world, ViewId = 1, Target = _framebuffer, ClearColor = new Float4(0, 0, 0, 1) };
        view.Layers[0] = new ViewLayer { View = Float4x4.Identity, Projection = s_projection };

        _renderer.SubmitFrame(new FrameDesc { Views = new[] { view }, Profilers = [sink] });
        _renderer.WaitForRenderThread();
        _device.WaitForIdle();

        DrawnRenderable[] drawn = sink.Payloads.OfType<DrawCallGroup>().SelectMany(g => g.Commands).SelectMany(c => c.Renderables).ToArray();
        Assert.NotEmpty(drawn);
        Assert.All(drawn, r => Assert.Equal((handle, "Quad", "Red"), (r.Handle, r.Name, r.Material)));
    }

    [Fact]
    public void Frames_DrawEveryMaterial_InTheFirstFrame()
    {
        Add(Colored(1, 0, 0), new Double3(-2, 0, 10));
        Add(Colored(0, 1, 0), new Double3(2, 0, 10));

        Color32[] pixels = Frame();
        Assert.Equal(new Color32(255, 0, 0, 255), At(pixels, 4, 8));
        Assert.Equal(new Color32(0, 255, 0, 255), At(pixels, 12, 8));
        Assert.Equal(new Color32(0, 0, 0, 255), At(pixels, 8, 8));
        Assert.Empty(_errors);
    }

    [Fact]
    public void NearerObjects_Win_WhateverTheirOrder()
    {
        Add(Colored(1, 0, 0), new Double3(0, 0, 50));
        Add(Colored(0, 0, 1), new Double3(0, 0, 10));
        Add(Colored(0, 1, 0), new Double3(0, 0, 80));

        Assert.Equal(new Color32(0, 0, 255, 255), At(Frame(), 8, 8));
    }

    [Fact]
    public void MovingObjects_DrawWhereTheyAreNow()
    {
        RenderableHandle handle = Add(Colored(1, 1, 1), new Double3(-2, 0, 10));
        Assert.Equal(new Color32(255, 255, 255, 255), At(Frame(), 4, 8));

        _world.SetTransform(handle, Double4x4.CreateTranslation(new Double3(2, 0, 10)) * (Double4x4)Float4x4.CreateScale(2));
        Color32[] pixels = Frame();
        Assert.Equal(new Color32(0, 0, 0, 255), At(pixels, 4, 8));
        Assert.Equal(new Color32(255, 255, 255, 255), At(pixels, 12, 8));
    }

    [Fact]
    public void MirroredObjects_StillShowTheirFrontFaces()
    {
        Add(Colored(1, 1, 0), new Double3(0, 0, 10), new Float3(-2, 2, 2));
        Assert.Equal(new Color32(255, 255, 0, 255), At(Frame(), 8, 8));
    }

    [Fact]
    public void ObjectsFarFromTheOrigin_DrawWhereTheCameraSeesThem()
    {
        // Floats at ten million are a whole unit apart, so only the low parts place these a quarter and half unit off.
        Double3 camera = new(10_000_000.25, 0, -3_000_000);
        Add(Colored(1, 0, 0), camera + new Double3(-2.5, 0, 10));
        Add(Colored(0, 1, 0), camera + new Double3(2.5, 0, 10));

        Color32[] pixels = Frame(camera);
        Assert.Equal(new Color32(255, 0, 0, 255), At(pixels, 1, 8));
        Assert.Equal(new Color32(0, 0, 0, 255), At(pixels, 5, 8));
        Assert.Equal(new Color32(0, 0, 0, 255), At(pixels, 10, 8));
        Assert.Equal(new Color32(0, 255, 0, 255), At(pixels, 14, 8));
    }

    [Fact]
    public void ShadersThatFailToLoad_AreReportedOnce_AndDrawTheGreyFallback()
    {
        Shader broken = _renderer.LoadShader(ShaderParser.Parse(Source.Replace("uniform vec4 _Color;", "uniform vec4 _Color; uniform float _Undeclared;").Replace("outColor = _Color;", "outColor = _Color * _Undeclared;")));
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) * (Double4x4)Float4x4.CreateScale(2) }, [_renderer.CreateMaterial(broken)]);

        Assert.Equal(new Color32(188, 188, 188, 255), At(Frame(), 8, 8));
        Frame();
        Assert.Single(_errors, e => e.Message.Contains("_Undeclared"));
    }

    private Material Glass(float r, float g, float b)
    {
        Material material = _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(GlassSource)));
        material.SetColor("_Color", new Float4(r, g, b, 0.5f));
        return material;
    }

    [Fact]
    public void TransparentObjects_BlendBackToFront_OverOpaques_AndBehindNearerOpaques()
    {
        Add(Colored(0, 0, 1), new Double3(0, 0, 30), new Float3(8, 8, 1));
        Add(Glass(0, 1, 0), new Double3(0, 0, 10));
        Add(Glass(1, 0, 0), new Double3(0, 0, 20));
        Add(Colored(1, 1, 1), new Double3(-3, 0, 5), new Float3(2, 8, 1));

        Color32[] pixels = Frame();
        Color32 blended = At(pixels, 8, 8);
        Assert.InRange(blended.R, 135, 139);
        Assert.InRange(blended.G, 186, 190);
        Assert.InRange(blended.B, 135, 139);
        Assert.Equal(new Color32(255, 255, 255, 255), At(pixels, 1, 8));
    }

    [Fact]
    public void TheRenderThread_DrawsTheSameFrames()
    {
        void Scene()
        {
            Add(Colored(1, 0, 0), new Double3(-2, 0, 10));
            Add(Colored(0, 1, 0), new Double3(2, 0, 20));
            Add(Glass(0, 0, 1), new Double3(1, 0, 5));
        }

        Scene();
        Color32[] inline = Frame();

        _renderer.Dispose();
        Start(RenderThreading.RenderThread);
        Scene();
        Color32[] pixels = Frame();
        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        while (!pixels.AsSpan().SequenceEqual(inline) && watch.Elapsed < TimeSpan.FromSeconds(30))
            pixels = Frame();

        Assert.Equal(inline, pixels);
        Assert.Empty(_errors);
    }
}
