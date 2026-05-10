// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.UI;
using Prowl.Runtime.Rendering;

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
    [SerializeField] private Float2 _anchorMin = new(0.5f, 0.5f);
    public Float2 AnchorMin
    {
        get => _anchorMin;
        set { if (_anchorMin.Equals(value)) return; _anchorMin = value; MarkLayoutDirty(); }
    }

    /// <summary>
    /// The maximum anchor point (upper-right corner of the anchor rectangle).
    /// </summary>
    [SerializeField] private Float2 _anchorMax = new(0.5f, 0.5f);
    public Float2 AnchorMax
    {
        get => _anchorMax;
        set { if (_anchorMax.Equals(value)) return; _anchorMax = value; MarkLayoutDirty(); }
    }

    /// <summary>
    /// The pivot point of the element, in normalized coordinates (0–1).
    /// (0.5, 0.5) means the center.
    /// </summary>
    [SerializeField] private Float2 _pivot = new(0.5f, 0.5f);
    public Float2 Pivot
    {
        get => _pivot;
        set { if (_pivot.Equals(value)) return; _pivot = value; MarkLayoutDirty(); }
    }

    /// <summary>
    /// When the anchors are together, this represents the width and height of the rect.
    /// When the anchors are apart, this is the amount added to the anchor-defined size.
    /// </summary>
    [SerializeField] private Float2 _sizeDelta = new(100f, 100f);
    public Float2 SizeDelta
    {
        get => _sizeDelta;
        set { if (_sizeDelta.Equals(value)) return; _sizeDelta = value; MarkLayoutDirty(); }
    }

    /// <summary>
    /// The position of the pivot relative to the anchor reference point, in pixels.
    /// </summary>
    [SerializeField] private Float2 _anchoredPosition = Float2.Zero;
    public Float2 AnchoredPosition
    {
        get => _anchoredPosition;
        set { if (_anchoredPosition.Equals(value)) return; _anchoredPosition = value; MarkLayoutDirty(); }
    }

    /// <summary>
    /// The computed screen-space rect after layout, set by the GameCanvas during tree construction.
    /// </summary>
    [SerializeIgnore]
    public Rect ComputedRect;

    private static bool Approximately(float a, float b) => Maths.Abs(a - b) < 1e-6f;

    public override Float4x4 WorldToLocalMatrix => LocalToWorldMatrix.Invert();

    public override Float4x4 LocalToWorldMatrix
    {
        get
        {
            Float4x4 t = Float4x4.CreateTRS(new Float3(AnchoredPosition, 0), LocalRotation, LocalScale);
            return Parent != null ? (Parent.LocalToWorldMatrix * t) : t;
        }
    }

    /// <summary>
    /// Builds a TRS matrix that places a unit XY quad (local +Z facing camera)
    /// exactly over the projected rectangle defined by the four world‑space corners.
    /// Corners must follow the order: 0 = top‑left, 1 = top‑right,
    /// 2 = bottom‑right, 3 = bottom‑left.
    /// </summary>
    public static Float4x4 TRSFromCorners(Float3[] corners)
    {
        Float3 right   = Float3.Normalize(corners[1] - corners[0]);
        Float3 up      = Float3.Normalize(corners[0] - corners[3]);
        Float3 forward = Float3.Cross(right, up);

        // Ensure forward is really unit length (it should be if corners form a rectangle)
        forward = Float3.Normalize(forward);

        Float3 center = (corners[0] + corners[2]) * 0.5f;

        // z offset to draw all elements
        center += forward * 0.1f;


        //Debug.Log($"Calculate width: {corners[1]} <===> {corners[0]}");
        float width  = Float3.Distance(corners[1], corners[0]);
        //Debug.Log($"Calculate height: {corners[3]} <===> {corners[0]}");
        float height = Float3.Distance(corners[3], corners[0]);

        Float4x4 rotMatrix = new Float4x4(
            right.X,  right.Y,  right.Z,  0,
            up.X,     up.Y,     up.Z,     0,
            forward.X,forward.Y,forward.Z, 0,
            0,        0,        0,         1);

        Quaternion rotation = Quaternion.FromMatrix(rotMatrix);
        Float3 scale = new Float3(width, height, 1f);

        Debug.Log($"Calculate position/scale: {center}/{scale}");

        return Float4x4.CreateTRS(center, rotation, scale);
    }

    public Float4x4 GetCanvasMatrix(ViewerData cameraData)
    {
        if (GetWorldCornersOnNearPlane(cameraData, out var corners))
        {
            Float4x4 t = TRSFromCorners(corners);
            return Parent != null ? (Parent.LocalToWorldMatrix * t) : t;
        }

        return LocalToWorldMatrix;
    }

    /// <summary>
    /// Returns the four world‑space corners of this RectTransform on the camera’s near plane.
    /// Uses the camera’s base projection (equivalent to ScreenPointToRay).
    /// </summary>
    public bool GetWorldCornersOnNearPlane(ViewerData cameraData, out Float3[] corners)
    {
        corners = new Float3[4];

        float w = cameraData.PixelWidth;
        float h = cameraData.PixelHeight;
        if (w <= 0 || h <= 0) return false;

        var screenSize = new Float2(w, h);
        var screenCorners = new Float2[]
        {
            ComputedRect.Min,                                          // top‑left
            new Float2(ComputedRect.Max.X, ComputedRect.Min.Y),        // top‑right
            ComputedRect.Max,                                          // bottom‑right
            new Float2(ComputedRect.Min.X, ComputedRect.Max.Y)         // bottom‑left
        };

        ComputeRect(new Rect(0,0,1920,1080));

        for (int i = 0; i < 4; i++)
        {
            var corner = screenCorners[i];
            var vpMatrix = cameraData.ProjectionMatrix * cameraData.ViewMatrix; // or NonJitteredProjectionMatrix
            var invVP = vpMatrix.Invert();

            // For each corner (x,y), compute NDC:
            float ndcX = (2f * corner.X) / w - 1f;
            float ndcY = 1f - (2f * corner.Y) / h; // flip Y
            float ndcZ = 0f; // near plane in Prowl’s NDC (0…1)

            var hPoint = invVP * new Float4(ndcX, ndcY, ndcZ, 1f);
            var worldPos = hPoint.XYZ / hPoint.W;
            //Debug.Log($"Corner {i}: {corner} -> {worldPos}");
            corners[i] = worldPos;
        }
        return true;
    }

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

    private void MarkLayoutDirty()
    {
        foreach (UIBehaviour ui in GameObject.GetComponents<UIBehaviour>())
            ui.MarkDirty(UIDirtyFlags.Layout | UIDirtyFlags.Vertices);
        GameObject.GetComponentInParent<GameCanvas>(includeSelf: true)?.MarkDirty(UIDirtyFlags.Layout);
    }
}
