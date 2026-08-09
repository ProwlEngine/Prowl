using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Prowl.Editor.Profiling;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GUI.RenderProfiler;

public partial class RenderProfilerPanel
{
    private const float HierarchyPanelWidth = 340f;
    private const float HierarchyRowHeight = 22f;
    private const float HierarchyPingDuration = 1.5f;
    private const float HierarchyPingFadeStart = 0.5f;
    private const float HierarchyTitleHeight = 24f;
    private const float HierarchyTitlePadTop = 6f;
    private const float HierarchyTitlePadLeft = 8f;

    // Not a real hierarchy node (it isn't in the tree's node list), but the frame title label is
    // still selectable/pingable, so it gets its own sentinel id for the shared ping state.
    private const string FrameHierarchyNodeId = "__frame__";

    private enum HierarchyKind { View, Pass, CommandBuffer, Pipeline, Object, DrawCall }

    private readonly record struct HierarchyUserData(
        HierarchyKind Kind,
        ProfiledView? View,
        ProfiledPass? Pass,
        ProfiledCommandBuffer? CommandBuffer,
        ProfiledPipeline? Pipeline,
        ProfiledCallingObject? Object,
        int DrawIndex);

    private readonly Dictionary<string, bool> _hierarchyExpandState = [];
    private readonly HashSet<string> _hierarchyForceExpandIds = [];
    private string? _pingedHierarchyNodeId;
    private float _hierarchyPingTimer;
    private bool _hierarchyScrollPending;


    private void DrawHierarchy(Paper paper, float width, float height)
    {
        ProfiledFrame? frame = SelectedFrame;

        using (paper.Column("rdp_hierarchy").Width(width).Height(height).Enter())
        {
            DrawHierarchyTitle(paper, frame);

            var nodes = new List<TreeNode>();
            if (frame != null)
                BuildHierarchyNodes(frame, nodes);

            TickHierarchyPing();

            float treeHeight = height - HierarchyTitlePadTop - HierarchyTitleHeight;
            Origami.Tree(paper, "rdp_hier_tree", width, treeHeight)
                .Nodes(nodes)
                .RowHeight(HierarchyRowHeight)
                .IsSelected(n => IsHierarchyNodeSelected((HierarchyUserData)n.UserData!))
                .OnSelect(e => OnHierarchyNodeSelected((HierarchyUserData)e.Node.UserData!))
                .IsPinged(n => _pingedHierarchyNodeId == n.Id)
                .PingAlpha(GetHierarchyPingAlpha)
                .ExpandStateSink(_hierarchyExpandState)
                .EmptyMessage("No frame selected")
                .Show();

            if (_hierarchyScrollPending && _pingedHierarchyNodeId != null)
            {
                int pingIndex = nodes.FindIndex(n => n.Id == _pingedHierarchyNodeId);
                if (pingIndex >= 0)
                {
                    float rowTotal = HierarchyRowHeight + 2f;
                    float targetY = pingIndex * rowTotal - (treeHeight * 0.5f) + rowTotal * 0.5f;
                    Origami.ScrollTo("rdp_hier_tree_scroll", new Float2(0, targetY));
                }
                _hierarchyScrollPending = false;
            }
        }
    }


