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

    /// <summary>The hardware slope offset every shadow caster draws with.</summary>
    public float ShadowSlopeBias = 1.5f;

    /// <summary>The hardware constant offset every shadow caster draws with, in depth steps.</summary>
    public float ShadowConstantBias = 2;

    /// <summary>The depth format of shadow maps, D16_UNorm (half the memory) or D32_Float.</summary>
    public PixelFormat ShadowFormat = PixelFormat.D16_UNorm;

    /// <summary>Words of vertex data per geometry page.</summary>
    public uint PageVertexWords = GeometryPool.DefaultPageVertexWords;

    /// <summary>Indices per geometry page.</summary>
    public uint PageIndices = GeometryPool.DefaultPageIndices;

    /// <summary>
    /// When shader variants compile. Sync with Inline threading never draws a stand in, for golden images and tests.
    /// Players shipping only baked shaders pass a <see cref="BakedVariants"/> without a compiler as the compiler and use BakedOnly.
    /// </summary>
    public CompilePolicy Compile = CompilePolicy.Async;

    /// <summary>Format of reflection probe and sky probe cubes. B10G11R11 halves their memory, alpha is never used.</summary>
    public PixelFormat ReflectionFormat = PixelFormat.R16_G16_B16_A16_Float;

    /// <summary>GGX samples per prefiltered texel of every mip but the sharpest.</summary>
    public int PrefilterSamples = 64;

    /// <summary>Edge length of the smallest prefiltered mip, rougher reflections turn blocky below it.</summary>
    public uint SmallestProbeMip = 4;

    /// <summary>
    /// Texels of reflection probe captures drawn per world per frame: each face's texels, plus every prefiltered mip's when
    /// a probe filters. The default fits one 128 probe with its filter. The first capture of a frame always runs.
    /// </summary>
    public long ProbeTexelsPerFrame = 256 * 1024;
}

/// <summary>
/// The skies Graphite ships, loaded the first time they are asked for: the procedural sky lit by the world's sun
/// (_SunSize, _SunGlow, intensity, _Tint, _GroundColor), a two colour gradient (_TopColor, _BottomColor, _Exponent), a
/// cube texture (_Cubemap), an equirectangular panorama (_Panorama), both with _Tint, intensity and _Rotation in degrees,
/// and a cube from six images (_CubeRight, _CubeLeft, _CubeTop, _CubeBottom, _CubeFront, _CubeBack, _Tint, intensity).
/// Each is a Surface "Sky" shader, see Graphite/Standard/Sky.shader.
/// </summary>
public sealed class BuiltInSkies(Renderer renderer)
{
    private Shader? _procedural, _gradient, _cubemap, _panorama, _sixSided;

    public Shader Procedural => _procedural ??= Load("Procedural");
    public Shader Gradient => _gradient ??= Load("Gradient");
    public Shader Cubemap => _cubemap ??= Load("Cubemap");
    public Shader Panorama => _panorama ??= Load("Panorama");
    public Shader SixSided => _sixSided ??= Load("SixSided");

    private Shader Load(string name) => renderer.LoadShader(ShaderParser.Parse(ShaderLibrary.Get($"{ShaderLibrary.Prefix}Skies/{name}.shader")!));
}

/// <summary>
/// The standard surface shaders Graphite ships, loaded the first time they are asked for: Lit, OpenPBR Surface with a
/// map for every texturable input, and Unlit. Both take <see cref="SurfaceSettings"/>.
/// </summary>
public sealed class BuiltInStandard(Renderer renderer)
{
    private Shader? _lit, _unlit;

    public Shader Lit => _lit ??= Load("Lit");
    public Shader Unlit => _unlit ??= Load("Unlit");

    private Shader Load(string name) => renderer.LoadShader(ShaderParser.Parse(ShaderLibrary.Get($"{ShaderLibrary.Prefix}Standard/{name}.shader")!));
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
public readonly record struct RendererError(string Code, string Message, RendererErrorSeverity Severity = RendererErrorSeverity.Error, object? Source = null)
{
    /// <summary>What the error is reported once per, the code when null.</summary>
    internal string? Key { get; init; }

    public bool Equals(RendererError other) => Code == other.Code && Message == other.Message && Severity == other.Severity && Equals(Source, other.Source);

    public override int GetHashCode() => HashCode.Combine(Code, Message, Severity, Source);
}

public struct FrameTime
{
    public double Time;
    public float DeltaTime;
    public ulong FrameIndex;
}

public struct FrameDesc
{
    public ReadOnlyMemory<RenderView> Views;
    public FrameTime Time;

