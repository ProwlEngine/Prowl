using System;
using System.Collections.Generic;

using Prowl.Editor.Profiling;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;

namespace Prowl.Editor.GUI.RenderProfiler;

public partial class RenderProfilerPanel
{
    private const float FramePickerHeight = 100f;


    private void DrawFramePicker(Paper paper)
    {
        using (paper.Row("rdp_frame_picker")
            .Height(FramePickerHeight)
            .Enter())
        {
            DrawFrameBars(paper);

            EditorGUI.VerticalDivider(paper, "rdp_picker_vdiv");

            DrawFrameStatsBox(paper);
        }
    }


    private void DrawFrameBars(Paper paper)
    {
        IReadOnlyList<ProfiledFrame> history = _profiler.History;
        int count = history.Count;

        ElementBuilder chart = paper.Box("rdp_picker_chart")
            .Width(UnitValue.Stretch())
            .Height(FramePickerHeight)
            .BackgroundColor(EditorTheme.Neutral300);

        if (count == 0)
            return;

        double maxMs = 0.0;
        double sumMs = 0.0;
        foreach (ProfiledFrame f in history)
        {
            maxMs = Math.Max(maxMs, f.FrameMilliseconds);
            sumMs += f.FrameMilliseconds;
        }
        double avgMs = sumMs / count;
        float avgNormalized = maxMs > 0.0 ? (float)(avgMs / maxMs) : 0f;
        long? selectedAgo = _selectedFrameIndex;

        chart.OnClick(history, (h, e) =>
        {
            int index = Math.Clamp((int)(e.NormalizedPosition.X * h.Count), 0, h.Count - 1);
            SelectFrame(h.Count - 1 - index);
        });

        chart.OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
        {
            Float2? hoverPos = paper.IsElementHovered(handle.Data.ID) ? paper.PointerPos : null;
            DrawBars(canvas, r, history, maxMs, selectedAgo, hoverPos);
            DrawAverageLine(canvas, r, avgNormalized);
        }));
    }


    private static void DrawBars(Canvas canvas, Rect rect, IReadOnlyList<ProfiledFrame> history, double maxMs, long? selectedAgo, Float2? hoverPos)
    {
        int count = history.Count;
        float x0 = rect.Min.X;
        float y0 = rect.Min.Y;
        float w = rect.Size.X;
        float h = rect.Size.Y;
        float barWidth = w / count;
        float barGap = barWidth > 3f ? 1f : 0f;

        int hoveredIndex = hoverPos.HasValue ? Math.Clamp((int)((hoverPos.Value.X - x0) / barWidth), 0, count - 1) : -1;

        for (int i = 0; i < count; i++)
        {
            ProfiledFrame frame = history[i];
            float normalized = maxMs > 0.0 ? (float)(frame.FrameMilliseconds / maxMs) : 0f;
            float barHeight = Math.Max(2f, normalized * h);
            float bx = x0 + i * barWidth;
            float by = y0 + h - barHeight;

            long ago = count - 1 - i;
            bool isSelected = selectedAgo.HasValue && selectedAgo.Value == ago;
            bool isHovered = i == hoveredIndex;

            Color color = isSelected ? EditorTheme.Purple500 : isHovered ? EditorTheme.Purple400 : EditorTheme.Purple300;
            canvas.RectFilled(bx, by, Math.Max(1f, barWidth - barGap), barHeight, color);
        }
    }


    private static void DrawAverageLine(Canvas canvas, Rect rect, float normalizedY)
    {
        float x0 = rect.Min.X;
        float x1 = rect.Max.X;
        float y = rect.Min.Y + rect.Size.Y * (1f - normalizedY);

        canvas.SetStrokeColor(EditorTheme.WithAlpha(EditorTheme.Amber500, 180));
        canvas.SetStrokeWidth(1f);

        const float dashLength = 4f;
        const float gapLength = 3f;
        float x = x0;
        while (x < x1)
        {
            float dashEnd = Math.Min(x + dashLength, x1);
            canvas.BeginPath();
            canvas.MoveTo(x, y);
            canvas.LineTo(dashEnd, y);
            canvas.Stroke();
            x = dashEnd + gapLength;
        }
    }


    private void DrawFrameStatsBox(Paper paper)
    {
        IReadOnlyList<ProfiledFrame> history = _profiler.History;

        using (paper.Column("rdp_stats_content")
            .Width(200)
            .Padding(10)
            .RowBetween(4)
            .Enter())
        {
            ProfiledFrame frame = SelectedFrame;

            if (frame == null)
            {
                EditorGUI.EmptyState(paper, "rdp_stats_empty", "No frames captured", EditorTheme.DefaultFont!);
                return;
            }

            double avgFrameMs = 0.0;
            double avgGpuMs = 0.0;
            if (history.Count > 0)
            {
                double sumFrameMs = 0.0;
                double sumGpuMs = 0.0;
                foreach (ProfiledFrame f in history)
                {
                    sumFrameMs += f.FrameMilliseconds;
                    sumGpuMs += f.GpuMilliseconds;
                }
                avgFrameMs = sumFrameMs / history.Count;
                avgGpuMs = sumGpuMs / history.Count;
            }

            EditorGUI.StatRow(paper, "rdp_stats_frame", $"Frame {frame.FrameIndex}", $"{frame.ViewCount} views");
            EditorGUI.StatRow(paper, "rdp_stats_capture", frame.HasCaptureDepth ? "Capture" : "Live", $"{frame.Fps:F1} FPS", BudgetColor(frame.Fps));
            EditorGUI.StatRow(paper, "rdp_stats_cpu", $"CPU {frame.FrameMilliseconds:F2} ms", $"avg {avgFrameMs:F2} ms", BudgetColor(MsToFps(frame.FrameMilliseconds)));
            EditorGUI.StatRow(paper, "rdp_stats_gpu", $"GPU {frame.GpuMilliseconds:F2} ms", $"avg {avgGpuMs:F2} ms", BudgetColor(MsToFps(frame.GpuMilliseconds)));
        }
    }


    private static double MsToFps(double ms) => ms > 0.0 ? 1000.0 / ms : 0.0;


    private static Color BudgetColor(double fps)
    {
        if (fps < 20.0) return EditorTheme.Red500;
        if (fps < 40.0) return EditorTheme.Amber500;
        return EditorTheme.Green500;
    }
}
