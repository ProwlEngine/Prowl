// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;


namespace Prowl.Runtime.GUI
{
    public static class CornerRounding
    {
        public const int None = 0;

        public const int TopLeft = 1;
        public const int TopRight = 2;
        public const int BottomLeft = 8;
        public const int BottomRight = 4;

        public const int Top = 3;
        public const int Bottom = 12;
        public const int Left = 9;
        public const int Right = 6;

        public const int BottomRightAndTopLeft = 5;
        public const int BottomLeftAndTopRight = 10;

        public const int RightAndTop = 7;
        public const int TopAndLeft = 11;
        public const int BottomAndLeft = 13;
        public const int BottomAndRight = 14;

        public const int All = 15;
    }
}


namespace Prowl.Runtime.GUI.Graphics
{
    public class UIDrawChannel
    {
        public List<UIDrawCmd> CommandList { get; private set; } = [];
        public List<uint> IndexBuffer { get; private set; } = [];
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct UIVertex(System.Numerics.Vector3 position, System.Numerics.Vector2 UV, Color32 color)
    {
        public System.Numerics.Vector3 Position = position;
        public System.Numerics.Vector2 UV = UV;
        public Color32 Color = color;
    }

    public struct UIDrawCmd
    {
        public uint ElemCount; // Number of indices (multiple of 3) to be rendered as triangles. Vertices are stored in the callee ImDrawList's vtx_buffer[] array, indices in idx_buffer[].
        public Vector4 ClipRect; // Clipping rectangle (x1, y1, x2, y2)
        public Texture2D Texture; // User-provided texture ID. Set by user in ImfontAtlas::SetTexID() for fonts or passed to Image*() functions. Ignore if never using images or multiple fonts atlas.
    }

    // This is essentially a port of the ImGui ImDrawList class to C#. Rendering is handled in UIDrawListRenderer.cs
    public class UIDrawList
    {
        private static readonly Vector4 s_gNullClipRect = new Vector4(-8192.0f, -8192.0f, +8192.0f, +8192.0f);

        // This is what you have to render
        internal List<UIDrawCmd> _commandList; // Commands. Typically 1 command = 1 gpu draw call.

        internal List<uint> _indices; // Index buffer. Each command consume ImDrawCmd::ElemCount of those
        internal List<UIVertex> _vertices; // Vertex buffer.

        private uint _currentVertexIndex; // == _vertices.Count
        private int _vertexWritePos; // point within _vertices after each add command (to avoid using the ImVector<> operators too much)
        private int _indexWritePos; // point within _indices after each add command (to avoid using the ImVector<> operators too much)
        private readonly List<Vector4> _clipRectStack;
        private readonly List<Texture2D> _textureStack;
        private readonly List<Vector2> _buildingPath;   // current path building
        private int _currentChannel;                    // current channel number (0)
        private int _activeChannels;                    // number of active channels (1+)
        private readonly List<UIDrawChannel> _channels; // draw channels for columns API (not resized down so _channelsCount may be smaller than _channels.Count)
        private int _primitiveCount = -10000;

        private bool _antiAliasing;



        public UIDrawList(bool antiAliasing)
        {
            _commandList = [];
            _indices = [];
            _vertices = [];
            _clipRectStack = [];
            _textureStack = [];
            _buildingPath = [];
            _channels = [];

            _primitiveCount = -10000;
            _antiAliasing = antiAliasing;

            Clear();
        }


        public void AntiAliasing(bool antiAliasing)
        {
            _antiAliasing = antiAliasing;
        }


        public UIDrawCmd? GetCurrentDrawCmd()
        {
            return _commandList.Count > 0 ? _commandList[^1] : null;
        }


        public void SetCurrentDrawCmd(UIDrawCmd cmd)
        {
            System.Diagnostics.Debug.Assert(_commandList.Count > 0);
            _commandList[^1] = cmd;
        }


        public UIDrawCmd? GetPreviousDrawCmd()
        {
            return _commandList.Count > 1 ? _commandList[^2] : null;
        }


        public Vector4 GetCurrentClipRect()
        {
            return _clipRectStack.Count > 0 ? _clipRectStack[^1] : s_gNullClipRect;
        }


        public Texture2D GetCurrentTexture()
        {
            return _textureStack.Count > 0 ? _textureStack[^1] : Font.DefaultFont.Texture;
        }


        public void Clear()
        {
            _indices.Clear();
            _vertices.Clear();

            _commandList.Clear();

            _clipRectStack.Clear();
            _textureStack.Clear();
            _buildingPath.Clear();

            _channels.Clear();

            _currentVertexIndex = 0;
            _vertexWritePos = -1;
            _indexWritePos = -1;

            _currentChannel = 0;
            _activeChannels = 1;

            _primitiveCount = -10000;

            // Add Initial Draw Command
            AddDrawCmd();
            PushClipRectFullScreen();
        }


        public void PushClipRect(Vector4 clip_rect, bool force = false)  // Scissoring. Note that the values are (x1,y1,x2,y2) and NOT (x1,y1,w,h). This is passed down to your render function but not used for CPU-side clipping. Prefer using higher-level ImGui::PushClipRect() to affect logic (hit-testing and widget culling)
        {
            if (!force && _clipRectStack.Count > 0)
                clip_rect = IntersectRects(_clipRectStack.Peek(), clip_rect);

            _clipRectStack.Add(clip_rect);
            UpdateClipRect(force);
        }


        private Vector4 IntersectRects(Vector4 rectA, Vector4 rectB)
        {
            double left = MathD.Max(rectA.x, rectB.x);
            double top = MathD.Max(rectA.y, rectB.y);
            double right = MathD.Min(rectA.z, rectB.z);
            double bottom = MathD.Min(rectA.w, rectB.w);

            if (right < left || bottom < top)
            {
                // No intersection, return an empty rect
                return new Vector4(0, 0, 0, 0);
            }

            return new Vector4(left, top, right, bottom);
        }


