using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Prowl.Graphite.Debugging;

using Prowl.Graphite.Debugger.Trace;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.Graphite.Debugger.Viewer;

internal readonly record struct TargetStep(int EventIndex, int Ordinal, IndirectSlice? Indirect = null);

internal sealed partial class ProfilerView
{
    private static readonly string[] MeshOverlays = { "None", "Normals", "UV layout" };

    private readonly Dictionary<(int, int, int?, IndirectSlice?), ReplayResult> _replays = new();
    private readonly Dictionary<string, (object Texture, int Width, int Height, string Version)> _textures = new();
    private readonly ConditionalWeakTable<byte[], Dictionary<CopyRegion, byte[]>> _previews = new();
    private readonly ConditionalWeakTable<byte[], ConditionalWeakTable<byte[], Dictionary<CopyRegion, byte[]>>> _coverages = new();
    private readonly bool[] _channels = { true, true, true, true };
    private float _exposure;
    private int _mip;
    private (float U, float V)? _pixel;
    private bool _meshShaded = true, _meshWire = true;
    private int _meshOverlay;
    private float _yaw = -35f, _pitch = 22f;

    private ReplayResult Replay(FrameModel model, int instance, int pass, int? eventIndex, IndirectSlice? slice = null)
    {
        var key = (instance, pass, eventIndex, slice);
        if (!_replays.TryGetValue(key, out ReplayResult? result))
        {
            result = _source.Replay(new ReplayRequest { GroupIndex = instance, PassIndex = pass, EventIndex = eventIndex, Indirect = slice });
            _replays[key] = result;
        }
        return result;
    }

    private static ReplayOutput? Output(ReplayResult result, TraceResourceId id)
        => result.Outputs.FirstOrDefault(o => o.Resource == id);

    private void ResolveTexture(FrameModel model, DeepGroup view, DeepPass pass, RecordedResource? res, TargetStep? step, ViewerData data)
    {
        if (res == null)
        {
            data.Origin = "Unknown resource";
            return;
        }

        TraceResourceId id = res.Id;
        DeepIndex index = model.Index!;

        bool Writes(DeepGroup v, DeepPass p) => p.Accesses.Any(a => a.IsOutput && GraphResource(v, a.Resource) is { } g && Backing(index, g)?.Id == res.Id);

        ReplayOutput? output = null;
        if (Writes(view, pass))
        {
            ReplayResult result = Replay(model, view.Index, pass.Index, step?.EventIndex, step?.Indirect);
            output = Output(result, id);
            data.Origin = result.Status == ReplayStatus.NotReplayable ? $"NotReplayable: {result.Reason}" : $"{result.Status} {(step != null ? $"to event {step.Value.EventIndex}" : "whole pass")}";

            if (step is { } s && s.EventIndex >= s.Ordinal && Output(Replay(model, view.Index, pass.Index, s.Ordinal, s.Indirect), id) is { } after
                && Output(s.Indirect is { Count: > 1 } sl ? Replay(model, view.Index, pass.Index, s.Ordinal, sl with { Count = sl.Count - 1 }) : Replay(model, view.Index, pass.Index, s.Ordinal - 1), id) is { } before
                && Region(after, _mip) is { } covered)
                data.Coverage = Coverage(before, after, covered);
        }
        else
        {
            (DeepGroup, DeepPass)? writer = null;
            foreach (DeepGroup v in model.Deep!.Groups.OrderBy(v => v.Index))
                foreach (DeepPass p in v.Passes)
                {
                    bool earlier = v.Index < view.Index || (v.Index == view.Index && p.Index < pass.Index);
                    if (earlier && Writes(v, p))
                        writer = (v, p);
                }

            if (writer is { } w)
            {
                ReplayResult result = Replay(model, w.Item1.Index, w.Item2.Index, null);
                output = Output(result, id);
                data.Origin = $"{result.Status} at end of {w.Item2.Name}";
            }
            else if (index.Contents(res.Id, pass) is { } copy && copy.Copy.Regions.Length > 0)
            {
                output = new ReplayOutput(res.Id, copy.Copy.Regions, ImmutableCollectionsMarshal.AsImmutableArray(copy.Data));
                data.Origin = $"{copy.Copy.Placement} copy v{copy.Copy.Version.Version}";
            }
            else
                data.Origin = "Not captured";
        }

        if (output != null && Region(output, _mip) is { } region)
        {
            data.Raw = ImmutableCollectionsMarshal.AsArray(output.Data.Items);
            data.Region = region;
            data.Bytes = Preview(output, region);
            (data.PreviewWidth, data.PreviewHeight) = Texels.PreviewSize(region, Texels.PreviewStep(region));
        }
    }

