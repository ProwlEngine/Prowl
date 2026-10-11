using System;
using System.Collections.Generic;

using BenchmarkDotNet.Attributes;

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;

using Prowl.Vector;

namespace Prowl.Graphite.Bench;

public readonly struct BenchView(uint width, uint height, int viewId) : IRenderView
{
    public uint PixelWidth { get; } = width;
    public uint PixelHeight { get; } = height;
    public int ViewId { get; } = viewId;
}

public sealed class BenchPass(string name, Action<RenderContextBuilder> setup, Action<RenderContext, CommandBuffer> render) : IPass
{
    public string Name { get; } = name;
    public void Setup(RenderContextBuilder builder) => setup(builder);
    public void Render(RenderContext context, CommandBuffer cmd) => render(context, cmd);
}

public abstract class GraphiteBenchmark
{
    public const uint DefaultSize = 64;
    public const PixelFormat ColorFormat = PixelFormat.R8_G8_B8_A8_UNorm;

    protected static readonly GraphTextureDesc ColorDesc = GraphTextureDesc.ViewSized(ColorFormat);
    protected static readonly TargetLoadStoreOps ClearOps = new(AttachmentOps.Clear(Color.Black), AttachmentOps.Discard);
    protected static readonly TargetLoadStoreOps LoadOps = new(AttachmentOps.Loaded, AttachmentOps.Discard);

    private readonly List<IDisposable> _owned = new();
    private BenchProfiler _profiler = null!;
    private RenderPipeline _pipeline = null!;
    private IRenderView[] _views = null!;

    protected GraphicsDevice Device { get; private set; } = null!;
    internal BenchMesh Cube { get; private set; } = null!;
    internal BenchMesh Quad { get; private set; } = null!;
    protected Texture WhiteTexture { get; private set; } = null!;
    protected Sampler PointSampler { get; private set; } = null!;
    protected Sampler LinearSampler { get; private set; } = null!;
    protected PropertySet ViewProperties { get; private set; } = null!;

    protected virtual int WarmupFrames => 8;

    [GlobalSetup]
    public void Setup()
    {
        _profiler = new BenchProfiler();
        Device = BenchDevice.Create(_profiler);

        Cube = Own(BenchMesh.CreateCube(Device));
        Quad = Own(BenchMesh.CreateQuad(Device));
        WhiteTexture = CreateTexture(1, 0);
        PointSampler = Own(Device.ResourceFactory.CreateSampler(SamplerDescription.Point));
        LinearSampler = Own(Device.ResourceFactory.CreateSampler(SamplerDescription.Linear));

        Float4x4 projection = Float4x4.CreatePerspectiveFov(1.0472f, 1f, 0.1f, 100f);
        Float4x4 view = Float4x4.CreateLookAt(new Float3(0, 0, 6), Float3.Zero, Float3.UnitY);
        ViewProperties = new PropertySet();
        ViewProperties.SetMatrix("MatrixViewProjection", projection * view);
        ViewProperties.SetFloat4("ViewTint", new Float4(1, 1, 1, 1));

        _views = CreateViews();
        _pipeline = new RenderPipeline(CreatePasses()) { Name = GetType().Name };

        for (int i = 0; i < WarmupFrames; i++)
            Frame();

        Device.WaitForIdle();
        _profiler.Reset();
    }

    [Benchmark]
    public void Frame()
    {
        ExecutionTask task = Device.DispatchGraph(_pipeline, _views);
        Device.WaitForExecution(task);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Device.WaitForIdle();
        BenchMetrics.Store(this, _profiler.Snapshot());

        _pipeline.Dispose();
        for (int i = _owned.Count - 1; i >= 0; i--)
            _owned[i].Dispose();
        _owned.Clear();

        Device.Dispose();
    }

    protected abstract IEnumerable<IPass> CreatePasses();

    protected virtual IRenderView[] CreateViews() => [new BenchView(DefaultSize, DefaultSize, 0)];

    protected T Own<T>(T resource) where T : IDisposable
    {
        _owned.Add(resource);
        return resource;
    }

    protected GraphicsProgram LoadProgram(string file) => Own(BenchShaderLoader.Create(Device, file));

    protected Texture CreateTexture(uint size, int seed)
    {
        Texture texture = Own(Device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(size, size, 1, 1, ColorFormat, TextureUsage.Sampled)));

        uint[] texels = new uint[size * size];
        Random random = new(seed);
        for (int i = 0; i < texels.Length; i++)
            texels[i] = seed == 0 ? 0xFFFFFFFFu : (uint)random.Next() | 0xFF000000u;

        Device.UpdateTexture<uint>(texture, texels);
        return texture;
    }

    protected PropertySet CreatePassProperties(Float4 tint, Texture? input = null)
    {
        PropertySet props = new();
        props.SetFloat4("Tint", tint);
        if (input != null)
            props.SetTexture("Input", input, LinearSampler);
        return props;
    }

    protected void DrawQuad(CommandBuffer cmd, GraphicsProgram program, PropertySet props)
    {
        cmd.SetFullViewport();
        cmd.SetShader(program);
        cmd.SetVertexSource(Quad.Source);
        cmd.SetProperties(props);
        cmd.DrawIndexed();
    }

    protected PropertySet CreateModelProperties(Float4 color)
    {
        PropertySet props = new();
        props.SetMatrix("MatrixModel", Float4x4.Identity);
        props.SetFloat4("Color", color);
        props.SetTexture("MainTexture", WhiteTexture, PointSampler);
        return props;
    }

    protected PropertySet[] CreateModels(int count)
    {
        PropertySet[] models = new PropertySet[count];
        for (int i = 0; i < count; i++)
            models[i] = CreateModelProperties(new Float4(i / (float)count, 0.5f, 1f, 1f));

        return models;
    }
}
