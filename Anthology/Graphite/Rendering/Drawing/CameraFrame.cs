using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>The id and render size of a camera's view. Kept per camera so a frame allocates nothing.</summary>
internal sealed class CameraView
{
    public uint PixelWidth { get; set; } = 1;
    public uint PixelHeight { get; set; } = 1;
    public int ViewId { get; set; }
}

/// <summary>Everything a camera pipeline draws in one frame, prepared before dispatch.</summary>
internal sealed class CameraFrame : IDisposable
{
    public readonly DrawList Prepass = new();
    public readonly DrawList Forward = new();
    public readonly DrawList Transparent = new();
    public readonly SceneDrawer Drawer;

    /// <summary>The features this frame's pick runs on the view.</summary>
    public readonly List<RenderFeature> Selected = new();

    /// <summary>The colours and scene a feature drew onto this frame, so the first to draw one clears it.</summary>
    public readonly HashSet<RenderResourceID> Written = new();

    /// <summary>Whether a feature before the one recording encoded the colour for display, as tonemappers do.</summary>
    public bool DisplayEncoded;

    /// <summary>
    /// The opaque colour with its mips that passes tagged "SceneColor" = "Read" sample, made the first time the view's
    /// transparent list holds one. Used says whether this frame's list holds one.
    /// </summary>
    public Texture? SceneColor;
    public bool SceneColorUsed;
    private TextureView? _sceneColorView;

    /// <summary>The cube a capture view presents into.</summary>
    public RenderTexture? CaptureCube;

    /// <summary>The screen lighting a producer in the view writes, kept across frames and made again when the view's size changes.</summary>
    public RenderTexture? ScreenVisibility, ScreenIrradiance;
    private (RenderTexture? Texture, TextureView? View) _visibilityView, _irradianceView;

    /// <summary>Makes the screen lighting for this frame's size, or drops it when no producer runs. Render thread.</summary>
    public void EnsureScreenLighting(bool wanted, bool irradiance, uint width, uint height, uint layers)
    {
        ScreenVisibility = EnsureScreen(ScreenVisibility, wanted, width, height, layers, PixelFormat.R8_UNorm, "Graphite.ScreenVisibility");
        ScreenIrradiance = EnsureScreen(ScreenIrradiance, wanted && irradiance, width, height, layers, PixelFormat.R16_G16_B16_A16_Float, "Graphite.ScreenIrradiance");
    }

    private RenderTexture? EnsureScreen(RenderTexture? current, bool wanted, uint width, uint height, uint layers, PixelFormat format, string name)
    {
        if (current != null && wanted && current.Desc.Width == width && current.Desc.Height == height && Math.Max(current.Desc.ArrayLayers, 1u) == Math.Max(layers, 1u))
            return current;
        if (current != null)
            _frame.Store.Retire(current);
        if (!wanted)
            return null;
        RenderTexture created = new(_device, PersistentTexture.Describe(GraphTextureDesc.Sized((int)width, (int)height, format) with { ArrayLayers = layers > 1 ? layers : 0 }));
        created.Name = $"{name} {View.ViewId}";
        return created;
    }

    public TextureView? ScreenVisibilityView() => ArrayView(ref _visibilityView, ScreenVisibility);

    public TextureView? ScreenIrradianceView() => ArrayView(ref _irradianceView, ScreenIrradiance);

    private TextureView? ArrayView(ref (RenderTexture? Texture, TextureView? View) cache, RenderTexture? texture)
    {
        if (cache.Texture == texture)
            return cache.View;
        if (cache.View != null)
            _frame.Retired.Add(cache.View);
        cache = (texture, texture == null ? null : _device.ResourceFactory.CreateTextureView(new TextureViewDescription(texture.ColorTextures[0]) { Arrayed = true }));
        return cache.View;
    }

    /// <summary>The host's buffers the view's graph declares, from the world's globals.</summary>
    public DeviceBuffer[] HostBuffers = [];

    /// <summary>The world's prefiltered cubes the view's draws sample.</summary>
    public RenderTexture[] Cubes = [];

    /// <summary>Where the world's providers add this view's items.</summary>
    public readonly DrawItemSink Sink = new();

    /// <summary>The view's directional cascades, and the records its shaders read them through.</summary>
    public readonly ViewShadows Shadows = new();
    public readonly GpuArray Cascades;

    /// <summary>The buffers the view and its draws read, collected each frame.</summary>
    public readonly BufferSet Buffers = new();