    private static CopyRegion? Region(ReplayOutput output, int mip)
    {
        foreach (CopyRegion region in output.Regions)
            if (region.MipLevel == mip && region.ArrayLayer == 0)
                return region;
        return output.Regions.Length > 0 ? output.Regions[0] : null;
    }

    private byte[] Preview(ReplayOutput output, CopyRegion region)
    {
        byte[] raw = ImmutableCollectionsMarshal.AsArray(output.Data.Items) ?? [];
        Dictionary<CopyRegion, byte[]> decoded = _previews.GetOrCreateValue(raw);
        if (!decoded.TryGetValue(region, out byte[]? pixels))
            decoded[region] = pixels = Texels.ToRgba8(raw, region, Texels.PreviewStep(region));
        return pixels;
    }

    private byte[] Coverage(ReplayOutput before, ReplayOutput after, CopyRegion region)
    {
        byte[] from = ImmutableCollectionsMarshal.AsArray(before.Data.Items) ?? [];
        byte[] to = ImmutableCollectionsMarshal.AsArray(after.Data.Items) ?? [];
        Dictionary<CopyRegion, byte[]> masks = _coverages.GetOrCreateValue(to).GetOrCreateValue(from);
        if (!masks.TryGetValue(region, out byte[]? mask))
            masks[region] = mask = CoverageMask(Texels.Changed(from, to, region, Texels.PreviewStep(region)));
        return mask;
    }

    private static byte[] CoverageMask(bool[] changed)
    {
        byte[] mask = new byte[changed.Length * 4];
        for (int p = 0; p < changed.Length; p++)
        {
            if (!changed[p])
                continue;
            int i = p * 4;
            mask[i] = Style.Coverage.R;
            mask[i + 1] = Style.Coverage.G;
            mask[i + 2] = Style.Coverage.B;
            mask[i + 3] = 120;
        }
        return mask;
    }

    private object? UploadTexture(string slot, int width, int height, string version, Func<byte[]> pixels)
    {
        if (_textures.TryGetValue(slot, out var existing) && existing.Width == width && existing.Height == height)
        {
            if (existing.Version != version)
            {
                _paper.Renderer.SetTextureData(existing.Texture, new IntRect(0, 0, width, height), pixels());
                _textures[slot] = (existing.Texture, width, height, version);
            }
            return existing.Texture;
        }

        object texture = _paper.Renderer.CreateTexture((uint)width, (uint)height);
        _paper.Renderer.SetTextureData(texture, new IntRect(0, 0, width, height), pixels());
        _textures[slot] = (texture, width, height, version);
        return texture;
    }

    private byte[] ApplyChannels(byte[] source)
    {
        byte[] result = new byte[source.Length];
        float scale = MathF.Pow(2f, _exposure);
        bool r = _channels[0], g = _channels[1], b = _channels[2], a = _channels[3];
        int colorCount = (r ? 1 : 0) + (g ? 1 : 0) + (b ? 1 : 0);
        for (int i = 0; i + 3 < source.Length; i += 4)
        {
            float sr = source[i], sg = source[i + 1], sb = source[i + 2], sa = source[i + 3];
            float or, og, ob;
            if (colorCount == 0 && a)
                or = og = ob = sa;
            else if (colorCount == 1)
                or = og = ob = (r ? sr : g ? sg : sb) * scale;
            else
            {
                or = r ? sr * scale : 0;
                og = g ? sg * scale : 0;
                ob = b ? sb * scale : 0;
            }
            result[i] = (byte)Math.Clamp(or, 0, 255);
            result[i + 1] = (byte)Math.Clamp(og, 0, 255);
            result[i + 2] = (byte)Math.Clamp(ob, 0, 255);
            result[i + 3] = colorCount > 0 && a ? (byte)sa : (byte)255;
        }
        return result;
    }

