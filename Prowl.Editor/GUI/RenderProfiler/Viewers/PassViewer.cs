using System.Collections.Generic;

using Prowl.Editor.GUI.RenderProfiler.Data;
using Prowl.Editor.GUI.Widgets;
using Prowl.Editor.Profiling;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.Editor.GUI.RenderProfiler.Viewers;

public sealed class ProfilerPassViewer
{
    private const float ResourceRowHeight = 26f;

    // Cleared whenever the selected pass changes, so a stale texture/buffer selection from a
    // previously-inspected pass doesn't linger under a pass that never touched that resource.
    private ProfiledPass? _lastPass;
    private ResourceRef? _selectedResource;
    private readonly Dictionary<string, bool> _resourceExpanded = new();


    public void Draw(Paper paper, ProfiledView? view, ProfiledPass? pass, IProfilerHistory history, bool isFullCapture)
    {
        if (view == null || pass == null)
        {
            Origami.Label(paper, "rdp_pass_empty", "No pass selected")
                .Muted()
                .Show();
            return;
        }

        if (!ReferenceEquals(_lastPass, pass))
        {
            _lastPass = pass;
            _selectedResource = null;
        }

        using (paper.Column("rdp_pass_viewer").Height(UnitValue.Auto).ColBetween(ViewerKit.SectionGap).Enter())
        {
            using (paper.Row("rdp_pass_header").Height(ViewerKit.SelectionViewerHeaderHeight).ColBetween(8f).Enter())
            {
                Origami.Label(paper, "rdp_pass_title", pass.Name)
                    .Heading()
                    .AlignLeft()
                    .Show();

                Origami.Label(paper, "rdp_pass_view", view.Name)
                    .Muted()
                    .AlignLeft()
                    .Show();

                paper.Box("rdp_pass_header_spacer");

                Origami.Label(paper, "rdp_pass_gpu", $"{pass.GpuMilliseconds:F2} ms")
                    .Muted()
                    .AlignRight()
                    .Show();
            }

            using (paper.Row("rdp_pass_io_row").Height(UnitValue.Auto).ColBetween(8f).Enter())
            {
                DrawResourceCard(paper, "rdp_pass_inputs", "Inputs", pass.Inputs);
                DrawResourceCard(paper, "rdp_pass_outputs", "Outputs", pass.Outputs);
            }

            if (isFullCapture && _selectedResource is { } selected)
            {
                if (selected.Kind == ResourceRefKind.Texture)
                    TextureViewer.Create(paper, "rdp_pass_texview", selected.Name).Show();
                else if (selected.Kind == ResourceRefKind.Buffer)
                    BufferViewer.Create(paper, "rdp_pass_bufview", selected.Name).Show();
            }

            ViewerKit.SectionCard(paper, "rdp_pv_objects", "Objects", () => DrawObjectsChart(paper, view.Name, pass.Index, history));
            ViewerKit.SectionCard(paper, "rdp_pv_renderops", "Render Operations", () => DrawRenderOperationsSection(paper, view.Name, pass, history));
        }
    }


    // ── Input/Output resource cards ────────────────────────────────

    private void DrawResourceCard(Paper paper, string id, string title, IReadOnlyList<ResourceRef> resources)
    {
        using (paper.Column(id + "_card")
            .Height(UnitValue.Auto)
            .BorderColor(EditorTheme.BorderStrong)
            .BorderWidth(1f)
            .Rounded(EditorTheme.Roundness)
            .Padding(8f)
            .ColBetween(6f)
            .Enter())
        {
            ViewerKit.SectionHeading(paper, id + "_hdr", title);

            if (resources.Count == 0)
            {
                Origami.Label(paper, id + "_empty", "None")
                    .Muted()
                    .SM()
                    .Show();
                return;
            }

            for (int i = 0; i < resources.Count; i++)
                DrawResourceRow(paper, $"{id}_r{i}", resources[i]);
        }
    }


    private void DrawResourceRow(Paper paper, string id, ResourceRef resource)
    {
        bool selected = _selectedResource is { } sel && sel.Id == resource.Id && sel.Kind == resource.Kind;

        // Textures back a render target - fold out to its attachments. Buffers/unknown resources have
        // no attachment hierarchy, so they're just a selectable row.
        if (resource.Kind == ResourceRefKind.Texture)
        {
            bool expanded = _resourceExpanded.TryGetValue(id, out bool e) && e;

            Origami.Foldout(paper, id, resource.Name)
                .Icon(EditorIcons.Image_I)
                .Badge("Texture")
                .HeaderBackground(selected ? EditorTheme.Selected : EditorTheme.Glass)
                .Expanded(expanded, v =>
                {
                    _resourceExpanded[id] = v;
                    _selectedResource = resource;
                })
                .Body(() => DrawTextureAttachments(paper, id));
            return;
        }

        var row = paper.Row(id)
            .Height(ResourceRowHeight)
            .Padding(8f, 0f)
            .Rounded(EditorTheme.Roundness)
            .BorderColor(EditorTheme.BorderSoft)
            .BorderWidth(1f)
            .BackgroundColor(selected ? EditorTheme.Selected : EditorTheme.Glass);

        if (resource.Kind == ResourceRefKind.Buffer)
        {
            row.Cursor(PaperCursor.Pointer);
            ResourceRef captured = resource;
            row.OnClick(_ => _selectedResource = captured);
        }

        using (row.Enter())
        {
            Origami.Label(paper, $"{id}_lbl", resource.Name)
                .LeadingIcon(resource.Kind == ResourceRefKind.Buffer ? EditorIcons.Database_I : EditorIcons.CircleQuestion_I, 14f)
                .AlignLeft()
                .Show();
        }
    }


