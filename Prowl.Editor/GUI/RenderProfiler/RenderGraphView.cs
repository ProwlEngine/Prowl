using System.Collections.Generic;

using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime.Rendering;
using Prowl.Scribe;
using Prowl.Vector;

namespace Prowl.Editor.GUI.RenderProfiler;

/// <summary>
/// Render Graph live tab. Visualizes the pass DAG with the existing Origami <see cref="Origami.NodeGraph"/>
/// widget (no new DAG widget): each <see cref="PassReport"/> is a node, its declared inputs/outputs are
/// input/output ports keyed by interned resource id, and each <see cref="GraphEdge"/> becomes a wire from
/// the writer pass's output port to the reader pass's input port. Node/port layout is rebuilt only when
/// the displayed frame changes; the graph is framed to fit on the first build.
/// </summary>
public sealed class RenderGraphView
{
    private readonly NodeGraphController _controller = new();
    private List<GraphNode> _nodes = new();
    private List<GraphConnection> _connections = new();
    private long _builtFrameIndex = long.MinValue;
    private bool _pendingFrame;

    private const float ColumnSpacing = 240f;
    private const float RowSpacing = 150f;

    public void Draw(Paper paper, RenderFrameReport? report, FontFile font, float width, float height)
    {
        using (paper.Column("rp_graph_root").Size(width, height).Padding(8, 8, 8, 8).Enter())
        {
            if (report == null || report.Passes.Count == 0)
            {
                EditorGUI.EmptyState(paper, "rp_graph_empty", "No render graph this frame", font);
                return;
            }

            if (report.FrameIndex != _builtFrameIndex)
            {
                Rebuild(report);
                _builtFrameIndex = report.FrameIndex;
            }

            if (_pendingFrame)
            {
                _controller.FrameAll();
                _pendingFrame = false;
            }

            Origami.NodeGraph(paper, "rp_graph", width - 16f, height - 16f)
                .Nodes(_nodes)
                .Connections(_connections)
                .Controller(_controller)
                .Grid()
                .Show();
        }
    }

    private void Rebuild(RenderFrameReport report)
    {
        _nodes = new List<GraphNode>(report.Passes.Count);
        _connections = new List<GraphConnection>(report.Edges.Count);
        _pendingFrame = true;

        for (int i = 0; i < report.Passes.Count; i++)
        {
            var pass = report.Passes[i];
            var node = new GraphNode
            {
                Id = i.ToString(),
                Title = string.IsNullOrEmpty(pass.Name) ? $"Pass {i}" : pass.Name,
                Position = new Float2(i * ColumnSpacing, (i % 3) * RowSpacing),
                Width = 176f,
                Accent = pass.IsPresentationSource ? EditorTheme.Green400 : EditorTheme.Purple400,
                UserData = pass,
            };

            var seenIn = new HashSet<string>();
            foreach (var res in pass.Inputs)
                if (seenIn.Add(res))
                    node.Inputs.Add(new GraphPort(res, res));

            var seenOut = new HashSet<string>();
            foreach (var res in pass.Outputs)
                if (seenOut.Add(res))
                    node.Outputs.Add(new GraphPort(res, res));

            _nodes.Add(node);
        }

        foreach (var edge in report.Edges)
        {
            if (edge.WriterPassIndex < 0 || edge.ReaderPassIndex < 0) continue;
            EnsureOutputPort(edge.WriterPassIndex, edge.ResourceId);
            EnsureInputPort(edge.ReaderPassIndex, edge.ResourceId);
            _connections.Add(new GraphConnection(
                edge.WriterPassIndex.ToString(), edge.ResourceId,
                edge.ReaderPassIndex.ToString(), edge.ResourceId));
        }
    }

    private void EnsureInputPort(int passIndex, string resId)
    {
        if (passIndex >= _nodes.Count) return;
        var node = _nodes[passIndex];
        foreach (var p in node.Inputs) if (p.Id == resId) return;
        node.Inputs.Add(new GraphPort(resId, resId));
    }

    private void EnsureOutputPort(int passIndex, string resId)
    {
        if (passIndex >= _nodes.Count) return;
        var node = _nodes[passIndex];
        foreach (var p in node.Outputs) if (p.Id == resId) return;
        node.Outputs.Add(new GraphPort(resId, resId));
    }
}
