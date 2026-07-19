using System;

using Prowl.Editor.Theming;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Runtime.Rendering;
using Prowl.Scribe;
using Prowl.Vector;

using Color = System.Drawing.Color;
using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.Editor.GUI.RenderProfiler;

/// <summary>
/// Self-contained wireframe viewer for a draw call's <see cref="SnapshotGeometry"/>. The captured mesh
/// positions are projected through the snapshot camera's View*Projection into clip space, then to the
/// panel rect, and each triangle's three edges are stroked on the Quill canvas. An orbit rotation
/// (drag) and a zoom scale (accumulated from drag distance) are applied to the geometry about its
/// centroid before the captured view, so the wireframe stays inside the original frustum. No GPU render
/// is involved; this is a pure CPU line projection.
/// </summary>
public sealed class WireframeViewer
{
    private float _yaw;
    private float _pitch;
    private float _zoom = 1f;

    public void Draw(Paper paper, string id, SnapshotGeometry? geo, CapturedCamera camera,
        FontFile font, float width, float height)
    {
        using (paper.Column(id).Width(width).Height(height).ColBetween(4).Enter())
        {
            if (geo == null || geo.Positions.Length < 9 || geo.Indices.Length < 3)
            {
                EditorGUI.EmptyState(paper, id + "_empty", "No wireframe geometry", font);
                return;
            }

            using (paper.Row(id + "_ctl").Width(UnitValue.Percentage(100)).Height(16).RowBetween(8).Enter())
            {
                int tris = geo.Indices.Length / 3;
                paper.Box(id + "_info").Width(UnitValue.Stretch()).Height(16)
                    .Text($"{geo.Positions.Length / 3} verts  {tris} tris", font)
                    .FontSize(EditorTheme.FontSizeSmall - 1f).TextColor(EditorTheme.InkDim)
                    .Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
                paper.Box(id + "_hint").Width(UnitValue.Auto).Height(16)
                    .Text("drag orbit / scroll-drag zoom", font)
                    .FontSize(EditorTheme.FontSizeSmall - 2f).TextColor(EditorTheme.Ink300)
                    .Alignment(TextAlignment.MiddleRight).IsNotInteractable();
            }

            float canvasH = height - 20f;
            if (canvasH < 24f) canvasH = 24f;

            var geoRef = geo;
            var cam = camera;
            using (paper.Box(id + "_cv").Width(UnitValue.Percentage(100)).Height(canvasH)
                .Rounded(4).BorderColor(EditorTheme.BorderSoft).BorderWidth(1).Clip()
                .StopEventPropagation()
                .OnDragging(e =>
                {
                    _yaw -= (float)e.Delta.X * 0.01f;
                    _pitch -= (float)e.Delta.Y * 0.01f;
                    _pitch = Math.Clamp(_pitch, -1.55f, 1.55f);
                })
                .OnScroll(e => _zoom = Math.Clamp(_zoom * (1f + (float)e.Delta * 0.1f), 0.1f, 10f))
                .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                    Paint(canvas, r, geoRef, cam)))
                .Enter()) { }
        }
    }

    private void Paint(Canvas canvas, Rect rect, SnapshotGeometry geo, CapturedCamera camera)
    {
        float x0 = (float)rect.Min.X, y0 = (float)rect.Min.Y;
        float w = (float)rect.Size.X, h = (float)rect.Size.Y;
        if (w <= 2f || h <= 2f) return;

        canvas.SaveState();
        canvas.SetFillColor(new Color32(18, 18, 22, 255));
        canvas.BeginPath();
        canvas.RoundedRect(x0, y0, w, h, 4f);
        canvas.Fill();
        canvas.RestoreState();

        float[] pos = geo.Positions;
        int vertCount = pos.Length / 3;

        Float3 centroid = default;
        for (int i = 0; i < vertCount; i++)
            centroid += new Float3(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]);
        centroid /= Math.Max(1, vertCount);

        Float4x4 model = BuildModel(centroid);
        Float4x4 mvp = camera.Projection * camera.View * model;

        var screen = new Float2[vertCount];
        var valid = new bool[vertCount];
        for (int i = 0; i < vertCount; i++)
        {
            var world = new Float4(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2], 1f);
            Float4 clip = mvp * world;
            if (clip.W <= 1e-4f) { valid[i] = false; continue; }
            float ndcX = clip.X / clip.W;
            float ndcY = clip.Y / clip.W;
            screen[i] = new Float2(
                x0 + (ndcX * 0.5f + 0.5f) * w,
                y0 + (1f - (ndcY * 0.5f + 0.5f)) * h);
            valid[i] = true;
        }

        var edge = new Color32(120, 200, 255, 220);
        canvas.SaveState();
        canvas.SetStrokeColor(edge);
        canvas.SetStrokeWidth(1f);

        int[] idx = geo.Indices;
        for (int t = 0; t + 2 < idx.Length; t += 3)
        {
            int a = idx[t], b = idx[t + 1], c = idx[t + 2];
            if (a < 0 || b < 0 || c < 0 || a >= vertCount || b >= vertCount || c >= vertCount) continue;
            if (!valid[a] || !valid[b] || !valid[c]) continue;
            StrokeLine(canvas, screen[a], screen[b]);
            StrokeLine(canvas, screen[b], screen[c]);
            StrokeLine(canvas, screen[c], screen[a]);
        }

        canvas.RestoreState();
    }

    private Float4x4 BuildModel(Float3 centroid)
    {
        float cy = MathF.Cos(_yaw), sy = MathF.Sin(_yaw);
        float cp = MathF.Cos(_pitch), sp = MathF.Sin(_pitch);

        var ry = new Float4x4(
            cy, 0, sy, 0,
            0, 1, 0, 0,
            -sy, 0, cy, 0,
            0, 0, 0, 1);

        var rx = new Float4x4(
            1, 0, 0, 0,
            0, cp, -sp, 0,
            0, sp, cp, 0,
            0, 0, 0, 1);

        Float4x4 scale = Float4x4.CreateScale(_zoom);
        Float4x4 toOrigin = Float4x4.CreateTranslation(-centroid);
        Float4x4 back = Float4x4.CreateTranslation(centroid);

        return back * (rx * ry) * scale * toOrigin;
    }

    private static void StrokeLine(Canvas canvas, Float2 a, Float2 b)
    {
        canvas.BeginPath();
        canvas.MoveTo((float)a.X, (float)a.Y);
        canvas.LineTo((float)b.X, (float)b.Y);
        canvas.Stroke();
    }
}
