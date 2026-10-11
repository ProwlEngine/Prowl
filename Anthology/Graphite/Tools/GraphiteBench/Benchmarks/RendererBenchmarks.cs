using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using BenchmarkDotNet.Attributes;

using Prowl.Graphite.Rendering;
using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;
using Prowl.Vector;

namespace Prowl.Graphite.Bench;

/// <summary>
/// A frame of the high level renderer: a grid of cubes in front of the camera with a number of materials of one
/// shader, a share of them moving every frame. Measures SubmitFrame in Inline mode, which includes extraction,
/// culling, compaction, uploads and recording, until the GPU finishes the frame.
/// </summary>
public class RendererBenchmarks
{
    private const string Source = """
        Shader "Bench/Unlit"
        {
            Properties { _Color("Tint", Color) = (1, 1, 1, 1) }

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
                Fragment { void main() { } }
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
                    layout(location = 0) out vec3 vNormal;
                    void main()
                    {
                        gl_Position = Graphite_ObjectToClip(vertexPosition);
                        vNormal = Graphite_ObjectToWorldNormal(vertexNormal);
                    }
                }
                Fragment
                {
                    #include "Graphite/Core.glsl"
                    uniform vec4 _Color;
                    layout(location = 0) in vec3 vNormal;
                    layout(location = 0) out vec4 outColor;
                    void main() { outColor = _Color * (0.5 + 0.5 * normalize(vNormal).y); }
                }
                ENDGLSL
            }
        }
        """;

    private const uint Size = 512;

    [Params(1_000, 10_000, 50_000)]
    public int Objects { get; set; }

    [Params(1, 256)]
    public int Materials { get; set; }

    [Params(0, 10)]
    public int MovingPercent { get; set; }

    private BenchProfiler _profiler = null!;
    private GraphicsDevice _device = null!;
    private GlslShaderCompiler _compiler = null!;
    private Renderer _renderer = null!;
    private RenderWorld _world = null!;
    private Texture _target = null!;
    private Framebuffer _framebuffer = null!;
    private RenderableHandle[] _handles = [];
    private Double3[] _positions = [];
    private ViewDesc[] _views = [];
    private int _frame;

    [GlobalSetup]
    public void Setup()
    {
        _profiler = new BenchProfiler();
        _device = BenchDevice.Create(_profiler);
        _compiler = new GlslShaderCompiler();
        _renderer = new Renderer(_device, _compiler, new RendererOptions { Threading = RenderThreading.Inline, Deterministic = true });
        _world = _renderer.CreateWorld();
        _target = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(Size, Size, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget | TextureUsage.Sampled));
        _framebuffer = _device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, _target));

        Shader shader = _renderer.LoadShader(ShaderParser.Parse(Source));
        Material[] materials = Enumerable.Range(0, Materials).Select(i =>
        {
            Material material = _renderer.CreateMaterial(shader);
            material.SetColor("_Color", new Float4((i * 37 % 256) / 255f, (i * 91 % 256) / 255f, (i * 53 % 256) / 255f, 1));
            return material;
        }).ToArray();
        GpuMesh cube = _renderer.CreateMesh(Cube());

        int side = (int)MathF.Ceiling(MathF.Sqrt(Objects));
        _handles = new RenderableHandle[Objects];
        _positions = new Double3[Objects];
        for (int i = 0; i < Objects; i++)
        {
            _positions[i] = new Double3((i % side - side * 0.5) * 1.5, (i / side - side * 0.5) * 1.5, side * 1.2);
            _handles[i] = _world.Add(new RenderableDesc { Mesh = cube, Transform = Double4x4.CreateTranslation(_positions[i]) }, [materials[i % Materials]]);
        }

        float f = 1.5f;
        ViewDesc view = new() { World = _world, ViewId = 0, Target = _framebuffer, ClearColor = new Float4(0.1f, 0.1f, 0.1f, 1), Near = 0.1f };
        view.Layers[0] = new ViewLayer
        {
            View = Float4x4.Identity,
            Projection = new Float4x4(new Float4(f, 0, 0, 0), new Float4(0, f, 0, 0), new Float4(0, 0, 0, 1), new Float4(0, 0, 0.1f, 0)),
        };
        _views = [view];

        for (int i = 0; i < 8; i++)
            Frame();
        _device.WaitForIdle();
        _profiler.Reset();
    }

    [Benchmark]
    public void Frame()
    {
        int moving = Objects * MovingPercent / 100;
        double offset = Math.Sin(++_frame * 0.1) * 0.25;
        for (int i = 0; i < moving; i++)
            _world.SetTransform(_handles[i], Double4x4.CreateTranslation(_positions[i] + new Double3(0, offset, 0)));

        _renderer.SubmitFrame(new FrameDesc { Views = _views });
        _device.WaitForIdle();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _device.WaitForIdle();
        BenchMetrics.Store(this, _profiler.Snapshot());
        _renderer.Dispose();
        _framebuffer.Dispose();
        _target.Dispose();
        _compiler.Dispose();
        _device.Dispose();
    }

    private static MeshData Cube()
    {
        List<Float3> positions = new(), normals = new();
        List<uint> indices = new();
        Float3[] axes = [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)];
        foreach (Float3 n in axes)
        {
            Float3 up = MathF.Abs(n.Y) > 0.5f ? new Float3(0, 0, 1) : new Float3(0, 1, 0);
            Float3 right = Float3.Cross(up, n);
            uint start = (uint)positions.Count;
            foreach ((float u, float v) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
            {
                positions.Add((n + right * u + up * v) * 0.5f);
                normals.Add(n);
            }
            indices.AddRange([start, start + 1, start + 2, start, start + 2, start + 3]);
        }

        return new MeshData
        {
            VertexCount = (uint)positions.Count,
            Streams =
            [
                new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(positions)).ToArray() },
                new VertexStream { Semantic = VertexSemantic.Normal, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(normals)).ToArray() },
            ],
            Indices = indices.ToArray(),
        };
    }
}
