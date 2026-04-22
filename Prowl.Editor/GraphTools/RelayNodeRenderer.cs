// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Quill;
using Prowl.Runtime.GraphTools;
using Prowl.Vector;

namespace Prowl.Editor.GraphTools;

/// <summary>
/// Custom renderer for <see cref="RelayNode"/> — draws a small compact node instead
/// of the full card. Ports sit flush to the left/right edges of a narrow body so a
/// relay is visibly just a "waypoint dot" that the wire passes through.
/// </summary>
[NodeRenderer(typeof(RelayNode))]
public sealed class RelayNodeRenderer : NodeRenderer
{
    private const float Width  = 28f;
    private const float Height = 20f;

    public override Rect GetRect(Node node)
        => new Rect(node.Position.X, node.Position.Y,
                     node.Position.X + Width, node.Position.Y + Height);

    public override Float2 GetPortPosition(Node node, Port port)
    {
        float y = node.Position.Y + Height * 0.5f;
        float x = port.Direction == PortDirection.Input
            ? node.Position.X
            : node.Position.X + Width;
        return new Float2(x, y);
    }

    public override void Draw(Canvas canvas, Prowl.Runtime.GraphTools.Graph graph, Node node,
        bool isSelected, bool isHovered,
        (string portName, PortDirection direction)? hoveredPort,
        float zoom, Prowl.Scribe.FontFile? font)
    {
        var rect = GetRect(node);
        float x = (float)rect.Min.X, y = (float)rect.Min.Y;

        // Body — rounded pill tinted by the carried port colour so users can eyeball
        // the data type at a glance without reading a label.
        Color32 dataColor = new Color32(170, 170, 170, 255);
        if (node.Outputs.Count > 0)
            dataColor = GraphLayout.GetPortColor(node.Outputs[0].DataType);

        var bodyCol = isHovered
            ? new Color32((byte)(dataColor.R / 2 + 40), (byte)(dataColor.G / 2 + 40), (byte)(dataColor.B / 2 + 40), 255)
            : new Color32((byte)(dataColor.R / 3 + 30), (byte)(dataColor.G / 3 + 30), (byte)(dataColor.B / 3 + 30), 255);

        canvas.RoundedRectFilled(x, y, Width, Height, Height * 0.5f, Height * 0.5f, Height * 0.5f, Height * 0.5f, bodyCol);

        canvas.BeginPath();
        canvas.RoundedRect(x, y, Width, Height, Height * 0.5f);
        canvas.SetStrokeColor(isSelected
            ? new Color32(255, 200, 80, 255)
            : new Color32(20, 22, 28, 255));
        canvas.SetStrokeWidth(isSelected ? 2.0f : 1.0f);
        canvas.Stroke();

        // Ports (just the dots — no labels, no halo for zoomed-out simplicity).
        foreach (var port in node.Inputs)
            DrawDot(canvas, GetPortPosition(node, port), dataColor, hoveredPort, node.Id, port);
        foreach (var port in node.Outputs)
            DrawDot(canvas, GetPortPosition(node, port), dataColor, hoveredPort, node.Id, port);
    }

    private static void DrawDot(Canvas canvas, Float2 pos, Color32 color,
        (string portName, PortDirection direction)? hoveredPort, System.Guid nodeId, Port port)
    {
        bool dim = GraphLayout.IsDropTargetRejected(nodeId, port);
        bool isHov = !dim && hoveredPort.HasValue
            && hoveredPort.Value.direction == port.Direction
            && hoveredPort.Value.portName == port.Name;

        byte DimA(byte a) => dim ? (byte)(a * 0.4f) : a;

        if (isHov)
        {
            var halo = color; halo.A = 90;
            canvas.CircleFilled(pos.X, pos.Y, GraphLayout.PortDotRadius + 5f, halo, 16);
        }
        float r = isHov ? GraphLayout.PortDotRadius + 1.5f : GraphLayout.PortDotRadius;
        var ring = new Color32(20, 22, 28, 255); ring.A = DimA(ring.A);
        var fill = color;                        fill.A = DimA(fill.A);
        canvas.CircleFilled(pos.X, pos.Y, r + 1.5f, ring, 14);
        canvas.CircleFilled(pos.X, pos.Y, r, fill, 14);
    }
}
