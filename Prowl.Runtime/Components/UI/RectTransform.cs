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

    private static bool Approximately(float a, float b) => Maths.Abs(a - b) < 1e-6f;

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

        // Anchor positions in parent space
        float anchorMinX = parentX + AnchorMin.X * parentW;
        float anchorMinY = parentY + AnchorMin.Y * parentH;
        float anchorMaxX = parentX + AnchorMax.X * parentW;
        float anchorMaxY = parentY + AnchorMax.Y * parentH;

        float width, height, posX, posY;

        // Horizontal
        if (Approximately(AnchorMin.X, AnchorMax.X))
        {
            // Fixed width
            width = SizeDelta.X;
            float anchorX = anchorMinX;
            posX = anchorX + AnchoredPosition.X - Pivot.X * width;
        }
        else
        {
            // Stretch
            float left = anchorMinX + SizeDelta.X * 0.5f;
            float right = anchorMaxX - SizeDelta.X * 0.5f;
            width = right - left;
            posX = left + AnchoredPosition.X;
        }

        // Vertical (+Y up: anchorMinY is the lower edge, anchorMaxY the upper edge).
        if (Approximately(AnchorMin.Y, AnchorMax.Y))
        {
            // Fixed height
            height = SizeDelta.Y;
            float anchorY = anchorMinY;
            posY = anchorY + AnchoredPosition.Y - Pivot.Y * height;
        }
        else
        {
            // Stretch
            float lower = anchorMinY + SizeDelta.Y * 0.5f;
            float upper = anchorMaxY - SizeDelta.Y * 0.5f;
            height = upper - lower;
            posY = lower + AnchoredPosition.Y;
        }

        // Rect.Min is the bottom-left corner, Rect.Max the top-right (+Y up).
        ComputedRect = new Rect(posX, posY, posX + width, posY + height);
        return ComputedRect;
    }

    public void MarkLayoutDirty()
    {
        foreach (UIBehaviour ui in GameObject.GetComponents<UIBehaviour>())
            ui.MarkDirty(UIDirtyFlags.Layout | UIDirtyFlags.Vertices);
        GameObject.GetComponentInParent<GameCanvas>(includeSelf: true)?.MarkDirty(UIDirtyFlags.Layout);
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
