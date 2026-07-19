using System;

using Prowl.Editor.Theming;
using Prowl.Graphite;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Scribe;
using Prowl.Vector;

using Color = System.Drawing.Color;
using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.Editor.GUI.RenderProfiler;

/// <summary>
/// Shared texture inspector used by the snapshot Passes tab. Given a <see cref="SnapshotTexture"/> it
/// decodes the read-back pixel bytes into a display <see cref="Texture2D"/> (R8G8B8A8) on the CPU,
/// applying the per-channel R/G/B/A toggles and, for depth textures, an optional depth linearization.
/// The decoded texture is cached and only rebuilt when the source or a control changes. A mip selector
/// is shown but disabled because v1 snapshots only capture mip 0.
/// </summary>
public sealed class SnapshotTextureInspector
{
    private bool _r = true, _g = true, _b = true, _a;
    private bool _depthLinear;

    private Texture2D? _display;
    private SnapshotTexture? _builtFrom;
    private int _builtSignature;

    private const float ControlsHeight = 60f;

    public void Draw(Paper paper, string id, SnapshotTexture? tex, Float4x4 projection,
        FontFile font, float width, float height)
    {
        using (paper.Column(id).Width(width).Height(height).ColBetween(6).Enter())
        {
            if (tex == null)
            {
                EditorGUI.EmptyState(paper, id + "_empty", "No texture", font);
                return;
            }

            DrawControls(paper, id, tex, font, width);
            RebuildIfNeeded(tex, projection);

            float imageH = height - ControlsHeight - 6f;
            if (imageH < 24f) imageH = 24f;

            Texture2D? display = _display;
            using (paper.Box(id + "_img").Width(width).Height(imageH)
                .Rounded(4).BorderColor(EditorTheme.BorderSoft).BorderWidth(1).Clip()
                .IsNotInteractable()
                .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                    PaintImage(canvas, r, display, tex.Width, tex.Height)))
                .Enter()) { }
        }
    }

    private void DrawControls(Paper paper, string id, SnapshotTexture tex, FontFile font, float width)
    {
        using (paper.Column(id + "_ctl").Width(width).Height(ControlsHeight).ColBetween(4).Enter())
        {
            string readout = $"{tex.Format}  {tex.Width}x{tex.Height}" + (tex.IsDepth ? "  depth" : "");
            paper.Box(id + "_read").Width(UnitValue.Percentage(100)).Height(16)
                .Text(readout, font).FontSize(EditorTheme.FontSizeSmall - 1f).TextColor(EditorTheme.InkDim)
                .Alignment(TextAlignment.MiddleLeft).TextTruncate().IsNotInteractable();

            using (paper.Row(id + "_toggles").Width(UnitValue.Percentage(100)).Height(20).RowBetween(10).Enter())
            {
                Channel(paper, id + "_r", "R", _r, v => _r = v);
                Channel(paper, id + "_g", "G", _g, v => _g = v);
                Channel(paper, id + "_b", "B", _b, v => _b = v);
                Channel(paper, id + "_a", "A", _a, v => _a = v);

                if (tex.IsDepth)
                    using (paper.Box(id + "_dl_wrap").Width(UnitValue.Auto).Height(20).Enter())
                        Origami.Checkbox(paper, id + "_dl", _depthLinear, v => _depthLinear = v)
                            .LabelRight("Linearize").Show();
            }

            paper.Box(id + "_mip").Width(UnitValue.Percentage(100)).Height(14)
                .Text("Mip 0 (only mip captured)", font).FontSize(EditorTheme.FontSizeSmall - 2f)
                .TextColor(EditorTheme.Ink300).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
        }
    }

    private static void Channel(Paper paper, string id, string label, bool value, Action<bool> setter)
    {
        using (paper.Box(id + "_wrap").Width(UnitValue.Auto).Height(20).Enter())
            Origami.Checkbox(paper, id, value, setter).LabelRight(label).Show();
    }

    private int Signature(SnapshotTexture tex)
    {
        int s = 17;
        s = s * 31 + (_r ? 1 : 0);
        s = s * 31 + (_g ? 1 : 0);
        s = s * 31 + (_b ? 1 : 0);
        s = s * 31 + (_a ? 1 : 0);
        s = s * 31 + (_depthLinear ? 1 : 0);
        s = s * 31 + tex.Width;
        s = s * 31 + tex.Height;
        return s;
    }

    private void RebuildIfNeeded(SnapshotTexture tex, Float4x4 projection)
    {
        int sig = Signature(tex);
        if (ReferenceEquals(_builtFrom, tex) && sig == _builtSignature && _display != null)
            return;

        _builtFrom = tex;
        _builtSignature = sig;

        byte[]? rgba = Decode(tex, projection);
        if (rgba == null || tex.Width <= 0 || tex.Height <= 0)
        {
            _display?.Dispose();
            _display = null;
            return;
        }

        try
        {
            if (_display == null || _display.Width != (uint)tex.Width || _display.Height != (uint)tex.Height)
            {
                _display?.Dispose();
                _display = new Texture2D((uint)tex.Width, (uint)tex.Height, false, PixelFormat.R8_G8_B8_A8_UNorm);
            }
            _display.SetData<byte>(rgba.AsMemory());
        }
        catch
        {
            _display?.Dispose();
            _display = null;
        }
    }

    private byte[]? Decode(SnapshotTexture tex, Float4x4 projection)
    {
        int w = tex.Width, h = tex.Height;
        long pixels = (long)w * h;
        if (pixels <= 0) return null;

        int bpp;
        try { bpp = (int)tex.Format.GetSizeInBytes(); }
        catch { bpp = 0; }
        if (bpp <= 0) return null;

        byte[] src = tex.Pixels;
        if (src.LongLength < pixels * bpp) return null;

        var outp = new byte[pixels * 4];

        if (tex.IsDepth || IsDepthFormat(tex.Format))
        {
            (double near, double far) = ExtractNearFar(projection);
            for (long i = 0; i < pixels; i++)
            {
                double d = ReadDepth(src, (int)(i * bpp), tex.Format);
                if (_depthLinear) d = Linearize(d, near, far);
                byte v = (byte)Math.Clamp(d * 255.0, 0, 255);
                long o = i * 4;
                outp[o + 0] = v; outp[o + 1] = v; outp[o + 2] = v; outp[o + 3] = 255;
            }
            return outp;
        }

        bool bgr = tex.Format is PixelFormat.B8_G8_R8_A8_UNorm or PixelFormat.B8_G8_R8_A8_UNorm_SRgb;
        bool rgba16 = tex.Format is PixelFormat.R16_G16_B16_A16_UNorm or PixelFormat.R16_G16_B16_A16_Float;
        bool alphaOnly = _a && !_r && !_g && !_b;

        for (long i = 0; i < pixels; i++)
        {
            int p = (int)(i * bpp);
            byte r, g, b, a;

            if (rgba16)
            {
                r = (byte)(BitConverter.ToUInt16(src, p + 0) >> 8);
                g = (byte)(BitConverter.ToUInt16(src, p + 2) >> 8);
                b = (byte)(BitConverter.ToUInt16(src, p + 4) >> 8);
                a = (byte)(BitConverter.ToUInt16(src, p + 6) >> 8);
            }
            else if (bpp >= 4)
            {
                if (bgr) { b = src[p + 0]; g = src[p + 1]; r = src[p + 2]; a = src[p + 3]; }
                else { r = src[p + 0]; g = src[p + 1]; b = src[p + 2]; a = src[p + 3]; }
            }
            else if (bpp >= 1)
            {
                r = g = b = src[p]; a = 255;
            }
            else
            {
                r = g = b = 0; a = 255;
            }

            long o = i * 4;
            if (alphaOnly)
            {
                outp[o + 0] = a; outp[o + 1] = a; outp[o + 2] = a; outp[o + 3] = 255;
            }
            else
            {
                outp[o + 0] = _r ? r : (byte)0;
                outp[o + 1] = _g ? g : (byte)0;
                outp[o + 2] = _b ? b : (byte)0;
                outp[o + 3] = 255;
            }
        }

        return outp;
    }

    private static bool IsDepthFormat(PixelFormat f)
        => f is PixelFormat.D24_UNorm_S8_UInt or PixelFormat.D32_Float_S8_UInt;

    private static double ReadDepth(byte[] src, int offset, PixelFormat format)
    {
        switch (format)
        {
            case PixelFormat.D24_UNorm_S8_UInt:
            {
                int d24 = src[offset] | (src[offset + 1] << 8) | (src[offset + 2] << 16);
                return d24 / 16777215.0;
            }
            case PixelFormat.D32_Float_S8_UInt:
            case PixelFormat.R32_Float:
                return Math.Clamp(BitConverter.ToSingle(src, offset), 0.0, 1.0);
            default:
                return src[offset] / 255.0;
        }
    }

    private static (double near, double far) ExtractNearFar(Float4x4 projection)
    {
        double m22 = projection.c2.Z;
        double m23 = projection.c3.Z;
        if (Math.Abs(m22) < 1e-6) return (0.1, 1000.0);
        double near = m23 / m22;
        double denom = 1.0 + m22;
        double far = Math.Abs(denom) < 1e-6 ? near * 1000.0 : m22 * near / denom;
        if (!(near > 0) || !(far > near) || double.IsNaN(near) || double.IsNaN(far))
            return (0.1, 1000.0);
        return (near, far);
    }

    private static double Linearize(double d, double near, double far)
    {
        double denom = d + (far / (near - far));
        if (Math.Abs(denom) < 1e-9) return 0.0;
        double zView = -(near * far / (near - far)) / denom;
        double lin = (zView - near) / (far - near);
        return Math.Clamp(lin, 0.0, 1.0);
    }

    private static void PaintImage(Canvas canvas, Rect rect, Texture2D? tex, int srcW, int srcH)
    {
        float x0 = (float)rect.Min.X, y0 = (float)rect.Min.Y;
        float w = (float)rect.Size.X, h = (float)rect.Size.Y;
        if (w <= 1f || h <= 1f) return;

        canvas.SaveState();
        canvas.SetFillColor(new Color32(20, 20, 24, 255));
        canvas.BeginPath();
        canvas.RoundedRect(x0, y0, w, h, 4f);
        canvas.Fill();
        canvas.RestoreState();

        if (tex == null || srcW <= 0 || srcH <= 0)
            return;

        float aspect = srcW / (float)srcH;
        float dw = w, dh = w / aspect;
        if (dh > h) { dh = h; dw = h * aspect; }
        float dx = x0 + (w - dw) * 0.5f;
        float dy = y0 + (h - dh) * 0.5f;

        canvas.DrawImage(tex, dx, dy, dw, dh);
    }
}
