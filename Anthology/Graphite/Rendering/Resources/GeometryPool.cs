using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// A mesh uploaded to a geometry page. Its id is dense and stable for its life, and it draws nothing until its data is
/// resident on the GPU.
/// </summary>
public sealed class GpuMesh : IDisposable
{
    private readonly GeometryPool _pool;
    private uint _version;

    internal GpuMesh(GeometryPool pool, int id)
    {
        _pool = pool;
        Id = id;
    }

    /// <summary>Dense id, unique among live meshes of the renderer.</summary>
    public int Id { get; }

    private sealed record Info(AABB Bounds, int SubMeshCount, int ChunkCount);

    private Info _info = new(default, 0, 0);

    /// <summary>Bounds of the data last given, which may not be resident yet.</summary>
    public AABB Bounds => Volatile.Read(ref _info).Bounds;

    public int SubMeshCount => Volatile.Read(ref _info).SubMeshCount;

    public int ChunkCount => Volatile.Read(ref _info).ChunkCount;

    internal void Describe(MeshLayout layout) => Volatile.Write(ref _info, new Info(layout.Bounds, layout.SubMeshes.Length, layout.Chunks.Length));

    /// <summary>Bumps on every <see cref="SetData"/>.</summary>
    public uint Version => Volatile.Read(ref _version);

    public bool IsDisposed { get; private set; }

    /// <summary>Where the data lives, null until it is resident. Render thread only.</summary>
    internal MeshPlacement? Placement
    {
        get => _placement;
        set
        {
            _placement = value;
            if (value != null)
                _placements++;
        }
    }

    private MeshPlacement? _placement;
    private uint _placements;

    /// <summary>
    /// Where a resident mesh's vertex streams live, for a compute pass writing its <see cref="MeshData.GpuStreams"/>.
    /// False until the mesh is resident. A new <see cref="GpuMeshStorage.Placement"/> means the mesh moved and its GPU
    /// streams have to be written again. Render thread, such as in a RenderProvider's Compute.
    /// </summary>
    public bool TryGetStorage(out GpuMeshStorage storage)
    {
        if (_placement is not { } placement)
        {
            storage = default;
            return false;
        }
        storage = new GpuMeshStorage(_pool.Pages[placement.Page].Vertices, placement.StreamOffsets, placement.VertexCount, _placements);
        return true;
    }

    /// <summary>Replaces the mesh's data, keeping its id. Thread safe, the upload happens on a later frame.</summary>
    public void SetData(MeshData data)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        MeshLayout layout = new(data);
        Interlocked.Increment(ref _version);
        Describe(layout);
        _pool.Queue(this, layout);
    }

    /// <summary>Frees the mesh's geometry once the GPU no longer uses it.</summary>
    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        _pool.Queue(this, null);
    }
}

/// <summary>
/// A resident mesh's vertex streams: the page buffer holding them as 32 bit words, and the first word of each stream.
/// Each stream is tightly packed in its canonical layout, Position and Normal 3 floats, Tangent 4, UVs 2, Custom 4.
/// </summary>
public readonly struct GpuMeshStorage
{
    private readonly uint[] _offsets;

    internal GpuMeshStorage(DeviceBuffer vertices, uint[] offsets, uint vertexCount, uint placement)
    {
        Vertices = vertices;
        _offsets = offsets;
        VertexCount = vertexCount;
        Placement = placement;
    }

    /// <summary>The page's word buffer, shared with other meshes.</summary>
    public DeviceBuffer Vertices { get; }

    public uint VertexCount { get; }

    /// <summary>Counts the mesh's placements, so a host notices when it moved.</summary>
    public uint Placement { get; }

    /// <summary>The word the stream starts at in <see cref="Vertices"/>.</summary>
    public uint StreamOffset(VertexSemantic semantic) => _offsets[(int)semantic];

    /// <summary>Words one vertex of the stream takes.</summary>
    public static uint WordsPerVertex(VertexSemantic semantic) => MeshLayout.WordsPerVertex(semantic);
}

/// <summary>A placed blend shape frame: its weight, delta count, and the first word of each delta array, none when absent.</summary>
internal readonly record struct BlendFrame(float Weight, uint Count, uint Vertices, uint Positions, uint Normals, uint Tangents)
{
    public const uint None = uint.MaxValue;
}