        public void PushClipRectFullScreen()
        {
            PushClipRect(s_gNullClipRect, true);
        }


        public void PopClipRect()
        {
            System.Diagnostics.Debug.Assert(_clipRectStack.Count > 0);
            _clipRectStack.Pop();
            UpdateClipRect();
        }


        public Vector4 PeekClipRect()
        {
            return _clipRectStack.Peek();
        }


        public void PushTexture(Texture2D texture_id)
        {
            _textureStack.Add(texture_id);
            UpdateTextureID();
        }


        public void PopTexture()
        {
            System.Diagnostics.Debug.Assert(_textureStack.Count > 0);
            _textureStack.Pop();
            UpdateTextureID();
        }


        // Primitives
        public void AddLine(Vector2 a, Vector2 b, Color32 col, float thickness = 1.0f)
        {
            PathLineTo(a + new Vector2(0.5f, 0.5f));
            PathLineTo(b + new Vector2(0.5f, 0.5f));
            PathStroke(col, false, thickness);
        }


        // a: upper-left, b: lower-right
        public void AddRect(Vector2 a, Vector2 b, Color32 col, float rounding = 0.0f, int corners = CornerRounding.None, float thickness = 1.0f)
        {
            PathRect(a + new Vector2(0.5f, 0.5f), b - new Vector2(0.5f, 0.5f), rounding, corners);
            PathStroke(col, true, thickness);
        }


        // a: upper-left, b: lower-right
        public void AddRectFilled(Vector2 a, Vector2 b, Color32 col, float rounding = 0.0f, int corners = CornerRounding.None)
        {
            if (rounding > 0.0f)
            {
                PathRect(a, b, rounding, corners);
                PathFill(col);
            }
            else
            {
                PrimReserve(6, 4);
                PrimRect(a, b, col);
            }
        }


        public void AddRectFilledMultiColor(Vector2 a, Vector2 c, Color32 col_upr_left, Color32 col_upr_right, Color32 col_bot_right, Color32 col_bot_left)
        {
            PrimReserve(6, 4);

            Vector2 uv = Font.DefaultFont.TexUvWhitePixel;
            var b = new Vector2(c.x, a.y);
            var d = new Vector2(a.x, c.y);

            AddVerts(
                new UIVertex(new(a, _primitiveCount), uv, col_upr_left),
                new UIVertex(new(b, _primitiveCount), uv, col_upr_right),
                new UIVertex(new(c, _primitiveCount), uv, col_bot_right),
                new UIVertex(new(d, _primitiveCount), uv, col_bot_left)
            );
        }


        public void AddTriangle(Vector2 a, Vector2 b, Vector2 c, Color32 col, float thickness = 1.0f)
        {
            PathLineTo(a);
            PathLineTo(b);
            PathLineTo(c);
            PathStroke(col, true, thickness);
        }


        public void AddTriangleFilled(Vector2 a, Vector2 b, Vector2 c, Color32 col)
        {
            PathLineTo(a);
            PathLineTo(b);
            PathLineTo(c);
            PathFill(col);
        }


        public void AddCircle(Vector2 centre, float radius, Color32 col, int num_segments = 12, float thickness = 1.0f)
        {
            float a_max = MathF.PI * 2.0f * (num_segments - 1.0f) / num_segments;
            PathArcTo(centre, radius - 0.5f, 0.0f, a_max, num_segments);
            PathStroke(col, true, thickness);
        }


        public void AddCircleFilled(Vector2 centre, float radius, Color32 col, int num_segments = 12)
        {
            float a_max = MathF.PI * 2.0f * ((num_segments - 1.0f) / num_segments);
            PathArcTo(centre, radius, 0.0f, a_max, num_segments);
            PathFill(col);
        }


        public void AddText(float font_size, Vector2 pos, Color32 col, string text, int text_begin = 0, int text_end = -1, float wrap_width = 0.0f)
        {
            AddText(Font.DefaultFont, font_size, pos, col, text, text_begin, text_end, wrap_width);
        }


        public void AddText(Font font, float font_size, Vector2 pos, Color32 col, string text, int text_begin = 0, int text_end = -1, float wrap_width = 0.0f, Vector4? cpu_fine_clip_rect = null)
        {
            ArgumentNullException.ThrowIfNull(font);
            if (font_size <= 0.0f)
                return;

            if (text_end == -1)
                text_end = text.Length;

            if (text_begin == text_end)
                return;

            System.Diagnostics.Debug.Assert(font.Texture == GetCurrentTexture());

            // reserve vertices for worse case (over-reserving is useful and easily amortized)
            int char_count = text_end - text_begin;
            int vtx_count_max = char_count * 4;
            int idx_count_max = char_count * 6;

            int idx_begin = _indices.Count;

            PrimReserve(idx_count_max, vtx_count_max);

            Vector4 clip_rect = _clipRectStack[_clipRectStack.Count - 1];
            if (cpu_fine_clip_rect.HasValue)
            {
                Vector4 cfcr = cpu_fine_clip_rect.Value;
                clip_rect.x = MathD.Max(clip_rect.x, cfcr.x);
                clip_rect.y = MathD.Max(clip_rect.y, cfcr.y);
                clip_rect.z = MathD.Min(clip_rect.z, cfcr.z);
                clip_rect.w = MathD.Min(clip_rect.w, cfcr.w);
            }

            font.RenderText(font_size, pos, col, clip_rect, text, text_begin, text_end, this, wrap_width, cpu_fine_clip_rect.HasValue);

            // give back unused vertices
            // FIXME-OPT: clean this up
            _vertices.Resize(_vertexWritePos);
            _indices.Resize(_indexWritePos);

            int idx_unused = idx_count_max - (_indices.Count - idx_begin);

            UIDrawCmd curr_cmd = _commandList[_commandList.Count - 1];
            curr_cmd.ElemCount -= (uint)idx_unused;

            _commandList[_commandList.Count - 1] = curr_cmd;

            _currentVertexIndex = (uint)_vertices.Count;
        }


