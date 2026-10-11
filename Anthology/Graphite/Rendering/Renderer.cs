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
}

/// <summary>A problem the renderer reports once, raised on the host thread at SubmitFrame.</summary>
public readonly record struct RendererError(string Message);

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

        Textures = new RendererTextures(device);
        _uploader = new Uploader(device, InternalShaders.Compute(device, compiler, "Internal/Scatter.glsl"));
        _geometry = new GeometryPool(device, _options.PageVertexWords, _options.PageIndices);
        _materials = new MaterialSystem(device, Textures) { Reporter = message => Report(message, message) };
        _globals = new GlobalRegistry(device, Textures) { Reporter = message => Report(message, message) };
        _programs.Reporter = message => Report(message, message);
        _frame = new FramePipeline(device, compiler, _uploader, _geometry, _materials, _globals, _programs, Textures, _options.Deterministic, Report);

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
            RenderWorld world = new(Device, this, Textures);
            (GlobalProperties globals, int region) = _globals.AddWorld();
            world.Globals = globals;
            TableRing frameTable = new(Device, ProgramCache.FrameLayout, "Frame table");
            Textures.Track(frameTable);
            _worlds[world] = new WorldFrame(world, new BucketSet(world.Render, _programs), frameTable, region);
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
            if (view.LayerCount != 1)
                throw new NotSupportedException("Views with several layers are not supported yet.");
        }

        RaiseErrors();
        _frameDone.Wait();
        bool queued = false;
        try
        {
            RemoveWorlds();

            if (Textures.Sweep(_lastDispatched, Device.LastCompletedExecutionId))
            {
                _materials.MarkAllRecords();
                _globals.MarkAllDirty();
                foreach (WorldFrame world in _worlds.Values)
                    world.World.Lighting.RepackDecals();
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

    private void RemoveWorlds()
    {
        foreach (RenderWorld world in _removedWorlds)
        {
            if (_worlds.Remove(world, out WorldFrame? removed))
            {
                _globals.RemoveWorld(removed.GlobalRegion);
                Textures.Untrack(removed.FrameTable);
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

    internal void Report(string key, string message)
    {
        if (_reported.TryAdd(key, true))
            _errors.Enqueue(new RendererError(message));
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
        foreach (WorldFrame world in _worlds.Values)
        {
            world.World.Render.Flush(_uploader, _replaced);
            world.World.Lighting.Update();
            world.World.Lighting.Flush(_uploader, _replaced);
        }
        foreach (DeviceBuffer buffer in _replaced)
            _retiring.Add((_lastDispatched, buffer));

        ExecutionTask task = _frame.Run(_views.AsSpan(0, _viewCount), _worlds, _worlds.Values, _lastDispatched, _time);
        _lastDispatched = task.Id;
    }
}
