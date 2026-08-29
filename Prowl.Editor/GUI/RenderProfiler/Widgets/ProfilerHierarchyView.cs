using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Prowl.Editor.Profiling;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Vector;

namespace Prowl.Editor.GUI.RenderProfiler.Widgets;

public enum HierarchyNodeKind { Frame, View, Pass, CommandBuffer, Pipeline, Object, DrawCall }

public readonly record struct HierarchyNode(
    HierarchyNodeKind Kind,
    ProfiledView? View,
    ProfiledPass? Pass,
    ProfiledCommandBuffer? CommandBuffer,
    ProfiledPipeline? Pipeline,
    ProfiledCallingObject? Object,
    int DrawIndex);

// Owns the hierarchy tree's own expand/ping state, so two instances (e.g. one per host) never
// collide - each derives its element ids from its own constructor-supplied id.
public sealed class ProfilerHierarchyView
{
    private const float RowHeight = 22f;
    private const float PingDuration = 1.5f;
    private const float PingFadeStart = 0.5f;

    // Sentinel id for the frame's root tree node.
    private const string FrameNodeId = "__frame__";

    private readonly string _id;

    private readonly Dictionary<string, bool> _expandState = [];
    private HashSet<string>? _forceExpandIds;
    private string? _pingedNodeId;
    private float _pingTimer;
    private bool _scrollPending;

    public Func<HierarchyNode, bool>? IsSelected { get; set; }
    public event Action<HierarchyNode>? Selected;

    public ProfilerHierarchyView(string id)
    {
        _id = id;
    }


    public void Draw(Paper paper, ProfiledFrame? frame, float width, float height)
    {
        var nodes = new List<TreeNode>();
        if (frame != null)
            BuildFrameNodes(frame, nodes);

        TickPing();

        Origami.Tree(paper, _id, width, height)
            .Nodes(nodes)
            .RowHeight(RowHeight)
            .IndentSize(10f)
            .BaseIndent(2f)
            .ArrowWidth(8f)
            .IsSelected(n => IsSelected?.Invoke((HierarchyNode)n.UserData!) ?? false)
            .OnSelect(e => Selected?.Invoke((HierarchyNode)e.Node.UserData!))
            .IsPinged(n => _pingedNodeId == n.Id)
            .PingAlpha(GetPingAlpha)
            .ExpandStateSink(_expandState)
            .EmptyMessage("No frame selected")
            .Show();

        // Force-expand is a one-shot nudge - the tree persists it into its own element
        // storage the moment it's applied, so it doesn't need to be reasserted next frame.
        _forceExpandIds = null;

        if (_scrollPending && _pingedNodeId != null)
        {
            int pingIndex = nodes.FindIndex(n => n.Id == _pingedNodeId);
            if (pingIndex >= 0)
            {
                float rowTotal = RowHeight + 2f;
                float targetY = pingIndex * rowTotal - (height * 0.5f) + rowTotal * 0.5f;
                Origami.ScrollTo($"{_id}_scroll", new Float2(0, targetY));
            }
            _scrollPending = false;
        }
    }


