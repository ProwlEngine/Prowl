using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Prowl.Editor.Profiling;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Vector;

namespace Prowl.Editor.GUI.RenderProfiler;

public partial class RenderProfilerPanel
{
    private const float HierarchyPanelWidth = 340f;
    private const float HierarchyRowHeight = 22f;
    private const float HierarchyPingDuration = 1.5f;
    private const float HierarchyPingFadeStart = 0.5f;

    // Sentinel id for the frame's root tree node.
    private const string FrameHierarchyNodeId = "__frame__";

    private enum HierarchyKind { Frame, View, Pass, CommandBuffer, Pipeline, Object, DrawCall }

    private readonly record struct HierarchyUserData(
        HierarchyKind Kind,
        ProfiledView? View,
        ProfiledPass? Pass,
        ProfiledCommandBuffer? CommandBuffer,
        ProfiledPipeline? Pipeline,
        ProfiledCallingObject? Object,
        int DrawIndex);

    private readonly Dictionary<string, bool> _hierarchyExpandState = [];
    private HashSet<string>? _hierarchyForceExpandIds;
    private string? _pingedHierarchyNodeId;
    private float _hierarchyPingTimer;
    private bool _hierarchyScrollPending;


    private void DrawHierarchy(Paper paper, float width, float height)
    {
        ProfiledFrame? frame = SelectedFrame;

        var nodes = new List<TreeNode>();
        if (frame != null)
            BuildHierarchyNodes(frame, nodes);

        TickHierarchyPing();

        Origami.Tree(paper, "rdp_hier_tree", width, height)
            .Nodes(nodes)
            .RowHeight(HierarchyRowHeight)
            .IndentSize(10f)
            .BaseIndent(2f)
            .ArrowWidth(8f)
            .IsSelected(n => IsHierarchyNodeSelected((HierarchyUserData)n.UserData!))
            .OnSelect(e => OnHierarchyNodeSelected((HierarchyUserData)e.Node.UserData!))
            .IsPinged(n => _pingedHierarchyNodeId == n.Id)
            .PingAlpha(GetHierarchyPingAlpha)
            .ExpandStateSink(_hierarchyExpandState)
            .EmptyMessage("No frame selected")
            .Show();

        // Force-expand is a one-shot nudge - the tree persists it into its own element
        // storage the moment it's applied, so it doesn't need to be reasserted next frame.
        _hierarchyForceExpandIds = null;

        if (_hierarchyScrollPending && _pingedHierarchyNodeId != null)
        {
            int pingIndex = nodes.FindIndex(n => n.Id == _pingedHierarchyNodeId);
            if (pingIndex >= 0)
            {
                float rowTotal = HierarchyRowHeight + 2f;
                float targetY = pingIndex * rowTotal - (height * 0.5f) + rowTotal * 0.5f;
                Origami.ScrollTo("rdp_hier_tree_scroll", new Float2(0, targetY));
            }
            _hierarchyScrollPending = false;
        }
    }


    private void BuildHierarchyNodes(ProfiledFrame frame, List<TreeNode> nodes)
    {
        nodes.Add(new TreeNode
        {
            Id = FrameHierarchyNodeId,
            Label = $"Frame {frame.FrameIndex} ({frame.GpuMilliseconds:F2} ms)",
            IsLeaf = true,
            Depth = 0,
            UserData = new HierarchyUserData(HierarchyKind.Frame, null, null, null, null, null, -1),
        });

        foreach (ProfiledView view in frame.Views)
            BuildViewNode(view, 1, nodes);
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
            OverrideExpanded = _hierarchyForceExpandIds?.Contains(id) == true ? true : null,
            UserData = new HierarchyUserData(HierarchyKind.View, view, null, null, null, null, -1),
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
            OverrideExpanded = _hierarchyForceExpandIds?.Contains(id) == true ? true : null,
            UserData = new HierarchyUserData(HierarchyKind.Pass, view, pass, null, null, null, -1),
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

    private static string NodeId(object owner) => RuntimeHelpers.GetHashCode(owner).ToString();


    private void OnHierarchyNodeSelected(HierarchyUserData d)
    {
        switch (d.Kind)
        {
            case HierarchyKind.Frame:
                ClearSubFrameSelection();
                break;
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
        HierarchyKind.Frame => SelectionType == ProfilerSelectionType.Frame,
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
        string targetId;
        var forceExpandIds = new HashSet<string>();
        if (commandBuffer != null)
        {
            targetId = NodeId(commandBuffer);
            if (view != null) forceExpandIds.Add(NodeId(view));
            if (pass != null) forceExpandIds.Add(NodeId(pass));
        }
        else if (pass != null)
        {
            targetId = NodeId(pass);
            if (view != null) forceExpandIds.Add(NodeId(view));
        }
        else if (view != null)
        {
            targetId = NodeId(view);
        }
        else
        {
            targetId = FrameHierarchyNodeId;
        }

        _hierarchyForceExpandIds = forceExpandIds;
        _pingedHierarchyNodeId = targetId;
        _hierarchyPingTimer = HierarchyPingDuration;
        _hierarchyScrollPending = true;
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