    private void DrawHierarchyTitle(Paper paper, ProfiledFrame? frame)
    {
        bool isSelected = SelectionType == ProfilerSelectionType.Frame;
        bool isPinged = _pingedHierarchyNodeId == FrameHierarchyNodeId;

        ElementBuilder row = paper.Row("rdp_hierarchy_title_row")
            .Height(HierarchyTitleHeight)
            .Width(UnitValue.Stretch())
            .Margin(HierarchyTitlePadLeft, HierarchyTitlePadTop, 0, 0)
            .Rounded(4);

        if (isSelected)
            row.BackgroundColor(EditorTheme.Hover);

        if (frame != null)
        {
            row.Cursor(PaperCursor.Pointer)
                .OnClick(e =>
                {
                    e.StopPropagation();
                    ClearSubFrameSelection();
                });
        }

        if (isPinged)
        {
            row.OnPostLayout((handle, rect) =>
            {
                float alpha = GetHierarchyPingAlpha();
                if (alpha <= 0f)
                    return;

                paper.Draw(ref handle, (canvas, r) =>
                {
                    int fillA = (int)(alpha * 60);
                    int borderA = (int)(alpha * 200);
                    Color fillColor = Color.FromArgb(fillA, 255, 220, 50);
                    Color borderColor = Color.FromArgb(borderA, 255, 200, 0);
                    float x = r.Min.X, y = r.Min.Y, w = r.Size.X, h = r.Size.Y;
                    canvas.RoundedRectFilled(x, y, w, h, 4, 4, 4, 4, fillColor);
                    canvas.SetStrokeColor(borderColor);
                    canvas.SetStrokeWidth(2f);
                    canvas.BeginPath();
                    canvas.RoundedRect(x + 1f, y + 1f, w - 2f, h - 2f, 3, 3, 3, 3);
                    canvas.Stroke();
                });
            });
        }

        using (row.Enter())
        {
            Origami.Label(paper, "rdp_hierarchy_title_label", frame != null ? $"Frame {frame.FrameIndex} ({frame.GpuMilliseconds:F2} ms)" : "No frame selected")
                .Height(HierarchyTitleHeight)
                .AlignLeft()
                .Show();
        }
    }


    private void BuildHierarchyNodes(ProfiledFrame frame, List<TreeNode> nodes)
    {
        foreach (ProfiledView view in frame.Views)
            BuildViewNode(view, 0, nodes);
    }


    private void BuildViewNode(ProfiledView view, int depth, List<TreeNode> nodes)
    {
        string id = NodeId(view);
        nodes.Add(new TreeNode
        {
            Id = id,
            Label = $"{view.Name} ({view.GpuMilliseconds:F2} ms)",
            HasChildren = view.Passes.Count > 0,
            Depth = depth,
            UserData = new HierarchyUserData(HierarchyKind.View, view, null, null, null, null, -1),
            OverrideExpanded = ForceExpandedOrNull(id),
        });

        foreach (ProfiledPass pass in view.Passes)
            BuildPassNode(view, pass, depth + 1, nodes);
    }


    private void BuildPassNode(ProfiledView view, ProfiledPass pass, int depth, List<TreeNode> nodes)
    {
        string id = NodeId(pass);
        nodes.Add(new TreeNode
        {
            Id = id,
            Label = $"{pass.Name} ({pass.GpuMilliseconds:F2} ms)",
            HasChildren = pass.CommandBuffers.Count > 0,
            Depth = depth,
            UserData = new HierarchyUserData(HierarchyKind.Pass, view, pass, null, null, null, -1),
            OverrideExpanded = ForceExpandedOrNull(id),
        });

        foreach (ProfiledCommandBuffer commandBuffer in pass.CommandBuffers)
            BuildCommandBufferNode(view, pass, commandBuffer, depth + 1, nodes);
    }


    private void BuildCommandBufferNode(ProfiledView view, ProfiledPass pass, ProfiledCommandBuffer commandBuffer, int depth, List<TreeNode> nodes)
    {
        bool isFullCapture = SelectedFrame?.HasCaptureDepth ?? false;
        bool hasChildren = isFullCapture && commandBuffer.Switches.Count > 0;

        string id = NodeId(commandBuffer);
        nodes.Add(new TreeNode
        {
            Id = id,
            Label = $"{commandBuffer.Name} ({commandBuffer.GpuMilliseconds:F2} ms)",
            HasChildren = hasChildren,
            IsLeaf = !isFullCapture,
            Depth = depth,
            UserData = new HierarchyUserData(HierarchyKind.CommandBuffer, view, pass, commandBuffer, null, null, -1),
            OverrideExpanded = ForceExpandedOrNull(id),
        });

        if (!isFullCapture)
            return;

        foreach (ProfiledPipeline pipeline in commandBuffer.Switches)
            BuildPipelineNode(view, pass, commandBuffer, pipeline, depth + 1, nodes);
    }