        public void AddImage(Texture2D user_texture_id, Vector2 a, Vector2 b, Vector2? _uv0 = null, Vector2? _uv1 = null, Color32? _col = null)
        {
            Vector2 uv0 = _uv0 ?? new Vector2(0, 0);
            Vector2 uv1 = _uv1 ?? new Vector2(1, 1);
            Color32 col = _col ?? Color.white;

            // FIXME-OPT: This is wasting draw calls.
            bool push_texture_id = _textureStack.Count == 0 || user_texture_id != GetCurrentTexture();

            if (push_texture_id)
                PushTexture(user_texture_id);

            PrimReserve(6, 4);
            PrimRectUV(a, b, uv0, uv1, col);

            //PrimRect(a, b, col);
            if (push_texture_id)
                PopTexture();
        }

        private Vector2[] _tempNormals;
        private Vector2[] _tempPoints;

        public void AddPolyline(List<Vector2> points, int pointsCount, Color32 col, bool closed, float thickness)
        {
            if (pointsCount < 2)
                return;

            if (_tempNormals == null || _tempNormals.Length < pointsCount * 5)
            {
                _tempNormals = new Vector2[pointsCount * 5];
                _tempPoints = new Vector2[pointsCount * 5];
            }

            Vector2 uv = Font.DefaultFont.TexUvWhitePixel;
            int count = closed ? pointsCount : pointsCount - 1;
            bool thickLine = thickness > 1.0f;

            if (_antiAliasing)
            {
                float AA_SIZE = 1.0f;
                Color32 col_trans = new Color32(col.r, col.g, col.b, 0);

                int idx_count = thickLine ? count * 18 : count * 12;
                int vtx_count = thickLine ? pointsCount * 4 : pointsCount * 3;
                PrimReserve(idx_count, vtx_count);

                for (int i1 = 0; i1 < count; i1++)
                {
                    int i2 = (i1 + 1) == pointsCount ? 0 : i1 + 1;
                    Vector2 diff = points[i2] - points[i1];
                    double d = diff.x * diff.x + diff.y * diff.y;
                    if (d > 0.0f)
                        diff *= 1.0f / (float)Math.Sqrt(d);

                    _tempNormals[i1] = new Vector2(diff.y, -diff.x);
                }

                if (!closed)
                    _tempNormals[pointsCount - 1] = _tempNormals[pointsCount - 2];

                if (!thickLine)
                {
                    if (!closed)
                    {
                        _tempPoints[0] = points[0] + _tempNormals[0] * AA_SIZE;
                        _tempPoints[1] = points[0] - _tempNormals[0] * AA_SIZE;
                        _tempPoints[(pointsCount - 1) * 2 + 0] = points[pointsCount - 1] + _tempNormals[pointsCount - 1] * AA_SIZE;
                        _tempPoints[(pointsCount - 1) * 2 + 1] = points[pointsCount - 1] - _tempNormals[pointsCount - 1] * AA_SIZE;
                    }

                    // FIXME-OPT: Merge the different loops, possibly remove the temporary buffer.
                    uint idx1 = _currentVertexIndex;
                    for (int i1 = 0; i1 < count; i1++)
                    {
                        int i2 = i1 + 1 == pointsCount ? 0 : i1 + 1;
                        uint idx2 = i1 + 1 == pointsCount ? _currentVertexIndex : idx1 + 3;

                        // Average normals
                        Vector2 dm = (_tempNormals[i1] + _tempNormals[i2]) * 0.5f;
                        double dmr2 = dm.x * dm.x + dm.y * dm.y;
                        if (dmr2 > 0.000001f)
                        {
                            double scale = 1.0f / dmr2;
                            if (scale > 100.0f) scale = 100.0f;
                            dm *= scale;
                        }
                        dm *= AA_SIZE;
                        _tempPoints[i2 * 2 + 0] = points[i2] + dm;
                        _tempPoints[i2 * 2 + 1] = points[i2] - dm;

                        // Add indexes

                        _indices[_indexWritePos++] = idx2 + 0; _indices[_indexWritePos++] = idx1 + 0; _indices[_indexWritePos++] = idx1 + 2;
                        _indices[_indexWritePos++] = idx1 + 2; _indices[_indexWritePos++] = idx2 + 2; _indices[_indexWritePos++] = idx2 + 0;
                        _indices[_indexWritePos++] = idx2 + 1; _indices[_indexWritePos++] = idx1 + 1; _indices[_indexWritePos++] = idx1 + 0;
                        _indices[_indexWritePos++] = idx1 + 0; _indices[_indexWritePos++] = idx2 + 0; _indices[_indexWritePos++] = idx2 + 1;
                        //_IdxWritePtr += 12;

                        idx1 = idx2;
                    }

                    // Add vertexes
                    for (int i = 0; i < pointsCount; i++)
                    {
                        _vertices[_vertexWritePos++] = new UIVertex(new(points[i], _primitiveCount), uv, col);
                        _vertices[_vertexWritePos++] = new UIVertex(new(_tempPoints[i * 2 + 0], _primitiveCount), uv, col_trans);
                        _vertices[_vertexWritePos++] = new UIVertex(new(_tempPoints[i * 2 + 1], _primitiveCount), uv, col_trans);
                    }
                }
                else
                {
                    float half_inner_thickness = (thickness - AA_SIZE) * 0.5f;
                    if (!closed)
                    {
                        _tempPoints[0] = points[0] + _tempNormals[0] * (half_inner_thickness + AA_SIZE);
                        _tempPoints[1] = points[0] + _tempNormals[0] * half_inner_thickness;
                        _tempPoints[2] = points[0] - _tempNormals[0] * half_inner_thickness;
                        _tempPoints[3] = points[0] - _tempNormals[0] * (half_inner_thickness + AA_SIZE);

                        _tempPoints[(pointsCount - 1) * 4 + 0] = points[pointsCount - 1] + _tempNormals[pointsCount - 1] * (half_inner_thickness + AA_SIZE);
                        _tempPoints[(pointsCount - 1) * 4 + 1] = points[pointsCount - 1] + _tempNormals[pointsCount - 1] * half_inner_thickness;
                        _tempPoints[(pointsCount - 1) * 4 + 2] = points[pointsCount - 1] - _tempNormals[pointsCount - 1] * half_inner_thickness;
                        _tempPoints[(pointsCount - 1) * 4 + 3] = points[pointsCount - 1] - _tempNormals[pointsCount - 1] * (half_inner_thickness + AA_SIZE);
                    }

                    // FIXME-OPT: Merge the different loops, possibly remove the temporary buffer.
                    uint idx1 = _currentVertexIndex;
                    for (int i1 = 0; i1 < count; i1++)
                    {
                        int i2 = i1 + 1 == pointsCount ? 0 : i1 + 1;
                        uint idx2 = i1 + 1 == pointsCount ? _currentVertexIndex : idx1 + 4;

                        // Average normals
                        Vector2 dm = (_tempNormals[i1] + _tempNormals[i2]) * 0.5f;
                        double dmr2 = dm.x * dm.x + dm.y * dm.y;
                        if (dmr2 > 0.000001f)
                        {
                            double scale = 1.0f / dmr2;
                            if (scale > 100.0f) scale = 100.0f;
                            dm *= scale;
                        }
                        Vector2 dm_out = dm * (half_inner_thickness + AA_SIZE);
                        Vector2 dm_in = dm * half_inner_thickness;
                        _tempPoints[i2 * 4 + 0] = points[i2] + dm_out;
                        _tempPoints[i2 * 4 + 1] = points[i2] + dm_in;
                        _tempPoints[i2 * 4 + 2] = points[i2] - dm_in;
                        _tempPoints[i2 * 4 + 3] = points[i2] - dm_out;

                        // Add indexes
                        _indices[_indexWritePos++] = (idx2 + 1); _indices[_indexWritePos++] = (idx1 + 1); _indices[_indexWritePos++] = (idx1 + 2);
                        _indices[_indexWritePos++] = (idx1 + 2); _indices[_indexWritePos++] = (idx2 + 2); _indices[_indexWritePos++] = (idx2 + 1);
                        _indices[_indexWritePos++] = (idx2 + 1); _indices[_indexWritePos++] = (idx1 + 1); _indices[_indexWritePos++] = (idx1 + 0);
                        _indices[_indexWritePos++] = (idx1 + 0); _indices[_indexWritePos++] = (idx2 + 0); _indices[_indexWritePos++] = (idx2 + 1);
                        _indices[_indexWritePos++] = (idx2 + 2); _indices[_indexWritePos++] = (idx1 + 2); _indices[_indexWritePos++] = (idx1 + 3);
                        _indices[_indexWritePos++] = (idx1 + 3); _indices[_indexWritePos++] = (idx2 + 3); _indices[_indexWritePos++] = (idx2 + 2);
                        //_IdxWritePtr += 18;

                        idx1 = idx2;
                    }

                    // Add vertexes
                    for (int i = 0; i < pointsCount; i++)
                    {
                        _vertices[_vertexWritePos++] = new UIVertex(new(_tempPoints[i * 4 + 0], _primitiveCount), uv, col_trans);
                        _vertices[_vertexWritePos++] = new UIVertex(new(_tempPoints[i * 4 + 1], _primitiveCount), uv, col);
                        _vertices[_vertexWritePos++] = new UIVertex(new(_tempPoints[i * 4 + 2], _primitiveCount), uv, col);
                        _vertices[_vertexWritePos++] = new UIVertex(new(_tempPoints[i * 4 + 3], _primitiveCount), uv, col_trans);
                        //_VtxWritePtr += 4;
                    }
                }
                _currentVertexIndex += (uint)vtx_count;
            }
            else
            {
                int idx_count = count * 6;
                int vtx_count = count * 4;      // FIXME-OPT: Not sharing edges
                PrimReserve(idx_count, vtx_count);

                for (int i1 = 0; i1 < count; i1++)
                {
                    int i2 = i1 + 1 == pointsCount ? 0 : i1 + 1;
                    Vector2 p1 = points[i1];
                    Vector2 p2 = points[i2];
                    Vector2 diff = p2 - p1;

                    double d = diff.x * diff.x + diff.y * diff.y;
                    if (d > 0.0f)
                        diff *= 1.0f / (float)Math.Sqrt(d);

                    double dx = diff.x * (thickness * 0.5f);
                    double dy = diff.y * (thickness * 0.5f);

                    _vertices[_vertexWritePos++] = new UIVertex(new Vector3(p1.x + dy, p1.y - dx, _primitiveCount), uv, col);
                    _vertices[_vertexWritePos++] = new UIVertex(new Vector3(p2.x + dy, p2.y - dx, _primitiveCount), uv, col);
                    _vertices[_vertexWritePos++] = new UIVertex(new Vector3(p2.x - dy, p2.y + dx, _primitiveCount), uv, col);
                    _vertices[_vertexWritePos++] = new UIVertex(new Vector3(p1.x - dy, p1.y + dx, _primitiveCount), uv, col);
                    //_VtxWritePtr += 4;

                    _indices[_indexWritePos++] = _currentVertexIndex; _indices[_indexWritePos++] = _currentVertexIndex + 1; _indices[_indexWritePos++] = _currentVertexIndex + 2;
                    _indices[_indexWritePos++] = _currentVertexIndex; _indices[_indexWritePos++] = _currentVertexIndex + 2; _indices[_indexWritePos++] = _currentVertexIndex + 3;
                    //_IdxWritePtr += 6;
                    _currentVertexIndex += 4;
                }
            }
            _primitiveCount++;
        }