    private void TextureView(FrameModel model, ViewerData data, TargetStep? step, float width, bool compact = false)
    {
        var t = data.Resource?.Texture;
        int mipMax = t is { } d ? (int)Math.Max(0, d.MipLevels - 1) : 0;
        _mip = Math.Min(_mip, mipMax);

        if (!compact)
        using (_paper.Row("tools").Width(UnitValue.Stretch()).Height(UnitValue.Auto).WrapContent().Gap(Metrics.SpacingLarge).LineGap(Metrics.Spacing).AlignItems(LayoutAlignment.Center).Enter())
        {
            string[] names = { "R", "G", "B", "A" };
            ButtonGroupBuilder chans = Origami.ButtonGroup(_paper, "chans", -1, c => _channels[c] = !_channels[c]).Small().Selected(c => _channels[c]);
            foreach (string name in new[] { "R", "G", "B", "A" })
                chans.Item(name);
            chans.Show();

            Dim("evl", "Exposure");
            using (_paper.Row("evs").Width(120).Height(Metrics.HeaderHeight).AlignItems(LayoutAlignment.Center).Enter())
                Origami.Slider(_paper, "ev", _exposure, v => _exposure = MathF.Round(v * 2f) / 2f, -4f, 4f).Small().ShowValue(false).Show();
            Mono("evv", (_exposure >= 0 ? "+" : "") + Style.F(_exposure, "0.0") + " EV", Style.InkSoft);

            Dim("mipl", "Mip");
            using (_paper.Row("mips").Width(100).Height(Metrics.HeaderHeight).AlignItems(LayoutAlignment.Center).Enter())
                Origami.IntSlider(_paper, "mip", _mip, v => _mip = v, 0, Math.Max(1, mipMax)).Small().ShowValue(false).Disabled(mipMax == 0).Show();
            string mipSize = t is { } td ? $"{Math.Max(1, td.Width >> _mip)}x{Math.Max(1, td.Height >> _mip)}" : "-";
            Mono("mipv", $"{_mip} / {mipSize}", Style.InkSoft);
        }

        if (data.Bytes == null || data.PreviewWidth == 0)
        {
            using (_paper.Box("none").Height(120).Rounded(4).BackgroundColor(Style.Alpha(Style.Ink, 6)).Enter())
                Dim("t", data.Origin);
            return;
        }

        int pw = data.PreviewWidth, ph = data.PreviewHeight;
        string identity = RuntimeHelpers.GetHashCode(data.Bytes).ToString();
        object? image = UploadTexture("image", pw, ph, $"{identity}|{string.Join("", _channels)}|{_exposure}", () => ApplyChannels(data.Bytes));
        object? coverage = data.Coverage != null
            ? UploadTexture("coverage", pw, ph, RuntimeHelpers.GetHashCode(data.Coverage).ToString(), () => data.Coverage)
            : null;

        float aspect = (float)pw / ph;
        float displayWidth = MathF.Min(width, (compact ? 240f : 560f) * aspect);
        ElementBuilder box = _paper.Box("img").Width(displayWidth).Height(displayWidth / aspect)
            .Rounded(4).BorderColor(Style.Border).BorderWidth(1).Clip()
            .Cursor(PaperCursor.Crosshair)
            .OnClick(e => _pixel = (Math.Clamp((float)e.NormalizedPosition.X, 0f, 1f), Math.Clamp((float)e.NormalizedPosition.Y, 0f, 1f)));

        using (box.Enter())
        {
            _paper.Draw((canvas, r) =>
            {
                float x = (float)r.Min.X, y = (float)r.Min.Y, w = (float)r.Size.X, h = (float)r.Size.Y;
                const float cell = 8f;
                canvas.RectFilled(x, y, w, h, Style.Theme.Neutral.C100);
                for (float cy = 0; cy < h; cy += cell)
                    for (float cx = ((int)(cy / cell) % 2) * cell; cx < w; cx += cell * 2)
                        canvas.RectFilled(x + cx, y + cy, MathF.Min(cell, w - cx), MathF.Min(cell, h - cy), Style.Theme.Neutral.C300);
                if (image != null)
                    canvas.DrawImage(image, x, y, w, h);
            });

            using (_paper.Box("over").PositionType(PositionType.SelfDirected).Position(0, 0).Width(UnitValue.Stretch()).Height(UnitValue.Stretch()).IsNotInteractable().Enter())
            {
                (float U, float V)? pick = _pixel;
                _paper.Draw((canvas, r) =>
                {
                    float x = (float)r.Min.X, y = (float)r.Min.Y, w = (float)r.Size.X, h = (float)r.Size.Y;
                    if (coverage != null)
                        canvas.DrawImage(coverage, x, y, w, h);
                    if (pick is { } p)
                    {
                        float px = x + p.U * w, py = y + p.V * h;
                        void Cross()
                        {
                            canvas.BeginPath();
                            canvas.MoveTo(px - 7f, py); canvas.LineTo(px + 7f, py);
                            canvas.MoveTo(px, py - 7f); canvas.LineTo(px, py + 7f);
                            canvas.Stroke();
                        }
                        canvas.SetStrokeColor(Style.Theme.Popover);
                        canvas.SetStrokeWidth(3f);
                        Cross();
                        canvas.SetStrokeColor(Style.Selection);
                        canvas.SetStrokeWidth(1.5f);
                        Cross();
                    }
                });
            }
        }

        if (!compact)
            Mono("readout", PixelReadout(data), Style.InkSoft);
    }