    private void BuildPipelineNode(ProfiledView view, ProfiledPass pass, ProfiledCommandBuffer commandBuffer, ProfiledPipeline pipeline, int depth, List<TreeNode> nodes)
    {
        string id = NodeId(pipeline);
        bool hasChildren = pipeline.Draws.Count > 0;
        nodes.Add(new TreeNode
        {
            Id = id,
            Label = $"{pipeline.ShaderName} ({pipeline.ShaderPassName}, {pipeline.Variant})",
            HasChildren = hasChildren,
            Depth = depth,
            UserData = new HierarchyUserData(HierarchyKind.Pipeline, view, pass, commandBuffer, pipeline, null, -1),
            OverrideExpanded = ForceExpandedOrNull(id),
        });

        if (!hasChildren)
            return;

        // Draws are a flat array; objects each claim a contiguous [DrawStart, DrawEnd) range within
        // it (in ascending order - see DrawHierarchyCollector). Anything not covered by an object's
        // range is a loose draw that goes directly under the pipeline.
        IReadOnlyList<ProfiledCallingObject> objects = pipeline.Objects;
        IReadOnlyList<ProfiledDrawCall> draws = pipeline.Draws;
        int objIndex = 0;
        int i = 0;
        while (i < draws.Count)
        {
            if (objIndex < objects.Count && objects[objIndex].DrawStart == i)
            {
                ProfiledCallingObject obj = objects[objIndex];
                BuildObjectNode(view, pass, commandBuffer, pipeline, obj, depth + 1, nodes);
                i = Math.Max(obj.DrawEnd, i + 1);
                objIndex++;
            }
            else
            {
                BuildDrawCallNode(view, pass, commandBuffer, pipeline, null, i, depth + 1, nodes);
                i++;
            }
        }
    }


    private void BuildObjectNode(ProfiledView view, ProfiledPass pass, ProfiledCommandBuffer commandBuffer, ProfiledPipeline pipeline, ProfiledCallingObject obj, int depth, List<TreeNode> nodes)
    {
        int count = obj.DrawEnd - obj.DrawStart;
        string label = string.IsNullOrEmpty(obj.Label) ? $"Object ({count} draws)" : $"{obj.Label} ({count} draws)";

        string id = NodeId(obj);
        nodes.Add(new TreeNode
        {
            Id = id,
            Label = label,
            HasChildren = count > 0,
            Depth = depth,
            UserData = new HierarchyUserData(HierarchyKind.Object, view, pass, commandBuffer, pipeline, obj, -1),
            OverrideExpanded = ForceExpandedOrNull(id),
        });

        for (int i = obj.DrawStart; i < obj.DrawEnd; i++)
            BuildDrawCallNode(view, pass, commandBuffer, pipeline, obj, i, depth + 1, nodes);
    }


    private void BuildDrawCallNode(ProfiledView view, ProfiledPass pass, ProfiledCommandBuffer commandBuffer, ProfiledPipeline pipeline, ProfiledCallingObject? obj, int drawIndex, int depth, List<TreeNode> nodes)
    {
        ProfiledDrawCall draw = pipeline.Draws[drawIndex];
        nodes.Add(new TreeNode
        {
            Id = $"{NodeId(pipeline)}_d{drawIndex}",
            Label = FormatDrawCallLabel(draw, drawIndex),
            IsLeaf = true,
            Depth = depth,
            UserData = new HierarchyUserData(HierarchyKind.DrawCall, view, pass, commandBuffer, pipeline, obj, drawIndex),
        });
    }


    private static string FormatDrawCallLabel(ProfiledDrawCall draw, int index)
    {
        string suffix = draw.Culled ? " (culled)" : "";

        if (draw.Dispatch is { } dispatch)
            return $"Dispatch #{index} ({dispatch.GroupCountX}x{dispatch.GroupCountY}x{dispatch.GroupCountZ}){suffix}";

        if (draw.Draw is { } d)
        {
            string tris = draw.TriangleCount is { } t ? $", {t} tris" : "";
            return $"Draw #{index} ({d.VertexOrIndexCount} verts x{d.InstanceCount}{tris}){suffix}";
        }

        return $"Draw #{index}{suffix}";
    }


