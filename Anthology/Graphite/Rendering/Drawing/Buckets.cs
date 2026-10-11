using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>What a draw record tells the vertex shader: the chunk, the view layer block, flags and the submesh for the material lookup.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DrawRecord
{
    public uint Chunk;
    public uint View;
    public uint Flags;
    public uint SubMesh;
}

/// <summary>One multi draw indirect call: consecutive commands sharing program, page, topology and winding.</summary>
internal readonly record struct DrawCall(ushort Program, int Page, PrimitiveTopology Topology, bool Mirrored, int FirstCommand, int CommandCount);

/// <summary>
/// A material pass resolved up to its global keywords: shader pass, material variant and state override. Buckets and
/// merges compare these ids. The final program picks the global keywords per view.
/// </summary>
internal readonly record struct DrawProgram(Shader Shader, int PassIndex, ulong Variant, PassState? StateOverride, Keyword[] Keywords)
{
    public bool Equals(DrawProgram other) => Shader == other.Shader && PassIndex == other.PassIndex && Variant == other.Variant && Equals(StateOverride, other.StateOverride);

    public override int GetHashCode() => HashCode.Combine(Shader, PassIndex, Variant, StateOverride);
}

/// <summary>
/// Gives every distinct draw program a dense id for bucket keys. Buckets and transparent items hold references, and an
/// id nothing references any more is freed for reuse, its compiled programs dropped through <see cref="Freed"/>.
/// </summary>
internal sealed class DrawPrograms
{
    private readonly Dictionary<DrawProgram, ushort> _ids = new();
    private readonly List<DrawProgram> _programs = new();
    private readonly List<int> _references = new();
    private readonly Stack<ushort> _free = new();

    public DrawProgram this[ushort id] => _programs[id];

    /// <summary>Ids in use.</summary>
    public int Count => _ids.Count;

    /// <summary>Raised when an id is freed, before it can be handed out again.</summary>
    public event Action<ushort>? Freed;

    /// <summary>Called once when every id is in use and a new program cannot get one.</summary>
    public Action<RendererError>? Reporter { get; set; }

    public void Acquire(ushort id) => _references[id]++;

    public void Release(ushort id)
    {
        if (--_references[id] > 0)
            return;

        _ids.Remove(_programs[id]);
        _programs[id] = default;
        Freed?.Invoke(id);
        _free.Push(id);
    }

    /// <summary>The program a material draws a pass type with, or false when its shader has no such pass.</summary>
    public bool TryGet(MaterialDraw draw, PassType pass, out ushort id)
    {
        id = 0;
        int passIndex = draw.Shader.PassIndex(pass);
        if (passIndex < 0)
            return false;

        DrawProgram program = new(draw.Shader, passIndex, draw.Variant, draw.StateOverride, draw.Keywords);
        if (_ids.TryGetValue(program, out id))
            return true;

        if (_free.Count > 0)
        {
            id = _free.Pop();
            _programs[id] = program;
        }
        else if (_programs.Count <= ushort.MaxValue)
        {
            id = (ushort)_programs.Count;
            _programs.Add(program);
            _references.Add(0);
        }
        else
        {
            Reporter?.Invoke(new RendererError("programs.full", "Every one of the 65536 draw program ids is in use, so new programs draw nothing."));
            Reporter = null;
            return false;
        }
        _ids[program] = id;
        return true;
    }
}