    /// <summary>What a shadow view culls its casters against, and which of them it draws, set before it is prepared.</summary>
    public CullFrustum ShadowCull;
    public CasterFilter ShadowFilter;

    /// <summary>Whether this is one of the renderer's own shadow views, drawing its casters alone.</summary>
    public bool IsShadowView { get; init; }

    /// <summary>This frame's instance counts copied from providers' buffers into the view's commands, as byte offsets.</summary>
    public readonly List<(RenderResourceID Source, uint Offset, uint Destination)> Counts = new();

    public void PlanCounts()
    {
        Counts.Clear();
        uint stride = (uint)Marshal.SizeOf<IndirectDrawIndexedArguments>();
        foreach ((BucketSet.TransparentItem item, DrawList list, RenderResourceID source, uint offset) in Sink.Counts)
        {
            int at = Lists.IndexOf(list);
            if (at < 0 || item.FirstCommand < 0)
                continue;
            for (int c = 0; c < item.Chunks.Length; c++)
                Counts.Add((source, offset, (uint)(CommandBases[at] + item.FirstCommand + c) * stride + 4));
        }
    }

    public FeatureView Info;

    /// <summary>Adds the view's passes to the frame graph.</summary>
    public readonly ViewGraph Build, Probe;

    /// <summary>The pass types each selected feature asked for, learned by a probe the first time the view selects it.</summary>
    public readonly Dictionary<RenderFeature, PassType[]> KnownTypes = new();

    /// <summary>What the features selected this frame ask of the view, as a shape.</summary>
    public CameraShape ShapeKey;

    /// <summary>The directional cascades the view fits.</summary>
    public CascadeSlot[] CascadeSlots = [];
    public string CascadeKey = "";

    /// <summary>This frame's time as the host gave it.</summary>
    public FrameTime Time;
    public readonly TableRing ViewTable;
    public readonly GpuArray Instances;
    public readonly GpuArray Records;
    public readonly GpuArray Commands;
    public readonly GpuArray ViewBlocks;

    /// <summary>What the view builds each spatial set's trees into, by the set's slot, made the first time it sees one.</summary>
    public readonly SpatialView?[] Spatial = new SpatialView?[SpatialIndex.MaxSets];

    /// <summary>Each set's roots in this view, all empty for sets the world lacks.</summary>
    public readonly GpuArray SpatialRoots;
    public readonly SpatialRoots[] Roots = new SpatialRoots[SpatialIndex.MaxSets];

    /// <summary>Whether the view's basis flips handedness, as cube captures do, so every draw's winding flips too.</summary>
    public bool Mirrored;
    public ViewDesc Desc;
    public WorldFrame World = null!;

    /// <summary>The world's global keywords with the view's own over them, what its draws pick their variants by.</summary>
    public IReadOnlyDictionary<string, string> Keywords = new Dictionary<string, string>();
    private IReadOnlyDictionary<string, string>? _worldKeywords;
    private Keyword[] _viewKeywords = [];

    /// <summary>Brings <see cref="Keywords"/> up to date with the world's snapshot and the view's keywords. Render thread.</summary>
    public void ResolveKeywords()
    {
        IReadOnlyDictionary<string, string> world = World.World.Globals.KeywordSnapshot;
        Keyword[] view = Desc.Keywords ?? [];
        if (ReferenceEquals(world, _worldKeywords) && view.AsSpan().SequenceEqual(_viewKeywords))
            return;
        _worldKeywords = world;
        _viewKeywords = view;
        if (view.Length == 0)
        {
            Keywords = world;
            return;
        }
        Dictionary<string, string> merged = new(world, StringComparer.Ordinal);
        foreach (Keyword keyword in view)
            merged[keyword.Name] = keyword.Value;
        Keywords = merged;
    }
    public int LastFrame;
    public readonly CameraView View = new();

    /// <summary>Last frame's view block per layer, for motion: its view projection and camera position.</summary>
    public readonly ViewBlock[] Previous = new ViewBlock[6];
    public readonly ViewBlock[] Blocks = new ViewBlock[6];
    public bool HasPrevious;

    /// <summary>Whether the view had drawn before this frame, as features see it.</summary>
    public bool HadPrevious;

    /// <summary>The Halton phase of the view's jitter, and this and last frame's jitter in clip space.</summary>
    public int JitterPhase;
    public Float2 Jitter, PreviousJitter;
    public int LayerCount = 1;

    private IndirectDrawIndexedArguments[] _commands = new IndirectDrawIndexedArguments[64];
    private DrawRecord[] _records = new DrawRecord[64];
    private uint[] _instances = new uint[256];
    private readonly FramePipeline _frame;
    private readonly GraphicsDevice _device;
    private readonly TableHost _tables;