    private bool? ForceExpandedOrNull(string id) => _hierarchyForceExpandIds.Contains(id) ? true : null;


    private static string NodeId(object owner) => RuntimeHelpers.GetHashCode(owner).ToString();


    private void OnHierarchyNodeSelected(HierarchyUserData d)
    {
        switch (d.Kind)
        {
            case HierarchyKind.View:
                SelectView(d.View!);
                break;
            case HierarchyKind.Pass:
                SelectPass(d.View!, d.Pass!);
                break;
            case HierarchyKind.CommandBuffer:
                SelectCommandBuffer(d.View, d.Pass, d.CommandBuffer!);
                break;
            case HierarchyKind.Pipeline:
                SelectPipeline(d.View, d.Pass, d.CommandBuffer, d.Pipeline!);
                break;
            case HierarchyKind.Object:
                SelectObject(d.View, d.Pass, d.CommandBuffer, d.Pipeline!, d.Object!);
                break;
            case HierarchyKind.DrawCall:
                SelectDrawCall(d.View, d.Pass, d.CommandBuffer, d.Pipeline!, d.Object, d.DrawIndex);
                break;
        }
    }


    private bool IsHierarchyNodeSelected(HierarchyUserData d) => d.Kind switch
    {
        HierarchyKind.View => SelectionType == ProfilerSelectionType.View && ReferenceEquals(SelectedView, d.View),
        HierarchyKind.Pass => SelectionType == ProfilerSelectionType.Pass && ReferenceEquals(SelectedPass, d.Pass),
        HierarchyKind.CommandBuffer => SelectionType == ProfilerSelectionType.CommandBuffer && ReferenceEquals(SelectedCommandBuffer, d.CommandBuffer),
        HierarchyKind.Pipeline => SelectionType == ProfilerSelectionType.Pipeline && ReferenceEquals(SelectedPipeline, d.Pipeline),
        HierarchyKind.Object => SelectionType == ProfilerSelectionType.Object && ReferenceEquals(SelectedObject, d.Object),
        HierarchyKind.DrawCall => SelectionType == ProfilerSelectionType.DrawCall && SelectedDrawCallIndex == d.DrawIndex && ReferenceEquals(SelectedPipeline, d.Pipeline),
        _ => false,
    };


    // Called when a flame graph node is clicked - highlights and scrolls to the matching hierarchy
    // row, expanding whatever ancestors are needed to make it visible.
    private void PingHierarchyForFlameSelection(ProfiledView? view, ProfiledPass? pass, ProfiledCommandBuffer? commandBuffer)
    {
        _hierarchyForceExpandIds.Clear();

        string targetId;
        if (commandBuffer != null)
        {
            targetId = NodeId(commandBuffer);
            if (view != null) _hierarchyForceExpandIds.Add(NodeId(view));
            if (pass != null) _hierarchyForceExpandIds.Add(NodeId(pass));
        }
        else if (pass != null)
        {
            targetId = NodeId(pass);
            if (view != null) _hierarchyForceExpandIds.Add(NodeId(view));
        }
        else if (view != null)
        {
            targetId = NodeId(view);
        }
        else
        {
            // Nothing beneath the frame was clicked - the flame graph's root bar represents the
            // whole frame, which isn't a tree node but the title label above it.
            targetId = FrameHierarchyNodeId;
        }

        _pingedHierarchyNodeId = targetId;
        _hierarchyPingTimer = HierarchyPingDuration;
        _hierarchyScrollPending = targetId != FrameHierarchyNodeId;
    }


    private void TickHierarchyPing()
    {
        if (_hierarchyPingTimer <= 0f)
            return;

        _hierarchyPingTimer -= Time.UnscaledDeltaTime;
        if (_hierarchyPingTimer <= 0f)
        {
            _hierarchyPingTimer = 0f;
            _pingedHierarchyNodeId = null;
        }
    }


    private float GetHierarchyPingAlpha()
    {
        if (_hierarchyPingTimer <= 0f)
            return 0f;
        if (_hierarchyPingTimer > HierarchyPingFadeStart)
            return 1f;
        return _hierarchyPingTimer / HierarchyPingFadeStart;
    }
}
