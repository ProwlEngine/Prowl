// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.UI;
using Prowl.Runtime.Rendering;

namespace Prowl.Vector;

/// <summary>
/// A component that stores anchor, pivot, and size information for 2D UI layout. It is a standalone
/// component (not a <see cref="Transform"/> subclass), required by every UI element; rotation, scale
/// and Z come from the GameObject's regular Transform.
/// </summary>
/// <remarks>
/// <para>
/// The UI coordinate system is <b>+Y up</b>: anchors, pivots and
/// <see cref="AnchoredPosition"/> all grow upward, so increasing
/// <c>AnchoredPosition.Y</c> moves an element toward the top of the screen.
/// </para>
/// <para>
/// <b>Anchors</b> define how the element's edges attach to its parent rect.
/// Values are normalized (0-1): (0,0) is bottom-left, (1,1) is top-right.
/// When <see cref="AnchorMin"/> == <see cref="AnchorMax"/>, the element has a
/// fixed size controlled by <see cref="SizeDelta"/>. When they differ, the
/// element stretches to fill the anchor range and <see cref="SizeDelta"/>
/// acts as a padding offset.
/// </para>
/// <para>
/// <b>Pivot</b> is the local origin of the element (0-1). (0,0) is bottom-left,
/// (1,1) is top-right, (0.5, 0.5) is center.
/// </para>
/// </remarks>
public sealed class RectTransform : MonoBehaviour
{
    /// <summary>
    /// The minimum anchor point (lower-left corner of the anchor rectangle).
    /// </summary>
    [SerializeField] private Float2 _anchorMin = new(0.5f, 0.5f);
    public Float2 AnchorMin
    {
        get => _anchorMin;
        set => SetField(ref _anchorMin, value);
    }

    /// <summary>
    /// The maximum anchor point (upper-right corner of the anchor rectangle).
    /// </summary>
    [SerializeField] private Float2 _anchorMax = new(0.5f, 0.5f);
    public Float2 AnchorMax
    {
        get => _anchorMax;
        set => SetField(ref _anchorMax, value);
    }

    /// <summary>
    /// The pivot point of the element, in normalized coordinates (0-1).
    /// (0.5, 0.5) means the center.
    /// </summary>
    [SerializeField] private Float2 _pivot = new(0.5f, 0.5f);
    public Float2 Pivot
    {
        get => _pivot;
        set => SetField(ref _pivot, value);
    }

    /// <summary>
    /// When the anchors are together, this represents the width and height of the rect.
    /// When the anchors are apart, this is the amount added to the anchor-defined size.
    /// </summary>
    [SerializeField] private Float2 _sizeDelta = new(100f, 100f);
    public Float2 SizeDelta
    {
        get => _sizeDelta;
        set => SetField(ref _sizeDelta, value);
    }

    /// <summary>
    /// The position of the pivot relative to the anchor reference point, in pixels.
    /// </summary>
    [SerializeField] private Float2 _anchoredPosition = Float2.Zero;
    public Float2 AnchoredPosition
    {
        get => _anchoredPosition;
        set => SetField(ref _anchoredPosition, value);
    }

    /// <summary>
    /// The computed screen-space rect after layout, set by the GameCanvas during tree construction.
    /// </summary>
    [SerializeIgnore]
    public Rect ComputedRect;

    // Rotation / scale / Z live on the GameObject's regular Transform; the RectTransform proxies them so
    // UI code (layout, gizmos, inspector) reads them through one place while anchors/pivot/size drive XY.
    public Quaternion LocalRotation { get => Transform.LocalRotation; set => Transform.LocalRotation = value; }
    public Float3 LocalScale { get => Transform.LocalScale; set => Transform.LocalScale = value; }
    public Float3 LocalPosition { get => Transform.LocalPosition; set => Transform.LocalPosition = value; }


