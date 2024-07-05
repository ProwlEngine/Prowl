using Prowl.Icons;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using Veldrid;
using Prowl.Runtime.Utils;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Collections.Immutable;

namespace Prowl.Runtime.GUI.Graphics
{

    // This is essentially a port of the ImGui ImDrawList class to C#

    public class UIDrawList : IGeometryDrawData
    {
        public class UIDrawChannel
        {
            public List<UIDrawCmd> CommandList { get; private set; } = new();
            public List<uint> IndexBuffer { get; private set; } = new();
        };

        // A single vertex (20 bytes by default, override layout with IMGUI_OVERRIDE_DRAWVERT_STRUCT_LAYOUT)
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UIVertex
        {
            public System.Numerics.Vector3 Position;
            public System.Numerics.Vector2 UV;
            public Color32 Color;

            public UIVertex(Vector3 pos, Vector2 uv, Color32 color)
            {
                this.Position = pos;
                this.UV = uv;
                this.Color = color;
            }
        }

        public struct UIDrawCmd
        {
            public uint ElemCount; // Number of indices (multiple of 3) to be rendered as triangles. Vertices are stored in the callee ImDrawList's vtx_buffer[] array, indices in idx_buffer[].
            public Vector4 ClipRect; // Clipping rectangle (x1, y1, x2, y2)
            public Texture2D Texture; // User-provided texture ID. Set by user in ImfontAtlas::SetTexID() for fonts or passed to Image*() functions. Ignore if never using images or multiple fonts atlas.
        }

        internal static Vector4 GNullClipRect = new Vector4(-8192.0f, -8192.0f, +8192.0f, +8192.0f);
        internal static ShaderPass UIPass;
        internal static ShaderVariant UIVariant;

        // This is what you have to render
        internal List<UIDrawCmd> CommandList; // Commands. Typically 1 command = 1 gpu draw call.

        public List<uint> Indices; // Index buffer. Each command consume ImDrawCmd::ElemCount of those
        public List<UIVertex> Vertices; // Vertex buffer.
        public int IndexCount => Indices.Count;
        public IndexFormat IndexFormat => IndexFormat.UInt32;

        internal uint CurrentVertexIndex; // [Internal] == VtxBuffer.Size
        internal int VertexWritePos; // [Internal] point within VtxBuffer.Data after each add command (to avoid using the ImVector<> operators too much)
        internal int IndexWritePos; // [Internal] point within IdxBuffer.Data after each add command (to avoid using the ImVector<> operators too much)
        internal List<Vector4> ClipRectStack; // [Internal]
        internal List<Texture2D> TextureStack; // [Internal]
        internal List<Vector2> BuildingPath; // [Internal] current path building
        internal int CurrentChannel; // [Internal] current channel number (0)
        internal int ActiveChannels; // [Internal] number of active channels (1+)
        internal List<UIDrawChannel> Channels; // [Internal] draw channels for columns API (not resized down so _ChannelsCount may be smaller than _Channels.Size)
        internal int PrimitiveCount = -10000;

        private bool _AntiAliasing;

        public UIDrawList(bool antiAliasing)
        {
            CreateDeviceResources();

            CommandList = new();
            Indices = new();
            Vertices = new();
            ClipRectStack = new();
            TextureStack = new();
            BuildingPath = new();
            Channels = new();
            PrimitiveCount = -10000;
            _AntiAliasing = antiAliasing;

            Clear();
        }

        public void AntiAliasing(bool antiAliasing)
        {
            this._AntiAliasing = antiAliasing;
        }

        public UIDrawCmd? GetCurrentDrawCmd()
        {
            return CommandList.Count > 0 ? CommandList[^1] : null;
        }

        public void SetCurrentDrawCmd(UIDrawCmd cmd)
        {
            System.Diagnostics.Debug.Assert(CommandList.Count > 0);
            CommandList[^1] = cmd;
        }

        public UIDrawCmd? GetPreviousDrawCmd()
        {
            return CommandList.Count > 1 ? CommandList[^2] : null;
        }

        public Vector4 GetCurrentClipRect()
        {
            return ClipRectStack.Count > 0 ? ClipRectStack[^1] : GNullClipRect;
        }

        public Texture2D GetCurrentTexture()
        {
            return TextureStack.Count > 0 ? TextureStack[^1] : Font.DefaultFont.Texture;
        }

        public void Clear()
        {
            CommandList.Clear();
            Indices.Clear();
            Vertices.Clear();

            ClipRectStack.Clear();
            TextureStack.Clear();
            BuildingPath.Clear();

            Channels.Clear();

            CurrentVertexIndex = 0;
            VertexWritePos = -1;
            IndexWritePos = -1;

            CurrentChannel = 0;
            ActiveChannels = 1;

            PrimitiveCount = -10000;

            // Add Initial Draw Command
            AddDrawCmd();
            PushClipRectFullScreen();
        }

        public void PushClipRect(Vector4 clip_rect, bool force = false)  // Scissoring. Note that the values are (x1,y1,x2,y2) and NOT (x1,y1,w,h). This is passed down to your render function but not used for CPU-side clipping. Prefer using higher-level ImGui::PushClipRect() to affect logic (hit-testing and widget culling)
        {
            if(!force && ClipRectStack.Count > 0)
                clip_rect = IntersectRects(ClipRectStack.Peek(), clip_rect);

            ClipRectStack.Add(clip_rect);
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
            PushClipRect(GNullClipRect, true);
        }