        public void AddConvexPolyFilled(List<Vector2> points, int points_count, Color32 col)
        {

            if (points_count < 3)
                return;

            //Vector2 uv = ImGui.Instance.FontTexUvWhitePixel;
            Vector2 uv = Font.DefaultFont.TexUvWhitePixel;

            if (_antiAliasing)
            {
                // Anti-aliased Fill
                float AA_SIZE = 1.0f;
                Color32 col_trans = new Color32(col.r, col.g, col.b, 0);
                int idx_count = (points_count - 2) * 3 + points_count * 6;
                int vtx_count = points_count * 2;
                PrimReserve(idx_count, vtx_count);

                // Add indexes for fill
                uint vtx_inner_idx = _currentVertexIndex;
                uint vtx_outer_idx = _currentVertexIndex + 1;
                for (int i = 2; i < points_count; i++)
                {
                    _indices[_indexWritePos++] = vtx_inner_idx;
                    _indices[_indexWritePos++] = (uint)(vtx_inner_idx + (i - 1 << 1));
                    _indices[_indexWritePos++] = (uint)(vtx_inner_idx + (i << 1));
                }

                // Compute normals
                Vector2[] temp_normals = new Vector2[points_count];

                for (int i0 = points_count - 1, i1 = 0; i1 < points_count; i0 = i1++)
                {
                    Vector2 p0 = points[i0];
                    Vector2 p1 = points[i1];
                    Vector2 diff = p1 - p0;

                    double d = diff.x * diff.x + diff.y * diff.y;
                    if (d > 0.0f)
                        diff *= 1.0f / (float)Math.Sqrt(d);

                    temp_normals[i0].x = diff.y;
                    temp_normals[i0].y = -diff.x;
                }

                for (int i0 = points_count - 1, i1 = 0; i1 < points_count; i0 = i1++)
                {
                    // Average normals
                    Vector2 n0 = temp_normals[i0];
                    Vector2 n1 = temp_normals[i1];
                    Vector2 dm = (n0 + n1) * 0.5f;
                    double dmr2 = dm.x * dm.x + dm.y * dm.y;
                    if (dmr2 > 0.000001f)
                    {
                        double scale = 1.0f / dmr2;
                        if (scale > 100.0f) scale = 100.0f;
                        dm *= scale;
                    }
                    dm *= AA_SIZE * 0.5f;

                    // Add vertices
                    _vertices[_vertexWritePos++] = new UIVertex() { Position = new(points[i1] - dm, _primitiveCount), UV = uv, Color = col };
                    _vertices[_vertexWritePos++] = new UIVertex() { Position = new(points[i1] + dm, _primitiveCount), UV = uv, Color = col_trans };

                    // Add indexes for fringes

                    _indices[_indexWritePos++] = (uint)(vtx_inner_idx + (i1 << 1)); _indices[_indexWritePos++] = (uint)(vtx_inner_idx + (i0 << 1)); _indices[_indexWritePos++] = (uint)(vtx_outer_idx + (i0 << 1));
                    _indices[_indexWritePos++] = (uint)(vtx_outer_idx + (i0 << 1)); _indices[_indexWritePos++] = (uint)(vtx_outer_idx + (i1 << 1)); _indices[_indexWritePos++] = (uint)(vtx_inner_idx + (i1 << 1));
                }
                _currentVertexIndex += (uint)vtx_count;
            }
            else
            {
                int idx_count = (points_count - 2) * 3;
                int vtx_count = points_count;
                PrimReserve(idx_count, vtx_count);
                for (int i = 0; i < vtx_count; i++)
                    _vertices[_vertexWritePos++] = new UIVertex() { Position = new(points[i], _primitiveCount), UV = uv, Color = col };

                for (uint i = 2u; i < points_count; i++)
                {
                    _indices[_indexWritePos++] = _currentVertexIndex; _indices[_indexWritePos++] = _currentVertexIndex + i - 1u; _indices[_indexWritePos++] = _currentVertexIndex + i;
                }
                _currentVertexIndex += (uint)vtx_count;
            }

            _primitiveCount++;
        }


