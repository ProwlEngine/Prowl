// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.UI;

/// <summary>
/// Scene-view gizmo helpers for UI elements. Draws the rect outline, pivot marker
/// and anchor handles for a <see cref="UIBehaviour"/>'s <see cref="RectTransform"/>,
/// or the canvas outline for a <see cref="GameCanvas"/>.
/// </summary>
/// <remarks>
/// Only emits gizmos for <see cref="RenderMode.WorldSpace"/> canvases. Overlay /
/// ScreenSpaceCamera UI lives in screen space and has no meaningful position in
/// the editor scene view.
/// </remarks>
internal static class UIGizmos
{
    public static readonly Color SelectedColor   = new Color(0.20f, 0.95f, 0.40f, 1.00f);  // bright green
    public static readonly Color UnselectedColor = new Color(0.20f, 0.95f, 0.40f, 0.25f);  // dim green
    public static readonly Color PivotColor      = new Color(0.20f, 0.80f, 1.00f, 1.00f);  // cyan
    public static readonly Color AnchorColor     = new Color(1.00f, 0.85f, 0.20f, 1.00f);  // amber

    /// <summary>
    /// Draws the outline of <paramref name="ui"/>'s rect in the scene view.
    /// Optionally draws the pivot marker and the four anchor handles inside the
    /// parent rect.
    /// </summary>
    public static void DrawRect(UIBehaviour ui, Color color, bool drawPivot, bool drawAnchors)
    {
        GameCanvas? canvas = ui.GetCanvas();
        if (canvas is null || canvas.RenderMode != RenderMode.WorldSpace) return;

        RectTransform? rt = ui.GameObject.RectTransform;
        if (rt is null) return;

        // Layout might be stale (gizmos can fire before the world-space canvas has
        // been collected this frame). RebuildIfDirty is a no-op when clean.
        canvas.RebuildIfDirty();

        Rect cr = rt.ComputedRect;
        if (cr.Size.X <= 0 || cr.Size.Y <= 0) return;

        Float2 pivot = rt.Pivot;
        float w = cr.Size.X;
        float h = cr.Size.Y;

        // Element-local pivot-centered space — matches UIImage.GenerateMesh /
        // TextComponent.GenerateMesh.
        Float3 ltl = new Float3(-pivot.X * w,        -pivot.Y * h,        0);
        Float3 ltr = new Float3((1 - pivot.X) * w,   -pivot.Y * h,        0);
        Float3 lbr = new Float3((1 - pivot.X) * w,   (1 - pivot.Y) * h,   0);
        Float3 lbl = new Float3(-pivot.X * w,        (1 - pivot.Y) * h,   0);

        Float4x4 model = canvas.BuildItemModel(ui);
        Float3 tl = Float4x4.TransformPoint(ltl, model);
        Float3 tr = Float4x4.TransformPoint(ltr, model);
        Float3 br = Float4x4.TransformPoint(lbr, model);
        Float3 bl = Float4x4.TransformPoint(lbl, model);

        Debug.DrawLine(tl, tr, color);
        Debug.DrawLine(tr, br, color);
        Debug.DrawLine(br, bl, color);
        Debug.DrawLine(bl, tl, color);

        // In-plane axes for any sub-handles. Fall back to identity if degenerate.
        Float3 rightW = tr - tl;
        Float3 downW  = bl - tl;
        float rLen = Float3.Length(rightW);
        float dLen = Float3.Length(downW);
        if (rLen < 1e-6f || dLen < 1e-6f) return;
        Float3 rightU = rightW / rLen;
        Float3 downU  = downW  / dLen;

        if (drawPivot)
        {
            // Pivot is the origin in element-local space — TransformPoint(Zero, model) gives world.
            Float3 pivotW = Float4x4.TransformPoint(Float3.Zero, model);
            float r = Maths.Max(Maths.Min(rLen, dLen) * 0.05f, 1e-4f);
            Debug.DrawLine(pivotW - rightU * r, pivotW + rightU * r, PivotColor);
            Debug.DrawLine(pivotW - downU  * r, pivotW + downU  * r, PivotColor);
            Float3 normal = Float3.Normalize(Float3.Cross(rightU, downU));
            Debug.DrawWireCircle(pivotW, normal, r * 0.6f, PivotColor, 16);
        }

        if (drawAnchors)
            DrawAnchorHandles(ui, canvas, rt, rightU, downU);
    }

