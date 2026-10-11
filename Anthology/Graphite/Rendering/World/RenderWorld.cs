using System;
using System.Collections.Generic;
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

    /// <summary>A hint: joins the static cluster structure and the static shadow layer.</summary>
    Static = 1 << 0,
    CastShadows = 1 << 1,
    ReceiveShadows = 1 << 2,

    /// <summary>Keeps a previous transform and writes per object motion.</summary>
    MotionVectors = 1 << 3,
    Hidden = 1 << 4,
    ShadowsOnly = 1 << 5,
}

/// <summary>Which lightmap page a renderable samples and where its UVs land on it. A negative page means none.</summary>
public readonly record struct LightmapBinding(int Page, Float4 ScaleOffset)
{
    public static readonly LightmapBinding None = new(-1, new Float4(1, 1, 0, 0));
}

public struct RenderableDesc
{
    public GpuMesh Mesh;
    public Float4x4 Transform;

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
        Transform = Float4x4.Identity;
        LayerMask = ~0u;
        Flags = RenderableFlags.CastShadows | RenderableFlags.ReceiveShadows;
        Lightmap = LightmapBinding.None;
    }
}

/// <summary>The world matrix as the GPU reads it: the three rows of the 3x4, 48 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ObjectTransform
{
    public Float4 Row0, Row1, Row2;

    public ObjectTransform(in Float4x4 m)
    {
        Row0 = new Float4(m.c0.X, m.c1.X, m.c2.X, m.c3.X);
        Row1 = new Float4(m.c0.Y, m.c1.Y, m.c2.Y, m.c3.Y);
        Row2 = new Float4(m.c0.Z, m.c1.Z, m.c2.Z, m.c3.Z);
    }
}

/// <summary>World bounds for culling: a sphere and a box, 48 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ObjectBounds
{
    public Float4 Sphere;
    public Float3 Min;
    private uint _pad0;
    public Float3 Max;
    private uint _pad1;
}

/// <summary>Per object data shaders read as needed, 80 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ObjectAttributes
{
    public const uint NoPrevious = uint.MaxValue;

    public uint MaterialSliceBase;
    public uint Flags;
    public uint ObjectId;
    public uint PreviousTransform;
    public int LightmapPage;
    public uint ProbeIndex;
    public float LodFade;
    private uint _pad;
    public Float4 LightmapScaleOffset;
    public Float4 Custom0;
    public Float4 Custom1;
}

