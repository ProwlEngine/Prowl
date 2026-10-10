using System.Runtime.InteropServices;

using DebuggerViewer.Rendering;

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;
using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace DebuggerViewer.Scene;

internal readonly struct OffscreenView(uint width, uint height) : IRenderView
{
    public uint PixelWidth => width;

    public uint PixelHeight => height;

    public int ViewId => 1;

    public string Name => "Offscreen";
}

internal sealed record SceneCube(string Name, Float3 Position, Float3 Axis, float Speed, float Phase, Float3 Scale, Float4 Color);

/// <summary>Spinning lit cubes rendered to graph-only targets. Nothing presents them, so the debugger is the only way to see them.</summary>
internal sealed class OffscreenScene : IDisposable
{
    public const uint Width = 960;
    public const uint Height = 540;
    public const float Exposure = 0.9f;
    public const float FieldOfView = 1.0472f;
    public const int ConstantBytes = 192;
    public static readonly Float3 LightDirection = Float3.Normalize(new Float3(-0.4f, -1f, -0.6f));
    public static readonly Float4 LightColor = new(1.4f, 1.3f, 1.15f, 1f);
    public static readonly Float4 Ambient = new(0.08f, 0.09f, 0.13f, 1f);

    private readonly GraphicsDevice _device;
    private readonly DeviceBuffer _vertices;
    private readonly DeviceBuffer _indices;
    private readonly uint _indexCount;
    private readonly GraphicsProgram _lit;
    private readonly ShaderPass _tonemap;
    private readonly Sampler _sampler;
    private readonly OffscreenView[] _views = [new(Width, Height)];

    public OffscreenScene(GraphicsDevice device)
    {
        _device = device;
        (CubeVertex[] vertices, ushort[] indices) = BuildCube();
        _indexCount = (uint)indices.Length;

        _vertices = device.ResourceFactory.CreateBuffer(new BufferDescription((uint)(vertices.Length * CubeVertex.Size), BufferUsage.VertexBuffer));
        _vertices.Name = "Cube_VB";
        device.UpdateBuffer(_vertices, 0, (ReadOnlySpan<CubeVertex>)vertices);

        _indices = device.ResourceFactory.CreateBuffer(new BufferDescription((uint)(indices.Length * sizeof(ushort)), BufferUsage.IndexBuffer));
        _indices.Name = "Cube_IB";
        device.UpdateBuffer(_indices, 0, (ReadOnlySpan<ushort>)indices);

        ShaderPass[] passes = ShaderFiles.Load(device, "Lit.shader", "Tonemap.shader");
        _lit = ShaderFiles.Interleaved(device, passes[0], "Sample/Lit/Forward",
            new VertexLayoutDescription(0, CubeVertex.Size,
                new VertexElementDescription("POSITION0", VertexElementFormat.Float3, 0),
                new VertexElementDescription("NORMAL0", VertexElementFormat.Float3, 12),
                new VertexElementDescription("TEXCOORD0", VertexElementFormat.Float2, 24)),
            BlendStateDescription.SingleDisabled,
            DepthStencilStateDescription.DepthOnlyLessEqual,
            RasterizerStateDescription.Default);
        _tonemap = passes[1];
        _sampler = device.ResourceFactory.CreateSampler(SamplerDescription.Linear);

        Cubes = CreateCubes();
        Pipeline = new RenderPipeline([new ForwardPass(this), new PostPass(this)]);
    }

    public IReadOnlyList<SceneCube> Cubes { get; }

    public RenderPipeline Pipeline { get; }

    public float Time { get; private set; }

    public void Advance(float dt) => Time += dt;

    public ExecutionTask Dispatch(params IProfiler[] profilers) => _device.DispatchGraph(Pipeline, _views, profilers);

    public Float4x4 Model(SceneCube cube)
        => Float4x4.CreateTRS(cube.Position, Quaternion.AngleAxis(cube.Phase + Time * cube.Speed, cube.Axis), cube.Scale);

    public static Float3 Eye(float time) => new(MathF.Sin(time * 0.2f) * 9f, 4.5f, MathF.Cos(time * 0.2f) * 9f);

    public Float4x4 ViewProjection()
    {
        Float4x4 projection = Float4x4.CreatePerspectiveFov(FieldOfView, (float)Width / Height, 0.5f, 100f);
        return projection * Float4x4.CreateLookAt(Eye(Time), Float3.Zero, Float3.UnitY);
    }

    public void Dispose()
    {
        Pipeline.Dispose();
        _lit.Dispose();
        _sampler.Dispose();
        _vertices.Dispose();
        _indices.Dispose();
    }