        public void PopClipRect()
        {
            System.Diagnostics.Debug.Assert(ClipRectStack.Count > 0);
            ClipRectStack.Pop();
            UpdateClipRect();
        }

        public void PushTexture(Texture2D texture_id)
        {
            TextureStack.Add(texture_id);
            UpdateTextureID();
        }

        public void PopTexture()
        {
            System.Diagnostics.Debug.Assert(TextureStack.Count > 0);
            TextureStack.Pop();
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
        public void AddRect(Vector2 a, Vector2 b, Color32 col, float rounding = 0.0f, int rounding_corners = 0x0F, float thickness = 1.0f)
        {
            PathRect(a + new Vector2(0.5f, 0.5f), b - new Vector2(0.5f, 0.5f), rounding, rounding_corners);
            PathStroke(col, true, thickness);
        }

        // a: upper-left, b: lower-right
        public void AddRectFilled(Vector2 a, Vector2 b, Color32 col, float rounding = 0.0f, int rounding_corners = 0x0F)
        {
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

        public void AddRectFilledMultiColor(Vector2 a, Vector2 c, Color32 col_upr_left, Color32 col_upr_right, Color32 col_bot_right, Color32 col_bot_left)
        {
            PrimReserve(6, 4);

            Vector2 uv = Font.DefaultFont.TexUvWhitePixel;
            var b = new Vector2(c.x, a.y);
            var d = new Vector2(a.x, c.y);
            
            AddVerts(
                new UIVertex(new(a, PrimitiveCount), uv, col_upr_left),
                new UIVertex(new(b, PrimitiveCount), uv, col_upr_right),
                new UIVertex(new(c, PrimitiveCount), uv, col_bot_right),
                new UIVertex(new(d, PrimitiveCount), uv, col_bot_left)
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

            System.Diagnostics.Debug.Assert(font.Texture == GetCurrentTexture());  // Use high-level ImGui::PushFont() or low-level ImDrawList::PushTextureId() to change font.

            // reserve vertices for worse case (over-reserving is useful and easily amortized)
            int char_count = text_end - text_begin;
            int vtx_count_max = char_count * 4;
            int idx_count_max = char_count * 6;
            int vtx_begin = Vertices.Count;
            int idx_begin = Indices.Count;
            PrimReserve(idx_count_max, vtx_count_max);

            Vector4 clip_rect = ClipRectStack[ClipRectStack.Count - 1];
            if (cpu_fine_clip_rect.HasValue)
            {
                var cfcr = cpu_fine_clip_rect.Value;
                clip_rect.x = MathD.Max(clip_rect.x, cfcr.x);
                clip_rect.y = MathD.Max(clip_rect.y, cfcr.y);
                clip_rect.z = MathD.Min(clip_rect.z, cfcr.z);
                clip_rect.w = MathD.Min(clip_rect.w, cfcr.w);
            }
            var rect = font.RenderText(font_size, pos, col, clip_rect, text, text_begin, text_end, this, wrap_width, cpu_fine_clip_rect.HasValue);

            // give back unused vertices
            // FIXME-OPT: clean this up
            Vertices.Resize(VertexWritePos);
            Indices.Resize(IndexWritePos);
            int vtx_unused = vtx_count_max - (Vertices.Count - vtx_begin);
            int idx_unused = idx_count_max - (Indices.Count - idx_begin);
            var curr_cmd = CommandList[CommandList.Count - 1];
            curr_cmd.ElemCount -= (uint)idx_unused;
            CommandList[CommandList.Count - 1] = curr_cmd;

            //_VtxWritePtr -= vtx_unused; //this doesn't seem right, vtx/idx are already pointing to the unused spot
            //_IdxWritePtr -= idx_unused;
            CurrentVertexIndex = (uint)Vertices.Count;

            //AddRect(rect.Min, rect.Max, 0xff0000ff);
        }

        public void AddImage(Texture2D user_texture_id, Vector2 a, Vector2 b, Vector2? _uv0 = null, Vector2? _uv1 = null, Color32? _col = null)
        {
            var uv0 = _uv0 ?? new Vector2(0, 0);
            var uv1 = _uv1 ?? new Vector2(1, 1);
            var col = _col ?? (Color32)Color.white;

            // FIXME-OPT: This is wasting draw calls.
            bool push_texture_id = TextureStack.Count == 0 || user_texture_id != GetCurrentTexture();
            
            if (push_texture_id)
                PushTexture(user_texture_id);

            PrimReserve(6, 4);
            PrimRectUV(a, b, uv0, uv1, col);

            //PrimRect(a, b, col);
            if (push_texture_id)
                PopTexture();
        }

        public void AddPolyline(List<Vector2> points, int points_count, Color32 col, bool closed, float thickness)
        {
            if (points_count < 2)
                return;

            Vector2 uv = Font.DefaultFont.TexUvWhitePixel;

            int count = points_count;
            if (!closed)
                count = points_count - 1;

            bool thick_line = thickness > 1.0f;
            if (_AntiAliasing)
            {
                // Anti-aliased stroke
                float AA_SIZE = 1.0f;
                Color32 col_trans = new Color32(col.r, col.g, col.b, 0);

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
                    uint idx1 = CurrentVertexIndex;
                    for (int i1 = 0; i1 < count; i1++)
                    {
                        int i2 = i1 + 1 == points_count ? 0 : i1 + 1;
                        uint idx2 = i1 + 1 == points_count ? CurrentVertexIndex : idx1 + 3;

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

                        Indices[IndexWritePos++] = (uint)(idx2 + 0); Indices[IndexWritePos++] = (uint)(idx1 + 0); Indices[IndexWritePos++] = (uint)(idx1 + 2);
                        Indices[IndexWritePos++] = (uint)(idx1 + 2); Indices[IndexWritePos++] = (uint)(idx2 + 2); Indices[IndexWritePos++] = (uint)(idx2 + 0);
                        Indices[IndexWritePos++] = (uint)(idx2 + 1); Indices[IndexWritePos++] = (uint)(idx1 + 1); Indices[IndexWritePos++] = (uint)(idx1 + 0);
                        Indices[IndexWritePos++] = (uint)(idx1 + 0); Indices[IndexWritePos++] = (uint)(idx2 + 0); Indices[IndexWritePos++] = (uint)(idx2 + 1);
                        //_IdxWritePtr += 12;

                        idx1 = idx2;
                    }

                    // Add vertexes
                    for (int i = 0; i < points_count; i++)
                    {
                        Vertices[VertexWritePos++] = new UIVertex(new(points[i], PrimitiveCount), uv, col);
                        Vertices[VertexWritePos++] = new UIVertex(new(temp_points[i * 2 + 0], PrimitiveCount), uv, col_trans);
                        Vertices[VertexWritePos++] = new UIVertex(new(temp_points[i * 2 + 1], PrimitiveCount), uv, col_trans);
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
                    uint idx1 = CurrentVertexIndex;
                    for (int i1 = 0; i1 < count; i1++)
                    {
                        int i2 = i1 + 1 == points_count ? 0 : i1 + 1;
                        uint idx2 = i1 + 1 == points_count ? CurrentVertexIndex : idx1 + 4;

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
                        Indices[IndexWritePos++] = (idx2 + 1); Indices[IndexWritePos++] = (idx1 + 1); Indices[IndexWritePos++] = (idx1 + 2);
                        Indices[IndexWritePos++] = (idx1 + 2); Indices[IndexWritePos++] = (idx2 + 2); Indices[IndexWritePos++] = (idx2 + 1);
                        Indices[IndexWritePos++] = (idx2 + 1); Indices[IndexWritePos++] = (idx1 + 1); Indices[IndexWritePos++] = (idx1 + 0);
                        Indices[IndexWritePos++] = (idx1 + 0); Indices[IndexWritePos++] = (idx2 + 0); Indices[IndexWritePos++] = (idx2 + 1);
                        Indices[IndexWritePos++] = (idx2 + 2); Indices[IndexWritePos++] = (idx1 + 2); Indices[IndexWritePos++] = (idx1 + 3);
                        Indices[IndexWritePos++] = (idx1 + 3); Indices[IndexWritePos++] = (idx2 + 3); Indices[IndexWritePos++] = (idx2 + 2);
                        //_IdxWritePtr += 18;

                        idx1 = idx2;
                    }

                    // Add vertexes
                    for (int i = 0; i < points_count; i++)
                    {
                        Vertices[VertexWritePos++] = new UIVertex(new(temp_points[i * 4 + 0], PrimitiveCount), uv, col_trans);
                        Vertices[VertexWritePos++] = new UIVertex(new(temp_points[i * 4 + 1], PrimitiveCount), uv, col);
                        Vertices[VertexWritePos++] = new UIVertex(new(temp_points[i * 4 + 2], PrimitiveCount), uv, col);
                        Vertices[VertexWritePos++] = new UIVertex(new(temp_points[i * 4 + 3], PrimitiveCount), uv, col_trans);
                        //_VtxWritePtr += 4;
                    }
                }
                CurrentVertexIndex += (uint)vtx_count;
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

                    Vertices[VertexWritePos++] = new UIVertex(new Vector3(p1.x + dy, p1.y - dx, PrimitiveCount), uv, col);
                    Vertices[VertexWritePos++] = new UIVertex(new Vector3(p2.x + dy, p2.y - dx, PrimitiveCount), uv, col);
                    Vertices[VertexWritePos++] = new UIVertex(new Vector3(p2.x - dy, p2.y + dx, PrimitiveCount), uv, col);
                    Vertices[VertexWritePos++] = new UIVertex(new Vector3(p1.x - dy, p1.y + dx, PrimitiveCount), uv, col);
                    //_VtxWritePtr += 4;

                    Indices[IndexWritePos++] = (uint)CurrentVertexIndex; Indices[IndexWritePos++] = (uint)(CurrentVertexIndex + 1); Indices[IndexWritePos++] = (uint)(CurrentVertexIndex + 2);
                    Indices[IndexWritePos++] = (uint)CurrentVertexIndex; Indices[IndexWritePos++] = (uint)(CurrentVertexIndex + 2); Indices[IndexWritePos++] = (uint)(CurrentVertexIndex + 3);
                    //_IdxWritePtr += 6;
                    CurrentVertexIndex += 4;
                }
            }
            PrimitiveCount++;
        }

        public void AddConvexPolyFilled(List<Vector2> points, int points_count, Color32 col)
        {

            if (points_count < 3)
                return;

            //Vector2 uv = ImGui.Instance.FontTexUvWhitePixel;
            Vector2 uv = Font.DefaultFont.TexUvWhitePixel;

            if (_AntiAliasing)
            {
                // Anti-aliased Fill
                float AA_SIZE = 1.0f;
                Color32 col_trans = new Color32(col.r, col.g, col.b, 0);
                int idx_count = (points_count - 2) * 3 + points_count * 6;
                int vtx_count = points_count * 2;
                PrimReserve(idx_count, vtx_count);

                // Add indexes for fill
                uint vtx_inner_idx = CurrentVertexIndex;
                uint vtx_outer_idx = CurrentVertexIndex + 1;
                for (int i = 2; i < points_count; i++)
                {
                    Indices[IndexWritePos++] = (uint)vtx_inner_idx;
                    Indices[IndexWritePos++] = (uint)(vtx_inner_idx + (i - 1 << 1));
                    Indices[IndexWritePos++] = (uint)(vtx_inner_idx + (i << 1));
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
                    Vertices[VertexWritePos++] = new UIVertex() { Position = new(points[i1] - dm, PrimitiveCount), UV = uv, Color = col };
                    Vertices[VertexWritePos++] = new UIVertex() { Position = new(points[i1] + dm, PrimitiveCount), UV = uv, Color = col_trans};

                    // Add indexes for fringes

                    Indices[IndexWritePos++] = (uint)(vtx_inner_idx + (i1 << 1)); Indices[IndexWritePos++] = (uint)(vtx_inner_idx + (i0 << 1)); Indices[IndexWritePos++] = (uint)(vtx_outer_idx + (i0 << 1));
                    Indices[IndexWritePos++] = (uint)(vtx_outer_idx + (i0 << 1)); Indices[IndexWritePos++] = (uint)(vtx_outer_idx + (i1 << 1)); Indices[IndexWritePos++] = (uint)(vtx_inner_idx + (i1 << 1));
                }
                CurrentVertexIndex += (uint)vtx_count;
            }
            else
            {
                int idx_count = (points_count - 2) * 3;
                int vtx_count = points_count;
                PrimReserve(idx_count, vtx_count);
                for (int i = 0; i < vtx_count; i++)
                    Vertices[VertexWritePos++] = new UIVertex() { Position = new(points[i], PrimitiveCount), UV = uv, Color = col };

                for (uint i = 2u; i < points_count; i++)
                {
                    Indices[IndexWritePos++] = (uint)CurrentVertexIndex; Indices[IndexWritePos++] = (uint)(CurrentVertexIndex + i - 1u); Indices[IndexWritePos++] = (uint)(CurrentVertexIndex + i);
                }
                CurrentVertexIndex += (uint)vtx_count;
            }

            PrimitiveCount++;
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
            BuildingPath.Add(pos);
        }

        public void PathLineToMergeDuplicate(Vector2 pos)
        {
            if (BuildingPath.Count == 0 || BuildingPath[BuildingPath.Count - 1].x != pos.x || BuildingPath[BuildingPath.Count - 1].y != pos.y)
                BuildingPath.Add(pos);
        }

        public void PathFill(Color32 col)
        {
            AddConvexPolyFilled(BuildingPath, BuildingPath.Count, col);
            BuildingPath.Clear();
        }

        public void PathStroke(Color32 col, bool closed, float thickness = 1.0f)
        {
            AddPolyline(BuildingPath, BuildingPath.Count, col, closed, thickness);
            BuildingPath.Clear();
        }

        public void PathArcTo(Vector2 centre, float radius, float amin, float amax, int num_segments = 10)
        {
            if (radius == 0.0f)
                BuildingPath.Add(centre);
            for (int i = 0; i <= num_segments; i++)
            {
                float a = amin + i / (float)num_segments * (amax - amin);
                BuildingPath.Add(new Vector2(centre.x + MathD.Cos(a) * radius, centre.y + MathD.Sin(a) * radius));
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
                BuildingPath.Add(centre);
            }
            else
            {
                for (int a = amin; a <= amax; a++)
                {
                    Vector2 c = circle_vtx[a % circle_vtx_count];
                    BuildingPath.Add(new Vector2(centre.x + c.x * radius, centre.y + c.y * radius));
                }
            }
        }

        public void PathBezierCurveTo(Vector2 p2, Vector2 p3, Vector2 p4, int num_segments = 0)
        {
            Vector2 p1 = BuildingPath[BuildingPath.Count - 1];
            if (num_segments == 0)
            {
                // Auto-tessellated
                const float tess_tol = 1.25f;
                PathBezierToCasteljau(BuildingPath, (float)p1.x, (float)p1.y, (float)p2.x, (float)p2.y, (float)p3.x, (float)p3.y, (float)p4.x, (float)p4.y, tess_tol, 0);
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
                    BuildingPath.Add(new Vector2(w1 * p1.x + w2 * p2.x + w3 * p3.x + w4 * p4.x, w1 * p1.y + w2 * p2.y + w3 * p3.y + w4 * p4.y));
                }
            }
        }

        void PathBezierToCasteljau(List<Vector2> path, float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4, float tess_tol, int level)
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
            r = (float)MathD.Min(r, MathD.Abs(b.x - a.x) * ((rounding_corners & (1 | 2)) == (1 | 2) || (rounding_corners & (4 | 8)) == (4 | 8) ? 0.5f : 1.0f) - 1.0f);
            r = (float)MathD.Min(r, MathD.Abs(b.y - a.y) * ((rounding_corners & (1 | 8)) == (1 | 8) || (rounding_corners & (2 | 4)) == (2 | 4) ? 0.5f : 1.0f) - 1.0f);

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
            System.Diagnostics.Debug.Assert(CurrentChannel == 0 && ActiveChannels == 1);
            int old_channels_count = Channels.Count;
            if (old_channels_count < channels_count)
                Channels.Resize(channels_count);
            ActiveChannels = channels_count;

            // _Channels[] (24 bytes each) hold storage that we'll swap with this->_CmdBuffer/_IdxBuffer
            // The content of _Channels[0] at this point doesn't matter. We clear it to make state tidy in a debugger but we don't strictly need to.
            // When we switch to the next channel, we'll copy _CmdBuffer/_IdxBuffer into _Channels[0] and then _Channels[1] into _CmdBuffer/_IdxBuffer
            //memset(&_Channels[0], 0, sizeof(ImDrawChannel));
            for (int i = 1; i < channels_count; i++)
            {
                if (i >= old_channels_count)
                {
                    //IM_PLACEMENT_NEW(&_Channels[i]) ImDrawChannel();
                    Channels[i] = new UIDrawChannel();
                }
                else
                {
                    Channels[i].CommandList.Clear();
                    Channels[i].IndexBuffer.Clear();
                }
                if (Channels[i].CommandList.Count == 0)
                {
                    UIDrawCmd draw_cmd = new UIDrawCmd();
                    draw_cmd.ClipRect = ClipRectStack[ClipRectStack.Count - 1];
                    draw_cmd.Texture = GetCurrentTexture();
                    Channels[i].CommandList.Add(draw_cmd);
                }
            }
        }

