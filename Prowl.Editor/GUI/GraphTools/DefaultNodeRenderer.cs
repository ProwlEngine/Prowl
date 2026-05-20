// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Quill;
using Prowl.Runtime.GraphTools;
using Prowl.Vector;

namespace Prowl.Editor.GraphTools;

/// <summary>
/// Out-of-the-box renderer used when a node type doesn't register its own. Paints the
/// familiar rectangular card: drop shadow, tinted header with title, body with stacked
/// port rows, coloured port dots + labels, diagnostic badge. Inputs on the left edge,
/// outputs on the right; geometry is fixed-width with height derived from port count.
/// </summary>
public sealed class DefaultNodeRenderer : NodeRenderer
{
    // --- Palette ---------------------------------------------------------------------
    private static readonly Color32 NodeBody      = new Color32(50, 53, 62, 255);
    private static readonly Color32 NodeBodyHover = new Color32(58, 62, 72, 255);
    private static readonly Color32 NodeBorder    = new Color32(20, 22, 28, 255);
    private static readonly Color32 NodeSelected  = new Color32(255, 200, 80, 255);
    private static readonly Color32 NodeShadow    = new Color32(0, 0, 0, 110);
    private static readonly Color32 PortLabel     = new Color32(190, 192, 200, 255);
    private static readonly Color32 TitleText     = new Color32(238, 240, 245, 255);

    public override Rect GetRect(Node node)
    {
        node.EnsureDefined();

        // Resizable node? Use its explicit size when set (non-zero on either axis).
        if (node is IResizableNode r)
        {
            var s = r.GetSize();
            if (s.X > 0 && s.Y > 0)
                return new Rect(node.Position.X, node.Position.Y,
                                 node.Position.X + s.X, node.Position.Y + s.Y);
        }

        int rows = Math.Max(VisibleCount(node.Inputs), VisibleCount(node.Outputs));
        float bodyHeight = MathF.Max(
            rows * GraphLayout.PortRowHeight + GraphLayout.BodyPadding * 2,
            GraphLayout.PortRowHeight);
        float h = GraphLayout.HeaderHeight + bodyHeight;

        // Reserve preview area below the body when the node opts in. Only counts when
        // both the node says HasPreview AND a drawer is registered otherwise the
        // node stays its compact size.
        if (node is INodePreview p && p.HasPreview && p.PreviewHeight > 0
            && NodePreviewRegistry.GetDrawer(node.GetType()) != null)
            h += p.PreviewHeight + GraphLayout.BodyPadding;

        return new Rect(node.Position.X, node.Position.Y,
                        node.Position.X + GraphLayout.NodeWidth,
                        node.Position.Y + h);
    }

