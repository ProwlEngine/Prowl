using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

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
    private readonly GlslShaderCompiler _compiler = TestCompiler.Shared;
    private readonly Texture _target;
    private readonly Framebuffer _framebuffer;
    private readonly DeviceBuffer _readback;
    private Renderer _renderer = null!;
    private RenderWorld _world = null!;
    private Shader _shader = null!;
    private GpuMesh _quad = null!;
    private readonly List<RendererError> _errors = new();

    public RendererTests()
    {
        _target = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(Size, Size, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget | TextureUsage.Sampled));
        _framebuffer = _device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, _target));
        _readback = _device.ResourceFactory.CreateBuffer(new BufferDescription(Size * Size * 4, BufferUsage.Staging));
        Start(RenderThreading.Inline);
    }

    private void Start(RenderThreading threading)
    {
        _renderer = new Renderer(_device, _compiler, new RendererOptions { Threading = threading, Compile = threading == RenderThreading.Inline ? CompilePolicy.Sync : CompilePolicy.Async, PageVertexWords = 1 << 16, PageIndices = 1 << 16 });
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

    private RenderView? _view;

    // The test's one view into the target, cleared to black
    private RenderView View()
    {
        _view ??= _renderer.CreateView(_world);
        _view.Target = _framebuffer;
        _view.ClearColor = new Float4(0, 0, 0, 1);
        return _view;
    }

    private Color32[] Frame(Double3 camera = default)
    {
        RenderView view = View();
        view.Camera = new ViewCamera(new ViewLayer { View = Float4x4.Identity, Projection = RendererHarness.Projection, Position = camera });
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

    private Material Glass(float r, float g, float b)
    {
        Material material = _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(GlassSource)));
        material.SetColor("_Color", new Float4(r, g, b, 0.5f));
        return material;
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

    // The render thread runs until the renderer is disposed, so whatever it copied from the thread that created
    // the renderer would stay alive that long.
    [Fact]
    public void TheRenderThreadDoesNotKeepTheAsyncLocalsOfTheThreadThatCreatedTheRenderer()
    {
        _renderer.Dispose();
        AsyncLocal<object?> local = new();
        WeakReference held = HoldInAsyncLocal(local);
        Start(RenderThreading.RenderThread);
        Frame();
        local.Value = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(held.IsAlive, "the render thread kept an object its creator held in an AsyncLocal");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference HoldInAsyncLocal(AsyncLocal<object?> local)
    {
        object value = new byte[1 << 20];
        local.Value = value;
        return new WeakReference(value);
    }
}
