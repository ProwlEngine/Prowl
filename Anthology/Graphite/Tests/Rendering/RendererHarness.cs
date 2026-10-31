using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

/// <summary>An inline renderer drawing one world into a 16 by 16 float colour target that tests read back.</summary>
internal sealed class RendererHarness : IDisposable
{
    public const int Size = 16;

    // Orthographic and reversed Z: sees x and y from -4 to 4, and z from 0 (depth 1) to 100 (depth 0)
    public static readonly Float4x4 Projection = new(new Float4(0.25f, 0, 0, 0), new Float4(0, 0.25f, 0, 0), new Float4(0, 0, -0.01f, 0), new Float4(0, 0, 1, 1));

    public GraphicsDevice Device { get; } = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
    public RendererOptions Options { get; } = new() { Threading = RenderThreading.Inline, Compile = CompilePolicy.Sync, PageVertexWords = 1 << 16, PageIndices = 1 << 16 };
    public Renderer Renderer { get; }
    public RenderWorld World { get; }
    public Texture Target { get; }
    public Framebuffer Framebuffer { get; }
    public List<RendererError> Errors { get; } = new();

    /// <summary>Disposed after the renderer and before the device.</summary>
    public List<IDisposable> Owned { get; } = new();

    private readonly DeviceBuffer _readback;

    public RendererHarness()
    {
        Target = Device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(Size, Size, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
        Framebuffer = Device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, Target));
        _readback = Device.ResourceFactory.CreateBuffer(new BufferDescription(Size * Size * 16, BufferUsage.Staging));
        Renderer = new Renderer(Device, TestCompiler.Shared, Options);
        Renderer.Error += Errors.Add;
        World = Renderer.CreateWorld();
        World.Environment = new EnvironmentDesc { AmbientColor = default, Fog = default };
    }

    public void Dispose()
    {
        Renderer.Dispose();
        foreach (IDisposable owned in Owned)
            owned.Dispose();
        _readback.Dispose();
        Framebuffer.Dispose();
        Target.Dispose();
        Device.Dispose();
    }

    /// <summary>An unlit shader drawing _Color on both faces, times the view's exposure when exposed.</summary>
    public static string UnlitSource(bool exposed) => $$"""
        Shader "Test/Unlit"
        {
            Properties
            {
                _Color("Color", Color) = (1, 1, 1, 1)
            }

            Pass
            {
                Tags { "LightMode" = "Forward" }
                Cull Off
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
                    void main() { Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), vec3(0.0), 1.0); outColor = vec4(_Color.rgb{{(exposed ? " * Graphite_Exposure()" : "")}}, 1.0); }
                }
                ENDGLSL
            }
        }
        """;

    /// <summary>An eight unit square in the XY plane facing -Z, with normals when asked.</summary>
    public GpuMesh CreateQuad(bool normals = false)
    {
        Float3[] positions = [new(-4, -4, 0), new(4, -4, 0), new(4, 4, 0), new(-4, 4, 0)];
        List<VertexStream> streams = [new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() }];
        if (normals)
        {
            Float3[] facing = [new(0, 0, -1), new(0, 0, -1), new(0, 0, -1), new(0, 0, -1)];
            streams.Add(new VertexStream { Semantic = VertexSemantic.Normal, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(facing.AsSpan()).ToArray() });
        }
        return Renderer.CreateMesh(new MeshData { VertexCount = 4, Streams = [.. streams], Indices = new uint[] { 0, 1, 2, 0, 2, 3 } });
    }

    private readonly Dictionary<int, RenderView> _views = new();

    /// <summary>The harness's view of an id, made the first time it is asked for. A view keeps its state across frames.</summary>
    public RenderView ViewOf(int id)
    {
        if (!_views.TryGetValue(id, out RenderView? view))
            _views[id] = view = Renderer.CreateView(World);
        return view;
    }

    /// <summary>The view of an id with every setting a new view of the world into the target has, and no camera.</summary>
    public RenderView BareView(int id)
    {
        RenderView view = ViewOf(id);
        view.World = World;
        view.Name = null;
        view.Camera = default;
        view.Target = Framebuffer;
        view.CullingMask = ~0u;
        view.ClearColor = default;
        view.Sky = true;
        view.EV100 = 0;
        view.Kind = ViewKind.Game;
        view.Viewport = new Float4(0, 0, 1, 1);
        view.ClearTarget = false;
        view.Overlay = false;
        view.RenderScale = 1;
        view.Ldr = false;
        view.Features = null;
        view.Keywords = null;
        view.Order = 0;
        view.DependsOn.Clear();
        return view;
    }

    /// <summary>The view of an id into the target without sky, cleared to black, through the shared projection.</summary>
    public RenderView View(int id = 1)
    {
        RenderView view = BareView(id);
        view.Sky = false;
        view.ClearColor = new Float4(0, 0, 0, 1);
        view.Camera = new ViewCamera(new ViewLayer { View = Float4x4.Identity, Projection = Projection });
        return view;
    }

    /// <summary>Submits the frame and waits for the device to finish it.</summary>
    public void Submit(in FrameDesc frame)
    {
        Renderer.SubmitFrame(frame);
        Device.WaitForIdle();
    }

    /// <summary>Submits the frame, reads the target back and asserts the renderer raised no errors.</summary>
    public Float4[] Frame(in FrameDesc frame)
    {
        Submit(frame);
        Float4[] pixels = ReadTarget();
        Assert.Empty(Errors);
        return pixels;
    }

    public Float4[] Frame(params RenderView[] views) => Frame(new FrameDesc { Views = views });

    public Float4[] ReadTarget() => ReadLayer(Target, 0);

    /// <summary>Reads one layer of a float texture the size of the target.</summary>
    public Float4[] ReadLayer(Texture texture, uint layer)
    {
        Device.Record(cmd => cmd.CopyTextureToBuffer(texture, _readback, 0, TextureRegion.Whole(texture, 0, layer))).Wait();
        Float4[] pixels = Device.Map<Float4>(_readback).ToArray();
        Device.Unmap(_readback);
        return pixels;
    }

    public static void AssertAll(Float3 expected, Float4[] pixels, Func<int, bool>? where = null) => AssertAll(expected, pixels, 1e-3f, where);

    /// <summary>Asserts every pixel where asked holds the expected colour within the tolerance.</summary>
    public static void AssertAll(Float3 expected, Float4[] pixels, float tolerance, Func<int, bool>? where = null)
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            if (where != null && !where(i))
                continue;
            Float4 p = pixels[i];
            Assert.True(MathF.Abs(p.X - expected.X) < tolerance && MathF.Abs(p.Y - expected.Y) < tolerance && MathF.Abs(p.Z - expected.Z) < tolerance, $"pixel {i}: expected {expected}, got {p}");
        }
    }
}

/// <summary>Material resources that give every texture and sampler slot 0, for tests that never draw them.</summary>
internal sealed class NoResources : IMaterialResources
{
    public uint TextureSlot(Texture? texture, MaterialPropertyType type, string defaultName) => 0;

    public uint SamplerSlot(SamplerDescription? sampler) => 0;
}
