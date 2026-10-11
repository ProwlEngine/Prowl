using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

public enum RenderThreading
{
    /// <summary>The renderer's own thread prepares and records frames while the host builds the next one.</summary>
    RenderThread,

    /// <summary>Every step runs on the thread calling SubmitFrame, for tests, tools and single threaded hosts.</summary>
    Inline,
}

public sealed class RendererOptions
{
    public RenderThreading Threading = RenderThreading.RenderThread;

    /// <summary>Bytes of queued resource uploads applied per frame.</summary>
    public long UploadBudgetPerFrame = 64 << 20;

    /// <summary>Words of vertex data per geometry page.</summary>
    public uint PageVertexWords = GeometryPool.DefaultPageVertexWords;

    /// <summary>Indices per geometry page.</summary>
    public uint PageIndices = GeometryPool.DefaultPageIndices;

    /// <summary>
    /// Forces Inline threading and blocking variant compiles, so no fallback variant is ever drawn. For golden images
    /// and tests.
    /// </summary>
    public bool Deterministic;

    /// <summary>Format of reflection probe and sky probe cubes. B10G11R11 halves their memory, alpha is never used.</summary>
    public PixelFormat ReflectionFormat = PixelFormat.R16_G16_B16_A16_Float;

    /// <summary>GGX samples per prefiltered texel of every mip but the sharpest.</summary>
    public int PrefilterSamples = 64;

    /// <summary>Edge length of the smallest prefiltered mip, rougher reflections turn blocky below it.</summary>
    public uint SmallestProbeMip = 4;

    /// <summary>Faces of reflection probes captured per world per frame, six being one whole probe.</summary>
    public int ProbeFacesPerFrame = 6;

    /// <summary>Frames a view keeps its per view state after it was last drawn.</summary>
    public int StaleViewFrames = 8;
}

/// <summary>
/// The skies Graphite ships, loaded the first time they are asked for: the procedural sky lit by the world's sun, a two
/// colour gradient (_TopColor, _BottomColor, _Exponent) and a cubemap from six images (_CubeRight, _CubeLeft, _CubeTop,
/// _CubeBottom, _CubeFront, _CubeBack, _Tint, _Exposure).
/// </summary>
public sealed class BuiltInSkies(Renderer renderer)
{
    private Shader? _procedural, _gradient, _cubemap;

    public Shader Procedural => _procedural ??= Load("Procedural");
    public Shader Gradient => _gradient ??= Load("Gradient");
    public Shader Cubemap => _cubemap ??= Load("Cubemap");

    private Shader Load(string name) => renderer.LoadShader(ShaderParser.Parse(ShaderLibrary.Get($"{ShaderLibrary.Prefix}Skies/{name}.shader")!));
}

/// <summary>A problem the renderer reports once, raised on the host thread at SubmitFrame.</summary>
public enum RendererErrorSeverity : byte
{
    /// <summary>Something draws differently than asked: a capacity reached, a value adjusted.</summary>
    Warning,

    /// <summary>Something does not draw: a shader that failed, a frame that failed, a missing resource.</summary>
    Error,
}

/// <summary>
/// A problem the renderer reports once per <see cref="Code"/>, raised on the host thread at SubmitFrame. The code is
/// stable for filtering, the message is for people.
/// </summary>
public readonly record struct RendererError(string Code, string Message, RendererErrorSeverity Severity = RendererErrorSeverity.Error, object? Source = null);

/// <summary>One layer of a view: a camera relative view matrix (rotation only), a reversed Z projection and the camera's world position.</summary>
public struct ViewLayer
{
    public Float4x4 View;
    public Float4x4 Projection;
    public Double3 Position;
}

[InlineArray(6)]
public struct ViewLayers
{
    private ViewLayer _first;
}

public struct ViewDesc
{
    public RenderWorld World;

    /// <summary>Unique per renderer, keys per view state.</summary>
    public int ViewId;
    public ViewLayers Layers;

    /// <summary>1 for mono. Stereo and cube layers draw through multiview.</summary>
    public byte LayerCount;

    /// <summary>What the view draws into. A swapchain framebuffer presents after the frame.</summary>
    public Framebuffer Target;
    public uint CullingMask;
    public Float4 ClearColor;

    /// <summary>Whether the world's sky draws behind what the view sees.</summary>
    public bool Sky;

    /// <summary>The projection's near plane, for linear depth.</summary>
    public float Near;

    /// <summary>The logical far distance, for effects that need one with an infinite projection.</summary>
    public float FarDistance;

    public ViewDesc()
    {
        World = null!;
        Target = null!;
        LayerCount = 1;
        CullingMask = ~0u;
        Sky = true;
        Near = 0.1f;
        FarDistance = 1000;
    }
}

