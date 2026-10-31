using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// The world's built in provider of <see cref="RenderWorld.DrawTransient"/> geometry. Inline geometry is written into
/// the geometry pool each frame and its ranges freed once the frame that drew it is done, no mesh is made for it.
/// </summary>
internal sealed class TransientDraws : RenderProvider
{
    private struct Draw
    {
        public GpuMesh? Mesh;
        public Material Material;
        public DrawItem Item;
        public PrimitiveTopology Topology;
        public int FirstWord, Vertices, FirstIndex, Indices;
        public bool Colors;
        public AABB Bounds;
        public MeshPlacement? Placement;
    }

    private const uint PositionStream = 1u << (int)VertexSemantic.Position, ColorStream = 1u << (int)VertexSemantic.Color;

    private List<Draw> _host = new(), _render = new();
    private List<uint> _hostWords = new(), _renderWords = new();
    private List<uint> _hostIndices = new(), _renderIndices = new();
    private readonly List<MeshPlacement> _placed = new();
    private readonly List<MeshChunk> _chunks = new();

    /// <summary>Copies a draw for the next frame. Host thread.</summary>
    public void Add(in TransientDraw draw)
    {
        ArgumentNullException.ThrowIfNull(draw.Material);
        Draw entry = new()
        {
            Mesh = draw.Mesh,
            Material = draw.Material,
            Item = new DrawItem { Transform = draw.Transform, LayerMask = draw.LayerMask, Flags = draw.Flags },
            Topology = draw.Topology,
        };
        if (draw.Mesh == null)
        {
            ReadOnlySpan<Float3> positions = draw.Positions.Span;
            if (positions.IsEmpty)
                throw new ArgumentException("A transient draw needs a mesh or positions.", nameof(draw));
            if (!draw.Colors.IsEmpty && draw.Colors.Length != positions.Length)
                throw new ArgumentException($"A transient draw has {positions.Length} positions and {draw.Colors.Length} colours.", nameof(draw));
            ReadOnlySpan<uint> indices = draw.Indices.Span;
            foreach (uint index in indices)
            {
                if (index >= positions.Length)
                    throw new ArgumentException($"Index {index} is past the {positions.Length} positions.", nameof(draw));
            }

            AABB bounds = new(positions[0], positions[0]);
            foreach (Float3 position in positions)
                bounds.Encapsulate(position);
            entry.Bounds = bounds;
            entry.FirstWord = _hostWords.Count;
            entry.Vertices = positions.Length;
            entry.Colors = !draw.Colors.IsEmpty;
            _hostWords.AddRange(MemoryMarshal.Cast<Float3, uint>(positions));
            if (entry.Colors)
                _hostWords.AddRange(draw.Colors.Span);

            entry.FirstIndex = _hostIndices.Count;
            entry.Indices = indices.IsEmpty ? positions.Length : indices.Length;
            if (indices.IsEmpty)
            {
                for (int i = 0; i < positions.Length; i++)
                    _hostIndices.Add((uint)i);
            }
            else
            {
                _hostIndices.AddRange(indices);
            }
        }
        _host.Add(entry);
    }

    /// <summary>Hands the draws added since the last frame to the render side. Host thread, at SubmitFrame.</summary>
    public void Swap()
    {
        (_host, _render) = (_render, _host);
        (_hostWords, _renderWords) = (_renderWords, _hostWords);
        (_hostIndices, _renderIndices) = (_renderIndices, _hostIndices);
        _host.Clear();
        _hostWords.Clear();
        _hostIndices.Clear();
    }