    // Highlights and scrolls to the matching hierarchy row, expanding whatever ancestors are needed
    // to make it visible. Called by the host in response to a click on another widget (flame graph,
    // pass graph, ...).
    public void Ping(ProfiledView? view, ProfiledPass? pass, ProfiledCommandBuffer? commandBuffer)
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
            targetId = FrameNodeId;
        }

        _forceExpandIds = forceExpandIds;
        _pingedNodeId = targetId;
        _pingTimer = PingDuration;
        _scrollPending = true;
    }


    private void BuildFrameNodes(ProfiledFrame frame, List<TreeNode> nodes)
    {
        nodes.Add(new TreeNode
        {
            Id = FrameNodeId,
            Label = $"Frame {frame.FrameIndex} ({frame.GpuMilliseconds:F2} ms)",
            IsLeaf = true,
            Depth = 0,
            UserData = new HierarchyNode(HierarchyNodeKind.Frame, null, null, null, null, null, -1),
        });

        foreach (ProfiledView view in frame.Views)
            BuildViewNode(view, frame.HasCaptureDepth, 1, nodes);
    }


    private void BuildViewNode(ProfiledView view, bool isFullCapture, int depth, List<TreeNode> nodes)
    {
        string id = NodeId(view);
        nodes.Add(new TreeNode
        {
            Id = id,
            Label = $"{view.Name} ({view.GpuMilliseconds:F2} ms)",
            HasChildren = view.Passes.Count > 0,
            Depth = depth,
            OverrideExpanded = _forceExpandIds?.Contains(id) == true ? true : null,
            UserData = new HierarchyNode(HierarchyNodeKind.View, view, null, null, null, null, -1),
        });

        foreach (ProfiledPass pass in view.Passes)
            BuildPassNode(view, pass, isFullCapture, depth + 1, nodes);
    }


    private void BuildPassNode(ProfiledView view, ProfiledPass pass, bool isFullCapture, int depth, List<TreeNode> nodes)
    {
        string id = NodeId(pass);
        nodes.Add(new TreeNode
        {
            Id = id,
            Label = $"{pass.Name} ({pass.GpuMilliseconds:F2} ms)",
            HasChildren = pass.CommandBuffers.Count > 0,
            Depth = depth,
            OverrideExpanded = _forceExpandIds?.Contains(id) == true ? true : null,
            UserData = new HierarchyNode(HierarchyNodeKind.Pass, view, pass, null, null, null, -1),
        });

        foreach (ProfiledCommandBuffer commandBuffer in pass.CommandBuffers)
            BuildCommandBufferNode(view, pass, commandBuffer, isFullCapture, depth + 1, nodes);
    }


    private static void BuildCommandBufferNode(ProfiledView view, ProfiledPass pass, ProfiledCommandBuffer commandBuffer, bool isFullCapture, int depth, List<TreeNode> nodes)
    {
        bool hasChildren = isFullCapture && commandBuffer.Switches.Count > 0;

        string id = NodeId(commandBuffer);
        nodes.Add(new TreeNode
        {
            Id = id,
            Label = $"{commandBuffer.Name} ({commandBuffer.GpuMilliseconds:F2} ms)",
            HasChildren = hasChildren,
            IsLeaf = !isFullCapture,
            Depth = depth,
            UserData = new HierarchyNode(HierarchyNodeKind.CommandBuffer, view, pass, commandBuffer, null, null, -1),
        });

        if (!isFullCapture)
            return;

        foreach (ProfiledPipeline pipeline in commandBuffer.Switches)
            BuildPipelineNode(view, pass, commandBuffer, pipeline, depth + 1, nodes);
    }


    private static void BuildPipelineNode(ProfiledView view, ProfiledPass pass, ProfiledCommandBuffer commandBuffer, ProfiledPipeline pipeline, int depth, List<TreeNode> nodes)
    {
        string id = NodeId(pipeline);
        bool hasChildren = pipeline.Draws.Count > 0;
        nodes.Add(new TreeNode
        {
            Id = id,
            Label = $"{pipeline.ShaderName} ({pipeline.ShaderPassName})",
            HasChildren = hasChildren,
            Depth = depth,
            UserData = new HierarchyNode(HierarchyNodeKind.Pipeline, view, pass, commandBuffer, pipeline, null, -1),
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


    private static void BuildObjectNode(ProfiledView view, ProfiledPass pass, ProfiledCommandBuffer commandBuffer, ProfiledPipeline pipeline, ProfiledCallingObject obj, int depth, List<TreeNode> nodes)
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
            UserData = new HierarchyNode(HierarchyNodeKind.Object, view, pass, commandBuffer, pipeline, obj, -1),
        });

        for (int i = obj.DrawStart; i < obj.DrawEnd; i++)
            BuildDrawCallNode(view, pass, commandBuffer, pipeline, obj, i, depth + 1, nodes);
    }


    private static void BuildDrawCallNode(ProfiledView view, ProfiledPass pass, ProfiledCommandBuffer commandBuffer, ProfiledPipeline pipeline, ProfiledCallingObject? obj, int drawIndex, int depth, List<TreeNode> nodes)
    {
        ProfiledDrawCall draw = pipeline.Draws[drawIndex];
        nodes.Add(new TreeNode
        {
            Id = $"{NodeId(pipeline)}_d{drawIndex}",
            Label = FormatDrawCallLabel(draw, drawIndex),
            IsLeaf = true,
            Depth = depth,
            UserData = new HierarchyNode(HierarchyNodeKind.DrawCall, view, pass, commandBuffer, pipeline, obj, drawIndex),
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


    private void TickPing()
    {
        if (_pingTimer <= 0f)
            return;

        _pingTimer -= Time.UnscaledDeltaTime;
        if (_pingTimer <= 0f)
        {
            _pingTimer = 0f;
            _pingedNodeId = null;
        }
    }


    private float GetPingAlpha()
    {
        if (_pingTimer <= 0f)
            return 0f;
        if (_pingTimer > PingFadeStart)
            return 1f;
        return _pingTimer / PingFadeStart;
    }
}