    /// <summary>
    /// Computes the pixel rect of this element given the parent's pixel rect.
    /// </summary>
    /// <param name="parentRect">The parent's screen-space rect.</param>
    /// <returns>The computed rect in screen-space pixels.</returns>
    public Rect ComputeRect(Rect parentRect)
    {
        float parentX = parentRect.Min.X;
        float parentY = parentRect.Min.Y;
        float parentW = parentRect.Size.X;
        float parentH = parentRect.Size.Y;

        // Anchor positions in parent space (+Y up: min is the lower edge, max the upper edge).
        float anchorMinX = parentX + AnchorMin.X * parentW;
        float anchorMinY = parentY + AnchorMin.Y * parentH;
        float anchorMaxX = parentX + AnchorMax.X * parentW;
        float anchorMaxY = parentY + AnchorMax.Y * parentH;

        // One formula for both fixed and stretched anchors:
        //   size       = anchorSpan + sizeDelta
        //   min-corner = anchorMin + anchoredPosition - pivot * sizeDelta
        // When the anchors coincide, anchorSpan is 0 so sizeDelta is the literal size and the pivot
        // places it about the anchor point. When they differ, sizeDelta pads the anchor span, split by
        // the pivot (not symmetrically) so a non-center pivot on a stretched rect stays put. No branch,
        // no anchor-equality epsilon.
        float width  = (anchorMaxX - anchorMinX) + SizeDelta.X;
        float height = (anchorMaxY - anchorMinY) + SizeDelta.Y;
        float posX = anchorMinX + AnchoredPosition.X - Pivot.X * SizeDelta.X;
        float posY = anchorMinY + AnchoredPosition.Y - Pivot.Y * SizeDelta.Y;

        // Rect.Min is the bottom-left corner, Rect.Max the top-right (+Y up).
        ComputedRect = new Rect(posX, posY, posX + width, posY + height);
        return ComputedRect;
    }

    // ============================================================
    // Derived rect accessors
    // ============================================================

    /// <summary>Which parent edge <see cref="SetInsetAndSizeFromParentEdge"/> anchors to.</summary>
    public enum Edge { Left, Right, Top, Bottom }

    /// <summary>Axis selector for <see cref="SetSizeWithCurrentAnchors"/>.</summary>
    public enum Axis { Horizontal, Vertical }

    /// <summary>
    /// The laid-out rect in this element's own space, with the pivot at the origin - the same space
    /// meshes are generated in. Valid after the owning canvas has run its layout.
    /// </summary>
    public Rect Rect
    {
        get
        {
            Float2 size = ComputedRect.Size;
            return new Rect(-_pivot.X * size.X, -_pivot.Y * size.Y,
                            (1f - _pivot.X) * size.X, (1f - _pivot.Y) * size.Y);
        }
    }

    /// <summary>Offset of the lower-left corner from the lower-left anchor. Setting it moves that
    /// corner, resizing the element rather than translating it.</summary>
    public Float2 OffsetMin
    {
        get => _anchoredPosition - new Float2(_sizeDelta.X * _pivot.X, _sizeDelta.Y * _pivot.Y);
        set
        {
            Float2 delta = value - OffsetMin;
            SizeDelta = _sizeDelta - delta;
            AnchoredPosition = _anchoredPosition + new Float2(delta.X * (1f - _pivot.X), delta.Y * (1f - _pivot.Y));
        }
    }

    /// <summary>Offset of the upper-right corner from the upper-right anchor. Setting it moves that
    /// corner, resizing the element rather than translating it.</summary>
    public Float2 OffsetMax
    {
        get => _anchoredPosition + new Float2(_sizeDelta.X * (1f - _pivot.X), _sizeDelta.Y * (1f - _pivot.Y));
        set
        {
            Float2 delta = value - OffsetMax;
            SizeDelta = _sizeDelta + delta;
            AnchoredPosition = _anchoredPosition + new Float2(delta.X * _pivot.X, delta.Y * _pivot.Y);
        }
    }

    /// <summary><see cref="AnchoredPosition"/> plus the Transform's Z, which is the one positional axis
    /// the layout does not drive.</summary>
    public Float3 AnchoredPosition3D
    {
        get => new(_anchoredPosition.X, _anchoredPosition.Y, LocalPosition.Z);
        set
        {
            LocalPosition = new Float3(LocalPosition.X, LocalPosition.Y, value.Z);
            AnchoredPosition = new Float2(value.X, value.Y);
        }
    }

    /// <summary>
    /// Pins the element to one parent edge at a fixed inset and size on that axis, collapsing the
    /// anchors on it. The other axis keeps whatever anchoring it had.
    /// </summary>
    public void SetInsetAndSizeFromParentEdge(Edge edge, float inset, float size)
    {
        bool horizontal = edge is Edge.Left or Edge.Right;
        bool atMax = edge is Edge.Right or Edge.Top;
        float anchor = atMax ? 1f : 0f;

        Float2 min = _anchorMin, max = _anchorMax, sd = _sizeDelta, ap = _anchoredPosition;
        if (horizontal)
        {
            min.X = max.X = anchor;
            sd.X = size;
            ap.X = atMax ? -inset - size * (1f - _pivot.X) : inset + size * _pivot.X;
        }
        else
        {
            min.Y = max.Y = anchor;
            sd.Y = size;
            ap.Y = atMax ? -inset - size * (1f - _pivot.Y) : inset + size * _pivot.Y;
        }

        AnchorMin = min;
        AnchorMax = max;
        SizeDelta = sd;
        AnchoredPosition = ap;
    }