/// <summary>
/// The planes of a view's frustums, one per layer, built from each layer's camera relative view projection: the view
/// without the camera's translation, so objects are tested at their position minus the first layer's camera in double
/// precision. Something is visible when any layer sees it. Planes a reversed infinite projection makes degenerate are dropped.
/// </summary>
internal readonly struct CullFrustum
{
    private const int MaxLayers = 6;

    [InlineArray(MaxLayers * 6)]
    private struct Planes
    {
        private Float4 _first;
    }

    [InlineArray(MaxLayers)]
    private struct Counts
    {
        private int _first;
    }

    private readonly Planes _planes;
    private readonly Counts _counts;
    private readonly int _layers;

    public CullFrustum(in Float4x4 viewProjection, Double3 camera)
    {
        Camera = camera;
        _layers = 1;
        _counts[0] = AddLayer(ref _planes, 0, viewProjection, Float3.Zero);
    }

    public CullFrustum(in ViewLayers layers, int count)
    {
        if (count < 1 || count > MaxLayers)
            throw new ArgumentOutOfRangeException(nameof(count));
        Camera = layers[0].Position;
        _layers = count;
        for (int l = 0; l < count; l++)
        {
            ViewLayer layer = layers[l];
            _counts[l] = AddLayer(ref _planes, l * 6, layer.Projection * layer.View, Relative(layer.Position));
        }
    }

    // A layer's planes moved from its own camera to the first layer's, offset being its camera from the first
    private static int AddLayer(ref Planes planes, int first, in Float4x4 viewProjection, Float3 offset)
    {
        Float4 row0 = new(viewProjection.c0.X, viewProjection.c1.X, viewProjection.c2.X, viewProjection.c3.X);
        Float4 row1 = new(viewProjection.c0.Y, viewProjection.c1.Y, viewProjection.c2.Y, viewProjection.c3.Y);
        Float4 row2 = new(viewProjection.c0.Z, viewProjection.c1.Z, viewProjection.c2.Z, viewProjection.c3.Z);
        Float4 row3 = new(viewProjection.c0.W, viewProjection.c1.W, viewProjection.c2.W, viewProjection.c3.W);

        int count = 0;
        Span<Float4> candidates = [row3 + row0, row3 - row0, row3 + row1, row3 - row1, row2, row3 - row2];
        foreach (Float4 plane in candidates)
        {
            float length = MathF.Sqrt(plane.X * plane.X + plane.Y * plane.Y + plane.Z * plane.Z);
            if (length <= 1e-6f)
                continue;
            Float4 p = plane / length;
            p.W -= p.X * offset.X + p.Y * offset.Y + p.Z * offset.Z;
            planes[first + count++] = p;
        }
        return count;
    }

    /// <summary>The first layer's camera position, which positions are tested relative to.</summary>
    public Double3 Camera { get; }

    public Float3 Relative(Double3 world) => new((float)(world.X - Camera.X), (float)(world.Y - Camera.Y), (float)(world.Z - Camera.Z));

    public bool Sphere(Double3 center, float radius)
    {
        Float3 c = Relative(center);
        for (int l = 0; l < _layers; l++)
        {
            bool inside = true;
            for (int i = 0; i < _counts[l] && inside; i++)
            {
                Float4 p = _planes[l * 6 + i];
                inside = p.X * c.X + p.Y * c.Y + p.Z * c.Z + p.W >= -radius;
            }
            if (inside)
                return true;
        }
        return false;
    }

    public bool Box(Double3 worldCenter, Float3 extents)
    {
        Float3 center = Relative(worldCenter);
        for (int l = 0; l < _layers; l++)
        {
            bool inside = true;
            for (int i = 0; i < _counts[l] && inside; i++)
            {
                Float4 p = _planes[l * 6 + i];
                float distance = p.X * center.X + p.Y * center.Y + p.Z * center.Z + p.W;
                float radius = MathF.Abs(p.X) * extents.X + MathF.Abs(p.Y) * extents.Y + MathF.Abs(p.Z) * extents.Z;
                inside = distance >= -radius;
            }
            if (inside)
                return true;
        }
        return false;
    }
}

/// <summary>What one pass of one view draws this frame: the instance list, the indirect commands, their draw records and the calls over them.</summary>
internal sealed class DrawList
{
    public uint[] Instances = new uint[256];
    public int InstanceCount;
    public IndirectDrawIndexedArguments[] Commands = new IndirectDrawIndexedArguments[64];
    public DrawRecord[] Records = new DrawRecord[64];
    public int CommandCount;
    public readonly List<DrawCall> Calls = new();