    /// <summary>
    /// Frees last frame's ranges once <paramref name="lastDispatched"/> retires and writes this frame's inline geometry
    /// into the pool. Render thread, before the views cull.
    /// </summary>
    public void Place(GeometryPool pool, Uploader uploader, ulong lastDispatched)
    {
        Release(pool, lastDispatched);
        Span<Draw> draws = CollectionsMarshal.AsSpan(_render);
        ReadOnlySpan<uint> words = CollectionsMarshal.AsSpan(_renderWords), indices = CollectionsMarshal.AsSpan(_renderIndices);
        for (int d = 0; d < draws.Length; d++)
        {
            ref Draw draw = ref draws[d];
            if (draw.Mesh != null)
                continue;
            uint vertices = (uint)draw.Vertices, count = (uint)draw.Indices;
            ReadOnlySpan<uint> positions = words.Slice(draw.FirstWord, draw.Vertices * 3);
            ReadOnlySpan<uint> drawIndices = indices.Slice(draw.FirstIndex, draw.Indices);
            MeshPlacement placement = pool.PlaceTransient(draw.Colors ? PositionStream | ColorStream : PositionStream, vertices, count, Chunks(positions, drawIndices, draw.Topology), [new SubMesh { IndexStart = 0, IndexCount = count, Topology = draw.Topology }], draw.Bounds);
            DeviceBuffer page = pool.Pages[placement.Page].Vertices;
            uploader.Write(page, placement.StreamOffsets[(int)VertexSemantic.Position], positions);
            if (draw.Colors)
                uploader.Write(page, placement.StreamOffsets[(int)VertexSemantic.Color], words.Slice(draw.FirstWord + draw.Vertices * 3, draw.Vertices));
            uploader.Write(pool.Pages[placement.Page].Indices, placement.IndexBase, drawIndices);
            draw.Placement = placement;
            _placed.Add(placement);
        }
    }

    /// <summary>Frees the ranges placed for the last frame once <paramref name="lastDispatched"/> retires. Render thread or idle.</summary>
    public void Release(GeometryPool pool, ulong lastDispatched)
    {
        foreach (MeshPlacement placement in _placed)
            pool.ReleaseCopy(placement, lastDispatched);
        _placed.Clear();
    }

    // The draw's chunks of about MeshLayout.ChunkTriangles each, bounded by the positions they index
    private MeshChunk[] Chunks(ReadOnlySpan<uint> positions, ReadOnlySpan<uint> indices, PrimitiveTopology topology)
    {
        _chunks.Clear();
        uint size = MeshLayout.ChunkIndexCount(topology);
        for (uint start = 0; start < indices.Length; start += size)
        {
            ReadOnlySpan<uint> chunk = indices.Slice((int)start, (int)Math.Min(size, (uint)indices.Length - start));
            AABB bounds = new(Position(positions, chunk[0]), Position(positions, chunk[0]));
            foreach (uint index in chunk)
                bounds.Encapsulate(Position(positions, index));
            _chunks.Add(new MeshChunk(0, start, (uint)chunk.Length, bounds));
        }
        return [.. _chunks];
    }

    private static Float3 Position(ReadOnlySpan<uint> words, uint index)
        => new(BitConverter.UInt32BitsToSingle(words[(int)index * 3]), BitConverter.UInt32BitsToSingle(words[(int)index * 3 + 1]), BitConverter.UInt32BitsToSingle(words[(int)index * 3 + 2]));

    public override void Cull(in ProviderView view, DrawItemSink sink)
    {
        foreach (Draw draw in _render)
        {
            MeshPlacement? placement = draw.Mesh != null ? draw.Mesh.Placement : draw.Placement;
            if (placement == null || (draw.Mesh?.IsDisposed ?? false))
                continue;
            (Double3 center, Float3 extents) = RenderWorld.RenderSide.TransformBox(draw.Item.Transform, placement.Bounds);
            if (!view.Sees(center, MathF.Sqrt(extents.X * extents.X + extents.Y * extents.Y + extents.Z * extents.Z)))
                continue;
            for (int s = 0; s < placement.SubMeshes.Length; s++)
                sink.AddPlaced(placement, s, draw.Material, draw.Item);
        }
    }
}