public struct FrameTime
{
    public double Time;
    public float DeltaTime;
    public ulong FrameIndex;
}

public struct FrameDesc
{
    public ReadOnlyMemory<ViewDesc> Views;
    public FrameTime Time;
}

/// <summary>
/// Graphite's high level renderer. The host mutates worlds, materials and globals on its thread and calls
/// <see cref="SubmitFrame"/>, which extracts what changed and hands the frame to the render thread. Resources are
/// created from any thread and become resident on a later frame.
/// </summary>
public sealed class Renderer : IDisposable
{
    private readonly RendererOptions _options;
    private readonly IShaderCompiler _compiler;
    private readonly Uploader _uploader;
    private readonly GeometryPool _geometry;
    private readonly MaterialSystem _materials;
    private readonly GlobalRegistry _globals;
    private readonly DrawPrograms _programs = new();
    private readonly FramePipeline _frame;
    private readonly Dictionary<RenderWorld, WorldFrame> _worlds = new();
    private readonly List<RenderWorld> _removedWorlds = new();

    private readonly ConcurrentQueue<RendererError> _errors = new();
    private readonly ConcurrentDictionary<string, bool> _reported = new(StringComparer.Ordinal);

    private readonly List<Material> _changedMaterials = new();
    private readonly List<GpuMesh> _changedMeshes = new();
    private readonly List<DeviceBuffer> _replaced = new();
    private readonly List<(ulong Execution, DeviceBuffer Buffer)> _retiring = new();
    private ViewDesc[] _views = [];
    private FrameTime _time;
    private int _viewCount;
    private ulong _lastDispatched;

    private readonly Thread? _thread;
    private readonly SemaphoreSlim _frameQueued = new(0, 1);
    private readonly SemaphoreSlim _frameDone = new(1, 1);
    private volatile bool _stopping;
    private bool _disposed;

    public Renderer(GraphicsDevice device, IShaderCompiler compiler, RendererOptions? options = null)
    {
        Device = device;
        _compiler = compiler;
        _options = options ?? new RendererOptions();
        GraphicsDeviceFeatures features = device.Features;
        if (!features.MultiDrawIndirect || !features.DrawIndirectBaseInstance || !features.DescriptorIndexing)
            throw new NotSupportedException("The renderer needs multi draw indirect, a first instance in indirect draws, and descriptor indexing.");
        InternalShaders.Register();

        Textures = new RendererTextures(device) { Reporter = Warn };
        _uploader = new Uploader(device, InternalShaders.Compute(device, compiler, "Internal/Scatter.glsl"));
        _geometry = new GeometryPool(device, _options.PageVertexWords, _options.PageIndices);
        _materials = new MaterialSystem(device, Textures) { Reporter = message => Report(message, message) };
        _globals = new GlobalRegistry(device, Textures) { Reporter = message => Report(message, message) };
        _programs.Reporter = message => Report(message, message);
        _frame = new FramePipeline(device, compiler, _uploader, _geometry, _materials, _globals, _programs, Textures, _options.Deterministic, Report) { StaleViewFrames = _options.StaleViewFrames };
        Skies = new BuiltInSkies(this);

        if (_options.Threading == RenderThreading.RenderThread && !_options.Deterministic)
        {
            _thread = new Thread(RenderLoop) { Name = "Graphite Render", IsBackground = true };
            _thread.Start();
        }
    }

    public GraphicsDevice Device { get; }

    public RendererTextures Textures { get; }

    /// <summary>Values and keywords every shader can read, overridden per world.</summary>
    public GlobalProperties Globals => _globals.Renderer;

    /// <summary>Raised on the host thread at SubmitFrame, once per distinct problem.</summary>
    public event Action<RendererError>? Error;

    /// <summary>A new world. Host thread.</summary>
    public RenderWorld CreateWorld()
    {
        _frameDone.Wait();
        try
        {
            RenderWorld world = new(Device, this, Textures, new ReflectionSettings(_options.ReflectionFormat, _options.PrefilterSamples, _options.SmallestProbeMip, _options.ProbeFacesPerFrame));
            (GlobalProperties globals, int region) = _globals.AddWorld();
            world.Globals = globals;
            _worlds[world] = new WorldFrame(world, new BucketSet(world.Render, _programs), region);
            return world;
        }
        finally
        {
            _frameDone.Release();
        }
    }

    internal void RemoveWorld(RenderWorld world) => _removedWorlds.Add(world);

    /// <summary>Creates a mesh. Any thread, resident on a later frame.</summary>
    public GpuMesh CreateMesh(MeshData data) => _geometry.Create(data);