    public override Float2 GetPortPosition(Node node, Port port)
    {
        node.EnsureDefined();
        // Default card: ports are indexed by their VISIBLE position in the same-
        // direction list. Hidden ports are skipped so the visible row indices stay
        // dense and ports don't visually float over empty rows.
        var list = port.Direction == PortDirection.Input ? node.Inputs : node.Outputs;
        int visibleIdx = -1, found = -1;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].IsHidden) continue;
            visibleIdx++;
            if (list[i] == port) { found = visibleIdx; break; }
        }
        if (found < 0) return node.Position;

        float yOffset = GraphLayout.HeaderHeight + GraphLayout.BodyPadding
                      + found * GraphLayout.PortRowHeight
                      + GraphLayout.PortRowHeight * 0.5f;
        float x = port.Direction == PortDirection.Input
            ? node.Position.X
            : node.Position.X + GraphLayout.NodeWidth;
        return new Float2(x, node.Position.Y + yOffset);
    }

    private static int VisibleCount(System.Collections.Generic.List<Port> ports)
    {
        int n = 0;
        for (int i = 0; i < ports.Count; i++) if (!ports[i].IsHidden) n++;
        return n;
    }

    public override void Draw(Canvas canvas, Prowl.Runtime.GraphTools.Graph graph, Node node,
        bool isSelected, bool isHovered,
        (string portName, PortDirection direction)? hoveredPort,
        float zoom, Prowl.Scribe.FontFile? font)
    {
        var rect = GetRect(node);

        // Drop shadow (offset slightly down-right).
        canvas.RoundedRectFilled(
            (float)rect.Min.X + 2, (float)rect.Min.Y + 4,
            (float)rect.Size.X, (float)rect.Size.Y,
            GraphLayout.NodeRadius, GraphLayout.NodeRadius, GraphLayout.NodeRadius, GraphLayout.NodeRadius,
            NodeShadow);

        // Body.
        var bodyColor = isHovered ? NodeBodyHover : NodeBody;
        canvas.RoundedRectFilled(
            (float)rect.Min.X, (float)rect.Min.Y, (float)rect.Size.X, (float)rect.Size.Y,
            GraphLayout.NodeRadius, GraphLayout.NodeRadius, GraphLayout.NodeRadius, GraphLayout.NodeRadius,
            bodyColor);

        // Header strip (tinted by node accent colour).
        var accent = (Color32)node.AccentColor;
        canvas.RoundedRectFilled(
            (float)rect.Min.X, (float)rect.Min.Y, (float)rect.Size.X, GraphLayout.HeaderHeight,
            GraphLayout.NodeRadius, GraphLayout.NodeRadius, 0f, 0f, accent);

        // Border (bright when selected).
        canvas.BeginPath();
        canvas.RoundedRect((float)rect.Min.X, (float)rect.Min.Y, (float)rect.Size.X, (float)rect.Size.Y,
            GraphLayout.NodeRadius);
        canvas.SetStrokeColor(isSelected ? NodeSelected : NodeBorder);
        canvas.SetStrokeWidth(isSelected ? 2.0f : 1.0f);
        canvas.Stroke();

        // LOD thresholds.
        bool drawTitle = zoom > 0.35f;
        bool drawPortLabels = zoom > 0.55f;

        if (drawTitle && font != null)
        {
            const float titleSize = 14f;
            canvas.DrawText(node.Title, (float)rect.Min.X + 8, (float)rect.Min.Y + 5,
                TitleText, titleSize, font);
        }

        for (int i = 0; i < node.Inputs.Count; i++)
        {
            var port = node.Inputs[i];
            if (port.IsHidden) continue;
            var pos = GetPortPosition(node, port);
            bool portHov = hoveredPort.HasValue
                && hoveredPort.Value.direction == PortDirection.Input
                && hoveredPort.Value.portName == port.Name;
            bool dim = GraphLayout.IsDropTargetRejected(node.Id, port);
            DrawPort(canvas, port, pos, drawPortLabels, font, isLeftSide: true, isHovered: portHov, dim: dim);
        }
        for (int i = 0; i < node.Outputs.Count; i++)
        {
            var port = node.Outputs[i];
            if (port.IsHidden) continue;
            var pos = GetPortPosition(node, port);
            bool portHov = hoveredPort.HasValue
                && hoveredPort.Value.direction == PortDirection.Output
                && hoveredPort.Value.portName == port.Name;
            bool dim = GraphLayout.IsDropTargetRejected(node.Id, port);
            DrawPort(canvas, port, pos, drawPortLabels, font, isLeftSide: false, isHovered: portHov, dim: dim);
        }

        if (node.Messages.Count > 0)
            DrawNodeMessages(canvas, node, rect);

        // Resize grip small triangular tab in the bottom-right corner. Same
        // visual / hit area as sticky notes / groups so the gesture is consistent.
        if (node is IResizableNode)
        {
            float hx1 = (float)rect.Max.X, hy1 = (float)rect.Max.Y;
            float hx0 = hx1 - 14f, hy0 = hy1 - 14f;
            var gripCol = new Color32(180, 184, 196, 220);
            canvas.BeginPath();
            canvas.MoveTo(hx1, hy0);
            canvas.LineTo(hx1, hy1);
            canvas.LineTo(hx0, hy1);
            canvas.ClosePath();
            canvas.SetFillColor(gripCol);
            canvas.Fill();
        }

        // Custom preview region drawn AFTER ports so the preview can extend down past
        // the natural body. Sized by the node, drawn by the registered drawer.
        if (node is INodePreview p && p.HasPreview && p.PreviewHeight > 0)
        {
            var drawer = NodePreviewRegistry.GetDrawer(node.GetType());
            if (drawer != null)
            {
                int rows = Math.Max(VisibleCount(node.Inputs), VisibleCount(node.Outputs));
                float bodyHeight = MathF.Max(
                    rows * GraphLayout.PortRowHeight + GraphLayout.BodyPadding * 2,
                    GraphLayout.PortRowHeight);
                float top = node.Position.Y + GraphLayout.HeaderHeight + bodyHeight;
                var pRect = new Rect(
                    node.Position.X + GraphLayout.BodyPadding,
                    top,
                    node.Position.X + GraphLayout.NodeWidth - GraphLayout.BodyPadding,
                    top + p.PreviewHeight);
                drawer.Draw(canvas, node, pRect, zoom);
            }
        }
    }

    private static void DrawPort(Canvas canvas, Port port, Float2 pos, bool drawLabel,
        Prowl.Scribe.FontFile? font, bool isLeftSide, bool isHovered, bool dim = false)
    {
        var color = GraphLayout.GetPortColor(port.DataType);

        // Dim incompatible drop targets to ~40% opacity so the user sees at a glance
        // which ports a wire can legally plug into.
        byte DimA(byte a) => dim ? (byte)(a * 0.4f) : a;

        if (isHovered && !dim)
        {
            var halo = color; halo.A = 90;
            canvas.CircleFilled(pos.X, pos.Y, GraphLayout.PortDotRadius + 5f, halo, 16);
        }

        float r = isHovered && !dim ? GraphLayout.PortDotRadius + 1.5f : GraphLayout.PortDotRadius;
        // Two stacked filled circles outer ring + inner coloured fill. Crisper than
        // a Stroke at small radii with less aliasing.
        var ring = NodeBorder; ring.A = DimA(ring.A);
        var fill = color;      fill.A = DimA(fill.A);
        canvas.CircleFilled(pos.X, pos.Y, r + 1.5f, ring, 14);
        canvas.CircleFilled(pos.X, pos.Y, r, fill, 14);

        if (drawLabel && font != null)
        {
            const float labelGap = 12f;
            const float labelSize = 12f;
            float labelX = isLeftSide
                ? pos.X + labelGap
                : pos.X - labelGap - MeasureWidthApprox(port.Name, labelSize);
            var lbl = PortLabel; lbl.A = DimA(lbl.A);
            canvas.DrawText(port.Name, labelX, pos.Y - labelSize * 0.5f - 1, lbl, labelSize, font);
        }
    }

    private static float MeasureWidthApprox(string text, float pixelSize)
        => string.IsNullOrEmpty(text) ? 0 : text.Length * pixelSize * 0.55f;

    private static void DrawNodeMessages(Canvas canvas, Node node, Rect rect)
    {
        var worst = NodeMessageSeverity.Info;
        foreach (var m in node.Messages)
            if ((int)m.Severity > (int)worst) worst = m.Severity;

        Color32 badge = worst switch
        {
            NodeMessageSeverity.Error   => new Color32(230, 90, 90, 255),
            NodeMessageSeverity.Warning => new Color32(230, 180, 60, 255),
            _                            => new Color32(120, 170, 230, 255),
        };
        float bx = (float)rect.Max.X - 10, by = (float)rect.Min.Y + 14;
        canvas.CircleFilled(bx, by, 5, badge, 12);
    }
}
