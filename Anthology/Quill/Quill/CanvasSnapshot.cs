// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Quill
{
    /// <summary>
    /// Finished geometry recorded by <see cref="Canvas.BeginSnapshot"/> and <see cref="Canvas.EndSnapshot"/>,
    /// which <see cref="Canvas.DrawSnapshot"/> appends again without tessellating anything.
    /// Keep one instance per cached item, its buffers are reused and only ever grow. A snapshot only
    /// replays on the canvas that recorded it, since its text points into that canvas' font atlas.
    /// </summary>
    public sealed class CanvasSnapshot
    {
        internal struct Segment
        {
            public int ElementCount;
            public Brush Brush;
            public Transform2D Scissor;
            public Float2 ScissorExtent;
            public object? FontAtlas;

            // Split from the draw call before it only because a new one was requested.
            public bool Forced;
        }

        internal Vertex[] Vertices = Array.Empty<Vertex>();
        internal uint[] Indices = Array.Empty<uint>();
        internal Segment[] Segments = Array.Empty<Segment>();
        internal int VertexCountInternal;
        internal int IndexCountInternal;
        internal int SegmentCount;

        internal Canvas? Owner;

        // The state the geometry was baked against. Replay needs the same linear transform, scale,
        // alpha and brush, and only the translation is allowed to differ.
        internal float OriginX, OriginY;
        internal float A, B, C, D;
        internal float FramebufferScale;
        internal float GlobalAlpha;
        internal bool AntiAlias;
        internal Brush IncomingBrush;
        internal bool IncomingBrushIsPlain;
        internal Transform2D IncomingScissor;
        internal Float2 IncomingScissorExtent;

        internal bool HasText;
        internal int AtlasVersion;
        internal object? EndFontAtlas;
        internal bool HasCustomShader;
        internal bool EndsWithNewDrawCallRequest;

        // Set when the drawing replaced state outright (transform, scissor, texture mapping) instead
        // of building on what it was handed, so it only reproduces where it was recorded.
        internal bool Pinned;

        /// <summary>True when the last capture succeeded and can be replayed.</summary>
        public bool IsValid { get; internal set; }

        /// <summary>Number of vertices recorded.</summary>
        public int VertexCount => VertexCountInternal;

        /// <summary>Number of indices recorded.</summary>
        public int IndexCount => IndexCountInternal;

        /// <summary>Discards the recorded geometry so the next <see cref="Canvas.DrawSnapshot"/> fails.</summary>
        public void Invalidate() => IsValid = false;

        internal void EnsureCapacity(int vertices, int indices)
        {
            if (Vertices.Length < vertices)
                Vertices = new Vertex[Grow(Vertices.Length, vertices)];
            if (Indices.Length < indices)
                Indices = new uint[Grow(Indices.Length, indices)];
        }

        internal void AddSegment(in Segment segment)
        {
            if (SegmentCount == Segments.Length)
                Array.Resize(ref Segments, Math.Max(4, Segments.Length * 2));
            Segments[SegmentCount++] = segment;
        }

        // Drops references to textures, shaders and atlases from segments past the current count.
        internal void TrimSegments(int previousCount)
        {
            if (previousCount > SegmentCount)
                Array.Clear(Segments, SegmentCount, previousCount - SegmentCount);
        }

        private static int Grow(int current, int needed)
        {
            int capacity = Math.Max(current, 16);
            while (capacity < needed)
                capacity *= 2;
            return capacity;
        }
    }
}