        public void ChannelsMerge()
        {
            // Note that we never use or rely on channels.Size because it is merely a buffer that we never shrink back to 0 to keep all sub-buffers ready for use.
            if (ActiveChannels <= 1)
                return;

            ChannelsSetCurrent(0);

            var curr_cmd = GetCurrentDrawCmd();
            if (curr_cmd.HasValue && curr_cmd.Value.ElemCount == 0)
                CommandList.Pop();

            int new_cmd_buffer_count = 0, new_idx_buffer_count = 0;
            for (int i = 1; i < ActiveChannels; i++)
            {
                UIDrawChannel ch = Channels[i];

                if (ch.CommandList.Count > 0 && ch.CommandList[ch.CommandList.Count - 1].ElemCount == 0)
                    ch.CommandList.Pop();
                new_cmd_buffer_count += ch.CommandList.Count;
                new_idx_buffer_count += ch.IndexBuffer.Count;
            }

            CommandList.Resize(CommandList.Count + new_cmd_buffer_count);
            Indices.Resize(Indices.Count + new_idx_buffer_count);

            int cmd_write = CommandList.Count - new_cmd_buffer_count;
            IndexWritePos = Indices.Count - new_idx_buffer_count;
            for (int i = 1; i < ActiveChannels; i++)
            {
                int sz;
                UIDrawChannel ch = Channels[i];
                if ((sz = ch.CommandList.Count) > 0)
                {
                    for (var k = cmd_write; k < sz; k++)
                        CommandList[cmd_write + k] = ch.CommandList[k];

                    cmd_write += sz;
                }
                if ((sz = ch.IndexBuffer.Count) > 0)
                {
                    for (var k = cmd_write; k < sz; k++)
                        Indices[IndexWritePos + k] = ch.IndexBuffer[k];

                    IndexWritePos += sz;
                }
            }

            AddDrawCmd();
            ActiveChannels = 1;
        }

