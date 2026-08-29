using System;
using System.Collections.Generic;

using Prowl.Editor.GUI.RenderProfiler.Data;
using Prowl.Editor.Profiling;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Vector;

namespace Prowl.Editor.GUI.RenderProfiler.Inspectors;

public sealed class ProfilerViewInspector
{
    private const int ModeStats = 0;
    private const int ModePipeline = 1;

    private const float PipelinePassWidth = 190f;
    private const float PipelinePassSpacingX = 260f;
    private const float PipelineResourceWidth = 150f;
    private const float PipelineResourceSpacingY = 90f;
    private const float PipelineResourceOffsetY = -170f;
    private const float PipelineGraphHeight = 480f;
    private const float PipelineGraphPadding = 16f;

    private int _mode = ModeStats;
    private readonly NodeGraphController _pipelineGraphController = new();
    private ProfiledView? _pipelineGraphFramedFor;
    private int _pipelineGraphFramedSignature;

    // Raised instead of selecting the pass/pinging the hierarchy directly - see FlameGraph's
    // NodeClicked for why the host owns this wiring.
    public event Action<ProfiledView, ProfiledPass>? PassSelected;


    public void Draw(Paper paper, ProfiledView? view, IProfilerHistory history, float width)
    {
        if (view == null)
        {
            Origami.Label(paper, "rdp_view_empty", "No view selected")
                .Muted()
                .Show();
            return;
        }

        using (paper.Column("rdp_view_viewer").Height(UnitValue.Auto).ColBetween(InspectorKit.SectionGap).Enter())
        {
            using (paper.Row("rdp_view_header").Height(InspectorKit.SelectionViewerHeaderHeight).ColBetween(8f).Enter())
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

                Origami.ButtonGroup(paper, "rdp_view_mode", _mode, i => _mode = i)
                    .Item("Stats")
                    .Item("Pipeline")
                    .Small()
                    .Show();
            }

            if (_mode == ModePipeline)
                DrawViewPipeline(paper, view, width);
            else
                DrawViewStats(paper, view, history);
        }
    }


    // ── Stats view ──────────────────────────────────────────────────

    private static void DrawViewStats(Paper paper, ProfiledView view, IProfilerHistory history)
    {
        InspectorKit.SectionCard(paper, "rdp_vv_objects", "Objects", () => DrawViewObjectsChart(paper, view.Name, history));
        InspectorKit.SectionCard(paper, "rdp_vv_renderops", "Render Operations", () => DrawViewRenderOperationsSection(paper, view, history));
    }


    private static void DrawViewObjectsChart(Paper paper, string viewName, IProfilerHistory history)
    {
        InspectorKit.DrawChart(paper, history, "rdp_vv_objects_chart", "Objects", "Count", InspectorKit.FormatCountCompact, UnitValue.Stretch(), InspectorKit.ChartHeight, true,
            ("Total", EditorTheme.Blue500, InspectorKit.ViewSeries(history, viewName, v => v.RegisteredObjects)),
            ("Drawn", EditorTheme.Green500, InspectorKit.ViewSeries(history, viewName, v => v.RenderedObjects)),
            ("Culled", EditorTheme.Red500, InspectorKit.ViewSeries(history, viewName, v => v.CulledObjects)));
    }


    private static void DrawViewRenderOperationsSection(Paper paper, ProfiledView view, IProfilerHistory history)
    {
        using (paper.Column("rdp_vv_renderops_col").Height(UnitValue.Auto).ColBetween(InspectorKit.ChartRowGap).Enter())
        {
            DrawViewGeometryChart(paper, view.Name, history);
            DrawViewRenderingChart(paper, view.Name, history);
            DrawViewPixelProcessingChart(paper, view, history);
            DrawViewPipelineStateChart(paper, view.Name, history);
        }
    }


    private static void DrawViewGeometryChart(Paper paper, string viewName, IProfilerHistory history)
    {
        InspectorKit.DrawChart(paper, history, "rdp_vv_geometry_chart", "Geometry", "Count", InspectorKit.FormatCountCompact, UnitValue.Stretch(), InspectorKit.ChartHeight, true,
            ("Triangles", EditorTheme.Blue500, InspectorKit.ViewSeries(history, viewName, v => v.TrianglesDrawn)),
            ("Vertices", EditorTheme.Purple500, InspectorKit.ViewSeries(history, viewName, v => v.InputAssemblyVertices)));
    }


    private static void DrawViewRenderingChart(Paper paper, string viewName, IProfilerHistory history)
    {
        InspectorKit.DrawChart(paper, history, "rdp_vv_rendering_chart", "Rendering", "Count", InspectorKit.FormatCountCompact, UnitValue.Stretch(), InspectorKit.ChartHeight, true,
            ("Draw Calls", EditorTheme.Green500, InspectorKit.ViewSeries(history, viewName, v => v.DrawCallCount)),
            ("Dispatches", EditorTheme.Red500, InspectorKit.ViewSeries(history, viewName, v => v.DispatchCallCount)));
    }


    // Overdraw has no history worth charting (it's a ratio, not a count that meaningfully sums), so it
    // rides along as a stat readout next to the chart title instead of its own series.
    private static void DrawViewPixelProcessingChart(Paper paper, ProfiledView view, IProfilerHistory history)
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

            InspectorKit.DrawChart(paper, history, "rdp_vv_pixelproc_chart", "Pixel Processing", "Count", InspectorKit.FormatCountCompact, UnitValue.Stretch(), InspectorKit.ChartHeight, false,
                ("Fragment Invocations", EditorTheme.Amber500, InspectorKit.ViewSeries(history, view.Name, v => v.FragmentShaderInvocations)));
        }
    }


    // Transfer Submits (Submit/Transfer) has no per-view attribution - transfers are frame-global free
    // command buffers, never tied to a view - so this chart only carries what's actually per-view.
    private static void DrawViewPipelineStateChart(Paper paper, string viewName, IProfilerHistory history)
    {
        InspectorKit.DrawChart(paper, history, "rdp_vv_pipelinestate_chart", "Pipeline State", "Count", InspectorKit.FormatCountCompact, UnitValue.Stretch(), InspectorKit.ChartHeight, true,
            ("Switches", EditorTheme.Purple500, InspectorKit.ViewSeries(history, viewName, v => v.PipelineSwitchCount)),
            ("Command Submits", EditorTheme.Blue500, InspectorKit.ViewSeries(history, viewName, ViewCommandBufferCount)));
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
        InspectorKit.SectionCard(paper, "rdp_vv_pipeline", "Pass Graph", () => DrawPipelineGraph(paper, view, width));
    }


    private void DrawPipelineGraph(Paper paper, ProfiledView view, float width)
    {
        var nodes = new List<GraphNode>();
        var connections = new List<GraphConnection>();
        var resourceNodes = new Dictionary<uint, GraphNode>();
        var resourceColumnUsage = new Dictionary<int, int>();

        IReadOnlyList<ProfiledPass> passes = view.Passes;
        IReadOnlyList<PassEdge> edges = view.Edges;

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

                // A pure input - nothing else in this view produced it - gets its own loose node.
                // One produced earlier in the view is wired below as a direct edge from the pass
                // that produced it instead, so it never becomes a second loose node here.
                if (!IsEdgeInto(edges, pass.Index, input.Id))
                {
                    GraphNode resourceNode = GetOrAddResourceNode(nodes, resourceNodes, resourceColumnUsage, input, i);
                    connections.Add(new GraphConnection(resourceNode.Id, "out", passNode.Id, $"in_{input.Id}"));
                }
            }

            foreach (ResourceRef output in pass.Outputs)
            {
                passNode.Outputs.Add(new GraphPort($"out_{output.Id}", output.Name) { Side = PortSide.Right });

                bool consumedInView = false;
                foreach (PassEdge edge in edges)
                {
                    if (edge.FromPass != pass.Index || edge.Resource.Id != output.Id)
                        continue;

                    // Passed directly from this pass into the one that reads it - a direct
                    // node-to-node connection instead of a loose resource pill in between.
                    connections.Add(new GraphConnection(passNode.Id, $"out_{output.Id}", $"pass_{edge.ToPass}", $"in_{edge.Resource.Id}"));
                    consumedInView = true;
                }

                // A pure output - nothing in this view reads it back - gets its own loose node.
                if (!consumedInView)
                {
                    GraphNode resourceNode = GetOrAddResourceNode(nodes, resourceNodes, resourceColumnUsage, output, i);
                    connections.Add(new GraphConnection(passNode.Id, $"out_{output.Id}", resourceNode.Id, "in"));
                }
            }

            nodes.Add(passNode);
        }

        // Re-frame whenever the selected view changes, or the graph's own node/edge set changes
        // (fewer/more loose resource nodes as edges form) - not just on the first draw - so the
        // view always centers on what's actually on screen instead of a stale prior framing.
        int signature = HashCode.Combine(nodes.Count, connections.Count, passes.Count);
        if (!ReferenceEquals(_pipelineGraphFramedFor, view) || signature != _pipelineGraphFramedSignature)
        {
            _pipelineGraphController.FrameAll();
            _pipelineGraphFramedFor = view;
            _pipelineGraphFramedSignature = signature;
        }

        Origami.NodeGraph(paper, "rdp_vv_pipeline_graph", width - PipelineGraphPadding, PipelineGraphHeight)
            .Nodes(nodes)
            .ReadOnly()
            .Connections(connections)
            .Controller(_pipelineGraphController)
            .OnNodeDoubleClick(node =>
            {
                if (node.UserData is ProfiledPass pass)
                    PassSelected?.Invoke(view, pass);
            })
            .Show();
    }


    // Whether some earlier pass in this view already produced this resource - if so, the
    // consuming pass's input is satisfied by a direct edge instead of a loose resource node.
    private static bool IsEdgeInto(IReadOnlyList<PassEdge> edges, int toPass, uint resourceId)
    {
        foreach (PassEdge edge in edges)
        {
            if (edge.ToPass == toPass && edge.Resource.Id == resourceId)
                return true;
        }
        return false;
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
