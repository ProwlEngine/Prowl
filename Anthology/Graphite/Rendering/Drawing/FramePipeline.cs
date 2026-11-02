using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// The frame: the passes uploading every change, then those of every world and view, built into one graph every frame
/// and run as one execution.
/// </summary>
internal readonly record struct FrameEntry(FrameFeature Feature, WorldFrame? World);

internal sealed partial class FramePipeline : IDisposable
{
    private static readonly PropertyID s_measured = "Graphite_Measured", s_exposureState = "Graphite_ExposureState",
        s_exposureViews = "Graphite_ExposureViews", s_chunks = "Graphite_Chunks", s_transforms = "Graphite_Transforms",
        s_previousTransforms = "Graphite_PreviousTransforms", s_objects = "Graphite_Objects", s_materialSlots = "Graphite_MaterialSlots",
        s_worldLighting = "Graphite_WorldLighting", s_objectProbes = "Graphite_ObjectProbes", s_skyAmbientSH = "Graphite_SkyAmbientSH",
        s_directionals = "Graphite_Directionals";

    [StructLayout(LayoutKind.Sequential)]
    public struct Push
    {
        public uint DrawRecordBase;
        public uint InstanceBase;
        public uint ViewBase;
        public uint Flags;
    }

    private struct ExposurePush
    {
        public float Host;
        public uint Flags;
        public uint Layers;
        private uint _pad;
    }

    private static readonly List<RenderResourceID> s_bufferIds = new();

    private readonly GraphicsDevice _device;
    private readonly IShaderCompiler? _compiler;
    private readonly UploadPass _upload;
    private readonly ProbePass _probes;
    private readonly DeformPass _deform;
    private readonly Dictionary<int, CameraFrame> _cameras = new();
    private readonly Dictionary<(int Page, PrimitiveTopology Topology), VertexSource> _vertexSources = new();
    private readonly List<PlannedInstance> _planned = new();
    private readonly RenderStore _store;
    private readonly FrameGraph _graph;

    internal RenderStore Store => _store;

    internal DeformPass Deform => _deform;
    internal ProbePass Probes => _probes;

    /// <summary>The frame features that ran in the last frame, for tests.</summary>
    internal IEnumerable<FrameFeature> ActiveFrameFeatures => _frameFeatures.Select(entry => entry.Feature);

    /// <summary>The frame features of the renderer's own set, as of this frame. Render thread.</summary>
    internal FrameFeature[] RendererFeatures = [];

    /// <summary>The graph every frame is built into.</summary>
    internal FrameGraph Graph => _graph;

    /// <summary>The renderer's own view features, which every view runs unless a feature of their group replaces one.</summary>
    internal RenderFeature[] BuiltIns { get; }
    private readonly List<CameraFrame> _captures = new();
    private readonly List<IDisposable> _retired = new();
    private readonly ProviderCountsPass _counts;
    private readonly FrameExports _exports = new();
    private readonly Dictionary<FrameFeature, FrameFeaturePass> _frameFeaturePasses = new();
    private readonly List<FrameEntry> _frameFeatures = new();
    private FrameTime _currentTime;
    private readonly ReadbackStaging _readback;
    private readonly List<int> _sortedViews = new();
    private bool[] _placed = [];
    private readonly HashSet<RenderFeature> _prepared = new();
    private readonly List<int> _viewOrder = new();
    private readonly List<WorldFrame> _frameWorlds = new();
    private int _frame;

    private readonly RendererTextures _textures;
    private readonly bool _syncCompiles;

    private readonly Uploader _uploader;

    public FramePipeline(GraphicsDevice device, RenderStore store, IShaderCompiler? compiler, Uploader uploader, GeometryPool geometry, MaterialSystem materials, GlobalRegistry globals, DrawPrograms programs, RendererResources resources, Action<RendererError> report, CompilePolicy policy = CompilePolicy.Async)
    {
        Report = report;
        _device = device;
        _compiler = compiler;
        Resources = resources;
        _textures = resources.Textures;
        _store = store;
        _uploader = uploader;
        _syncCompiles = policy == CompilePolicy.Sync;
        Geometry = geometry;
        Materials = materials;
        Globals = globals;
        DrawPrograms = programs;
        Programs = new ProgramCache(device, compiler, globals, programs, report, policy, _retired.Add);
        programs.Freed += Programs.Drop;
        _graph = new FrameGraph(device, "Graphite");
        BuiltIns = FeatureSelection.BuiltIns(resources);
        Buffers = new FrameBuffers();
        _counts = new ProviderCountsPass(this);
        _upload = new UploadPass(uploader, store);
        _probes = new ProbePass(InternalShaders.Compute(device, compiler, "Internal/Probes.glsl", policy));
        _deform = new DeformPass(InternalShaders.Compute(device, compiler, "Internal/Deform.glsl", policy), geometry);
        _readback = new ReadbackStaging(device, report);
    }

    public GraphicsDevice Device => _device;
    public GeometryPool Geometry { get; }
    public MaterialSystem Materials { get; }
    public GlobalRegistry Globals { get; }
    public DrawPrograms DrawPrograms { get; }
    public ProgramCache Programs { get; }

    /// <summary>The GPU resources shared by every frame, view and effect.</summary>
    public RendererResources Resources { get; }

    /// <summary>Resources the frame stopped using, disposed once it is dispatched.</summary>
    public List<IDisposable> Retired => _retired;

    /// <summary>What the capture fill passes bind, one set each so nothing one binds reaches the other.</summary>
    internal PropertySet PrefilterProperties { get; } = new();
    internal PropertySet AmbientProperties { get; } = new();

    /// <summary>Reports a problem once per key.</summary>
    public Action<RendererError> Report { get; }
    private readonly PropertySet _exposureProperties = new();

