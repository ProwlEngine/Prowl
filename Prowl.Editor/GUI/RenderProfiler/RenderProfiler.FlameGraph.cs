using System;
using System.Collections.Generic;

using Prowl.Editor.Profiling;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.OrigamiUI.Charts;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Vector;

namespace Prowl.Editor.GUI.RenderProfiler;

public partial class RenderProfilerPanel
{
    private const float FlameGraphHeight = 100f;
    private const float FlameGraphRowHeight = 18f;
    private const double MinNodeDurationFraction = 0.004;

    // Frame is the sole root; passes and command buffers (free or nested under a pass) are its
    // descendants. Views aren't represented as their own node - they only tag a pass so a click can
    // still ping the right hierarchy row.
    private sealed class FlameNode
    {
        public string Label = "";
        public double GpuMilliseconds;
        public ProfiledView? View;
        public ProfiledPass? Pass;
        public ProfiledCommandBuffer? CommandBuffer;
        public readonly List<FlameNode> Children = new();
    }


    private void DrawFlameGraph(Paper paper)
    {
        ProfiledFrame? frame = SelectedFrame;

        using (paper.Box("rdp_flame_graph").Width(UnitValue.Stretch()).Height(FlameGraphHeight).Enter())
        {
            if (frame == null)
            {
                Origami.Label(paper, "rdp_flame_empty", "No frame selected")
                    .Muted()
                    .AlignCenter()
                    .Show();
                return;
            }

            FlameNode root = BuildFlameTree(frame);

            Chart.FlameGraph(paper, "rdp_flame_chart", new List<FlameNode> { root })
                .Title("Frame Timeline")
                .Width(UnitValue.Stretch())
                .Height(FlameGraphHeight)
                .RowHeight(FlameGraphRowHeight)
                .Legend(false)
                .Padding(2f)
                .Name(n => n.Label)
                .Value(n => n.GpuMilliseconds)
                .Children(n => n.Children)
                .OnNodeClick(n => PingHierarchyForFlameSelection(n.View, n.Pass, n.CommandBuffer))
                .Zoomable()
                .Pannable()
                .Show();
        }
    }


    private static FlameNode BuildFlameTree(ProfiledFrame frame)
    {
        double minDuration = frame.GpuMilliseconds * MinNodeDurationFraction;

        var root = new FlameNode
        {
            Label = $"Frame {frame.FrameIndex} ({frame.GpuMilliseconds:F2} ms)",
            GpuMilliseconds = frame.GpuMilliseconds,
        };

        for (int i = 0; i < frame.TimelineElementCount; i++)
        {
            frame.GetTimelineElement(i, out ProfiledView? view, out ProfiledCommandBuffer? freeCommandBuffer);

            if (view != null)
            {
                foreach (ProfiledPass pass in view.Passes)
                {
                    if (pass.GpuMilliseconds < minDuration) continue;
                    root.Children.Add(BuildFlamePassNode(view, pass, minDuration));
                }
            }
            else if (freeCommandBuffer != null && freeCommandBuffer.GpuMilliseconds >= minDuration)
            {
                root.Children.Add(BuildFlameCommandBufferNode(null, null, freeCommandBuffer));
            }
        }

        return root;
    }


    private static FlameNode BuildFlamePassNode(ProfiledView view, ProfiledPass pass, double minDuration)
    {
        var node = new FlameNode
        {
            Label = $"{pass.Name} ({pass.GpuMilliseconds:F2} ms)",
            GpuMilliseconds = pass.GpuMilliseconds,
            View = view,
            Pass = pass,
        };

        foreach (ProfiledCommandBuffer commandBuffer in pass.CommandBuffers)
        {
            if (commandBuffer.GpuMilliseconds < minDuration) continue;
            node.Children.Add(BuildFlameCommandBufferNode(view, pass, commandBuffer));
        }

        return node;
    }


    private static FlameNode BuildFlameCommandBufferNode(ProfiledView? view, ProfiledPass? pass, ProfiledCommandBuffer commandBuffer)
        => new()
        {
            Label = $"{commandBuffer.Name} ({commandBuffer.GpuMilliseconds:F2} ms)",
            GpuMilliseconds = commandBuffer.GpuMilliseconds,
            View = view,
            Pass = pass,
            CommandBuffer = commandBuffer,
        };
}