    private string PixelReadout(ViewerData data)
    {
        if (_pixel is not { } p || data.Bytes == null)
            return "Click the image to read a pixel value.";

        if (data.Raw is not { } raw || data.Region is not { } region)
            return $"Mip {_mip} / -";

        int w = (int)region.Width, h = (int)region.Height;
        int sx = Math.Min(w - 1, (int)(p.U * w)), sy = Math.Min(h - 1, (int)(p.V * h));
        string where = $"Pixel ({sx}, {sy}) / mip {_mip} / ";

        Float4 v = Texels.Read(raw, region, sx, sy);
        int channels = Texels.Channels(region.Format);
        string value = Texels.IsDepth(region.Format)
            ? $"Depth {Style.F(v.X, "0.000000")}"
            : $"{Describe.Format(region.Format)} ({string.Join(", ", new[] { v.X, v.Y, v.Z, v.W }.Take(channels).Select(c => Style.F(c, "0.####")))})";
        return where + value;
    }

    private void MeshView(ViewerData data, float width, bool compact = false)
    {
        if (data.Vertices == null || data.Indices == null)
        {
            Dim("none", "Mesh contents were not captured.");
            return;
        }

        Mesh mesh = Mesh.Decode(data);
        string stats = $"{mesh.VertexCount} verts / {mesh.TriangleCount} tris";

        ElementBuilder box = _paper.Box("mesh").Width(UnitValue.Stretch()).Height(compact ? 240 : 460)
            .Rounded(4).BackgroundColor(Style.Theme.Neutral.C100).BorderColor(Style.Border).BorderWidth(1).Clip()
            .Cursor(PaperCursor.Grab).CursorDragging(PaperCursor.Grabbing)
            .OnDragging(e =>
            {
                _yaw += (float)e.Delta.X * 0.6f;
                while (_yaw > 180f) _yaw -= 360f;
                while (_yaw < -180f) _yaw += 360f;
                _pitch = Math.Clamp(_pitch + (float)e.Delta.Y * 0.5f, -85f, 85f);
            });

        using (box.Enter())
        {
            bool shaded = _meshShaded, wire = _meshWire;
            int overlay = _meshOverlay;
            float yaw = _yaw, pitch = _pitch;
            _paper.Draw((canvas, r) => mesh.Draw(canvas, r, yaw, pitch, shaded, wire, overlay));

            if (!compact)
            using (_paper.Column("tools").PositionType(PositionType.SelfDirected).Position(0, 0).Width(UnitValue.Auto).Height(UnitValue.Auto)
                .Padding(Metrics.SpacingLarge).Gap(Metrics.Spacing).Enter())
            {
                MeshToggle("wire", OrigamiGlyphs.BorderAll_I, _meshWire, _meshWire ? "Wireframe: on" : "Wireframe: off", () => _meshWire = !_meshWire);
                MeshToggle("shade", OrigamiGlyphs.Cube_I, _meshShaded, _meshShaded ? "Shading: flat" : "Shading: clear", () => _meshShaded = !_meshShaded);
                MeshToggle("overlay", OrigamiGlyphs.Compass_I, _meshOverlay != 0, $"Overlay: {MeshOverlays[_meshOverlay]} (click to cycle)", () => _meshOverlay = (_meshOverlay + 1) % MeshOverlays.Length);
                Origami.IconButton(_paper, "reset", Glyph.Rotate, () => { _yaw = -35f; _pitch = 22f; }).Small().NotFocusable().Subtle().Tooltip("Reset view").Show();
            }
        }

        if (!compact)
            Mono("stats", stats, Style.InkDim);
    }

    private void MeshToggle(string id, IOrigamiIcon icon, bool active, string tooltip, Action toggle)
    {
        ButtonBuilder button = Origami.IconButton(_paper, id, icon, toggle).Small().NotFocusable().Tooltip(tooltip);
        if (active)
            button.Primary();
        else
            button.Subtle();
        button.Show();
    }