    /// <summary>Frames a capture view's scene copies and an empty shadow page are kept after their last use.</summary>
    private const int IdleFrames = 8;

    /// <summary>Every persistent buffer the frame writes and its cameras read, in a stable order.</summary>
    public FrameBuffers Buffers { get; }

    /// <summary>Declares what a view's draws bind: the tables and buffers of its world and itself, the shadow pages and cascades, the host's buffers and the cubes it samples.</summary>
    internal void DeclareDrawReads(ViewGraph view, PassBuilder builder, CameraFrame camera, bool screenVisibility, bool screenIrradiance)
    {
        FrameGraph graph = view.Graph;
        RequireOutputs(view, camera);
        Buffers.ReadCamera(graph, builder, camera);
        DeclareShadowReads(graph, builder);
        foreach (DeviceBuffer buffer in camera.HostBuffers)
            builder.Read(graph.Import(buffer), BufferAccess.AllReads);
        if (screenVisibility && camera.ScreenVisibility != null)
            builder.Read(graph.Import(camera.ScreenVisibility), TextureState.Sampled, TextureState.Sampled);
        if (screenIrradiance && camera.ScreenIrradiance != null)
            builder.Read(graph.Import(camera.ScreenIrradiance), TextureState.Sampled, TextureState.Sampled);
    }

    /// <summary>
    /// Declares every shadow page, cascade and reflection cube of the frame as sampled, bar the ones the pass writes, as they
    /// all sit in the sampler table a pass that draws binds whole.
    /// </summary>
    internal void DeclareShadowReads(FrameGraph graph, PassBuilder builder, RenderTexture? written = null, List<RenderTexture>? alsoWritten = null)
    {
        foreach (WorldFrame world in _frameWorlds)
        {
            foreach (RenderTexture cube in world.World.Reflections.SampledCubes())
                builder.Read(graph.Import(cube), TextureState.Sampled);
            if (world.Shadows == null)
                continue;
            foreach (ShadowPage page in world.Shadows.AllPages())
            {
                if (page.Texture != written)
                    builder.Read(graph.Import(page.Texture), TextureState.Sampled, TextureState.Sampled);
            }
        }
        foreach (CameraFrame camera in Active())
        {
            foreach (ViewShadows.CascadeLight light in camera.Shadows.Lights)
            {
                for (int c = 0; c < light.Count; c++)
                {
                    if (light.Cascades[c].Texture?.Texture is { } cascade && cascade != written && alsoWritten?.Contains(cascade) != true)
                        builder.Read(graph.Import(cascade), TextureState.Sampled, TextureState.Sampled);
                }
            }
        }
    }

    // What the draws read must have been provided by the active features, so a replacement missing one fails with its name
    private void RequireOutputs(ViewGraph view, CameraFrame camera)
    {
        RenderWorld world = camera.World.World;
        int id = camera.View.ViewId;
        _exports.Require(RendererOutputs.DeformedVertices, world, id);
        _exports.Require(RendererOutputs.ObjectProbes, world, id);
        if (camera.World.Shadows is { } shadows && shadows.AllPages().Any())
        {
            _exports.Require(RendererOutputs.ShadowPages, world, id);
            _exports.Require(RendererOutputs.ShadowFaces, world, id);
        }
        if (camera.Cubes.Length > 0)
            _exports.Require(RendererOutputs.CaptureCubes, world, id);
        foreach (ViewShadows.CascadeLight light in camera.Shadows.Lights)
        {
            for (int c = 0; c < light.Count; c++)
            {
                if (light.Cascades[c].Texture?.Texture != null && !view.Published.ContainsKey(RendererOutputs.Cascades))
                    throw new InvalidOperationException($"The draws of view {id} read '{RendererOutputs.Cascades}', which no earlier feature of the view published.");
            }
        }
    }

    public VertexSource VertexSource(int page, PrimitiveTopology topology)
    {
        DeviceBuffer indices = Geometry.Pages[page].Indices;
        if (!_vertexSources.TryGetValue((page, topology), out VertexSource? source))
        {
            source = new VertexSource(topology).SetIndexBuffer(indices, IndexFormat.UInt32, indices.SizeInBytes / 4);
            _vertexSources[(page, topology)] = source;
        }
        return source;
    }

    private int _drawCalls, _drawCommands, _shadowDrawCalls, _shadowDrawCommands;

    /// <summary>The counts of the last frame recorded. Any thread.</summary>
    public FrameStats LastStats { get; private set; } = new(0, 0, 0, 0, 0);

    // Render thread, while the frame records
    internal void CountDraw(bool shadow, int commands)
    {
        if (shadow) { _shadowDrawCalls++; _shadowDrawCommands += commands; }
        else { _drawCalls++; _drawCommands += commands; }
    }

