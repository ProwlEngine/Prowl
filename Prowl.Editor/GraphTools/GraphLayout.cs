// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.GraphTools;
using Prowl.Vector;

namespace Prowl.Editor.GraphTools;

/// <summary>
/// Pure layout math for graph elements — node bounding rects, port positions,
/// type-coloured port palette. No drawing here, no Paper dependency. The renderer
/// and the interaction layer both consume these so they agree on geometry.
/// </summary>
public static class GraphLayout
{
    // ─── Constant node geometry (graph-space units) ──────────────────────────────────
    public const float NodeWidth      = 200f;
    public const float HeaderHeight   = 28f;
    public const float PortRowHeight  = 22f;
    public const float BodyPadding    = 6f;
    public const float NodeRadius     = 6f;
    public const float PortDotRadius  = 5f;

    /// <summary>Bounding rectangle of a node in graph space. Dispatches into the node's
    /// registered <see cref="NodeRenderer"/> so custom renderers (vertical / icon / etc.)
    /// pick their own shape — the whole system (hit-test, marquee, framing) follows.</summary>
    public static Rect GetNodeRect(Node node)
        => NodeRendererRegistry.GetRenderer(node).GetRect(node);

    /// <summary>
    /// Find a port's position in graph space by name. Returns null if the port doesn't
    /// belong to the node (defensive — happens after rename).
    /// </summary>
    public static Float2? TryGetPortPosition(Node node, string portName, PortDirection direction)
    {
        var list = direction == PortDirection.Input ? node.Inputs : node.Outputs;
        for (int i = 0; i < list.Count; i++)
            if (list[i].Name == portName && !list[i].IsHidden)
                return NodeRendererRegistry.GetRenderer(node).GetPortPosition(node, list[i]);
        return null;
    }

    /// <summary>
    /// Compute the AABB enclosing every node + sticky note in the graph. Used to frame
    /// the view (View → Frame All) and to draw the minimap viewport rectangle.
    /// Returns false if the graph is empty.
    /// </summary>
    public static bool ComputeGraphBounds(Graph graph, out Float2 min, out Float2 max)
    {
        min = new Float2(float.MaxValue);
        max = new Float2(float.MinValue);
        bool found = false;
        foreach (var n in graph.Nodes)
        {
            var r = GetNodeRect(n);
            min.X = MathF.Min(min.X, (float)r.Min.X); min.Y = MathF.Min(min.Y, (float)r.Min.Y);
            max.X = MathF.Max(max.X, (float)r.Max.X); max.Y = MathF.Max(max.Y, (float)r.Max.Y);
            found = true;
        }
        foreach (var s in graph.StickyNotes)
        {
            min.X = MathF.Min(min.X, s.Position.X); min.Y = MathF.Min(min.Y, s.Position.Y);
            max.X = MathF.Max(max.X, s.Position.X + s.Size.X); max.Y = MathF.Max(max.Y, s.Position.Y + s.Size.Y);
            found = true;
        }
        foreach (var g in graph.Groups)
        {
            min.X = MathF.Min(min.X, g.Position.X); min.Y = MathF.Min(min.Y, g.Position.Y);
            max.X = MathF.Max(max.X, g.Position.X + g.Size.X); max.Y = MathF.Max(max.Y, g.Position.Y + g.Size.Y);
            found = true;
        }
        if (!found) { min = max = Float2.Zero; }
        return found;
    }

    // ─── Type-based port colour palette ──────────────────────────────────────────────
    // Convention close to Unity Shader Graph / Blender: flat-shaded by data category.
    private static readonly Dictionary<Type, Color32> _portColors = new()
    {
        [typeof(float)]   = new Color32(220, 200, 80, 255),    // amber — scalar
        [typeof(int)]     = new Color32(120, 200, 220, 255),   // cyan — integer
        [typeof(bool)]    = new Color32(220, 90, 90, 255),     // red — boolean
        [typeof(Float2)]  = new Color32(180, 220, 100, 255),   // lime
        [typeof(Float3)]  = new Color32(110, 200, 130, 255),   // green
        [typeof(Float4)]  = new Color32(220, 120, 200, 255),   // pink
        [typeof(string)]  = new Color32(200, 160, 120, 255),   // tan
    };

    /// <summary>Get the connection-circle colour for a port type. Falls back to grey for unknown types.</summary>
    public static Color32 GetPortColor(Type dataType)
    {
        if (dataType != null && _portColors.TryGetValue(dataType, out var c)) return c;
        return new Color32(170, 170, 170, 255);
    }