    public void Clear()
    {
        InstanceCount = 0;
        CommandCount = 0;
        Calls.Clear();
    }

    public void AddInstance(uint objectIndex)
    {
        if (InstanceCount == Instances.Length)
            Array.Resize(ref Instances, Instances.Length * 2);
        Instances[InstanceCount++] = objectIndex;
    }

    public void AddCommand(in IndirectDrawIndexedArguments command, in DrawRecord record)
    {
        if (CommandCount == Commands.Length)
        {
            Array.Resize(ref Commands, Commands.Length * 2);
            Array.Resize(ref Records, Records.Length * 2);
        }
        Commands[CommandCount] = command;
        Records[CommandCount] = record;
        CommandCount++;
    }

    /// <summary>Draws one more instance with the last command, whose instances end where the list ends.</summary>
    public void ExtendLast() => Commands[CommandCount - 1].InstanceCount++;

    /// <summary>Adds a command to the last call when program, page, topology and winding all match, else starts a call.</summary>
    public void Merge(ushort program, int page, PrimitiveTopology topology, bool mirrored)
    {
        int command = CommandCount - 1;
        if (Calls.Count > 0)
        {
            DrawCall last = Calls[^1];
            if (last.Program == program && last.Page == page && last.Topology == topology && last.Mirrored == mirrored && last.FirstCommand + last.CommandCount == command)
            {
                Calls[^1] = last with { CommandCount = last.CommandCount + 1 };
                return;
            }
        }
        Calls.Add(new DrawCall(program, page, topology, mirrored, command, 1));
    }
}

/// <summary>
/// The persistent draw buckets of one world. A bucket is one indirect command: a mesh chunk drawn by one program, with
/// the objects that use it. Buckets change only when renderables, their meshes, materials or flags change, never when
/// objects move. Per view, culling and a compaction turn them into a <see cref="DrawList"/> per pass. Transparent
/// submeshes are items instead, sorted per view back to front.
/// </summary>
internal sealed class BucketSet
{
    /// <summary>
    /// Pass types drawn from buckets: the built in three, opaque only, then any a feature asked for, drawn for every
    /// material with that pass. Transparent forward items are sorted per object.
    /// </summary>
    private readonly List<PassType> _passes = [PassType.Prepass, PassType.Forward, PassType.ShadowCaster];
    private const int BuiltInPasses = 3;

    internal readonly record struct BucketId(int Pass, ushort Program, int Queue, int Page, PrimitiveTopology Topology, bool Mirrored, uint Chunk);

    private readonly record struct Membership(Bucket Bucket, int Position);

    private readonly record struct ChunkRef(uint Chunk, int ChunkInMesh, uint FirstIndex, uint IndexCount);

    /// <summary>One transparent submesh of one object, with the chunks it draws.</summary>
    private sealed class TransparentItem
    {
        public required int Object;
        public required ushort Program;
        public required int Page;
        public required PrimitiveTopology Topology;
        public required bool Mirrored;
        public required int Queue;
        public required uint SubMesh;
        public required ChunkRef[] Chunks;
        public int Position;
    }

    internal sealed class Bucket
    {
        public required BucketId Id;
        public required int Queue;
        public required int MeshId;
        public required uint ChunkInMesh;
        public required uint FirstIndex;
        public required uint IndexCount;
        public required uint SubMesh;
        public required bool MultiChunk;
        public ulong Key;
        public int[] Objects = new int[4];
        public int Count;
        public int Pass => Id.Pass;
        public ushort Program => Id.Program;
        public int Page => Id.Page;
        public PrimitiveTopology Topology => Id.Topology;
        public bool Mirrored => Id.Mirrored;
        public uint Chunk => Id.Chunk;
    }

