using System;
using System.Collections.Generic;
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

/// <summary>Gives every distinct draw program a dense id for bucket keys.</summary>
internal sealed class DrawPrograms
{
    private readonly Dictionary<DrawProgram, ushort> _ids = new();
    private readonly List<DrawProgram> _programs = new();

    public DrawProgram this[ushort id] => _programs[id];

    public int Count => _programs.Count;

    /// <summary>The program a material draws a pass type with, or false when its shader has no such pass.</summary>
    public bool TryGet(MaterialDraw draw, PassType pass, out ushort id)
    {
        id = 0;
        int passIndex = draw.Shader.PassIndex(pass);
        if (passIndex < 0)
            return false;

        DrawProgram program = new(draw.Shader, passIndex, draw.Variant, draw.StateOverride, draw.Keywords);
        if (!_ids.TryGetValue(program, out id))
        {
            if (_programs.Count > ushort.MaxValue)
                throw new InvalidOperationException("More than 65536 distinct draw programs.");
            id = (ushort)_programs.Count;
            _ids[program] = id;
            _programs.Add(program);
        }
        return true;
    }
}

/// <summary>
/// The planes of a view's frustum, built from its camera relative view projection: the view without the camera's
/// translation, so objects are tested at their position minus the camera's in double precision. Planes a reversed
/// infinite projection makes degenerate are dropped.
/// </summary>
internal readonly struct CullFrustum
{
    private readonly Float4[] _planes;

    public CullFrustum(in Float4x4 viewProjection, Double3 camera)
    {
        Camera = camera;
        Float4 row0 = new(viewProjection.c0.X, viewProjection.c1.X, viewProjection.c2.X, viewProjection.c3.X);
        Float4 row1 = new(viewProjection.c0.Y, viewProjection.c1.Y, viewProjection.c2.Y, viewProjection.c3.Y);
        Float4 row2 = new(viewProjection.c0.Z, viewProjection.c1.Z, viewProjection.c2.Z, viewProjection.c3.Z);
        Float4 row3 = new(viewProjection.c0.W, viewProjection.c1.W, viewProjection.c2.W, viewProjection.c3.W);

        List<Float4> planes = new(6);
        foreach (Float4 plane in (Float4[])[row3 + row0, row3 - row0, row3 + row1, row3 - row1, row2, row3 - row2])
        {
            float length = MathF.Sqrt(plane.X * plane.X + plane.Y * plane.Y + plane.Z * plane.Z);
            if (length > 1e-6f)
                planes.Add(plane / length);
        }
        _planes = [.. planes];
    }

    /// <summary>The camera position the view projection is relative to.</summary>
    public Double3 Camera { get; }

    public Float3 Relative(Double3 world) => new((float)(world.X - Camera.X), (float)(world.Y - Camera.Y), (float)(world.Z - Camera.Z));

    public bool Sphere(Double3 center, float radius)
    {
        Float3 c = Relative(center);
        foreach (Float4 p in _planes)
        {
            if (p.X * c.X + p.Y * c.Y + p.Z * c.Z + p.W < -radius)
                return false;
        }
        return true;
    }

    public bool Box(Double3 worldCenter, Float3 extents)
    {
        Float3 center = Relative(worldCenter);
        foreach (Float4 p in _planes)
        {
            float distance = p.X * center.X + p.Y * center.Y + p.Z * center.Z + p.W;
            float radius = MathF.Abs(p.X) * extents.X + MathF.Abs(p.Y) * extents.Y + MathF.Abs(p.Z) * extents.Z;
            if (distance < -radius)
                return false;
        }
        return true;
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
/// objects move. Per view, culling and a compaction turn them into a <see cref="DrawList"/> per pass.
/// </summary>
internal sealed class BucketSet
{
    /// <summary>Pass types drawn from buckets, opaque only. Transparent items are sorted per object.</summary>
    public static readonly PassType[] Passes = [PassType.Prepass, PassType.Forward, PassType.ShadowCaster];

    internal readonly record struct BucketId(int Pass, ushort Program, int Page, PrimitiveTopology Topology, bool Mirrored, uint Chunk);

    private readonly record struct Membership(Bucket Bucket, int Position);

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
        public required AABB ChunkBounds;
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
    private readonly List<Bucket>[] _ordered = [new(), new(), new()];
    private readonly bool[] _orderDirty = [true, true, true];
    private List<Membership>[] _members = [];
    private readonly Dictionary<Material, HashSet<int>> _materialUsers = new();
    private List<Material>?[] _objectMaterials = [];
    private readonly Dictionary<int, int> _queueRanks = new();
    private readonly Dictionary<int, int> _queueUse = new();
    private bool _ranksDirty;
    private readonly HashSet<int> _rebuild = new();
    private readonly List<(ulong Key, int Bucket, int FirstInstance, int Count)> _prepass = new();
    private bool[] _visible = [];

    public BucketSet(RenderWorld.RenderSide world, DrawPrograms programs)
    {
        _world = world;
        _programs = programs;
    }

    public int BucketCount => _buckets.Count;

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
            Array.Fill(_orderDirty, true);
            _ranksDirty = false;
        }
    }

    /// <summary>
    /// Culls the world for one view and fills the draw list of a pass: objects whose layer the culling mask has, not hidden,
    /// with their sphere in the frustum, then their chunk box when the mesh has several chunks. Empty commands are left
    /// out. Prepass commands are sorted near to far by their nearest visible instance.
    /// </summary>
    public void Build(int pass, in CullFrustum frustum, uint cullingMask, DrawList list)
    {
        list.Clear();
        Cull(frustum, cullingMask, pass);
        Order(pass);
        bool prepass = Passes[pass] == PassType.Prepass;
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
                if (!_visible[index] || (bucket.MultiChunk && !ChunkVisible(frustum, index, bucket.ChunkBounds)))
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

    private static void Emit(DrawList list, Bucket bucket, int first, int count)
    {
        list.AddCommand(
            new IndirectDrawIndexedArguments { IndexCount = bucket.IndexCount, InstanceCount = (uint)count, FirstIndex = bucket.FirstIndex, FirstInstance = (uint)first },
            new DrawRecord { Chunk = bucket.Chunk, SubMesh = bucket.SubMesh });
        list.Merge(bucket.Program, bucket.Page, bucket.Topology, bucket.Mirrored);
    }

    private void Cull(in CullFrustum frustum, uint cullingMask, int pass)
    {
        if (_visible.Length < _world.Capacity)
            _visible = new bool[_world.Capacity];

        bool shadows = Passes[pass] == PassType.ShadowCaster;
        for (int index = 0; index < _world.Capacity; index++)
        {
            RenderableFlags flags = _world.Flags[index];
            _visible[index] = _world.Alive[index]
                && (flags & RenderableFlags.Hidden) == 0
                && (_world.LayerMask[index] & cullingMask) != 0
                && (shadows ? (flags & RenderableFlags.CastShadows) != 0 : (flags & RenderableFlags.ShadowsOnly) == 0)
                && frustum.Sphere(_world.WorldCenter[index], _world.WorldRadius[index]);
        }
    }

    private bool ChunkVisible(in CullFrustum frustum, int index, in AABB local)
    {
        (Double3 center, Float3 extents) = RenderWorld.RenderSide.TransformBox(_world.Transform[index], local);
        return frustum.Box(center, extents);
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

        RenderableFlags flags = _world.Flags[index];
        bool mirrored = _world.Mirrored[index];
        for (int pass = 0; pass < Passes.Length; pass++)
        {
            bool shadows = Passes[pass] == PassType.ShadowCaster;
            if (shadows ? (flags & RenderableFlags.CastShadows) == 0 : (flags & RenderableFlags.ShadowsOnly) != 0)
                continue;

            for (int c = 0; c < placement.Chunks.Length; c++)
            {
                MeshChunk chunk = placement.Chunks[c];
                Material? material = _world.SliceMaterials[_world.SliceBase[index] + (uint)chunk.SubMesh];
                if (material == null || material.IsDisposed || material.Draw is not { } draw)
                    continue;
                if (draw.Transparent || (shadows && !draw.CastShadows))
                    continue;
                if (!_programs.TryGet(draw, Passes[pass], out ushort program))
                    continue;

                Track(material, index);
                BucketId id = new(pass, program, placement.Page, placement.SubMeshes[chunk.SubMesh].Topology, mirrored, placement.FirstChunk + (uint)c);
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
                        ChunkBounds = chunk.Bounds,
                    };
                    _buckets[id] = bucket;
                    UseQueue(bucket.Queue, 1);
                    bucket.Key = Key(bucket);
                    _orderDirty[pass] = true;
                }
                Join(bucket, index);
            }
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
        if (index >= _members.Length || _members[index] is not { } members)
            return;

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
        List<int> queues = [.. _queueUse.Keys];
        queues.Sort();
        if (queues.Count > 64)
            throw new InvalidOperationException("More than 64 distinct render queues are in use.");
        _queueRanks.Clear();
        for (int i = 0; i < queues.Count; i++)
            _queueRanks[queues[i]] = i;
    }

    // [queueRank 6][program 16][topology 2, winding 1, page 5][mesh 24][spare 10]
    private ulong Key(Bucket bucket)
    {
        ulong rank = (ulong)_queueRanks.GetValueOrDefault(bucket.Queue);
        return (rank << 58) | ((ulong)bucket.Program << 42) | (((ulong)bucket.Topology & 3) << 40) | ((bucket.Mirrored ? 1ul : 0) << 39)
            | (((ulong)bucket.Page & 31) << 34) | (((ulong)bucket.MeshId & 0xFFFFFF) << 10) | (bucket.ChunkInMesh & 1023);
    }

    // [queueRank 6][program 16][page 8][depth 12][mesh 22]
    private ulong PrepassKey(Bucket bucket, float distance)
    {
        ulong rank = (ulong)_queueRanks.GetValueOrDefault(bucket.Queue);
        ulong depth = (ulong)Math.Min(4095, (int)(MathF.Log2(1 + distance) * 256));
        return (rank << 58) | ((ulong)bucket.Program << 42) | (((ulong)bucket.Page & 255) << 34) | (depth << 22) | ((ulong)bucket.MeshId & 0x3FFFFF);
    }
}