    /// <summary>Creates a material. Any thread, setters are host thread only.</summary>
    public Material CreateMaterial(Shader shader) => _materials.Create(shader);

    /// <summary>The built in skies, each a shader for a material to set as <see cref="EnvironmentDesc.Sky"/>.</summary>
    public BuiltInSkies Skies { get; }

    /// <summary>A prefiltered cube from a bake, to show through <see cref="ReflectionProbeDesc.Cubemap"/>. The caller owns it. Any thread.</summary>
    public Texture CreateReflectionCubemap(BakedReflectionProbe baked)
    {
        ArgumentNullException.ThrowIfNull(baked);
        if (baked.Size <= 0 || baked.Mips <= 0 || baked.Faces.Length < 6 * baked.Mips)
            throw new ArgumentException("A baked reflection probe needs a size, mips and every face of every mip.", nameof(baked));

        Texture cube = Device.ResourceFactory.CreateTexture(TextureDescription.CreateCube((uint)baked.Size, (uint)baked.Mips, 1, PixelFormat.R16_G16_B16_A16_Float, TextureUsage.Sampled));
        cube.Name = "Baked reflection probe";
        for (int face = 0; face < 6; face++)
        {
            for (int mip = 0; mip < baked.Mips; mip++)
            {
                uint size = (uint)Math.Max(baked.Size >> mip, 1);
                byte[] data = baked.Faces[face * baked.Mips + mip];
                if (data.Length != size * size * 8)
                    throw new ArgumentException($"Face {face} mip {mip} holds {data.Length} bytes, {size * size * 8} expected.", nameof(baked));
                Device.UpdateTexture<byte>(cube, data, new TextureRegion(0, 0, 0, size, size, 1, (uint)mip, (uint)face));
            }
        }
        return cube;
    }

    /// <summary>Loads a shader through the renderer's compiler. Any thread.</summary>
    public Shader LoadShader(ShaderDefinition definition) => new(definition, _compiler);

    /// <summary>
    /// Extracts every change since the last frame and queues the frame. Waits for the render thread to finish the
    /// previous frame's CPU work first, so at most one frame is being prepared while the host builds the next. Host thread.
    /// </summary>
    public void SubmitFrame(in FrameDesc frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ReadOnlySpan<ViewDesc> views = frame.Views.Span;
        foreach (ViewDesc view in views)
        {
            if (view.LayerCount < 1 || view.LayerCount > 6)
                throw new ArgumentException($"View {view.ViewId} has {view.LayerCount} layers, a view has 1 to 6.");
            if (Math.Max(view.Target.ViewCount, 1u) != view.LayerCount)
                throw new ArgumentException($"View {view.ViewId} has {view.LayerCount} layers but its target has {Math.Max(view.Target.ViewCount, 1u)} views.");
        }

        RaiseErrors();
        _frameDone.Wait();
        bool queued = false;
        try
        {
            RaiseBakes();
            RemoveWorlds();

            if (Textures.Sweep(_lastDispatched, Device.LastCompletedExecutionId))
            {
                _materials.MarkAllRecords();
                _globals.MarkAllDirty();
                foreach (WorldFrame world in _worlds.Values)
                {
                    world.World.Lighting.RepackDecals();
                    world.World.RepackLightmaps();
                    world.World.Reflections.RepackBaked();
                }
            }

            _changedMaterials.Clear();
            _materials.Extract(_changedMaterials);
            _globals.Extract();
            foreach (WorldFrame world in _worlds.Values)
                world.World.Extract();

            if (_views.Length < views.Length)
                _views = new ViewDesc[views.Length];
            views.CopyTo(_views);
            _viewCount = views.Length;
            _time = frame.Time;

            if (_thread == null)
            {
                RunFrame();
            }
            else
            {
                _frameQueued.Release();
                queued = true;
            }
        }
        finally
        {
            if (!queued)
                _frameDone.Release();
        }
        RaiseErrors();
    }

    private int _frameCount;
    private readonly List<Double3> _cameras = new();
    private readonly List<(Action<BakedReflectionProbe> Done, BakedReflectionProbe Baked)> _bakes = new();

    // Hands finished reflection probe bakes to the host, before this frame's changes are extracted
    private void RaiseBakes()
    {
        _bakes.Clear();
        ulong completed = Device.LastCompletedExecutionId;
        foreach (WorldFrame world in _worlds.Values)
            world.World.Reflections.CollectBakes(completed, _bakes);
        foreach ((Action<BakedReflectionProbe> done, BakedReflectionProbe baked) in _bakes)
            done(baked);
    }

