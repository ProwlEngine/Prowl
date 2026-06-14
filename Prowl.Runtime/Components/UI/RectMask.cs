// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.UI;

/// <summary>
/// Axis-aligned rectangular mask backed by GPU scissor. Children outside the rect are
/// not drawn; children fully outside are culled at build time and produce no draw call.
/// </summary>
/// <remarks>
/// Compared to <see cref="Mask"/>:
/// <list type="bullet">
///   <item><b>Cheaper</b> - no extra draws, no fragment overdraw, no stencil buffer requirement.</item>
///   <item><b>Cull</b> - items whose bounding rect lies entirely outside the scissor are dropped from the tree.</item>
///   <item><b>Limitation</b> - the clip is axis-aligned in framebuffer space. Rotated UI hierarchies will
///         still get an axis-aligned clip (the rect's screen-space AABB).</item>
/// </list>
/// Use <see cref="RectMask"/> for scroll views, lists, and any other rectangular clipping;
/// fall back to <see cref="Mask"/> when you need an arbitrary or rounded shape.
/// </remarks>
[AddComponentMenu("UI/Rect Mask")]
[ExecuteAlways]
public class RectMask : UIBehaviour
{
    /// <summary>
    /// Per-side padding (in canvas pixels) shrinking the clip rect. Order: left, top, right, bottom.
    /// </summary>
    [SerializeField] private Float4 _padding;
    public Float4 Padding
    {
        get => _padding;
        set => SetField(ref _padding, value, UIDirtyFlags.Hierarchy);
    }

    /// <summary>
    /// Computes the clip rect in canvas-design pixels (the same space as <see cref="RectTransform.ComputedRect"/>),
    /// after applying <see cref="Padding"/>. Returns a degenerate rect if the RectTransform is missing.
    /// </summary>
    public Rect GetClipRectInCanvasPixels()
    {
        RectTransform? rt = GameObject.RectTransform;
        if (rt is null) return new Rect(0, 0, 0, 0);
        Rect r = rt.ComputedRect;
        return new Rect(
            r.Min.X + _padding.X,
            r.Min.Y + _padding.W,
            r.Max.X - _padding.Z,
            r.Max.Y - _padding.Y);
    }

    /// <summary>RectMask itself contributes no geometry - the work happens in <see cref="GameCanvas.BuildRecursive"/>.</summary>
    public override void GenerateMesh(UIMeshBuilder _, in UIContext __) { /* no geometry */ }
}