    /// <summary>Culls and compacts every view, then dispatches the frame. Returns the execution. Render thread.</summary>
    public ExecutionTask Run(ReadOnlySpan<ViewDesc> views, ReadOnlySpan<RenderFeature[]> viewFeatures, List<int> released, List<ReadbackRequest> readbacks, IReadOnlyDictionary<RenderWorld, WorldFrame> worlds, IEnumerable<WorldFrame> allWorlds, ulong lastDispatched, in FrameTime time, IProfiler[]? profilers = null)
    {
        _drawCalls = _drawCommands = _shadowDrawCalls = _shadowDrawCommands = 0;
        _frame++;
        ReleaseViews(released, lastDispatched);
        _currentTime = time;
        _planned.Clear();
        _frameWorlds.Clear();
        _prepared.Clear();
        foreach (WorldFrame world in allWorlds)
            PrepareFeatures(world.World.FeatureSnapshot);
        foreach (RenderFeature[] features in viewFeatures)
            PrepareFeatures(features);

        PrepareFrameFeatures(allWorlds);
        DrawPrograms.Sweep();
        foreach (WorldFrame world in allWorlds)
        {
            world.World.Render.BeginFrame();
        }

        foreach (WorldFrame world in allWorlds)
        {
            _frameWorlds.Add(world);
            world.ProbeCamerasUsed = 0;
            world.ProbeFaceCamerasUsed = 0;
            UpdateSkyProgram(world);
            WorldReflections reflections = world.World.Reflections;
            if (reflections.CaptureDue)
            {
                CaptureSky(world, time);
                float exposure = reflections.CaptureExposure;
                RenderWorld host = world.World;
                readbacks.Add(new ReadbackRequest { Name = $"{WorldName(world)} sky ambient readback", Buffer = reflections.SkyAmbient, Size = reflections.SkyAmbient.SizeInBytes, BufferDone = readback => host.ReceiveSkyAmbient(readback, exposure) });
            }
            foreach (ProbeCapture capture in reflections.Captures)
                CaptureProbe(world, capture, time);
        }

        OrderViews(views);
        foreach (int index in _viewOrder)
        {
            ViewDesc view = views[index];
            if (!worlds.TryGetValue(view.World, out WorldFrame? world))
                continue;

            if (!_cameras.TryGetValue(view.ViewId, out CameraFrame? camera))
            {
                camera = new CameraFrame(_device, this, view.ViewId);
                _cameras[view.ViewId] = camera;
                _textures.TrackView(camera.ViewTable);
            }

            Draw(camera, view, viewFeatures[index], world, time);
        }

        PrepareShadows();

        // Providers' objects, written while the views were prepared, the deforming copies' poses, and the shadows' faces,
        // after the frame's earlier writes, as they can rewrite a record those wrote
        _uploader.Fence();
        foreach (WorldFrame world in _frameWorlds)
        {
            world.World.Render.PrepareDeformation();
            world.World.Render.Flush(_uploader);
            world.Shadows!.Faces.Flush(_uploader);
        }

        ResolvePrograms();
        if (_syncCompiles && Globals.NeedsGrowth)
        {
            Globals.Extract();
            Globals.Flush(_uploader, lastDispatched);
            ResolvePrograms();
        }

        CollectBuffers(allWorlds);
        PlanCounts();

        _readback.Prepare(readbacks);
        try
        {
            BuildFrame();
        }
        catch
        {
            _graph.ResetFrame();
            throw;
        }
        ExecutionTask task = _graph.Execute(profilers ?? []);
        LastStats = Stats(views);
        _store.RetireAll(task);
        ReturnShadowCameras();
        foreach (IDisposable retired in _shadowRetired)
            task.Retire(retired);
        _shadowRetired.Clear();
        foreach (IDisposable retired in _retired)
            task.Retire(retired);
        _retired.Clear();
        DisposeReleased();
        ReleaseIdleCaptures();
        return task;
    }

    // The counts of the frame just recorded, and what each host view drawn did
    private FrameStats Stats(ReadOnlySpan<ViewDesc> views)
    {
        List<ViewStats> drawn = new(views.Length);
        foreach (int index in _viewOrder)
        {
            if (_cameras.TryGetValue(views[index].ViewId, out CameraFrame? camera) && camera.LastFrame == _frame)
                drawn.Add(new ViewStats(camera.Desc.Owner!, camera.SceneColorUsed, camera.SceneDepthUsed, camera.BackDepthUsed, camera.Jitter));
        }
        return new FrameStats(views.Length, _drawCalls, _drawCommands, _shadowDrawCalls, _shadowDrawCommands, ShadowsAdded, ShadowsGrown, ShadowsShrunk, ShadowViewsDrawn, ShadowCopiesMade) { ViewStats = drawn };
    }

    private void Draw(CameraFrame camera, in ViewDesc view, RenderFeature[] features, WorldFrame world, in FrameTime time)
    {
        camera.LastFrame = _frame;
        camera.Desc = view;
        if (camera.LayerCount != view.LayerCount)
        {
            camera.LayerCount = view.LayerCount;
            camera.HasPrevious = false;
        }
        if (view.Cut)
            camera.HasPrevious = false;
        camera.World = world;
        camera.ResolveKeywords();
        camera.HostBuffers = world.World.Globals.BufferList;
        camera.Cubes = world.World.Reflections.SampledCubes();
        uint width = Math.Max(1u, (uint)MathF.Round(view.Target.Width * view.Viewport.Z * view.RenderScale));
        uint height = Math.Max(1u, (uint)MathF.Round(view.Target.Height * view.Viewport.W * view.RenderScale));
        camera.Info = new FeatureView(view, width, height);
        camera.SelectFeatures(world.World.FeatureSnapshot, features);
        camera.PruneStates(world.World.FeatureSnapshot, features);
        camera.WasAutoExposed = camera.AutoExposed;
        camera.AutoExposed = camera.Selected.Exists(static feature => feature.MeasuresExposure);
        if (camera.AutoExposed)
            camera.EnsureExposure();
        (CascadeSlot[] _, string _) = CascadesOf(camera, view, camera.World);
        CameraShape shape = CameraShape.Of(view, camera.Selected, camera.HostBuffers.Length, camera.Cubes.Length);
        camera.ShapeKey = shape;
        ResolveScreenLighting(camera, shape, width, height);
        camera.ListSelectedPasses();
        Prepare(camera, view, world, time, width, height);
        UpdateSceneColor(camera, shape, width, height);
        UpdateSceneDepth(camera, shape, width, height);
        UpdateBackDepth(camera, shape, width, height);
        if ((shape.Targets & SceneTargets.Reactive) != 0 && !DrawsTransmission(camera))
            camera.ShapeKey = shape with { Targets = shape.Targets & ~SceneTargets.Reactive };
        camera.View.PixelWidth = width;
        camera.View.PixelHeight = height;
        bool capture = view.Kind is ViewKind.SkyCapture or ViewKind.ReflectionProbe;
        _planned.Add(new PlannedInstance(capture ? PlannedKind.Capture : PlannedKind.Host, world, camera, view.Name ?? camera.DefaultName(capture)));
    }

