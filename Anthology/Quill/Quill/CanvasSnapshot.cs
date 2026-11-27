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

        // A tessellated fill triangulates differently at different positions, so comparisons of
        // snapshots holding one fall back to the area covered.
        internal bool HasTessellation;
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

        /// <summary>
        /// Checks that two recordings of the same drawing match, allowing for them having been recorded
        /// at different anchors. Returns null when they match, otherwise a description of the first
        /// difference. Positions may differ by <paramref name="tolerance"/> pixels. Recordings holding
        /// a tessellated fill are compared by the area they cover, since the triangulation depends on
        /// where the fill was drawn.
        /// </summary>
        public static string? Compare(CanvasSnapshot expected, CanvasSnapshot actual, float tolerance = 0.01f)
        {
            if (!expected.IsValid || !actual.IsValid)
                return "one of the recordings is not valid";
            if (expected.FramebufferScale != actual.FramebufferScale)
                return "recorded at different framebuffer scales";

            if (expected.SegmentCount != actual.SegmentCount)
                return $"{expected.SegmentCount} draw segments against {actual.SegmentCount}";
            for (int i = 0; i < expected.SegmentCount; i++)
            {
                ref var e = ref expected.Segments[i];
                ref var a = ref actual.Segments[i];
                if (e.Brush.Type != a.Brush.Type || !ReferenceEquals(e.Brush.Texture, a.Brush.Texture) || !ReferenceEquals(e.Brush.Shader, a.Brush.Shader))
                    return $"segment {i} draws with a different brush";
                if (!expected.HasTessellation && !actual.HasTessellation && e.ElementCount != a.ElementCount)
                    return $"segment {i} holds {e.ElementCount} indices against {a.ElementCount}";
            }

            if (expected.HasTessellation || actual.HasTessellation)
            {
                double areaE = CoveredArea(expected), areaA = CoveredArea(actual);
                double allowed = Math.Max(1.0, Math.Max(areaE, areaA) * 1e-3);
                return Math.Abs(areaE - areaA) <= allowed ? null : $"covers {areaE:F1} square pixels against {areaA:F1}";
            }

            if (expected.VertexCountInternal != actual.VertexCountInternal)
                return $"{expected.VertexCountInternal} vertices against {actual.VertexCountInternal}";
            if (expected.IndexCountInternal != actual.IndexCountInternal)
                return $"{expected.IndexCountInternal} indices against {actual.IndexCountInternal}";

            for (int i = 0; i < expected.IndexCountInternal; i++)
            {
                if (expected.Indices[i] != actual.Indices[i])
                    return $"index {i} is {expected.Indices[i]} against {actual.Indices[i]}";
            }

            // Brought to the same anchor, in pixels.
            float dx = (expected.OriginX - actual.OriginX) * expected.FramebufferScale;
            float dy = (expected.OriginY - actual.OriginY) * expected.FramebufferScale;
            float tol = tolerance * expected.FramebufferScale;
            for (int i = 0; i < expected.VertexCountInternal; i++)
            {
                ref var e = ref expected.Vertices[i];
                ref var a = ref actual.Vertices[i];
                if (Math.Abs(e.x - (a.x + dx)) > tol || Math.Abs(e.y - (a.y + dy)) > tol)
                    return $"vertex {i} sits at ({e.x}, {e.y}) against ({a.x + dx}, {a.y + dy})";
                if (e.u != a.u || e.v != a.v)
                    return $"vertex {i} has uv ({e.u}, {e.v}) against ({a.u}, {a.v})";
                if (e.r != a.r || e.g != a.g || e.b != a.b || e.a != a.a)
                    return $"vertex {i} has a different color";
            }

            return null;
        }

        private static double CoveredArea(CanvasSnapshot s)
        {
            double area = 0;
            for (int i = 0; i + 2 < s.IndexCountInternal; i += 3)
            {
                ref var a = ref s.Vertices[s.Indices[i]];
                ref var b = ref s.Vertices[s.Indices[i + 1]];
                ref var c = ref s.Vertices[s.Indices[i + 2]];
                area += Math.Abs(((double)b.x - a.x) * ((double)c.y - a.y) - ((double)b.y - a.y) * ((double)c.x - a.x)) * 0.5;
            }
            return area;
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
