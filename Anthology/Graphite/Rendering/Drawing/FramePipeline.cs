using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// The frame: the root passes uploading every change, then the instances of every world and view, all in
/// one execution. Graphs rebuild only when the set of persistent buffers changes.
/// </summary>
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
    private readonly Dictionary<WorldFrame, GraphInstance> _worldInstances = new();
    private readonly Dictionary<GraphKind, FeatureShape> _shapes = new();
    private readonly GraphStore _store;
    private readonly IPass[] _fillPasses;

    internal GraphStore Store => _store;
    private readonly ReadbackTexturePass _readbackTexture = new();
    private readonly ReadbackBufferPass _readbackBuffer = new();
    private readonly List<GraphImport> _imports = new();
    private RenderGraph? _graph;
    private bool _graphDirty = true;
    private readonly List<PersistentBuffer> _buffers = new();
    private readonly List<DeviceBuffer> _importedBuffers = new();
    private readonly List<CameraFrame> _captures = new();
    private readonly List<Texture> _disposeAfterDispatch = new();
    private readonly List<IDisposable> _retired = new();
    private readonly ProviderCountsPass _counts = new();
    private readonly FrameExports _exports = new();
    private readonly Dictionary<FrameFeature, FrameFeaturePass> _frameFeaturePasses = new();
    private readonly List<FrameFeaturePass> _frameFeatures = new();
    private readonly List<FrameFeaturePass> _activeFrameFeatures = new();
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

    public FramePipeline(GraphicsDevice device, GraphStore store, IShaderCompiler? compiler, Uploader uploader, GeometryPool geometry, MaterialSystem materials, GlobalRegistry globals, DrawPrograms programs, RendererTextures textures, bool deterministic, Action<RendererError> report, CompilePolicy policy = CompilePolicy.Async)
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
        _upload = new UploadPass(uploader) { Buffers = Buffers };
        _probes = new ProbePass(InternalShaders.Compute(device, compiler, "Internal/Probes.glsl"));
        _deform = new DeformPass(InternalShaders.Compute(device, compiler, "Internal/Deform.glsl"), geometry) { Buffers = Buffers };
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
        _fillPasses = CaptureFillPasses.Of(this);
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
    public FrameBuffers Buffers { get; } = new();

    /// <summary>Declares the geometry pages and material tables a view's draws bind from the frame's buffers.</summary>
    internal void DeclareDrawReads(InstanceContextBuilder builder)
    {
        List<PersistentBuffer> known = [.. Buffers.Persistent];
        foreach (GeometryPage page in Geometry.Pages)
        {
            if (known.Contains(page.VertexBuffer))
                builder.DeclareInputBuffer(page.VertexBuffer.Id, BufferAccess.ShaderRead);
            if (known.Contains(page.IndexBuffer))
                builder.DeclareInputBuffer(page.IndexBuffer.Id, BufferAccess.Index);
        }
        foreach (MaterialTable table in Materials.Tables.Values)
        {
            if (known.Contains(table.Records.Persistent))
                builder.DeclareInputBuffer(table.Records.Persistent.Id, BufferAccess.ShaderRead);
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
        foreach (FeatureShape shape in _shapes.Values)
        {
            if (shape.LastFrame == _frame && shape.Prune(_frame))
                _graphDirty = true;
        }
        if (_graphDirty)
            BuildGraph();
        ExecutionTask task = _device.DispatchGraph(_graph!, CreateInstances(_graph!), profilers ?? []);
        foreach (Texture texture in _disposeAfterDispatch)
            texture.Dispose();
        _disposeAfterDispatch.Clear();
        ReturnShadowCameras();
        foreach (IDisposable retired in _shadowRetired)
            retired.Dispose();
        _shadowRetired.Clear();
        foreach (IDisposable retired in _retired)
            retired.Dispose();
        _retired.Clear();
        ReleaseStaleCameras();
        ReleaseIdleCaptures();
        ReleaseStaleShapes();
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
        uint width = Math.Max(1u, (uint)MathF.Round(view.Target.Width * view.Viewport.Z * view.RenderScale));
        uint height = Math.Max(1u, (uint)MathF.Round(view.Target.Height * view.Viewport.W * view.RenderScale));
        camera.Info = new FeatureView(view, width, height);
        camera.SelectFeatures(world.World.FeatureSnapshot, features);
        camera.PruneStates(world.World.FeatureSnapshot, features);
        FeatureShape shape = ShapeOf(camera, view);
        camera.ResetPassLists();
        foreach (RenderFeature feature in camera.Selected)
        {
            foreach (PassType type in shape.PassOf(feature).Types)
                camera.ListFor(type);
        }
        ResolveScreenLighting(camera, shape, width, height);
        Prepare(camera, view, world, time, width, height);
        UpdateSceneColor(camera, shape, width, height);
        camera.View.PixelWidth = width;
        camera.View.PixelHeight = height;
        _planned.Add(new PlannedInstance(shape.Kind, view.ViewId, world, new GraphExtent(width, height), shape.Captures ? null : view.Target, camera, view.Name ?? (shape.Captures ? $"Capture {view.ViewId}" : $"Camera {view.ViewId}")));
    }

    private void ReleaseStaleShapes()
    {
        List<GraphKind>? stale = null;
        foreach ((GraphKind kind, FeatureShape shape) in _shapes)
        {
            if (_frame - shape.LastFrame > StaleViewFrames)
                (stale ??= new()).Add(kind);
        }
        foreach (GraphKind kind in stale ?? [])
        {
            _shapes[kind].Dispose();
            _shapes.Remove(kind);
            _graphDirty = true;
        }
    }

    private FeatureShape ShapeOf(CameraFrame camera, in ViewDesc view)
    {
        (CascadeSlot[] cascades, string cascadeKey) = CascadesOf(camera, view, camera.World);
        CameraShape key = CameraShape.Of(view, camera.Selected, camera.HostBuffers.Length, cascadeKey);
        GraphKind kind = RenderKinds.Of(view.Kind, key);
        bool changed = false;
        if (!_shapes.TryGetValue(kind, out FeatureShape? shape))
        {
            _shapes[kind] = shape = new FeatureShape(kind, view.Kind, key, cascades) { Frame = this };
            changed = true;
        }
        shape.LastFrame = _frame;
        camera.Shape = shape;
        foreach (RenderFeature feature in camera.Selected)
        {
            if (shape.Add(feature, _frame) || shape.PassOf(feature).BuiltVersion != feature.Version)
                changed = true;
        }
        if (changed)
        {
            _graphDirty = true;
            DeclareShape(shape);
        }
        return shape;
    }

    private void DeclareShape(FeatureShape shape)
    {
        _exports.Clear();
        RenderContextBuilder frame = new();
        foreach (FrameFeaturePass pass in _frameFeatures)
        {
            frame.Reset();
            pass.Setup(frame);
        }
        InstanceContextBuilder view = new();
        foreach (IInstancePass pass in shape.Passes())
        {
            view.Reset();
            pass.Setup(view);
        }
    }

    private void BuildGraph()
    {
        _exports.Clear();
        List<IPass> passes = [_upload, _probes, _deform, .. _frameFeatures, _counts, _readbackTexture, _readbackBuffer, .. _fillPasses, .. ShadowPasses()];
        List<GraphKind> kinds = [];
        foreach (FeatureShape shape in _shapes.Values)
        {
            passes.AddRange(shape.Passes());
            kinds.Add(shape.Kind);
        }
        _graph = RenderGraph.Build(passes, RenderKinds.Scopes(kinds), "Graphite", _store);
        _graphDirty = false;
    }

    private void ResolveScreenLighting(CameraFrame camera, FeatureShape shape, uint width, uint height)
    {
        if (!shape.Shape.HasScreenLighting)
        {
            camera.ScreenVisibility = camera.ScreenIrradiance = null;
            return;
        }
        GraphInstanceKey key = new(shape.Kind, camera.View.ViewId);
        GraphExtent extent = new(width, height);
        camera.ScreenVisibility = _store.ResolvePersistent(_device, shape.ScreenVisibilityId, shape.ScreenDesc(PixelFormat.R8_UNorm), key, extent);
        camera.ScreenIrradiance = shape.Shape.ScreenIrradiance
            ? _store.ResolvePersistent(_device, shape.ScreenIrradianceId, shape.ScreenDesc(PixelFormat.R16_G16_B16_A16_Float), key, extent)
            : null;
    }

    private GraphInstances CreateInstances(RenderGraph graph)
    {
        GraphInstances instances = graph.CreateInstances(data: this);
        _worldInstances.Clear();
        foreach (WorldFrame world in _frameWorlds)
            _worldInstances[world] = instances.Add(RenderKinds.World, world.World.Id, data: world, name: world.World.Name);
        foreach (PlannedInstance planned in _planned)
        {
            _imports.Clear();
            if (planned.Data is CameraFrame camera)
            {
                FeatureShape shape = _shapes[planned.Kind];
                for (int i = 0; i < camera.HostBuffers.Length; i++)
                    _imports.Add(GraphImport.Of(shape.HostId(i), camera.HostBuffers[i]));
                if (camera.CaptureCube != null)
                    _imports.Add(GraphImport.Of(shape.CaptureId, camera.CaptureCube));
            }
            else if (planned.Data is CaptureFill fill)
            {
                _imports.Add(GraphImport.Of(RenderKinds.FillSource, fill.Source));
                _imports.Add(GraphImport.Of(RenderKinds.FillTarget, fill.Target.Persistent.Texture!));
                if (fill.SkyAmbient != null)
                    _imports.Add(GraphImport.Of(RenderKinds.FillAmbient, fill.SkyAmbient));
            }
            instances.Add(planned.Kind, planned.Id, _worldInstances[planned.World], planned.Extent, planned.Target, planned.Data, planned.Name, _imports);
        }
        for (int i = 0; i < _readback.Requests.Count; i++)
        {
            ReadbackRequest request = _readback.Requests[i];
            GraphImport import = request.Texture is { } texture ? GraphImport.Of(RenderKinds.ReadbackTexture, texture) : GraphImport.Of(RenderKinds.ReadbackBuffer, request.Buffer!);
            instances.Add(RenderKinds.Readback, i, data: request, name: $"Readback {i}", imports: [import]);
        }
        return instances;
    }

    // Whether the view's transparent list reads the scene colour this frame. Returns whether its copy was just made, which
    // adds the copy to the view's graph; the lists keep their order through that rebuild, so what Prepare uploaded holds.
    private void UpdateSceneColor(CameraFrame camera, FeatureShape shape, uint width, uint height)
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
            camera.EnsureSceneColor(width, height, (uint)shape.Shape.Layers, shape.ColorFormat);
    }

    internal FrameTime CurrentTime => _currentTime;

    internal FrameExports Exports => _exports;

    // The frame features that run this frame, in order. A change in who runs or in what a feature declares rebuilds the graph.
    private void PrepareFrameFeatures(IEnumerable<WorldFrame> worlds)
    {
        _activeFrameFeatures.Clear();
        foreach (WorldFrame world in worlds)
        {
            foreach (FrameFeature feature in world.World.FrameFeatureSnapshot)
            {
                if (!feature.EnabledSnapshot || !IsActive(feature))
                    continue;
                if (!_frameFeaturePasses.TryGetValue(feature, out FrameFeaturePass? pass))
                    _frameFeaturePasses[feature] = pass = new FrameFeaturePass(this, feature) { BuiltVersion = -1 };
                _activeFrameFeatures.Add(pass);
            }
        }
        _activeFrameFeatures.Sort((a, b) => a.Feature.OrderSnapshot != b.Feature.OrderSnapshot ? a.Feature.OrderSnapshot.CompareTo(b.Feature.OrderSnapshot) : a.Feature.Id.CompareTo(b.Feature.Id));
        bool changed = _activeFrameFeatures.Count != _frameFeatures.Count;
        for (int i = 0; !changed && i < _activeFrameFeatures.Count; i++)
            changed = _activeFrameFeatures[i] != _frameFeatures[i] || _activeFrameFeatures[i].BuiltVersion != _activeFrameFeatures[i].Feature.Version;
        if (!changed)
            return;
        _frameFeatures.Clear();
        _frameFeatures.AddRange(_activeFrameFeatures);
        _graphDirty = true;
        List<FrameFeature>? gone = null;
        foreach (FrameFeature feature in _frameFeaturePasses.Keys)
        {
            if (!_activeFrameFeatures.Exists(pass => pass.Feature == feature))
                (gone ??= new()).Add(feature);
        }
        foreach (FrameFeature feature in gone ?? [])
            _frameFeaturePasses.Remove(feature);
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

    // The copies of indirect items' instance counts into the views' commands, the graph built again when what they touch changes
    private void PlanCounts()
    {
        _counts.Copies.Clear();
        foreach (CameraFrame camera in Active())
        {
            if (camera.LastFrame != _frame)
                continue;
            foreach ((RenderResourceID source, uint offset, uint destination) in camera.Counts)
                _counts.Copies.Add((source, offset, camera.Commands.Persistent, destination));
        }
        _counts.Plan();
        if (_counts.Changed())
            _graphDirty = true;
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

    // Holds the draw program of the environment's sky material while it has a Sky pass
    private void UpdateSkyProgram(WorldFrame world)
    {
        ushort? program = null;
        if (world.World.Lighting.Environment.Sky is { IsDisposed: false, Table: not null } sky && DrawPrograms.TryGet(sky.Draw, PassType.Sky, out ushort id))
            program = id;
        if (program == world.SkyProgram)
            return;
        if (program is ushort acquired)
            DrawPrograms.Acquire(acquired);
        if (world.SkyProgram is ushort released)
            DrawPrograms.Release(released);
        world.SkyProgram = program;
    }

    private void CaptureSky(WorldFrame world, in FrameTime time)
    {
        WorldReflections reflections = world.World.Reflections;
        world.SkyCamera ??= Capturing();
        WorldReflections.Scratch scratch = reflections.SkyScratch!;
        if (world.SkyProgram is ushort sky && !Programs.Ready(sky, false, world.World.Globals.KeywordSnapshot))
            reflections.SkyIncomplete();

        ViewDesc capture = new()
        {
            World = world.World,
            Kind = ViewKind.SkyCapture,
            ViewId = world.SkyCamera.View.ViewId,
            LayerCount = 6,
            Target = scratch.All,
            CullingMask = 0,
            ClearColor = world.World.Lighting.Environment.SkyColor,
            Near = 0.05f,
        };
        for (int face = 0; face < 6; face++)
            capture.Layers[face] = new ViewLayer { View = WorldReflections.FaceView(face), Projection = WorldReflections.FaceProjection };
        world.SkyCamera.CaptureCube = scratch.AllTexture;
        Draw(world.SkyCamera, capture, [], world, time);
        Fill(world, reflections.Sky!, scratch, null, reflections.SkyAmbient);
    }

    private void Fill(WorldFrame world, WorldReflections.Target target, WorldReflections.Scratch scratch, DeviceBuffer? readback, DeviceBuffer? ambient)
    {
        CaptureFill fill = new() { Source = scratch.AllTexture, Target = target, Samples = world.World.Reflections.PrefilterSamples, Readback = readback, SkyAmbient = ambient };
        _planned.Add(new PlannedInstance(RenderKinds.CaptureFill, _planned.Count, world, new GraphExtent(target.Resolution, target.Resolution), null, fill, $"Capture fill {_planned.Count}"));
    }

    private void CaptureProbe(WorldFrame world, ProbeCapture work, in FrameTime time)
    {
        WorldReflections reflections = world.World.Reflections;
        ReflectionProbeDesc probe = reflections.Probe(work.Index);
        WorldReflections.Target target = reflections.ProbeTarget(work.Index);
        WorldReflections.Scratch scratch = work.Scratch;
        Float4x4 projection = WorldReflections.FaceProjectionBetween(probe.NearClip, probe.FarClip);
        if (world.SkyProgram is ushort sky && !Programs.Ready(sky, false, world.World.Globals.KeywordSnapshot) && !Programs.Failed(sky, false, world.World.Globals.KeywordSnapshot))
            reflections.Request(work.Index);
        int faces = work.EndFace - work.FirstFace;
        if (faces == 6)
        {
            CameraFrame camera = Pooled(world.ProbeCameras, ref world.ProbeCamerasUsed);
            ViewDesc capture = new()
            {
                World = world.World,
                Kind = ViewKind.ReflectionProbe,
                ViewId = camera.View.ViewId,
                LayerCount = 6,
                Target = scratch.All,
                CullingMask = probe.CullingMask,
                ClearColor = world.World.Lighting.Environment.SkyColor,
                Near = probe.NearClip,
                FarDistance = probe.FarClip,
            };
            for (int face = 0; face < 6; face++)
                capture.Layers[face] = new ViewLayer { View = WorldReflections.FaceView(face), Projection = projection, Position = probe.Position };
            camera.CaptureCube = scratch.AllTexture;
            Draw(camera, capture, [], world, time);
        }
        else if (faces == 1)
        {
            CameraFrame camera = Pooled(world.ProbeFaceCameras, ref world.ProbeFaceCamerasUsed);
            ViewDesc capture = new()
            {
                World = world.World,
                Kind = ViewKind.ReflectionProbe,
                ViewId = camera.View.ViewId,
                Target = scratch.Faces[work.FirstFace],
                CullingMask = probe.CullingMask,
                ClearColor = world.World.Lighting.Environment.SkyColor,
                Near = probe.NearClip,
                FarDistance = probe.FarClip,
            };
            capture.Layers[0] = new ViewLayer { View = WorldReflections.FaceView(work.FirstFace), Projection = projection, Position = probe.Position };
            camera.CaptureCube = scratch.FaceTextures[work.FirstFace];
            Draw(camera, capture, [], world, time);
        }

        if (work.Prefilter)
            Fill(world, target, scratch, work.Readback, null);
    }

    private CameraFrame Capturing()
    {
        CameraFrame camera = new(_device, this, WorldFrame.NextCaptureViewId());
        _textures.TrackView(camera.ViewTable);
        return camera;
    }

    private CameraFrame Pooled(List<CameraFrame> pool, ref int used)
    {
        if (used == pool.Count)
            pool.Add(Capturing());
        return pool[used++];
    }

    // Capture views keep their textures while drawing now and then, and let them go once unused for a while
    private void ReleaseIdleCaptures()
    {
        foreach (WorldFrame world in _frameWorlds)
        {
            foreach (CameraFrame camera in world.Captures())
            {
                if (_frame - camera.LastFrame > StaleViewFrames)
                    camera.ReleaseSceneColor();
            }
        }
    }

    /// <summary>Releases what the frame pipeline keeps for a removed world. Render thread idle.</summary>
    public void ForgetWorld(WorldFrame world)
    {
        world.Programs.Clear();
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
        camera.RefreshBuffers();
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

    private void CollectBuffers(IEnumerable<WorldFrame> worlds)
    {
        _captures.Clear();
        foreach (WorldFrame world in worlds)
            _captures.AddRange(world.Captures());
        _buffers.Clear();
        _importedBuffers.Clear();
        _buffers.Add(Geometry.Chunks.Persistent);
        _buffers.Add(Geometry.Meshes.Persistent);
        foreach (GeometryPage page in Geometry.Pages)
        {
            _buffers.Add(page.VertexBuffer);
            _buffers.Add(page.IndexBuffer);
        }
        foreach (MaterialTable table in Materials.Tables.Values)
            _buffers.Add(table.Records.Persistent);
        _buffers.Add(Globals.Persistent);
        _probes.Worlds.Clear();
        _deform.Worlds.Clear();
        foreach (WorldFrame world in worlds)
        {
            RenderWorld.RenderSide render = world.World.Render;
            _deform.Worlds.Add(render);
            WorldProbes probes = world.World.Probes;
            _probes.Worlds.Add(probes);
            _buffers.Add(probes.ProbeRecords.Persistent);
            _buffers.Add(probes.TetRecords.Persistent);
            _buffers.Add(probes.Requests.Persistent);
            _buffers.Add(probes.ObjectShBuffer);
            foreach (SpatialIndex set in world.World.SpatialSets)
            {
                if (set.Slot < SpatialIndex.ProbesSet)
                    continue;
                _buffers.Add(set.StaticRecords.Persistent);
                _buffers.Add(set.StaticNodes.Persistent);
            }
            _importedBuffers.Add(world.World.Reflections.SkyAmbient);

            foreach (GpuTable table in render.Tables)
                _buffers.Add(table.Persistent);
            WorldLighting lighting = world.World.Lighting;
            foreach (GpuTable table in lighting.Tables)
                _buffers.Add(table.Persistent);
        }
        foreach (WorldFrame world in worlds)
        {
            if (world.Shadows != null)
                _buffers.Add(world.Shadows.Faces.Persistent);
        }
        foreach (CameraFrame camera in Active())
            camera.AddBuffers(_buffers);

        if (Same(Buffers, _buffers, _importedBuffers))
            return;

        Buffers.Persistent = [.. _buffers];
        Buffers.Sizes = new uint[_buffers.Count];
        for (int i = 0; i < _buffers.Count; i++)
            Buffers.Sizes[i] = _buffers[i].Description.SizeInBytes;
        Buffers.Imported = [.. _importedBuffers];
        _graphDirty = true;
    }

    private static bool Same(FrameBuffers current, List<PersistentBuffer> buffers, List<DeviceBuffer> imports)
    {
        if (current.Persistent.Length != buffers.Count || current.Imported.Length != imports.Count)
            return false;
        for (int i = 0; i < buffers.Count; i++)
        {
            if (current.Persistent[i] != buffers[i] || buffers[i].Description.SizeInBytes != current.Sizes[i])
                return false;
        }
        for (int i = 0; i < imports.Count; i++)
        {
            if (current.Imported[i] != imports[i])
                return false;
        }
        return true;
    }

    // Per view state is released after a view has not been submitted for a few frames.
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
        _graph?.Dispose();
        foreach (FeatureShape shape in _shapes.Values)
            shape.Dispose();
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

/// <summary>A copy to the host the renderer records after a frame's views.</summary>
internal sealed class ReadbackRequest
{
    public Texture? Texture;
    public TextureRegion Region;
    public DeviceBuffer? Buffer;
    public uint Offset, Size;
    public Action<TextureReadback>? TextureDone;
    public Action<BufferReadback>? BufferDone;
    public DeviceBuffer? Staging;
    public ulong Execution;
    public int Attempts;
    public string? Error;
}

/// <summary>Makes the staging buffers of a frame's readbacks and drops the requests whose source was disposed.</summary>
internal sealed class ReadbackStaging(GraphicsDevice device, Action<RendererError> report)
{
    public readonly List<ReadbackRequest> Requests = new();

    public void Prepare(List<ReadbackRequest> requests)
    {
        Requests.Clear();
        for (int i = requests.Count - 1; i >= 0; i--)
        {
            ReadbackRequest request = requests[i];
            if (request.Texture?.IsDisposed == true || request.Buffer?.IsDisposed == true)
            {
                request.Error = "The texture or buffer was disposed before it was copied.";
                report(new RendererError("readback.disposed", "A readback's texture or buffer was disposed before it was copied.", RendererErrorSeverity.Warning, (object?)request.Texture ?? request.Buffer));
                continue;
            }
            uint bytes = request.Texture is { } texture ? request.Region.Width * request.Region.Height * request.Region.Depth * texture.Format.GetSizeInBytes() : request.Size;
            request.Staging ??= device.ResourceFactory.CreateBuffer(new BufferDescription(bytes, BufferUsage.Staging));
            Requests.Add(request);
        }
    }
}

/// <summary>Copies the texture a readback instance binds into its staging buffer.</summary>
internal sealed class ReadbackTexturePass : IInstancePass
{
    private TextureHandle _source;

    public string Name => "Graphite.ReadbackTexture";

    public GraphKind Kind => RenderKinds.Readback;

    public bool IsEnabled(GraphInstance instance) => instance.Data is ReadbackRequest { Texture: not null };

    public void Setup(InstanceContextBuilder builder) => _source = builder.DeclareImportSlotTexture(RenderKinds.ReadbackTexture, TextureState.TransferSrc);

    public void Render(InstanceContext context, CommandBuffer cmd)
    {
        ReadbackRequest request = (ReadbackRequest)context.Instance.Data!;
        cmd.CopyTextureToBuffer(context.GetRenderTexture(_source).ColorTextures[0], request.Staging!, 0, request.Region);
    }
}

/// <summary>Copies the buffer a readback instance binds into its staging buffer.</summary>
internal sealed class ReadbackBufferPass : IInstancePass
{
    private BufferHandle _source;

    public string Name => "Graphite.ReadbackBuffer";

    public GraphKind Kind => RenderKinds.Readback;

    public bool IsEnabled(GraphInstance instance) => instance.Data is ReadbackRequest { Buffer: not null };

    public void Setup(InstanceContextBuilder builder) => _source = builder.DeclareImportSlotBuffer(RenderKinds.ReadbackBuffer, BufferAccess.TransferRead);

    public void Render(InstanceContext context, CommandBuffer cmd)
    {
        ReadbackRequest request = (ReadbackRequest)context.Instance.Data!;
        cmd.CopyBuffer(context.GetRenderBuffer(_source), request.Offset, request.Staging!, 0, request.Size);
    }
}

/// <summary>
/// What a frame drew: the views asked for, and the indirect draw calls recorded with the draws inside them (each
/// command one mesh piece, instanced or not), split between camera views and shadow views, and the local light shadows
/// that were given tiles, grew or shrank.
/// </summary>
public sealed record FrameStats(int Views, int DrawCalls, int DrawCommands, int ShadowDrawCalls, int ShadowDrawCommands, int ShadowsAdded = 0, int ShadowsGrown = 0, int ShadowsShrunk = 0);