    private readonly RenderWorld.RenderSide _world;
    private readonly DrawPrograms _programs;
    private readonly Dictionary<BucketId, Bucket> _buckets = new();
    private readonly List<List<Bucket>> _ordered = [new(), new(), new()];
    private readonly List<bool> _orderDirty = [true, true, true];
    private List<Membership>[] _members = [];
    private readonly Dictionary<Material, HashSet<int>> _materialUsers = new();
    private List<Material>?[] _objectMaterials = [];
    private readonly Dictionary<int, int> _queueRanks = new();
    private readonly Dictionary<int, int> _queueUse = new();
    private bool _ranksDirty;
    private readonly HashSet<int> _rebuild = new();
    private readonly List<(ulong Key, int Bucket, int FirstInstance, int Count)> _prepass = new();
    private readonly List<TransparentItem> _transparent = new();
    private List<TransparentItem>?[] _transparentOf = [];
    private readonly List<(ulong Key, TransparentItem Item)> _sorted = new();
    private bool[] _visible = [];
    private int[] _chunkStamp = [];
    private int[] _chunkStart = [];
    private bool[] _chunkVisible = new bool[256];
    private int _chunkCount;
    private int _cullStamp;

    public BucketSet(RenderWorld.RenderSide world, DrawPrograms programs)
    {
        _world = world;
        _programs = programs;
    }

    public int BucketCount => _buckets.Count;

    public int TransparentCount => _transparent.Count;

    /// <summary>The index of a pass type's buckets, bucketing every renderable for it the first time it is asked for. Render thread.</summary>
    public int Pass(PassType type)
    {
        int pass = _passes.IndexOf(type);
        if (pass >= 0)
            return pass;

        pass = _passes.Count;
        _passes.Add(type);
        _ordered.Add(new List<Bucket>());
        _orderDirty.Add(true);
        for (int index = 0; index < _world.Capacity; index++)
        {
            if (_world.Alive[index] && _world.Mesh[index]?.Placement is { } placement)
                AddToPass(index, pass, placement);
        }
        return pass;
    }

    public IReadOnlyList<Bucket> Ordered(int pass)
    {
        Order(pass);
        return _ordered[pass];
    }

    /// <summary>Rebuilds the buckets of renderables whose draws changed, and of every renderable using a material whose draws changed. Render thread.</summary>
    public void Update(IReadOnlyList<int> drawChanged, IReadOnlyList<Material> changedMaterials)
    {
        _rebuild.Clear();
        foreach (int index in drawChanged)
            _rebuild.Add(index);
        foreach (Material material in changedMaterials)
        {
            if (_materialUsers.TryGetValue(material, out HashSet<int>? users))
                _rebuild.UnionWith(users);
        }

        foreach (int index in _rebuild)
        {
            RemoveObject(index);
            if (index < _world.Capacity && _world.Alive[index])
                AddObject(index);
        }

        if (_ranksDirty)
        {
            RankQueues();
            foreach (Bucket bucket in _buckets.Values)
                bucket.Key = Key(bucket);
            for (int i = 0; i < _orderDirty.Count; i++)
                _orderDirty[i] = true;
            _ranksDirty = false;
        }
    }

    /// <summary>
    /// Fills the draw list of a pass from the last <see cref="Cull"/>: visible objects, then their chunk box when the mesh
    /// has several chunks. Empty commands are left out. Prepass commands are sorted near to far by their nearest visible
    /// instance.
    /// </summary>
    public void Build(int pass, in CullFrustum frustum, DrawList list)
    {
        list.Clear();
        Order(pass);
        bool prepass = pass == 0;
        _prepass.Clear();

        List<Bucket> ordered = _ordered[pass];
        for (int b = 0; b < ordered.Count; b++)
        {
            Bucket bucket = ordered[b];
            int first = list.InstanceCount;
            float nearest = float.MaxValue;
            for (int i = 0; i < bucket.Count; i++)
            {
                int index = bucket.Objects[i];
                if (!_visible[index] || (bucket.MultiChunk && !ChunkVisible(frustum, index, (int)bucket.ChunkInMesh)))
                    continue;
                list.AddInstance((uint)index);
                if (prepass)
                    nearest = MathF.Min(nearest, Distance(frustum, index));
            }

            int count = list.InstanceCount - first;
            if (count == 0)
                continue;

            if (prepass)
            {
                _prepass.Add((PrepassKey(bucket, nearest), b, first, count));
                continue;
            }
            Emit(list, bucket, first, count);
        }

        if (!prepass)
            return;

        _prepass.Sort((a, c) => a.Key.CompareTo(c.Key));
        foreach ((_, int b, int first, int count) in _prepass)
            Emit(list, ordered[b], first, count);
    }

