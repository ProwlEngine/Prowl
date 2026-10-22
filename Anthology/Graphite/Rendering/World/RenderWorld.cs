using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>A renderable in a world. Generation checked, so a handle of a removed renderable stays invalid.</summary>
public readonly record struct RenderableHandle(int Index, int Generation)
{
    public bool IsValid => Generation > 0;
}

[Flags]
public enum RenderableFlags : ushort
{
    None = 0,

    /// <summary>A hint: joins the static culling tree and the static shadow layer. One that moves anyway is culled as moving from then on.</summary>
    Static = 1 << 0,
    CastShadows = 1 << 1,
    ReceiveShadows = 1 << 2,

    /// <summary>Keeps a previous transform and writes per object motion.</summary>
    MotionVectors = 1 << 3,
    Hidden = 1 << 4,
    ShadowsOnly = 1 << 5,
}

/// <summary>
/// The lightmap a renderable samples and where its UVs land on it. A lightmapped renderable whose texture is null, one
/// still loading, lights with ambient light until it is set. <see cref="None"/> is lit by the light probes instead.
/// </summary>
public readonly record struct LightmapBinding
{
    public static readonly LightmapBinding None = default;

    public LightmapBinding(Texture? texture, Float4 scaleOffset, LightmapEncoding encoding = LightmapEncoding.RGBM, SamplerDescription? sampler = null)
    {
        Texture = texture;
        ScaleOffset = scaleOffset;
        Encoding = encoding;
        Sampler = sampler;
        Lightmapped = true;
    }

    public Texture? Texture { get; }

    /// <summary>The lightmap UVs times xy plus zw.</summary>
    public Float4 ScaleOffset { get; }
    public LightmapEncoding Encoding { get; }

    /// <summary>Linear and clamped when null.</summary>
    public SamplerDescription? Sampler { get; }

    public bool Lightmapped { get; }
}

/// <summary>One instance of a batch: its transform relative to the batch's, and data its shaders read as Custom0 and Custom1.</summary>
public struct InstanceData
{
    public Float4x4 Transform;
    public Float4 Color;
    public Float4 Custom;
}

/// <summary>What every instance of a batch shares.</summary>
public struct InstanceBatchDesc
{
    public GpuMesh Mesh;

    /// <summary>Where the batch sits, double so batches far from the origin keep their place. Instances are relative to it.</summary>
    public Double4x4 Transform;
    public uint LayerMask;
    public RenderableFlags Flags;

    /// <summary>The object id of the first instance, the rest following on.</summary>
    public uint ObjectIdBase;
    public sbyte SortBias;

    public InstanceBatchDesc()
    {
        Mesh = null!;
        Transform = Double4x4.Identity;
        LayerMask = ~0u;
        Flags = RenderableFlags.CastShadows | RenderableFlags.ReceiveShadows;
    }
}

public readonly record struct InstanceBatchHandle(int Index, int Generation)
{
    public bool IsValid => Generation > 0;
}

/// <summary>
/// Geometry drawn for one frame, a mesh or inline positions with optional colours and indices. Inline geometry becomes a
/// mesh the world owns and disposes after the frame.
/// </summary>
public struct TransientDraw
{
    public GpuMesh? Mesh;
    public ReadOnlyMemory<Float3> Positions;

    /// <summary>RGBA8 colours, one per position, read as the color stream.</summary>
    public ReadOnlyMemory<uint> Colors;

    /// <summary>Indices into the positions, none for the positions in order.</summary>
    public ReadOnlyMemory<uint> Indices;
    public PrimitiveTopology Topology;
    public Material Material;
    public Double4x4 Transform;
    public uint LayerMask;
    public RenderableFlags Flags;

    public TransientDraw()
    {
        Material = null!;
        Transform = Double4x4.Identity;
        LayerMask = ~0u;
        Flags = RenderableFlags.CastShadows | RenderableFlags.ReceiveShadows;
        Topology = PrimitiveTopology.TriangleList;
    }
}

public struct RenderableDesc
{
    public GpuMesh Mesh;

    /// <summary>World transform in double precision, so objects far from the origin keep their precision. Float matrices convert explicitly.</summary>
    public Double4x4 Transform;

    /// <summary>Which view culling masks see it.</summary>
    public uint LayerMask;
    public RenderableFlags Flags;

    /// <summary>Free per object shader data.</summary>
    public Float4 Custom0, Custom1;

    /// <summary>Host id written by the ObjectId pass.</summary>
    public uint ObjectId;
    public LightmapBinding Lightmap;

    /// <summary>Transparent order inside a queue, applied before depth.</summary>
    public sbyte SortBias;

    public RenderableDesc()
    {
        Mesh = null!;
        Transform = Double4x4.Identity;
        LayerMask = ~0u;
        Flags = RenderableFlags.CastShadows | RenderableFlags.ReceiveShadows;
        Lightmap = LightmapBinding.None;
    }
}

/// <summary>Splits doubles into a float and the float of what it missed, so the GPU can subtract the camera position exactly.</summary>
internal static class Precise
{
    public static (Float3 High, Float3 Low) Split(Double3 value)
    {
        Float3 high = new((float)value.X, (float)value.Y, (float)value.Z);
        Float3 low = new((float)(value.X - high.X), (float)(value.Y - high.Y), (float)(value.Z - high.Z));
        return (high, low);
    }
}

/// <summary>
/// The world matrix as the GPU reads it, 64 bytes: the rows of the 3x3 with the high part of the translation in w, then
/// the low part. Shaders work camera relative: the linear part times the position plus (high minus camera high) plus
/// (low minus camera low), which keeps full precision far from the origin.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ObjectTransform
{
    public Float4 Row0, Row1, Row2;
    public Float4 TranslationLow;

    public ObjectTransform(in Double4x4 m)
    {
        (Float3 high, Float3 low) = Precise.Split(new Double3(m.c3.X, m.c3.Y, m.c3.Z));
        Row0 = new Float4((float)m.c0.X, (float)m.c1.X, (float)m.c2.X, high.X);
        Row1 = new Float4((float)m.c0.Y, (float)m.c1.Y, (float)m.c2.Y, high.Y);
        Row2 = new Float4((float)m.c0.Z, (float)m.c1.Z, (float)m.c2.Z, high.Z);
        TranslationLow = new Float4(low.X, low.Y, low.Z, 0);
    }
}

/// <summary>World bounds for culling, 48 bytes: the center split high and low with the sphere radius, and the box half size.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ObjectBounds
{
    public Float4 CenterHighRadius;
    public Float4 CenterLow;
    public Float4 Extents;
}

/// <summary>Per object data shaders read as needed, 96 bytes, matching Graphite_ObjectData.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ObjectAttributes
{
    public const uint NoPrevious = uint.MaxValue;

    public uint MaterialSliceBase;
    public uint Flags;
    public uint ObjectId;
    public uint PreviousTransform;
    public const uint NoLightmap = uint.MaxValue;
    public const uint LightmapLoading = uint.MaxValue - 1;

    public uint LightmapTexture;
    public uint ProbeIndex;
    public float LodFade;
    public uint LayerMask;
    public uint LightmapSampler;

    /// <summary>The first word plus one of a skinned copy's current and last frame's positions, 0 for the chunk's own and none.</summary>
    public uint CurrentPositions;
    public uint PreviousPositions;
    private uint _pad0;
    public Float4 LightmapScaleOffset;
    public Float4 Custom0;
    public Float4 Custom1;
}

/// <summary>
/// A scene's renderable content. Mutation is host thread only and recorded for extraction: setters write a host copy
/// and a dirty bit, <see cref="Extract"/> copies what changed into the render copy, and the render thread turns that
/// into GPU records. Storage is struct of arrays keyed by a dense index with a free list.
/// </summary>
public sealed partial class RenderWorld : IDisposable
{
    private volatile Texture? _liveShadowAtlas, _staticShadowAtlas;

    /// <summary>The page local light shadow tiles live in as of the last frame, for looking at. Null until a light casts.</summary>
    public Texture? LiveShadowAtlas { get => _liveShadowAtlas; internal set => _liveShadowAtlas = value; }

    /// <summary>The page the static copies of local light shadows live in as of the last frame. Null until one is made.</summary>
    public Texture? StaticShadowAtlas { get => _staticShadowAtlas; internal set => _staticShadowAtlas = value; }