/// <summary>Where a mesh's data lives: its page, its ranges, each present stream's first word, and its chunk records.</summary>
internal sealed class MeshPlacement
{
    public const uint None = uint.MaxValue;

    /// <summary>The first word of a copy's second range of positions, which takes turns with its own, or none.</summary>
    public uint SecondPositions = None;

    /// <summary>The mesh's blend shape frames, after its streams, and each shape's first frame and count. Copies have none.</summary>
    public BlendFrame[] BlendFrames = [];
    public (int First, int Count)[] BlendShapes = [];

    public required int Page;
    public required RangeAllocation Vertices;
    public required RangeAllocation Indices;
    public required RangeAllocation ChunkRange;
    public required uint[] StreamOffsets;
    public required uint StreamsPresent;
    public required uint VertexCount;
    public required MeshChunk[] Chunks;
    public required SubMesh[] SubMeshes;
    public required AABB Bounds;

    /// <summary>First index of the mesh in its page's index buffer. Chunk index starts are relative to it.</summary>
    public uint IndexBase => Indices.Offset;

    /// <summary>Index of the mesh's first chunk in the chunk table.</summary>
    public uint FirstChunk => ChunkRange.Offset;
}

[InlineArray(MeshLayout.StreamCount)]
internal struct StreamOffsets
{
    private uint _first;
}

/// <summary>A chunk table entry as std430 sees it, 96 bytes. Stream offsets are absolute words in the page's vertex buffer.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ChunkRecord
{
    public StreamOffsets StreamOffsets;
    public Float3 BoundsMin;
    public uint StreamsPresent;
    public Float3 BoundsMax;
    public uint Page;
    public uint FirstIndex;
    public uint IndexCount;
    public uint Mesh;
    public uint SubMesh;
}

/// <summary>A mesh table entry as std430 sees it, 32 bytes, indexed by mesh id. A freed mesh has no chunks.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MeshRecord
{
    public Float3 BoundsMin;
    public uint FirstChunk;
    public Float3 BoundsMax;
    public uint ChunkCount;
}

/// <summary>One fixed size page of geometry: a word buffer for vertex streams and a 32 bit index buffer.</summary>
internal sealed class GeometryPage(RangeAllocator vertexWords, RangeAllocator indices, DeviceBuffer vertices, DeviceBuffer indexBuffer)
{
    public RangeAllocator VertexWords { get; } = vertexWords;
    public RangeAllocator IndexSpace { get; } = indices;
    public DeviceBuffer Vertices { get; } = vertices;
    public DeviceBuffer Indices { get; } = indexBuffer;
}

/// <summary>
/// All geometry, in fixed size pages that are never grown or copied, so draws of different meshes in one page merge into
/// one indirect call. Creation, data changes and disposal are queued from any thread and applied on the render thread,
/// and freed ranges and ids wait until the GPU retires the frame that freed them. Chunk and mesh records live in two
/// tables every pass can read.
/// </summary>
internal sealed class GeometryPool(GraphicsDevice device, uint pageVertexWords = GeometryPool.DefaultPageVertexWords, uint pageIndices = GeometryPool.DefaultPageIndices) : IDisposable
{
    /// <summary>Words of vertex data per page, 96 MB.</summary>
    public const uint DefaultPageVertexWords = 24u << 20;

    /// <summary>Indices per page, 32 MB.</summary>
    public const uint DefaultPageIndices = 8u << 20;

    private const uint StreamAlignment = 4;
    private const uint MaxChunks = 1u << 26;

    private readonly object _lock = new();
    private readonly List<(GpuMesh Mesh, MeshLayout? Layout)> _pending = new();
    private readonly Stack<int> _freeIds = new();
    private int _nextId;

    private readonly RangeAllocator _chunkSpace = new(MaxChunks);
    private readonly List<(ulong Execution, MeshPlacement Placement)> _retiringRanges = new();
    private readonly List<(ulong Execution, int Id)> _retiringIds = new();
    private readonly List<(ulong Execution, DeviceBuffer Buffer)> _retiringBuffers = new();

    public List<GeometryPage> Pages { get; } = new();

    public GpuTable Chunks { get; } = GpuTable.Of<ChunkRecord>(device, "Chunk table", 1024);

    public GpuTable Meshes { get; } = GpuTable.Of<MeshRecord>(device, "Mesh table", 256);