    /// <summary>
    /// Fills the transparent draw list from the last <see cref="Cull"/>: every visible transparent item, sorted by queue,
    /// sort bias, then back to front. Adjacent items of the same program, page, topology, winding and chunk share a command.
    /// </summary>
    public void BuildTransparent(in CullFrustum frustum, DrawList list)
    {
        list.Clear();
        _sorted.Clear();
        foreach (TransparentItem item in _transparent)
        {
            if (_visible[item.Object])
                _sorted.Add((TransparentKey(item, frustum), item));
        }
        _sorted.Sort((a, b) => a.Key.CompareTo(b.Key));

        DrawCommandRef previous = default;
        foreach ((_, TransparentItem item) in _sorted)
        {
            bool multiChunk = item.Chunks.Length > 1;
            foreach (ChunkRef chunk in item.Chunks)
            {
                if (multiChunk && !ChunkVisible(frustum, item.Object, chunk.ChunkInMesh))
                    continue;

                DrawCommandRef current = new(item.Program, item.Page, item.Topology, item.Mirrored, chunk.Chunk, list.CommandCount);
                list.AddInstance((uint)item.Object);
                if (list.CommandCount > 0 && previous == current with { Command = previous.Command } && previous.Command == list.CommandCount - 1)
                {
                    list.ExtendLast();
                    continue;
                }

                list.AddCommand(
                    new IndirectDrawIndexedArguments { IndexCount = chunk.IndexCount, InstanceCount = 1, FirstIndex = chunk.FirstIndex, FirstInstance = (uint)(list.InstanceCount - 1) },
                    new DrawRecord { Chunk = chunk.Chunk, SubMesh = item.SubMesh });
                list.Merge(item.Program, item.Page, item.Topology, item.Mirrored);
                previous = current;
            }
        }
    }

    private readonly record struct DrawCommandRef(ushort Program, int Page, PrimitiveTopology Topology, bool Mirrored, uint Chunk, int Command);

    // [queueRank 6][sortBias 8][depth 32 back to front][program 12][spare 6]
    private ulong TransparentKey(TransparentItem item, in CullFrustum frustum)
    {
        ulong rank = (ulong)_queueRanks.GetValueOrDefault(item.Queue);
        ulong bias = (ulong)(_world.SortBias[item.Object] + 128);
        ulong depth = ~BitConverter.SingleToUInt32Bits(Distance(frustum, item.Object)) & 0xFFFFFFFFu;
        return (rank << 58) | (bias << 50) | (depth << 18) | (((ulong)item.Program & 0xFFF) << 6);
    }

    private static void Emit(DrawList list, Bucket bucket, int first, int count)
    {
        list.AddCommand(
            new IndirectDrawIndexedArguments { IndexCount = bucket.IndexCount, InstanceCount = (uint)count, FirstIndex = bucket.FirstIndex, FirstInstance = (uint)first },
            new DrawRecord { Chunk = bucket.Chunk, SubMesh = bucket.SubMesh });
        list.Merge(bucket.Program, bucket.Page, bucket.Topology, bucket.Mirrored);
    }

