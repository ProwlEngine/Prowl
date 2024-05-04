using Prowl.Icons;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Rendering.OpenGL;
using Silk.NET.OpenGL;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace Prowl.Runtime.GUI.Graphics
{

    // This is essentially a port of the ImGui ImDrawList class to C#

    public class UIDrawList
    {
        public class UIDrawChannel
        {
            public UIBuffer<UIDrawCmd> CmdBuffer { get; private set; } = new();
            public UIBuffer<ushort> IdxBuffer { get; private set; } = new();
        };

        // A single vertex (20 bytes by default, override layout with IMGUI_OVERRIDE_DRAWVERT_STRUCT_LAYOUT)
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UIVertex
        {
            public System.Numerics.Vector3 pos;
            public System.Numerics.Vector2 uv;
            public uint col;
        }

        public struct UIDrawCmd
        {
            public uint ElemCount; // Number of indices (multiple of 3) to be rendered as triangles. Vertices are stored in the callee ImDrawList's vtx_buffer[] array, indices in idx_buffer[].
            public Vector4 ClipRect; // Clipping rectangle (x1, y1, x2, y2)
            public object TextureId; // User-provided texture ID. Set by user in ImfontAtlas::SetTexID() for fonts or passed to Image*() functions. Ignore if never using images or multiple fonts atlas.
        }

        internal static Vector4 GNullClipRect = new Vector4(-8192.0f, -8192.0f, +8192.0f, +8192.0f);

        // This is what you have to render
        internal UIBuffer<UIDrawCmd> CmdBuffer; // Commands. Typically 1 command = 1 gpu draw call.

        internal UIBuffer<ushort> IdxBuffer; // Index buffer. Each command consume ImDrawCmd::ElemCount of those
        internal UIBuffer<UIVertex> VtxBuffer; // Vertex buffer.

        internal uint _VtxCurrentIdx; // [Internal] == VtxBuffer.Size
        internal int _VtxWritePtr; // [Internal] point within VtxBuffer.Data after each add command (to avoid using the ImVector<> operators too much)
        internal int _IdxWritePtr; // [Internal] point within IdxBuffer.Data after each add command (to avoid using the ImVector<> operators too much)
        internal UIBuffer<Vector4> _ClipRectStack; // [Internal]
        internal UIBuffer<object> _TextureIdStack; // [Internal]
        internal UIBuffer<Vector2> _Path; // [Internal] current path building
        internal int _ChannelsCurrent; // [Internal] current channel number (0)
        internal int _ChannelsCount; // [Internal] number of active channels (1+)
        internal UIBuffer<UIDrawChannel> _Channels; // [Internal] draw channels for columns API (not resized down so _ChannelsCount may be smaller than _Channels.Size)
        internal int _primitiveCount = -10000;

        public UIDrawList()
        {
            CmdBuffer = new();
            IdxBuffer = new();
            VtxBuffer = new();
            _ClipRectStack = new();
            _TextureIdStack = new();
            _Path = new();
            _Channels = new();
            _primitiveCount = -10000;

            Clear();
        }

        private static float[] u32FloatLookup;
        public static Vector4 ColorConvertU32ToFloat4(uint @in)
        {
            float s = 1.0f / 255.0f;
            return new Vector4(@in & 0xFF, @in >> 8 & 0xFF, @in >> 16 & 0xFF, @in >> 24) * s;
        }

        public static uint ColorConvertFloat4ToU32(Vector4 @in)
        {
            uint @out;
            @out = (uint)(Mathf.Clamp01(@in.x) * 255.0f + 0.5f);
            @out |= (uint)(Mathf.Clamp01(@in.y) * 255.0f + 0.5f) << 8;
            @out |= (uint)(Mathf.Clamp01(@in.z) * 255.0f + 0.5f) << 16;
            @out |= (uint)(Mathf.Clamp01(@in.w) * 255.0f + 0.5f) << 24;
            return @out;
        }

        public UIDrawCmd? GetCurrentDrawCmd()
        {
            return CmdBuffer.Count > 0 ? CmdBuffer[CmdBuffer.Count - 1] : null;
        }

        public void SetCurrentDrawCmd(UIDrawCmd cmd)
        {
            System.Diagnostics.Debug.Assert(CmdBuffer.Count > 0);
            CmdBuffer[CmdBuffer.Count - 1] = cmd;
        }

        public UIDrawCmd? GetPreviousDrawCmd()
        {
            return CmdBuffer.Count > 1 ? CmdBuffer[CmdBuffer.Count - 2] : null;
        }

        ~UIDrawList()
        {
            Clear();
        }

        // Internal helpers
        // NB: all primitives needs to be reserved via PrimReserve() beforehand!
        public void Clear()
        {
            CmdBuffer.resize(0);
            IdxBuffer.resize(0);
            VtxBuffer.resize(0);
            _VtxCurrentIdx = 0;
            _VtxWritePtr = -1;
            _IdxWritePtr = -1;
            _ClipRectStack.resize(0);
            _TextureIdStack.resize(0);
            _Path.resize(0);
            _ChannelsCurrent = 0;
            _ChannelsCount = 1;
            //for (int i = 0; i < _Channels.Count; i++)
            //{
            //    _Channels[i].CmdBuffer.Clear();
            //    _Channels[i].IdxBuffer.Clear();
            //}
            _Channels.Clear();
            _primitiveCount = -10000;

            // Add Initial Draw Command
            AddDrawCmd();
            PushClipRectFullScreen();
        }

        public void PushClipRect(Vector4 clip_rect)  // Scissoring. Note that the values are (x1,y1,x2,y2) and NOT (x1,y1,w,h). This is passed down to your render function but not used for CPU-side clipping. Prefer using higher-level ImGui::PushClipRect() to affect logic (hit-testing and widget culling)
        {
            if(_ClipRectStack.Count > 0)
                clip_rect = IntersectRects(_ClipRectStack.Peek(), clip_rect);
            _ClipRectStack.Add(clip_rect);
            UpdateClipRect();
        }

        private Vector4 IntersectRects(Vector4 rectA, Vector4 rectB)
        {
            double left = Mathf.Max(rectA.x, rectB.x);
            double top = Mathf.Max(rectA.y, rectB.y);
            double right = Mathf.Min(rectA.z, rectB.z);
            double bottom = Mathf.Min(rectA.w, rectB.w);

            if (right < left || bottom < top)
            {
                // No intersection, return an empty rect
                return new Vector4(0, 0, 0, 0);
            }

            return new Vector4(left, top, right, bottom);
        }

        public void PushClipRectFullScreen()
        {
            PushClipRect(GNullClipRect);

            // FIXME-OPT: This would be more correct but we're not supposed to access ImGuiState from here?
            //ImGuiState& g = *GImGui;
            //PushClipRect(GetVisibleRect());
        }

        public void PopClipRect()
        {
            System.Diagnostics.Debug.Assert(_ClipRectStack.Count > 0);
            _ClipRectStack.Pop();
            UpdateClipRect();
        }

        public void PushTextureID(object texture_id)
        {
            _TextureIdStack.Add(texture_id);
            UpdateTextureID();
        }

        public void PopTextureID()
        {
            System.Diagnostics.Debug.Assert(_TextureIdStack.Count > 0);
            _TextureIdStack.Pop();
            UpdateTextureID();
        }

        // Primitives
        public void AddLine(Vector2 a, Vector2 b, uint col, float thickness = 1.0f)
        {
            if (col >> 24 == 0)
                return;
            PathLineTo(a + new Vector2(0.5f, 0.5f));
            PathLineTo(b + new Vector2(0.5f, 0.5f));
            PathStroke(col, false, thickness);
        }

        // a: upper-left, b: lower-right
        public void AddRect(Vector2 a, Vector2 b, uint col, float rounding = 0.0f, int rounding_corners = 0x0F, float thickness = 1.0f)
        {
            if (col >> 24 == 0)
                return;
            PathRect(a + new Vector2(0.5f, 0.5f), b - new Vector2(0.5f, 0.5f), rounding, rounding_corners);
            PathStroke(col, true, thickness);
        }

        // a: upper-left, b: lower-right
        public void AddRectFilled(Vector2 a, Vector2 b, uint col, float rounding = 0.0f, int rounding_corners = 0x0F)
        {
            if (col >> 24 == 0)
                return;
            if (rounding > 0.0f)
            {
                PathRect(a, b, rounding, rounding_corners);
                PathFill(col);
            }
            else
            {
                PrimReserve(6, 4);
                PrimRect(a, b, col);
            }
        }

        public void AddRectFilledMultiColor(Vector2 a, Vector2 c, uint col_upr_left, uint col_upr_right, uint col_bot_right, uint col_bot_left)
        {
            if ((col_upr_left | col_upr_right | col_bot_right | col_bot_left) >> 24 == 0)
                return;

            PrimReserve(6, 4);

            Vector2 uv = DefaultFont.TexUvWhitePixel;
            var b = new Vector2(c.x, a.y);
            var d = new Vector2(a.x, c.y);
            ushort idx = (ushort)_VtxCurrentIdx;
            IdxBuffer[_IdxWritePtr + 0] = idx; IdxBuffer[_IdxWritePtr + 1] = (ushort)(idx + 1); IdxBuffer[_IdxWritePtr + 2] = (ushort)(idx + 2);
            IdxBuffer[_IdxWritePtr + 3] = idx; IdxBuffer[_IdxWritePtr + 4] = (ushort)(idx + 2); IdxBuffer[_IdxWritePtr + 5] = (ushort)(idx + 3);
            VtxBuffer[_VtxWritePtr + 0] = new UIVertex() { pos = new(a, _primitiveCount), uv = uv, col = col_upr_left };
            VtxBuffer[_VtxWritePtr + 1] = new UIVertex() { pos = new(b, _primitiveCount), uv = uv, col = col_upr_right };
            VtxBuffer[_VtxWritePtr + 2] = new UIVertex() { pos = new(c, _primitiveCount), uv = uv, col = col_bot_right };
            VtxBuffer[_VtxWritePtr + 3] = new UIVertex() { pos = new(d, _primitiveCount), uv = uv, col = col_bot_left };
            _VtxWritePtr += 4;
            _VtxCurrentIdx += 4;
            _IdxWritePtr += 6;

            _primitiveCount++;
        }

        public void AddTriangle(Vector2 a, Vector2 b, Vector2 c, uint col, float thickness = 1.0f)
        {
            if (col >> 24 == 0)
                return;

            PathLineTo(a);
            PathLineTo(b);
            PathLineTo(c);
            PathStroke(col, true, thickness);
        }

        public void AddTriangleFilled(Vector2 a, Vector2 b, Vector2 c, uint col)
        {
            if (col >> 24 == 0)
                return;

            PathLineTo(a);
            PathLineTo(b);
            PathLineTo(c);
            PathFill(col);
        }

        public void AddCircle(Vector2 centre, float radius, uint col, int num_segments = 12, float thickness = 1.0f)
        {
            if (col >> 24 == 0)
                return;

            float a_max = MathF.PI * 2.0f * (num_segments - 1.0f) / num_segments;
            PathArcTo(centre, radius - 0.5f, 0.0f, a_max, num_segments);
            PathStroke(col, true, thickness);
        }

        public void AddCircleFilled(Vector2 centre, float radius, uint col, int num_segments = 12)
        {
            if (col >> 24 == 0)
                return;

            float a_max = MathF.PI * 2.0f * ((num_segments - 1.0f) / num_segments);
            PathArcTo(centre, radius, 0.0f, a_max, num_segments);
            PathFill(col);
        }

        public void AddText(float font_size, Vector2 pos, uint col, string text, int text_begin = 0, int text_end = -1, float wrap_width = 0.0f)
        {
            AddText(DefaultFont, font_size, pos, col, text, text_begin, text_end, wrap_width);
        }
        
        public void AddText(Font font, float font_size, Vector2 pos, uint col, string text, int text_begin = 0, int text_end = -1, float wrap_width = 0.0f, Vector4? cpu_fine_clip_rect = null)
        {
            ArgumentNullException.ThrowIfNull(font);
            if (font_size <= 0.0f)
                return;

            if (col >> 24 == 0)
                return;

            if (text_end == -1)
                text_end = text.Length;
            if (text_begin == text_end)
                return;


            System.Diagnostics.Debug.Assert(font.Texture.Handle == _TextureIdStack[_TextureIdStack.Count - 1]);  // Use high-level ImGui::PushFont() or low-level ImDrawList::PushTextureId() to change font.

            // reserve vertices for worse case (over-reserving is useful and easily amortized)
            int char_count = text_end - text_begin;
            int vtx_count_max = char_count * 4;
            int idx_count_max = char_count * 6;
            int vtx_begin = VtxBuffer.Count;
            int idx_begin = IdxBuffer.Count;
            PrimReserve(idx_count_max, vtx_count_max);

            Vector4 clip_rect = _ClipRectStack[_ClipRectStack.Count - 1];
            if (cpu_fine_clip_rect.HasValue)
            {
                var cfcr = cpu_fine_clip_rect.Value;
                clip_rect.x = Mathf.Max(clip_rect.x, cfcr.x);
                clip_rect.y = Mathf.Max(clip_rect.y, cfcr.y);
                clip_rect.z = Mathf.Min(clip_rect.z, cfcr.z);
                clip_rect.w = Mathf.Min(clip_rect.w, cfcr.w);
            }
            var rect = font.RenderText(font_size, pos, col, clip_rect, text, text_begin, text_end, this, wrap_width, cpu_fine_clip_rect.HasValue);

            // give back unused vertices
            // FIXME-OPT: clean this up
            VtxBuffer.resize(_VtxWritePtr);
            IdxBuffer.resize(_IdxWritePtr);
            int vtx_unused = vtx_count_max - (VtxBuffer.Count - vtx_begin);
            int idx_unused = idx_count_max - (IdxBuffer.Count - idx_begin);
            var curr_cmd = CmdBuffer[CmdBuffer.Count - 1];
            curr_cmd.ElemCount -= (uint)idx_unused;
            CmdBuffer[CmdBuffer.Count - 1] = curr_cmd;

            //_VtxWritePtr -= vtx_unused; //this doesn't seem right, vtx/idx are already pointing to the unused spot
            //_IdxWritePtr -= idx_unused;
            _VtxCurrentIdx = (uint)VtxBuffer.Count;

            //AddRect(rect.Min, rect.Max, 0xff0000ff);
        }

        public void AddImage(object user_texture_id, Vector2 a, Vector2 b, Vector2? _uv0 = null, Vector2? _uv1 = null, uint? _col = null)
        {
            var uv0 = _uv0.HasValue ? _uv0.Value : new Vector2(0, 0);
            var uv1 = _uv1.HasValue ? _uv1.Value : new Vector2(1, 1);
            var col = _col.HasValue ? _col.Value : 0xFFFFFFFFu;

            if (col >> 24 == 0)
                return;

            // FIXME-OPT: This is wasting draw calls.
            bool push_texture_id = _TextureIdStack.Count == 0 || user_texture_id != _TextureIdStack[_TextureIdStack.Count - 1];
            if (push_texture_id)
                PushTextureID(user_texture_id);

            PrimReserve(6, 4);
            PrimRectUV(a, b, uv0, uv1, col);
            //PrimRect(a, b, col);
            if (push_texture_id)
                PopTextureID();
        }

        public void AddPolyline(UIBuffer<Vector2> points, int points_count, uint col, bool closed, float thickness, bool anti_aliased)
        {
            if (points_count < 2)
                return;

            //Vector2 uv = ImGui.Instance.FontTexUvWhitePixel;
            Vector2 uv = DefaultFont.TexUvWhitePixel;

            int count = points_count;
            if (!closed)
                count = points_count - 1;

            bool thick_line = thickness > 1.0f;
            if (anti_aliased)
            {
                // Anti-aliased stroke
                float AA_SIZE = 1.0f;
                uint col_trans = col & 0x00ffffff;

                int idx_count = thick_line ? count * 18 : count * 12;
                int vtx_count = thick_line ? points_count * 4 : points_count * 3;
                PrimReserve(idx_count, vtx_count);

                // Temporary buffer
                var temp_normals = new Vector2[points_count * (thick_line ? 5 : 3)];
                var temp_points = new Vector2[points_count * (thick_line ? 5 : 3)];

                for (int i1 = 0; i1 < count; i1++)
                {
                    int i2 = i1 + 1 == points_count ? 0 : i1 + 1;
                    Vector2 diff = points[i2] - points[i1];

                    double d = diff.x * diff.x + diff.y * diff.y;
                    if (d > 0.0f)
                        diff *= 1.0f / (float)Math.Sqrt(d);

                    temp_normals[i1].x = diff.y;
                    temp_normals[i1].y = -diff.x;
                }
                if (!closed)
                    temp_normals[points_count - 1] = temp_normals[points_count - 2];

                if (!thick_line)
                {
                    if (!closed)
                    {
                        temp_points[0] = points[0] + temp_normals[0] * AA_SIZE;
                        temp_points[1] = points[0] - temp_normals[0] * AA_SIZE;
                        temp_points[(points_count - 1) * 2 + 0] = points[points_count - 1] + temp_normals[points_count - 1] * AA_SIZE;
                        temp_points[(points_count - 1) * 2 + 1] = points[points_count - 1] - temp_normals[points_count - 1] * AA_SIZE;
                    }

                    // FIXME-OPT: Merge the different loops, possibly remove the temporary buffer.
                    uint idx1 = _VtxCurrentIdx;
                    for (int i1 = 0; i1 < count; i1++)
                    {
                        int i2 = i1 + 1 == points_count ? 0 : i1 + 1;
                        uint idx2 = i1 + 1 == points_count ? _VtxCurrentIdx : idx1 + 3;

                        // Average normals
                        Vector2 dm = (temp_normals[i1] + temp_normals[i2]) * 0.5f;
                        double dmr2 = dm.x * dm.x + dm.y * dm.y;
                        if (dmr2 > 0.000001f)
                        {
                            double scale = 1.0f / dmr2;
                            if (scale > 100.0f) scale = 100.0f;
                            dm *= scale;
                        }
                        dm *= AA_SIZE;
                        temp_points[i2 * 2 + 0] = points[i2] + dm;
                        temp_points[i2 * 2 + 1] = points[i2] - dm;

                        // Add indexes

                        IdxBuffer[_IdxWritePtr++] = (ushort)(idx2 + 0); IdxBuffer[_IdxWritePtr++] = (ushort)(idx1 + 0); IdxBuffer[_IdxWritePtr++] = (ushort)(idx1 + 2);
                        IdxBuffer[_IdxWritePtr++] = (ushort)(idx1 + 2); IdxBuffer[_IdxWritePtr++] = (ushort)(idx2 + 2); IdxBuffer[_IdxWritePtr++] = (ushort)(idx2 + 0);
                        IdxBuffer[_IdxWritePtr++] = (ushort)(idx2 + 1); IdxBuffer[_IdxWritePtr++] = (ushort)(idx1 + 1); IdxBuffer[_IdxWritePtr++] = (ushort)(idx1 + 0);
                        IdxBuffer[_IdxWritePtr++] = (ushort)(idx1 + 0); IdxBuffer[_IdxWritePtr++] = (ushort)(idx2 + 0); IdxBuffer[_IdxWritePtr++] = (ushort)(idx2 + 1);
                        //_IdxWritePtr += 12;

                        idx1 = idx2;
                    }

                    // Add vertexes
                    for (int i = 0; i < points_count; i++)
                    {
                        VtxBuffer[_VtxWritePtr++] = new UIVertex() { pos = new(points[i], _primitiveCount), uv = uv, col = col };
                        VtxBuffer[_VtxWritePtr++] = new UIVertex() { pos = new(temp_points[i * 2 + 0], _primitiveCount), uv = uv, col = col_trans };
                        VtxBuffer[_VtxWritePtr++] = new UIVertex() { pos = new(temp_points[i * 2 + 1], _primitiveCount), uv = uv, col = col_trans };
                    }
                }
                else
                {
                    float half_inner_thickness = (thickness - AA_SIZE) * 0.5f;
                    if (!closed)
                    {
                        temp_points[0] = points[0] + temp_normals[0] * (half_inner_thickness + AA_SIZE);
                        temp_points[1] = points[0] + temp_normals[0] * half_inner_thickness;
                        temp_points[2] = points[0] - temp_normals[0] * half_inner_thickness;
                        temp_points[3] = points[0] - temp_normals[0] * (half_inner_thickness + AA_SIZE);
                        temp_points[(points_count - 1) * 4 + 0] = points[points_count - 1] + temp_normals[points_count - 1] * (half_inner_thickness + AA_SIZE);
                        temp_points[(points_count - 1) * 4 + 1] = points[points_count - 1] + temp_normals[points_count - 1] * half_inner_thickness;
                        temp_points[(points_count - 1) * 4 + 2] = points[points_count - 1] - temp_normals[points_count - 1] * half_inner_thickness;
                        temp_points[(points_count - 1) * 4 + 3] = points[points_count - 1] - temp_normals[points_count - 1] * (half_inner_thickness + AA_SIZE);
                    }

                    // FIXME-OPT: Merge the different loops, possibly remove the temporary buffer.
                    uint idx1 = _VtxCurrentIdx;
                    for (int i1 = 0; i1 < count; i1++)
                    {
                        int i2 = i1 + 1 == points_count ? 0 : i1 + 1;
                        uint idx2 = i1 + 1 == points_count ? _VtxCurrentIdx : idx1 + 4;

                        // Average normals
                        Vector2 dm = (temp_normals[i1] + temp_normals[i2]) * 0.5f;
                        double dmr2 = dm.x * dm.x + dm.y * dm.y;
                        if (dmr2 > 0.000001f)
                        {
                            double scale = 1.0f / dmr2;
                            if (scale > 100.0f) scale = 100.0f;
                            dm *= scale;
                        }
                        Vector2 dm_out = dm * (half_inner_thickness + AA_SIZE);
                        Vector2 dm_in = dm * half_inner_thickness;
                        temp_points[i2 * 4 + 0] = points[i2] + dm_out;
                        temp_points[i2 * 4 + 1] = points[i2] + dm_in;
                        temp_points[i2 * 4 + 2] = points[i2] - dm_in;
                        temp_points[i2 * 4 + 3] = points[i2] - dm_out;

                        // Add indexes
                        IdxBuffer[_IdxWritePtr++] = (ushort)(idx2 + 1); IdxBuffer[_IdxWritePtr++] = (ushort)(idx1 + 1); IdxBuffer[_IdxWritePtr++] = (ushort)(idx1 + 2);
                        IdxBuffer[_IdxWritePtr++] = (ushort)(idx1 + 2); IdxBuffer[_IdxWritePtr++] = (ushort)(idx2 + 2); IdxBuffer[_IdxWritePtr++] = (ushort)(idx2 + 1);
                        IdxBuffer[_IdxWritePtr++] = (ushort)(idx2 + 1); IdxBuffer[_IdxWritePtr++] = (ushort)(idx1 + 1); IdxBuffer[_IdxWritePtr++] = (ushort)(idx1 + 0);
                        IdxBuffer[_IdxWritePtr++] = (ushort)(idx1 + 0); IdxBuffer[_IdxWritePtr++] = (ushort)(idx2 + 0); IdxBuffer[_IdxWritePtr++] = (ushort)(idx2 + 1);
                        IdxBuffer[_IdxWritePtr++] = (ushort)(idx2 + 2); IdxBuffer[_IdxWritePtr++] = (ushort)(idx1 + 2); IdxBuffer[_IdxWritePtr++] = (ushort)(idx1 + 3);
                        IdxBuffer[_IdxWritePtr++] = (ushort)(idx1 + 3); IdxBuffer[_IdxWritePtr++] = (ushort)(idx2 + 3); IdxBuffer[_IdxWritePtr++] = (ushort)(idx2 + 2);
                        //_IdxWritePtr += 18;

                        idx1 = idx2;
                    }

                    // Add vertexes
                    for (int i = 0; i < points_count; i++)
                    {
                        VtxBuffer[_VtxWritePtr++] = new UIVertex() { pos = new(temp_points[i * 4 + 0], _primitiveCount), uv = uv, col = col_trans };
                        VtxBuffer[_VtxWritePtr++] = new UIVertex() { pos = new(temp_points[i * 4 + 1], _primitiveCount), uv = uv, col = col };
                        VtxBuffer[_VtxWritePtr++] = new UIVertex() { pos = new(temp_points[i * 4 + 2], _primitiveCount), uv = uv, col = col };
                        VtxBuffer[_VtxWritePtr++] = new UIVertex() { pos = new(temp_points[i * 4 + 3], _primitiveCount), uv = uv, col = col_trans };
                        //_VtxWritePtr += 4;
                    }
                }
                _VtxCurrentIdx += (ushort)vtx_count;
            }
            else
            {
                int idx_count = count * 6;
                int vtx_count = count * 4;      // FIXME-OPT: Not sharing edges
                PrimReserve(idx_count, vtx_count);

                for (int i1 = 0; i1 < count; i1++)
                {
                    int i2 = i1 + 1 == points_count ? 0 : i1 + 1;
                    Vector2 p1 = points[i1];
                    Vector2 p2 = points[i2];
                    Vector2 diff = p2 - p1;

                    double d = diff.x * diff.x + diff.y * diff.y;
                    if (d > 0.0f)
                        diff *= 1.0f / (float)Math.Sqrt(d);

                    double dx = diff.x * (thickness * 0.5f);
                    double dy = diff.y * (thickness * 0.5f);
                    VtxBuffer[_VtxWritePtr++] = new UIVertex() { pos = new Vector3(p1.x + dy, p1.y - dx, _primitiveCount), uv = uv, col = col };
                    VtxBuffer[_VtxWritePtr++] = new UIVertex() { pos = new Vector3(p2.x + dy, p2.y - dx, _primitiveCount), uv = uv, col = col };
                    VtxBuffer[_VtxWritePtr++] = new UIVertex() { pos = new Vector3(p2.x - dy, p2.y + dx, _primitiveCount), uv = uv, col = col };
                    VtxBuffer[_VtxWritePtr++] = new UIVertex() { pos = new Vector3(p1.x - dy, p1.y + dx, _primitiveCount), uv = uv, col = col };
                    //_VtxWritePtr += 4;

                    IdxBuffer[_IdxWritePtr++] = (ushort)_VtxCurrentIdx; IdxBuffer[_IdxWritePtr++] = (ushort)(_VtxCurrentIdx + 1); IdxBuffer[_IdxWritePtr++] = (ushort)(_VtxCurrentIdx + 2);
                    IdxBuffer[_IdxWritePtr++] = (ushort)_VtxCurrentIdx; IdxBuffer[_IdxWritePtr++] = (ushort)(_VtxCurrentIdx + 2); IdxBuffer[_IdxWritePtr++] = (ushort)(_VtxCurrentIdx + 3);
                    //_IdxWritePtr += 6;
                    _VtxCurrentIdx += 4;
                }
            }
            _primitiveCount++;
        }

        public void AddConvexPolyFilled(UIBuffer<Vector2> points, int points_count, uint col, bool anti_aliased)
        {
            //Vector2 uv = ImGui.Instance.FontTexUvWhitePixel;
            Vector2 uv = DefaultFont.TexUvWhitePixel;

            if (anti_aliased)
            {
                // Anti-aliased Fill
                float AA_SIZE = 1.0f;
                uint col_trans = col & 0x00ffffff;
                int idx_count = (points_count - 2) * 3 + points_count * 6;
                int vtx_count = points_count * 2;
                PrimReserve(idx_count, vtx_count);

                // Add indexes for fill
                uint vtx_inner_idx = _VtxCurrentIdx;
                uint vtx_outer_idx = _VtxCurrentIdx + 1;
                for (int i = 2; i < points_count; i++)
                {
                    IdxBuffer[_IdxWritePtr++] = (ushort)vtx_inner_idx;
                    IdxBuffer[_IdxWritePtr++] = (ushort)(vtx_inner_idx + (i - 1 << 1));
                    IdxBuffer[_IdxWritePtr++] = (ushort)(vtx_inner_idx + (i << 1));
                }

                // Compute normals
                Vector2[] temp_normals = new Vector2[points_count];
                //ImVec2* temp_normals = (ImVec2*)alloca(points_count * sizeof(ImVec2));

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
                    VtxBuffer[_VtxWritePtr++] = new UIVertex() { pos = new(points[i1] - dm, _primitiveCount), uv = uv, col = col };
                    VtxBuffer[_VtxWritePtr++] = new UIVertex() { pos = new(points[i1] + dm, _primitiveCount), uv = uv, col = col_trans };

                    // Add indexes for fringes

                    IdxBuffer[_IdxWritePtr++] = (ushort)(vtx_inner_idx + (i1 << 1)); IdxBuffer[_IdxWritePtr++] = (ushort)(vtx_inner_idx + (i0 << 1)); IdxBuffer[_IdxWritePtr++] = (ushort)(vtx_outer_idx + (i0 << 1));
                    IdxBuffer[_IdxWritePtr++] = (ushort)(vtx_outer_idx + (i0 << 1)); IdxBuffer[_IdxWritePtr++] = (ushort)(vtx_outer_idx + (i1 << 1)); IdxBuffer[_IdxWritePtr++] = (ushort)(vtx_inner_idx + (i1 << 1));
                }
                _VtxCurrentIdx += (ushort)vtx_count;
            }
            else
            {
                int idx_count = (points_count - 2) * 3;
                int vtx_count = points_count;
                PrimReserve(idx_count, vtx_count);
                for (int i = 0; i < vtx_count; i++)
                    VtxBuffer[_VtxWritePtr++] = new UIVertex() { pos = new(points[i], _primitiveCount), uv = uv, col = col };

                for (uint i = 2u; i < points_count; i++)
                {
                    IdxBuffer[_IdxWritePtr++] = (ushort)_VtxCurrentIdx; IdxBuffer[_IdxWritePtr++] = (ushort)(_VtxCurrentIdx + i - 1u); IdxBuffer[_IdxWritePtr++] = (ushort)(_VtxCurrentIdx + i);
                }
                _VtxCurrentIdx += (ushort)vtx_count;
            }

            _primitiveCount++;
        }

        public void AddBezierCurve(Vector2 pos0, Vector2 cp0, Vector2 cp1, Vector2 pos1, uint col, float thickness, int num_segments = 0)
        {
            if (col >> 24 == 0)
                return;

            PathLineTo(pos0);
            PathBezierCurveTo(cp0, cp1, pos1, num_segments);
            PathStroke(col, false, thickness);
        }

        // Stateful path API, add points then finish with PathFill() or PathStroke()
        public void PathLineTo(Vector2 pos) => _Path.Add(pos);

        public void PathLineToMergeDuplicate(Vector2 pos)
        {
            if (_Path.Count == 0 || _Path[_Path.Count - 1].x != pos.x || _Path[_Path.Count - 1].y != pos.y)
                _Path.Add(pos);
        }

        public void PathFill(uint col)
        {
            AddConvexPolyFilled(_Path, _Path.Count, col, true);
            _Path.resize(0);
        }

        public void PathStroke(uint col, bool closed, float thickness = 1.0f)
        {
            AddPolyline(_Path, _Path.Count, col, closed, thickness, true);
            _Path.resize(0);
        }

        public void PathArcTo(Vector2 centre, float radius, float amin, float amax, int num_segments = 10)
        {
            if (radius == 0.0f)
                _Path.Add(centre);
            _Path.reserve(_Path.Count + num_segments + 1);
            for (int i = 0; i <= num_segments; i++)
            {
                float a = amin + i / (float)num_segments * (amax - amin);
                _Path.Add(new Vector2(centre.x + Mathf.Cos(a) * radius, centre.y + Mathf.Sin(a) * radius));
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
                    circle_vtx[i].x = Mathf.Cos(a);
                    circle_vtx[i].y = Mathf.Sin(a);
                }
                circle_vtx_builds = true;
            }

            if (amin > amax) return;
            if (radius == 0.0f)
            {
                _Path.Add(centre);
            }
            else
            {
                _Path.reserve(_Path.Count + amax - amin + 1);
                for (int a = amin; a <= amax; a++)
                {
                    Vector2 c = circle_vtx[a % circle_vtx_count];
                    _Path.Add(new Vector2(centre.x + c.x * radius, centre.y + c.y * radius));
                }
            }
        }

        public void PathBezierCurveTo(Vector2 p2, Vector2 p3, Vector2 p4, int num_segments = 0)
        {
            Vector2 p1 = _Path[_Path.Count - 1];
            if (num_segments == 0)
            {
                // Auto-tessellated
                const float tess_tol = 1.25f;
                PathBezierToCasteljau(_Path, (float)p1.x, (float)p1.y, (float)p2.x, (float)p2.y, (float)p3.x, (float)p3.y, (float)p4.x, (float)p4.y, tess_tol, 0);
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
                    _Path.Add(new Vector2(w1 * p1.x + w2 * p2.x + w3 * p3.x + w4 * p4.x, w1 * p1.y + w2 * p2.y + w3 * p3.y + w4 * p4.y));
                }
            }
        }

        void PathBezierToCasteljau(UIBuffer<Vector2> path, float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4, float tess_tol, int level)
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

        public void PathRect(Vector2 a, Vector2 b, float rounding = 0.0f, int rounding_corners = 0x0F)
        {
            float r = rounding;
            r = (float)Mathf.Min(r, Mathf.Abs(b.x - a.x) * ((rounding_corners & (1 | 2)) == (1 | 2) || (rounding_corners & (4 | 8)) == (4 | 8) ? 0.5f : 1.0f) - 1.0f);
            r = (float)Mathf.Min(r, Mathf.Abs(b.y - a.y) * ((rounding_corners & (1 | 8)) == (1 | 8) || (rounding_corners & (2 | 4)) == (2 | 4) ? 0.5f : 1.0f) - 1.0f);

            if (r <= 0.0f || rounding_corners == 0)
            {
                PathLineTo(a);
                PathLineTo(new Vector2(b.x, a.y));
                PathLineTo(b);
                PathLineTo(new Vector2(a.x, b.y));
            }
            else
            {
                float r0 = (rounding_corners & 1) > 0 ? r : 0.0f;
                float r1 = (rounding_corners & 2) > 0 ? r : 0.0f;
                float r2 = (rounding_corners & 4) > 0 ? r : 0.0f;
                float r3 = (rounding_corners & 8) > 0 ? r : 0.0f;
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
            System.Diagnostics.Debug.Assert(_ChannelsCurrent == 0 && _ChannelsCount == 1);
            int old_channels_count = _Channels.Count;
            if (old_channels_count < channels_count)
                _Channels.resize(channels_count);
            _ChannelsCount = channels_count;

            // _Channels[] (24 bytes each) hold storage that we'll swap with this->_CmdBuffer/_IdxBuffer
            // The content of _Channels[0] at this point doesn't matter. We clear it to make state tidy in a debugger but we don't strictly need to.
            // When we switch to the next channel, we'll copy _CmdBuffer/_IdxBuffer into _Channels[0] and then _Channels[1] into _CmdBuffer/_IdxBuffer
            //memset(&_Channels[0], 0, sizeof(ImDrawChannel));
            for (int i = 1; i < channels_count; i++)
            {
                if (i >= old_channels_count)
                {
                    //IM_PLACEMENT_NEW(&_Channels[i]) ImDrawChannel();
                    _Channels[i] = new UIDrawChannel();
                }
                else
                {
                    _Channels[i].CmdBuffer.resize(0);
                    _Channels[i].IdxBuffer.resize(0);
                }
                if (_Channels[i].CmdBuffer.Count == 0)
                {
                    UIDrawCmd draw_cmd = new UIDrawCmd();
                    draw_cmd.ClipRect = _ClipRectStack[_ClipRectStack.Count - 1];
                    draw_cmd.TextureId = _TextureIdStack[_TextureIdStack.Count - 1];
                    _Channels[i].CmdBuffer.Add(draw_cmd);
                }
            }
        }

        public void ChannelsMerge()
        {
            // Note that we never use or rely on channels.Size because it is merely a buffer that we never shrink back to 0 to keep all sub-buffers ready for use.
            if (_ChannelsCount <= 1)
                return;

            ChannelsSetCurrent(0);

            var curr_cmd = GetCurrentDrawCmd();
            if (curr_cmd.HasValue && curr_cmd.Value.ElemCount == 0)
                CmdBuffer.Pop();

            int new_cmd_buffer_count = 0, new_idx_buffer_count = 0;
            for (int i = 1; i < _ChannelsCount; i++)
            {
                UIDrawChannel ch = _Channels[i];

                if (ch.CmdBuffer.Count > 0 && ch.CmdBuffer[ch.CmdBuffer.Count - 1].ElemCount == 0)
                    ch.CmdBuffer.Pop();
                new_cmd_buffer_count += ch.CmdBuffer.Count;
                new_idx_buffer_count += ch.IdxBuffer.Count;
            }
            CmdBuffer.resize(CmdBuffer.Count + new_cmd_buffer_count);
            IdxBuffer.resize(IdxBuffer.Count + new_idx_buffer_count);

            int cmd_write = CmdBuffer.Count - new_cmd_buffer_count;
            _IdxWritePtr = IdxBuffer.Count - new_idx_buffer_count;
            for (int i = 1; i < _ChannelsCount; i++)
            {
                int sz;
                UIDrawChannel ch = _Channels[i];
                if ((sz = ch.CmdBuffer.Count) > 0)
                {
                    for (var k = cmd_write; k < sz; k++)
                        CmdBuffer[cmd_write + k] = ch.CmdBuffer[k];
                    //memcpy(cmd_write, ch.CmdBuffer.Data, sz * sizeof(ImDrawCmd));
                    cmd_write += sz;
                }
                if ((sz = ch.IdxBuffer.Count) > 0)
                {
                    for (var k = cmd_write; k < sz; k++)
                        IdxBuffer[_IdxWritePtr + k] = ch.IdxBuffer[k];
                    //memcpy(_IdxWritePtr, ch.IdxBuffer.Data, sz * sizeof(ushort));
                    _IdxWritePtr += sz;
                }
            }

            AddDrawCmd();
            _ChannelsCount = 1;
        }

        public void ChannelsSetCurrent(int idx)
        {
            System.Diagnostics.Debug.Assert(idx < _ChannelsCount);
            if (_ChannelsCurrent == idx)
                return;

            _ChannelsCurrent = idx;

            CmdBuffer = _Channels[_ChannelsCurrent].CmdBuffer;
            IdxBuffer = _Channels[_ChannelsCurrent].IdxBuffer;

            _IdxWritePtr = IdxBuffer.Count;
        }

        //// Advanced
        public Vector4 GetCurrentClipRect()
        {
            return _ClipRectStack.Count > 0 ? _ClipRectStack[_ClipRectStack.Count - 1] : GNullClipRect;
        }

        public object GetCurrentTextureId()
        {
            return _TextureIdStack.Count > 0 ? _TextureIdStack[_TextureIdStack.Count - 1] : null;
        }

        public void AddDrawCmd()
        {
            // This is useful if you need to forcefully create a new draw call (to allow for dependent rendering / blending). Otherwise primitives are merged into the same draw-call as much as possible
            UIDrawCmd draw_cmd = new UIDrawCmd();
            draw_cmd.ClipRect = GetCurrentClipRect();
            draw_cmd.TextureId = GetCurrentTextureId();

            System.Diagnostics.Debug.Assert(draw_cmd.ClipRect.x <= draw_cmd.ClipRect.z && draw_cmd.ClipRect.y <= draw_cmd.ClipRect.w);
            CmdBuffer.Add(draw_cmd);
        }

        public void UpdateClipRect()
        {
            // If current command is used with different settings we need to add a new command
            Vector4 curr_clip_rect = GetCurrentClipRect();
            UIDrawCmd? curr_cmd = GetCurrentDrawCmd();
            if (!curr_cmd.HasValue || curr_cmd.Value.ElemCount != 0 && curr_cmd.Value.ClipRect != curr_clip_rect)
            {
                AddDrawCmd();
                return;
            }

            // Try to merge with previous command if it matches, else use current command
            UIDrawCmd? prev_cmd = GetPreviousDrawCmd();
            if (prev_cmd.HasValue && prev_cmd.Value.ClipRect == curr_clip_rect && prev_cmd.Value.TextureId == GetCurrentTextureId())
                CmdBuffer.Pop();
            else
            {
                var value = curr_cmd.Value;
                value.ClipRect = curr_clip_rect;
                SetCurrentDrawCmd(value);
            }
        }

        public void PrimReserve(int idx_count, int vtx_count)
        {
            UIDrawCmd draw_cmd = CmdBuffer[CmdBuffer.Count - 1];
            draw_cmd.ElemCount += (uint)idx_count;
            SetCurrentDrawCmd(draw_cmd);

            int vtx_buffer_size = VtxBuffer.Count;
            VtxBuffer.resize(vtx_buffer_size + vtx_count);
            _VtxWritePtr = vtx_buffer_size;

            int idx_buffer_size = IdxBuffer.Count;
            IdxBuffer.resize(idx_buffer_size + idx_count);
            _IdxWritePtr = idx_buffer_size;
        }

        // Axis aligned rectangle (composed of two triangles)
        public void PrimRect(Vector2 a, Vector2 c, uint col)
        {
            var b = new Vector2(c.x, a.y);
            var d = new Vector2(a.x, c.y);
            //var uv = new Vector2(-1, -1);
            Vector2 uv = DefaultFont.TexUvWhitePixel;
            //Vector2 b(c.x, a.y), d(a.x, c.y), uv(GImGui->FontTexUvWhitePixel);
            ushort idx = (ushort)_VtxCurrentIdx;
            IdxBuffer[_IdxWritePtr + 0] = idx; IdxBuffer[_IdxWritePtr + 1] = (ushort)(idx + 1); IdxBuffer[_IdxWritePtr + 2] = (ushort)(idx + 2);
            IdxBuffer[_IdxWritePtr + 3] = idx; IdxBuffer[_IdxWritePtr + 4] = (ushort)(idx + 2); IdxBuffer[_IdxWritePtr + 5] = (ushort)(idx + 3);
            VtxBuffer[_VtxWritePtr + 0] = new UIVertex() { pos = new(a, _primitiveCount), uv = uv, col = col };
            VtxBuffer[_VtxWritePtr + 1] = new UIVertex() { pos = new(b, _primitiveCount), uv = uv, col = col };
            VtxBuffer[_VtxWritePtr + 2] = new UIVertex() { pos = new(c, _primitiveCount), uv = uv, col = col };
            VtxBuffer[_VtxWritePtr + 3] = new UIVertex() { pos = new(d, _primitiveCount), uv = uv, col = col };
            _VtxWritePtr += 4;
            _VtxCurrentIdx += 4;
            _IdxWritePtr += 6;

            _primitiveCount++;
        }

        public void PrimRectUV(Vector2 a, Vector2 c, Vector2 uv_a, Vector2 uv_c, uint col)
        {
            var b = new Vector2(c.x, a.y);
            var d = new Vector2(a.x, c.y);
            var uv_b = new Vector2(uv_c.x, uv_a.y);
            var uv_d = new Vector2(uv_a.x, uv_c.y);

            ushort idx = (ushort)_VtxCurrentIdx;
            IdxBuffer[_IdxWritePtr + 0] = idx; IdxBuffer[_IdxWritePtr + 1] = (ushort)(idx + 1); IdxBuffer[_IdxWritePtr + 2] = (ushort)(idx + 2);
            IdxBuffer[_IdxWritePtr + 3] = idx; IdxBuffer[_IdxWritePtr + 4] = (ushort)(idx + 2); IdxBuffer[_IdxWritePtr + 5] = (ushort)(idx + 3);
            VtxBuffer[_VtxWritePtr + 0] = new UIVertex() { pos = new(a, _primitiveCount), uv = uv_a, col = col };
            VtxBuffer[_VtxWritePtr + 1] = new UIVertex() { pos = new(b, _primitiveCount), uv = uv_b, col = col };
            VtxBuffer[_VtxWritePtr + 2] = new UIVertex() { pos = new(c, _primitiveCount), uv = uv_c, col = col };
            VtxBuffer[_VtxWritePtr + 3] = new UIVertex() { pos = new(d, _primitiveCount), uv = uv_d, col = col };

            _VtxWritePtr += 4;
            _VtxCurrentIdx += 4;
            _IdxWritePtr += 6;

            _primitiveCount++;
        }

        public void PrimQuadUV(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector2 uv_a, Vector2 uv_b, Vector2 uv_c, Vector2 uv_d, uint col)
        {
            ushort idx = (ushort)_VtxCurrentIdx;
            IdxBuffer[_IdxWritePtr + 0] = idx; IdxBuffer[_IdxWritePtr + 1] = (ushort)(idx + 1); IdxBuffer[_IdxWritePtr + 2] = (ushort)(idx + 2);
            IdxBuffer[_IdxWritePtr + 3] = idx; IdxBuffer[_IdxWritePtr + 4] = (ushort)(idx + 2); IdxBuffer[_IdxWritePtr + 5] = (ushort)(idx + 3);
            VtxBuffer[_VtxWritePtr + 0] = new UIVertex() { pos = new(a, _primitiveCount), uv = uv_a, col = col };
            VtxBuffer[_VtxWritePtr + 1] = new UIVertex() { pos = new(b, _primitiveCount), uv = uv_b, col = col };
            VtxBuffer[_VtxWritePtr + 2] = new UIVertex() { pos = new(c, _primitiveCount), uv = uv_c, col = col };
            VtxBuffer[_VtxWritePtr + 3] = new UIVertex() { pos = new(d, _primitiveCount), uv = uv_d, col = col };

            _VtxWritePtr += 4;
            _VtxCurrentIdx += 4;
            _IdxWritePtr += 6;

            _primitiveCount++;
        }

        public void UpdateTextureID()
        {
            // If current command is used with different settings we need to add a new command
            object curr_texture_id = GetCurrentTextureId();
            UIDrawCmd? curr_cmd = GetCurrentDrawCmd();
            if (!curr_cmd.HasValue || curr_cmd.Value.ElemCount != 0 && curr_cmd.Value.TextureId != curr_texture_id)
            {
                AddDrawCmd();
                return;
            }

            // Try to merge with previous command if it matches, else use current command
            UIDrawCmd? prev_cmd = GetPreviousDrawCmd();
            if (prev_cmd.HasValue && prev_cmd.Value.TextureId == curr_texture_id && prev_cmd.Value.ClipRect == GetCurrentClipRect())
                CmdBuffer.Pop();
            else
            {
                var value = curr_cmd.Value;
                value.TextureId = curr_texture_id;
                SetCurrentDrawCmd(value);
            }
        }


        public unsafe static void Draw(GL _gl, Vector2 DisplaySize, UIDrawList[] lists)
        {
            int framebufferWidth = (int)DisplaySize.x;
            int framebufferHeight = (int)DisplaySize.y;
            if (framebufferWidth <= 0 || framebufferHeight <= 0)
                return;

            // Backup GL state
            _gl.GetInteger(GLEnum.ActiveTexture, out int lastActiveTexture);
            _gl.ActiveTexture(GLEnum.Texture0);

            _gl.GetInteger(GLEnum.TextureBinding2D, out int lastTexture);

            _gl.GetInteger(GLEnum.SamplerBinding, out int lastSampler);

            _gl.GetInteger(GLEnum.ArrayBufferBinding, out int lastArrayBuffer);
            _gl.GetInteger(GLEnum.VertexArrayBinding, out int lastVertexArrayObject);

#if !GLES
            Span<int> lastPolygonMode = stackalloc int[2];
            _gl.GetInteger(GLEnum.PolygonMode, lastPolygonMode);
#endif

            Span<int> lastScissorBox = stackalloc int[4];
            _gl.GetInteger(GLEnum.ScissorBox, lastScissorBox);

            _gl.GetInteger(GLEnum.BlendSrcRgb, out int lastBlendSrcRgb);
            _gl.GetInteger(GLEnum.BlendDstRgb, out int lastBlendDstRgb);

            _gl.GetInteger(GLEnum.BlendSrcAlpha, out int lastBlendSrcAlpha);
            _gl.GetInteger(GLEnum.BlendDstAlpha, out int lastBlendDstAlpha);

            _gl.GetInteger(GLEnum.BlendEquationRgb, out int lastBlendEquationRgb);
            _gl.GetInteger(GLEnum.BlendEquationAlpha, out int lastBlendEquationAlpha);

            bool lastSrgb = _gl.IsEnabled(GLEnum.FramebufferSrgb);
            bool lastEnableBlend = _gl.IsEnabled(GLEnum.Blend);
            bool lastEnableCullFace = _gl.IsEnabled(GLEnum.CullFace);
            bool lastEnableDepthTest = _gl.IsEnabled(GLEnum.DepthTest);
            bool lastEnableStencilTest = _gl.IsEnabled(GLEnum.StencilTest);
            bool lastEnableScissorTest = _gl.IsEnabled(GLEnum.ScissorTest);

#if !GLES && !LEGACY
            bool lastEnablePrimitiveRestart = _gl.IsEnabled(GLEnum.PrimitiveRestart);
#endif

            SetupRenderState(_gl, DisplaySize, framebufferWidth, framebufferHeight);

            // Render command lists
            for (int n = 0; n < lists.Length; n++)
            {
                var cmdListPtr = lists[n];

                // Upload vertex/index buffers
                var vtxArr = cmdListPtr.VtxBuffer.Data;
                fixed (void* vtxArrPtr = vtxArr)
                    _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(cmdListPtr.VtxBuffer.Count * sizeof(UIVertex)), vtxArrPtr, GLEnum.StreamDraw);
                var idxArr = cmdListPtr.IdxBuffer.Data;
                fixed (void* idxArrPtr = idxArr)
                    _gl.BufferData(GLEnum.ElementArrayBuffer, (nuint)(cmdListPtr.IdxBuffer.Count * sizeof(ushort)), idxArrPtr, GLEnum.StreamDraw);

                var idxoffset = 0;
                for (int cmd_i = 0; cmd_i < cmdListPtr.CmdBuffer.Count; cmd_i++)
                {
                    var cmdPtr = cmdListPtr.CmdBuffer[cmd_i];

                    Vector4 clipRect;
                    clipRect.x = cmdPtr.ClipRect.x;
                    clipRect.y = cmdPtr.ClipRect.y;
                    clipRect.z = cmdPtr.ClipRect.z;
                    clipRect.w = cmdPtr.ClipRect.w;

                    if (clipRect.x < framebufferWidth && clipRect.y < framebufferHeight && clipRect.z >= 0.0f && clipRect.w >= 0.0f)
                    {
                        // Apply scissor/clipping rectangle
                        _gl.Scissor((int)clipRect.x, (int)(framebufferHeight - clipRect.w), (uint)(clipRect.z - clipRect.x), (uint)(clipRect.w - clipRect.y));

                        // Bind texture, Draw
                        GLTexture? tex = cmdPtr.TextureId as GLTexture;
                        _gl.ActiveTexture(TextureUnit.Texture0);
                        _gl.BindTexture(GLEnum.Texture2D, tex?.Handle ?? 0);

                        _gl.DrawElements(GLEnum.Triangles, cmdPtr.ElemCount, GLEnum.UnsignedShort, (void*)(idxoffset * sizeof(ushort)));
                        //_gl.DrawElements(GLEnum.Triangles, cmdPtr.ElemCount, GLEnum.UnsignedShort, (void*)idxoffset);
                    }

                    idxoffset += (int)cmdPtr.ElemCount;
                }
            }

            // Destroy the temporary VAO
            _gl.DeleteVertexArray(_vertexArrayObject);
            _vertexArrayObject = 0;

            // Restore modified GL state
            _gl.BindTexture(GLEnum.Texture2D, (uint)lastTexture);

            _gl.BindSampler(0, (uint)lastSampler);
            _gl.ActiveTexture((GLEnum)lastActiveTexture);
            _gl.BindVertexArray((uint)lastVertexArrayObject);

            _gl.BindBuffer(GLEnum.ArrayBuffer, (uint)lastArrayBuffer);
            _gl.BlendEquationSeparate((GLEnum)lastBlendEquationRgb, (GLEnum)lastBlendEquationAlpha);
            _gl.BlendFuncSeparate((GLEnum)lastBlendSrcRgb, (GLEnum)lastBlendDstRgb, (GLEnum)lastBlendSrcAlpha, (GLEnum)lastBlendDstAlpha);

            //if (lastSrgb)
            //    _gl.Enable(GLEnum.FramebufferSrgb);
            //else
            //    _gl.Disable(GLEnum.FramebufferSrgb);

            if (lastEnableBlend)
            {
                _gl.Enable(GLEnum.Blend);
            }
            else
            {
                _gl.Disable(GLEnum.Blend);
            }

            if (lastEnableCullFace)
            {
                _gl.Enable(GLEnum.CullFace);
            }
            else
            {
                _gl.Disable(GLEnum.CullFace);
            }

            if (lastEnableDepthTest)
            {
                _gl.Enable(GLEnum.DepthTest);
            }
            else
            {
                _gl.Disable(GLEnum.DepthTest);
            }
            if (lastEnableStencilTest)
            {
                _gl.Enable(GLEnum.StencilTest);
            }
            else
            {
                _gl.Disable(GLEnum.StencilTest);
            }

            if (lastEnableScissorTest)
            {
                _gl.Enable(GLEnum.ScissorTest);
            }
            else
            {
                _gl.Disable(GLEnum.ScissorTest);
            }

#if !GLES && !LEGACY
            if (lastEnablePrimitiveRestart)
            {
                _gl.Enable(GLEnum.PrimitiveRestart);
            }
            else
            {
                _gl.Disable(GLEnum.PrimitiveRestart);
            }

            _gl.PolygonMode(GLEnum.FrontAndBack, (GLEnum)lastPolygonMode[0]);
#endif

            _gl.Scissor(lastScissorBox[0], lastScissorBox[1], (uint)lastScissorBox[2], (uint)lastScissorBox[3]);
        }

        private static unsafe void SetupRenderState(GL _gl, Vector2 DisplaySize, int framebufferWidth, int framebufferHeight)
        {
            // Setup render state: alpha-blending enabled, no face culling, no depth testing, scissor enabled, polygon fill
            //_gl.Enable(EnableCap.FramebufferSrgb);
            _gl.Enable(GLEnum.Blend);
            _gl.BlendEquation(GLEnum.FuncAdd);
            _gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha, GLEnum.One, GLEnum.OneMinusSrcAlpha);
            _gl.Disable(GLEnum.CullFace);
            //_gl.Disable(GLEnum.DepthTest);
            _gl.Disable(GLEnum.StencilTest);
            _gl.Enable(GLEnum.ScissorTest);