        public void AddBezierCurve(Vector2 pos0, Vector2 cp0, Vector2 cp1, Vector2 pos1, Color32 col, float thickness, int num_segments = 0)
        {
            PathLineTo(pos0);
            PathBezierCurveTo(cp0, cp1, pos1, num_segments);
            PathStroke(col, false, thickness);
        }


        // Stateful path API, add points then finish with PathFill() or PathStroke()
        public void PathLineTo(Vector2 pos)
        {
            _buildingPath.Add(pos);
        }


        public void PathLineToMergeDuplicate(Vector2 pos)
        {
            if (_buildingPath.Count == 0 || MathD.ApproximatelyEquals(_buildingPath[ 1].x, pos.x) || MathD.ApproximatelyEquals(_buildingPath[^1].y, pos.y))
                _buildingPath.Add(pos);
        }


        public void PathFill(Color32 col)
        {
            AddConvexPolyFilled(_buildingPath, _buildingPath.Count, col);
            _buildingPath.Clear();
        }


        public void PathStroke(Color32 col, bool closed, float thickness = 1.0f)
        {
            AddPolyline(_buildingPath, _buildingPath.Count, col, closed, thickness);
            _buildingPath.Clear();
        }


        public void PathArcTo(Vector2 centre, float radius, float amin, float amax, int num_segments = 10)
        {
            if (radius == 0.0f)
                _buildingPath.Add(centre);
            for (int i = 0; i <= num_segments; i++)
            {
                float a = amin + i / (float)num_segments * (amax - amin);
                _buildingPath.Add(new Vector2(centre.x + MathD.Cos(a) * radius, centre.y + MathD.Sin(a) * radius));
            }
        }