    /// <summary>Runs on the render thread before the frame is prepared, for host work its submission has to follow.</summary>
    public Action? Started;

    /// <summary>Profilers the frame's execution reports its passes to, such as GPU timers.</summary>
    public IProfiler[]? Profilers;

    /// <summary>
    /// Runs on the render thread once the frame's command buffers are on the graphics queue, with false when the frame
    /// failed. Always follows <see cref="Started"/>.
    /// </summary>
    public Action<bool>? Submitted;
}

/// <summary>A texture region read back to the host: tightly packed rows of its format, or why it could not be read.</summary>
public readonly record struct TextureReadback(byte[] Data, uint Width, uint Height, uint Depth, PixelFormat Format, string? Error = null)
{
    public bool Failed => Error != null;
}

/// <summary>A buffer range read back to the host, or why it could not be read.</summary>
public readonly record struct BufferReadback(byte[] Data, string? Error = null)
{
    public bool Failed => Error != null;
}

/// <summary>
/// Graphite's high level renderer. The host mutates worlds, materials and globals on its thread and calls
/// <see cref="SubmitFrame"/>, which extracts what changed and hands the frame to the render thread. Resources are
/// created from any thread and become resident on a later frame.
/// </summary>
public sealed class Renderer : IDisposable
{
    private readonly RendererOptions _options;
    private readonly IShaderCompiler? _compiler;
    private readonly Uploader _uploader;
    private readonly GeometryPool _geometry;
    private readonly MaterialSystem _materials;
    private readonly GlobalRegistry _globals;
    private readonly DrawPrograms _programs = new();

    // Keeps the depth caster's material table, which every material casting through it draws with
    private readonly Material _depthCasterMaterial;
    private readonly FramePipeline _frame;

    /// <summary>The frame pipeline, for tests.</summary>
    internal FramePipeline Frame => _frame;

    /// <summary>A world's local light shadows, for tests.</summary>
    internal WorldShadows? ShadowsOf(RenderWorld world) => _worlds[world].Shadows;
    private readonly Dictionary<RenderWorld, WorldFrame> _worlds = new();
    private readonly List<RenderWorld> _removedWorlds = new();

    private readonly ConcurrentQueue<RendererError> _errors = new();
    private readonly ConcurrentDictionary<string, bool> _reported = new(StringComparer.Ordinal);

    private readonly List<Material> _changedMaterials = new();
    private readonly List<Material> _shadowMaterials = new();
    private readonly List<GpuMesh> _changedMeshes = new();
    private readonly RenderStore _store;
    private ViewDesc[] _views = [];
    private FrameTime _time;
    private int _viewCount;
    private ulong _lastDispatched;

    private readonly Thread? _thread;
    private readonly SemaphoreSlim _frameQueued = new(0, 1);
    private readonly SemaphoreSlim _frameDone = new(1, 1);
    private volatile bool _stopping;
    private bool _disposed;

    /// <summary>Frame features that are not per world, with the built in Deform and Readback. Host thread.</summary>
    public ExtensionSet<FrameFeature> FrameFeatures { get; } = new(null);

    public Renderer(GraphicsDevice device, IShaderCompiler? compiler, RendererOptions? options = null)
    {
        Device = device;
        _compiler = compiler;
        _options = options ?? new RendererOptions();
        if (Unsupported(device) is { } unsupported)
            throw new NotSupportedException($"The renderer cannot run on '{device.DeviceName}': {unsupported}.");
        if (_options.PrefilterSamples < 1)
            throw new ArgumentException($"PrefilterSamples is {_options.PrefilterSamples}, it must be at least 1.", nameof(options));
        if (_options.SmallestProbeMip is < 1 or > 2048 || !uint.IsPow2(_options.SmallestProbeMip))
            throw new ArgumentException($"SmallestProbeMip is {_options.SmallestProbeMip}, it must be a power of two from 1 to 2048.", nameof(options));
        InternalShaders.Register();
        _fallbackShader = new Shader(ShaderParser.Parse(ShaderLibrary.Get($"{ShaderLibrary.Prefix}Fallback.shader")!), _compiler);
        if (_options.Compile == CompilePolicy.Async)
            CompileFallback(CompilePolicy.Async);

        _uploader = new Uploader(device, InternalShaders.Compute(device, compiler, "Internal/Scatter.glsl", _options.Compile));
        Resources = new RendererResources(device, compiler, _options.Compile);
        Textures = Resources.Textures;
        Textures.Reporter = Report;
        _store = new RenderStore();
        _geometry = new GeometryPool(device, _store, _options.PageVertexWords, _options.PageIndices);
        _materials = new MaterialSystem(device, _store, Textures) { Reporter = Report };
        _globals = new GlobalRegistry(device, _store, Textures) { Reporter = Report };
        _programs.Reporter = Report;
        _frame = new FramePipeline(device, _store, compiler, _uploader, _geometry, _materials, _globals, _programs, Resources, Report, _options.Compile) { ShadowFormat = _options.ShadowFormat };
        FrameFeatures.Add(new DeformFeature());
        FrameFeatures.Add(new ReadbackFeature());
        _frame.Programs.ShadowBias = (_options.ShadowConstantBias, _options.ShadowSlopeBias);
        _frame.ChangedMaterials = _shadowMaterials;
        Skies = new BuiltInSkies(this);
        Standard = new BuiltInStandard(this);
        _programs.DepthCaster = LoadShader(ShaderParser.Parse(ShaderLibrary.Get($"{ShaderLibrary.Prefix}DepthCaster.shader")!));
        _depthCasterMaterial = CreateMaterial(_programs.DepthCaster);
        CompileFallback(CompilePolicy.Sync);

        if (_options.Threading == RenderThreading.RenderThread)
        {
            _thread = new Thread(RenderLoop) { Name = "Graphite Render", IsBackground = true };
            _thread.Start();
        }
    }

