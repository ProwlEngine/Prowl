// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.UI;

/// <summary>
/// Clips descendant graphics to this element's rectangle. The clip is evaluated per-fragment in the
/// shader against a matrix that maps each fragment into the mask's local space, so it follows the
/// mask's rotation and scale (not just a screen-aligned box) and supports <see cref="CornerRadius"/>
/// rounded corners and a soft <see cref="Softness"/> edge. Items whose rect lies entirely outside the
/// mask are culled at build time so they cost nothing.
/// </summary>
[AddComponentMenu("UI/Rect Mask")]
[ExecuteAlways]
[ComponentIcon("")] // Crop
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

    /// <summary>Corner radius of the clip, in canvas pixels. 0 = sharp rectangle.</summary>
    [SerializeField] private float _cornerRadius;
    public float CornerRadius
    {
        get => _cornerRadius;
        set => SetField(ref _cornerRadius, Maths.Max(0f, value), UIDirtyFlags.Hierarchy);
    }

    /// <summary>Width of the soft alpha falloff at the clip edge, in canvas pixels. 0 = a crisp
    /// anti-aliased edge; larger values feather the mask.</summary>
    [SerializeField] private float _softness;
    public float Softness
    {
        get => _softness;
        set => SetField(ref _softness, Maths.Max(0f, value), UIDirtyFlags.Hierarchy);
    }

    /// <summary>
    /// Computes the clip rect in canvas-design pixels (the same space as <see cref="RectTransform.ComputedRect"/>),
    /// after applying <see cref="Padding"/>. Used for the coarse build-time cull. Returns a degenerate
    /// rect if the RectTransform is missing.
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

    /// <summary>RectMask itself contributes no geometry - the clip is applied in <see cref="GameCanvas.BuildRecursive"/>.</summary>
    public override void GenerateMesh(UIMeshBuilder _, in UIContext __) { /* no geometry */ }
}
