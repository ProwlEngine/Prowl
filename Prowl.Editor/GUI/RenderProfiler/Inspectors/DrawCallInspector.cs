using System;
using System.Collections.Generic;

using Prowl.Editor.GUI.RenderProfiler.Data;
using Prowl.Editor.GUI.Widgets;
using Prowl.Editor.Profiling;
using Prowl.Editor.Theming;
using Prowl.Graphite;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.Editor.GUI.RenderProfiler.Inspectors;

// Snapshot-only: draw calls and their reference buffers only exist on a captured frame.
public sealed class ProfilerDrawCallInspector
{
    private const float ResourceRowHeight = 26f;
    private const float ResourceTreeHeight = 180f;
    private const float ResourceTreeIndentSize = 0f;

    // Cleared whenever the selected draw changes, so a stale buffer selection from a
    // previously-inspected draw doesn't linger under a draw that never touched that buffer.
    private ProfiledPipeline? _lastPipeline;
    private int _lastDrawIndex = -1;
    private int? _selectedBufferIndex;
    private readonly Dictionary<string, bool> _bufferExpanded = new();


    public void Draw(Paper paper, ProfiledView? view, ProfiledPass? pass, ProfiledCommandBuffer? commandBuffer,
        ProfiledPipeline? pipeline, int drawIndex, ISnapshotResourceResolver? resolver, float width)
    {
        if (pipeline == null || drawIndex < 0 || drawIndex >= pipeline.Draws.Count)
        {
            EditorGUI.EmptyState(paper, "rdp_dc_empty", "No draw call selected", EditorTheme.DefaultFont!);
            return;
        }

        ProfiledDrawCall draw = pipeline.Draws[drawIndex];
        if (!ReferenceEquals(_lastPipeline, pipeline) || _lastDrawIndex != drawIndex)
        {
            _lastPipeline = pipeline;
            _lastDrawIndex = drawIndex;
            _selectedBufferIndex = null;
        }

        using (paper.Column("rdp_dc_viewer").Height(UnitValue.Auto).ColBetween(InspectorKit.SectionGap).Enter())
        {
            DrawHeader(paper, view, pass, commandBuffer, draw);

            float rowWidth = (width - 8f) / 2f;
            using (paper.Row("rdp_dc_row").Height(UnitValue.Auto).ColBetween(8f).Enter())
            {
                using (paper.Column("rdp_dc_info").Width(rowWidth).Height(UnitValue.Auto).ColBetween(2f).Enter())
                {
                    if (draw.Draw is { } d)
                        DrawDrawInfo(paper, "rdp_dc_draw", d);
                    else if (draw.Dispatch is { } dispatch)
                        DrawDispatchInfo(paper, "rdp_dc_dispatch", dispatch);

                    EditorGUI.StatRow(paper, "rdp_dc_culled", "Culled", draw.Culled ? "Yes" : "No");
                }

                DrawResourceCard(paper, "rdp_dc_buffers", "Buffers", draw, resolver, rowWidth);
            }

            if (resolver != null && _selectedBufferIndex is { } selectedIndex
                && selectedIndex >= 0 && selectedIndex < draw.ReferenceBuffers.Length)
            {
                ReferenceBuffer buffer = draw.ReferenceBuffers[selectedIndex];
                SnapshotResourceVersion? version = FindVersion(resolver.Resolve(buffer.Resource), buffer.Resource.Version);
                BufferViewer.Create(paper, "rdp_dc_bufview", buffer.Name)
                    .Data(version?.BufferData ?? Array.Empty<byte>(), version?.BufferMeta)
                    .Show();
            }
        }
    }