    /// <summary>Resizes one axis to an absolute pixel size without touching the anchors, by solving
    /// for the <see cref="SizeDelta"/> that produces it against the current anchor span.</summary>
    public void SetSizeWithCurrentAnchors(Axis axis, float size)
    {
        Float2 parent = ParentSize();
        Float2 sd = _sizeDelta;
        if (axis == Axis.Horizontal) sd.X = size - parent.X * (_anchorMax.X - _anchorMin.X);
        else                          sd.Y = size - parent.Y * (_anchorMax.Y - _anchorMin.Y);
        SizeDelta = sd;
    }

    /// <summary>The four corners of <see cref="Rect"/> in this element's own space, ordered
    /// bottom-left, top-left, top-right, bottom-right.</summary>
    public void GetLocalCorners(Float3[] fourCorners)
    {
        if (fourCorners is null || fourCorners.Length < 4) return;
        Rect r = Rect;
        fourCorners[0] = new Float3(r.Min.X, r.Min.Y, 0f);
        fourCorners[1] = new Float3(r.Min.X, r.Max.Y, 0f);
        fourCorners[2] = new Float3(r.Max.X, r.Max.Y, 0f);
        fourCorners[3] = new Float3(r.Max.X, r.Min.Y, 0f);
    }

    /// <summary>The four corners in world space, in the same order as <see cref="GetLocalCorners"/>.
    /// Leaves the array untouched when the element is not under a canvas.</summary>
    public void GetWorldCorners(Float3[] fourCorners)
    {
        if (fourCorners is null || fourCorners.Length < 4) return;

        GameCanvas? canvas = GameObject.GetComponentInParent<GameCanvas>(includeSelf: true);
        if (canvas.IsNotValid()) return;

        GetLocalCorners(fourCorners);
        Float4x4 model = canvas.CanvasToWorld * canvas.BuildRectModel(this);
        for (int i = 0; i < 4; i++)
            fourCorners[i] = Float4x4.TransformPoint(fourCorners[i], model);
    }

    /// <summary>Rebuilds the owning canvas immediately so <see cref="ComputedRect"/> reflects
    /// changes made this frame, instead of waiting for the next render.</summary>
    public void ForceUpdateRectTransforms()
    {
        GameCanvas? canvas = GameObject.GetComponentInParent<GameCanvas>(includeSelf: true);
        if (canvas.IsValid()) canvas.RebuildIfDirty();
    }

    /// <summary>
    /// Laid-out size of the rect this element anchors against. Walks up past any ancestor without a
    /// RectTransform, because the canvas passes its parent rect straight through those, and ends at the
    /// canvas root rect.
    /// </summary>
    private Float2 ParentSize()
    {
        for (GameObject? node = GameObject.Parent; node != null; node = node.Parent)
        {
            if (node.RectTransform is { } prt) return prt.ComputedRect.Size;
            GameCanvas? canvas = node.GetComponent<GameCanvas>();
            if (canvas.IsValid()) return canvas.RootRect.Size;
        }
        return Float2.Zero;
    }

    public void MarkLayoutDirty()
    {
        foreach (UIBehaviour ui in GameObject.GetComponents<UIBehaviour>())
            ui.MarkDirty(UIDirtyFlags.Layout | UIDirtyFlags.Vertices);
        var canvas = GameObject.GetComponentInParent<GameCanvas>(includeSelf: true);
        if (canvas.IsValid()) canvas.MarkDirty(UIDirtyFlags.Layout);
    }

    /// <summary>
    /// Backing-field setter for the layout properties above. Assigns only on a real change
    /// and flags the owning elements + canvas for a layout rebuild. Mirrors
    /// <see cref="UIBehaviour.SetField{T}"/> - the single value-change check for the UI,
    /// shared by code, inspector edits, and undo (via <c>PropertyGrid.ApplyFieldValue</c>).
    /// </summary>
    private bool SetField<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        MarkLayoutDirty();
        return true;
    }
}
