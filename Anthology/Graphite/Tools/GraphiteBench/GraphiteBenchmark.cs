using System;
using System.Collections.Generic;
using System.Linq;

using BenchmarkDotNet.Attributes;

using Prowl.Graphite;

using Prowl.Vector;

namespace Prowl.Graphite.Bench;

public readonly record struct BenchTexture(PixelFormat Format, float Scale = 1f, bool Depth = false);

public sealed class BenchBuilder(FrameGraph graph)
{
    private readonly Dictionary<RenderResourceID, TextureHandle> _textures = new();
    private readonly Dictionary<RenderResourceID, BufferHandle> _buffers = new();
    private PassBuilder _pass = null!;

    internal int View { get; private set; }
    internal (uint Width, uint Height) Extent { get; private set; }

    internal void BeginView(int view, (uint Width, uint Height) extent)
    {
        View = view;
        Extent = extent;
        _textures.Clear();
        _buffers.Clear();
    }

    internal void BeginPass(PassBuilder pass) => _pass = pass;

    public TextureHandle DeclareOutputTexture(RenderResourceID id, BenchTexture desc, int history = 0, TargetLoadStoreOps? ops = null)
    {
        if (!_textures.TryGetValue(id, out TextureHandle handle))
        {
            GraphTextureDesc sized = GraphTextureDesc.Sized(Math.Max(1, (int)(Extent.Width * desc.Scale)), Math.Max(1, (int)(Extent.Height * desc.Scale)), desc.Format, desc.Depth);
            handle = graph.Create(id, sized, history > 0 ? Lifetime.History(history) : Lifetime.Transient, View);
        }

        return _textures[id] = _pass.Write(handle, firstWrite: ops);
    }

    public TextureHandle DeclareInputTexture(RenderResourceID id) => _pass.Read(_textures[id]);

    public BufferHandle DeclareOutputBuffer(RenderResourceID id, GraphBufferDesc desc)
    {
        if (!_buffers.TryGetValue(id, out BufferHandle handle))
            handle = graph.Create(id, desc, scope: View);

        return _buffers[id] = _pass.Write(handle);
    }

    public BufferHandle DeclareInputBuffer(RenderResourceID id, BufferAccess access) => _pass.Read(_buffers[id], access);
}

public sealed class BenchPass(string name, Action<BenchBuilder> setup, Action<PassContext, CommandBuffer> render)
{
    public string Name { get; } = name;

    internal void Add(FrameGraph graph, BenchBuilder builder)
        => graph.AddPass(Name, pass =>
        {
            builder.BeginPass(pass);
            setup(builder);
        }, context => render(context, context.Command));
}

public abstract class GraphiteBenchmark
{
    public const uint DefaultSize = 64;
    public const PixelFormat ColorFormat = PixelFormat.R8_G8_B8_A8_UNorm;

    protected static readonly BenchTexture ColorDesc = new(ColorFormat);
    protected static readonly TargetLoadStoreOps ClearOps = new(AttachmentOps.Clear(Color.Black), AttachmentOps.Discard);
    protected static readonly TargetLoadStoreOps LoadOps = new(AttachmentOps.Loaded, AttachmentOps.Discard);

    private readonly List<IDisposable> _owned = new();
    private BenchProfiler _profiler = null!;
    private FrameGraph _graph = null!;
    private BenchBuilder _builder = null!;
    private BenchPass[] _passes = null!;
    private (uint Width, uint Height)[] _views = null!;
    private string[] _groupNames = null!;

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

        _graph = new FrameGraph(Device, GetType().Name);
        _builder = new BenchBuilder(_graph);
        _passes = [.. CreatePasses()];
        _views = CreateViews();
        _groupNames = Enumerable.Range(0, _views.Length).Select(i => "View " + i).ToArray();

        for (int i = 0; i < WarmupFrames; i++)
            Frame();

        Device.WaitForIdle();
        _profiler.Reset();
    }

    [Benchmark]
    public void Frame()
    {
        for (int view = 0; view < _views.Length; view++)
        {
            _builder.BeginView(view, _views[view]);
            using (_graph.Group(_groupNames[view]))
            {
                foreach (BenchPass pass in _passes)
                    pass.Add(_graph, _builder);
            }
        }

        Device.WaitForExecution(_graph.Execute());
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Device.WaitForIdle();
        BenchMetrics.Store(this, _profiler.Snapshot());

        _graph.Dispose();
        for (int i = _owned.Count - 1; i >= 0; i--)
            _owned[i].Dispose();
        _owned.Clear();

        Device.Dispose();
    }

    protected abstract IEnumerable<BenchPass> CreatePasses();

    protected virtual (uint Width, uint Height)[] CreateViews() => [(DefaultSize, DefaultSize)];

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