    private void ResolveScreenLighting(CameraFrame camera, CameraShape shape, uint width, uint height)
    {
        uint w = Math.Max(1u, (uint)(width * shape.ScreenFraction)), h = Math.Max(1u, (uint)(height * shape.ScreenFraction));
        camera.EnsureScreenLighting(shape.HasScreenLighting, shape.ScreenIrradiance, w, h, (uint)(shape.Layers > 1 ? shape.Layers : 0));
    }

    // The frame's passes, in the order they run: uploads, the frame features of the slots before the views, then each world's
    // shadows, captures and fills, the host's views in order and the frame features of the slots after them
    private void BuildFrame()
    {
        _exports.Clear();
        Buffers.Worlds.Clear();
        Buffers.Worlds.AddRange(_frameWorlds);
        Buffers.Cameras.Clear();
        foreach (CameraFrame camera in Active())
            Buffers.Cameras.Add(camera);
        _upload.Add(_graph, Buffers);
        AddExposure();
        AddFrameFeatures(FrameSlot.Deform, FrameSlot.BeforeViews);
        _counts.Add(_graph);
        AddFrameFeatures(FrameSlot.Shadows, FrameSlot.Shadows, rendererOnly: true);
        foreach (WorldFrame world in _frameWorlds)
        {
            using (_graph.Group(world.ShadowsGroup(WorldName(world))))
                AddFrameFeatures(FrameSlot.Shadows, FrameSlot.Shadows, world);
            foreach (PlannedInstance planned in _planned)
            {
                if (planned.World == world && planned.Kind == PlannedKind.Capture)
                    AddPlanned(planned);
            }
            AddFrameFeatures(FrameSlot.Captures, FrameSlot.Captures, world);
        }
        AddFrameFeatures(FrameSlot.Captures, FrameSlot.Captures, rendererOnly: true);
        foreach (PlannedInstance planned in _planned)
        {
            if (planned.Kind == PlannedKind.Host)
                AddPlanned(planned);
        }
        AddFrameFeatures(FrameSlot.AfterViews, FrameSlot.Readback);
        _exports.Verify();
        if (_readback.Requests.Count > 0 && !_exports.Provided(RendererOutputs.ReadbackStaging))
            throw new InvalidOperationException($"The host asked for readbacks, which read '{RenderResourceID.ToString(RendererOutputs.ReadbackStaging)}', and no active frame feature provided it.");
    }

    // Views an AutoExposure drives, or drove last frame, take their exposure on the GPU after the view blocks upload
    private void AddExposure()
    {
        List<(CameraFrame Camera, ExposurePush Push, bool Measured)> exposed = new();
        foreach (CameraFrame camera in Active())
        {
            bool fromState = camera.WasAutoExposed && camera.HadPrevious;
            if (camera.LastFrame != _frame || !camera.AutoExposed && !fromState)
                continue;
            bool measured = camera.AutoExposed && camera.WasAutoExposed;
            uint flags = (measured ? 1u : 0) | (fromState ? 2u : 0) | (camera.HadPrevious ? 0 : 4u);
            exposed.Add((camera, new ExposurePush { Host = camera.HostExposure, Flags = flags, Layers = (uint)camera.LayerCount }, measured));
        }
        if (exposed.Count == 0)
            return;

        _graph.AddPass("Graphite.Exposure", builder =>
        {
            foreach ((CameraFrame camera, ExposurePush _, bool measured) in exposed)
            {
                builder.Write(_graph.Import(camera.ViewBlocks.Buffer), BufferAccess.ShaderRead | BufferAccess.ShaderWrite);
                builder.Write(_graph.Import(camera.ExposureState!), BufferAccess.ShaderRead | BufferAccess.ShaderWrite);
                if (measured)
                    builder.Read(_graph.Import(camera.MeasuredExposure!), TextureState.Sampled);
            }
        }, context =>
        {
            CommandBuffer cmd = context.Command;
            cmd.SetComputeShader(Resources.ExposureProgram.Program);
            foreach ((CameraFrame camera, ExposurePush push, bool measured) in exposed)
            {
                _exposureProperties.SetTexture(s_measured, measured ? camera.MeasuredExposure!.ColorTextures[0] : Resources.NoExposure, Resources.LinearClamp);
                _exposureProperties.SetBuffer(s_exposureState, camera.ExposureState!);
                _exposureProperties.SetBuffer(s_exposureViews, camera.ViewBlocks.Buffer);
                cmd.SetProperties(_exposureProperties);
                cmd.SetPushConstants(push);
                cmd.Dispatch(1, 1, 1);
            }
        });
    }

    // The active features of the slots, in order: a world's own, the renderer's own, or every one
    private void AddFrameFeatures(FrameSlot first, FrameSlot last, WorldFrame? world = null, bool rendererOnly = false)
    {
        foreach (FrameEntry entry in _frameFeatures)
        {
            FrameSlot slot = entry.Feature.Slot;
            if (slot < first || slot > last || (world != null && entry.World != world) || (rendererOnly && entry.World != null))
                continue;
            if (entry.World is { } owner && slot is not (FrameSlot.Shadows or FrameSlot.Captures))
            {
                using (_graph.Group(WorldName(owner)))
                    AddFrameFeature(entry);
            }
            else
            {
                AddFrameFeature(entry);
            }
        }
    }

    private static string WorldName(WorldFrame world) => world.World.Name ?? $"World {world.World.Id}";

    private void AddFrameFeature(in FrameEntry entry)
    {
        if (entry.Feature.IsBuiltIn)
        {
            entry.Feature.AddBuiltIn(this, entry.World);
            return;
        }
        _frameFeaturePasses[entry.Feature].Add();
    }

