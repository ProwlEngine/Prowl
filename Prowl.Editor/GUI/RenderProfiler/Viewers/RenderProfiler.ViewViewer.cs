using System.Collections.Generic;

using Prowl.Editor.Profiling;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Vector;

namespace Prowl.Editor.GUI.RenderProfiler;

public partial class RenderProfilerPanel
{
    private const int ViewViewerModeStats = 0;
    private const int ViewViewerModePipeline = 1;

    private int _viewViewerMode = ViewViewerModeStats;

    private readonly NodeGraphController _viewPipelineGraphController = new();
    private ProfiledView? _viewPipelineGraphFramedFor;

    private const float PipelinePassWidth = 190f;
    private const float PipelinePassSpacingX = 260f;
    private const float PipelineResourceWidth = 150f;
    private const float PipelineResourceSpacingY = 90f;
    private const float PipelineResourceOffsetY = -170f;
    private const float PipelineGraphHeight = 480f;
    private const float PipelineGraphPadding = 16f;


    private void DrawViewViewer(Paper paper, float width)
    {
        ProfiledView? view = SelectedView;
        if (view == null)
        {
            Origami.Label(paper, "rdp_view_empty", "No view selected")
                .Muted()
                .Show();
            return;
        }

        using (paper.Column("rdp_view_viewer").Height(UnitValue.Auto).ColBetween(SectionGap).Enter())
        {
            using (paper.Row("rdp_view_header").Height(SelectionViewerHeaderHeight).ColBetween(8f).Enter())
            {
                Origami.Label(paper, "rdp_view_title", view.Name)
                    .Heading()
                    .AlignLeft()
                    .Show();

                Origami.Label(paper, "rdp_view_res", $"{view.PixelWidth}x{view.PixelHeight}")
                    .Muted()
                    .AlignLeft()
                    .Show();

                paper.Box("rdp_view_header_spacer");

                Origami.ButtonGroup(paper, "rdp_view_mode", _viewViewerMode, i => _viewViewerMode = i)
                    .Item("Stats")
                    .Item("Pipeline")
                    .Small()
                    .Show();
            }

            if (_viewViewerMode == ViewViewerModePipeline)
                DrawViewPipeline(paper, view, width);
            else
                DrawViewStats(paper, view);
        }
    }


    // ── Stats view ──────────────────────────────────────────────────

    private void DrawViewStats(Paper paper, ProfiledView view)
    {
        SectionCard(paper, "rdp_vv_objects", "Objects", () => DrawViewObjectsChart(paper, view.Name));
        SectionCard(paper, "rdp_vv_renderops", "Render Operations", () => DrawViewRenderOperationsSection(paper, view));
    }


    private void DrawViewObjectsChart(Paper paper, string viewName)
    {
        DrawLineChart(paper, "rdp_vv_objects_chart", "Objects", "Count", FormatCountCompact, UnitValue.Stretch(), ChartHeight, true,
            ("Total", EditorTheme.Blue500, ViewSeries(viewName, v => v.RegisteredObjects)),
            ("Drawn", EditorTheme.Green500, ViewSeries(viewName, v => v.RenderedObjects)),
            ("Culled", EditorTheme.Red500, ViewSeries(viewName, v => v.CulledObjects)));
    }


    private void DrawViewRenderOperationsSection(Paper paper, ProfiledView view)
    {
        using (paper.Column("rdp_vv_renderops_col").Height(UnitValue.Auto).ColBetween(ChartRowGap).Enter())
        {
            DrawViewGeometryChart(paper, view.Name);
            DrawViewRenderingChart(paper, view.Name);
            DrawViewPixelProcessingChart(paper, view);
            DrawViewPipelineStateChart(paper, view.Name);
        }
    }


    private void DrawViewGeometryChart(Paper paper, string viewName)
    {
        DrawLineChart(paper, "rdp_vv_geometry_chart", "Geometry", "Count", FormatCountCompact, UnitValue.Stretch(), ChartHeight, true,
            ("Triangles", EditorTheme.Blue500, ViewSeries(viewName, v => v.TrianglesDrawn)),
            ("Vertices", EditorTheme.Purple500, ViewSeries(viewName, v => v.InputAssemblyVertices)));
    }


    private void DrawViewRenderingChart(Paper paper, string viewName)
    {
        DrawLineChart(paper, "rdp_vv_rendering_chart", "Rendering", "Count", FormatCountCompact, UnitValue.Stretch(), ChartHeight, true,
            ("Draw Calls", EditorTheme.Green500, ViewSeries(viewName, v => v.DrawCallCount)),
            ("Dispatches", EditorTheme.Red500, ViewSeries(viewName, v => v.DispatchCallCount)));
    }


    // Overdraw has no history worth charting (it's a ratio, not a count that meaningfully sums), so it
    // rides along as a stat readout next to the chart title instead of its own series.
    private void DrawViewPixelProcessingChart(Paper paper, ProfiledView view)
    {
        using (paper.Column("rdp_vv_pixelproc").Height(UnitValue.Auto).ColBetween(2f).Enter())
        {
            using (paper.Row("rdp_vv_pixelproc_stat_row").Height(16f).Enter())
            {
                paper.Box("rdp_vv_pixelproc_stat_spacer");

                double overdraw = view.Overdraw;
                Origami.Label(paper, "rdp_vv_overdraw", $"{overdraw:F2}x overdraw ({overdraw * 100d:F0}%)")
                    .SM()
                    .Muted()
                    .AlignRight()
                    .Show();
            }

            DrawLineChart(paper, "rdp_vv_pixelproc_chart", "Pixel Processing", "Count", FormatCountCompact, UnitValue.Stretch(), ChartHeight, false,
                ("Fragment Invocations", EditorTheme.Amber500, ViewSeries(view.Name, v => v.FragmentShaderInvocations)));
        }
    }


