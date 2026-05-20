// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.GraphTools;
using Prowl.Vector;

namespace Prowl.Editor.GraphTools;

/// <summary>
/// Per-window pan/zoom state for a graph editor viewport. Owns the view transform and
/// converts between three coordinate spaces:
/// <list type="bullet">
///   <item><b>Graph space</b> where nodes live (Node.Position units, persisted on the asset)</item>
///   <item><b>Canvas-local pixels</b> relative to the canvas Box's top-left, zero-indexed</item>
///   <item><b>Screen pixels</b> Paper's logical-pixel root frame; what <c>paper.PointerPos</c> returns</item>
/// </list>
/// The transform itself is two values: an integer-friendly pan offset (in canvas-local
/// pixels) and a scalar zoom (1.0 = nodes drawn at native size). Persists to/from
/// <see cref="Graph.ViewportPan"/> + <see cref="Graph.ViewportZoom"/> so reopening the
/// editor restores the view.
/// </summary>
public sealed class GraphCanvasView
{
    public const float MinZoom = 0.20f;
    public const float MaxZoom = 3.0f;
    public const float ZoomStep = 1.10f;

    private readonly Graph _graph;
    private Float2 _pan;
    private float _zoom;

    /// <summary>Current pan offset in canvas-local pixels (origin = top-left of canvas Box).</summary>
    public Float2 Pan
    {
        get => _pan;
        set { _pan = value; _graph.ViewportPan = value; }
    }

    /// <summary>Current zoom factor (clamped to [<see cref="MinZoom"/>, <see cref="MaxZoom"/>]).</summary>
    public float Zoom
    {
        get => _zoom;
        set { _zoom = Math.Clamp(value, MinZoom, MaxZoom); _graph.ViewportZoom = _zoom; }
    }

    public GraphCanvasView(Graph graph)
    {
        _graph = graph;
        _pan = graph.ViewportPan;
        _zoom = graph.ViewportZoom <= 0 ? 1f : graph.ViewportZoom;
    }

    // --- Coordinate conversion --------------------------------------------------------

    /// <summary>Graph-space point -> canvas-local pixels (top-left of canvas Box = origin).</summary>
    public Float2 GraphToCanvas(Float2 graphPos) => graphPos * _zoom + _pan;

    /// <summary>Canvas-local pixels -> graph-space point.</summary>
    public Float2 CanvasToGraph(Float2 canvasPos) => (canvasPos - _pan) / _zoom;

    /// <summary>
    /// Apply this view to a Quill canvas so subsequent draw calls are in graph-space units.
    /// Caller is responsible for <c>canvas.SaveState()</c> / <c>canvas.RestoreState()</c>
    /// around the call.
    /// </summary>
    public void ApplyTransform(Prowl.Quill.Canvas canvas)
    {
        canvas.TransformBy(Prowl.Vector.Spatial.Transform2D.CreateTranslation(_pan.X, _pan.Y));
        canvas.TransformBy(Prowl.Vector.Spatial.Transform2D.CreateScale(_zoom, _zoom));
    }

    // --- View manipulation ------------------------------------------------------------

    /// <summary>
    /// Translate the view by a delta in canvas-local pixels (right=positive X, down=positive Y).
    /// Pan is the user dragging the world; visually, nodes appear to move with the drag.
    /// </summary>
    public void PanBy(Float2 deltaCanvasPixels) => Pan = _pan + deltaCanvasPixels;

    /// <summary>
    /// Zoom by a multiplicative factor centered on a canvas-local pixel point. After zoom,
    /// the graph point that was under <paramref name="anchorCanvas"/> is still under it,
    /// so users can scroll-zoom toward whatever's under the cursor.
    /// </summary>
    public void ZoomBy(float factor, Float2 anchorCanvas)
    {
        var graphAnchor = CanvasToGraph(anchorCanvas);
        Zoom = _zoom * factor;
        // Re-derive pan so the anchor point in graph space lands back on its original screen pixel.
        Pan = anchorCanvas - graphAnchor * _zoom;
    }

    /// <summary>Reset to identity view (pan = 0, zoom = 1).</summary>
    public void ResetView() { Pan = Float2.Zero; Zoom = 1f; }

    /// <summary>
    /// Frame the given graph-space rectangle so it fits the canvas viewport (with padding).
    /// Used by "Recenter" / "Frame Selection" actions.
    /// </summary>
    public void FrameBounds(Float2 minGraph, Float2 maxGraph, Float2 viewportSize, float paddingPixels = 40f)
    {
        Float2 size = maxGraph - minGraph;
        if (size.X <= 0 || size.Y <= 0) { ResetView(); return; }

        float availW = MathF.Max(1f, viewportSize.X - paddingPixels * 2);
        float availH = MathF.Max(1f, viewportSize.Y - paddingPixels * 2);
        float zoom = MathF.Min(availW / size.X, availH / size.Y);
        Zoom = zoom; // clamps internally

        Float2 graphCenter = (minGraph + maxGraph) * 0.5f;
        Float2 viewportCenter = viewportSize * 0.5f;
        Pan = viewportCenter - graphCenter * _zoom;
    }
}
