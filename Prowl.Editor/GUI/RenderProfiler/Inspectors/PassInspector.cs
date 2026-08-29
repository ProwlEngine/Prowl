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
    private const float ResourceTreeHeight = 240f;
    private const float ResourceTreeIndentSize = 10f;

    // UserData carried by every node in the Inputs/Outputs resource trees. SubTextureName is null for
    // a resource's own root row (and for Buffer/Unknown rows, which have no children); set for a
    // texture's attachment leaves.
    private readonly record struct ResourceTreeNode(ResourceRef Resource, string? SubTextureName);

    // Cleared whenever the selected pass changes, so a stale texture/buffer selection from a
    // previously-inspected pass doesn't linger under a pass that never touched that resource.
    private ProfiledPass? _lastPass;
    private ResourceRef? _selectedResource;
    private string? _selectedSubTextureName;
    private readonly Dictionary<string, bool> _resourceExpanded = new();

    // GPU textures built from captured pixel bytes are expensive to rebuild every frame, so they're
    // cached per (resource, version, subtexture) and disposed whenever the selected pass changes.
    private readonly Dictionary<(uint ResourceId, uint Version, string SubName), Texture2D> _textureCache = new();


    public void Draw(Paper paper, ProfiledView? view, ProfiledPass? pass, IProfilerHistory history, ISnapshotResourceResolver? resolver, float width)
    {
        if (view == null || pass == null)
        {
            EditorGUI.EmptyState(paper, "rdp_pass_empty", "No pass selected", EditorTheme.DefaultFont!);
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

            float resourceCardWidth = (width - 8f) / 2f;
            using (paper.Row("rdp_pass_io_row").Height(UnitValue.Auto).ColBetween(8f).Enter())
            {
                DrawResourceCard(paper, "rdp_pass_inputs", "Inputs", pass.Inputs, resolver, resourceCardWidth);
                DrawResourceCard(paper, "rdp_pass_outputs", "Outputs", pass.Outputs, resolver, resourceCardWidth);
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

    // Textures and buffers used to render as two visually different widgets (a foldout with a badge
    // vs. a plain selectable row) - both now go through the same hierarchy (Tree) widget, so a
    // texture's attachments nest as children instead of the two kinds looking unrelated.
    private void DrawResourceCard(Paper paper, string id, string title, IReadOnlyList<ResourceRef> resources, ISnapshotResourceResolver? resolver, float width)
    {
        EditorGUI.Group(paper, id, title, () =>
        {
            if (resources.Count == 0)
            {
                EditorGUI.EmptyState(paper, id + "_empty", "No resources", EditorTheme.DefaultFont!);
                return;
            }

            var nodes = new List<TreeNode>();
            for (int i = 0; i < resources.Count; i++)
                BuildResourceNodes(nodes, $"{id}_r{i}", resources[i], resolver);

            float treeWidth = MathF.Max(120f, width - 16f);

            Origami.Tree(paper, id + "_tree", treeWidth, ResourceTreeHeight)
                .Nodes(nodes)
                .RowHeight(ResourceRowHeight)
                .IndentSize(ResourceTreeIndentSize)
                .IsSelected(n => n.UserData is ResourceTreeNode rn && IsResourceNodeSelected(rn))
                .OnSelect(e =>
                {
                    if (e.Node.UserData is ResourceTreeNode rn)
                        SelectResourceNode(rn);
                })
                .ExpandStateSink(_resourceExpanded)
                .Show();
        });
    }


    private void BuildResourceNodes(List<TreeNode> nodes, string rootId, ResourceRef resource, ISnapshotResourceResolver? resolver)
    {
        // Textures back a render target - fold out to its attachments. Buffers/unknown resources have
        // no attachment hierarchy, so they're just a selectable leaf.
        if (resource.Kind == ResourceRefKind.Texture)
        {
            nodes.Add(new TreeNode
            {
                Id = rootId,
                Label = resource.Name,
                Icon = EditorIcons.Image,
                Badge = "Texture",
                HasChildren = true,
                Depth = 0,
                UserData = new ResourceTreeNode(resource, null),
            });

            SnapshotResourceVersion? version = FindVersion(resolver?.Resolve(resource.Resource), resource.Resource.Version);
            if (version == null || version.Subtextures.Count == 0)
            {
                nodes.Add(new TreeNode
                {
                    Id = $"{rootId}_empty",
                    Label = "Attachments unavailable outside a snapshot",
                    LabelColor = EditorTheme.Ink300,
                    IsLeaf = true,
                    Disabled = true,
                    Depth = 1,
                    UserData = new ResourceTreeNode(resource, null),
                });
                return;
            }

            for (int i = 0; i < version.Subtextures.Count; i++)
            {
                SnapshotSubTexture sub = version.Subtextures[i];
                nodes.Add(new TreeNode
                {
                    Id = $"{rootId}_s{i}",
                    Label = sub.Name,
                    Icon = EditorIcons.Image,
                    IsLeaf = true,
                    Depth = 1,
                    UserData = new ResourceTreeNode(resource, sub.Name),
                });
            }
            return;
        }

        nodes.Add(new TreeNode
        {
            Id = rootId,
            Label = resource.Name,
            Icon = resource.Kind == ResourceRefKind.Buffer ? EditorIcons.Database : EditorIcons.CircleQuestion,
            IsLeaf = true,
            Disabled = resource.Kind != ResourceRefKind.Buffer,
            Depth = 0,
            UserData = new ResourceTreeNode(resource, null),
        });
    }


    private bool IsResourceNodeSelected(ResourceTreeNode node)
    {
        if (_selectedResource is not { } sel || sel.Id != node.Resource.Id || sel.Kind != node.Resource.Kind)
            return false;

        return node.Resource.Kind == ResourceRefKind.Buffer
            ? _selectedSubTextureName == null
            : _selectedSubTextureName == node.SubTextureName && node.SubTextureName != null;
    }


    private void SelectResourceNode(ResourceTreeNode node)
    {
        if (node.Resource.Kind == ResourceRefKind.Buffer)
        {
            _selectedResource = node.Resource;
            _selectedSubTextureName = null;
        }
        else if (node.SubTextureName != null)
        {
            _selectedResource = node.Resource;
            _selectedSubTextureName = node.SubTextureName;
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