    private static void DrawHeader(Paper paper, ProfiledView? view, ProfiledPass? pass,
        ProfiledCommandBuffer? commandBuffer, ProfiledDrawCall draw)
    {
        using (paper.Row("rdp_dc_header").Height(InspectorKit.SelectionViewerHeaderHeight).ColBetween(8f).Enter())
        {
            Origami.Label(paper, "rdp_dc_title", draw.Draw != null ? draw.Draw.Value.Kind.ToString() : "Dispatch")
                .Heading()
                .AlignLeft()
                .Show();

            if (view != null)
            {
                Origami.Label(paper, "rdp_dc_view", view.Name)
                    .Muted()
                    .AlignLeft()
                    .Show();
            }

            if (pass != null)
            {
                Origami.Label(paper, "rdp_dc_pass", pass.Name)
                    .Muted()
                    .AlignLeft()
                    .Show();
            }

            if (commandBuffer != null)
            {
                Origami.Label(paper, "rdp_dc_cmd", commandBuffer.Name)
                    .Muted()
                    .AlignLeft()
                    .Show();
            }

            paper.Box("rdp_dc_header_spacer");

            Origami.Label(paper, "rdp_dc_culled_badge", draw.Culled ? "Culled" : "Issued")
                .Variant(draw.Culled ? OrigamiVariant.Subtle : OrigamiVariant.Success)
                .AlignRight()
                .Show();
        }
    }


    private static void DrawDrawInfo(Paper paper, string id, DrawCallInfo info)
    {
        EditorGUI.StatRow(paper, $"{id}_kind", "Kind", info.Kind.ToString());
        EditorGUI.StatRow(paper, $"{id}_topology", "Topology", info.Topology.ToString());
        EditorGUI.StatRow(paper, $"{id}_count", "Vertex/Index Count", InspectorKit.FormatCountCompact(info.VertexOrIndexCount));
        EditorGUI.StatRow(paper, $"{id}_instances", "Instance Count", InspectorKit.FormatCountCompact(info.InstanceCount));
        EditorGUI.StatRow(paper, $"{id}_draws", "Draw Count", InspectorKit.FormatCountCompact(info.DrawCount));
        EditorGUI.StatRow(paper, $"{id}_indirect", "Indirect", info.IsIndirect ? "Yes" : "No");
    }


    private static void DrawDispatchInfo(Paper paper, string id, DispatchCallInfo info)
    {
        EditorGUI.StatRow(paper, $"{id}_gx", "Group Count X", InspectorKit.FormatCountCompact(info.GroupCountX));
        EditorGUI.StatRow(paper, $"{id}_gy", "Group Count Y", InspectorKit.FormatCountCompact(info.GroupCountY));
        EditorGUI.StatRow(paper, $"{id}_gz", "Group Count Z", InspectorKit.FormatCountCompact(info.GroupCountZ));
        EditorGUI.StatRow(paper, $"{id}_indirect", "Indirect", info.IsIndirect ? "Yes" : "No");
    }


    // ── Reference buffer hierarchy ─────────────────────────────────

    private void DrawResourceCard(Paper paper, string id, string title, ProfiledDrawCall draw,
        ISnapshotResourceResolver? resolver, float width)
    {
        EditorGUI.Group(paper, id, title, () =>
        {
            if (draw.ReferenceBuffers.Length == 0)
            {
                EditorGUI.EmptyState(paper, id + "_empty", "No buffers", EditorTheme.DefaultFont!);
                return;
            }

            var nodes = new List<TreeNode>();
            for (int i = 0; i < draw.ReferenceBuffers.Length; i++)
            {
                ReferenceBuffer buffer = draw.ReferenceBuffers[i];
                nodes.Add(new TreeNode
                {
                    Id = $"{id}_r{i}",
                    Label = buffer.Name,
                    Icon = EditorIcons.Database,
                    IsLeaf = true,
                    Depth = 0,
                    UserData = i,
                });
            }

            float treeWidth = MathF.Max(120f, width - 16f);

            Origami.Tree(paper, id + "_tree", treeWidth, ResourceTreeHeight)
                .Nodes(nodes)
                .RowHeight(ResourceRowHeight)
                .IndentSize(ResourceTreeIndentSize)
                .IsSelected(n => n.UserData is int idx && _selectedBufferIndex == idx)
                .OnSelect(e =>
                {
                    if (e.Node.UserData is int idx)
                        _selectedBufferIndex = idx;
                })
                .ExpandStateSink(_bufferExpanded)
                .Show();
        });
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
}