    /// <summary>Creates a mesh and queues its upload. Thread safe.</summary>
    public GpuMesh Create(MeshData data)
    {
        MeshLayout layout = new(data);
        lock (_lock)
        {
            GpuMesh mesh = new(this, _freeIds.Count > 0 ? _freeIds.Pop() : _nextId++);
            mesh.Describe(layout);
            _pending.Add((mesh, layout));
            return mesh;
        }
    }

    internal void Queue(GpuMesh mesh, MeshLayout? layout)
    {
        lock (_lock)
            _pending.Add((mesh, layout));
    }

    /// <summary>
    /// Places queued meshes and writes their data and records through the uploader, stopping at the byte budget once
    /// something was applied. Meshes whose placement changed are added to <paramref name="changed"/>. Freed ranges wait
    /// for <paramref name="lastDispatched"/>, the newest execution that may still read them. Returns true when the set
    /// of buffers changed, a page was added or a table moved, so graphs importing them need a rebuild.
    /// </summary>
    public bool Apply(Uploader uploader, ulong lastDispatched, long budgetBytes, List<GpuMesh> changed)
    {
        int pagesBefore = Pages.Count;
        long spent = 0;
        lock (_lock)
        {
            int applied = 0;
            for (; applied < _pending.Count; applied++)
            {
                (GpuMesh mesh, MeshLayout? layout) = _pending[applied];
                if (layout == null)
                {
                    Release(mesh, lastDispatched);
                    Meshes.Set((uint)mesh.Id, default(MeshRecord));
                    _retiringIds.Add((lastDispatched, mesh.Id));
                    changed.Add(mesh);
                    continue;
                }

                if (mesh.IsDisposed)
                    continue;

                long bytes = ((long)layout.VertexWords + layout.BlendWords + layout.Indices.Length) * 4;
                if (spent > 0 && spent + bytes > budgetBytes)
                    break;

                Release(mesh, lastDispatched);
                MeshPlacement placement = Place(layout.StreamsPresent, layout.VertexCount, (uint)layout.Indices.Length, layout.Chunks, layout.SubMeshes, layout.Bounds, layout.BlendFrames);
                placement.BlendShapes = layout.BlendShapes;
                Write(uploader, placement, layout);
                WriteChunks(mesh.Id, placement);
                WriteMesh(mesh.Id, placement);
                mesh.Placement = placement;
                changed.Add(mesh);
                spent += bytes;
            }

            _pending.RemoveRange(0, applied);
        }

        bool moved = Retire(Chunks.Flush(uploader), lastDispatched) | Retire(Meshes.Flush(uploader), lastDispatched);
        return moved || Pages.Count != pagesBefore;
    }

    /// <summary>
    /// Room for a copy of a placed mesh's streams and indices, with chunk records of its own naming the mesh, for a
    /// renderable that deforms it, and a second range of positions when it keeps last frame's pose. Its data is copied
    /// on the GPU by whoever asked. Render thread.
    /// </summary>
    public MeshPlacement PlaceCopy(GpuMesh mesh, MeshPlacement source, bool secondPositions)
    {
        MeshPlacement copy = Place(source.StreamsPresent, source.VertexCount, source.Indices.Size, source.Chunks, source.SubMeshes, source.Bounds, [], secondPositions ? source.VertexCount * 3 : 0);
        WriteChunks(mesh.Id, copy);
        return copy;
    }

    /// <summary>Frees a copy's ranges once <paramref name="lastDispatched"/> retires. Render thread.</summary>
    public void ReleaseCopy(MeshPlacement copy, ulong lastDispatched) => _retiringRanges.Add((lastDispatched, copy));

    /// <summary>Queues the chunk records of copies placed since <see cref="Apply"/>, retiring the buffer the table moved off. Render thread.</summary>
    public void FlushCopies(Uploader uploader, ulong lastDispatched) => Retire(Chunks.Flush(uploader), lastDispatched);