    private readonly List<Action> _hostSetExtracts = new();
    private readonly List<SpatialIndex> _hostSets = new();
    private readonly GraphicsDevice _device;
    private readonly Action<RendererError> _report;

    /// <summary>Every spatial set of the world, the built in ones first. Render side.</summary>
    internal IEnumerable<SpatialIndex> SpatialSets
    {
        get
        {
            yield return Lighting.Lights;
            yield return Lighting.Decals;
            yield return Reflections.Regions;
            foreach (SpatialIndex set in _hostSets)
                yield return set;
        }
    }

    /// <summary>
    /// A set of the host's own items found by where they are. Its index is the renderer's for the name, the same in every
    /// world, so materials shared by worlds read the same set; a renderer has room for 13 names. It draws from the next
    /// SubmitFrame. Host thread.
    /// </summary>
    public SpatialSet<T> CreateSpatialSet<T>(string name) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(name);
        if (System.Runtime.CompilerServices.Unsafe.SizeOf<T>() % 16 != 0)
            throw new ArgumentException($"A spatial set's record must be a whole number of 16 byte words, {typeof(T).Name} is {System.Runtime.CompilerServices.Unsafe.SizeOf<T>()} bytes.");
        if (!_hostSetNames.Add(name))
            throw new InvalidOperationException($"The world already has a spatial set named {name}.");
        int slot = SpatialSlots.Slot(_owner?.SpatialSlots, name, typeof(T));
        SpatialSet<T> set = new(_tables.Scope($"Set{slot}"), slot, _report);
        _newHostSets.Add(set.Render);
        _hostSetExtracts.Add(set.Extract);
        return set;
    }

    private readonly HashSet<string> _hostSetNames = new(StringComparer.Ordinal);
    private readonly List<SpatialIndex> _newHostSets = new();
    private readonly SpatialSlots SpatialSlots = new();

    /// <summary>How the world's local light shadows are stored and sized. Host thread, read at SubmitFrame.</summary>
    public ShadowSettings Shadows { get; } = new();

    /// <summary>The shadow settings as of the last SubmitFrame. The render thread reads only this.</summary>
    internal ShadowSettings ShadowSnapshot = new();

    /// <summary>Features every view of the world runs. Host thread.</summary>
    public FeatureSet Features { get; } = new();

    /// <summary>GPU work run once a frame outside any view. Host thread.</summary>
    public FrameFeatureSet FrameFeatures { get; }

    /// <summary>Host geometry drawn into each view through its passes without being renderables. Host thread.</summary>
    public ProviderSet Providers { get; } = new();

    /// <summary>The providers as of the last SubmitFrame. The render thread reads only this.</summary>
    internal RenderProvider[] ProviderSnapshot = [];

    private sealed class InstanceBatch
    {
        public InstanceBatchDesc Desc;
        public Material[] Materials = [];
        public readonly List<RenderableHandle> Instances = new();
        public readonly List<Float4x4> Relative = new();
        public int Generation;
        public bool Alive;
    }

    private readonly List<InstanceBatch> _batches = new();
    private readonly Stack<int> _freeBatches = new();

    /// <summary>
    /// Adds many instances of one mesh and material set, each an object of its own for culling, lighting and object ids.
    /// Host thread.
    /// </summary>
    public InstanceBatchHandle AddInstanceBatch(in InstanceBatchDesc desc, ReadOnlySpan<Material> materials, ReadOnlySpan<InstanceData> instances)
    {
        ArgumentNullException.ThrowIfNull(desc.Mesh);
        if (materials.IsEmpty)
            throw new ArgumentException("An instance batch needs at least one material.", nameof(materials));
        int index;
        if (_freeBatches.Count > 0)
        {
            index = _freeBatches.Pop();
        }
        else
        {
            index = _batches.Count;
            _batches.Add(new InstanceBatch());
        }
        InstanceBatch batch = _batches[index];
        batch.Generation++;
        batch.Alive = true;
        batch.Desc = desc;
        batch.Materials = materials.ToArray();
        SetInstances(batch, instances);
        return new InstanceBatchHandle(index, batch.Generation);
    }

    /// <summary>Replaces a batch's instances, adding and removing at the end when the count changes. Host thread.</summary>
    public void SetInstances(InstanceBatchHandle handle, ReadOnlySpan<InstanceData> instances) => SetInstances(Batch(handle), instances);

    /// <summary>Moves a batch or changes what its instances share, keeping their own data. Host thread.</summary>
    public void SetInstanceBatch(InstanceBatchHandle handle, in InstanceBatchDesc desc)
    {
        ArgumentNullException.ThrowIfNull(desc.Mesh);
        InstanceBatch batch = Batch(handle);
        InstanceBatchDesc old = batch.Desc;
        batch.Desc = desc;
        for (int i = 0; i < batch.Instances.Count; i++)
        {
            RenderableHandle instance = batch.Instances[i];
            SetTransform(instance, desc.Transform * (Double4x4)batch.Relative[i]);
            if (desc.Mesh != old.Mesh)
                SetMesh(instance, desc.Mesh);
            if (desc.LayerMask != old.LayerMask)
                SetLayerMask(instance, desc.LayerMask);
            if (desc.Flags != old.Flags)
                SetFlags(instance, desc.Flags);
            if (desc.ObjectIdBase != old.ObjectIdBase)
                SetObjectId(instance, desc.ObjectIdBase + (uint)i);
            if (desc.SortBias != old.SortBias)
                SetSortBias(instance, desc.SortBias);
        }
    }

    public void RemoveInstanceBatch(InstanceBatchHandle handle)
    {
        InstanceBatch batch = Batch(handle);
        foreach (RenderableHandle instance in batch.Instances)
            Remove(instance);
        batch.Instances.Clear();
        batch.Relative.Clear();
        batch.Alive = false;
        batch.Generation++;
        _freeBatches.Push(handle.Index);
    }

    public bool IsAlive(InstanceBatchHandle handle)
        => (uint)handle.Index < (uint)_batches.Count && _batches[handle.Index].Alive && _batches[handle.Index].Generation == handle.Generation;

    public int InstanceCount(InstanceBatchHandle handle) => Batch(handle).Instances.Count;

    private InstanceBatch Batch(InstanceBatchHandle handle)
    {
        if (!IsAlive(handle))
            throw new ArgumentException("The instance batch was removed, or is not from this world.", nameof(handle));
        return _batches[handle.Index];
    }

    private void SetInstances(InstanceBatch batch, ReadOnlySpan<InstanceData> instances)
    {
        InstanceBatchDesc desc = batch.Desc;
        while (batch.Instances.Count > instances.Length)
        {
            Remove(batch.Instances[^1]);
            batch.Instances.RemoveAt(batch.Instances.Count - 1);
            batch.Relative.RemoveAt(batch.Relative.Count - 1);
        }
        for (int i = 0; i < instances.Length; i++)
        {
            InstanceData data = instances[i];
            Double4x4 transform = desc.Transform * (Double4x4)data.Transform;
            if (i < batch.Instances.Count)
            {
                batch.Relative[i] = data.Transform;
                RenderableHandle instance = batch.Instances[i];
                SetTransform(instance, transform);
                SetCustomData(instance, data.Color, data.Custom);
                continue;
            }
            batch.Relative.Add(data.Transform);
            batch.Instances.Add(Add(new RenderableDesc
            {
                Mesh = desc.Mesh,
                Transform = transform,
                LayerMask = desc.LayerMask,
                Flags = desc.Flags,
                Custom0 = data.Color,
                Custom1 = data.Custom,
                ObjectId = desc.ObjectIdBase + (uint)i,
                SortBias = desc.SortBias,
            }, batch.Materials));
        }
    }

    private readonly List<(RenderableHandle Handle, GpuMesh Mesh, bool Owned)> _transients = new();
    private readonly List<(RenderableHandle Handle, GpuMesh Mesh, bool Owned)> _drawnTransients = new();

    /// <summary>Draws geometry in the next frame only, through every pass its material has. Host thread.</summary>
    public void DrawTransient(in TransientDraw draw)
    {
        ArgumentNullException.ThrowIfNull(draw.Material);
        GpuMesh mesh;
        if (draw.Mesh != null)
        {
            mesh = draw.Mesh;
        }
        else
        {
            if (_owner == null)
                throw new InvalidOperationException("Inline transient geometry needs a world made by a renderer.");
            if (draw.Positions.IsEmpty)
                throw new ArgumentException("A transient draw needs a mesh or positions.", nameof(draw));
            uint count = (uint)draw.Positions.Length;
            uint[] indices = draw.Indices.IsEmpty ? [.. System.Linq.Enumerable.Range(0, (int)count).Select(i => (uint)i)] : draw.Indices.ToArray();
            List<VertexStream> streams = [new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = System.Runtime.InteropServices.MemoryMarshal.AsBytes(draw.Positions.Span).ToArray() }];
            if (!draw.Colors.IsEmpty)
                streams.Add(new VertexStream { Semantic = VertexSemantic.Color, Format = VertexElementFormat.Byte4_Norm, Data = System.Runtime.InteropServices.MemoryMarshal.AsBytes(draw.Colors.Span).ToArray() });
            mesh = _owner.CreateMesh(new MeshData
            {
                VertexCount = count,
                Streams = [.. streams],
                Indices = indices,
                SubMeshes = [new SubMesh { IndexStart = 0, IndexCount = (uint)indices.Length, Topology = draw.Topology }],
            });
        }
        RenderableHandle handle = Add(new RenderableDesc { Mesh = mesh, Transform = draw.Transform, LayerMask = draw.LayerMask, Flags = draw.Flags }, [draw.Material]);
        _transients.Add((handle, mesh, draw.Mesh == null));
    }

    // Last frame's transients go with the meshes made for them, except those whose mesh was not placed in time to draw
    private void RetireTransients()
    {
        foreach ((RenderableHandle handle, GpuMesh mesh, bool owned) in _drawnTransients)
        {
            if (mesh.Placement == null && !mesh.IsDisposed)
            {
                _transients.Add((handle, mesh, owned));
                continue;
            }
            Remove(handle);
            if (owned)
                mesh.Dispose();
        }
        _drawnTransients.Clear();
        _drawnTransients.AddRange(_transients);
        _transients.Clear();
    }

    /// <summary>The features as of the last SubmitFrame. The render thread reads only this.</summary>
    internal RenderFeature[] FeatureSnapshot = [];
    internal FrameFeature[] FrameFeatureSnapshot = [];

    [Flags]
    internal enum Dirt : byte
    {
        None = 0,
        Transform = 1,
        Attributes = 2,
        Draw = 4,
        Bounds = 8,
        Added = 16,
        Removed = 32,
        Bones = 64,
        Weights = 128,
    }

    private const uint MaxSliceEntries = 1u << 24;

    private int _capacity;
    private int _count;
    private readonly Stack<int> _free = new();
    private int[] _generation = [];
    private bool[] _alive = [];
    private GpuMesh?[] _mesh = [];
    private RangeAllocation[] _slice = [];
    private Double4x4[] _transform = [];
    private uint[] _layerMask = [];
    private RenderableFlags[] _flags = [];
    private Float4[] _custom0 = [];
    private Float4[] _custom1 = [];
    private uint[] _objectId = [];
    private LightmapBinding[] _lightmap = [];
    private sbyte[] _sortBias = [];
    private AABB[] _boundsOverride = [];
    private bool[] _hasBoundsOverride = [];
    private Float4x4[]?[] _bones = [];
    private float[]?[] _shapeWeights = [];
    private Dirt[] _dirt = [];
    private readonly List<int> _dirty = new();

    private readonly RangeAllocator _sliceSpace = new(MaxSliceEntries);
    private Material?[] _sliceMaterials = new Material?[256];

    private readonly RenderSide _render;
    private readonly TableHost _tables;

    private readonly Renderer? _owner;
    private static int s_nextId;

    internal int Id { get; }

    internal RenderWorld(GraphicsDevice device, GraphStore store, Renderer? owner = null, IMaterialResources? resources = null, ReflectionSettings? reflections = null)
    {
        _owner = owner;
        _device = device;
        FrameFeatures = new FrameFeatureSet(this);
        int id = System.Threading.Interlocked.Increment(ref s_nextId);
        Id = id;
        Action<RendererError> report = owner != null ? error => owner.Report(error with { Source = error.Source ?? this, Key = $"{error.Key ?? error.Code}.world{id}" }) : _ => { };
        _report = report;
        _tables = new TableHost(device, store, $"Graphite.World{id}");
        Lighting = new WorldLighting(_tables.Scope("Lighting"), resources ?? NoMaterialResources.Instance, report);
        Probes = new WorldProbes(_tables.Scope("Probes"));
        Reflections = new WorldReflections(_tables.Scope("Reflections"), resources ?? NoMaterialResources.Instance, report, reflections ?? ReflectionSettings.Default);
        _render = new RenderSide(_tables.Scope("Render"), Probes, report);
        Grow(64);
    }

    /// <summary>Live renderables.</summary>
    public int Count { get; private set; }

    internal RenderSide Render => _render;

    internal WorldReflections Reflections { get; }

    /// <summary>Values and keywords this world's views read, falling back to the renderer's.</summary>
    public GlobalProperties Globals { get; internal set; } = null!;

    /// <summary>Renderables changed since the last extraction.</summary>
    internal int PendingChanges => _dirty.Count;

    /// <summary>Adds a renderable. Materials are one per submesh, the last repeating for the rest.</summary>
    public RenderableHandle Add(in RenderableDesc desc, ReadOnlySpan<Material> materials)
    {
        ArgumentNullException.ThrowIfNull(desc.Mesh);
        int index = _free.Count > 0 ? _free.Pop() : _count++;
        if (index >= _capacity)
            Grow(_capacity * 2);

        _generation[index]++;
        _alive[index] = true;
        _mesh[index] = desc.Mesh;
        _transform[index] = desc.Transform;
        _layerMask[index] = desc.LayerMask;
        _flags[index] = desc.Flags;
        _custom0[index] = desc.Custom0;
        _custom1[index] = desc.Custom1;
        _objectId[index] = desc.ObjectId;
        _lightmap[index] = desc.Lightmap;
        _sortBias[index] = desc.SortBias;
        _hasBoundsOverride[index] = false;
        _bones[index] = null;
        _shapeWeights[index] = null;
        _slice[index] = AllocateSlice(materials);
        Count++;
        MarkDirty(index, Dirt.Added | Dirt.Transform | Dirt.Attributes | Dirt.Draw | Dirt.Bounds);
        return new RenderableHandle(index, _generation[index]);
    }

    public void Remove(RenderableHandle handle)
    {
        int index = Check(handle);
        _alive[index] = false;
        _generation[index]++;
        _mesh[index] = null;
        _bones[index] = null;
        _shapeWeights[index] = null;
        FreeSlice(_slice[index]);
        _slice[index] = default;
        _free.Push(index);
        Count--;
        MarkDirty(index, Dirt.Removed);
    }

    public bool IsAlive(RenderableHandle handle)
        => (uint)handle.Index < (uint)_count && _alive[handle.Index] && _generation[handle.Index] == handle.Generation;

    /// <summary>Does nothing when the transform is equal to the stored one.</summary>
    public void SetTransform(RenderableHandle handle, in Float4x4 transform) => SetTransform(handle, (Double4x4)transform);

    /// <summary>Does nothing when the transform is equal to the stored one.</summary>
    public void SetTransform(RenderableHandle handle, in Double4x4 transform)
    {
        int index = Check(handle);
        if (_transform[index] == transform)
            return;
        _transform[index] = transform;
        MarkDirty(index, Dirt.Transform);
    }

    public Double4x4 GetTransform(RenderableHandle handle) => _transform[Check(handle)];

    public void SetMesh(RenderableHandle handle, GpuMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        int index = Check(handle);
        if (_mesh[index] == mesh)
            return;
        _mesh[index] = mesh;
        MarkDirty(index, Dirt.Draw | Dirt.Bounds);
    }

    public void SetMaterials(RenderableHandle handle, ReadOnlySpan<Material> materials)
    {
        int index = Check(handle);
        FreeSlice(_slice[index]);
        _slice[index] = AllocateSlice(materials);
        MarkDirty(index, Dirt.Draw | Dirt.Attributes);
    }

    public void SetFlags(RenderableHandle handle, RenderableFlags flags)
    {
        int index = Check(handle);
        if (_flags[index] == flags)
            return;
        _flags[index] = flags;
        MarkDirty(index, Dirt.Draw | Dirt.Attributes | Dirt.Transform);
    }

    public void SetLayerMask(RenderableHandle handle, uint mask)
    {
        int index = Check(handle);
        if (_layerMask[index] == mask)
            return;
        _layerMask[index] = mask;
        MarkDirty(index, Dirt.Attributes);
    }

    public void SetCustomData(RenderableHandle handle, Float4 custom0, Float4 custom1)
    {
        int index = Check(handle);
        if (_custom0[index] == custom0 && _custom1[index] == custom1)
            return;
        _custom0[index] = custom0;
        _custom1[index] = custom1;
        MarkDirty(index, Dirt.Attributes);
    }

    public void SetObjectId(RenderableHandle handle, uint objectId)
    {
        int index = Check(handle);
        _objectId[index] = objectId;
        MarkDirty(index, Dirt.Attributes);
    }

    public void SetLightmap(RenderableHandle handle, in LightmapBinding lightmap)
    {
        int index = Check(handle);
        _lightmap[index] = lightmap;
        MarkDirty(index, Dirt.Attributes);
    }

    private static readonly SamplerDescription s_lightmapSampler = SamplerDescription.Linear with
    {
        AddressModeU = SamplerAddressMode.Clamp,
        AddressModeV = SamplerAddressMode.Clamp,
        AddressModeW = SamplerAddressMode.Clamp,
    };

    // The lightmap's texture slot, or none, or loading, and its sampler slot with the encoding in the high 16 bits
    private (uint Texture, uint Sampler) LightmapSlots(in LightmapBinding lightmap)
    {
        if (!lightmap.Lightmapped)
            return (ObjectAttributes.NoLightmap, 0);
        if (lightmap.Texture is not { IsDisposed: false } texture)
            return (ObjectAttributes.LightmapLoading, 0);
        IMaterialResources resources = Lighting.Resources;
        uint sampler = resources.SamplerSlot(lightmap.Sampler ?? s_lightmapSampler);
        return (resources.TextureSlot(texture, MaterialPropertyType.Texture2D, "black"), sampler | ((uint)lightmap.Encoding << 16));
    }

    /// <summary>Resolves the slots of lightmaps whose textures were freed, every one when freed is null. Host thread, at extraction.</summary>
    internal void RepackLightmaps(HashSet<Texture>? freed)
    {
        for (int index = 0; index < _count; index++)
        {
            if (_alive[index] && _lightmap[index].Texture is { } texture && (freed == null || freed.Contains(texture)))
                MarkDirty(index, Dirt.Attributes);
        }
    }

    public void SetSortBias(RenderableHandle handle, sbyte sortBias)
    {
        int index = Check(handle);
        _sortBias[index] = sortBias;
        MarkDirty(index, Dirt.Attributes);
    }

    /// <summary>Overrides the mesh bounds, for deforming meshes.</summary>
    public void SetBounds(RenderableHandle handle, in AABB localBounds)
    {
        int index = Check(handle);
        _boundsOverride[index] = localBounds;
        _hasBoundsOverride[index] = true;
        MarkDirty(index, Dirt.Bounds);
    }

    public void ClearBounds(RenderableHandle handle)
    {
        int index = Check(handle);
        _hasBoundsOverride[index] = false;
        MarkDirty(index, Dirt.Bounds);
    }

    /// <summary>
    /// Skins the renderable's mesh, which needs bone index and weight streams, with a matrix per bone taking the rest
    /// pose into the renderable's object space. A bone index of 0 is no bone, 1 the first matrix. An empty span stops
    /// skinning. Nothing changes when the matrices equal the stored ones. A skinned renderable culls by its bounds
    /// override, which should hold every pose.
    /// </summary>
    public void SetBones(RenderableHandle handle, ReadOnlySpan<Float4x4> skinMatrices)
    {
        int index = Check(handle);
        Float4x4[]? bones = _bones[index];
        if (skinMatrices.IsEmpty)
        {
            if (bones == null)
                return;
            _bones[index] = null;
        }
        else
        {
            if (bones != null && MemoryMarshal.AsBytes(skinMatrices).SequenceEqual(MemoryMarshal.AsBytes(bones.AsSpan())))
                return;
            if (bones == null || bones.Length != skinMatrices.Length)
                _bones[index] = bones = new Float4x4[skinMatrices.Length];
            skinMatrices.CopyTo(bones);
        }
        MarkDirty(index, Dirt.Bones);
    }

    /// <summary>
    /// Weights the mesh's blend shapes, one per shape from 0 to 100 like Prowl, past either end to over or undershoot.
    /// Shapes past the span weigh 0, and an empty span rests every shape. Nothing changes when the weights equal the
    /// stored ones. A renderable whose mesh has blend shapes culls by its bounds override, which should hold every shape.
    /// </summary>
    public void SetBlendShapeWeights(RenderableHandle handle, ReadOnlySpan<float> weights)
    {
        int index = Check(handle);
        float[]? stored = _shapeWeights[index];
        if (weights.IsEmpty)
        {
            if (stored == null)
                return;
            _shapeWeights[index] = null;
        }
        else
        {
            if (stored != null && weights.SequenceEqual(stored))
                return;
            if (stored == null || stored.Length != weights.Length)
                _shapeWeights[index] = stored = new float[weights.Length];
            weights.CopyTo(stored);
        }
        MarkDirty(index, Dirt.Weights);
    }

    /// <summary>Copies everything that changed since the last call into the render copy. Host thread, at SubmitFrame.</summary>
    internal void Extract()
    {
        ShadowSnapshot = Shadows.Copy();
        ProviderSnapshot = Providers.Snapshot();
        foreach (RenderProvider provider in ProviderSnapshot)
        {
            provider.EnabledSnapshot = provider.Enabled;
            provider.RunsOnSnapshot = provider.RunsOn;
            if (!provider.Enabled)
                continue;
            try
            {
                provider.Extract();
            }
            catch (Exception e)
            {
                provider.EnabledSnapshot = false;
                _report(new RendererError("provider.extract", $"{provider.GetType().Name}.Extract threw, so it draws nothing this frame: {e}", RendererErrorSeverity.Error, provider) { Key = $"provider.extract.{provider.GetType().FullName}" });
            }
        }
        RetireTransients();
        _render.BeginExtract();
        foreach (int index in _dirty)
        {
            Dirt dirt = _dirt[index];
            _dirt[index] = Dirt.None;
            _render.Receive(this, index, dirt);
        }
        _dirty.Clear();
        _render.EndExtract();
        ExtractLighting();
        _hostSets.AddRange(_newHostSets);
        _newHostSets.Clear();
        foreach (Action extract in _hostSetExtracts)
            extract();
    }

    /// <summary>Rebuilds the host's sets' static regions and queues their tables. Render thread.</summary>
    internal void FlushHostSets(Uploader uploader)
    {
        foreach (SpatialIndex set in _hostSets)
        {
            set.Update();
            set.StaticRecords.Flush(uploader);
            set.StaticNodes.Flush(uploader);
        }
    }

    /// <summary>Removes the world from its renderer at the next frame. Host thread.</summary>
    public void Dispose()
    {
        foreach ((_, GpuMesh mesh, bool owned) in _transients)
        {
            if (owned)
                mesh.Dispose();
        }
        foreach ((_, GpuMesh mesh, bool owned) in _drawnTransients)
        {
            if (owned)
                mesh.Dispose();
        }
        _transients.Clear();
        _drawnTransients.Clear();
        if (_owner != null)
            _owner.RemoveWorld(this);
        else
            DisposeRender();
    }

    internal void DisposeRender()
    {
        Reflections.Dispose();
        foreach (string name in _hostSetNames)
            SpatialSlots.Release(_owner?.SpatialSlots, name);
        _hostSetNames.Clear();
    }

    private int Check(RenderableHandle handle)
    {
        if (!IsAlive(handle))
            throw new ArgumentException("The renderable was removed, or the handle is not from this world.", nameof(handle));
        return handle.Index;
    }

    private void MarkDirty(int index, Dirt dirt)
    {
        if (_dirt[index] == Dirt.None)
            _dirty.Add(index);
        _dirt[index] |= dirt;
    }

    private RangeAllocation AllocateSlice(ReadOnlySpan<Material> materials)
    {
        if (materials.IsEmpty)
            throw new ArgumentException("A renderable needs at least one material.", nameof(materials));
        if (!_sliceSpace.TryAllocate((uint)materials.Length, 1, out RangeAllocation slice))
            throw new InvalidOperationException("The world's material slices are full.");

        int end = (int)(slice.Offset + slice.Size);
        if (end > _sliceMaterials.Length)
            Array.Resize(ref _sliceMaterials, Math.Max(end, _sliceMaterials.Length * 2));
        for (int i = 0; i < slice.Size; i++)
            _sliceMaterials[slice.Offset + i] = materials[Math.Min(i, materials.Length - 1)];
        return slice;
    }

    private void FreeSlice(RangeAllocation slice)
    {
        if (slice.Size == 0)
            return;
        _sliceMaterials.AsSpan((int)slice.Offset, (int)slice.Size).Clear();
        _sliceSpace.Free(slice);
    }

    private void Grow(int capacity)
    {
        _capacity = capacity;
        Array.Resize(ref _generation, capacity);
        Array.Resize(ref _alive, capacity);
        Array.Resize(ref _mesh, capacity);
        Array.Resize(ref _slice, capacity);
        Array.Resize(ref _transform, capacity);
        Array.Resize(ref _layerMask, capacity);
        Array.Resize(ref _flags, capacity);
        Array.Resize(ref _custom0, capacity);
        Array.Resize(ref _custom1, capacity);
        Array.Resize(ref _objectId, capacity);
        Array.Resize(ref _lightmap, capacity);
        Array.Resize(ref _sortBias, capacity);
        Array.Resize(ref _boundsOverride, capacity);
        Array.Resize(ref _hasBoundsOverride, capacity);
        Array.Resize(ref _bones, capacity);
        Array.Resize(ref _shapeWeights, capacity);
        Array.Resize(ref _dirt, capacity);
    }


    /// <summary>
    /// The render thread's copy of a world: what culling and bucket building read, and the per object GPU tables.
    /// Extraction writes it while the render thread is idle, then the render thread owns it until the next extraction.
    /// </summary>
    internal sealed class RenderSide
    {
        private readonly HashSet<int> _drawChangedSet = new();
        private readonly Dictionary<GpuMesh, HashSet<int>> _meshUsers = new();
        private readonly Dictionary<Material, HashSet<uint>> _materialEntries = new();
        private readonly Stack<uint> _freePrevious = new();
        private uint _nextPrevious;
        private List<int> _movedLastFrame = new();
        private List<int> _movedThisFrame = new();
        private bool[] _moved = [];
        private ObjectAttributes[] _attributes = [];
        private readonly RangeAllocator _sliceSpace = new(MaxSliceEntries);
        private RangeAllocation[] _sliceRange = [];
        private readonly WorldProbes _probes;
        private uint[] _probeSlot = [];
        private readonly Action<RendererError> _report;
        private readonly RangeAllocator _boneSpace = new(MaxSliceEntries);
        private RangeAllocation[] _boneRange = [];
        private MeshPlacement?[] _deformSource = [];
        private readonly HashSet<int> _deformDirty = new();

        public RenderSide(TableHost tables, WorldProbes probes, Action<RendererError> report)
        {
            _probes = probes;
            _report = report;
            Transforms = tables.Of<ObjectTransform>("Transforms");
            PreviousTransforms = tables.Of<ObjectTransform>("PreviousTransforms", 64);
            Bounds = tables.Of<ObjectBounds>("Bounds");
            Attributes = tables.Of<ObjectAttributes>("Attributes");
            MaterialSlots = tables.Table("MaterialSlots", 1);
            SkinMatrices = tables.Of<Float4x4>("SkinMatrices", 64);
            Tables = [Transforms, PreviousTransforms, Bounds, Attributes, MaterialSlots, SkinMatrices];
        }

        /// <summary>Every skinned renderable's matrices, each in its own range.</summary>
        public GpuTable SkinMatrices { get; }

        /// <summary>A deforming renderable's copy of its mesh, which its draws read. Null for the rest.</summary>
        public MeshPlacement?[] Deformed = [];

        /// <summary>Whether a deforming renderable's copy is behind its bones, so the next frame a view sees it deforms it.</summary>
        public bool[] DeformStale = [];

        /// <summary>The frame stamp of the last cull that saw a deforming renderable.</summary>
        public uint[] DeformSeen = [];

        /// <summary>This frame's stamp, so <see cref="DeformSeen"/> equal to it means some view sees the renderable.</summary>
        public uint DeformStamp { get; private set; }

        /// <summary>Renderables with a deformed copy.</summary>
        public HashSet<int> Deforming { get; } = new();

        /// <summary>Copies deforming this frame, decided by <see cref="PrepareDeformation"/>.</summary>
        public bool[] DeformNow = [];

        /// <summary>The first word of the positions a copy deforms into this frame, its current ones.</summary>
        public uint[] PoseTarget = [];

        // Whether a copy's second position range is its current one, whether its current pose was drawn, and whether a
        // view saw it last frame
        private bool[] _secondCurrent = [];
        private bool[] _hasPose = [];
        private bool[] _seenLastFrame = [];

        /// <summary>A renderable's blend shape weights, and the frames they pick with the weight each adds.</summary>
        public float[]?[] ShapeWeights = [];
        public (int Frame, float Weight)[][] ActiveFrames = [];

        /// <summary>Copies made this frame, each of a renderable, whose data the GPU copies from their mesh.</summary>
        public List<(int Index, MeshPlacement From, MeshPlacement To)> NewCopies { get; } = new();

        /// <summary>The placement a renderable draws: its deformed copy, else its mesh's.</summary>
        public MeshPlacement? PlacementOf(int index) => Deformed[index] ?? Mesh[index]?.Placement;

        /// <summary>The first skin matrix of a renderable in <see cref="SkinMatrices"/>, and how many it has.</summary>
        public (uint First, uint Count) BonesOf(int index) => (_boneRange[index].Offset, _boneRange[index].Size);

        /// <summary>Every per object table, in a stable order.</summary>
        public GpuTable[] Tables { get; }

        public int Capacity { get; private set; }
        public bool[] Alive = [];
        public GpuMesh?[] Mesh = [];
        public Double4x4[] Transform = [];
        public uint[] LayerMask = [];
        public RenderableFlags[] Flags = [];
        public bool[] Mirrored = [];
        public sbyte[] SortBias = [];
        public AABB?[] BoundsOverride = [];
        public Double3[] WorldCenter = [];
        public Float3[] WorldExtents = [];
        public float[] WorldRadius = [];
        public uint[] SliceBase = [];
        public int[] SliceCount = [];
        public int[] GivenCount = [];
        public uint[] Previous = [];
        public Material?[] SliceMaterials = new Material?[256];

        public GpuTable Transforms { get; }
        public GpuTable PreviousTransforms { get; }
        public GpuTable Bounds { get; }
        public GpuTable Attributes { get; }

        /// <summary>The material's slot in its shader's table for every slice entry, one entry per submesh at least.</summary>
        public GpuTable MaterialSlots { get; }

        /// <summary>Renderables whose draws changed this frame: added, removed, new mesh or mesh data, materials, flags or mirroring.</summary>
        public List<int> DrawChanged { get; } = new();

        /// <summary>Renderables whose world bounds changed this frame.</summary>
        public List<int> BoundsChanged { get; } = new();
        private readonly HashSet<int> _boundsChangedSet = new();

        /// <summary>Where shadow casters changed since the shadows last looked, each before and after, static ones apart.</summary>
        public List<(Double3 Center, float Radius, bool Static)> ShadowChanges { get; } = new();

        private void ShadowChanged(int index)
        {
            if (Alive[index] && (Flags[index] & RenderableFlags.CastShadows) != 0)
                ShadowChanges.Add((WorldCenter[index], WorldRadius[index], (Flags[index] & RenderableFlags.Static) != 0));
        }

        private uint _transientObjects;

        /// <summary>Forgets last frame's provider objects and starts a frame of culls. Render thread, before any view.</summary>
        public void BeginFrame()
        {
            _transientObjects = 0;
            foreach (int index in Deforming)
                _seenLastFrame[index] = DeformSeen[index] == DeformStamp;
            DeformStamp++;
        }

        /// <summary>
        /// Decides which copies deform this frame, those behind their bones or weights that a view saw, and which
        /// position range those keeping a previous pose deform into, so last frame's pose stays for motion vectors. A
        /// copy that keeps still, or that no view saw last frame, has no motion of its own. Nothing is kept until
        /// <see cref="CommitDeformation"/>, so a frame that fails first decides again. Render thread, after every cull
        /// and before the tables flush.
        /// </summary>
        public void PrepareDeformation()
        {
            foreach (int index in Deforming)
            {
                MeshPlacement copy = Deformed[index]!;
                bool deforms = DeformStale[index] && DeformSeen[index] == DeformStamp;
                DeformNow[index] = deforms;
                if (copy.SecondPositions == MeshPlacement.None)
                {
                    PoseTarget[index] = copy.StreamOffsets[(int)VertexSemantic.Position];
                    continue;
                }

                bool swaps = deforms && _hasPose[index];
                bool moves = swaps && _seenLastFrame[index];
                bool secondCurrent = _secondCurrent[index] != swaps;
                uint first = copy.StreamOffsets[(int)VertexSemantic.Position];
                uint current = secondCurrent ? copy.SecondPositions : first;
                uint previous = secondCurrent ? first : copy.SecondPositions;
                PoseTarget[index] = current;

                ref ObjectAttributes attributes = ref _attributes[index];
                (uint currentWord, uint previousWord) = (current + 1, (moves ? previous : current) + 1);
                if (attributes.CurrentPositions == currentWord && attributes.PreviousPositions == previousWord)
                    continue;
                (attributes.CurrentPositions, attributes.PreviousPositions) = (currentWord, previousWord);
                Attributes.Set((uint)index, attributes);
            }
        }

        /// <summary>Keeps what the deform pass recorded this frame: the copies it filled and the poses it deformed. Render thread.</summary>
        public void CommitDeformation()
        {
            foreach (int index in Deforming)
            {
                if (!DeformNow[index])
                    continue;
                DeformStale[index] = false;
                if (Deformed[index]!.SecondPositions == MeshPlacement.None)
                    continue;
                if (_hasPose[index])
                    _secondCurrent[index] = !_secondCurrent[index];
                _hasPose[index] = true;
            }
            NewCopies.Clear();
        }

        /// <summary>An object record for one frame, after every renderable's, for a provider's item. Render thread.</summary>
        public uint AddTransientObject(in Double4x4 transform, in ObjectAttributes attributes)
        {
            uint index = (uint)Capacity + _transientObjects++;
            Transforms.Set(index, new ObjectTransform(transform));
            Attributes.Set(index, attributes);
            return index;
        }

        public void BeginExtract()
        {
            DrawChanged.Clear();
            _drawChangedSet.Clear();
            BoundsChanged.Clear();
            _boundsChangedSet.Clear();
            (_movedLastFrame, _movedThisFrame) = (_movedThisFrame, _movedLastFrame);
            _movedThisFrame.Clear();
        }

        public void Receive(RenderWorld host, int index, Dirt dirt)
        {
            if (index >= Capacity)
                Grow(Math.Max(index + 1, Math.Max(64, Capacity * 2)));

            ShadowChanged(index);
            if ((dirt & Dirt.Removed) != 0 && Alive[index])
                Retire(index);
            if (!host._alive[index])
                return;

            bool added = (dirt & Dirt.Added) != 0;
            Alive[index] = true;
            Flags[index] = host._flags[index];
            LayerMask[index] = host._layerMask[index];
            SortBias[index] = host._sortBias[index];
            BoundsOverride[index] = host._hasBoundsOverride[index] ? host._boundsOverride[index] : null;

            ref ObjectAttributes attributes = ref _attributes[index];
            attributes.Flags = (uint)host._flags[index];
            attributes.ObjectId = host._objectId[index];
            LightmapBinding lightmap = host._lightmap[index];
            (attributes.LightmapTexture, attributes.LightmapSampler) = host.LightmapSlots(lightmap);
            attributes.LightmapScaleOffset = lightmap.Lightmapped ? lightmap.ScaleOffset : new Float4(1, 1, 0, 0);
            attributes.Custom0 = host._custom0[index];
            attributes.Custom1 = host._custom1[index];
            attributes.LodFade = 1;
            attributes.LayerMask = host._layerMask[index];
            attributes.ProbeIndex = UpdateProbeSlot(index, _probes.Active && !lightmap.Lightmapped);
            if (added)
                attributes.PreviousTransform = ObjectAttributes.NoPrevious;
            if ((dirt & (Dirt.Bones | Dirt.Added)) != 0)
                ReceiveBones(index, host._bones[index]);
            if ((dirt & (Dirt.Weights | Dirt.Added)) != 0)
            {
                float[]? weights = host._shapeWeights[index];
                if (weights == null)
                    ShapeWeights[index] = null;
                else if (ShapeWeights[index]?.Length == weights.Length)
                    weights.CopyTo(ShapeWeights[index]!, 0);
                else
                    ShapeWeights[index] = (float[])weights.Clone();
            }
            if ((dirt & (Dirt.Bones | Dirt.Weights | Dirt.Added | Dirt.Draw | Dirt.Bounds)) != 0)
                _deformDirty.Add(index);

            if ((dirt & Dirt.Draw) != 0)
            {
                GpuMesh mesh = host._mesh[index]!;
                if (Mesh[index] != mesh)
                {
                    RemoveMeshUser(index);
                    Mesh[index] = mesh;
                    AddMeshUser(mesh, index);
                }

                RemoveSliceEntries(index);
                RangeAllocation slice = host._slice[index];
                WriteSlice(index, host._sliceMaterials.AsSpan((int)slice.Offset, (int)slice.Size));
                ChangedDraw(index);
            }

            if ((dirt & Dirt.Transform) != 0)
            {
                Double4x4 previous = added ? host._transform[index] : Transform[index];
                Transform[index] = host._transform[index];
                bool mirrored = Determinant3(Transform[index]) < 0;
                if (mirrored != Mirrored[index])
                    ChangedDraw(index);
                Mirrored[index] = mirrored;
                Transforms.Set((uint)index, new ObjectTransform(Transform[index]));
                UpdatePrevious(index, previous);
                if (!added && !_moved[index])
                {
                    _moved[index] = true;
                    _movedThisFrame.Add(index);
                }
            }

            if ((dirt & (Dirt.Transform | Dirt.Bounds | Dirt.Draw)) != 0)
                UpdateBounds(index);
            Attributes.Set((uint)index, attributes);
            ShadowChanged(index);
        }

        // A bone count change moves the renderable's matrices to a new range
        private void ReceiveBones(int index, Float4x4[]? bones)
        {
            uint count = (uint)(bones?.Length ?? 0);
            if (count != _boneRange[index].Size)
            {
                FreeBones(index);
                if (count > 0 && !_boneSpace.TryAllocate(count, 1, out _boneRange[index]))
                    throw new InvalidOperationException("The world's skin matrices are full.");
            }
            for (int i = 0; i < count; i++)
                SkinMatrices.Set(_boneRange[index].Offset + (uint)i, bones![i]);
            DeformStale[index] = true;
        }

        private void FreeBones(int index)
        {
            if (_boneRange[index].Size > 0)
                _boneSpace.Free(_boneRange[index]);
            _boneRange[index] = default;
        }

        /// <summary>
        /// Makes, remakes or frees deformed copies after renderables' bones, meshes or mesh data changed. Freed ranges
        /// wait for <paramref name="lastDispatched"/>. Render thread, after <see cref="Update"/> and before the buckets.
        /// </summary>
        public void UpdateDeformation(GeometryPool geometry, ulong lastDispatched)
        {
            foreach (int index in _deformDirty)
            {
                MeshPlacement? source = index < Capacity && Alive[index] && Mesh[index]?.Placement is { } placed
                    && (Skins(index, placed) || placed.BlendFrames.Length > 0) ? placed : null;
                bool keepsPose = source != null && Skins(index, source) && (Flags[index] & RenderableFlags.MotionVectors) != 0;
                if (source != null)
                {
                    ActiveFrames[index] = PickFrames(source, ShapeWeights[index]);
                    DeformStale[index] = true;
                }
                if (source != null && source == _deformSource[index] && keepsPose == (Deformed[index]!.SecondPositions != MeshPlacement.None))
                {
                    if (BoundsOverride[index] == null)
                        ReportBounds(index);
                    continue;
                }
                if (Deformed[index] is { } old)
                {
                    NewCopies.RemoveAll(made => made.To == old);
                    geometry.ReleaseCopy(old, lastDispatched);
                    Deformed[index] = null;
                    _deformSource[index] = null;
                    Deforming.Remove(index);
                    ChangedDraw(index);
                    if (index < Capacity && Alive[index])
                    {
                        (_attributes[index].CurrentPositions, _attributes[index].PreviousPositions) = (0, 0);
                        Attributes.Set((uint)index, _attributes[index]);
                    }
                }
                if (source == null)
                    continue;

                MeshPlacement copy = geometry.PlaceCopy(Mesh[index]!, source, keepsPose);
                (_secondCurrent[index], _hasPose[index]) = (false, false);
                Deformed[index] = copy;
                _deformSource[index] = source;
                DeformStale[index] = true;
                Deforming.Add(index);
                NewCopies.Add((index, source, copy));
                ChangedDraw(index);
                if (BoundsOverride[index] == null)
                    ReportBounds(index);
            }
            _deformDirty.Clear();
        }

        /// <summary>Whether a renderable skins its mesh: it has bones and the mesh has bone streams.</summary>
        public bool Skins(int index, MeshPlacement mesh)
        {
            const uint boneStreams = (1u << (int)VertexSemantic.BoneIndices) | (1u << (int)VertexSemantic.BoneWeights);
            return _boneRange[index].Size > 0 && (mesh.StreamsPresent & boneStreams) == boneStreams;
        }

        // Prowl's frame pick: a lone frame scales by weight over its weight, several interpolate between the two around
        // the weight, clamped to the last; near zero weights and picks are left out
        private static (int Frame, float Weight)[] PickFrames(MeshPlacement mesh, float[]? weights)
        {
            if (weights == null || mesh.BlendShapes.Length == 0)
                return [];
            List<(int, float)> picked = new();
            void Pick(int frame, float weight)
            {
                if (MathF.Abs(weight) >= 1e-5f)
                    picked.Add((frame, weight));
            }
            for (int s = 0; s < mesh.BlendShapes.Length && s < weights.Length; s++)
            {
                float w = weights[s];
                (int first, int count) = mesh.BlendShapes[s];
                if (MathF.Abs(w) < 1e-4f || count == 0)
                    continue;
                float firstWeight = mesh.BlendFrames[first].Weight, lastWeight = mesh.BlendFrames[first + count - 1].Weight;
                if (count == 1 || w <= firstWeight)
                {
                    Pick(first, firstWeight != 0 ? w / firstWeight : 0);
                    continue;
                }
                if (w >= lastWeight)
                {
                    Pick(first + count - 1, 1);
                    continue;
                }
                int i = 0;
                while (i < count - 1 && mesh.BlendFrames[first + i + 1].Weight <= w)
                    i++;
                float a = mesh.BlendFrames[first + i].Weight, b = mesh.BlendFrames[first + i + 1].Weight;
                float t = b > a ? (w - a) / (b - a) : 0;
                Pick(first + i, 1 - t);
                Pick(first + i + 1, t);
            }
            return [.. picked];
        }

        private void ReportBounds(int index)
            => _report(new RendererError("deform.bounds", $"Renderable {index} deforms without a bounds override, so it culls by its rest pose. SetBounds with bounds holding every pose.", RendererErrorSeverity.Warning) { Key = $"deform.bounds.{index}" });

        /// <summary>Applies the previous transform rule: what moved last frame and not this one gets its previous transform caught up.</summary>
        public void EndExtract()
        {
            foreach (int index in _movedLastFrame)
            {
                if (Alive[index] && !_moved[index])
                    UpdatePrevious(index, Transform[index]);
            }
            foreach (int index in _movedThisFrame)
                _moved[index] = false;
        }

        /// <summary>Follows meshes whose data changed and materials whose slot changed. Render thread, after the geometry apply.</summary>
        public void Update(IReadOnlyList<GpuMesh> changedMeshes, IReadOnlyList<Material> changedMaterials)
        {
            foreach (GpuMesh mesh in changedMeshes)
            {
                if (!_meshUsers.TryGetValue(mesh, out HashSet<int>? users))
                    continue;
                foreach (int index in users)
                {
                    _deformDirty.Add(index);
                    ShadowChanged(index);
                    UpdateBounds(index);
                    ShadowChanged(index);
                    Repad(index);
                    ChangedDraw(index);
                }
            }

            foreach (Material material in changedMaterials)
            {
                if (!_materialEntries.TryGetValue(material, out HashSet<uint>? entries))
                    continue;
                uint slot = SlotOf(material);
                foreach (uint entry in entries)
                    MaterialSlots.Set(entry, slot);
            }
        }

        /// <summary>Queues the dirty records. Returns true when a table outgrew its buffer. Render thread.</summary>
        public bool Flush(Uploader uploader)
        {
            bool resized = false;
            foreach (GpuTable table in Tables)
                resized |= table.Flush(uploader);
            return resized;
        }

        private static uint SlotOf(Material material) => material.IsDisposed || material.Table == null ? uint.MaxValue : material.Slot;

        private void UpdatePrevious(int index, in Double4x4 previous)
        {
            ref ObjectAttributes attributes = ref _attributes[index];
            if ((Flags[index] & RenderableFlags.MotionVectors) == 0)
            {
                if (Previous[index] != ObjectAttributes.NoPrevious)
                    _freePrevious.Push(Previous[index]);
                Previous[index] = ObjectAttributes.NoPrevious;
            }
            else
            {
                if (Previous[index] == ObjectAttributes.NoPrevious)
                    Previous[index] = _freePrevious.Count > 0 ? _freePrevious.Pop() : _nextPrevious++;
                PreviousTransforms.Set(Previous[index], new ObjectTransform(previous));
            }

            attributes.PreviousTransform = Previous[index];
            Attributes.Set((uint)index, attributes);
        }

        /// <summary>Gives or takes harmonics slots after the probes or the lightmap pages changed. Extraction.</summary>
        public void RefreshProbeSlots()
        {
            for (int index = 0; index < Capacity; index++)
            {
                if (!Alive[index])
                    continue;
                uint slot = UpdateProbeSlot(index, _probes.Active && _attributes[index].LightmapTexture == ObjectAttributes.NoLightmap);
                if (_attributes[index].ProbeIndex == slot)
                    continue;
                _attributes[index].ProbeIndex = slot;
                Attributes.Set((uint)index, _attributes[index]);
            }
        }

        // Renderables the probes light keep a harmonics slot, sampled again whenever their bounds move.
        private uint UpdateProbeSlot(int index, bool wanted)
        {
            if (wanted && _probeSlot[index] == WorldProbes.NoSlot)
                _probeSlot[index] = _probes.Assign(index);
            else if (!wanted && _probeSlot[index] != WorldProbes.NoSlot)
                ReleaseProbeSlot(index);
            return _probeSlot[index];
        }

        private void ReleaseProbeSlot(int index)
        {
            if (_probeSlot[index] == WorldProbes.NoSlot)
                return;
            _probes.Release(_probeSlot[index]);
            _probeSlot[index] = WorldProbes.NoSlot;
        }

        private void UpdateBounds(int index)
        {
            AABB local = BoundsOverride[index] ?? Mesh[index]?.Placement?.Bounds ?? default;
            (Double3 center, Float3 extents) = TransformBox(Transform[index], local);
            WorldCenter[index] = center;
            WorldExtents[index] = extents;
            WorldRadius[index] = MathF.Sqrt(extents.X * extents.X + extents.Y * extents.Y + extents.Z * extents.Z);
            if (_boundsChangedSet.Add(index))
                BoundsChanged.Add(index);
            if (_probeSlot[index] != WorldProbes.NoSlot)
                _probes.Request(_probeSlot[index]);
            (Float3 high, Float3 low) = Precise.Split(center);
            Bounds.Set((uint)index, new ObjectBounds
            {
                CenterHighRadius = new Float4(high.X, high.Y, high.Z, WorldRadius[index]),
                CenterLow = new Float4(low.X, low.Y, low.Z, 0),
                Extents = new Float4(extents.X, extents.Y, extents.Z, 0),
            });
        }

        /// <summary>The world center and axis aligned half size of a local box under a transform.</summary>
        public static (Double3 Center, Float3 Extents) TransformBox(in Double4x4 m, in AABB local)
        {
            Float3 c = (local.Min + local.Max) * 0.5f;
            Float3 e = (local.Max - local.Min) * 0.5f;
            Double3 center = new(
                m.c0.X * c.X + m.c1.X * c.Y + m.c2.X * c.Z + m.c3.X,
                m.c0.Y * c.X + m.c1.Y * c.Y + m.c2.Y * c.Z + m.c3.Y,
                m.c0.Z * c.X + m.c1.Z * c.Y + m.c2.Z * c.Z + m.c3.Z);
            Float3 extents = new(
                (float)(Math.Abs(m.c0.X) * e.X + Math.Abs(m.c1.X) * e.Y + Math.Abs(m.c2.X) * e.Z),
                (float)(Math.Abs(m.c0.Y) * e.X + Math.Abs(m.c1.Y) * e.Y + Math.Abs(m.c2.Y) * e.Z),
                (float)(Math.Abs(m.c0.Z) * e.X + Math.Abs(m.c1.Z) * e.Y + Math.Abs(m.c2.Z) * e.Z));
            return (center, extents);
        }

        // A slice covers every submesh of the mesh, the last given material repeating, so any draw's subMesh indexes it.
        private void WriteSlice(int index, ReadOnlySpan<Material?> given)
        {
            int count = Math.Max(given.Length, Math.Max(Mesh[index]?.Placement?.SubMeshes.Length ?? 1, 1));
            if (!_sliceSpace.TryAllocate((uint)count, 1, out RangeAllocation range))
                throw new InvalidOperationException("The world's material slots are full.");

            uint sliceBase = range.Offset;
            int end = (int)sliceBase + count;
            if (end > SliceMaterials.Length)
                Array.Resize(ref SliceMaterials, Math.Max(end, SliceMaterials.Length * 2));
            _sliceRange[index] = range;
            SliceBase[index] = sliceBase;
            SliceCount[index] = count;
            GivenCount[index] = given.Length;
            _attributes[index].MaterialSliceBase = sliceBase;

            for (int i = 0; i < count; i++)
            {
                uint entry = sliceBase + (uint)i;
                Material material = given[Math.Min(i, given.Length - 1)]!;
                SliceMaterials[entry] = material;
                if (!_materialEntries.TryGetValue(material, out HashSet<uint>? entries))
                {
                    entries = new HashSet<uint>();
                    _materialEntries[material] = entries;
                }
                entries.Add(entry);
                MaterialSlots.Set(entry, SlotOf(material));
            }
        }

        private void RemoveSliceEntries(int index)
        {
            for (int i = 0; i < SliceCount[index]; i++)
            {
                uint entry = SliceBase[index] + (uint)i;
                Material? material = SliceMaterials[entry];
                if (material != null && _materialEntries.TryGetValue(material, out HashSet<uint>? entries))
                {
                    entries.Remove(entry);
                    if (entries.Count == 0)
                        _materialEntries.Remove(material);
                }
                SliceMaterials[entry] = null;
            }

            if (SliceCount[index] > 0)
                _sliceSpace.Free(_sliceRange[index]);
            SliceCount[index] = 0;
        }

        // Rebuilds a slice whose mesh gained or lost submeshes, from the materials it was given.
        private void Repad(int index)
        {
            int wanted = Math.Max(GivenCount[index], Math.Max(Mesh[index]?.Placement?.SubMeshes.Length ?? 1, 1));
            if (wanted == SliceCount[index])
                return;

            Material?[] given = SliceMaterials.AsSpan((int)SliceBase[index], GivenCount[index]).ToArray();
            RemoveSliceEntries(index);
            WriteSlice(index, given);
            Attributes.Set((uint)index, _attributes[index]);
        }

        private void Retire(int index)
        {
            RemoveSliceEntries(index);
            RemoveMeshUser(index);
            FreeBones(index);
            _deformDirty.Add(index);
            (_hasPose[index], _seenLastFrame[index]) = (false, false);
            Mesh[index] = null;
            if (Previous[index] != ObjectAttributes.NoPrevious)
                _freePrevious.Push(Previous[index]);
            Previous[index] = ObjectAttributes.NoPrevious;
            ReleaseProbeSlot(index);
            Mirrored[index] = false;
            Alive[index] = false;
            _attributes[index] = new ObjectAttributes { Flags = (uint)RenderableFlags.Hidden, PreviousTransform = ObjectAttributes.NoPrevious, LightmapTexture = ObjectAttributes.NoLightmap, ProbeIndex = WorldProbes.NoSlot };
            Attributes.Set((uint)index, _attributes[index]);
            ChangedDraw(index);
        }

        private void ChangedDraw(int index)
        {
            if (_drawChangedSet.Add(index))
                DrawChanged.Add(index);
        }

        private void AddMeshUser(GpuMesh mesh, int index)
        {
            if (!_meshUsers.TryGetValue(mesh, out HashSet<int>? users))
            {
                users = new HashSet<int>();
                _meshUsers[mesh] = users;
            }
            users.Add(index);
        }

        private void RemoveMeshUser(int index)
        {
            GpuMesh? mesh = Mesh[index];
            if (mesh == null || !_meshUsers.TryGetValue(mesh, out HashSet<int>? users))
                return;
            users.Remove(index);
            if (users.Count == 0)
                _meshUsers.Remove(mesh);
        }

        internal static double Determinant3(in Double4x4 m)
            => m.c0.X * (m.c1.Y * m.c2.Z - m.c2.Y * m.c1.Z)
             - m.c1.X * (m.c0.Y * m.c2.Z - m.c2.Y * m.c0.Z)
             + m.c2.X * (m.c0.Y * m.c1.Z - m.c1.Y * m.c0.Z);

        private void Grow(int capacity)
        {
            Capacity = capacity;
            Array.Resize(ref Alive, capacity);
            Array.Resize(ref Mesh, capacity);
            Array.Resize(ref Transform, capacity);
            Array.Resize(ref LayerMask, capacity);
            Array.Resize(ref Flags, capacity);
            Array.Resize(ref Mirrored, capacity);
            Array.Resize(ref SortBias, capacity);
            Array.Resize(ref BoundsOverride, capacity);
            Array.Resize(ref WorldCenter, capacity);
            Array.Resize(ref WorldExtents, capacity);
            Array.Resize(ref WorldRadius, capacity);
            Array.Resize(ref SliceBase, capacity);
            Array.Resize(ref SliceCount, capacity);
            Array.Resize(ref GivenCount, capacity);
            Array.Resize(ref _sliceRange, capacity);
            Array.Resize(ref _moved, capacity);
            Array.Resize(ref _attributes, capacity);
            Array.Resize(ref _boneRange, capacity);
            Array.Resize(ref _deformSource, capacity);
            Array.Resize(ref Deformed, capacity);
            Array.Resize(ref DeformStale, capacity);
            Array.Resize(ref DeformSeen, capacity);
            Array.Resize(ref DeformNow, capacity);
            Array.Resize(ref PoseTarget, capacity);
            Array.Resize(ref _secondCurrent, capacity);
            Array.Resize(ref _hasPose, capacity);
            Array.Resize(ref _seenLastFrame, capacity);
            Array.Resize(ref ShapeWeights, capacity);
            int oldFrames = ActiveFrames.Length;
            Array.Resize(ref ActiveFrames, capacity);
            Array.Fill(ActiveFrames, [], oldFrames, capacity - oldFrames);
            int old = Previous.Length;
            Array.Resize(ref Previous, capacity);
            Array.Fill(Previous, ObjectAttributes.NoPrevious, old, capacity - old);
            Array.Resize(ref _probeSlot, capacity);
            Array.Fill(_probeSlot, WorldProbes.NoSlot, old, capacity - old);
        }
    }
}