    private sealed class Mesh
    {
        private Float3[] _positions = Array.Empty<Float3>();
        private Float3[] _normals = Array.Empty<Float3>();
        private Float2[] _uvs = Array.Empty<Float2>();
        private int[] _indices = Array.Empty<int>();
        private Float3 _center;
        private float _radius = 1f;

        public int VertexCount => _positions.Length;
        public int TriangleCount => _indices.Length / 3;

        public static Mesh Decode(ViewerData data)
        {
            byte[] v = data.Vertices!, ib = data.Indices!;
            int count = v.Length / data.Stride;
            Mesh mesh = new()
            {
                _positions = new Float3[count],
                _normals = new Float3[count],
                _uvs = new Float2[count],
            };
            Float3 Read3(int o) => new(BitConverter.ToSingle(v, o), BitConverter.ToSingle(v, o + 4), BitConverter.ToSingle(v, o + 8));
            for (int i = 0; i < count; i++)
            {
                int o = i * data.Stride;
                mesh._positions[i] = Read3(o + data.PositionOffset);
                mesh._normals[i] = Read3(o + data.NormalOffset);
                mesh._uvs[i] = new Float2(BitConverter.ToSingle(v, o + data.UvOffset), BitConverter.ToSingle(v, o + data.UvOffset + 4));
            }

            int size = data.Index32 ? 4 : 2;
            int indexCount = ib.Length / size / 3 * 3;
            mesh._indices = new int[indexCount];
            for (int i = 0; i < indexCount; i++)
            {
                int idx = data.Index32 ? (int)BitConverter.ToUInt32(ib, i * 4) : BitConverter.ToUInt16(ib, i * 2);
                mesh._indices[i] = Math.Min(idx, Math.Max(0, count - 1));
            }

            if (count > 0)
            {
                Float3 min = mesh._positions[0], max = mesh._positions[0];
                foreach (Float3 p in mesh._positions)
                {
                    min = new Float3(MathF.Min((float)min.X, (float)p.X), MathF.Min((float)min.Y, (float)p.Y), MathF.Min((float)min.Z, (float)p.Z));
                    max = new Float3(MathF.Max((float)max.X, (float)p.X), MathF.Max((float)max.Y, (float)p.Y), MathF.Max((float)max.Z, (float)p.Z));
                }
                mesh._center = (min + max) * 0.5f;
                float radius = 0f;
                foreach (Float3 p in mesh._positions)
                    radius = MathF.Max(radius, (float)Float3.Length(p - mesh._center));
                mesh._radius = radius > 0 ? radius : 1f;
            }
            return mesh;
        }