/// <summary>
/// A scene's renderable content. Mutation is host thread only and recorded for extraction: setters write a host copy
/// and a dirty bit, <see cref="Extract"/> copies what changed into the render copy, and the render thread turns that
/// into GPU records. Storage is struct of arrays keyed by a dense index with a free list.
/// </summary>
public sealed class RenderWorld : IDisposable
{
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
    }

    private const uint MaxSliceEntries = 1u << 24;

    private int _capacity;
    private int _count;
    private readonly Stack<int> _free = new();
    private int[] _generation = [];
    private bool[] _alive = [];
    private GpuMesh?[] _mesh = [];
    private RangeAllocation[] _slice = [];
    private Float4x4[] _transform = [];
    private uint[] _layerMask = [];
    private RenderableFlags[] _flags = [];
    private Float4[] _custom0 = [];
    private Float4[] _custom1 = [];
    private uint[] _objectId = [];
    private LightmapBinding[] _lightmap = [];
    private sbyte[] _sortBias = [];
    private AABB[] _boundsOverride = [];
    private bool[] _hasBoundsOverride = [];
    private Dirt[] _dirt = [];
    private readonly List<int> _dirty = new();

    private readonly RangeAllocator _sliceSpace = new(MaxSliceEntries);
    private Material?[] _sliceMaterials = new Material?[256];

    private readonly RenderSide _render;

    internal RenderWorld(GraphicsDevice device)
    {
        _render = new RenderSide(device);
        Grow(64);
    }

    /// <summary>Live renderables.</summary>
    public int Count { get; private set; }

    internal RenderSide Render => _render;

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
        FreeSlice(_slice[index]);
        _slice[index] = default;
        _free.Push(index);
        Count--;
        MarkDirty(index, Dirt.Removed);
    }

    public bool IsAlive(RenderableHandle handle)
        => (uint)handle.Index < (uint)_count && _alive[handle.Index] && _generation[handle.Index] == handle.Generation;

    /// <summary>Does nothing when the transform is equal to the stored one.</summary>
    public void SetTransform(RenderableHandle handle, in Float4x4 transform)
    {
        int index = Check(handle);
        if (_transform[index] == transform)
            return;
        _transform[index] = transform;
        MarkDirty(index, Dirt.Transform);
    }

    public Float4x4 GetTransform(RenderableHandle handle) => _transform[Check(handle)];

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

    /// <summary>Copies everything that changed since the last call into the render copy. Host thread, at SubmitFrame.</summary>
    internal void Extract()
    {
        _render.BeginExtract();
        foreach (int index in _dirty)
        {
            Dirt dirt = _dirt[index];
            _dirt[index] = Dirt.None;
            _render.Receive(this, index, dirt);
        }
        _dirty.Clear();
        _render.EndExtract();
    }

    public void Dispose() => _render.Dispose();

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
        Array.Resize(ref _dirt, capacity);
    }


    /// <summary>
    /// The render thread's copy of a world: what culling and bucket building read, and the per object GPU tables.
    /// Extraction writes it while the render thread is idle, then the render thread owns it until the next extraction.
    /// </summary>
    internal sealed class RenderSide : IDisposable
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

        public RenderSide(GraphicsDevice device)
        {
            Transforms = GpuTable.Of<ObjectTransform>(device, "Object transforms");
            PreviousTransforms = GpuTable.Of<ObjectTransform>(device, "Object previous transforms", 64);
            Bounds = GpuTable.Of<ObjectBounds>(device, "Object bounds");
            Attributes = GpuTable.Of<ObjectAttributes>(device, "Object attributes");
            MaterialSlots = new GpuTable(device, "Material slots", 1);
        }

        public int Capacity { get; private set; }
        public bool[] Alive = [];
        public GpuMesh?[] Mesh = [];
        public Float4x4[] Transform = [];
        public uint[] LayerMask = [];
        public RenderableFlags[] Flags = [];
        public bool[] Mirrored = [];
        public sbyte[] SortBias = [];
        public AABB?[] BoundsOverride = [];
        public AABB[] WorldBounds = [];
        public Float4[] WorldSphere = [];
        public uint[] SliceBase = [];
        public int[] SliceCount = [];
        public uint[] Previous = [];
        public Material?[] SliceMaterials = new Material?[256];

        public GpuTable Transforms { get; }
        public GpuTable PreviousTransforms { get; }
        public GpuTable Bounds { get; }
        public GpuTable Attributes { get; }

        /// <summary>
        /// The material's slot in its shader's table for every slice entry. A slice holds the materials as given, so draws
        /// of submeshes past its end use its last entry.
        /// </summary>
        public GpuTable MaterialSlots { get; }

        /// <summary>Renderables whose draws changed this frame: added, removed, new mesh or mesh data, materials, flags or mirroring.</summary>
        public List<int> DrawChanged { get; } = new();

        public void BeginExtract()
        {
            DrawChanged.Clear();
            _drawChangedSet.Clear();
            (_movedLastFrame, _movedThisFrame) = (_movedThisFrame, _movedLastFrame);
            _movedThisFrame.Clear();
        }

        public void Receive(RenderWorld host, int index, Dirt dirt)
        {
            if (index >= Capacity)
                Grow(Math.Max(index + 1, Math.Max(64, Capacity * 2)));

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
            attributes.LightmapPage = host._lightmap[index].Page;
            attributes.LightmapScaleOffset = host._lightmap[index].ScaleOffset;
            attributes.Custom0 = host._custom0[index];
            attributes.Custom1 = host._custom1[index];
            attributes.LodFade = 1;
            if (added)
                attributes.PreviousTransform = ObjectAttributes.NoPrevious;

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
                WriteSlice(index, slice.Offset, host._sliceMaterials.AsSpan((int)slice.Offset, (int)slice.Size));
                attributes.MaterialSliceBase = slice.Offset;
                ChangedDraw(index);
            }

            if ((dirt & Dirt.Transform) != 0)
            {
                Float4x4 previous = added ? host._transform[index] : Transform[index];
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
        }

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
                    UpdateBounds(index);
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

        /// <summary>Queues the dirty records, adding tables that moved to a new buffer to <paramref name="replaced"/>. Render thread.</summary>
        public bool Flush(Uploader uploader, List<DeviceBuffer> replaced)
        {
            int before = replaced.Count;
            foreach (GpuTable table in (GpuTable[])[Transforms, PreviousTransforms, Bounds, Attributes, MaterialSlots])
            {
                if (table.Flush(uploader) is { } old)
                    replaced.Add(old);
            }
            return replaced.Count != before;
        }

        public void Dispose()
        {
            foreach (GpuTable table in (GpuTable[])[Transforms, PreviousTransforms, Bounds, Attributes, MaterialSlots])
                table.Dispose();
        }

        private static uint SlotOf(Material material) => material.IsDisposed || material.Table == null ? uint.MaxValue : material.Slot;

        private void UpdatePrevious(int index, in Float4x4 previous)
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

        private void UpdateBounds(int index)
        {
            AABB local = BoundsOverride[index] ?? Mesh[index]?.Bounds ?? default;
            Float4x4 m = Transform[index];
            Float3 center = (local.Min + local.Max) * 0.5f;
            Float3 extents = (local.Max - local.Min) * 0.5f;
            Float3 worldCenter = new(
                m.c0.X * center.X + m.c1.X * center.Y + m.c2.X * center.Z + m.c3.X,
                m.c0.Y * center.X + m.c1.Y * center.Y + m.c2.Y * center.Z + m.c3.Y,
                m.c0.Z * center.X + m.c1.Z * center.Y + m.c2.Z * center.Z + m.c3.Z);
            Float3 worldExtents = new(
                MathF.Abs(m.c0.X) * extents.X + MathF.Abs(m.c1.X) * extents.Y + MathF.Abs(m.c2.X) * extents.Z,
                MathF.Abs(m.c0.Y) * extents.X + MathF.Abs(m.c1.Y) * extents.Y + MathF.Abs(m.c2.Y) * extents.Z,
                MathF.Abs(m.c0.Z) * extents.X + MathF.Abs(m.c1.Z) * extents.Y + MathF.Abs(m.c2.Z) * extents.Z);

            AABB world = new() { Min = worldCenter - worldExtents, Max = worldCenter + worldExtents };
            float radius = MathF.Sqrt(worldExtents.X * worldExtents.X + worldExtents.Y * worldExtents.Y + worldExtents.Z * worldExtents.Z);
            WorldBounds[index] = world;
            WorldSphere[index] = new Float4(worldCenter.X, worldCenter.Y, worldCenter.Z, radius);
            Bounds.Set((uint)index, new ObjectBounds { Sphere = WorldSphere[index], Min = world.Min, Max = world.Max });
        }

        private void WriteSlice(int index, uint sliceBase, ReadOnlySpan<Material?> materials)
        {
            int end = (int)sliceBase + materials.Length;
            if (end > SliceMaterials.Length)
                Array.Resize(ref SliceMaterials, Math.Max(end, SliceMaterials.Length * 2));
            materials.CopyTo(SliceMaterials.AsSpan((int)sliceBase));
            SliceBase[index] = sliceBase;
            SliceCount[index] = materials.Length;

            for (int i = 0; i < materials.Length; i++)
            {
                uint entry = sliceBase + (uint)i;
                Material material = materials[i]!;
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
            SliceCount[index] = 0;
        }

        private void Retire(int index)
        {
            RemoveSliceEntries(index);
            RemoveMeshUser(index);
            Mesh[index] = null;
            if (Previous[index] != ObjectAttributes.NoPrevious)
                _freePrevious.Push(Previous[index]);
            Previous[index] = ObjectAttributes.NoPrevious;
            Mirrored[index] = false;
            Alive[index] = false;
            _attributes[index] = new ObjectAttributes { Flags = (uint)RenderableFlags.Hidden, PreviousTransform = ObjectAttributes.NoPrevious, LightmapPage = -1 };
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

        private static float Determinant3(in Float4x4 m)
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
            Array.Resize(ref WorldBounds, capacity);
            Array.Resize(ref WorldSphere, capacity);
            Array.Resize(ref SliceBase, capacity);
            Array.Resize(ref SliceCount, capacity);
            Array.Resize(ref _moved, capacity);
            Array.Resize(ref _attributes, capacity);
            int old = Previous.Length;
            Array.Resize(ref Previous, capacity);
            Array.Fill(Previous, ObjectAttributes.NoPrevious, old, capacity - old);
        }
    }
}