    private static string? Unsupported(GraphicsDevice device)
        => Unsupported(device.Features.DrawIndirectBaseInstance, device.Features.DescriptorIndexing, device.Limits);

    /// <summary>Why the renderer cannot run on a device with these features and limits, or null when it can.</summary>
    internal static string? Unsupported(bool drawIndirectBaseInstance, bool descriptorIndexing, GraphicsDeviceLimits limits)
    {
        if (!drawIndirectBaseInstance)
            return "indirect draws need a first instance, which carries each draw's record";
        if (!descriptorIndexing)
            return "the bindless tables need descriptor indexing";

        uint varyings = (GraphiteBindings.MaterialSlotLocation + 1) * 4;
        if (limits.MaxVertexOutputComponents < varyings || limits.MaxFragmentInputComponents < varyings)
            return $"material varyings reach location {GraphiteBindings.MaterialSlotLocation}, which needs {varyings} vertex output and fragment input components, the device has {limits.MaxVertexOutputComponents} and {limits.MaxFragmentInputComponents}";

        const ShaderStages Graphics = ShaderStages.Vertex | ShaderStages.Fragment;
        ResourceLayoutDescription pass = new(GraphiteBindings.PassSet,
            new ResourceLayoutElementDescription(GraphiteBindings.MaterialsName, ResourceKind.StructuredBufferReadOnly, Graphics, GraphiteBindings.Materials),
            new ResourceLayoutElementDescription(GraphiteBindings.InstancesName, ResourceKind.StructuredBufferReadOnly, Graphics, GraphiteBindings.Instances),
            new ResourceLayoutElementDescription(GraphiteBindings.DrawRecordsName, ResourceKind.StructuredBufferReadOnly, Graphics, GraphiteBindings.DrawRecords),
            new ResourceLayoutElementDescription(GraphiteBindings.VerticesName, ResourceKind.StructuredBufferReadOnly, Graphics, GraphiteBindings.Vertices));
        return limits.Exceeded([ProgramCache.FrameLayout, ProgramCache.ViewLayout, pass]) is { } exceeded
            ? $"a material program exceeds the descriptor limits, {exceeded}"
            : null;
    }

    public GraphicsDevice Device { get; }

    public RendererTextures Textures { get; }

