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
    /// Forces Inline threading and blocking variant compiles, so no fallback variant is ever drawn. For golden images
    /// and tests.
    /// </summary>
    public bool Deterministic;

    /// <summary>When shader variants compile. Deterministic forces Sync, players with only baked shaders use BakedOnly.</summary>
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

    /// <summary>Frames a view keeps its per view state after it was last drawn.</summary>
    public int StaleViewFrames = 8;
}

/// <summary>
/// The skies Graphite ships, loaded the first time they are asked for: the procedural sky lit by the world's sun
/// (_SunSize, _SunGlow, _Exposure, _Tint, _GroundColor), a two colour gradient (_TopColor, _BottomColor, _Exponent), a
/// cube texture (_Cubemap), an equirectangular panorama (_Panorama), both with _Tint, _Exposure and _Rotation in degrees,
/// and a cube from six images (_CubeRight, _CubeLeft, _CubeTop, _CubeBottom, _CubeFront, _CubeBack, _Tint, _Exposure).
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
/// The standard surface shaders Graphite ships, loaded the first time they are asked for: Lit, physically based with
/// optional maps and models compiled in only while a material uses them, and Unlit. Both take <see cref="SurfaceSettings"/>.
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

    /// <summary>What the view is for, which decides the features that run on it.</summary>
    public ViewKind Kind;

    /// <summary>The part of the target the view draws into, as x, y, width and height from 0 to 1, y down from the top.</summary>
    public Float4 Viewport;

    /// <summary>Clears the whole target to the clear colour before drawing into the viewport, for a view letterboxed into its target.</summary>
    public bool ClearTarget;

    /// <summary>
    /// Draws over what its target already holds, such as views of a lower <see cref="Order"/>: the view starts transparent
    /// without sky or clear colour, and its colour, premultiplied by its alpha as blending over transparent leaves it, is
    /// blended over the target: transparent and additive surfaces and glow reach the views under it. For stacked cameras
    /// such as a first person weapon or world space UI.
    /// </summary>
    public bool Overlay;

    /// <summary>Render resolution against the viewport's size in the target, presented scaled to fit.</summary>
    public float RenderScale;

    /// <summary>Features of this view alone, over the world's.</summary>
    public FeatureSet? Features;

    /// <summary>Values of global axes for this view alone, over the world's, such as GRAPHITE_DEBUG. Copied at SubmitFrame.</summary>
    public Keyword[]? Keywords;

    /// <summary>Views draw lowest Order first, each after the views it depends on.</summary>
    public int Order;

    /// <summary>ViewIds of views that draw before this one, such as ones whose targets it samples.</summary>
    public int[]? DependsOn;

    /// <summary>
    /// The renderer culls the view and prepares its draws but runs no features and presents nothing. Host pipelines in
    /// <see cref="FrameDesc.Pipelines"/> record them through <see cref="Renderer.Draws"/>.
    /// </summary>
    public bool External;

    /// <summary>Pass types an external view prepares draws for, beyond the prepass, opaque and transparent lists.</summary>
    public PassType[]? Passes;

    public ViewDesc()
    {
        World = null!;
        Target = null!;
        LayerCount = 1;
        CullingMask = ~0u;
        Sky = true;
        Near = 0.1f;
        FarDistance = 1000;
        Viewport = new Float4(0, 0, 1, 1);
        RenderScale = 1;
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

    /// <summary>
    /// Host pipelines run in the frame's execution after its views, on the render thread. Their passes draw external
    /// views through <see cref="Renderer.Draws"/>.
    /// </summary>
    public ReadOnlyMemory<GraphDispatch> Pipelines;

    /// <summary>Runs on the render thread before the frame is prepared, for host work its submission has to follow.</summary>
    public Action? Started;

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
    private readonly IShaderCompiler _compiler;
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
        if (_options.PrefilterSamples < 1)
            throw new ArgumentException($"PrefilterSamples is {_options.PrefilterSamples}, it must be at least 1.", nameof(options));
        if (_options.SmallestProbeMip is < 1 or > 2048 || !uint.IsPow2(_options.SmallestProbeMip))
            throw new ArgumentException($"SmallestProbeMip is {_options.SmallestProbeMip}, it must be a power of two from 1 to 2048.", nameof(options));
        InternalShaders.Register();

        Textures = new RendererTextures(device) { Reporter = Report };
        _uploader = new Uploader(device, InternalShaders.Compute(device, compiler, "Internal/Scatter.glsl"));
        _geometry = new GeometryPool(device, _options.PageVertexWords, _options.PageIndices);
        _materials = new MaterialSystem(device, Textures) { Reporter = Report };
        _globals = new GlobalRegistry(device, Textures) { Reporter = Report };
        _programs.Reporter = Report;
        _frame = new FramePipeline(device, compiler, _uploader, _geometry, _materials, _globals, _programs, Textures, _options.Deterministic, Report, _options.Compile) { StaleViewFrames = _options.StaleViewFrames, ShadowFormat = _options.ShadowFormat };
        _frame.Programs.ShadowBias = (_options.ShadowConstantBias, _options.ShadowSlopeBias);
        _frame.ChangedMaterials = _shadowMaterials;
        Skies = new BuiltInSkies(this);
        Standard = new BuiltInStandard(this);
        _programs.DepthCaster = LoadShader(ShaderParser.Parse(ShaderLibrary.Get($"{ShaderLibrary.Prefix}DepthCaster.shader")!));
        _depthCasterMaterial = CreateMaterial(_programs.DepthCaster);

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
            RenderWorld world = new(Device, this, Textures, new ReflectionSettings(_options.ReflectionFormat, _options.PrefilterSamples, _options.SmallestProbeMip, _options.ProbeTexelsPerFrame));
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

    /// <summary>Loads a shader through the renderer's compiler. Any thread.</summary>
    /// <summary>A shader for materials. A Fallback naming "Graphite/Fallback" draws flat grey in its place while it compiles.</summary>
    public Shader LoadShader(ShaderDefinition definition)
    {
        Shader shader = new(definition, _compiler);
        if (definition.Fallback == FallbackShaderName)
            shader.Fallback = _fallbackShader ??= new Shader(ShaderParser.Parse(ShaderLibrary.Get($"{ShaderLibrary.Prefix}Fallback.shader")!), _compiler);
        return shader;
    }

    public const string FallbackShaderName = "Graphite/Fallback";
    private Shader? _fallbackShader;

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
            if (!(view.RenderScale > 0) || !float.IsFinite(view.RenderScale))
                throw new ArgumentException($"View {view.ViewId} has a render scale of {view.RenderScale}, it must be above 0.");
            Float4 rect = view.Viewport;
            if (!(rect.X >= 0 && rect.Y >= 0 && rect.Z > 0 && rect.W > 0 && rect.X + rect.Z <= 1.0001f && rect.Y + rect.W <= 1.0001f))
                throw new ArgumentException($"View {view.ViewId} has the viewport {rect}, which must lie within 0 to 1 with a size above 0.");
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
            views.CopyTo(_views);
            _viewCount = views.Length;
            for (int v = 0; v < _viewCount; v++)
            {
                if (_views[v].Keywords is { Length: > 0 } keywords)
                    _views[v].Keywords = (Keyword[])keywords.Clone();
            }
            ExtractFeatures(views);
            _pipelines = frame.Pipelines.ToArray();
            _started = frame.Started;
            _submitted = frame.Submitted;
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
    private GraphDispatch[] _pipelines = [];
    private Action? _started;
    private Action<bool>? _submitted;
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
    /// The draws prepared this frame for an external view, for host pipelines to record. Render thread, inside a pass of
    /// <see cref="FrameDesc.Pipelines"/>.
    /// </summary>
    public ViewDraws Draws(int viewId) => _frame.Draws(viewId);

    /// <summary>The counts of the last frame recorded. Any thread.</summary>
    public FrameStats LastFrameStats => _frame.LastStats;

    /// <summary>Shader variants compiling in the background now. Draws needing one skip, or use a ready variant of the same pass, until it is done. Any thread.</summary>
    public int ShaderCompilesPending => _frame.Programs.Compiling;

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
        }
        for (int i = 0; i < views.Length; i++)
        {
            _viewFeatures[i] = views[i].Features?.Snapshot() ?? [];
            Extract(_viewFeatures[i]);
        }

        void Extract(RenderFeature[] features)
        {
            foreach (RenderFeature feature in features)
            {
                if (!_extracted.Add(feature))
                    continue;
                feature.TakeSnapshot();
                if (feature.EnabledSnapshot)
                    feature.Extract();
            }
        }
    }

    /// <summary>
    /// A fullscreen program for features. The fragment code follows an include giving it vUV, outColor and
    /// Graphite_SampleSource(uv), which samples the colour the feature reads in mono and layered views alike, unless
    /// source is false. Textures the size of the view are declared GRAPHITE_SCREEN_TEXTURE and read through
    /// Graphite_Sample, arrays on layered views. Blend is one override attachment unless given.
    /// </summary>
    /// <remarks>
    /// A lit program may include Graphite/Lighting.glsl and Graphite/Shadows.glsl and read the world's lights, shadows,
    /// probes and bindless textures as forward passes do; its feature declares <see cref="FeatureBuilder.DrawsRenderables"/>.
    /// </remarks>
    public FullscreenProgram CreateFullscreenProgram(string fragment, string name = "Fullscreen", bool source = true, BlendStateDescription? blend = null, bool lit = false)
        => FullscreenProgram.Create(Device, _compiler, fragment, name, source, blend, lit);

    /// <summary>
    /// Creates a program making its own geometry from gl_VertexIndex, drawn by features through
    /// <see cref="FeatureContext.DrawProcedural"/>. Its code holds both stages, the vertex one under GRAPHITE_VERTEX, and
    /// reads the view through Graphite/Internal/PostView.glsl as fullscreen programs do.
    /// </summary>
    public FullscreenProgram CreateProceduralProgram(string code, string name = "Procedural", BlendStateDescription? blend = null, bool lit = false)
        => FullscreenProgram.Create(Device, _compiler, code, name, false, blend, lit, procedural: true);

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
            world.World.Render.UpdateDeformation(_geometry, _lastDispatched);
            world.Buckets.Update(world.World.Render.DrawChanged, world.World.Render.BoundsChanged, _changedMaterials);
        }
        _geometry.FlushCopies(_uploader, _lastDispatched);

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
            world.World.FlushHostSets(_uploader, _replaced);
            world.World.Probes.Update(world.World.Render, _uploader, _replaced);
        }
        foreach (DeviceBuffer buffer in _replaced)
            _retiring.Add((_lastDispatched, buffer));

        ExecutionTask task = _frame.Run(_views.AsSpan(0, _viewCount), _viewFeatures.AsSpan(0, _viewCount), _pipelines, _frameReadbacks, _worlds, _worlds.Values, _lastDispatched, _time);
        _lastDispatched = task.Id;
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
