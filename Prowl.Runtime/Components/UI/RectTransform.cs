// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;

namespace Prowl.Vector;

/// <summary>
/// A <see cref="Transform"/> subclass that stores anchor, pivot, and size
/// information for 2D UI layout, analogous to Unity's <c>RectTransform</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Anchors</b> define how the element's edges attach to its parent rect.
/// Values are normalized (0–1): (0,0) is top-left, (1,1) is bottom-right.
/// When <see cref="AnchorMin"/> == <see cref="AnchorMax"/>, the element has a
/// fixed size controlled by <see cref="SizeDelta"/>. When they differ, the
/// element stretches to fill the anchor range and <see cref="SizeDelta"/>
/// acts as a padding offset.
/// </para>
/// <para>
/// <b>Pivot</b> is the local origin of the element (0–1). (0.5, 0.5) is center.
/// </para>
/// </remarks>
public class RectTransform : Transform
{
    /// <summary>
    /// The minimum anchor point (lower-left corner of the anchor rectangle).
    /// </summary>
    [SerializeField]
    public Float2 AnchorMin = new(0.5f, 0.5f);

    /// <summary>
    /// The maximum anchor point (upper-right corner of the anchor rectangle).
    /// </summary>
    [SerializeField]
    public Float2 AnchorMax = new(0.5f, 0.5f);

    /// <summary>
    /// The pivot point of the element, in normalized coordinates (0–1).
    /// (0.5, 0.5) means the center.
    /// </summary>
    [SerializeField]
    public Float2 Pivot = new(0.5f, 0.5f);

    /// <summary>
    /// When the anchors are together, this represents the width and height of the rect.
    /// When the anchors are apart, this is the amount added to the anchor-defined size.
    /// </summary>
    [SerializeField]
    public Float2 SizeDelta = new(100f, 100f);

    /// <summary>
    /// The position of the pivot relative to the anchor reference point, in pixels.
    /// </summary>
    [SerializeField]
    public Float2 AnchoredPosition = Float2.Zero;

    /// <summary>
    /// The computed screen-space rect after layout, set by the GameCanvas during tree construction.
    /// </summary>
    [SerializeIgnore]
    public Rect ComputedRect;

    private static bool Approximately(float a, float b) => Maths.Abs(a - b) < 1e-6f;

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

        // Vertical
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
            float top = anchorMinY + SizeDelta.Y * 0.5f;
            float bottom = anchorMaxY - SizeDelta.Y * 0.5f;
            height = bottom - top;
            posY = top + AnchoredPosition.Y;
        }

        ComputedRect = new Rect(posX, posY, posX + width, posY + height);
        return ComputedRect;
    }
}