        // Use precomputed angles for a 12 steps circle
        public void PathArcToFast(Vector2 centre, float radius, int amin, int amax)
        {
            Vector2[] circle_vtx = new Vector2[12];
            bool circle_vtx_builds = false;
            int circle_vtx_count = circle_vtx.Length;
            if (!circle_vtx_builds)
            {
                for (int i = 0; i < circle_vtx_count; i++)
                {
                    float a = i / (float)circle_vtx_count * 2 * MathF.PI;
                    circle_vtx[i].x = MathD.Cos(a);
                    circle_vtx[i].y = MathD.Sin(a);
                }
                circle_vtx_builds = true;
            }

            if (amin > amax) return;
            if (radius == 0.0f)
            {
                _buildingPath.Add(centre);
            }
            else
            {
                for (int a = amin; a <= amax; a++)
                {
                    Vector2 c = circle_vtx[a % circle_vtx_count];
                    _buildingPath.Add(new Vector2(centre.x + c.x * radius, centre.y + c.y * radius));
                }
            }
        }


        public void PathBezierCurveTo(Vector2 p2, Vector2 p3, Vector2 p4, int num_segments = 0)
        {
            Vector2 p1 = _buildingPath[_buildingPath.Count - 1];
            if (num_segments == 0)
            {
                // Auto-tessellated
                const float tess_tol = 1.25f;
                PathBezierToCasteljau(_buildingPath, (float)p1.x, (float)p1.y, (float)p2.x, (float)p2.y, (float)p3.x, (float)p3.y, (float)p4.x, (float)p4.y, tess_tol, 0);
            }
            else
            {
                float t_step = 1.0f / num_segments;
                for (int i_step = 1; i_step <= num_segments; i_step++)
                {
                    float t = t_step * i_step;
                    float u = 1.0f - t;
                    float w1 = u * u * u;
                    float w2 = 3 * u * u * t;
                    float w3 = 3 * u * t * t;
                    float w4 = t * t * t;
                    _buildingPath.Add(new Vector2(w1 * p1.x + w2 * p2.x + w3 * p3.x + w4 * p4.x, w1 * p1.y + w2 * p2.y + w3 * p3.y + w4 * p4.y));
                }
            }
        }


        private static void PathBezierToCasteljau(List<Vector2> path, float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4, float tess_tol, int level)
        {
            float dx = x4 - x1;
            float dy = y4 - y1;
            float d2 = (x2 - x4) * dy - (y2 - y4) * dx;
            float d3 = (x3 - x4) * dy - (y3 - y4) * dx;

            d2 = d2 >= 0 ? d2 : -d2;
            d3 = d3 >= 0 ? d3 : -d3;
            if ((d2 + d3) * (d2 + d3) < tess_tol * (dx * dx + dy * dy))
            {
                path.Add(new Vector2(x4, y4));
            }
            else if (level < 10)
            {
                float x12 = (x1 + x2) * 0.5f, y12 = (y1 + y2) * 0.5f;
                float x23 = (x2 + x3) * 0.5f, y23 = (y2 + y3) * 0.5f;
                float x34 = (x3 + x4) * 0.5f, y34 = (y3 + y4) * 0.5f;
                float x123 = (x12 + x23) * 0.5f, y123 = (y12 + y23) * 0.5f;
                float x234 = (x23 + x34) * 0.5f, y234 = (y23 + y34) * 0.5f;
                float x1234 = (x123 + x234) * 0.5f, y1234 = (y123 + y234) * 0.5f;

                PathBezierToCasteljau(path, x1, y1, x12, y12, x123, y123, x1234, y1234, tess_tol, level + 1);
                PathBezierToCasteljau(path, x1234, y1234, x234, y234, x34, y34, x4, y4, tess_tol, level + 1);
            }
        }


        public void PathRect(Vector2 a, Vector2 b, float rounding = 0.0f, int corners = CornerRounding.None)
        {
            float r = rounding;

            r = (float)MathD.Min(r, MathD.Abs(b.x - a.x) * ((corners & (1 | 2)) == (1 | 2) || (corners & (4 | 8)) == (4 | 8) ? 0.5f : 1.0f) - 1.0f);
            r = (float)MathD.Min(r, MathD.Abs(b.y - a.y) * ((corners & (1 | 8)) == (1 | 8) || (corners & (2 | 4)) == (2 | 4) ? 0.5f : 1.0f) - 1.0f);

            if (r <= 0.0f || corners == 0)
            {
                PathLineTo(a);
                PathLineTo(new Vector2(b.x, a.y));
                PathLineTo(b);
                PathLineTo(new Vector2(a.x, b.y));
            }
            else
            {
                float r0 = (corners & 1) > 0 ? r : 0.0f;
                float r1 = (corners & 2) > 0 ? r : 0.0f;
                float r2 = (corners & 4) > 0 ? r : 0.0f;
                float r3 = (corners & 8) > 0 ? r : 0.0f;
                PathArcToFast(new Vector2(a.x + r0, a.y + r0), r0, 6, 9);
                PathArcToFast(new Vector2(b.x - r1, a.y + r1), r1, 9, 12);
                PathArcToFast(new Vector2(b.x - r2, b.y - r2), r2, 0, 3);
                PathArcToFast(new Vector2(a.x + r3, b.y - r3), r3, 3, 6);
            }
        }


        //// Channels
        //// - Use to simulate layers. By switching channels to can render out-of-order (e.g. submit foreground primitives before background primitives)
        //// - Use to minimize draw calls (e.g. if going back-and-forth between multiple non-overlapping clipping rectangles, prefer to append into separate channels then merge at the end)