#if !GLES && !LEGACY
            _gl.Disable(GLEnum.PrimitiveRestart);
            _gl.PolygonMode(GLEnum.FrontAndBack, GLEnum.Fill);
#endif

            float L = 0.0f;
            float R = 0.0f + (float)DisplaySize.x;
            float T = 0.0f;
            float B = 0.0f + (float)DisplaySize.y;

            float near = -100000.0f; // Near clip plane distance
            float far = 100000.0f;   // Far clip plane distance
            //Span<float> orthoProjection = stackalloc float[] {
            //    2.0f / (R - L), 0.0f, 0.0f, 0.0f,
            //    0.0f, 2.0f / (T - B), 0.0f, 0.0f,
            //    0.0f, 0.0f, 2.0f / (far - near), 0.0f,
            //    (R + L) / (L - R), (T + B) / (B - T), (far + near) / (near - far), 1.0f,
            //};
            System.Numerics.Matrix4x4 orthoProjection = System.Numerics.Matrix4x4.CreateOrthographicOffCenter(L, R, B, T, near, far);

            Runtime.Graphics.Device.SetUniformI(_shader, "Texture", 0);
            Runtime.Graphics.Device.SetUniformMatrix(_shader, "ProjMtx", 1, false, in orthoProjection.M11);

            _gl.BindSampler(0, 0);

            // Setup desired GL state
            // Recreate the VAO every time (this is to easily allow multiple GL contexts to be rendered to. VAO are not shared among GL contexts)
            // The renderer would actually work without any VAO bound, but then our VertexAttrib calls would overwrite the default one currently bound.
            _vertexArrayObject = _gl.GenVertexArray();
            _gl.BindVertexArray(_vertexArrayObject);

            // Bind vertex/index buffers and setup attributes for ImDrawVert
            _gl.BindBuffer(GLEnum.ArrayBuffer, _vboHandle);
            _gl.BindBuffer(GLEnum.ElementArrayBuffer, _elementsHandle);
            _gl.EnableVertexAttribArray((uint)_attribLocationVtxPos);
            _gl.EnableVertexAttribArray((uint)_attribLocationVtxUV);
            _gl.EnableVertexAttribArray((uint)_attribLocationVtxColor);
            _gl.VertexAttribPointer((uint)_attribLocationVtxPos, 3, GLEnum.Float, false, (uint)sizeof(UIVertex), (void*)0);
            _gl.VertexAttribPointer((uint)_attribLocationVtxUV, 2, GLEnum.Float, false, (uint)sizeof(UIVertex), (void*)12);
            _gl.VertexAttribPointer((uint)_attribLocationVtxColor, 4, GLEnum.UnsignedByte, true, (uint)sizeof(UIVertex), (void*)20);
        }

        public static void CreateDeviceResources(GL _gl)
        {
            // Backup GL state

            _gl.GetInteger(GLEnum.ArrayBufferBinding, out int lastArrayBuffer);
            _gl.GetInteger(GLEnum.VertexArrayBinding, out int lastVertexArray);

            string vertexSource =
        @"#version 330
        layout (location = 0) in vec3 Position;
        layout (location = 1) in vec2 UV;
        layout (location = 2) in vec4 Color;
        uniform mat4 ProjMtx;
        out vec2 Frag_UV;
        out vec4 Frag_Color;
        void main()
        {
            Frag_UV = UV;
            Frag_Color = Color;
            gl_Position = ProjMtx * vec4(Position,1);
        }";


            string fragmentSource =
        @"#version 330
        in vec2 Frag_UV;
        in vec4 Frag_Color;
        uniform sampler2D Texture;
        layout (location = 0) out vec4 Out_Color;

        void main() {
            vec4 color = texture(Texture, Frag_UV);
        
            // Gamma Correct
            color = pow(color, vec4(1.0 / 1.43));
        
            Out_Color = Frag_Color * color;
        }";

            _shader = Runtime.Graphics.Device.CompileProgram(fragmentSource, vertexSource, "");

            _attribLocationVtxPos = Runtime.Graphics.Device.GetAttribLocation(_shader, "Position");
            _attribLocationVtxUV = Runtime.Graphics.Device.GetAttribLocation(_shader, "UV");
            _attribLocationVtxColor = Runtime.Graphics.Device.GetAttribLocation(_shader, "Color");

            _vboHandle = _gl.GenBuffer();
            _elementsHandle = _gl.GenBuffer();

            RecreateFontDeviceTexture(_gl);

            // Restore modified GL state
            _gl.BindBuffer(GLEnum.ArrayBuffer, (uint)lastArrayBuffer);

            _gl.BindVertexArray((uint)lastVertexArray);
        }

        public static Font DefaultFont { get; private set; }

        /// <summary>
        /// Creates the texture used to render text.
        /// </summary>
        private static unsafe void RecreateFontDeviceTexture(GL _gl)
        {
            if (DefaultFont == null)
            {
                var builder = Font.BuildNewFont(1024, 1024);
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Prowl.Runtime.EmbeddedResources.font.ttf"))
                {
                    using (MemoryStream ms = new())
                    {
                        stream.CopyTo(ms);
                        builder.Add(ms.ToArray(), 20, [Font.CharacterRange.BasicLatin]);
                    }
                }
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Prowl.Runtime.EmbeddedResources.{FontAwesome6.FontIconFileNameFAR}"))
                {
                    using (MemoryStream ms = new())
                    {
                        stream.CopyTo(ms);
                        builder.Add(ms.ToArray(), 20 * 2.0f / 3.0f, [new Font.CharacterRange(FontAwesome6.IconMin, FontAwesome6.IconMax)]);
                    }
                }
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Prowl.Runtime.EmbeddedResources.{FontAwesome6.FontIconFileNameFAS}"))
                {
                    using (MemoryStream ms = new())
                    {
                        stream.CopyTo(ms);
                        builder.Add(ms.ToArray(), 20 * 2.0f / 3.0f, [new Font.CharacterRange(FontAwesome6.IconMin, FontAwesome6.IconMax)]);
                    }
                }
                DefaultFont = builder.End(20);
            }
        }

        private static int _attribLocationVtxPos;
        private static int _attribLocationVtxUV;
        private static int _attribLocationVtxColor;
        private static uint _vboHandle;
        private static uint _elementsHandle;
        private static uint _vertexArrayObject;

        private static GraphicsProgram? _shader = null;

    }
}
