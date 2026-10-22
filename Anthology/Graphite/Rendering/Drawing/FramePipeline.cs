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
    [StructLayout(LayoutKind.Sequential)]
    public struct Push
    {
        public uint DrawRecordBase;
        public uint InstanceBase;
        public uint ViewBase;
        public uint Flags;
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
    private readonly FrameGraph _probeGraph;

    internal RenderStore Store => _store;

    internal DeformPass Deform => _deform;
    internal ProbePass Probes => _probes;

    /// <summary>The frame features that ran in the last frame, for tests.</summary>
    internal IEnumerable<FrameFeature> ActiveFrameFeatures => _frameFeatures.Select(entry => entry.Feature);

    /// <summary>The frame features of the renderer's own set, as of this frame. Render thread.</summary>
    internal FrameFeature[] RendererFeatures = [];

    /// <summary>The graph every frame is built into.</summary>
    internal FrameGraph Graph => _graph;

    /// <summary>A graph nobody runs, which views build into to learn what their features ask for.</summary>
    internal FrameGraph ProbeGraph => _probeGraph;
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
    private readonly bool _deterministic;

    private readonly Uploader _uploader;

    public FramePipeline(GraphicsDevice device, RenderStore store, IShaderCompiler? compiler, Uploader uploader, GeometryPool geometry, MaterialSystem materials, GlobalRegistry globals, DrawPrograms programs, RendererTextures textures, bool deterministic, Action<RendererError> report, CompilePolicy policy = CompilePolicy.Async)
    {
        Report = report;
        _device = device;
        _compiler = compiler;
        _textures = textures;
        _store = store;
        _uploader = uploader;
        _deterministic = deterministic;
        Geometry = geometry;
        Materials = materials;
        Globals = globals;
        DrawPrograms = programs;
        Programs = new ProgramCache(device, compiler, globals, programs, deterministic, report, policy, _retired.Add);
        programs.Freed += Programs.Drop;
        _graph = new FrameGraph(device, "Graphite");
        _probeGraph = new FrameGraph(device, "Graphite probe");
        Buffers = new FrameBuffers();
        _counts = new ProviderCountsPass(this);
        _upload = new UploadPass(uploader, store);
        _probes = new ProbePass(InternalShaders.Compute(device, compiler, "Internal/Probes.glsl"));
        _deform = new DeformPass(InternalShaders.Compute(device, compiler, "Internal/Deform.glsl"), geometry);
        _readback = new ReadbackStaging(device, report);

        const string present = InternalShaders.Present, decode = InternalShaders.Decode, encode = InternalShaders.Encode;
        BlendStateDescription over = new(default, BlendAttachmentDescription.AlphaBlend with { SourceColorFactor = BlendFactor.One, SourceAlphaFactor = BlendFactor.One });
        _present =
        [
            FullscreenProgram.Create(device, compiler, present, "Graphite/Present"),
            FullscreenProgram.Create(device, compiler, present, "Graphite/PresentOver", blend: over),
            FullscreenProgram.Create(device, compiler, decode, "Graphite/PresentDecode"),
            FullscreenProgram.Create(device, compiler, decode, "Graphite/PresentDecodeOver", blend: over),
            FullscreenProgram.Create(device, compiler, encode, "Graphite/PresentEncode"),
            FullscreenProgram.Create(device, compiler, encode, "Graphite/PresentEncodeOver", blend: over),
        ];
        Noise = LoadData(device, "Noise.bin", 256, 256, mipmapped: true);
        SmaaArea = LoadData(device, "SmaaArea.bin", 160, 560, mipmapped: false);
        SmaaSearch = LoadData(device, "SmaaSearch.bin", 64, 16, mipmapped: false);
        SmaaAreaLayered = LoadData(device, "SmaaArea.bin", 160, 560, mipmapped: false, layers: 6);
        SmaaSearchLayered = LoadData(device, "SmaaSearch.bin", 64, 16, mipmapped: false, layers: 6);
        textures.RegisterDefault("noise", Noise);
        NoScreenVisibility = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 6, PixelFormat.R8_UNorm, TextureUsage.Sampled));
        NoScreenIrradiance = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 6, PixelFormat.R16_G16_B16_A16_Float, TextureUsage.Sampled));
        NoSceneColor = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 6, PixelFormat.R16_G16_B16_A16_Float, TextureUsage.Sampled));
        for (uint layer = 0; layer < 6; layer++)
        {
            device.UpdateTexture<byte>(NoScreenVisibility, [255], TextureRegion.Whole(NoScreenVisibility, 0, layer));
            device.UpdateTexture<ushort>(NoScreenIrradiance, [0, 0, 0, 0], TextureRegion.Whole(NoScreenIrradiance, 0, layer));
            device.UpdateTexture<ushort>(NoSceneColor, [0, 0, 0, 0], TextureRegion.Whole(NoSceneColor, 0, layer));
        }
        ClearProgram = FullscreenProgram.Create(device, compiler, InternalShaders.Clear, "Graphite/Clear", source: false);
        BlendAttachmentDescription untouched = BlendAttachmentDescription.OverrideBlend with { ColorWriteMask = ColorWriteMask.None };
        CopyBackProgram = FullscreenProgram.Create(device, compiler, InternalShaders.CopyBack, "Graphite/CopyBack",
            blend: new BlendStateDescription(default, BlendAttachmentDescription.OverrideBlend, untouched, untouched));
        PrefilterProgram = Fullscreen(device, compiler, "Internal/Prefilter.glsl");
        SkyAmbientProgram = InternalShaders.Compute(device, compiler, "Internal/SkyAmbient.glsl");
        TrilinearClamp = device.ResourceFactory.CreateSampler(SamplerDescription.Linear with
        {
            AddressModeU = SamplerAddressMode.Clamp,
            AddressModeV = SamplerAddressMode.Clamp,
            AddressModeW = SamplerAddressMode.Clamp,
        });
        LinearSampler = device.ResourceFactory.CreateSampler(SamplerDescription.Linear);
    }

    public GraphicsDevice Device => _device;
    public GeometryPool Geometry { get; }
    public MaterialSystem Materials { get; }
    public GlobalRegistry Globals { get; }
    public DrawPrograms DrawPrograms { get; }
    public ProgramCache Programs { get; }
    private readonly FullscreenProgram[] _present;

    /// <summary>
    /// Copies the view's colour to its target with its rows reversed, the first row at the bottom of clip space. Decoding
    /// undoes a tonemapper's encode for sRGB and float targets so theirs applies once, encoding sRGB encodes linear colour for
    /// targets that store display values, and over blends it as premultiplied by its alpha.
    /// </summary>
    public FullscreenProgram Present(bool decode, bool encode, bool over) => _present[(decode ? 2 : encode ? 4 : 0) + (over ? 1 : 0)];

    /// <summary>Prowl's noise: blue noise in red, white noise in green, voronoi in blue, 256 square, mipmapped.</summary>
    public Texture Noise { get; }

    /// <summary>The SMAA reference's area and search tables, RGBA8 in its row order.</summary>
    public Texture SmaaArea { get; }
    public Texture SmaaSearch { get; }

    /// <summary>The SMAA tables in each of six layers, for layered views.</summary>
    public Texture SmaaAreaLayered { get; }
    public Texture SmaaSearchLayered { get; }

    /// <summary>The screen lighting a view without a producer reads: full visibility, no irradiance, six layers.</summary>
    public Texture NoScreenVisibility { get; }
    public Texture NoScreenIrradiance { get; }

    /// <summary>The scene colour a view without a copy reads: black, six layers.</summary>
    public Texture NoSceneColor { get; }

    // An RGBA8 texture from the library's data, in its stored row order, in every layer, mipmapped on the GPU when asked
    private static Texture LoadData(GraphicsDevice device, string file, uint width, uint height, bool mipmapped, uint layers = 1)
    {
        using System.IO.Stream stream = typeof(FramePipeline).Assembly.GetManifestResourceStream("Graphite/Data/" + file)
            ?? throw new InvalidOperationException($"The library has no Data/{file}.");
        byte[] pixels = new byte[width * height * 4];
        stream.ReadExactly(pixels);
        uint mips = mipmapped ? (uint)Math.Log2(Math.Max(width, height)) + 1 : 1;
        Texture texture = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(width, height, mips, layers, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        texture.Name = "Graphite " + System.IO.Path.GetFileNameWithoutExtension(file);
        for (uint layer = 0; layer < layers; layer++)
            device.UpdateTexture<byte>(texture, pixels, TextureRegion.Whole(texture, 0, layer));
        if (mipmapped)
            device.Record(cmd => cmd.GenerateMipmaps(texture)).Wait();
        return texture;
    }

    /// <summary>Fills a view's viewport with its clear colour when nothing drew.</summary>
    public FullscreenProgram ClearProgram { get; }

    /// <summary>Copies a colour features wrote back into the scene's colour, leaving its G-buffer.</summary>
    public FullscreenProgram CopyBackProgram { get; }

    /// <summary>Resources the frame stopped using, disposed once it is dispatched.</summary>
    public List<IDisposable> Retired => _retired;

    public GraphicsProgram PrefilterProgram { get; }

    /// <summary>What the capture fill passes bind, one set each so nothing one binds reaches the other.</summary>
    internal PropertySet PrefilterProperties { get; } = new();
    internal PropertySet AmbientProperties { get; } = new();

    /// <summary>Reports a problem once per key.</summary>
    public Action<RendererError> Report { get; }
    public ComputeProgram SkyAmbientProgram { get; }

    /// <summary>Frames a view keeps its per view state after it was last drawn.</summary>
    public int StaleViewFrames { get; init; } = 8;

    /// <summary>The renderer's bindless texture table, set 0 of every material program.</summary>
    public DescriptorTable TextureTable => _textures.Table;
    public Sampler TrilinearClamp { get; }

    private static GraphicsProgram Fullscreen(GraphicsDevice device, IShaderCompiler? compiler, string file)
    {
        ShaderDescription description = InternalShaders.FilePass(file).Variants(compiler, device.BackendType).Require();
        description.BlendState = BlendStateDescription.SingleOverrideBlend;
        description.DepthStencilState = DepthStencilStateDescription.Disabled;
        description.RasterizerState = RasterizerStateDescription.CullNone;
        return device.ResourceFactory.CreateGraphicsProgram(description);
    }
    public Sampler LinearSampler { get; }

    /// <summary>Every persistent buffer the frame writes and its cameras read, in a stable order.</summary>
    public FrameBuffers Buffers { get; }

    /// <summary>Declares what a view's draws bind: the tables and buffers of its world and itself, the shadow pages and cascades, the host's buffers and the cubes it samples.</summary>
    internal void DeclareDrawReads(ViewGraph view, PassBuilder builder, CameraFrame camera, bool screenVisibility, bool screenIrradiance)
    {
        FrameGraph graph = view.Graph;
        if (!view.Probing)
            RequireOutputs(view, camera);
        Buffers.ReadCamera(graph, builder, camera);
        if (camera.World.Shadows != null)
        {
            foreach (ShadowPage page in camera.World.Shadows.AllPages())
                builder.Read(graph.Import(page.Texture), TextureState.Sampled, TextureState.Sampled);
        }
        foreach (ViewShadows.CascadeLight light in camera.Shadows.Lights)
        {
            for (int c = 0; c < light.Count; c++)
            {
                if (light.Cascades[c].Texture?.Texture is { } cascade)
                    builder.Read(graph.Import(cascade), TextureState.Sampled, TextureState.Sampled);
            }
        }
        foreach (DeviceBuffer buffer in camera.HostBuffers)
            builder.Read(graph.Import(buffer), BufferAccess.AllReads);
        foreach (RenderTexture cube in camera.Cubes)
            builder.Read(graph.Import(cube), TextureState.Sampled);
        if (screenVisibility && camera.ScreenVisibility != null)
            builder.Read(graph.Import(camera.ScreenVisibility), TextureState.Sampled, TextureState.Sampled);
        if (screenIrradiance && camera.ScreenIrradiance != null)
            builder.Read(graph.Import(camera.ScreenIrradiance), TextureState.Sampled, TextureState.Sampled);
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

    /// <summary>Culls and compacts every view, then dispatches the frame. Returns the execution. Render thread.</summary>
    private int _drawCalls, _drawCommands, _shadowDrawCalls, _shadowDrawCommands;

    /// <summary>The counts of the last frame recorded. Any thread.</summary>
    public FrameStats LastStats { get; private set; } = new(0, 0, 0, 0, 0);

    // Render thread, while the frame records
    internal void CountDraw(bool shadow, int commands)
    {
        if (shadow) { _shadowDrawCalls++; _shadowDrawCommands += commands; }
        else { _drawCalls++; _drawCommands += commands; }
    }

    public ExecutionTask Run(ReadOnlySpan<ViewDesc> views, ReadOnlySpan<RenderFeature[]> viewFeatures, List<ReadbackRequest> readbacks, IReadOnlyDictionary<RenderWorld, WorldFrame> worlds, IEnumerable<WorldFrame> allWorlds, ulong lastDispatched, in FrameTime time, IProfiler[]? profilers = null)
    {
        LastStats = new FrameStats(views.Length, _drawCalls, _drawCommands, _shadowDrawCalls, _shadowDrawCommands, ShadowsAdded, ShadowsGrown, ShadowsShrunk);
        _drawCalls = _drawCommands = _shadowDrawCalls = _shadowDrawCommands = 0;
        _frame++;
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
            world.Programs.Sweep();
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
                readbacks.Add(new ReadbackRequest { Buffer = reflections.SkyAmbient, Size = reflections.SkyAmbient.SizeInBytes, BufferDone = world.World.ReceiveSkyAmbient });
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
            else if (camera.LastFrame == _frame)
            {
                throw new InvalidOperationException($"Two views share the ViewId {view.ViewId} in one frame.");
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
        if (_deterministic && Globals.NeedsGrowth)
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
        _store.RetireAll(task);
        ReturnShadowCameras();
        foreach (IDisposable retired in _shadowRetired)
            task.Retire(retired);
        _shadowRetired.Clear();
        foreach (IDisposable retired in _retired)
            task.Retire(retired);
        _retired.Clear();
        ReleaseStaleCameras();
        ReleaseIdleCaptures();
        return task;
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
        (CascadeSlot[] _, string _) = CascadesOf(camera, view, camera.World);
        CameraShape shape = CameraShape.Of(view, camera.Selected, camera.HostBuffers.Length, camera.Cubes.Length);
        camera.ShapeKey = shape;
        ResolveScreenLighting(camera, shape, width, height);
        DiscoverPassTypes(camera, shape);
        camera.ResetPassLists();
        foreach (RenderFeature feature in camera.Selected)
        {
            foreach (PassType type in camera.KnownTypes[feature])
                camera.ListFor(type);
        }
        Prepare(camera, view, world, time, width, height);
        UpdateSceneColor(camera, shape, width, height);
        camera.View.PixelWidth = width;
        camera.View.PixelHeight = height;
        bool capture = view.Kind is ViewKind.SkyCapture or ViewKind.ReflectionProbe;
        _planned.Add(new PlannedInstance(capture ? PlannedKind.Capture : PlannedKind.Host, world, camera, view.Name ?? (capture ? $"Capture {view.ViewId}" : $"Camera {view.ViewId}")));
    }

    // The pass types the view's features draw must be known before the view is culled, so the first time it selects a
    // feature the view's setup runs once into a graph nobody executes
    private void DiscoverPassTypes(CameraFrame camera, CameraShape shape)
    {
        bool unknown = false;
        foreach (RenderFeature feature in camera.Selected)
            unknown |= !camera.KnownTypes.ContainsKey(feature);
        if (!unknown)
            return;
        try
        {
            camera.Probe.Build(shape);
            foreach (RenderFeature feature in camera.Selected)
                camera.KnownTypes[feature] = [.. camera.Probe.PassOf(feature).Types];
        }
        finally
        {
            _probeGraph.ResetFrame();
        }
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
        AddFrameFeatures(FrameSlot.Deform, FrameSlot.BeforeViews);
        _counts.Add(_graph);
        AddFrameFeatures(FrameSlot.Shadows, FrameSlot.Shadows, rendererOnly: true);
        foreach (WorldFrame world in _frameWorlds)
        {
            using (_graph.Group(world.World.Name ?? $"World {world.World.Id}"))
            {
                AddFrameFeatures(FrameSlot.Shadows, FrameSlot.Shadows, world);
                foreach (PlannedInstance planned in _planned)
                {
                    if (planned.World == world && planned.Kind == PlannedKind.Capture)
                        AddPlanned(planned);
                }
                AddFrameFeatures(FrameSlot.Captures, FrameSlot.Captures, world);
            }
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

    // The active features of the slots, in order: a world's own, the renderer's own, or every one
    private void AddFrameFeatures(FrameSlot first, FrameSlot last, WorldFrame? world = null, bool rendererOnly = false)
    {
        foreach (FrameEntry entry in _frameFeatures)
        {
            FrameSlot slot = entry.Feature.SlotSnapshot;
            if (slot < first || slot > last || (world != null && entry.World != world) || (rendererOnly && entry.World != null))
                continue;
            AddFrameFeature(entry);
        }
    }

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
            if (planned.World == world && planned.Kind == PlannedKind.Fill)
                AddPlanned(planned);
        }
    }

    internal void AddReadbacks()
    {
        for (int i = 0; i < _readback.Requests.Count; i++)
        {
            using (_graph.Group($"Readback {i}"))
                ReadbackPasses.Add(_graph, _readback.Requests[i], i);
        }
    }

    private void AddPlanned(in PlannedInstance planned)
    {
        using (_graph.Group(planned.Name))
        {
            if (planned.Data is CameraFrame camera)
                camera.Build.Build(camera.ShapeKey);
            else if (planned.Data is CaptureFill fill)
                CaptureFillPasses.Add(this, _graph, fill);
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
            if (program.Shader.PassReadsSceneColor(program.PassIndex))
            {
                used = true;
                break;
            }
        }
        camera.SceneColorUsed = used;
        if (used)
            camera.EnsureSceneColor(width, height, (uint)shape.Layers, camera.Build.ColorFormatOf(shape));
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
            if (entry.Feature.GroupSnapshot is not { } group)
                continue;
            for (int j = 0; j < _frameFeatures.Count; j++)
            {
                FrameEntry other = _frameFeatures[j];
                if (j == i || other.Feature.GroupSnapshot != group)
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

        _frameFeatures.Sort(static (a, b) => a.Feature.SlotSnapshot != b.Feature.SlotSnapshot ? a.Feature.SlotSnapshot.CompareTo(b.Feature.SlotSnapshot)
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
        properties.SetBuffer("Graphite_Chunks", Geometry.Chunks.Buffer);
        properties.SetBuffer("Graphite_Transforms", render.Transforms.Buffer);
        properties.SetBuffer("Graphite_PreviousTransforms", render.PreviousTransforms.Buffer);
        properties.SetBuffer("Graphite_Objects", render.Attributes.Buffer);
        properties.SetBuffer("Graphite_MaterialSlots", render.MaterialSlots.Buffer);
        properties.SetBuffer("Graphite_WorldLighting", lighting.World.Buffer);
        properties.SetBuffer("Graphite_ObjectProbes", world.Probes.ObjectSh);
        properties.SetBuffer("Graphite_SkyAmbientSH", world.Reflections.SkyAmbient);
        properties.SetBuffer("Graphite_Directionals", lighting.Directionals.Buffer);
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
        foreach (RenderProvider provider in world.World.ProviderSnapshot)
        {
            if (!provider.EnabledSnapshot || (provider.RunsOnSnapshot & kind) == 0)
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
        foreach (int id in dependencies)
        {
            for (int i = 0; i < views.Length; i++)
            {
                if (i != index && views[i].ViewId == id && !placed[i])
                    return false;
            }
        }
        return true;
    }

    /// <summary>Releases what the frame pipeline keeps for a removed world. Render thread idle.</summary>
    public void ForgetWorld(WorldFrame world)
    {
        world.Programs.Clear();
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

    // Asks for every call's program before recording, so compiles start early and deterministic ones finish here.
    private void ResolvePrograms()
    {
        foreach (WorldFrame world in _frameWorlds)
        {
            if (world.SkyProgram is ushort sky)
                Programs.Get(sky, false, world.World.Globals.KeywordSnapshot);
        }
        foreach (CameraFrame camera in Active())
        {
            if (camera.LastFrame != _frame)
                continue;
            foreach (DrawList list in camera.Lists)
            {
                foreach (DrawCall call in list.Calls)
                    Programs.Get(call.Program, call.Mirrored != camera.Mirrored, camera.Keywords);
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

        for (int l = 0; l < layers; l++)
        {
            ViewLayer layer = view.Layers[l];
            Float4x4 projection = layer.Projection;
            if (projection.c3.W > 0.5f)
                (projection.c3.X, projection.c3.Y) = (projection.c3.X + camera.Jitter.X, projection.c3.Y + camera.Jitter.Y);
            else
                (projection.c2.X, projection.c2.Y) = (projection.c2.X + camera.Jitter.X, projection.c2.Y + camera.Jitter.Y);
            ViewBlock block = ViewBlock.Create(layer.View, projection, layer.Position, width, height, view.Near, view.FarDistance);
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
        camera.Upload(_uploader, camera.Blocks.AsSpan(0, layers));
        camera.PlanCounts();
    }

    /// <summary>A host view's frame state, for tests.</summary>
    internal CameraFrame? Camera(int viewId) => _cameras.GetValueOrDefault(viewId);

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

    private void ReleaseStaleCameras()
    {
        List<int>? stale = null;
        foreach ((int id, CameraFrame camera) in _cameras)
        {
            if (_frame - camera.LastFrame > StaleViewFrames)
                (stale ??= new()).Add(id);
        }
        if (stale == null)
            return;
        foreach (int id in stale)
        {
            Untrack(_cameras[id]);
            _cameras[id].Dispose();
            _cameras.Remove(id);
        }
    }

    public void Dispose()
    {
        foreach (CameraFrame camera in _cameras.Values)
            camera.Dispose();
        _cameras.Clear();
        ReturnShadowCameras();
        foreach (CameraFrame camera in _shadowCameraPool)
            camera.Dispose();
        _shadowCameraPool.Clear();
        _providerProbe?.Dispose();
        _graph.Dispose();
        _probeGraph.Dispose();
        _shadowSampler?.Dispose();
        _tileClear?.Dispose();
        _probes.Dispose();
        _deform.Dispose();
        Programs.Dispose();
        foreach (FullscreenProgram program in _present)
            program.Dispose();
        Noise.Dispose();
        SmaaArea.Dispose();
        SmaaSearch.Dispose();
        SmaaAreaLayered.Dispose();
        SmaaSearchLayered.Dispose();
        NoScreenVisibility.Dispose();
        NoScreenIrradiance.Dispose();
        NoSceneColor.Dispose();
        ClearProgram.Dispose();
        CopyBackProgram.Dispose();
        foreach (IDisposable retired in _retired)
            retired.Dispose();
        PrefilterProgram.Dispose();
        SkyAmbientProgram.Dispose();
        TrilinearClamp.Dispose();
        LinearSampler.Dispose();
    }
}

/// <summary>
/// What a frame drew: the views asked for, and the indirect draw calls recorded with the draws inside them (each
/// command one mesh piece, instanced or not), split between camera views and shadow views, and the local light shadows
/// that were given tiles, grew or shrank.
/// </summary>
public sealed record FrameStats(int Views, int DrawCalls, int DrawCommands, int ShadowDrawCalls, int ShadowDrawCommands, int ShadowsAdded = 0, int ShadowsGrown = 0, int ShadowsShrunk = 0);