        public void ChannelsSplit(int channels_count)
        {
            System.Diagnostics.Debug.Assert(_currentChannel == 0 && _activeChannels == 1);
            int old_channels_count = _channels.Count;
            if (old_channels_count < channels_count)
                _channels.Resize(channels_count);
            _activeChannels = channels_count;

            // _Channels[] (24 bytes each) hold storage that we'll swap with this->_CmdBuffer/_IdxBuffer
            // The content of _Channels[0] at this point doesn't matter. We clear it to make state tidy in a debugger but we don't strictly need to.
            // When we switch to the next channel, we'll copy _CmdBuffer/_IdxBuffer into _Channels[0] and then _Channels[1] into _CmdBuffer/_IdxBuffer
            //memset(&_Channels[0], 0, sizeof(ImDrawChannel));
            for (int i = 1; i < channels_count; i++)
            {
                if (i >= old_channels_count)
                {
                    //IM_PLACEMENT_NEW(&_Channels[i]) ImDrawChannel();
                    _channels[i] = new UIDrawChannel();
                }
                else
                {
                    _channels[i].CommandList.Clear();
                    _channels[i].IndexBuffer.Clear();
                }
                if (_channels[i].CommandList.Count == 0)
                {
                    UIDrawCmd draw_cmd = new UIDrawCmd();
                    draw_cmd.ClipRect = _clipRectStack[_clipRectStack.Count - 1];
                    draw_cmd.Texture = GetCurrentTexture();
                    _channels[i].CommandList.Add(draw_cmd);
                }
            }
        }


        public void ChannelsMerge()
        {
            // Note that we never use or rely on channels.Size because it is merely a buffer that we never shrink back to 0 to keep all sub-buffers ready for use.
            if (_activeChannels <= 1)
                return;

            ChannelsSetCurrent(0);

            UIDrawCmd? curr_cmd = GetCurrentDrawCmd();
            if (curr_cmd.HasValue && curr_cmd.Value.ElemCount == 0)
                _commandList.Pop();

            int new_cmd_buffer_count = 0, new_idx_buffer_count = 0;
            for (int i = 1; i < _activeChannels; i++)
            {
                UIDrawChannel ch = _channels[i];

                if (ch.CommandList.Count > 0 && ch.CommandList[ch.CommandList.Count - 1].ElemCount == 0)
                    ch.CommandList.Pop();
                new_cmd_buffer_count += ch.CommandList.Count;
                new_idx_buffer_count += ch.IndexBuffer.Count;
            }

            _commandList.Resize(_commandList.Count + new_cmd_buffer_count);
            _indices.Resize(_indices.Count + new_idx_buffer_count);

            int cmd_write = _commandList.Count - new_cmd_buffer_count;
            _indexWritePos = _indices.Count - new_idx_buffer_count;
            for (int i = 1; i < _activeChannels; i++)
            {
                int sz;
                UIDrawChannel ch = _channels[i];
                if ((sz = ch.CommandList.Count) > 0)
                {
                    for (int k = cmd_write; k < sz; k++)
                        _commandList[cmd_write + k] = ch.CommandList[k];

                    cmd_write += sz;
                }
                if ((sz = ch.IndexBuffer.Count) > 0)
                {
                    for (int k = cmd_write; k < sz; k++)
                        _indices[_indexWritePos + k] = ch.IndexBuffer[k];

                    _indexWritePos += sz;
                }
            }

            AddDrawCmd();
            _activeChannels = 1;
        }


        public void ChannelsSetCurrent(int idx)
        {
            System.Diagnostics.Debug.Assert(idx < _activeChannels);
            if (_currentChannel == idx)
                return;

            _currentChannel = idx;

            _commandList = _channels[_currentChannel].CommandList;
            _indices = _channels[_currentChannel].IndexBuffer;

            _indexWritePos = _indices.Count;
        }


        public void AddDrawCmd()
        {
            // This is useful if you need to forcefully create a new draw call (to allow for dependent rendering / blending). Otherwise primitives are merged into the same draw-call as much as possible
            UIDrawCmd draw_cmd = new UIDrawCmd();
            draw_cmd.ClipRect = GetCurrentClipRect();
            draw_cmd.Texture = GetCurrentTexture();

            System.Diagnostics.Debug.Assert(draw_cmd.ClipRect.x <= draw_cmd.ClipRect.z && draw_cmd.ClipRect.y <= draw_cmd.ClipRect.w);
            _commandList.Add(draw_cmd);
        }


        public void UpdateClipRect(bool force = false)
        {
            // If current command is used with different settings we need to add a new command
            Vector4 curr_clip_rect = GetCurrentClipRect();
            UIDrawCmd? curr_cmd = GetCurrentDrawCmd();
            if (!curr_cmd.HasValue || curr_cmd.Value.ElemCount != 0 && curr_cmd.Value.ClipRect != curr_clip_rect || force)
            {
                AddDrawCmd();
                return;
            }

            // Try to merge with previous command if it matches, else use current command
            UIDrawCmd? prev_cmd = GetPreviousDrawCmd();
            if (prev_cmd.HasValue && prev_cmd.Value.ClipRect == curr_clip_rect && prev_cmd.Value.Texture == GetCurrentTexture())
                _commandList.Pop();
            else
            {
                UIDrawCmd value = curr_cmd.Value;
                value.ClipRect = curr_clip_rect;
                SetCurrentDrawCmd(value);
            }
        }