    /// <summary>
    /// Compatibility check for connecting <paramref name="from"/>'s output to
    /// <paramref name="to"/>'s input. Phase-2 implementation is exact-type-match;
    /// Phase-3 will add implicit conversions (Float3 → Float4, scalar → vector, etc.).
    /// </summary>
    public static bool ArePortsCompatible(Port from, Port to)
    {
        if (from == null || to == null) return false;
        if (from.Direction != PortDirection.Output || to.Direction != PortDirection.Input) return false;
        if (from.DataType == to.DataType) return true;
        // Either side typed as object accepts the other side. The "from" being object
        // is the dynamic-port case used by GraphInputNode/GraphOutputNode and any
        // future generic relay-like node — they take whatever the wire happens to be.
        if (to.DataType == typeof(object)) return true;
        if (from.DataType == typeof(object)) return true;
        return false;
    }

    /// <summary>
    /// Hit-test a graph-space point against every port of <paramref name="node"/>.
    /// <paramref name="hitRadius"/> controls the catch zone; default is 2× the visual
    /// dot radius so users don't have to be pixel-perfect. Uses the node's registered
    /// renderer for port positions so custom shapes hit-test correctly.
    /// </summary>
    public static (Port port, int index)? HitTestPort(Node node, Float2 graphPoint, float? hitRadius = null)
    {
        node.EnsureDefined();
        float r = hitRadius ?? PortDotRadius * 2.2f;
        float r2 = r * r;
        var renderer = NodeRendererRegistry.GetRenderer(node);

        for (int i = 0; i < node.Inputs.Count; i++)
        {
            if (node.Inputs[i].IsHidden) continue;
            var p = renderer.GetPortPosition(node, node.Inputs[i]);
            if (Sq(graphPoint.X - p.X) + Sq(graphPoint.Y - p.Y) < r2)
                return (node.Inputs[i], i);
        }
        for (int i = 0; i < node.Outputs.Count; i++)
        {
            if (node.Outputs[i].IsHidden) continue;
            var p = renderer.GetPortPosition(node, node.Outputs[i]);
            if (Sq(graphPoint.X - p.X) + Sq(graphPoint.Y - p.Y) < r2)
                return (node.Outputs[i], i);
        }
        return null;
    }

    private static float Sq(float x) => x * x;

    /// <summary>
    /// Approximate point-to-bezier distance via 16-sample sweep. Returns the minimum
    /// distance² found between <paramref name="graphPoint"/> and the cubic bezier
    /// from <paramref name="from"/> to <paramref name="to"/> with the renderer's
    /// horizontal-tangent control points.
    /// </summary>
    public static float DistanceSqToWire(Float2 from, Float2 to, Float2 graphPoint)
        => DistanceSqToWire(from, to, graphPoint, out _);

    /// <summary>
    /// Same as <see cref="DistanceSqToWire(Float2,Float2,Float2)"/> but also returns the
    /// closest-point-on-the-wire in graph space — used by alt+click-split to place the
    /// new relay right on the wire rather than at the cursor.
    /// </summary>
    public static float DistanceSqToWire(Float2 from, Float2 to, Float2 graphPoint, out Float2 closestPoint)
    {
        float dx = MathF.Abs(to.X - from.X);
        float tangent = MathF.Max(40f, dx * 0.5f);
        Float2 c1 = new Float2(from.X + tangent, from.Y);
        Float2 c2 = new Float2(to.X - tangent, to.Y);

        float bestSq = float.MaxValue;
        closestPoint = from;
        // 32 samples — tighter than the 16 used originally so alt+click can land on a
        // wire with pixel-ish precision even at modest zoom. O(32) per edge is cheap.
        const int samples = 32;
        for (int i = 0; i <= samples; i++)
        {
            float t = i / (float)samples;
            // Cubic Bezier: B(t) = (1-t)³P0 + 3(1-t)²t·C1 + 3(1-t)t²·C2 + t³P1
            float u = 1f - t;
            float b0 = u * u * u, b1 = 3 * u * u * t, b2 = 3 * u * t * t, b3 = t * t * t;
            float bx = b0 * from.X + b1 * c1.X + b2 * c2.X + b3 * to.X;
            float by = b0 * from.Y + b1 * c1.Y + b2 * c2.Y + b3 * to.Y;
            float dSq = Sq(bx - graphPoint.X) + Sq(by - graphPoint.Y);
            if (dSq < bestSq)
            {
                bestSq = dSq;
                closestPoint = new Float2(bx, by);
            }
        }
        return bestSq;
    }
}