    /// <summary>
    /// Draws four small triangle handles inside the parent rect at this element's
    /// AnchorMin / AnchorMax positions. When AnchorMin == AnchorMax the four
    /// handles collapse to a single quartet at the same point (Unity behavior).
    /// </summary>
    private static void DrawAnchorHandles(UIBehaviour ui, GameCanvas canvas, RectTransform rt, Float3 rightU, Float3 downU)
    {
        GameObject? parent = ui.GameObject.Parent;
        if (parent is null) return;

        RectTransform? prt = parent.RectTransform;
        if (prt is null) return;

        Rect pr = prt.ComputedRect;
        if (pr.Size.X <= 0 || pr.Size.Y <= 0) return;

        // Anchor positions in the parent's canvas-design-pixel space.
        float aminX = pr.Min.X + rt.AnchorMin.X * pr.Size.X;
        float amaxX = pr.Min.X + rt.AnchorMax.X * pr.Size.X;
        float aminY = pr.Min.Y + rt.AnchorMin.Y * pr.Size.Y;
        float amaxY = pr.Min.Y + rt.AnchorMax.Y * pr.Size.Y;

        // Build the parent's model matrix so handles sit on the parent's plane.
        Float4x4 parentModel = BuildRectModel(canvas, prt);

        // Convert anchor positions to parent's pivot-centered space (matches
        // BuildRectModel's translation), then through the parent model.
        Float2 ppivot = prt.Pivot;
        float pivotPx = pr.Min.X + ppivot.X * pr.Size.X;
        float pivotPy = pr.Min.Y + ppivot.Y * pr.Size.Y;

        Float3 a00 = Float4x4.TransformPoint(new Float3(aminX - pivotPx, aminY - pivotPy, 0), parentModel);
        Float3 a10 = Float4x4.TransformPoint(new Float3(amaxX - pivotPx, aminY - pivotPy, 0), parentModel);
        Float3 a11 = Float4x4.TransformPoint(new Float3(amaxX - pivotPx, amaxY - pivotPy, 0), parentModel);
        Float3 a01 = Float4x4.TransformPoint(new Float3(aminX - pivotPx, amaxY - pivotPy, 0), parentModel);

        // Handle size — proportional to one design pixel through the parent model.
        Float3 originW = Float4x4.TransformPoint(Float3.Zero, parentModel);
        float pixelW = Float3.Length(Float4x4.TransformPoint(new Float3(1, 0, 0), parentModel) - originW);
        float size = pixelW * 8f; // 8 design pixels — readable at typical zoom

        DrawAnchorTriangle(a00, rightU, downU, size, +1, +1);
        DrawAnchorTriangle(a10, rightU, downU, size, -1, +1);
        DrawAnchorTriangle(a11, rightU, downU, size, -1, -1);
        DrawAnchorTriangle(a01, rightU, downU, size, +1, -1);
    }

    /// <summary>
    /// Builds the world-space matrix that maps a RectTransform's pivot-centered
    /// design-pixel space to world space. Mirrors <see cref="GameCanvas.BuildItemModel"/>'s
    /// WorldSpace branch; needed because BuildItemModel takes a UIBehaviour, not a Transform.
    /// </summary>
    private static Float4x4 BuildRectModel(GameCanvas canvas, RectTransform rt)
    {
        Rect cr = rt.ComputedRect;
        Float2 pivot = rt.Pivot;
        float pivotX = cr.Min.X + pivot.X * cr.Size.X;
        float pivotY = cr.Min.Y + pivot.Y * cr.Size.Y;

        Float4x4 elementTRS = Float4x4.CreateTRS(
            new Float3(pivotX, pivotY, 0),
            rt.LocalRotation,
            rt.LocalScale);

        float rpu = Maths.Max(canvas.ReferencePixelsPerUnit, 0.001f);
        return canvas.Transform.LocalToWorldMatrix
             * Float4x4.CreateScale(1f / rpu)
             * elementTRS;
    }

    /// <summary>
    /// Draws a small right-triangle handle whose right-angle vertex sits at
    /// <paramref name="cornerWorld"/> and whose legs point along the rect axes.
    /// <paramref name="sx"/>/<paramref name="sy"/> point the legs into the parent rect.
    /// </summary>
    private static void DrawAnchorTriangle(Float3 cornerWorld, Float3 rightU, Float3 downU, float size, int sx, int sy)
    {
        Float3 a = cornerWorld;
        Float3 b = cornerWorld + rightU * (sx * size);
        Float3 c = cornerWorld + downU  * (sy * size);

        Debug.DrawLine(a, b, AnchorColor);
        Debug.DrawLine(b, c, AnchorColor);
        Debug.DrawLine(c, a, AnchorColor);
    }

    /// <summary>
    /// Draws the canvas root rect in the scene view. Mirrors <see cref="DrawRect"/>
    /// but uses the canvas's own RectTransform (the canvas itself doesn't supply a
    /// UIBehaviour to feed BuildItemModel).
    /// </summary>
    public static void DrawCanvasRect(GameCanvas canvas, Color color)
    {
        if (canvas.RenderMode != RenderMode.WorldSpace) return;

        RectTransform? rt = canvas.GameObject.RectTransform;
        if (rt is null) return;

        canvas.RebuildIfDirty();

        Rect cr = rt.ComputedRect;
        if (cr.Size.X <= 0 || cr.Size.Y <= 0) return;

        Float4x4 model = BuildRectModel(canvas, rt);

        Float2 pivot = rt.Pivot;
        float pivotX = cr.Min.X + pivot.X * cr.Size.X;
        float pivotY = cr.Min.Y + pivot.Y * cr.Size.Y;
        // Corners in pivot-centered space, matching BuildRectModel's translation.
        float minX = cr.Min.X - pivotX;
        float minY = cr.Min.Y - pivotY;
        float maxX = cr.Max.X - pivotX;
        float maxY = cr.Max.Y - pivotY;

        Float3 tl = Float4x4.TransformPoint(new Float3(minX, minY, 0), model);
        Float3 tr = Float4x4.TransformPoint(new Float3(maxX, minY, 0), model);
        Float3 br = Float4x4.TransformPoint(new Float3(maxX, maxY, 0), model);
        Float3 bl = Float4x4.TransformPoint(new Float3(minX, maxY, 0), model);

        Debug.DrawLine(tl, tr, color);
        Debug.DrawLine(tr, br, color);
        Debug.DrawLine(br, bl, color);
        Debug.DrawLine(bl, tl, color);
    }
}