    internal void AddFills(WorldFrame world)
    {
        foreach (PlannedInstance planned in _planned)
        {
            if (planned.World == world && planned.Kind == PlannedKind.Fill && planned.Data is CaptureFill fill)
                CaptureFillPasses.Add(this, _graph, fill);
        }
    }

    internal void AddReadbacks()
    {
        for (int i = 0; i < _readback.Requests.Count; i++)
        {
            using (_graph.Group(_readback.Requests[i].Name ?? $"Readback {i}"))
                ReadbackPasses.Add(_graph, _readback.Requests[i], i);
        }
    }

    private void AddPlanned(in PlannedInstance planned)
    {
        using (_graph.Group(planned.Name))
        {
            if (planned.Data is CameraFrame camera)
                camera.Build.Build(camera.ShapeKey);
        }
    }

    // Whether the view's transparent list reads the scene colour this frame. Returns whether its copy was just made, which
    // adds the copy to the view's graph; the lists keep their order through that rebuild, so what Prepare uploaded holds.
    private void UpdateSceneColor(CameraFrame camera, CameraShape shape, uint width, uint height)
    {
        bool used = false;
        foreach (DrawCall call in camera.Transparent.Calls)
        {
            DrawProgram program = DrawPrograms[call.Program];
            if (program.Shader.PassReadsSceneColor(program.PassIndex) && program.Shader.PassReadsIn(program.PassIndex, camera.Keywords))
            {
                used = true;
                break;
            }
        }
        camera.SceneColorUsed = used;
        if (used)
            camera.EnsureSceneColor(width, height, (uint)shape.Layers, camera.Build.ColorFormatOf(shape));
    }

    // Whether the view's transparent list reads the scene depth this frame, which makes the copy
    private void UpdateSceneDepth(CameraFrame camera, CameraShape shape, uint width, uint height)
    {
        bool used = false;
        foreach (DrawCall call in camera.Transparent.Calls)
        {
            DrawProgram program = DrawPrograms[call.Program];
            if (program.Shader.PassReadsSceneDepth(program.PassIndex) && program.Shader.PassReadsIn(program.PassIndex, camera.Keywords))
            {
                used = true;
                break;
            }
        }
        camera.SceneDepthUsed = used;
        if (used)
            camera.EnsureSceneDepth(width, height, (uint)shape.Layers, camera.Build.SceneDepthFormat);
    }

    // Whether the view's transparent list holds a Transmission pass, the only writer of the reactive mask
    private bool DrawsTransmission(CameraFrame camera)
    {
        foreach (DrawCall call in camera.Transparent.Calls)
        {
            DrawProgram program = DrawPrograms[call.Program];
            if (program.PassIndex == program.Shader.PassIndex(PassType.Transmission))
                return true;
        }
        return false;
    }

    // Whether the view's transparent list reads the back depth of its volumes this frame and any volume draws it
    private void UpdateBackDepth(CameraFrame camera, CameraShape shape, uint width, uint height)
    {
        bool used = false;
        if (camera.FindList(PassType.BackDepth) is not { Calls.Count: > 0 })
        {
            camera.BackDepthUsed = false;
            return;
        }
        foreach (DrawCall call in camera.Transparent.Calls)
        {
            DrawProgram program = DrawPrograms[call.Program];
            if (program.Shader.PassReadsBackDepth(program.PassIndex) && program.Shader.PassReadsIn(program.PassIndex, camera.Keywords))
            {
                used = true;
                break;
            }
        }
        camera.BackDepthUsed = used;
        if (used)
            camera.EnsureBackDepth(width, height, (uint)shape.Layers, camera.Build.SceneDepthFormat);
    }

    internal FrameTime CurrentTime => _currentTime;

    internal FrameExports Exports => _exports;

    // The frame features that run this frame, in slot then order: the renderer's and every world's, each group resolved
    private void PrepareFrameFeatures(IEnumerable<WorldFrame> worlds)
    {
        _frameFeatures.Clear();
        AddCandidates(RendererFeatures, null);
        foreach (WorldFrame world in worlds)
            AddCandidates(world.World.FrameFeatureSnapshot, world);

        // A built in feature gives way to any other of its group in its own world, or from the renderer's set
        for (int i = _frameFeatures.Count - 1; i >= 0; i--)
        {
            FrameEntry entry = _frameFeatures[i];
            if (entry.Feature.Group is not { } group)
                continue;
            for (int j = 0; j < _frameFeatures.Count; j++)
            {
                FrameEntry other = _frameFeatures[j];
                if (j == i || other.Feature.Group != group)
                    continue;
                bool replaces = entry.Feature.IsBuiltIn ? !other.Feature.IsBuiltIn && (entry.World == null || other.World == null || other.World == entry.World)
                    : !other.Feature.IsBuiltIn && other.World == entry.World && j > i;
                if (replaces)
                {
                    _frameFeatures.RemoveAt(i);
                    break;
                }
            }
        }

        _frameFeatures.Sort(static (a, b) => a.Feature.Slot != b.Feature.Slot ? a.Feature.Slot.CompareTo(b.Feature.Slot)
            : a.Feature.OrderSnapshot != b.Feature.OrderSnapshot ? a.Feature.OrderSnapshot.CompareTo(b.Feature.OrderSnapshot) : a.Feature.Id.CompareTo(b.Feature.Id));

        int user = 0;
        foreach (FrameEntry entry in _frameFeatures)
        {
            if (entry.Feature.IsBuiltIn)
                continue;
            user++;
            if (!_frameFeaturePasses.ContainsKey(entry.Feature))
                _frameFeaturePasses[entry.Feature] = new FrameFeaturePass(this, entry.Feature, entry.World);
        }
        if (_frameFeaturePasses.Count == user)
            return;
        List<FrameFeature>? gone = null;
        foreach (FrameFeature feature in _frameFeaturePasses.Keys)
        {
            if (!_frameFeatures.Exists(entry => entry.Feature == feature))
                (gone ??= new()).Add(feature);
        }
        foreach (FrameFeature feature in gone ?? [])
            _frameFeaturePasses.Remove(feature);
    }