        public void ChannelsSetCurrent(int idx)
        {
            System.Diagnostics.Debug.Assert(idx < ActiveChannels);
            if (CurrentChannel == idx)
                return;

            CurrentChannel = idx;

            CommandList = Channels[CurrentChannel].CommandList;
            Indices = Channels[CurrentChannel].IndexBuffer;

            IndexWritePos = Indices.Count;
        }

        public void AddDrawCmd()
        {
            // This is useful if you need to forcefully create a new draw call (to allow for dependent rendering / blending). Otherwise primitives are merged into the same draw-call as much as possible
            UIDrawCmd draw_cmd = new UIDrawCmd();
            draw_cmd.ClipRect = GetCurrentClipRect();
            draw_cmd.Texture = GetCurrentTexture();

            System.Diagnostics.Debug.Assert(draw_cmd.ClipRect.x <= draw_cmd.ClipRect.z && draw_cmd.ClipRect.y <= draw_cmd.ClipRect.w);
            CommandList.Add(draw_cmd);
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
                CommandList.Pop();
            else
            {
                var value = curr_cmd.Value;
                value.ClipRect = curr_clip_rect;
                SetCurrentDrawCmd(value);
            }
        }

        public void PrimReserve(int idx_count, int vtx_count)
        {
            UIDrawCmd draw_cmd = CommandList[CommandList.Count - 1];
            draw_cmd.ElemCount += (uint)idx_count;
            SetCurrentDrawCmd(draw_cmd);

            int vtx_buffer_size = Vertices.Count;
            Vertices.Resize(vtx_buffer_size + vtx_count);
            VertexWritePos = vtx_buffer_size;

            int idx_buffer_size = Indices.Count;
            Indices.Resize(idx_buffer_size + idx_count);
            IndexWritePos = idx_buffer_size;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddVerts(UIVertex a, UIVertex b, UIVertex c, UIVertex d)
        {
            uint idx = (uint)CurrentVertexIndex;
            Indices[IndexWritePos + 0] = idx; Indices[IndexWritePos + 1] = (uint)(idx + 1); Indices[IndexWritePos + 2] = (uint)(idx + 2);
            Indices[IndexWritePos + 3] = idx; Indices[IndexWritePos + 4] = (uint)(idx + 2); Indices[IndexWritePos + 5] = (uint)(idx + 3);

            Vertices[VertexWritePos + 0] = a;
            Vertices[VertexWritePos + 1] = b;
            Vertices[VertexWritePos + 2] = c;
            Vertices[VertexWritePos + 3] = d;

            VertexWritePos += 4;
            CurrentVertexIndex += 4;
            IndexWritePos += 6;

            PrimitiveCount++;
        }

        // Axis aligned rectangle (composed of two triangles)
        public void PrimRect(Vector2 a, Vector2 c, Color32 col)
        {
            var b = new Vector2(c.x, a.y);
            var d = new Vector2(a.x, c.y);

            Vector2 uv = Font.DefaultFont.TexUvWhitePixel;

            AddVerts(
                new UIVertex(new(a, PrimitiveCount), uv, col), 
                new UIVertex(new(b, PrimitiveCount), uv, col), 
                new UIVertex(new(c, PrimitiveCount), uv, col), 
                new UIVertex(new(d, PrimitiveCount), uv, col)
            );
        }

        public void PrimRectUV(Vector2 a, Vector2 c, Vector2 uv_a, Vector2 uv_c, Color32 col)
        {
            var b = new Vector2(c.x, a.y);
            var d = new Vector2(a.x, c.y);
            var uv_b = new Vector2(uv_c.x, uv_a.y);
            var uv_d = new Vector2(uv_a.x, uv_c.y);

            AddVerts(
                new UIVertex(new(a, PrimitiveCount), uv_a, col),
                new UIVertex(new(b, PrimitiveCount), uv_b, col),
                new UIVertex(new(c, PrimitiveCount), uv_c, col),
                new UIVertex(new(d, PrimitiveCount), uv_d, col)
            );
        }

        public void PrimQuadUV(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector2 uv_a, Vector2 uv_b, Vector2 uv_c, Vector2 uv_d, Color32 col)
        {
            AddVerts(
                new UIVertex(new(a, PrimitiveCount), uv_a, col),
                new UIVertex(new(b, PrimitiveCount), uv_b, col),
                new UIVertex(new(c, PrimitiveCount), uv_c, col),
                new UIVertex(new(d, PrimitiveCount), uv_d, col)
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
                CommandList.Pop();
            }
            else
            {
                var value = curr_cmd.Value;
                value.Texture = curr_texture_id;
                SetCurrentDrawCmd(value);
            }
        }

        public void ShadeVertsLinearColorGradient(int vertStartIdx, int vertEndIdx, Vector2 gradientP0, Vector2 gradientP1, Color32 col0, Color32 col1)
        {
            var p0 = new System.Numerics.Vector3(gradientP0, PrimitiveCount);
            var p1 = new System.Numerics.Vector3(gradientP1, PrimitiveCount);
            var gradientExtent = p1 - p0;
            float gradientInvLength2 = 1.0f / ImLengthSqr(gradientExtent);

            int colDeltaR = col1.r - col0.r;
            int colDeltaG = col1.g - col0.g;
            int colDeltaB = col1.b - col0.b;
            int colDeltaA = col1.a - col0.a;

            for (int idx = vertStartIdx; idx < vertEndIdx; idx++)
            {
                var vert = Vertices[idx];
                float d = ImDot(vert.Position - p0, gradientExtent);
                float t = ImClamp(d * gradientInvLength2, 0.0f, 1.0f);

                byte r = (byte)(col0.r + colDeltaR * t);
                byte g = (byte)(col0.g + colDeltaG * t);
                byte b = (byte)(col0.b + colDeltaB * t);
                byte a = (byte)(col0.a + colDeltaA * t);

                vert.Color = new Color32(r, g, b, a);
                Vertices[idx] = vert;
            }
        }

        public void ShadeVertsLinearColorGradientKeepAlpha(int vertStartIdx, int vertEndIdx, Vector2 gradientP0, Vector2 gradientP1, Color32 col0, Color32 col1)
        {
            var p0 = new System.Numerics.Vector3(gradientP0, PrimitiveCount);
            var p1 = new System.Numerics.Vector3(gradientP1, PrimitiveCount);
            var gradientExtent = p1 - p0;
            float gradientInvLength2 = 1.0f / ImLengthSqr(gradientExtent);

            int colDeltaR = col1.r - col0.r;
            int colDeltaG = col1.g - col0.g;
            int colDeltaB = col1.b - col0.b;

            for (int idx = vertStartIdx; idx < vertEndIdx; idx++)
            {
                var vert = Vertices[idx];
                float d = ImDot(vert.Position - p0, gradientExtent);
                float t = ImClamp(d * gradientInvLength2, 0.0f, 1.0f);

                byte r = (byte)(col0.r + colDeltaR * t);
                byte g = (byte)(col0.g + colDeltaG * t);
                byte b = (byte)(col0.b + colDeltaB * t);
                byte a = vert.Color.a; // Keep the original alpha value

                vert.Color = new Color32(r, g, b, a);
                Vertices[idx] = vert;
            }
        }

        private float ImLengthSqr(System.Numerics.Vector3 v)
        {
            return v.X * v.X + v.Y * v.Y;
        }

        private float ImDot(System.Numerics.Vector3 a, System.Numerics.Vector3 b)
        {
            return a.X * b.X + a.Y * b.Y;
        }

        private float ImClamp(float value, float min, float max)
        {
            return value < min ? min : value > max ? max : value;
        }
  
        private static DeviceBuffer IndexBuffer;
        private static DeviceBuffer VertexBuffer;  

        private static DeviceBuffer EnsureBuffer(DeviceBuffer buffer, uint size, uint scale, BufferUsage usage)
        {
            size *= scale;

            if (buffer == null || size > buffer.SizeInBytes)
            {
                buffer?.Dispose();
                buffer = Runtime.Graphics.Factory.CreateBuffer(new BufferDescription(Math.Max(size, 10000), usage | BufferUsage.DynamicWrite));
            }

            return buffer;
        }

        public static void DisposeBuffers()
        {
            IndexBuffer?.Dispose();
            VertexBuffer?.Dispose();
        } 

        const uint uintSize = sizeof(uint);
        const uint vertSize = (sizeof(float) * 5) + 4;

        public void SetDrawData(CommandList commandList, VertexLayoutDescription[] vertexLayout)
        {
            Span<UIVertex> verticesSpan = CollectionsMarshal.AsSpan(Vertices);
            Span<uint> indicesSpan = CollectionsMarshal.AsSpan(Indices);

            commandList.UpdateBuffer(VertexBuffer, 0, verticesSpan);
            commandList.UpdateBuffer(IndexBuffer, 0, indicesSpan);

            commandList.SetIndexBuffer(IndexBuffer, IndexFormat.UInt32);
            commandList.SetVertexBuffer(0, VertexBuffer);
        }

        public static void Draw(CommandBuffer commandBuffer, Vector2 DisplaySize, UIDrawList[] lists)
        {
            int framebufferWidth = (int)DisplaySize.x;
            int framebufferHeight = (int)DisplaySize.y;
            if (framebufferWidth <= 0 || framebufferHeight <= 0)
                return;

            SetupRenderState(commandBuffer, DisplaySize);

            uint maxVertexCount = 0;
            uint maxIndexCount = 0;
            for (int i = 0; i < lists.Length; i++)
            {
                var cmdListPtr = lists[i];

                maxVertexCount = Math.Max(maxVertexCount, (uint)cmdListPtr.Vertices.Count);
                maxIndexCount = Math.Max(maxIndexCount, (uint)cmdListPtr.Indices.Count);
            }

            VertexBuffer = EnsureBuffer(VertexBuffer, maxVertexCount, vertSize, BufferUsage.VertexBuffer);
            VertexBuffer.Name = $"Draw List Vertex Buffer";
            
            IndexBuffer = EnsureBuffer(IndexBuffer, maxIndexCount, uintSize, BufferUsage.IndexBuffer);
            IndexBuffer.Name = $"Draw List Index Buffer";

            for (int n = 0; n < lists.Length; n++)
            {
                var cmdListPtr = lists[n];

                if (cmdListPtr.Vertices.Count == 0)
                    continue;

                commandBuffer.SetDrawData(cmdListPtr, null);

                var idxoffset = 0;
                for (int cmd_i = 0; cmd_i < cmdListPtr.CommandList.Count; cmd_i++)
                {
                    var cmdPtr = cmdListPtr.CommandList[cmd_i];

                    Vector4 clipRect = cmdPtr.ClipRect;

                    if (clipRect.x < framebufferWidth && clipRect.y < framebufferHeight && clipRect.z >= 0.0f && clipRect.w >= 0.0f)
                    {
                        // Apply scissor/clipping rectangle
                        commandBuffer.SetScissorRects((int)clipRect.x, (int)clipRect.y, (int)(clipRect.z - clipRect.x), (int)(clipRect.w - clipRect.y));
                        commandBuffer.SetTexture("MainTexture", cmdPtr.Texture);

                        commandBuffer.UploadResourceSet(1);
                        commandBuffer.ManualDraw((uint)cmdPtr.ElemCount, (uint)idxoffset, 1, 0, 0);
                    }

                    idxoffset += (int)cmdPtr.ElemCount;
                }

                // Clear Depth Buffer
                commandBuffer.ClearRenderTarget(true, false, Color.white, depth: 1.0f);
            }
        }

        private static void SetupRenderState(CommandBuffer commandBuffer, Vector2 DisplaySize)
        {
            float L = 0.0f;
            float R = 0.0f + (float)DisplaySize.x;
            float T = 0.0f;
            float B = 0.0f + (float)DisplaySize.y;

            float near = -100000.0f; // Near clip plane distance
            float far = 100000.0f;   // Far clip plane distance

            Matrix4x4 orthoProjection = Matrix4x4.CreateOrthographicOffCenter(L, R, B, T, near, far);

            commandBuffer.SetFullScissorRect(0);
            commandBuffer.SetPipeline(UIPass, UIVariant);

            commandBuffer.SetMatrix("ProjectionMatrix", orthoProjection);
            commandBuffer.SetTexture("MainSampler", Font.DefaultFont.Texture);

            commandBuffer.UploadResourceSet(0);
        }

        public static void CreateDeviceResources()
        {
            if (UIPass != null && UIVariant != null)
                return;

            ShaderPassDescription description = new()
            {
                DepthStencilState = new DepthStencilStateDescription(false, false, ComparisonKind.Always),
                CullingMode = FaceCullMode.None,
                BlendState = BlendStateDescription.SingleAlphaBlend,
            };

            ShaderSource[] sources = 
            [
                new()
                {
                    Stage = ShaderStages.Vertex,
                    SourceCode = "gui-vertex"
                },

                new()
                {
                    Stage = ShaderStages.Fragment,
                    SourceCode = "gui-frag"
                }
            ];

            var compiler = new EmbeddedVariantCompiler() {
                Inputs = [ 
                    new VertexLayoutDescription(
                        new VertexElementDescription("Position", VertexElementFormat.Float3, VertexElementSemantic.Position),
                        new VertexElementDescription("UV", VertexElementFormat.Float2, VertexElementSemantic.TextureCoordinate),
                        new VertexElementDescription("Color", VertexElementFormat.Byte4_Norm, VertexElementSemantic.Color)
                    ) 
                ],

                Resources = [
                    [
                        new BufferResource("ProjectionMatrixBuffer", ShaderStages.Vertex, ("ProjectionMatrix", ResourceType.Matrix4x4)),
                        new SamplerResource("MainSampler", ShaderStages.Fragment),
                    ],

                    [
                        new TextureResource("MainTexture", false, ShaderStages.Fragment)
                    ]
                ]
            };

            ShaderPass pass = new ShaderPass("UI Pass", sources, description, compiler);

            UIPass = pass;
            UIVariant = pass.GetVariant(KeywordState.Default);
        }

        private class EmbeddedVariantCompiler : IVariantCompiler
        {
            public VertexLayoutDescription[] Inputs;
            public ShaderResource[][] Resources;

            public ShaderVariant CompileVariant(ShaderSource[] sources, KeywordState keywords)
            {
                byte[] vertexShaderBytes = LoadEmbeddedShaderCode(sources[0].SourceCode);
                byte[] fragmentShaderBytes = LoadEmbeddedShaderCode(sources[1].SourceCode);

                var vertex = new ShaderDescription(
                    ShaderStages.Vertex, 
                    vertexShaderBytes, 
                    Runtime.Graphics.Device.BackendType == GraphicsBackend.Vulkan ? "main" : "VS"
                );

                var fragment = new ShaderDescription(
                    ShaderStages.Fragment, 
                    fragmentShaderBytes, 
                    Runtime.Graphics.Device.BackendType == GraphicsBackend.Vulkan ? "main" : "FS"
                );

                return new(keywords, [ (Runtime.Graphics.Device.BackendType, [ vertex, fragment ]) ], Inputs, Resources);
            }
        }

        private static byte[] LoadEmbeddedShaderCode(string name)
        {
            switch (Runtime.Graphics.Factory.BackendType)
            {
                case GraphicsBackend.Direct3D11:
                {
                    string resourceName = name + ".hlsl.bytes";
                    return GetEmbeddedResourceBytes(resourceName);
                }
                case GraphicsBackend.OpenGL:
                {
                    string resourceName = name + ".glsl";
                    return GetEmbeddedResourceBytes(resourceName);
                }
                case GraphicsBackend.OpenGLES:
                {
                    string resourceName = name + ".glsles";
                    return GetEmbeddedResourceBytes(resourceName);
                }
                case GraphicsBackend.Vulkan:
                {
                    string resourceName = name + ".spv";
                    return GetEmbeddedResourceBytes(resourceName);
                }
                case GraphicsBackend.Metal:
                {
                    string resourceName = name + ".metallib";
                    return GetEmbeddedResourceBytes(resourceName);
                }
                default:
                    throw new NotImplementedException();
            }
        }

        private static byte[] GetEmbeddedResourceBytes(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (Stream s = assembly.GetManifestResourceStream(resourceName))
            {
                byte[] ret = new byte[s.Length];
                s.Read(ret, 0, (int)s.Length);
                return ret;
            }
        }
    }
}