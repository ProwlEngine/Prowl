// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.UI;

internal static class UIRaycaster
{
    /// <summary>Result of a successful pointer hit.</summary>
    public readonly struct Hit
    {
        public readonly GameObject GameObject;
        public readonly GameCanvas Canvas;
        /// <summary>Pointer position in the hit canvas's design-pixel space (+Y up, origin bottom-left).</summary>
        public readonly Float2 DesignPosition;

        public Hit(GameObject go, GameCanvas canvas, Float2 designPos)
        {
            GameObject = go;
            Canvas = canvas;
            DesignPosition = designPos;
        }
    }

    public static bool TryPick(Scene? scene, Float2 screenPos, Float2 windowSize, out Hit hit)
    {
        hit = default;
        if (scene == null) return false;

        GameObject? bestGO = null;
        GameCanvas? bestCanvas = null;
        Float2 bestDesign = Float2.Zero;
        int bestSortOrder = int.MinValue;
        int bestDfs = -1;

        foreach (GameObject go in scene.ActiveObjects)
        {
            GameCanvas? canvas = go.GetComponent<GameCanvas>();
            if (canvas is null || !canvas.EnabledInHierarchy) continue;

            // Only screen-space canvases participate in the runtime raycaster - World canvases
            // need a 3D ray from a Camera, which lives in TryPickWorld below.
            if (canvas.RenderMode == RenderMode.WorldSpace) continue;

            canvas.RebuildIfDirty();

            if (!ScreenToDesign(canvas, screenPos, windowSize, out Float2 designPt)) continue;

            int dfs = 0;
            GameObject? localHit = null;
            int localDfs = -1;
            WalkRecurse(canvas, canvas.GameObject, designPt, scissor: null, ref dfs, ref localHit, ref localDfs);
            if (localHit == null) continue;

            // Higher SortOrder wins; ties resolved by deeper DFS index (drawn on top).
            bool wins =
                canvas.SortOrder > bestSortOrder ||
                (canvas.SortOrder == bestSortOrder && localDfs > bestDfs);

            if (wins)
            {
                bestGO = localHit;
                bestCanvas = canvas;
                bestDesign = designPt;
                bestSortOrder = canvas.SortOrder;
                bestDfs = localDfs;
            }
        }

        if (bestGO == null || bestCanvas == null) return false;
        hit = new Hit(bestGO, bestCanvas, bestDesign);
        return true;
    }

    private static bool ScreenToDesign(GameCanvas canvas, Float2 screenPos, Float2 windowSize, out Float2 designPt)
    {
        float scale = canvas.ScaleFactor;
        if (scale < 0.001f) { designPt = Float2.Zero; return false; }

        designPt = new Float2(screenPos.X / scale, (windowSize.Y - screenPos.Y) / scale);
        return true;
    }

    private static void WalkRecurse(GameCanvas canvas, GameObject parent, Float2 pt, Rect? scissor, ref int dfs, ref GameObject? bestGO, ref int bestDfs)
    {
        foreach (GameObject child in parent.Children)
        {
            if (!child.EnabledInHierarchy) continue;
            if (child.GetComponent<GameCanvas>() != null) continue; // nested canvas owns its own tree

            Rect? childScissor = scissor;
            RectMask? rectMask = child.GetComponent<RectMask>();
            if (rectMask != null && rectMask.EnabledInHierarchy)
            {
                Rect mr = rectMask.GetClipRectInCanvasPixels();
                childScissor = scissor is null ? mr : IntersectRect(scissor.Value, mr);
                if (childScissor.Value.Size.X <= 0f || childScissor.Value.Size.Y <= 0f) continue;
            }
            if (childScissor is { } cs && !RectContainsPoint(cs, pt))
            {
                // Pointer falls outside the active scissor - every descendant inherits this scissor,
                // so none of them can possibly hit. Skip the whole subtree.
                continue;
            }

            // A CanvasGroup with BlocksRaycasts off makes the whole subtree transparent
            // to the pointer - children still draw, but they don't consume input.
            CanvasGroup? grp = child.GetComponent<CanvasGroup>();
            bool blocked = grp == null || grp.BlocksRaycasts;

            if (blocked)
            {
                RectTransform? rt = child.RectTransform;
                if (rt != null)
                {
                    bool inside = ContainsCanvasPoint(canvas, rt, pt);

                    foreach (UIBehaviour ui in child.GetComponents<UIBehaviour>())
                    {
                        if (!ui.EnabledInHierarchy) continue;
                        if (ui is UIImage img && !img.RaycastTarget) { dfs++; continue; }
                        if (ui is CanvasGroup) continue;
                        if (ui is RectMask) continue;

                        if (inside)
                        {
                            bestGO = child;
                            bestDfs = dfs;
                        }
                        dfs++;
                    }
                }
            }

            WalkRecurse(canvas, child, pt, childScissor, ref dfs, ref bestGO, ref bestDfs);
        }
    }

    internal static Rect IntersectRect(Rect a, Rect b)
    {
        float minX = System.MathF.Max(a.Min.X, b.Min.X);
        float minY = System.MathF.Max(a.Min.Y, b.Min.Y);
        float maxX = System.MathF.Min(a.Max.X, b.Max.X);
        float maxY = System.MathF.Min(a.Max.Y, b.Max.Y);
        if (maxX < minX) maxX = minX;
        if (maxY < minY) maxY = minY;
        return new Rect(minX, minY, maxX, maxY);
    }

    internal static bool RectContainsPoint(Rect r, Float2 p)
        => p.X >= r.Min.X && p.X <= r.Max.X && p.Y >= r.Min.Y && p.Y <= r.Max.Y;

    internal static bool ContainsCanvasPoint(GameCanvas canvas, RectTransform rt, Float2 canvasPt)
    {
        Rect cr = rt.ComputedRect;
        if (cr.Size.X <= 0 || cr.Size.Y <= 0) return false;

        Float4x4 model = canvas.BuildRectModel(rt);
        Float3 local = Float4x4.TransformPoint(new Float3(canvasPt.X, canvasPt.Y, 0), model.Invert());

        Float2 pivot = rt.Pivot;
        float w = cr.Size.X;
        float h = cr.Size.Y;
        float minX = -pivot.X * w;
        float maxX = (1f - pivot.X) * w;
        float minY = -pivot.Y * h;
        float maxY = (1f - pivot.Y) * h;

        return local.X >= minX && local.X <= maxX && local.Y >= minY && local.Y <= maxY;
    }
}
