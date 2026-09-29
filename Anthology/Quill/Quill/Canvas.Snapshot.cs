// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Numerics;

using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Quill
{
    public partial class Canvas
    {
        private CanvasSnapshot? _capture;
        private int _captureVertexStart;
        private int _captureIndexStart;
        private int _captureDrawCall;
        private int _captureDrawCallElements;
        private int _captureStateDepth = -1;
        private bool _captureBroken;
        private ProwlCanvasState _captureState;
        private int _captureAnchorVersion;
        private int _captureTextDraws;
        private int _captureTessellations;
        private int _captureNewDrawCallRequests;

        private int _anchorVersion;
        private int _newDrawCallRequests;
        internal int _textDraws;
        private int _tessellations;

        /// <summary>True between <see cref="BeginSnapshot"/> and <see cref="EndSnapshot"/>.</summary>
        public bool IsCapturing => _capture != null;

        /// <summary>
        /// Starts recording everything drawn until <see cref="EndSnapshot"/> into <paramref name="snapshot"/>.
        /// (<paramref name="x"/>, <paramref name="y"/>) is the anchor in the current local space, and
        /// passing a different anchor to <see cref="DrawSnapshot"/> moves the geometry with it.
        /// Snapshots do not nest, but a snapshot can be replayed while another is being recorded.
        /// </summary>
        public void BeginSnapshot(CanvasSnapshot snapshot, float x, float y)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (_capture != null) throw new InvalidOperationException("A snapshot is already being captured.");

            _capture = snapshot;
            snapshot.Owner = this;
            _captureVertexStart = _vertices.Count;
            _captureIndexStart = _indices.Count;
            _captureDrawCall = _drawCalls.Count - 1;
            _captureDrawCallElements = _captureDrawCall >= 0 ? _drawCalls[_captureDrawCall].ElementCount : 0;
            _captureStateDepth = _savedStates.Count;
            _captureBroken = false;
            _captureState = _state;
            _captureAnchorVersion = _anchorVersion;
            _captureTextDraws = _textDraws;
            _captureTessellations = _tessellations;
            _captureNewDrawCallRequests = _newDrawCallRequests;

            ref readonly Transform2D t = ref _state.transform;
            snapshot.OriginX = t.A * x + t.C * y + t.E;
            snapshot.OriginY = t.B * x + t.D * y + t.F;
            snapshot.A = t.A;
            snapshot.B = t.B;
            snapshot.C = t.C;
            snapshot.D = t.D;
            snapshot.FramebufferScale = _framebufferScale;
            snapshot.GlobalAlpha = _globalAlpha;
            snapshot.AntiAlias = _antiAlias;
            snapshot.IncomingBrush = _state.brush;
            snapshot.IncomingBrushIsPlain = IsPlain(in _state.brush);
            snapshot.IncomingScissor = _state.scissor;
            snapshot.IncomingScissorExtent = _state.scissorExtent;
            snapshot.AtlasVersion = _scribeRenderer.FontEngine.AtlasVersion;
            snapshot.IsValid = false;
        }

        /// <summary>
        /// Stops recording and stores the geometry. Returns false when the drawing cannot be replayed
        /// faithfully, for example when it left canvas state changed or restored a state saved before
        /// the capture began. The geometry is still drawn this frame either way.
        /// </summary>
        public bool EndSnapshot()
        {
            var s = _capture ?? throw new InvalidOperationException("No snapshot is being captured.");
            int savedStates = _captureStateDepth;
            _capture = null;
            _captureStateDepth = -1;

            bool valid = !_captureBroken
                && _savedStates.Count == savedStates
                && _globalAlpha == s.GlobalAlpha
                && _antiAlias == s.AntiAlias
                && _scribeRenderer.FontEngine.AtlasVersion == s.AtlasVersion
                && SameStateAsCapture();

            int vertexCount = _vertices.Count - _captureVertexStart;
            int indexCount = _indices.Count - _captureIndexStart;
            s.EnsureCapacity(vertexCount, indexCount);
            Array.Copy(_vertices.Array, _captureVertexStart, s.Vertices, 0, vertexCount);

            // Rebase to zero. An index below the captured range wraps to a huge value and fails the bound too.
            uint start = (uint)_captureVertexStart;
            uint max = 0;
            uint[] src = _indices.Array;
            uint[] dst = s.Indices;
            for (int i = 0; i < indexCount; i++)
            {
                uint index = src[_captureIndexStart + i] - start;
                dst[i] = index;
                if (index > max) max = index;
            }
            if (indexCount > 0 && max >= (uint)vertexCount)
                valid = false;

            int previousSegments = s.SegmentCount;
            s.SegmentCount = 0;
            s.HasCustomShader = false;
            int total = 0;
            for (int d = Math.Max(_captureDrawCall, 0); d < _drawCalls.Count; d++)
            {
                ref readonly DrawCall call = ref _drawCalls[d];
                int elements = call.ElementCount - (d == _captureDrawCall ? _captureDrawCallElements : 0);
                if (elements <= 0)
                    continue;

                // A fresh draw call whose state matches the one before it was split on request.
                bool fresh = d != _captureDrawCall || _captureDrawCallElements == 0;
                bool forced = fresh && d > 0 && SameDrawState(in _drawCalls[d - 1], in call.Brush, in call.scissor, call.scissorExtent, call.fontAtlas);

                s.AddSegment(new CanvasSnapshot.Segment {
                    ElementCount = elements,
                    Brush = call.Brush,
                    Scissor = call.scissor,
                    ScissorExtent = call.scissorExtent,
                    FontAtlas = call.fontAtlas,
                    Forced = forced,
                });
                s.HasCustomShader |= call.Brush.Shader != null;
                total += elements;
            }
            s.TrimSegments(previousSegments);

            s.VertexCountInternal = vertexCount;
            s.IndexCountInternal = indexCount;
            s.HasText = _textDraws != _captureTextDraws;
            s.HasTessellation = _tessellations != _captureTessellations;
            s.EndFontAtlas = _currentFontAtlas;
            s.Pinned = _anchorVersion != _captureAnchorVersion;
            s.EndsWithNewDrawCallRequest = _isNewDrawCallRequested && _newDrawCallRequests != _captureNewDrawCallRequests;
            s.IsValid = valid && total == indexCount;
            return s.IsValid;
        }

        /// <summary>
        /// Appends a recorded snapshot anchored at (<paramref name="x"/>, <paramref name="y"/>) in the
        /// current local space, merging into the current batch like any other draw. Returns false and
        /// draws nothing when the snapshot does not fit the current state (different rotation, scale,
        /// alpha, brush, or a rebuilt font atlas), in which case the caller should draw and capture again.
        /// </summary>
        public bool DrawSnapshot(CanvasSnapshot snapshot, float x, float y)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            var s = snapshot;
            if (!s.IsValid || s.Owner != this)
                return false;

            ref readonly Transform2D t = ref _state.transform;
            if (t.A != s.A || t.B != s.B || t.C != s.C || t.D != s.D
                || _framebufferScale != s.FramebufferScale
                || _globalAlpha != s.GlobalAlpha
                || _antiAlias != s.AntiAlias
                || (s.HasText && _scribeRenderer.FontEngine.AtlasVersion != s.AtlasVersion)
                || !_state.brush.Matches(in s.IncomingBrush))
                return false;

            float dx = t.A * x + t.C * y + t.E - s.OriginX;
            float dy = t.B * x + t.D * y + t.F - s.OriginY;
            bool moved = dx != 0f || dy != 0f;

            // Only brushes set inside the capture follow the anchor, and custom shader uniforms may
            // hold positions, so any of these can only replay where they were recorded.
            if (moved && (s.Pinned || s.HasCustomShader || !s.IncomingBrushIsPlain))
                return false;

            // A pinned snapshot may have intersected the scissor it was handed.
            if (s.Pinned && (_state.scissorExtent.X != s.IncomingScissorExtent.X
                || _state.scissorExtent.Y != s.IncomingScissorExtent.Y
                || !DrawCall.SameTransform(in _state.scissor, in s.IncomingScissor)))
                return false;

            int vertexCount = s.VertexCountInternal;
            int indexCount = s.IndexCountInternal;

            // Replaying inside another capture has to leave the same marks drawing it directly would.
            if (s.HasText) _textDraws++;
            if (s.Pinned) _anchorVersion++;

            if (indexCount > 0)
            {
                int vertexBase = _vertices.Count;
                _vertices.Reserve(vertexCount);
                Vertex[] vertices = _vertices.Array;
                if (moved)
                {
                    float px = dx * _framebufferScale;
                    float py = dy * _framebufferScale;
                    Vertex[] src = s.Vertices;
                    for (int i = 0; i < vertexCount; i++)
                    {
                        ref Vertex v = ref vertices[vertexBase + i];
                        v = src[i];
                        v.x += px;
                        v.y += py;
                    }
                }
                else
                {
                    Array.Copy(s.Vertices, 0, vertices, vertexBase, vertexCount);
                }
                _vertices.Count += vertexCount;

                _indices.Reserve(indexCount);
                AddOffset(s.Indices, _indices.Array, _indices.Count, indexCount, (uint)vertexBase);
                _indices.Count += indexCount;

                for (int i = 0; i < s.SegmentCount; i++)
                {
                    ref readonly var segment = ref s.Segments[i];
                    Brush brush = segment.Brush;
                    if (moved)
                    {
                        if (brush.Type != BrushType.None)
                        {
                            brush.Transform.E += dx;
                            brush.Transform.F += dy;
                        }
                        if (brush.Texture != null)
                        {
                            brush.TextureTransform.E += dx;
                            brush.TextureTransform.F += dy;
                        }
                    }

                    // Shapes never sample the atlas, so only segments holding text need their own.
                    object? atlas = s.HasText ? segment.FontAtlas ?? _currentFontAtlas : _currentFontAtlas;
                    if (s.Pinned)
                        AppendSegment(segment.ElementCount, in brush, in segment.Scissor, segment.ScissorExtent, atlas, segment.Forced);
                    else
                        AppendSegment(segment.ElementCount, in brush, in _state.scissor, _state.scissorExtent, atlas, segment.Forced);
                }
            }

            if (s.HasText)
                SetFontAtlas(s.EndFontAtlas);
            if (s.EndsWithNewDrawCallRequest)
                RequestNewDrawCall();
            _verifiedDrawCall = -1;
            return true;
        }

        private static bool IsPlain(in Brush brush) => brush.Type == BrushType.None && brush.Texture == null && brush.Shader == null;

        private static void AddOffset(uint[] src, uint[] dst, int dstStart, int count, uint offset)
        {
            int i = 0;
            if (System.Numerics.Vector.IsHardwareAccelerated)
            {
                int width = Vector<uint>.Count;
                var add = new Vector<uint>(offset);
                for (; i <= count - width; i += width)
                    (new Vector<uint>(src, i) + add).CopyTo(dst, dstStart + i);
            }
            for (; i < count; i++)
                dst[dstStart + i] = src[i] + offset;
        }

        private static bool SameDrawState(in DrawCall call, in Brush brush, in Transform2D scissor, Float2 scissorExtent, object? fontAtlas)
            => ReferenceEquals(call.fontAtlas, fontAtlas)
                && call.scissorExtent.X == scissorExtent.X
                && call.scissorExtent.Y == scissorExtent.Y
                && call.Brush.Matches(in brush)
                && DrawCall.SameTransform(in call.scissor, in scissor);

        // Mirrors AddTriangleCount, but with the state carried by a snapshot segment instead of _state.
        private void AppendSegment(int elements, in Brush brush, in Transform2D scissor, Float2 scissorExtent, object? fontAtlas, bool forceNew)
        {
            if (_drawCalls.Count > 0)
            {
                ref DrawCall last = ref _drawCalls.Last;
                if (!forceNew && !_isNewDrawCallRequested && last.ElementCount != 0
                    && SameDrawState(in last, in brush, in scissor, scissorExtent, fontAtlas))
                {
                    last.ElementCount += elements;
                    return;
                }
            }

            ref DrawCall call = ref _drawCalls.Count > 0 && _drawCalls.Last.ElementCount == 0 ? ref _drawCalls.Last : ref _drawCalls.Add();
            call.ElementCount = elements;
            call.Brush = brush;
            call.scissor = scissor;
            call.scissorExtent = scissorExtent;
            call.fontAtlas = fontAtlas;
            call.scissorInverse = scissor.Inverse();
            call.brushInverse = brush.Transform.Inverse();
            call.textureInverse = brush.TextureTransform.Inverse();
            call.stateHash = ComputeStateHash(in scissorExtent, in scissor, in brush, fontAtlas);
            _isNewDrawCallRequested = false;
        }

        // Canvas state a capture must hand back unchanged, since replay does not reproduce state changes.
        private bool SameStateAsCapture()
        {
            ref readonly var a = ref _state;
            ref readonly var b = ref _captureState;
            return DrawCall.SameTransform(in a.transform, in b.transform)
                && DrawCall.SameTransform(in a.scissor, in b.scissor)
                && a.scissorExtent.X == b.scissorExtent.X && a.scissorExtent.Y == b.scissorExtent.Y
                && a.brush.Matches(in b.brush)
                && a.fillColor.Equals(b.fillColor)
                && a.fillMode == b.fillMode
                && a.strokeColor.Equals(b.strokeColor)
                && a.strokeWidth == b.strokeWidth
                && a.strokeScale == b.strokeScale
                && a.strokeJoint == b.strokeJoint
                && a.strokeStartCap == b.strokeStartCap
                && a.strokeEndCap == b.strokeEndCap
                && a.miterLimit == b.miterLimit
                && a.tess_tol == b.tess_tol
                && a.roundingMinDistance == b.roundingMinDistance;
        }
    }
}
