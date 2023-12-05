using HexaEngine.ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Prowl.Runtime.ImGUI.Widgets
{
    public class ImFlameGraph
    {
        public struct FlameGraphValue
        {
            public float StageStart;
            public float StageEnd;
            public byte Depth;
            public string Caption;
        }

        public static void PlotFlame(string label, int valuesCount, int valuesOffset, string overlayText, float scaleMin, float scaleMax, Vector2 graphSize, Func<int, FlameGraphValue> valuesGetter)
        {
            unsafe
            {
                ImGuiWindowPtr window = ImGui.GetCurrentWindow();
                if (window.SkipItems)
                    return;

                ImGuiContextPtr g = ImGui.GetCurrentContext();
                ImGuiStyle style = g.Style;

                // Find the maximum depth
                byte maxDepth = 0;
                for (int i = valuesOffset; i < valuesCount; ++i)
                {
                    var values = valuesGetter?.Invoke(i);
                    maxDepth = Math.Max(maxDepth, values.Value.Depth);
                }

                float blockHeight = ImGui.GetTextLineHeight() + (style.FramePadding.Y * 2);
                System.Numerics.Vector2 labelSize = ImGui.CalcTextSize(label);
                if (graphSize.X == 0.0f)
                    graphSize.X = ImGui.CalcItemWidth();
                if (graphSize.Y == 0.0f)
                    graphSize.Y = labelSize.Y + (style.FramePadding.Y * 3) + blockHeight * (maxDepth + 1);

                ImRect frameBB = new ImRect() { Min = window.DC.CursorPos, Max = window.DC.CursorPos + graphSize.ToFloat() };
                ImRect innerBB = new ImRect() { Min = frameBB.Min + style.FramePadding, Max = frameBB.Max - style.FramePadding };
                ImRect totalBB = new ImRect() { Min = frameBB.Min, Max = frameBB.Max + new System.Numerics.Vector2(labelSize.X > 0.0f ? style.ItemInnerSpacing.X + labelSize.X : 0.0f, 0) };
                ImGui.ItemSizeRect(totalBB, style.FramePadding.Y);
                if (!ImGui.ItemAdd(totalBB, 0, ref frameBB, ImGuiItemFlags.None))
                    return;

                // Determine scale from values if not specified
                if (scaleMin == float.MaxValue || scaleMax == float.MaxValue)
                {
                    float vMin = float.MaxValue;
                    float vMax = float.MinValue;
                    for (int i = valuesOffset; i < valuesCount; i++)
                    {
                        float vStart, vEnd;
                        var values = valuesGetter?.Invoke(i);
                        vStart = values.Value.StageStart;
                        vEnd = values.Value.StageEnd;

                        if (!float.IsNaN(vStart))
                            vMin = Math.Min(vMin, vStart);
                        if (!float.IsNaN(vEnd))
                            vMax = Math.Max(vMax, vEnd);
                    }
                    if (scaleMin == float.MaxValue)
                        scaleMin = vMin;
                    if (scaleMax == float.MaxValue)
                        scaleMax = vMax;
                }

                ImGui.RenderFrame(frameBB.Min, frameBB.Max, ImGui.GetColorU32(ImGuiCol.FrameBg), true, style.FrameRounding);

                bool anyHovered = false;
                if (valuesCount - valuesOffset >= 1)
                {
                    uint colBase = ImGui.GetColorU32(ImGuiCol.PlotHistogram) & 0x77FFFFFF;
                    uint colHovered = ImGui.GetColorU32(ImGuiCol.PlotHistogramHovered) & 0x77FFFFFF;
                    uint colOutlineBase = ImGui.GetColorU32(ImGuiCol.PlotHistogram) & 0x7FFFFFFF;
                    uint colOutlineHovered = ImGui.GetColorU32(ImGuiCol.PlotHistogramHovered) & 0x7FFFFFFF;

                    for (int i = valuesOffset; i < valuesCount; ++i)
                    {
                        float stageStart, stageEnd;
                        byte depth;
                        string caption;

                        //valuesGetter(out stageStart, out stageEnd, out depth, out caption, i);
                        var values = valuesGetter?.Invoke(i);
                        stageStart = values.Value.StageStart;
                        stageEnd = values.Value.StageEnd;
                        depth = values.Value.Depth;
                        caption = values.Value.Caption;

                        float duration = scaleMax - scaleMin;
                        if (duration == 0)
                        {
                            return;
                        }

                        float start = stageStart - scaleMin;
                        float end = stageEnd - scaleMin;

                        float startX = start / duration;
                        float endX = end / duration;

                        float width = innerBB.Max.X - innerBB.Min.X;
                        float height = blockHeight * (maxDepth - depth + 1) - style.FramePadding.Y;

                        Vector2 pos0 = innerBB.Min + new System.Numerics.Vector2(startX * width, height);
                        Vector2 pos1 = innerBB.Min + new System.Numerics.Vector2(endX * width, height + blockHeight);

                        bool vHovered = false;
                        if (ImGui.IsMouseHoveringRect(pos0, pos1))
                        {
                            ImGui.SetTooltip($"{caption}: {stageEnd - stageStart:0.####}");
                            vHovered = true;
                            anyHovered = vHovered;
                        }

                        window.DrawList.AddRectFilled(pos0, pos1, vHovered ? colHovered : colBase);
                        window.DrawList.AddRect(pos0, pos1, vHovered ? colOutlineHovered : colOutlineBase);
                        Vector2 textSize = ImGui.CalcTextSize(caption);
                        Vector2 boxSize = pos1 - pos0;
                        Vector2 textOffset = new Vector2(0.0f, 0.0f);
                        if (textSize.X < boxSize.X)
                        {
                            textOffset = new Vector2(0.5f, 0.5f) * (boxSize - textSize);
                            ImGui.RenderText(pos0 + textOffset, caption, "", false);
                        }
                    }

                    // Text overlay
                    if (overlayText != null)
                        ImGui.RenderTextClipped(new Vector2(frameBB.Min.X, frameBB.Min.Y + style.FramePadding.Y), frameBB.Max, overlayText, "", null, new Vector2(0.5f, 0.0f), null);

                    if (labelSize.X > 0.0f)
                        ImGui.RenderText(new Vector2(frameBB.Max.X + style.ItemInnerSpacing.X, innerBB.Min.Y), label, "", false);
                }

                if (!anyHovered && ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"Total: {scaleMax - scaleMin:0.####}");
                }
            }
        }
    }
}