    // Attachment enumeration needs the captured Snapshot's SnapshotResource, which the render profiler
    // panel doesn't retain yet - placeholder until that plumbing exists.
    private static void DrawTextureAttachments(Paper paper, string id)
    {
        Origami.Label(paper, $"{id}_attach_empty", "Attachments unavailable outside a snapshot")
            .Muted()
            .SM()
            .Show();
    }


    // ── Objects / render operations (mirrors ViewViewer's stats, scoped to this pass) ────────────

    private static void DrawObjectsChart(Paper paper, string viewName, int passIndex, IProfilerHistory history)
    {
        ViewerKit.DrawLineChart(paper, "rdp_pv_objects_chart", "Objects", "Count", ViewerKit.FormatCountCompact, UnitValue.Stretch(), ViewerKit.ChartHeight, true,
            ("Total", EditorTheme.Blue500, ViewerKit.PassSeries(history, viewName, passIndex, p => p.RegisteredObjects)),
            ("Drawn", EditorTheme.Green500, ViewerKit.PassSeries(history, viewName, passIndex, p => p.RenderedObjects)),
            ("Culled", EditorTheme.Red500, ViewerKit.PassSeries(history, viewName, passIndex, p => p.CulledObjects)));
    }


    private static void DrawRenderOperationsSection(Paper paper, string viewName, ProfiledPass pass, IProfilerHistory history)
    {
        using (paper.Column("rdp_pv_renderops_col").Height(UnitValue.Auto).ColBetween(ViewerKit.ChartRowGap).Enter())
        {
            DrawGeometryChart(paper, viewName, pass.Index, history);
            DrawRenderingChart(paper, viewName, pass.Index, history);
            DrawPixelProcessingChart(paper, viewName, pass.Index, history);
            DrawPipelineStateChart(paper, viewName, pass.Index, history);
        }
    }


    private static void DrawGeometryChart(Paper paper, string viewName, int passIndex, IProfilerHistory history)
    {
        ViewerKit.DrawLineChart(paper, "rdp_pv_geometry_chart", "Geometry", "Count", ViewerKit.FormatCountCompact, UnitValue.Stretch(), ViewerKit.ChartHeight, true,
            ("Triangles", EditorTheme.Blue500, ViewerKit.PassSeries(history, viewName, passIndex, p => p.TrianglesDrawn)),
            ("Vertices", EditorTheme.Purple500, ViewerKit.PassSeries(history, viewName, passIndex, p => p.InputAssemblyVertices)));
    }


    private static void DrawRenderingChart(Paper paper, string viewName, int passIndex, IProfilerHistory history)
    {
        ViewerKit.DrawLineChart(paper, "rdp_pv_rendering_chart", "Rendering", "Count", ViewerKit.FormatCountCompact, UnitValue.Stretch(), ViewerKit.ChartHeight, true,
            ("Draw Calls", EditorTheme.Green500, ViewerKit.PassSeries(history, viewName, passIndex, p => p.DrawCallCount)),
            ("Dispatches", EditorTheme.Red500, ViewerKit.PassSeries(history, viewName, passIndex, p => p.DispatchCallCount)));
    }


    private static void DrawPixelProcessingChart(Paper paper, string viewName, int passIndex, IProfilerHistory history)
    {
        ViewerKit.DrawLineChart(paper, "rdp_pv_pixelproc_chart", "Pixel Processing", "Count", ViewerKit.FormatCountCompact, UnitValue.Stretch(), ViewerKit.ChartHeight, false,
            ("Fragment Invocations", EditorTheme.Amber500, ViewerKit.PassSeries(history, viewName, passIndex, p => p.FragmentShaderInvocations)));
    }


    private static void DrawPipelineStateChart(Paper paper, string viewName, int passIndex, IProfilerHistory history)
    {
        ViewerKit.DrawLineChart(paper, "rdp_pv_pipelinestate_chart", "Pipeline State", "Count", ViewerKit.FormatCountCompact, UnitValue.Stretch(), ViewerKit.ChartHeight, true,
            ("Switches", EditorTheme.Purple500, ViewerKit.PassSeries(history, viewName, passIndex, p => p.PipelineSwitchCount)),
            ("Command Submits", EditorTheme.Blue500, ViewerKit.PassSeries(history, viewName, passIndex, p => p.CommandBuffers.Count)));
    }
}