    private void AddCandidates(FrameFeature[] features, WorldFrame? world)
    {
        foreach (FrameFeature feature in features)
        {
            if (feature.EnabledSnapshot && IsActive(feature))
                _frameFeatures.Add(new FrameEntry(feature, world));
        }
    }

    private bool IsActive(FrameFeature feature)
    {
        try
        {
            return feature.IsActive();
        }
        catch (Exception e)
        {
            Report(new RendererError("frame.feature", $"{feature.GetType().Name}.IsActive threw: {e}", RendererErrorSeverity.Error, feature) { Key = $"frame.feature.active.{feature.GetType().FullName}" });
            return false;
        }
    }

    // The copies of indirect items' instance counts into the views' commands
    private void PlanCounts()
    {
        _counts.Copies.Clear();
        foreach (CameraFrame camera in Active())
        {
            if (camera.LastFrame != _frame)
                continue;
            foreach ((RenderResourceID source, uint offset, uint destination) in camera.Counts)
                _counts.Copies.Add((source, offset, camera, destination));
        }
    }

    internal void BindWorld(PropertySet properties, RenderWorld world)
    {
        RenderWorld.RenderSide render = world.Render;
        WorldLighting lighting = world.Lighting;
        properties.SetBuffer(s_chunks, Geometry.Chunks.Buffer);
        properties.SetBuffer(s_transforms, render.Transforms.Buffer);
        properties.SetBuffer(s_previousTransforms, render.PreviousTransforms.Buffer);
        properties.SetBuffer(s_objects, render.Attributes.Buffer);
        properties.SetBuffer(s_materialSlots, render.MaterialSlots.Buffer);
        properties.SetBuffer(s_worldLighting, lighting.World.Buffer);
        properties.SetBuffer(s_objectProbes, world.Probes.ObjectSh);
        properties.SetBuffer(s_skyAmbientSH, world.Reflections.SkyAmbient);
        properties.SetBuffer(s_directionals, lighting.Directionals.Buffer);
    }

    private void PrepareFeatures(RenderFeature[] features)
    {
        foreach (RenderFeature feature in features)
        {
            if (!feature.EnabledSnapshot || !_prepared.Add(feature))
                continue;
            try
            {
                feature.PrepareFrame();
            }
            catch (Exception e)
            {
                Report(new RendererError("feature.prepare", $"{feature.GetType().Name}.PrepareFrame threw: {e}", RendererErrorSeverity.Error, feature) { Key = $"feature.prepare.{feature.GetType().FullName}" });
            }
        }
    }

    // The world's providers add this view's items to its lists, transparent ones kept for the sort
    private void CullProviders(CameraFrame camera, in ViewDesc view, WorldFrame world, in CullFrustum frustum)
    {
        camera.Sink.Begin(camera, world, frustum, view.CullingMask, camera.IsShadowView);
        ViewKinds kind = (ViewKinds)(1 << (int)view.Kind);
        ProviderView providerView = new(camera.Info, frustum);
        world.World.Transients.Cull(providerView, camera.Sink);
        foreach (RenderProvider provider in world.World.ProviderSnapshot)
        {
            if (!provider.EnabledSnapshot || (provider.RunsOn & kind) == 0)
                continue;
            try
            {
                provider.Cull(providerView, camera.Sink);
            }
            catch (Exception e)
            {
                Report(new RendererError("provider.cull", $"{provider.GetType().Name}.Cull threw: {e}", RendererErrorSeverity.Error, provider) { Key = $"provider.cull.{provider.GetType().FullName}" });
            }
        }
    }

    // Views by Order, each after the views it depends on. A cycle is reported and drawn in Order.
    private void OrderViews(ReadOnlySpan<ViewDesc> views)
    {
        _viewOrder.Clear();
        for (int i = 0; i < views.Length; i++)
        {
            int j = _viewOrder.Count - 1;
            while (j >= 0 && views[_viewOrder[j]].Order > views[i].Order)
                j--;
            _viewOrder.Insert(j + 1, i);
        }

        bool dependencies = false;
        foreach (ViewDesc view in views)
            dependencies |= view.DependsOn is { Length: > 0 };
        if (!dependencies)
            return;

        List<int> sorted = _sortedViews;
        sorted.Clear();
        sorted.AddRange(_viewOrder);
        if (_placed.Length < views.Length)
            _placed = new bool[views.Length];
        bool[] placed = _placed;
        Array.Clear(placed);
        _viewOrder.Clear();
        while (_viewOrder.Count < sorted.Count)
        {
            int next = -1;
            foreach (int index in sorted)
            {
                if (!placed[index] && Ready(views, index, placed))
                {
                    next = index;
                    break;
                }
            }
            if (next < 0)
            {
                Report(new RendererError("view.dependency.cycle", "Views depend on each other in a cycle, so the rest draw in Order.", RendererErrorSeverity.Warning));
                foreach (int index in sorted)
                {
                    if (!placed[index])
                        _viewOrder.Add(index);
                }
                return;
            }
            placed[next] = true;
            _viewOrder.Add(next);
        }
    }

    // Whether every view a view depends on is placed, ones not submitted this frame counting as placed
    private static bool Ready(ReadOnlySpan<ViewDesc> views, int index, bool[] placed)
    {
        if (views[index].DependsOn is not { } dependencies)
            return true;
        foreach (RenderView dependency in dependencies)
        {
            for (int i = 0; i < views.Length; i++)
            {
                if (i != index && views[i].Owner == dependency && !placed[i])
                    return false;
            }
        }
        return true;
    }