    private static List<SceneCube> CreateCubes()
    {
        Random rng = new(7);
        List<SceneCube> cubes = new();
        for (int i = 0; i < 12; i++)
        {
            float angle = i / 12f * MathF.PI * 2f;
            float radius = i % 2 == 0 ? 3.2f : 5f;
            Float3 axis = Float3.Normalize(new Float3((float)rng.NextDouble() - 0.5f, 1f, (float)rng.NextDouble() - 0.5f));
            Float4 color = new(0.35f + (float)rng.NextDouble() * 0.65f, 0.35f + (float)rng.NextDouble() * 0.65f, 0.35f + (float)rng.NextDouble() * 0.65f, 1f);
            float scale = 0.8f + (float)rng.NextDouble() * 0.6f;
            cubes.Add(new SceneCube($"Cube {i}", new Float3(MathF.Cos(angle) * radius, (float)rng.NextDouble() * 1.5f - 0.75f, MathF.Sin(angle) * radius),
                axis, 0.4f + (float)rng.NextDouble() * 1.2f, (float)rng.NextDouble() * MathF.PI * 2f, new Float3(scale, scale, scale), color));
        }

        cubes.Add(new SceneCube("Ground", new Float3(0f, -2.2f, 0f), Float3.UnitY, 0f, 0f, new Float3(14f, 0.2f, 14f), new Float4(0.3f, 0.32f, 0.36f, 1f)));
        return cubes;
    }

    private static (CubeVertex[] Vertices, ushort[] Indices) BuildCube()
    {
        Float3[] normals = [Float3.UnitX, -Float3.UnitX, Float3.UnitY, -Float3.UnitY, Float3.UnitZ, -Float3.UnitZ];
        List<CubeVertex> vertices = new();
        List<ushort> indices = new();
        foreach (Float3 n in normals)
        {
            Float3 up = MathF.Abs((float)n.Y) > 0.5f ? Float3.UnitZ : Float3.UnitY;
            Float3 right = Float3.Cross(up, n);
            ushort start = (ushort)vertices.Count;
            for (int corner = 0; corner < 4; corner++)
            {
                float u = corner is 1 or 2 ? 1f : 0f, v = corner >= 2 ? 1f : 0f;
                Float3 p = n * 0.5f + right * (u - 0.5f) + up * (v - 0.5f);
                vertices.Add(new CubeVertex((float)p.X, (float)p.Y, (float)p.Z, (float)n.X, (float)n.Y, (float)n.Z, u, v));
            }

            indices.AddRange([start, (ushort)(start + 2), (ushort)(start + 1), start, (ushort)(start + 3), (ushort)(start + 2)]);
        }

        return (vertices.ToArray(), indices.ToArray());
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct CubeVertex(float PX, float PY, float PZ, float NX, float NY, float NZ, float U, float V)
    {
        public const uint Size = 32;
    }

    private sealed class ForwardPass(OffscreenScene scene) : RasterPass
    {
        private readonly PropertySet _properties = new();

        public override string Name => "Forward";

        public override void Setup(RenderContextBuilder builder)
            => SetTarget(builder, "SceneColor", GraphTextureDesc.ViewSized(PixelFormat.R16_G16_B16_A16_Float, depth: true), ops: TargetLoadStoreOps.Clear(new Color(0.02f, 0.025f, 0.04f, 1f)));

        public override void Render(RenderContext context, CommandBuffer cmd)
        {
            Float4x4 viewProjection = scene.ViewProjection();
            VertexSource source = new VertexSource()
                .SetBuffer("POSITION0", scene._vertices)
                .SetIndexBuffer(scene._indices, IndexFormat.UInt16, scene._indexCount);

            _properties.SetFloat4("LightDirection", new Float4(LightDirection, 0f));
            _properties.SetFloat4("LightColor", LightColor);
            _properties.SetFloat4("Ambient", Ambient);

            cmd.SetShader(scene._lit);
            cmd.SetVertexSource(source);
            foreach (SceneCube cube in scene.Cubes)
            {
                Float4x4 model = scene.Model(cube);

                cmd.PushDebugGroup(cube.Name);
                _properties.SetMatrix("MatrixMVP", viewProjection * model);
                _properties.SetMatrix("MatrixModel", model);
                _properties.SetFloat4("Color", cube.Color);
                cmd.SetProperties(_properties);
                cmd.DrawIndexed();
                cmd.PopDebugGroup();
            }
        }
    }

    private sealed class PostPass(OffscreenScene scene) : RasterPass
    {
        private readonly PropertySet _properties = new();
        private TextureHandle _source;

        public override string Name => "Tonemap";

        public override void Setup(RenderContextBuilder builder)
        {
            _source = builder.DeclareInputTexture("SceneColor");
            SetTarget(builder, "Final", GraphTextureDesc.ViewSized(PixelFormat.R8_G8_B8_A8_UNorm));
        }

        public override void Render(RenderContext context, CommandBuffer cmd)
        {
            _properties.SetTexture("Source", context.GetRenderTexture(_source).ColorTextures[0], scene._sampler);
            _properties.SetFloat("Exposure", Exposure);

            cmd.SetShader(scene._tonemap);
            cmd.SetVertexSource(VertexSource.None);
            cmd.SetProperties(_properties);
            cmd.Draw(3);
        }
    }
}