    /// <summary>Returns ranges, ids and buffers freed by frames the GPU has finished. Render thread.</summary>
    public void Reclaim(ulong completedExecution)
    {
        for (int i = _retiringRanges.Count - 1; i >= 0; i--)
        {
            (ulong execution, MeshPlacement placement) = _retiringRanges[i];
            if (execution > completedExecution)
                continue;

            GeometryPage page = Pages[placement.Page];
            page.VertexWords.Free(placement.Vertices);
            page.IndexSpace.Free(placement.Indices);
            _chunkSpace.Free(placement.ChunkRange);
            _retiringRanges.RemoveAt(i);
        }

        for (int i = _retiringBuffers.Count - 1; i >= 0; i--)
        {
            if (_retiringBuffers[i].Execution > completedExecution)
                continue;

            _retiringBuffers[i].Buffer.Dispose();
            _retiringBuffers.RemoveAt(i);
        }

        for (int i = _retiringIds.Count - 1; i >= 0; i--)
        {
            if (_retiringIds[i].Execution > completedExecution)
                continue;

            lock (_lock)
                _freeIds.Push(_retiringIds[i].Id);
            _retiringIds.RemoveAt(i);
        }
    }

    public void Dispose()
    {
        foreach (GeometryPage page in Pages)
        {
            page.Vertices.Dispose();
            page.Indices.Dispose();
        }
        Pages.Clear();

        foreach ((_, DeviceBuffer buffer) in _retiringBuffers)
            buffer.Dispose();
        _retiringBuffers.Clear();
        Chunks.Dispose();
        Meshes.Dispose();
    }

    private void Release(GpuMesh mesh, ulong execution)
    {
        if (mesh.Placement is { } old)
            _retiringRanges.Add((execution, old));
        mesh.Placement = null;
    }

    private bool Retire(DeviceBuffer? buffer, ulong execution)
    {
        if (buffer == null)
            return false;

        _retiringBuffers.Add((execution, buffer));
        return true;
    }

    private static uint StreamWords(uint streamsPresent, int stream, uint vertexCount)
        => (streamsPresent & (1u << stream)) != 0 ? MeshLayout.WordsPerVertex((VertexSemantic)stream) * vertexCount : 0;

    private MeshPlacement Place(uint streamsPresent, uint vertexCount, uint indexCount, MeshChunk[] chunks, SubMesh[] subMeshes, AABB bounds, BlendFrameLayout[] blendFrames, uint secondPositions = 0)
    {
        uint vertexWords = 0;
        for (int s = 0; s < MeshLayout.StreamCount; s++)
        {
            if ((streamsPresent & (1u << s)) != 0)
                vertexWords = Align(vertexWords) + StreamWords(streamsPresent, s, vertexCount);
        }
        foreach (BlendFrameLayout frame in blendFrames)
            vertexWords += frame.Words;
        vertexWords += secondPositions;

        indexCount = Math.Max(1, indexCount);
        int pageIndex = -1;
        RangeAllocation vertices = default, indices = default;
        for (int p = 0; p < Pages.Count && pageIndex < 0; p++)
        {
            if (!Pages[p].VertexWords.TryAllocate(vertexWords, StreamAlignment, out vertices))
                continue;
            if (!Pages[p].IndexSpace.TryAllocate(indexCount, 1, out indices))
            {
                Pages[p].VertexWords.Free(vertices);
                continue;
            }
            pageIndex = p;
        }

        if (pageIndex < 0)
        {
            GeometryPage page = NewPage(Math.Max(pageVertexWords, Align(vertexWords)), Math.Max(pageIndices, indexCount));
            Pages.Add(page);
            pageIndex = Pages.Count - 1;
            page.VertexWords.TryAllocate(vertexWords, StreamAlignment, out vertices);
            page.IndexSpace.TryAllocate(indexCount, 1, out indices);
        }

        if (!_chunkSpace.TryAllocate((uint)Math.Max(1, chunks.Length), 1, out RangeAllocation chunkRange))
            throw new InvalidOperationException("The chunk table is full.");

        uint[] offsets = new uint[MeshLayout.StreamCount];
        uint cursor = vertices.Offset;
        for (int s = 0; s < MeshLayout.StreamCount; s++)
        {
            if ((streamsPresent & (1u << s)) == 0)
                continue;

            cursor = Align(cursor);
            offsets[s] = cursor;
            cursor += StreamWords(streamsPresent, s, vertexCount);
        }

        uint second = secondPositions > 0 ? cursor : MeshPlacement.None;
        cursor += secondPositions;
        BlendFrame[] placedFrames = new BlendFrame[blendFrames.Length];
        for (int f = 0; f < blendFrames.Length; f++)
        {
            BlendFrameLayout frame = blendFrames[f];
            uint Take(uint[]? words)
            {
                if (words == null)
                    return BlendFrame.None;
                uint at = cursor;
                cursor += (uint)words.Length;
                return at;
            }
            uint verticesAt = Take(frame.Vertices), positionsAt = Take(frame.Positions), normalsAt = Take(frame.Normals), tangentsAt = Take(frame.Tangents);
            placedFrames[f] = new BlendFrame(frame.Weight, (uint)frame.Vertices.Length, verticesAt, positionsAt, normalsAt, tangentsAt);
        }

        return new MeshPlacement
        {
            Page = pageIndex,
            Vertices = vertices,
            Indices = indices,
            ChunkRange = chunkRange,
            Bounds = bounds,
            StreamOffsets = offsets,
            StreamsPresent = streamsPresent,
            VertexCount = vertexCount,
            Chunks = chunks,
            SubMeshes = subMeshes,
            BlendFrames = placedFrames,
            SecondPositions = second,
        };
    }