    /// <summary>Releases what the frame pipeline keeps for a removed world. Render thread idle.</summary>
    public void ForgetWorld(WorldFrame world)
    {
        if (world.Shadows != null)
        {
            foreach (ShadowPage page in world.Shadows.AllPages())
                page.Persistent.Release(_store);
        }
        if (world.SkyProgram is ushort program)
            DrawPrograms.Release(program);
        world.SkyProgram = null;
        foreach (CameraFrame camera in world.Captures())
            Untrack(camera);
    }

    // A view's table and its cascades' tables leave the sampler tables they follow
    private void Untrack(CameraFrame camera)
    {
        _textures.Untrack(camera.ViewTable);
        foreach (ViewShadows.CascadeLight light in camera.Shadows.Lights)
        {
            foreach (ViewShadows.Cascade cascade in light.Cascades)
            {
                if (cascade.Camera != null)
                    _textures.Untrack(cascade.Camera.ViewTable);
            }
        }
    }

    // Takes in finished compiles and source edits, then asks for every call's program before recording, so compiles
    // start early and Sync ones finish here.
    private void ResolvePrograms()
    {
        Programs.Advance();
        foreach (WorldFrame world in _frameWorlds)
        {
            if (world.SkyProgram is ushort sky)
                Programs.Get(sky, false, world.World.Globals.KeywordSet);
        }
        foreach (CameraFrame camera in Active())
        {
            if (camera.LastFrame != _frame)
                continue;
            CompilePriority priority = camera.Desc.Kind switch
            {
                ViewKind.Shadow => CompilePriority.Shadow,
                ViewKind.ReflectionProbe or ViewKind.SkyCapture => CompilePriority.Capture,
                _ => CompilePriority.Visible,
            };
            foreach (DrawList list in camera.Lists)
            {
                foreach (DrawCall call in list.Calls)
                    Programs.Get(call.Program, call.Mirrored != camera.Mirrored, camera.KeywordSet, priority);
            }
        }
    }

    private void Prepare(CameraFrame camera, in ViewDesc view, WorldFrame world, in FrameTime time, uint width, uint height)
    {
        camera.Time = time;
        int layers = view.LayerCount;
        bool shadow = camera.IsShadowView;
        CullFrustum frustum = shadow ? camera.ShadowCull : new(view.Layers, layers);
        Array.Fill(camera.Roots, SpatialRoots.None);
        if (shadow)
        {
            world.Buckets.Cull(frustum, view.CullingMask, camera.ShadowFilter);
            if (camera.ShadowFilter == CasterFilter.StaticOnly)
                camera.Sink.Begin(camera, world, frustum, 0);
            else
                CullProviders(camera, view, world, frustum);
            camera.Prepass.Clear();
            camera.Forward.Clear();
            camera.Transparent.Clear();
            foreach ((PassType type, DrawList list) in camera.PassLists)
                world.Buckets.Build(world.Buckets.Pass(type), frustum, list, camera.Sink.For(list));
        }
        else if (view.CullingMask == 0)
        {
            camera.Sink.Begin(camera, world, frustum, 0);
            foreach (DrawList list in camera.Lists)
                list.Clear();
        }
        else
        {
            world.Buckets.Cull(frustum, view.CullingMask);
            CullProviders(camera, view, world, frustum);
            DrawItemSink sink = camera.Sink;
            world.Buckets.Build(0, frustum, camera.Prepass, sink.For(camera.Prepass));
            world.Buckets.Build(1, frustum, camera.Forward, sink.For(camera.Forward));
            foreach ((PassType type, DrawList list) in camera.PassLists)
                world.Buckets.Build(world.Buckets.Pass(type), frustum, list, sink.For(list));
            world.Buckets.BuildTransparent(frustum, camera.Transparent, sink.For(camera.Transparent)?.Sorted);

            foreach (SpatialIndex set in world.World.SpatialSets)
                camera.Roots[set.Slot] = set.BuildView(frustum, camera.SpatialFor(set.Slot), _uploader);
        }
        camera.SpatialRoots.Write<SpatialRoots>(_uploader, camera.Roots);
        ViewLayer first = view.Layers[0];
        float handedness = Determinant3(first.View) * first.Projection.c0.X * first.Projection.c1.Y;
        camera.Mirrored = handedness < 0;

        // A feature asking for jitter offsets the projection by under a pixel, through Halton(2, 3)
        int phases = 0;
        float mipBias = 0;
        foreach (RenderFeature feature in camera.Selected)
        {
            if (feature.JitterPhasesSnapshot > phases)
                (phases, mipBias) = (feature.JitterPhasesSnapshot, feature.JitterMipBiasSnapshot);
        }
        camera.HadPrevious = camera.HasPrevious;
        camera.PreviousJitter = camera.Jitter;
        camera.Jitter = default;
        if (phases > 0 && !camera.IsShadowView)
        {
            int phase = camera.JitterPhase++ % phases + 1;
            camera.Jitter = new Float2((Halton(phase, 2) - 0.5f) * 2f / width, (Halton(phase, 3) - 0.5f) * 2f / height);
        }
        _textures.SetViewBias(camera.ViewTable, phases > 0 ? mipBias : 0);
        float exposure = RenderView.ExposureOf(view.EV100);
        bool capture = view.Kind is ViewKind.SkyCapture or ViewKind.ReflectionProbe;

        for (int l = 0; l < layers; l++)
        {
            ViewLayer layer = view.Layers[l];
            Float4x4 projection = layer.Projection;
            if (projection.c3.W > 0.5f)
                (projection.c3.X, projection.c3.Y) = (projection.c3.X + camera.Jitter.X, projection.c3.Y + camera.Jitter.Y);
            else
                (projection.c2.X, projection.c2.Y) = (projection.c2.X + camera.Jitter.X, projection.c2.Y + camera.Jitter.Y);
            ViewBlock block = ViewBlock.Create(layer.View, projection, layer.Position, width, height, view.Near, view.FarDistance);
            block.Exposure = new Float4(exposure, camera.HasPrevious ? camera.HostExposure : exposure, capture ? 1 : 0, 0);
            block.Jitter = new Float4(camera.Jitter.X, camera.Jitter.Y, camera.PreviousJitter.X, camera.PreviousJitter.Y);
            float timeHigh = (float)time.Time;
            block.Time = new Float4(timeHigh, time.DeltaTime, time.FrameIndex, (float)(time.Time - timeHigh));
            Float3 offset = frustum.Relative(layer.Position);
            block.TreeOffset = new Float4(offset.X, offset.Y, offset.Z, 0);
            if (camera.HasPrevious)
            {
                block.PreviousViewProjection = camera.Previous[l].ViewProjection;
                block.PreviousCameraHigh = camera.Previous[l].CameraHigh;
                block.PreviousCameraLow = camera.Previous[l].CameraLow;
            }
            camera.Previous[l] = block with { ViewProjection = layer.Projection * layer.View };
            camera.Blocks[l] = block;
        }
        camera.HasPrevious = true;
        camera.HostExposure = exposure;
        camera.Upload(_uploader, camera.Blocks.AsSpan(0, layers));
        camera.PlanCounts();
    }