        public void PrimReserve(int idx_count, int vtx_count)
        {
            UIDrawCmd draw_cmd = _commandList[_commandList.Count - 1];
            draw_cmd.ElemCount += (uint)idx_count;
            SetCurrentDrawCmd(draw_cmd);

            int vtx_buffer_size = _vertices.Count;
            _vertices.Resize(vtx_buffer_size + vtx_count);
            _vertexWritePos = vtx_buffer_size;

            int idx_buffer_size = _indices.Count;
            _indices.Resize(idx_buffer_size + idx_count);
            _indexWritePos = idx_buffer_size;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddVerts(UIVertex a, UIVertex b, UIVertex c, UIVertex d)
        {
            uint idx = _currentVertexIndex;
            _indices[_indexWritePos + 0] = idx; _indices[_indexWritePos + 1] = idx + 1; _indices[_indexWritePos + 2] = idx + 2;
            _indices[_indexWritePos + 3] = idx; _indices[_indexWritePos + 4] = idx + 2; _indices[_indexWritePos + 5] = idx + 3;

            _vertices[_vertexWritePos + 0] = a;
            _vertices[_vertexWritePos + 1] = b;
            _vertices[_vertexWritePos + 2] = c;
            _vertices[_vertexWritePos + 3] = d;

            _vertexWritePos += 4;
            _currentVertexIndex += 4;
            _indexWritePos += 6;

            _primitiveCount++;
        }


        // Axis aligned rectangle (composed of two triangles)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PrimRect(Vector2 a, Vector2 c, Color32 col)
        {
            var b = new Vector2(c.x, a.y);
            var d = new Vector2(a.x, c.y);

            Vector2 uv = Font.DefaultFont.TexUvWhitePixel;

            AddVerts(
                new UIVertex(new(a, _primitiveCount), uv, col),
                new UIVertex(new(b, _primitiveCount), uv, col),
                new UIVertex(new(c, _primitiveCount), uv, col),
                new UIVertex(new(d, _primitiveCount), uv, col)
            );
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PrimRectUV(Vector2 a, Vector2 c, Vector2 uv_a, Vector2 uv_c, Color32 col)
        {
            var b = new Vector2(c.x, a.y);
            var d = new Vector2(a.x, c.y);
            var uv_b = new Vector2(uv_c.x, uv_a.y);
            var uv_d = new Vector2(uv_a.x, uv_c.y);

            AddVerts(
                new UIVertex(new(a, _primitiveCount), uv_a, col),
                new UIVertex(new(b, _primitiveCount), uv_b, col),
                new UIVertex(new(c, _primitiveCount), uv_c, col),
                new UIVertex(new(d, _primitiveCount), uv_d, col)
            );
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PrimQuadUV(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector2 uv_a, Vector2 uv_b, Vector2 uv_c, Vector2 uv_d, Color32 col)
        {
            AddVerts(
                new UIVertex(new(a, _primitiveCount), uv_a, col),
                new UIVertex(new(b, _primitiveCount), uv_b, col),
                new UIVertex(new(c, _primitiveCount), uv_c, col),
                new UIVertex(new(d, _primitiveCount), uv_d, col)
            );
        }


        public void UpdateTextureID()
        {
            // If current command is used with different settings we need to add a new command
            Texture2D curr_texture_id = GetCurrentTexture();
            UIDrawCmd? curr_cmd = GetCurrentDrawCmd();
            if (!curr_cmd.HasValue || curr_cmd.Value.ElemCount != 0 && curr_cmd.Value.Texture != curr_texture_id)
            {
                AddDrawCmd();
                return;
            }

            // Try to merge with previous command if it matches, else use current command
            UIDrawCmd? prev_cmd = GetPreviousDrawCmd();
            if (prev_cmd.HasValue && prev_cmd.Value.Texture == curr_texture_id && prev_cmd.Value.ClipRect == GetCurrentClipRect())
            {
                _commandList.Pop();
            }
            else
            {
                UIDrawCmd value = curr_cmd.Value;
                value.Texture = curr_texture_id;
                SetCurrentDrawCmd(value);
            }
        }


        public void ShadeVertsLinearColorGradient(int vertStartIdx, int vertEndIdx, Vector2 gradientP0, Vector2 gradientP1, Color32 col0, Color32 col1)
        {
            var p0 = new System.Numerics.Vector3(gradientP0, _primitiveCount);
            var p1 = new System.Numerics.Vector3(gradientP1, _primitiveCount);
            System.Numerics.Vector3 gradientExtent = p1 - p0;
            float gradientInvLength2 = 1.0f / Sqrlen2(gradientExtent);

            int colDeltaR = col1.r - col0.r;
            int colDeltaG = col1.g - col0.g;
            int colDeltaB = col1.b - col0.b;
            int colDeltaA = col1.a - col0.a;

            for (int idx = vertStartIdx; idx < vertEndIdx; idx++)
            {
                UIVertex vert = _vertices[idx];
                float d = Dot2(vert.Position - p0, gradientExtent);
                float t = Math.Clamp(d * gradientInvLength2, 0.0f, 1.0f);

                byte r = (byte)(col0.r + colDeltaR * t);
                byte g = (byte)(col0.g + colDeltaG * t);
                byte b = (byte)(col0.b + colDeltaB * t);
                byte a = (byte)(col0.a + colDeltaA * t);

                vert.Color = new Color32(r, g, b, a);
                _vertices[idx] = vert;
            }
        }


        public void ShadeVertsLinearColorGradientKeepAlpha(int vertStartIdx, int vertEndIdx, Vector2 gradientP0, Vector2 gradientP1, Color32 col0, Color32 col1)
        {
            var p0 = new System.Numerics.Vector3(gradientP0, _primitiveCount);
            var p1 = new System.Numerics.Vector3(gradientP1, _primitiveCount);
            System.Numerics.Vector3 gradientExtent = p1 - p0;
            float gradientInvLength2 = 1.0f / Sqrlen2(gradientExtent);

            int colDeltaR = col1.r - col0.r;
            int colDeltaG = col1.g - col0.g;
            int colDeltaB = col1.b - col0.b;

            for (int idx = vertStartIdx; idx < vertEndIdx; idx++)
            {
                UIVertex vert = _vertices[idx];
                float d = Dot2(vert.Position - p0, gradientExtent);
                float t = Math.Clamp(d * gradientInvLength2, 0.0f, 1.0f);

                byte r = (byte)(col0.r + colDeltaR * t);
                byte g = (byte)(col0.g + colDeltaG * t);
                byte b = (byte)(col0.b + colDeltaB * t);
                byte a = vert.Color.a; // Keep the original alpha value

                vert.Color = new Color32(r, g, b, a);
                _vertices[idx] = vert;
            }
        }


        private static float Sqrlen2(System.Numerics.Vector3 v)
        {
            return v.X * v.X + v.Y * v.Y;
        }


        private static float Dot2(System.Numerics.Vector3 a, System.Numerics.Vector3 b)
        {
            return a.X * b.X + a.Y * b.Y;
        }
    }
}