    /// <summary>
    /// Culls the world for one view, once for all of its passes: objects whose layer the culling mask has, not hidden,
    /// with their sphere in the frustum. Which passes an object draws in is decided by its buckets.
    /// </summary>
    public void Cull(in CullFrustum frustum, uint cullingMask)
    {
        if (_visible.Length < _world.Capacity)
        {
            _visible = new bool[_world.Capacity];
            Array.Resize(ref _chunkStamp, _world.Capacity);
            Array.Resize(ref _chunkStart, _world.Capacity);
        }
        _cullStamp++;
        _chunkCount = 0;

        for (int index = 0; index < _world.Capacity; index++)
        {
            _visible[index] = _world.Alive[index]
                && (_world.Flags[index] & RenderableFlags.Hidden) == 0
                && (_world.LayerMask[index] & cullingMask) != 0
                && frustum.Sphere(_world.WorldCenter[index], _world.WorldRadius[index]);
        }
    }

    // Every chunk of an object is tested once per view, the first time any pass asks, and the answers are shared.
    private bool ChunkVisible(in CullFrustum frustum, int index, int chunkInMesh)
    {
        if (_chunkStamp[index] != _cullStamp)
        {
            MeshChunk[] chunks = _world.Mesh[index]!.Placement!.Chunks;
            if (_chunkCount + chunks.Length > _chunkVisible.Length)
                Array.Resize(ref _chunkVisible, Math.Max(_chunkCount + chunks.Length, _chunkVisible.Length * 2));
            Double4x4 transform = _world.Transform[index];
            for (int c = 0; c < chunks.Length; c++)
            {
                (Double3 center, Float3 extents) = RenderWorld.RenderSide.TransformBox(transform, chunks[c].Bounds);
                _chunkVisible[_chunkCount + c] = frustum.Box(center, extents);
            }
            _chunkStamp[index] = _cullStamp;
            _chunkStart[index] = _chunkCount;
            _chunkCount += chunks.Length;
        }
        return _chunkVisible[_chunkStart[index] + chunkInMesh];
    }

    private float Distance(in CullFrustum frustum, int index)
    {
        Float3 c = frustum.Relative(_world.WorldCenter[index]);
        return MathF.Max(0, MathF.Sqrt(c.X * c.X + c.Y * c.Y + c.Z * c.Z) - _world.WorldRadius[index]);
    }

    private void AddObject(int index)
    {
        GpuMesh? mesh = _world.Mesh[index];
        if (mesh?.Placement is not { } placement)
            return;

        for (int pass = 0; pass < _passes.Count; pass++)
            AddToPass(index, pass, placement);
        if ((_world.Flags[index] & RenderableFlags.ShadowsOnly) == 0)
            AddTransparent(index, placement, _world.Mirrored[index]);
    }

    // Built in passes draw opaque materials, the shadow pass only casters; a feature's pass draws every material having it
    private void AddToPass(int index, int pass, MeshPlacement placement)
    {
        RenderableFlags flags = _world.Flags[index];
        bool shadows = _passes[pass] == PassType.ShadowCaster;
        bool custom = pass >= BuiltInPasses;
        if (shadows ? (flags & RenderableFlags.CastShadows) == 0 : (flags & RenderableFlags.ShadowsOnly) != 0)
            return;

        GpuMesh mesh = _world.Mesh[index]!;
        bool mirrored = _world.Mirrored[index];
        for (int c = 0; c < placement.Chunks.Length; c++)
        {
            MeshChunk chunk = placement.Chunks[c];
            Material? material = _world.SliceMaterials[_world.SliceBase[index] + (uint)chunk.SubMesh];
            if (material == null || material.IsDisposed || material.Draw is not { } draw)
                continue;
            if ((draw.Transparent && !custom) || (shadows && !draw.CastShadows))
                continue;
            if (!_programs.TryGet(draw, _passes[pass], out ushort program))
                continue;

            Track(material, index);
            BucketId id = new(pass, program, draw.RenderQueue, placement.Page, placement.SubMeshes[chunk.SubMesh].Topology, mirrored, placement.FirstChunk + (uint)c);
            if (!_buckets.TryGetValue(id, out Bucket? bucket))
            {
                bucket = new Bucket
                {
                    Id = id,
                    Queue = draw.RenderQueue,
                    MeshId = mesh.Id,
                    ChunkInMesh = (uint)c,
                    FirstIndex = placement.IndexBase + chunk.IndexStart,
                    IndexCount = chunk.IndexCount,
                    SubMesh = (uint)chunk.SubMesh,
                    MultiChunk = placement.Chunks.Length > 1,
                };
                _buckets[id] = bucket;
                _programs.Acquire(program);
                UseQueue(bucket.Queue, 1);
                bucket.Key = Key(bucket);
                _orderDirty[pass] = true;
            }
            Join(bucket, index);
        }
    }

