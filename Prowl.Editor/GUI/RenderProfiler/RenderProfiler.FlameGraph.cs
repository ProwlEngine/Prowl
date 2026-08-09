using System;

using Prowl.Editor.Profiling;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Vector;

namespace Prowl.Editor.GUI.RenderProfiler;

public partial class RenderProfilerPanel
{
    private const float FlameGraphHeight = 100f;
    private const double MinNodeDurationFraction = 0.004;

    private static readonly Color FrameNodeColor = EditorTheme.Neutral700;
    private static readonly Color ViewNodeColor = EditorTheme.Blue500;
    private static readonly Color PassNodeColor = EditorTheme.Green500;
    private static readonly Color CommandBufferNodeColor = EditorTheme.Red400;


    private readonly record struct FlameSelection(ProfiledView? View, ProfiledPass? Pass, ProfiledCommandBuffer? CommandBuffer);


    private void DrawFlameGraph(Paper paper)
    {

    }
}