    private void RemoveWorlds()
    {
        foreach (RenderWorld world in _removedWorlds)
        {
            if (_worlds.Remove(world, out WorldFrame? removed))
            {
                _globals.RemoveWorld(removed.GlobalRegion);
                _frame.ForgetWorld(removed);
                removed.World.DisposeRender();
                removed.Dispose();
            }
        }
        _removedWorlds.Clear();
    }

    /// <summary>Waits until the render thread has finished the CPU side of the last submitted frame. Host thread.</summary>
    public void WaitForRenderThread()
    {
        _frameDone.Wait();
        _frameDone.Release();
        RaiseErrors();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _frameDone.Wait();
        if (_thread != null)
        {
            _stopping = true;
            _frameQueued.Release();
            _thread.Join();
        }

        Device.WaitForIdle();
        foreach (WorldFrame world in _worlds.Values)
        {
            world.World.DisposeRender();
            world.Dispose();
        }
        _worlds.Clear();
        foreach ((_, DeviceBuffer buffer) in _retiring)
            buffer.Dispose();
        _frame.Dispose();
        _globals.Dispose();
        _materials.Dispose();
        _geometry.Dispose();
        _uploader.Dispose();
        Textures.Dispose();
        _frameQueued.Dispose();
        _frameDone.Dispose();
    }

    internal void Report(string key, string message) => Report(key, message, RendererErrorSeverity.Error, null);

    internal void Warn(string key, string message) => Report(key, message, RendererErrorSeverity.Warning, null);

    internal void Report(string key, string message, RendererErrorSeverity severity, object? source)
    {
        if (_reported.TryAdd(key, true))
            _errors.Enqueue(new RendererError(key, message, severity, source));
    }

    private void RaiseErrors()
    {
        while (_errors.TryDequeue(out RendererError error))
            Error?.Invoke(error);
    }

    private void RenderLoop()
    {
        while (true)
        {
            _frameQueued.Wait();
            if (_stopping)
                return;

            try
            {
                RunFrame();
            }
            finally
            {
                _frameDone.Release();
            }
        }
    }

    // Errors never leave the render step, they are reported on the host thread.
    private void RunFrame()
    {
        try
        {
            RenderFrame();
        }
        catch (Exception e)
        {
            _uploader.Clear();
            foreach (WorldFrame world in _worlds.Values)
                world.World.Reflections.FrameFailed();
            Report($"frame.{e.GetType().Name}.{e.Message}", $"The frame failed: {e}");
        }
    }

    // Render thread: apply resource changes, flush the tables, cull and compact, dispatch.
    private void RenderFrame()
    {
        ulong completed = Device.LastCompletedExecutionId;
        _geometry.Reclaim(completed);
        _materials.Reclaim(completed);
        _globals.Reclaim(completed);
        for (int i = _retiring.Count - 1; i >= 0; i--)
        {
            if (_retiring[i].Execution > completed)
                continue;
            _retiring[i].Buffer.Dispose();
            _retiring.RemoveAt(i);
        }

        _changedMeshes.Clear();
        _geometry.Apply(_uploader, _lastDispatched, _options.UploadBudgetPerFrame, _changedMeshes);
        foreach (WorldFrame world in _worlds.Values)
        {
            world.World.Render.Update(_changedMeshes, _changedMaterials);
            world.Buckets.Update(world.World.Render.DrawChanged, _changedMaterials);
        }

        _materials.Flush(_uploader, _lastDispatched);
        _globals.Flush(_uploader, _lastDispatched);
        _replaced.Clear();
        _frameCount++;
        foreach (WorldFrame world in _worlds.Values)
        {
            world.World.Render.Flush(_uploader, _replaced);
            _cameras.Clear();
            for (int v = 0; v < _viewCount; v++)
            {
                if (_views[v].World == world.World)
                    _cameras.Add(_views[v].Layers[0].Position);
            }
            world.World.Reflections.Plan(_frameCount, world.World.Lighting, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_cameras));
            world.World.Reflections.Flush(_uploader, _replaced);
            world.World.Lighting.Update();
            world.World.Lighting.Flush(_uploader, _replaced);
            world.World.Probes.Update(world.World.Render, _uploader, _replaced);
        }
        foreach (DeviceBuffer buffer in _replaced)
            _retiring.Add((_lastDispatched, buffer));

        ExecutionTask task = _frame.Run(_views.AsSpan(0, _viewCount), _worlds, _worlds.Values, _lastDispatched, _time);
        _lastDispatched = task.Id;
        foreach (WorldFrame world in _worlds.Values)
            world.World.Reflections.Dispatched(task.Id);
    }
}