    private void AddTransparent(int index, MeshPlacement placement, bool mirrored)
    {
        for (int s = 0; s < placement.SubMeshes.Length; s++)
        {
            Material? material = _world.SliceMaterials[_world.SliceBase[index] + (uint)s];
            if (material == null || material.IsDisposed || material.Draw is not { } draw || !draw.Transparent)
                continue;
            if (!_programs.TryGet(draw, PassType.Forward, out ushort program))
                continue;

            List<ChunkRef> chunks = new();
            for (int c = 0; c < placement.Chunks.Length; c++)
            {
                MeshChunk chunk = placement.Chunks[c];
                if (chunk.SubMesh == s)
                    chunks.Add(new ChunkRef(placement.FirstChunk + (uint)c, c, placement.IndexBase + chunk.IndexStart, chunk.IndexCount));
            }
            if (chunks.Count == 0)
                continue;

            Track(material, index);
            TransparentItem item = new()
            {
                Object = index,
                Program = program,
                Page = placement.Page,
                Topology = placement.SubMeshes[s].Topology,
                Mirrored = mirrored,
                Queue = draw.RenderQueue,
                SubMesh = (uint)s,
                Chunks = [.. chunks],
                Position = _transparent.Count,
            };
            _transparent.Add(item);
            _programs.Acquire(program);
            UseQueue(item.Queue, 1);
            if (index >= _transparentOf.Length)
                Array.Resize(ref _transparentOf, Math.Max(index + 1, _transparentOf.Length * 2));
            (_transparentOf[index] ??= new List<TransparentItem>()).Add(item);
        }
    }

    private void Join(Bucket bucket, int index)
    {
        if (bucket.Count == bucket.Objects.Length)
            Array.Resize(ref bucket.Objects, bucket.Objects.Length * 2);
        bucket.Objects[bucket.Count] = index;
        Members(index).Add(new Membership(bucket, bucket.Count));
        bucket.Count++;
    }

    private void RemoveObject(int index)
    {
        if (index < _transparentOf.Length && _transparentOf[index] is { } items)
        {
            foreach (TransparentItem item in items)
            {
                TransparentItem last = _transparent[^1];
                _transparent[item.Position] = last;
                last.Position = item.Position;
                _transparent.RemoveAt(_transparent.Count - 1);
                UseQueue(item.Queue, -1);
                _programs.Release(item.Program);
            }
            items.Clear();
        }

        List<Membership> members = index < _members.Length && _members[index] is { } list ? list : [];
        foreach (Membership membership in members)
        {
            Bucket bucket = membership.Bucket;
            int last = bucket.Count - 1;
            if (membership.Position != last)
            {
                int moved = bucket.Objects[last];
                bucket.Objects[membership.Position] = moved;
                List<Membership> movedMembers = _members[moved];
                for (int m = 0; m < movedMembers.Count; m++)
                {
                    if (movedMembers[m].Bucket == bucket && movedMembers[m].Position == last)
                    {
                        movedMembers[m] = movedMembers[m] with { Position = membership.Position };
                        break;
                    }
                }
            }
            bucket.Count--;

            if (bucket.Count == 0)
            {
                _buckets.Remove(bucket.Id);
                UseQueue(bucket.Queue, -1);
                _programs.Release(bucket.Program);
                _orderDirty[bucket.Pass] = true;
            }
        }
        members.Clear();

        if (index < _objectMaterials.Length && _objectMaterials[index] is { } materials)
        {
            foreach (Material material in materials)
            {
                if (_materialUsers.TryGetValue(material, out HashSet<int>? users) && users.Remove(index) && users.Count == 0)
                    _materialUsers.Remove(material);
            }
            materials.Clear();
        }
    }