    private GeometryPage NewPage(uint vertexWords, uint indexCount)
    {
        DeviceBuffer vertices = device.ResourceFactory.CreateBuffer(new BufferDescription(vertexWords * 4, BufferUsage.StructuredBufferReadWrite));
        DeviceBuffer indices = device.ResourceFactory.CreateBuffer(new BufferDescription(indexCount * 4, BufferUsage.IndexBuffer | BufferUsage.StructuredBufferReadWrite));
        vertices.Name = $"Geometry page {Pages.Count} vertices";
        indices.Name = $"Geometry page {Pages.Count} indices";
        return new GeometryPage(new RangeAllocator(vertexWords), new RangeAllocator(indexCount), vertices, indices);
    }

    private void Write(Uploader uploader, MeshPlacement placement, MeshLayout layout)
    {
        GeometryPage page = Pages[placement.Page];
        for (int s = 0; s < MeshLayout.StreamCount; s++)
        {
            if (layout.Streams[s] is { } stream)
                uploader.Write(page.Vertices, placement.StreamOffsets[s], stream);
        }

        for (int f = 0; f < layout.BlendFrames.Length; f++)
        {
            BlendFrameLayout frame = layout.BlendFrames[f];
            BlendFrame placed = placement.BlendFrames[f];
            uploader.Write(page.Vertices, placed.Vertices, frame.Vertices);
            uploader.Write(page.Vertices, placed.Positions, frame.Positions);
            if (frame.Normals != null)
                uploader.Write(page.Vertices, placed.Normals, frame.Normals);
            if (frame.Tangents != null)
                uploader.Write(page.Vertices, placed.Tangents, frame.Tangents);
        }

        if (layout.Indices.Length > 0)
            uploader.Write(page.Indices, placement.IndexBase, layout.Indices);
    }

    private void WriteChunks(int meshId, MeshPlacement placement)
    {
        StreamOffsets offsets = default;
        for (int s = 0; s < MeshLayout.StreamCount; s++)
            offsets[s] = placement.StreamOffsets[s];

        for (int c = 0; c < placement.Chunks.Length; c++)
        {
            MeshChunk chunk = placement.Chunks[c];
            Chunks.Set(placement.FirstChunk + (uint)c, new ChunkRecord
            {
                StreamOffsets = offsets,
                BoundsMin = chunk.Bounds.Min,
                StreamsPresent = placement.StreamsPresent,
                BoundsMax = chunk.Bounds.Max,
                Page = (uint)placement.Page,
                FirstIndex = placement.IndexBase + chunk.IndexStart,
                IndexCount = chunk.IndexCount,
                Mesh = (uint)meshId,
                SubMesh = (uint)chunk.SubMesh,
            });
        }
    }

    private void WriteMesh(int meshId, MeshPlacement placement)
        => Meshes.Set((uint)meshId, new MeshRecord
        {
            BoundsMin = placement.Bounds.Min,
            FirstChunk = placement.FirstChunk,
            BoundsMax = placement.Bounds.Max,
            ChunkCount = (uint)placement.Chunks.Length,
        });

    private static uint Align(uint words) => (words + StreamAlignment - 1) & ~(StreamAlignment - 1);
}