        public void Draw(Canvas canvas, Rect rect, float yawDeg, float pitchDeg, bool shaded, bool wire, int overlay)
        {
            float x0 = (float)rect.Min.X, y0 = (float)rect.Min.Y, w = (float)rect.Size.X, h = (float)rect.Size.Y;
            float cx = x0 + w / 2, cy = y0 + h / 2, unit = MathF.Min(w, h) / 300f;

            canvas.SetStrokeWidth(1f);
            canvas.SetStrokeColor(Style.Theme.BorderSoft);
            canvas.BeginPath();
            canvas.MoveTo(x0, cy); canvas.LineTo(x0 + w, cy);
            canvas.MoveTo(cx, y0); canvas.LineTo(cx, y0 + h);
            canvas.Stroke();

            if (overlay == 2)
            {
                DrawUvs(canvas, cx, cy, 260f * unit);
                return;
            }

            float yaw = yawDeg * MathF.PI / 180f, pitch = pitchDeg * MathF.PI / 180f;
            float cyw = MathF.Cos(yaw), syw = MathF.Sin(yaw), cp = MathF.Cos(pitch), sp = MathF.Sin(pitch);
            Float3 Rotate(Float3 p)
            {
                float x1 = (float)p.X * cyw + (float)p.Z * syw, z1 = -(float)p.X * syw + (float)p.Z * cyw;
                return new Float3(x1, (float)p.Y * cp - z1 * sp, (float)p.Y * sp + z1 * cp);
            }
            Float2 Project(Float3 p)
            {
                float k = 480f * unit / (3.4f - (float)p.Z);
                return new Float2(cx + (float)p.X * k, cy - (float)p.Y * k);
            }

            int n = _positions.Length;
            Float3[] view = new Float3[n];
            Float2[] screen = new Float2[n];
            for (int i = 0; i < n; i++)
            {
                view[i] = Rotate((_positions[i] - _center) / _radius);
                screen[i] = Project(view[i]);
            }

            List<(int Tri, float Depth, bool Front)> tris = new(_indices.Length / 3);
            for (int t = 0; t + 2 < _indices.Length; t += 3)
            {
                Float2 a = screen[_indices[t]], b = screen[_indices[t + 1]], c = screen[_indices[t + 2]];
                float cross = (float)((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X));
                float depth = ((float)view[_indices[t]].Z + (float)view[_indices[t + 1]].Z + (float)view[_indices[t + 2]].Z) / 3f;
                tris.Add((t, depth, cross < 0));
            }
            tris.Sort((p, q) => p.Depth.CompareTo(q.Depth));

            void Tri(int t)
            {
                Float2 a = screen[_indices[t]], b = screen[_indices[t + 1]], c = screen[_indices[t + 2]];
                canvas.MoveTo((float)a.X, (float)a.Y);
                canvas.LineTo((float)b.X, (float)b.Y);
                canvas.LineTo((float)c.X, (float)c.Y);
                canvas.ClosePath();
            }

            if (shaded)
            {
                canvas.SetFillColor(Style.Theme.Primary.C400);
                canvas.BeginPath();
                foreach (var tri in tris)
                    if (tri.Front)
                        Tri(tri.Tri);
                canvas.Fill();
            }

            if (wire)
            {
                canvas.SetStrokeWidth(0.8f);
                canvas.SetStrokeColor(Style.Alpha(!shaded ? Style.Theme.Ink.C300 : Style.Theme.Ink.C400, 200));
                canvas.BeginPath();
                foreach (var tri in tris)
                    if (!shaded || tri.Front)
                        Tri(tri.Tri);
                canvas.Stroke();
            }

            if (overlay == 1)
            {
                canvas.SetStrokeColor(Style.Good);
                canvas.BeginPath();
                for (int i = 0; i < n; i++)
                {
                    Float3 nrm = Rotate(_normals[i]);
                    if ((float)nrm.Z <= 0f)
                        continue;
                    Float2 e = Project(view[i] + nrm * 0.12f);
                    canvas.MoveTo((float)screen[i].X, (float)screen[i].Y);
                    canvas.LineTo((float)e.X, (float)e.Y);
                }
                canvas.Stroke();
            }

            DrawAxis(canvas, x0 + 34 * unit, y0 + h - 34 * unit, 22 * unit, Rotate(new Float3(1, 0, 0)), Style.ChannelR);
            DrawAxis(canvas, x0 + 34 * unit, y0 + h - 34 * unit, 22 * unit, Rotate(new Float3(0, 1, 0)), Style.ChannelG);
            DrawAxis(canvas, x0 + 34 * unit, y0 + h - 34 * unit, 22 * unit, Rotate(new Float3(0, 0, 1)), Style.ChannelB);
        }

        private static void DrawAxis(Canvas canvas, float x, float y, float length, Float3 dir, Color color)
        {
            canvas.SetStrokeWidth(1.6f);
            canvas.SetStrokeColor(color);
            canvas.BeginPath();
            canvas.MoveTo(x, y);
            canvas.LineTo(x + (float)dir.X * length, y - (float)dir.Y * length);
            canvas.Stroke();
        }

        private void DrawUvs(Canvas canvas, float cx, float cy, float size)
        {
            float left = cx - size / 2, top = cy - size / 2;
            canvas.RectFilled(left, top, size, size, Style.Theme.Neutral.C300);
            canvas.SetStrokeWidth(1f);
            canvas.SetStrokeColor(Style.Border);
            canvas.BeginPath();
            canvas.Rect(left, top, size, size);
            canvas.Stroke();

            Float2 P(int i) => new(left + (float)(_uvs[i].X % 1.0001) * size, top + (1f - (float)(_uvs[i].Y % 1.0001)) * size);
            canvas.SetStrokeWidth(0.8f);
            canvas.SetStrokeColor(Style.KindDot(NodeKind.Draw));
            canvas.BeginPath();
            for (int t = 0; t + 2 < _indices.Length; t += 3)
            {
                Float2 a = P(_indices[t]), b = P(_indices[t + 1]), c = P(_indices[t + 2]);
                canvas.MoveTo((float)a.X, (float)a.Y);
                canvas.LineTo((float)b.X, (float)b.Y);
                canvas.LineTo((float)c.X, (float)c.Y);
                canvas.ClosePath();
            }
            canvas.Stroke();
        }
    }
}