    private void Track(Material material, int index)
    {
        if (!_materialUsers.TryGetValue(material, out HashSet<int>? users))
        {
            users = new HashSet<int>();
            _materialUsers[material] = users;
        }
        if (!users.Add(index))
            return;

        if (index >= _objectMaterials.Length)
            Array.Resize(ref _objectMaterials, Math.Max(index + 1, _objectMaterials.Length * 2));
        (_objectMaterials[index] ??= new List<Material>()).Add(material);
    }

    private List<Membership> Members(int index)
    {
        if (index >= _members.Length)
            Array.Resize(ref _members, Math.Max(index + 1, _members.Length * 2));
        return _members[index] ??= new List<Membership>();
    }

    private void Order(int pass)
    {
        if (!_orderDirty[pass])
            return;

        List<Bucket> ordered = _ordered[pass];
        ordered.Clear();
        foreach (Bucket bucket in _buckets.Values)
        {
            if (bucket.Pass == pass)
                ordered.Add(bucket);
        }
        ordered.Sort((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Chunk.CompareTo(b.Chunk));
        _orderDirty[pass] = false;
    }

    private void UseQueue(int queue, int delta)
    {
        int count = _queueUse.GetValueOrDefault(queue) + delta;
        if (count == 0)
        {
            _queueUse.Remove(queue);
            _ranksDirty = true;
        }
        else
        {
            if (!_queueUse.ContainsKey(queue))
                _ranksDirty = true;
            _queueUse[queue] = count;
        }
    }

    private void RankQueues()
    {
        // Keys hold 64 ranks. Queues past the 64th share the last rank, so they keep drawing after the others.
        List<int> queues = [.. _queueUse.Keys];
        queues.Sort();
        _queueRanks.Clear();
        for (int i = 0; i < queues.Count; i++)
            _queueRanks[queues[i]] = Math.Min(i, 63);
    }

    // [queueRank 6][program 16][topology 2, winding 1, page 5][mesh 24][spare 10]
    private ulong Key(Bucket bucket)
    {
        ulong rank = (ulong)_queueRanks.GetValueOrDefault(bucket.Queue);
        return (rank << 58) | ((ulong)bucket.Program << 42) | (((ulong)bucket.Topology & 3) << 40) | ((bucket.Mirrored ? 1ul : 0) << 39)
            | (((ulong)bucket.Page & 31) << 34) | (((ulong)bucket.MeshId & 0xFFFFFF) << 10) | (bucket.ChunkInMesh & 1023);
    }

    // [queueRank 6][program 16][topology 2, winding 1, page 5][depth 12][mesh 22]
    private ulong PrepassKey(Bucket bucket, float distance)
    {
        ulong rank = (ulong)_queueRanks.GetValueOrDefault(bucket.Queue);
        ulong depth = (ulong)Math.Min(4095, (int)(MathF.Log2(1 + distance) * 256));
        return (rank << 58) | ((ulong)bucket.Program << 42) | (((ulong)bucket.Topology & 3) << 40) | ((bucket.Mirrored ? 1ul : 0) << 39)
            | (((ulong)bucket.Page & 31) << 34) | (depth << 22) | ((ulong)bucket.MeshId & 0x3FFFFF);
    }
}