    /// <summary>A host view's frame state, for tests.</summary>
    internal CameraFrame? Camera(RenderView view) => _cameras.GetValueOrDefault(view.Id);

    private static float Halton(int index, int radix)
    {
        float result = 0, fraction = 1f / radix;
        for (int i = index; i > 0; i /= radix)
        {
            result += i % radix * fraction;
            fraction /= radix;
        }
        return result;
    }

    // The hosts' views drawn this frame and every capture view, drawn this frame or not
    private IEnumerable<CameraFrame> Active()
    {
        foreach (CameraFrame camera in _cameras.Values)
        {
            if (camera.LastFrame == _frame)
                yield return camera;
        }
        foreach (CameraFrame camera in _captures)
            yield return camera;
        foreach (CameraFrame camera in ShadowCameras())
            yield return camera;
    }

    private static float Determinant3(in Float4x4 m)
        => m.c0.X * (m.c1.Y * m.c2.Z - m.c2.Y * m.c1.Z)
         - m.c1.X * (m.c0.Y * m.c2.Z - m.c2.Y * m.c0.Z)
         + m.c2.X * (m.c0.Y * m.c1.Z - m.c1.Y * m.c0.Z);

    // Disposed views' state leaves the frame, freed once the executions that may still read it are done
    private void ReleaseViews(List<int> released, ulong lastDispatched)
    {
        foreach (int id in released)
        {
            if (!_cameras.Remove(id, out CameraFrame? camera))
                continue;
            Untrack(camera);
            _releasing.Add((lastDispatched, camera));
        }
    }

    private void DisposeReleased()
    {
        ulong completed = _device.LastCompletedExecutionId;
        for (int i = _releasing.Count - 1; i >= 0; i--)
        {
            if (_releasing[i].After > completed)
                continue;
            _releasing[i].Camera.Dispose();
            _releasing.RemoveAt(i);
        }
    }

    private readonly List<(ulong After, CameraFrame Camera)> _releasing = new();

    public void Dispose()
    {
        foreach (CameraFrame camera in _cameras.Values)
            camera.Dispose();
        _cameras.Clear();
        foreach ((_, CameraFrame camera) in _releasing)
            camera.Dispose();
        _releasing.Clear();
        ReturnShadowCameras();
        foreach (CameraFrame camera in _shadowCameraPool)
            camera.Dispose();
        _shadowCameraPool.Clear();
        _providerProbe?.Dispose();
        _graph.Dispose();
        _shadowSampler?.Dispose();
        _tileClear?.Dispose();
        _probes.Dispose();
        _deform.Dispose();
        Programs.Dispose();
        foreach (IDisposable retired in _retired)
            retired.Dispose();
    }
}

/// <summary>
/// What a frame drew: the views asked for, and the indirect draw calls recorded with the draws inside them (each
/// command one mesh piece, instanced or not), split between camera views and shadow views, and the local light shadows
/// that were given tiles, grew or shrank. ShadowViewsDrawn counts one per cascade or local light face drawn again, faces
/// still cached are not counted, and ShadowCopiesMade the static shadow tiles copied back under lights whose moving casters drew again.
/// </summary>
public sealed record FrameStats(int Views, int DrawCalls, int DrawCommands, int ShadowDrawCalls, int ShadowDrawCommands, int ShadowsAdded = 0, int ShadowsGrown = 0, int ShadowsShrunk = 0, int ShadowViewsDrawn = 0, int ShadowCopiesMade = 0)
{
    /// <summary>The host views drawn, in the order they drew.</summary>
    public IReadOnlyList<ViewStats> ViewStats { get; init; } = [];

    /// <summary>The stats of a view, or null when it was not drawn.</summary>
    public ViewStats? View(RenderView view)
    {
        foreach (ViewStats stats in ViewStats)
        {
            if (stats.View == view)
                return stats;
        }
        return null;
    }
}

/// <summary>
/// What one host view did in a frame: whether a transparent pass read the scene colour or depth, so the view copied its
/// opaque colour or depth, whether a transmissive pass read the back depth, so the view drew its volumes' back faces,
/// and the sub pixel offset its projection was shifted by in clip space, zero when no feature asks for one.
/// </summary>
public sealed record ViewStats(RenderView View, bool SceneColorUsed, bool SceneDepthUsed, bool BackDepthUsed, Float2 Jitter);