    /// <summary>The view's buffers for a set's trees.</summary>
    public SpatialView SpatialFor(int slot) => Spatial[slot] ??= new SpatialView(_tables.Scope($"Set{slot}"));

    public CameraFrame(GraphicsDevice device, FramePipeline frame, int viewId)
    {
        _frame = frame;
        Drawer = new SceneDrawer(frame, this);
        Build = new ViewGraph(frame, this, frame.Graph, probing: false);
        Probe = new ViewGraph(frame, this, frame.ProbeGraph, probing: true);
        View.ViewId = viewId;
        Lists = [Prepass, Forward, Transparent];
        ViewTable = new TableRing(device, ProgramCache.ViewLayout, $"View table {viewId}");
        _tables = new TableHost(device, frame.Store, $"Graphite.Camera{viewId}");
        Instances = _tables.Array("Instances");
        Records = _tables.Array("Records");
        Commands = _tables.Array("Commands", BufferUsage.IndirectBuffer);
        ViewBlocks = _tables.Array("ViewBlocks");
        SpatialRoots = _tables.Array("SpatialRoots");
        Cascades = _tables.Array("Cascades");
        _device = device;
    }

    /// <summary>Picks this frame's features.</summary>
    public void SelectFeatures(RenderFeature[] world, RenderFeature[] view)
    {
        FeatureSelection.Select(Selected, world, view, Info);
    }

    /// <summary>The scene targets the selected features read.</summary>
    public SceneTargets SceneReads
    {
        get
        {
            SceneTargets targets = SceneTargets.None;
            foreach (RenderFeature feature in Selected)
                targets |= feature.SceneReadsSnapshot;
            return targets;
        }
    }

