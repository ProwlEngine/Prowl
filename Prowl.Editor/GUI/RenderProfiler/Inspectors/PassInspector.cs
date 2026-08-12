using System;
using System.Collections.Generic;

using Prowl.Editor.GUI.RenderProfiler.Data;
using Prowl.Editor.GUI.Widgets;
using Prowl.Editor.Profiling;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.GUI.RenderProfiler.Inspectors;

public sealed class ProfilerPassInspector : IDisposable
{
    private const float ResourceRowHeight = 26f;

    // Cleared whenever the selected pass changes, so a stale texture/buffer selection from a
    // previously-inspected pass doesn't linger under a pass that never touched that resource.
    private ProfiledPass? _lastPass;
    private ResourceRef? _selectedResource;
    private string? _selectedSubTextureName;
    private readonly Dictionary<string, bool> _resourceExpanded = new();

    // GPU textures built from captured pixel bytes are expensive to rebuild every frame, so they're
    // cached per (resource, version, subtexture) and disposed whenever the selected pass changes.
    private readonly Dictionary<(uint ResourceId, uint Version, string SubName), Texture2D> _textureCache = new();


    public void Draw(Paper paper, ProfiledView? view, ProfiledPass? pass, IProfilerHistory history, ISnapshotResourceResolver? resolver)
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
            _selectedSubTextureName = null;
            ClearTextureCache();
        }

        using (paper.Column("rdp_pass_viewer").Height(UnitValue.Auto).ColBetween(InspectorKit.SectionGap).Enter())
        {
            using (paper.Row("rdp_pass_header").Height(InspectorKit.SelectionViewerHeaderHeight).ColBetween(8f).Enter())
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
                DrawResourceCard(paper, "rdp_pass_inputs", "Inputs", pass.Inputs, resolver);
                DrawResourceCard(paper, "rdp_pass_outputs", "Outputs", pass.Outputs, resolver);
            }

            if (resolver != null && _selectedResource is { Kind: ResourceRefKind.Buffer } selectedBuffer)
            {
                SnapshotResourceVersion? version = FindVersion(resolver.Resolve(selectedBuffer.Resource), selectedBuffer.Resource.Version);
                BufferViewer.Create(paper, "rdp_pass_bufview", selectedBuffer.Name)
                    .Data(version?.BufferData ?? Array.Empty<byte>(), version?.BufferMeta)
                    .Show();
            }
            else if (resolver != null && _selectedResource is { Kind: ResourceRefKind.Texture } selectedTexture && _selectedSubTextureName != null)
            {
                SnapshotResourceVersion? version = FindVersion(resolver.Resolve(selectedTexture.Resource), selectedTexture.Resource.Version);
                SnapshotSubTexture? sub = FindSubTexture(version, _selectedSubTextureName);
                if (sub != null)
                {
                    Texture2D texture = GetOrCreateTexture(selectedTexture.Resource.ResourceId, selectedTexture.Resource.Version, sub.Value);
                    TextureViewer.Create(paper, "rdp_pass_texview", sub.Value.Name)
                        .Data(texture, sub.Value.Width, sub.Value.Height, sub.Value.Format.ToString())
                        .Show();
                }
            }

            InspectorKit.SectionCard(paper, "rdp_pv_objects", "Objects", () => DrawObjectsChart(paper, view.Name, pass.Index, history));
            InspectorKit.SectionCard(paper, "rdp_pv_renderops", "Render Operations", () => DrawRenderOperationsSection(paper, view.Name, pass, history));
        }
    }


    public void Dispose() => ClearTextureCache();


    private void ClearTextureCache()
    {
        foreach (Texture2D texture in _textureCache.Values)
            texture.Dispose();
        _textureCache.Clear();
    }


    private Texture2D GetOrCreateTexture(uint resourceId, uint version, SnapshotSubTexture sub)
    {
        (uint resourceId, uint version, string Name) key = (resourceId, version, sub.Name);
        if (_textureCache.TryGetValue(key, out Texture2D? existing))
            return existing;

        var texture = new Texture2D(sub.Width, sub.Height, false, sub.Format);
        texture.SetData<byte>(sub.Pixels);
        _textureCache[key] = texture;
        return texture;
    }


    // ── Input/Output resource cards ────────────────────────────────

    private void DrawResourceCard(Paper paper, string id, string title, IReadOnlyList<ResourceRef> resources, ISnapshotResourceResolver? resolver)
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
            InspectorKit.SectionHeading(paper, id + "_hdr", title);

            if (resources.Count == 0)
            {
                Origami.Label(paper, id + "_empty", "None")
                    .Muted()
                    .SM()
                    .Show();
                return;
            }

            for (int i = 0; i < resources.Count; i++)
                DrawResourceRow(paper, $"{id}_r{i}", resources[i], resolver);
        }
    }


    private void DrawResourceRow(Paper paper, string id, ResourceRef resource, ISnapshotResourceResolver? resolver)
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
                .Expanded(expanded, v => _resourceExpanded[id] = v)
                .Body(() => DrawTextureAttachments(paper, id, resource, resolver));
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


    private void DrawTextureAttachments(Paper paper, string id, ResourceRef resource, ISnapshotResourceResolver? resolver)
    {
        SnapshotResourceVersion? version = FindVersion(resolver?.Resolve(resource.Resource), resource.Resource.Version);

        if (version == null || version.Subtextures.Count == 0)
        {
            Origami.Label(paper, $"{id}_attach_empty", "Attachments unavailable outside a snapshot")
                .Muted()
                .SM()
                .Show();
            return;
        }

        for (int i = 0; i < version.Subtextures.Count; i++)
            DrawSubTextureRow(paper, $"{id}_attach{i}", resource, version.Subtextures[i]);
    }


    private void DrawSubTextureRow(Paper paper, string id, ResourceRef resource, SnapshotSubTexture sub)
    {
        bool selected = _selectedResource is { } sel && sel.Id == resource.Id && sel.Kind == resource.Kind
            && _selectedSubTextureName == sub.Name;

        var row = paper.Row(id)
            .Height(ResourceRowHeight)
            .Padding(8f, 0f)
            .Rounded(EditorTheme.Roundness)
            .BorderColor(EditorTheme.BorderSoft)
            .BorderWidth(1f)
            .BackgroundColor(selected ? EditorTheme.Selected : EditorTheme.Glass)
            .Cursor(PaperCursor.Pointer);

        ResourceRef captured = resource;
        string subName = sub.Name;
        row.OnClick(_ =>
        {
            _selectedResource = captured;
            _selectedSubTextureName = subName;
        });

        using (row.Enter())
        {
            Origami.Label(paper, $"{id}_lbl", sub.Name)
                .LeadingIcon(EditorIcons.Image_I, 14f)
                .AlignLeft()
                .Show();
        }
    }


    private static SnapshotResourceVersion? FindVersion(SnapshotResource? resource, uint version)
    {
        if (resource == null)
            return null;

        foreach (SnapshotResourceVersion v in resource.Versions)
        {
            if (v.Version == version)
                return v;
        }
        return null;
    }


    private static SnapshotSubTexture? FindSubTexture(SnapshotResourceVersion? version, string name)
    {
        if (version == null)
            return null;

        foreach (SnapshotSubTexture sub in version.Subtextures)
        {
            if (sub.Name == name)
                return sub;
        }
        return null;
    }


    // ── Objects / render operations (mirrors ProfilerViewInspector's stats, scoped to this pass) ──

    private static void DrawObjectsChart(Paper paper, string viewName, int passIndex, IProfilerHistory history)
    {
        InspectorKit.DrawChart(paper, history, "rdp_pv_objects_chart", "Objects", "Count", InspectorKit.FormatCountCompact, UnitValue.Stretch(), InspectorKit.ChartHeight, true,
            ("Total", EditorTheme.Blue500, InspectorKit.PassSeries(history, viewName, passIndex, p => p.RegisteredObjects)),
            ("Drawn", EditorTheme.Green500, InspectorKit.PassSeries(history, viewName, passIndex, p => p.RenderedObjects)),
            ("Culled", EditorTheme.Red500, InspectorKit.PassSeries(history, viewName, passIndex, p => p.CulledObjects)));
    }


    private static void DrawRenderOperationsSection(Paper paper, string viewName, ProfiledPass pass, IProfilerHistory history)
    {
        using (paper.Column("rdp_pv_renderops_col").Height(UnitValue.Auto).ColBetween(InspectorKit.ChartRowGap).Enter())
        {
            DrawGeometryChart(paper, viewName, pass.Index, history);
            DrawRenderingChart(paper, viewName, pass.Index, history);
            DrawPixelProcessingChart(paper, viewName, pass.Index, history);
            DrawPipelineStateChart(paper, viewName, pass.Index, history);
        }
    }


    private static void DrawGeometryChart(Paper paper, string viewName, int passIndex, IProfilerHistory history)
    {
        InspectorKit.DrawChart(paper, history, "rdp_pv_geometry_chart", "Geometry", "Count", InspectorKit.FormatCountCompact, UnitValue.Stretch(), InspectorKit.ChartHeight, true,
            ("Triangles", EditorTheme.Blue500, InspectorKit.PassSeries(history, viewName, passIndex, p => p.TrianglesDrawn)),
            ("Vertices", EditorTheme.Purple500, InspectorKit.PassSeries(history, viewName, passIndex, p => p.InputAssemblyVertices)));
    }


    private static void DrawRenderingChart(Paper paper, string viewName, int passIndex, IProfilerHistory history)
    {
        InspectorKit.DrawChart(paper, history, "rdp_pv_rendering_chart", "Rendering", "Count", InspectorKit.FormatCountCompact, UnitValue.Stretch(), InspectorKit.ChartHeight, true,
            ("Draw Calls", EditorTheme.Green500, InspectorKit.PassSeries(history, viewName, passIndex, p => p.DrawCallCount)),
            ("Dispatches", EditorTheme.Red500, InspectorKit.PassSeries(history, viewName, passIndex, p => p.DispatchCallCount)));
    }


    private static void DrawPixelProcessingChart(Paper paper, string viewName, int passIndex, IProfilerHistory history)
    {
        InspectorKit.DrawChart(paper, history, "rdp_pv_pixelproc_chart", "Pixel Processing", "Count", InspectorKit.FormatCountCompact, UnitValue.Stretch(), InspectorKit.ChartHeight, false,
            ("Fragment Invocations", EditorTheme.Amber500, InspectorKit.PassSeries(history, viewName, passIndex, p => p.FragmentShaderInvocations)));
    }


    private static void DrawPipelineStateChart(Paper paper, string viewName, int passIndex, IProfilerHistory history)
    {
        InspectorKit.DrawChart(paper, history, "rdp_pv_pipelinestate_chart", "Pipeline State", "Count", InspectorKit.FormatCountCompact, UnitValue.Stretch(), InspectorKit.ChartHeight, true,
            ("Switches", EditorTheme.Purple500, InspectorKit.PassSeries(history, viewName, passIndex, p => p.PipelineSwitchCount)),
            ("Command Submits", EditorTheme.Blue500, InspectorKit.PassSeries(history, viewName, passIndex, p => p.CommandBuffers.Count)));
    }
}