    // Transfer Submits (Submit/Transfer) has no per-view attribution - transfers are frame-global free
    // command buffers, never tied to a view - so this chart only carries what's actually per-view.
    private void DrawViewPipelineStateChart(Paper paper, string viewName)
    {
        DrawLineChart(paper, "rdp_vv_pipelinestate_chart", "Pipeline State", "Count", FormatCountCompact, UnitValue.Stretch(), ChartHeight, true,
            ("Switches", EditorTheme.Purple500, ViewSeries(viewName, v => v.PipelineSwitchCount)),
            ("Command Submits", EditorTheme.Blue500, ViewSeries(viewName, ViewCommandBufferCount)));
    }


    // One submit maps to exactly one command buffer (see PROFILING_MODEL.md), so a view's command
    // submit count is just the total command-buffer count across its passes.
    private static double ViewCommandBufferCount(ProfiledView view)
    {
        int sum = 0;
        foreach (ProfiledPass pass in view.Passes)
            sum += pass.CommandBuffers.Count;
        return sum;
    }


    // ── Pipeline view ───────────────────────────────────────────────

    private void DrawViewPipeline(Paper paper, ProfiledView view, float width)
    {
        SectionCard(paper, "rdp_vv_pipeline", "Pass Graph", () => DrawPipelineGraph(paper, view, width));
    }


    private void DrawPipelineGraph(Paper paper, ProfiledView view, float width)
    {
        var nodes = new List<GraphNode>();
        var connections = new List<GraphConnection>();
        var resourceNodes = new Dictionary<uint, GraphNode>();
        var resourceColumnUsage = new Dictionary<int, int>();

        IReadOnlyList<ProfiledPass> passes = view.Passes;

        for (int i = 0; i < passes.Count; i++)
        {
            ProfiledPass pass = passes[i];

            var passNode = new GraphNode
            {
                Id = $"pass_{pass.Index}",
                Title = pass.Name,
                Position = new Float2(i * PipelinePassSpacingX, 0f),
                Width = PipelinePassWidth,
                Accent = EditorTheme.Blue500,
                UserData = pass,
            };

            foreach (ResourceRef input in pass.Inputs)
            {
                passNode.Inputs.Add(new GraphPort($"in_{input.Id}", input.Name) { Side = PortSide.Left });
                GraphNode resourceNode = GetOrAddResourceNode(nodes, resourceNodes, resourceColumnUsage, input, i);
                connections.Add(new GraphConnection(resourceNode.Id, "out", passNode.Id, $"in_{input.Id}"));
            }

            foreach (ResourceRef output in pass.Outputs)
            {
                passNode.Outputs.Add(new GraphPort($"out_{output.Id}", output.Name) { Side = PortSide.Right });
                GraphNode resourceNode = GetOrAddResourceNode(nodes, resourceNodes, resourceColumnUsage, output, i);
                connections.Add(new GraphConnection(passNode.Id, $"out_{output.Id}", resourceNode.Id, "in"));
            }

            nodes.Add(passNode);
        }

        // Re-frame whenever the selected view changes, so the graph centers on all its nodes
        // instead of defaulting to zoom=1/pan=0 (which sits on the first node in the top-left).
        if (!ReferenceEquals(_viewPipelineGraphFramedFor, view))
        {
            _viewPipelineGraphController.FrameAll();
            _viewPipelineGraphFramedFor = view;
        }

        Origami.NodeGraph(paper, "rdp_vv_pipeline_graph", width - PipelineGraphPadding, PipelineGraphHeight)
            .Nodes(nodes)
            .ReadOnly()
            .Connections(connections)
            .Controller(_viewPipelineGraphController)
            .OnNodeDoubleClick(node =>
            {
                if (node.UserData is ProfiledPass pass)
                {
                    SelectPass(view, pass);
                    PingHierarchy(view, pass, null);
                }
            })
            .Show();
    }


    // Shared in a "shared place" per pass column: a resource read/written by multiple passes gets one
    // node, stacked vertically with any other resource first touched at that same column, instead of a
    // duplicate node per pass.
    private static GraphNode GetOrAddResourceNode(List<GraphNode> nodes, Dictionary<uint, GraphNode> resourceNodes,
        Dictionary<int, int> columnUsage, ResourceRef resource, int passColumn)
    {
        if (resourceNodes.TryGetValue(resource.Id, out GraphNode? existing))
            return existing;

        int stack = columnUsage.GetValueOrDefault(passColumn);
        columnUsage[passColumn] = stack + 1;

        var node = new GraphNode
        {
            Id = $"res_{resource.Id}",
            Title = resource.Name,
            Position = new Float2(passColumn * PipelinePassSpacingX, PipelineResourceOffsetY - stack * PipelineResourceSpacingY),
            Width = PipelineResourceWidth,
            Accent = resource.Kind == ResourceRefKind.Texture ? EditorTheme.Purple500 : EditorTheme.Amber500,
            Pill = true,
            UserData = resource,
        };
        node.Inputs.Add(new GraphPort("in", ""));
        node.Outputs.Add(new GraphPort("out", ""));

        resourceNodes[resource.Id] = node;
        nodes.Add(node);
        return node;
    }
}