    /// <summary>Makes the scene colour for the view's size. Returns whether it did.</summary>
    public bool EnsureSceneColor(uint width, uint height, uint layers, PixelFormat format)
    {
        layers = Math.Max(layers, 1u);
        if (SceneColor is { } existing && existing.Width == width && existing.Height == height && existing.ArrayLayers == layers && existing.Format == format)
            return false;
        ReleaseSceneColor();
        uint mips = (uint)Math.Log2(Math.Max(width, height)) + 1;
        SceneColor = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(width, height, mips, layers, format, TextureUsage.Sampled));
        SceneColor.Name = $"Graphite.SceneColor {View.ViewId}";
        return true;
    }

    /// <summary>The scene colour viewed as an array whatever its layer count.</summary>
    public TextureView SceneColorView()
        => _sceneColorView ??= _device.ResourceFactory.CreateTextureView(new TextureViewDescription(SceneColor!) { Arrayed = true });

    public void ReleaseSceneColor()
    {
        if (_sceneColorView != null)
            _frame.Retired.Add(_sceneColorView);
        if (SceneColor != null)
            _frame.Retired.Add(SceneColor);
        (SceneColor, _sceneColorView) = (null, null);
    }

    // States are kept while their feature is in the world's or view's set, so history survives toggles
    public void PruneStates(RenderFeature[] world, RenderFeature[] view)
    {
        bool Known(RenderFeature feature) => Array.IndexOf(world, feature) >= 0 || Array.IndexOf(view, feature) >= 0 || Array.IndexOf(FeatureSelection.BuiltIn, feature) >= 0;
        foreach (RenderFeature feature in new List<RenderFeature>(KnownTypes.Keys))
        {
            if (!Known(feature))
                KnownTypes.Remove(feature);
        }
        List<(RenderFeature, Type)>? stale = null;
        foreach ((RenderFeature feature, Type type) in States.Keys)
        {
            if (!Known(feature))
                (stale ??= new()).Add((feature, type));
        }
        foreach ((RenderFeature, Type) key in stale ?? [])
        {
            (States[key] as IDisposable)?.Dispose();
            States.Remove(key);
        }
    }

    public T State<T>(RenderFeature feature) where T : class, new()
    {
        if (!States.TryGetValue((feature, typeof(T)), out object? state))
            States[(feature, typeof(T))] = state = new T();
        return (T)state;
    }

    /// <summary>Collects the buffers the view's draws read this frame.</summary>
    public void CollectBuffers()
    {
        Buffers.Clear();
        Buffers.Add(Instances.Persistent);
        Buffers.Add(Records.Persistent);
        Buffers.Add(Commands.Persistent);
        Buffers.Add(ViewBlocks.Persistent);
        Buffers.Add(SpatialRoots.Persistent);
        Buffers.Add(Cascades.Persistent);
        foreach (SpatialView? view in Spatial)
            view?.AddBuffers(Buffers);
    }

    /// <summary>The frame's draw lists: prepass, opaque, transparent, then one per pass type a feature draws.</summary>
    public List<DrawList> Lists { get; }

    /// <summary>The pass types features of the view draw, each with its list, as of the last graph build.</summary>
    public readonly List<(PassType Type, DrawList List)> PassLists = new();
    private readonly Dictionary<PassType, DrawList> _passListPool = new();

    /// <summary>Where each list's commands start in the uploaded command buffer.</summary>
    public int[] CommandBases { get; private set; } = new int[3];

    /// <summary>State features keep for this view, made the first time each asks.</summary>
    public readonly Dictionary<(RenderFeature Feature, Type Type), object> States = new();

    /// <summary>The list of a pass type a feature draws, made when the view's graph is built.</summary>
    public DrawList ListFor(PassType type)
    {
        if (type == PassType.Prepass)
            return Prepass;
        if (type == PassType.Forward)
            return Forward;
        foreach ((PassType known, DrawList list) in PassLists)
        {
            if (known == type)
                return list;
        }
        if (!_passListPool.TryGetValue(type, out DrawList? created))
            _passListPool[type] = created = new DrawList();
        PassLists.Add((type, created));
        Lists.Add(created);
        return created;
    }

    /// <summary>The list of a pass type the view's features asked for, or null.</summary>
    public DrawList? FindList(PassType type)
    {
        if (type == PassType.Prepass)
            return Prepass;
        if (type == PassType.Forward)
            return Forward;
        foreach ((PassType known, DrawList list) in PassLists)
        {
            if (known == type)
                return list;
        }
        return null;
    }

    /// <summary>Forgets which pass lists the view draws before a graph build asks for them again, keeping what they hold.</summary>
    public void ResetPassLists()
    {
        PassLists.Clear();
        Lists.RemoveRange(3, Lists.Count - 3);
    }

    /// <summary>Queues the frame's commands, draw records, instances and view blocks, the lists one after another.</summary>
    public void Upload(Uploader uploader, ReadOnlySpan<ViewBlock> blocks)
    {
        int commands = 0, instances = 0;
        foreach (DrawList list in Lists)
        {
            commands += list.CommandCount;
            instances += list.InstanceCount;
        }
        if (CommandBases.Length < Lists.Count)
            CommandBases = new int[Lists.Count];
        if (_commands.Length < commands)
        {
            Array.Resize(ref _commands, commands * 2);
            Array.Resize(ref _records, commands * 2);
        }
        if (_instances.Length < instances)
            Array.Resize(ref _instances, instances * 2);

        int commandBase = 0, instanceBase = 0;
        for (int l = 0; l < Lists.Count; l++)
        {
            DrawList list = Lists[l];
            CommandBases[l] = commandBase;
            for (int i = 0; i < list.CommandCount; i++)
            {
                IndirectDrawIndexedArguments command = list.Commands[i];
                DrawRecord record = list.Records[i];
                command.FirstInstance += (uint)commandBase;
                if ((record.Flags & DrawRecord.SharedObject) == 0)
                    record.FirstInstance += (uint)instanceBase;
                _commands[commandBase + i] = command;
                _records[commandBase + i] = record;
            }
            Array.Copy(list.Instances, 0, _instances, instanceBase, list.InstanceCount);
            commandBase += list.CommandCount;
            instanceBase += list.InstanceCount;
        }

        Commands.Write<IndirectDrawIndexedArguments>(uploader, _commands.AsSpan(0, commands));
        Records.Write<DrawRecord>(uploader, _records.AsSpan(0, commands));
        Instances.Write<uint>(uploader, _instances.AsSpan(0, instances));
        ViewBlocks.Write<ViewBlock>(uploader, blocks);
    }

    public void Dispose()
    {
        _sceneColorView?.Dispose();
        SceneColor?.Dispose();
        foreach (object state in States.Values)
            (state as IDisposable)?.Dispose();
        ViewTable.Dispose();
        Shadows.Dispose();
        foreach (GpuArray array in new[] { Instances, Records, Commands, ViewBlocks, SpatialRoots, Cascades })
            array.Release();
        foreach (SpatialView? view in Spatial)
            view?.Release();
        _visibilityView.View?.Dispose();
        _irradianceView.View?.Dispose();
        ScreenVisibility?.Dispose();
        ScreenIrradiance?.Dispose();
    }
}