    /// <summary>The GPU resources the frames, views and effects share.</summary>
    internal RendererResources Resources { get; }

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
            RenderWorld world = new(Device, _store, this, Textures, new ReflectionSettings(_options.ReflectionFormat, _options.PrefilterSamples, _options.SmallestProbeMip, _options.ProbeTexelsPerFrame));
            (GlobalProperties globals, int region) = _globals.AddWorld();
            world.Globals = globals;
            _worlds[world] = new WorldFrame(world, new BucketSet(world.Render, _programs), region, _programs);
            return world;
        }
        finally
        {
            _frameDone.Release();
        }
    }

    internal void RemoveWorld(RenderWorld world) => _removedWorlds.Add(world);

    /// <summary>A new view of a world, drawn in the frames it is submitted in until disposed. Host thread.</summary>
    public RenderView CreateView(RenderWorld world, string? name = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(world);
        return new RenderView(this, _nextViewId++, world, name);
    }

    internal void ReleaseView(RenderView view)
    {
        lock (_releasedViews)
            _releasedViews.Add(view.Id);
    }

    private int _nextViewId;
    private readonly List<int> _releasedViews = new();
    private readonly List<int> _frameReleased = new();

    /// <summary>Creates a mesh. Any thread, resident on a later frame.</summary>
    public GpuMesh CreateMesh(MeshData data) => _geometry.Create(data);

    /// <summary>Creates a material. Any thread, setters are host thread only.</summary>
    public Material CreateMaterial(Shader shader) => _materials.Create(shader);

    /// <summary>The built in skies, each a shader for a material to set as <see cref="EnvironmentDesc.Sky"/>.</summary>
    public BuiltInSkies Skies { get; }

    public BuiltInStandard Standard { get; }

    /// <summary>A prefiltered cube from a bake, to show through <see cref="ReflectionProbeDesc.Cubemap"/>. The caller owns it. Any thread.</summary>
    public Texture CreateReflectionCubemap(BakedReflectionProbe baked)
    {
        ArgumentNullException.ThrowIfNull(baked);
        if (baked.Size <= 0 || baked.Mips <= 0 || baked.Faces.Length < 6 * baked.Mips)
            throw new ArgumentException("A baked reflection probe needs a size, mips and every face of every mip.", nameof(baked));

        uint texel = baked.Format.GetSizeInBytes();
        Texture cube = Device.ResourceFactory.CreateTexture(TextureDescription.CreateCube((uint)baked.Size, (uint)baked.Mips, 1, baked.Format, TextureUsage.Sampled));
        cube.Name = "Baked reflection probe";
        for (int face = 0; face < 6; face++)
        {
            for (int mip = 0; mip < baked.Mips; mip++)
            {
                uint size = (uint)Math.Max(baked.Size >> mip, 1);
                byte[] data = baked.Faces[face * baked.Mips + mip];
                if (data.Length != size * size * texel)
                {
                    cube.Dispose();
                    throw new ArgumentException($"Face {face} mip {mip} holds {data.Length} bytes, {size * size * texel} expected.", nameof(baked));
                }
                Device.UpdateTexture<byte>(cube, data, new TextureRegion(0, 0, 0, size, size, 1, (uint)mip, (uint)face));
            }
        }
        return cube;
    }

    /// <summary>
    /// A shader for materials, loaded through the renderer's compiler. A shader naming no Fallback, or "Graphite/Fallback",
    /// draws flat grey in its place while it compiles. Any thread.
    /// </summary>
    public Shader LoadShader(ShaderDefinition definition)
    {
        Shader shader = new(definition, _compiler);
        if (string.IsNullOrEmpty(shader.Definition.Fallback) || shader.Definition.Fallback == FallbackShaderName)
            shader.Fallback = _fallbackShader;
        return shader;
    }

    public const string FallbackShaderName = "Graphite/Fallback";
    private readonly Shader _fallbackShader;

    // The grey stand in compiles before the first frame, so every frame has something to draw
    private void CompileFallback(CompilePolicy policy)
    {
        if (_compiler == null || _options.Compile == CompilePolicy.BakedOnly)
            return;
        foreach (ShaderPass pass in _fallbackShader.Definition.Passes ?? [])
            pass.Variants(_compiler, Device.BackendType).Request(0, policy, CompilePriority.Fallback, out _, out _);
    }

    /// <summary>
    /// Starts compiling the variants a material draws with at background priority, so they are ready before it first
    /// draws. Global axes take their value from the keywords, else their first value. Debug views compile on demand. Host thread.
    /// </summary>
    public void Prewarm(Material material, params ReadOnlySpan<GlobalKeyword> keywords)
    {
        if (_compiler == null || _options.Compile == CompilePolicy.BakedOnly)
            return;
        MaterialDraw draw = material.SnapshotDraw();
        Shader shader = draw.Shader;
        Keyword[] combo = new Keyword[draw.Keywords.Length + shader.GlobalAxes.Count];
        draw.Keywords.CopyTo(combo, 0);
        for (int i = 0; i < shader.GlobalAxes.Count; i++)
        {
            VariantSpace axis = shader.GlobalAxes[i];
            combo[draw.Keywords.Length + i] = new Keyword(axis.Name, ValueIn(keywords, axis.Name) ?? axis.Values[0]);
        }
        foreach (ShaderPass pass in shader.Definition.Passes ?? [])
        {
            if (ShaderDefinition.PassHasTag(pass, PassType.Tag, PassType.Debug.Name))
                continue;
            PassVariants variants = pass.Variants(_compiler, Device.BackendType);
            variants.Request(variants.KeyOf(combo), CompilePolicy.Async, CompilePriority.Background, out _, out _);
        }
    }

    private static string? ValueIn(ReadOnlySpan<GlobalKeyword> keywords, string axis)
    {
        foreach (GlobalKeyword keyword in keywords)
        {
            if (keyword.Axis?.Name == axis)
                return keyword.Value;
        }
        return null;
    }

    /// <summary>
    /// Extracts every change since the last frame and queues the frame. Waits for the render thread to finish the
    /// previous frame's CPU work first, so at most one frame is being prepared while the host builds the next. Host thread.
    /// </summary>
    public void SubmitFrame(in FrameDesc frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ReadOnlySpan<RenderView> views = frame.Views.Span;
        for (int i = 0; i < views.Length; i++)
        {
            RenderView view = views[i];
            ArgumentNullException.ThrowIfNull(view);
            ObjectDisposedException.ThrowIf(view.IsDisposed, view);
            string name = view.Name ?? $"{i}";
            if (view.Target == null)
                throw new ArgumentException($"View {name} has no target.");
            byte layers = view.Camera.LayerCount;
            if (layers < 1 || layers > 6)
                throw new ArgumentException($"View {name} has {layers} layers, a view has 1 to 6.");
            if (Math.Max(view.Target.ViewCount, 1u) != layers)
                throw new ArgumentException($"View {name} has {layers} layers but its target has {Math.Max(view.Target.ViewCount, 1u)} views.");
            if (!(view.RenderScale > 0) || !float.IsFinite(view.RenderScale))
                throw new ArgumentException($"View {name} has a render scale of {view.RenderScale}, it must be above 0.");
            Float4 rect = view.Viewport;
            if (!(rect.X >= 0 && rect.Y >= 0 && rect.Z > 0 && rect.W > 0 && rect.X + rect.Z <= 1.0001f && rect.Y + rect.W <= 1.0001f))
                throw new ArgumentException($"View {name} has the viewport {rect}, which must lie within 0 to 1 with a size above 0.");
            for (int j = 0; j < i; j++)
            {
                if (views[j] == view)
                    throw new ArgumentException($"View {name} is submitted twice in one frame.");
            }
        }

        RaiseErrors();
        _frameDone.Wait();
        bool queued = false;
        try
        {
            RaiseBakes();
            RaiseReadbacks();
            RemoveWorlds();

            if (Textures.Sweep(_lastDispatched, Device.LastCompletedExecutionId, _freedTextures))
            {
                HashSet<Texture>? freed = Textures.Overflowed ? null : _freedTextures;
                Textures.Overflowed = false;
                _materials.MarkAllRecords();
                _globals.MarkAllDirty();
                foreach (WorldFrame world in _worlds.Values)
                {
                    world.World.Lighting.RepackDecals(freed);
                    world.World.Lighting.RepackLights(freed);
                    world.World.RepackLightmaps(freed);
                    world.World.Reflections.RepackBaked(freed);
                }
            }

            _changedMaterials.Clear();
            _shadowMaterials.Clear();
            _materials.Extract(_changedMaterials, _shadowMaterials);
            _shadowMaterials.AddRange(_changedMaterials);
            _globals.Extract();
            foreach (WorldFrame world in _worlds.Values)
                world.World.Extract();

            if (_views.Length < views.Length)
            {
                _views = new ViewDesc[views.Length];
                _viewFeatures = new RenderFeature[views.Length][];
            }
            for (int v = 0; v < views.Length; v++)
                _views[v] = views[v].Snapshot();
            _viewCount = views.Length;
            _frameReleased.Clear();
            lock (_releasedViews)
            {
                _frameReleased.AddRange(_releasedViews);
                _releasedViews.Clear();
            }
            ExtractFeatures(_views.AsSpan(0, _viewCount));
            _started = frame.Started;
            _submitted = frame.Submitted;
            _profilers = frame.Profilers ?? [];
            _frameReadbacks.Clear();
            while (_readbacks.TryDequeue(out ReadbackRequest? request))
                _frameReadbacks.Add(request);
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

    private RenderFeature[][] _viewFeatures = [];

    /// <summary>The slots of host spatial sets by name, the same in every world.</summary>
    internal SpatialSlots SpatialSlots { get; } = new();
    private Action? _started;
    private Action<bool>? _submitted;
    private IProfiler[] _profilers = [];
    private readonly ConcurrentQueue<ReadbackRequest> _readbacks = new();
    private readonly List<ReadbackRequest> _frameReadbacks = new();
    private readonly List<ReadbackRequest> _pendingReadbacks = new();

    /// <summary>
    /// Copies a texture region to the host after this frame's views, calling <paramref name="done"/> on the host thread
    /// at a later SubmitFrame, with an error when it could not be read. The whole mip when no region is given. Compressed
    /// and depth stencil formats cannot be read back. Host thread.
    /// </summary>
    public void Readback(Texture texture, Action<TextureReadback> done, uint mip = 0, uint layer = 0, TextureRegion? region = null)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(done);
        string format = texture.Format.ToString();
        if (format.StartsWith("BC", StringComparison.Ordinal) || format.StartsWith("ETC", StringComparison.Ordinal) || texture.Format is PixelFormat.D24_UNorm_S8_UInt or PixelFormat.D32_Float_S8_UInt)
            throw new ArgumentException($"A {texture.Format} texture cannot be read back.", nameof(texture));
        uint layers = texture.ArrayLayers * (texture.Type == TextureType.TextureCube ? 6u : 1u);
        TextureRegion r = region ?? new TextureRegion(0, 0, 0, Math.Max(texture.Width >> (int)mip, 1), Math.Max(texture.Height >> (int)mip, 1), Math.Max(texture.Depth >> (int)mip, 1), mip, layer);
        if (r.MipLevel >= texture.MipLevels || r.ArrayLayer >= layers)
            throw new ArgumentOutOfRangeException(nameof(region), $"The texture has {texture.MipLevels} mips and {layers} layers.");
        uint w = Math.Max(texture.Width >> (int)r.MipLevel, 1), h = Math.Max(texture.Height >> (int)r.MipLevel, 1), d = Math.Max(texture.Depth >> (int)r.MipLevel, 1);
        if (r.Width == 0 || r.Height == 0 || r.Depth == 0 || (long)r.X + r.Width > w || (long)r.Y + r.Height > h || (long)r.Z + r.Depth > d)
            throw new ArgumentOutOfRangeException(nameof(region), $"The region does not lie within the {w} by {h} by {d} mip.");
        if ((long)r.Width * r.Height * r.Depth * texture.Format.GetSizeInBytes() > uint.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(region), "The region is too large to read back at once.");
        _readbacks.Enqueue(new ReadbackRequest { Texture = texture, Region = r, TextureDone = done });
    }

    /// <summary>Copies a buffer range to the host after this frame's views, the rest of the buffer when size is 0. Host thread.</summary>
    public void Readback(DeviceBuffer buffer, Action<BufferReadback> done, uint offset = 0, uint size = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(done);
        if (size == 0)
            size = buffer.SizeInBytes - Math.Min(offset, buffer.SizeInBytes);
        if (size == 0 || (long)offset + size > buffer.SizeInBytes)
            throw new ArgumentOutOfRangeException(nameof(size), $"The buffer holds {buffer.SizeInBytes} bytes.");
        _readbacks.Enqueue(new ReadbackRequest { Buffer = buffer, Offset = offset, Size = size, BufferDone = done });
    }

    /// <summary>
    /// The GPU execution of the last frame dispatched, for hosts tracking their own readbacks or staging against it.
    /// Set on the render thread before <see cref="FrameDesc.Submitted"/> runs, so read it there or after SubmitFrame
    /// returns with inline threading.
    /// </summary>
    public ExecutionTask? LastExecution { get; private set; }

    /// <summary>The counts of the last frame recorded. Any thread.</summary>
    public FrameStats LastFrameStats => _frame.LastStats;

    /// <summary>Shader variants a draw waits on. Draws needing one use a stand in, or skip, until it is done. Any thread.</summary>
    public int ShaderCompilesPending => _frame.Programs.Compiling;

    /// <summary>Shader variants compiling ahead of use, like debug views. Any thread.</summary>
    public int ShaderCompilesBackground => _frame.Programs.Background;

    // Hands finished and failed readbacks to their callbacks. Host thread, render thread idle.
    private void RaiseReadbacks()
    {
        ulong completed = Device.LastCompletedExecutionId;
        for (int i = 0; i < _pendingReadbacks.Count; i++)
        {
            ReadbackRequest request = _pendingReadbacks[i];
            if (request.Error == null && (request.Execution == 0 || request.Execution > completed))
                continue;
            _pendingReadbacks.RemoveAt(i--);
            byte[] data = [];
            if (request.Staging is { } staging)
            {
                data = Device.Map<byte>(staging).ToArray();
                Device.Unmap(staging);
                staging.Dispose();
            }
            try
            {
                if (request.Texture is { } texture)
                    request.TextureDone!(new TextureReadback(data, request.Region.Width, request.Region.Height, request.Region.Depth, texture.Format, request.Error));
                else
                    request.BufferDone!(new BufferReadback(data, request.Error));
            }
            catch (Exception e)
            {
                Report(new RendererError("readback.callback", $"A readback's callback threw: {e}", RendererErrorSeverity.Error, e));
            }
        }
    }
    private readonly HashSet<Texture> _freedTextures = new();
    private readonly HashSet<RenderFeature> _extracted = new();

    // Snapshots every world's and view's features and copies their settings, each feature once
    private void ExtractFeatures(ReadOnlySpan<ViewDesc> views)
    {
        _extracted.Clear();
        foreach (WorldFrame world in _worlds.Values)
        {
            world.World.FeatureSnapshot = world.World.Features.Snapshot();
            Extract(world.World.FeatureSnapshot);
            world.World.FrameFeatureSnapshot = world.World.FrameFeatures.Snapshot();
            ExtractFrame(world.World.FrameFeatureSnapshot);
        }
        _frame.RendererFeatures = FrameFeatures.Snapshot();
        ExtractFrame(_frame.RendererFeatures);
        for (int i = 0; i < views.Length; i++)
        {
            _viewFeatures[i] = views[i].Features?.Snapshot() ?? [];
            Extract(_viewFeatures[i]);
        }

        static void ExtractFrame(FrameFeature[] features)
        {
            foreach (FrameFeature feature in features)
            {
                if (feature.EnabledSnapshot)
                    feature.Extract();
            }
        }

        void Extract(RenderFeature[] features)
        {
            foreach (RenderFeature feature in features)
            {
                if (_extracted.Add(feature) && feature.EnabledSnapshot)
                    feature.Extract();
            }
        }
    }

    /// <summary>
    /// A fullscreen program for features. The fragment code follows an include giving it vUV, outColor and
    /// Graphite_SampleSource(uv), which samples the colour the feature reads in mono and layered views alike, unless
    /// source is false. Textures the size of the view are declared GRAPHITE_SCREEN_TEXTURE and read through
    /// Graphite_Sample, arrays on layered views. Blend is one override attachment unless given. Under
    /// <see cref="CompilePolicy.Async"/> it compiles on the compile queue, and a feature drawing it sits out of its views
    /// until it is ready.
    /// </summary>
    /// <remarks>
    /// A lit program may include Graphite/Lighting.glsl and Graphite/Shadows.glsl and read the world's lights, shadows,
    /// probes and bindless textures as forward passes do; its feature declares <see cref="FeatureBuilder.DrawsRenderables"/>.
    /// </remarks>
    public FullscreenProgram CreateFullscreenProgram(string fragment, string name = "Fullscreen", bool source = true, BlendStateDescription? blend = null, bool lit = false, string variant = "")
        => FullscreenProgram.Create(Device, _compiler, fragment, name, source, blend, lit, variant: variant, policy: _options.Compile, report: Report);

    /// <summary>
    /// Creates a program making its own geometry from gl_VertexIndex, drawn by features through
    /// <see cref="FeatureContext.DrawProcedural"/>. Its code holds both stages, the vertex one under GRAPHITE_VERTEX, and
    /// reads the view through Graphite/Internal/PostView.glsl as fullscreen programs do.
    /// </summary>
    public FullscreenProgram CreateProceduralProgram(string code, string name = "Procedural", BlendStateDescription? blend = null, bool lit = false)
        => FullscreenProgram.Create(Device, _compiler, code, name, false, blend, lit, procedural: true, policy: _options.Compile, report: Report);

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
                removed.World.Transients.Release(_geometry, _lastDispatched);
                removed.World.DisposeRender();
                removed.Dispose();
            }
        }
        _removedWorlds.Clear();
    }

    private EffectResources? _effects;

    /// <summary>The stock effects' programs and samplers, made the first time an effect asks and freed with the renderer.</summary>
    internal EffectResources Effects
    {
        get
        {
            lock (this)
                return _effects ??= new EffectResources(this);
        }
    }

    /// <summary>
    /// Waits until the render thread has finished the CPU side of the last submitted frame, then hands readbacks and
    /// probe bakes the GPU has finished to their callbacks. Host thread.
    /// </summary>
    /// <summary>Resizes the device's main swapchain once the render thread and the GPU are done with it. Host thread.</summary>
    public void Resize(uint width, uint height)
    {
        WaitForRenderThread();
        Device.WaitForIdle();
        Device.ResizeMainWindow(width, height);
    }

    public void WaitForRenderThread()
    {
        _frameDone.Wait();
        try
        {
            RaiseBakes();
            RaiseReadbacks();
        }
        finally
        {
            _frameDone.Release();
        }
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
        _frame.Dispose();
        _effects?.Dispose();
        _uploader.Dispose();
        _store.Dispose();
        Resources.Dispose();
        _frameQueued.Dispose();
        _frameDone.Dispose();
    }

    internal void Report(RendererError error)
    {
        if (_reported.TryAdd(error.Key ?? error.Code, true))
            _errors.Enqueue(error);
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
        Action<bool>? submitted = _submitted;
        _submitted = null;
        bool succeeded = false;
        try
        {
            Action? started = _started;
            _started = null;
            started?.Invoke();
            RenderFrame();
            succeeded = true;
        }
        catch (Exception e)
        {
            _uploader.Fence();
            _lastDispatched = Math.Max(_lastDispatched, Device.LastBegunExecutionId);
            foreach (WorldFrame world in _worlds.Values)
                world.World.Reflections.FrameFailed();
            foreach (ReadbackRequest request in _frameReadbacks)
            {
                request.Staging?.Dispose();
                request.Staging = null;
                if (++request.Attempts < 2)
                {
                    _readbacks.Enqueue(request);
                    continue;
                }
                request.Error = "The frames that would have copied it failed.";
                _pendingReadbacks.Add(request);
            }
            _frameReadbacks.Clear();
            Report(new RendererError("frame.failed", $"The frame failed: {e}", RendererErrorSeverity.Error, e) { Key = $"frame.{e.GetType().Name}.{e.Message}" });
        }

        try
        {
            submitted?.Invoke(succeeded);
        }
        catch (Exception e)
        {
            Report(new RendererError("frame.submitted", $"A frame's Submitted callback threw: {e}", RendererErrorSeverity.Error, e) { Key = $"frame.submitted.{e.GetType().Name}.{e.Message}" });
        }
    }

    // Render thread: apply resource changes, flush the tables, cull and compact, dispatch.
    private void RenderFrame()
    {
        ulong completed = Device.LastCompletedExecutionId;
        _geometry.Reclaim(completed);
        _materials.Reclaim(completed);
        _globals.Reclaim(completed);

        _changedMeshes.Clear();
        _geometry.Apply(_uploader, _lastDispatched, _options.UploadBudgetPerFrame, _changedMeshes);
        foreach (WorldFrame world in _worlds.Values)
        {
            world.World.Render.Update(_changedMeshes, _changedMaterials);
            world.World.Render.UpdateDeformation(_geometry, _lastDispatched);
            world.World.Transients.Place(_geometry, _uploader, _lastDispatched);
            world.Buckets.Update(world.World.Render.DrawChanged, world.World.Render.BoundsChanged, _changedMaterials);
        }
        _geometry.FlushCopies(_uploader);

        _materials.Flush(_uploader, _lastDispatched);
        _globals.Flush(_uploader, _lastDispatched);
        _frameCount++;
        foreach (WorldFrame world in _worlds.Values)
        {
            world.World.Render.Flush(_uploader);
            _cameras.Clear();
            for (int v = 0; v < _viewCount; v++)
            {
                if (_views[v].World == world.World)
                    _cameras.Add(_views[v].Layers[0].Position);
            }
            world.World.Reflections.Plan(_frameCount, world.World.Lighting, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_cameras), world.World.Id);
            world.World.Reflections.Flush(_uploader);
            world.World.Lighting.Update();
            world.World.Lighting.Flush(_uploader);
            world.World.FlushHostSets(_uploader);
            world.World.Probes.Update(world.World.Render, _uploader);
        }

        ExecutionTask task = _frame.Run(_views.AsSpan(0, _viewCount), _viewFeatures.AsSpan(0, _viewCount), _frameReleased, _frameReadbacks, _worlds, _worlds.Values, _lastDispatched, _time, _profilers);
        _lastDispatched = task.Id;
        LastExecution = task;
        foreach (ReadbackRequest request in _frameReadbacks)
        {
            request.Execution = task.Id;
            _pendingReadbacks.Add(request);
        }
        _frameReadbacks.Clear();
        foreach (WorldFrame world in _worlds.Values)
            world.World.Reflections.Dispatched(task.Id);
    }
}
