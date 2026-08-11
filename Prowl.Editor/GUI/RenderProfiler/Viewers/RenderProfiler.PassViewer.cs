using System.Collections.Generic;

using Prowl.Editor.GUI.Widgets;
using Prowl.Editor.Profiling;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.Editor.GUI.RenderProfiler;

public partial class RenderProfilerPanel
{
    private const float PassResourceRowHeight = 26f;

    // Cleared whenever the selected pass changes, so a stale texture/buffer selection from a
    // previously-inspected pass doesn't linger under a pass that never touched that resource.
    private ProfiledPass? _passViewerLastPass;
    private ResourceRef? _passSelectedResource;
    private readonly Dictionary<string, bool> _passResourceExpanded = new();


    private void DrawPassViewer(Paper paper)
    {
        ProfiledView? view = SelectedView;
        ProfiledPass? pass = SelectedPass;
        if (view == null || pass == null)
        {
            Origami.Label(paper, "rdp_pass_empty", "No pass selected")
                .Muted()
                .Show();
            return;
        }

        if (!ReferenceEquals(_passViewerLastPass, pass))
        {
            _passViewerLastPass = pass;
            _passSelectedResource = null;
        }

        bool isFullCapture = SelectedFrame?.HasCaptureDepth ?? false;

        using (paper.Column("rdp_pass_viewer").Height(UnitValue.Auto).ColBetween(SectionGap).Enter())
        {
            using (paper.Row("rdp_pass_header").Height(SelectionViewerHeaderHeight).ColBetween(8f).Enter())
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
                DrawPassResourceCard(paper, "rdp_pass_inputs", "Inputs", pass.Inputs);
                DrawPassResourceCard(paper, "rdp_pass_outputs", "Outputs", pass.Outputs);
            }

            if (isFullCapture && _passSelectedResource is { } selected)
            {
                if (selected.Kind == ResourceRefKind.Texture)
                    TextureViewer.Create(paper, "rdp_pass_texview", selected.Name).Show();
                else if (selected.Kind == ResourceRefKind.Buffer)
                    BufferViewer.Create(paper, "rdp_pass_bufview", selected.Name).Show();
            }

            SectionCard(paper, "rdp_pv_objects", "Objects", () => DrawPassObjectsChart(paper, view.Name, pass.Index));
            SectionCard(paper, "rdp_pv_renderops", "Render Operations", () => DrawPassRenderOperationsSection(paper, view.Name, pass));
        }
    }


    // ── Input/Output resource cards ────────────────────────────────

    private void DrawPassResourceCard(Paper paper, string id, string title, IReadOnlyList<ResourceRef> resources)
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
            SectionHeading(paper, id + "_hdr", title);

            if (resources.Count == 0)
            {
                Origami.Label(paper, id + "_empty", "None")
                    .Muted()
                    .SM()
                    .Show();
                return;
            }

            for (int i = 0; i < resources.Count; i++)
                DrawPassResourceRow(paper, $"{id}_r{i}", resources[i]);
        }
    }


    private void DrawPassResourceRow(Paper paper, string id, ResourceRef resource)
    {
        bool selected = _passSelectedResource is { } sel && sel.Id == resource.Id && sel.Kind == resource.Kind;

        // Textures back a render target - fold out to its attachments. Buffers/unknown resources have
        // no attachment hierarchy, so they're just a selectable row.
        if (resource.Kind == ResourceRefKind.Texture)
        {
            bool expanded = _passResourceExpanded.TryGetValue(id, out bool e) && e;

            Origami.Foldout(paper, id, resource.Name)
                .Icon(EditorIcons.Image_I)
                .Badge("Texture")
                .HeaderBackground(selected ? EditorTheme.Selected : EditorTheme.Glass)
                .Expanded(expanded, v =>
                {
                    _passResourceExpanded[id] = v;
                    _passSelectedResource = resource;
                })
                .Body(() => DrawPassTextureAttachments(paper, id));
            return;
        }

        var row = paper.Row(id)
            .Height(PassResourceRowHeight)
            .Padding(8f, 0f)
            .Rounded(EditorTheme.Roundness)
            .BorderColor(EditorTheme.BorderSoft)
            .BorderWidth(1f)
            .BackgroundColor(selected ? EditorTheme.Selected : EditorTheme.Glass);

        if (resource.Kind == ResourceRefKind.Buffer)
        {
            row.Cursor(PaperCursor.Pointer);
            ResourceRef captured = resource;
            row.OnClick(_ => _passSelectedResource = captured);
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
    private void DrawPassTextureAttachments(Paper paper, string id)
    {
        Origami.Label(paper, $"{id}_attach_empty", "Attachments unavailable outside a snapshot")
            .Muted()
            .SM()
            .Show();
    }


    // ── Objects / render operations (mirrors ViewViewer's stats, scoped to this pass) ────────────

    private void DrawPassObjectsChart(Paper paper, string viewName, int passIndex)
    {
        DrawLineChart(paper, "rdp_pv_objects_chart", "Objects", "Count", FormatCountCompact, UnitValue.Stretch(), ChartHeight, true,
            ("Total", EditorTheme.Blue500, PassSeries(viewName, passIndex, p => p.RegisteredObjects)),
            ("Drawn", EditorTheme.Green500, PassSeries(viewName, passIndex, p => p.RenderedObjects)),
            ("Culled", EditorTheme.Red500, PassSeries(viewName, passIndex, p => p.CulledObjects)));
    }


    private void DrawPassRenderOperationsSection(Paper paper, string viewName, ProfiledPass pass)
    {
        using (paper.Column("rdp_pv_renderops_col").Height(UnitValue.Auto).ColBetween(ChartRowGap).Enter())
        {
            DrawPassGeometryChart(paper, viewName, pass.Index);
            DrawPassRenderingChart(paper, viewName, pass.Index);
            DrawPassPixelProcessingChart(paper, viewName, pass.Index);
            DrawPassPipelineStateChart(paper, viewName, pass.Index);
        }
    }


    private void DrawPassGeometryChart(Paper paper, string viewName, int passIndex)
    {
        DrawLineChart(paper, "rdp_pv_geometry_chart", "Geometry", "Count", FormatCountCompact, UnitValue.Stretch(), ChartHeight, true,
            ("Triangles", EditorTheme.Blue500, PassSeries(viewName, passIndex, p => p.TrianglesDrawn)),
            ("Vertices", EditorTheme.Purple500, PassSeries(viewName, passIndex, p => p.InputAssemblyVertices)));
    }


    private void DrawPassRenderingChart(Paper paper, string viewName, int passIndex)
    {
        DrawLineChart(paper, "rdp_pv_rendering_chart", "Rendering", "Count", FormatCountCompact, UnitValue.Stretch(), ChartHeight, true,
            ("Draw Calls", EditorTheme.Green500, PassSeries(viewName, passIndex, p => p.DrawCallCount)),
            ("Dispatches", EditorTheme.Red500, PassSeries(viewName, passIndex, p => p.DispatchCallCount)));
    }


    private void DrawPassPixelProcessingChart(Paper paper, string viewName, int passIndex)
    {
        DrawLineChart(paper, "rdp_pv_pixelproc_chart", "Pixel Processing", "Count", FormatCountCompact, UnitValue.Stretch(), ChartHeight, false,
            ("Fragment Invocations", EditorTheme.Amber500, PassSeries(viewName, passIndex, p => p.FragmentShaderInvocations)));
    }


    private void DrawPassPipelineStateChart(Paper paper, string viewName, int passIndex)
    {
        DrawLineChart(paper, "rdp_pv_pipelinestate_chart", "Pipeline State", "Count", FormatCountCompact, UnitValue.Stretch(), ChartHeight, true,
            ("Switches", EditorTheme.Purple500, PassSeries(viewName, passIndex, p => p.PipelineSwitchCount)),
            ("Command Submits", EditorTheme.Blue500, PassSeries(viewName, passIndex, p => p.CommandBuffers.Count)));
    }
}
