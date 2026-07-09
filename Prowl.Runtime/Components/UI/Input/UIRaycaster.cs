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

            // Screen-space canvases only in this pass - World canvases are cast against a camera ray
            // in TryPickWorld below, and only when nothing screen-space is hit (overlay UI composites
            // on top of the 3D scene, so it wins the pointer).
            if (canvas.RenderMode == RenderMode.WorldSpace) continue;

            canvas.RebuildIfDirty();

            if (!ScreenToDesign(canvas, screenPos, windowSize, out Float2 designPt)) continue;

            // Overlay/screen projects orthographically: cast a design-space ray straight into the screen.
            Float3 rayO = new Float3(designPt.X, designPt.Y, 1e6f);
            Float3 rayD = new Float3(0f, 0f, -1f);
            int dfs = 0;
            GameObject? localHit = null;
            int localDfs = -1;
            WalkRecurse(canvas, canvas.GameObject, designPt, rayO, rayD, scissor: null, ref dfs, ref localHit, ref localDfs);
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

        // No screen-space hit -> fall back to world-space canvases via a 3D camera ray.
        if (bestGO == null)
            TryPickWorld(scene, screenPos, windowSize, ref bestGO, ref bestCanvas, ref bestDesign);

        if (bestGO == null || bestCanvas == null) return false;
        hit = new Hit(bestGO, bestCanvas, bestDesign);
        return true;
    }

    /// <summary>
    /// Pick for <see cref="RenderMode.WorldSpace"/> canvases: unproject the pointer through the
    /// scene's main camera, intersect each world canvas's plane, and keep the nearest canvas whose
    /// element sits under the resulting point. Only called when no screen-space canvas was hit.
    /// </summary>
    private static void TryPickWorld(Scene scene, Float2 screenPos, Float2 windowSize,
        ref GameObject? bestGO, ref GameCanvas? bestCanvas, ref Float2 bestDesign)
    {
        Camera? cam = ResolveMainCamera(scene);
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(screenPos, windowSize);
        float bestT = float.MaxValue;

        foreach (GameObject go in scene.ActiveObjects)
        {
            GameCanvas? canvas = go.GetComponent<GameCanvas>();
            if (canvas is null || !canvas.EnabledInHierarchy) continue;
            if (canvas.RenderMode != RenderMode.WorldSpace) continue;

            canvas.RebuildIfDirty();

            if (!WorldRayToDesign(canvas, ray, out Float2 designPt, out float t)) continue;
            if (t >= bestT) continue; // a nearer canvas already owns the pointer

            // The camera ray expressed in the canvas's design space, so element quads (BuildRectModel)
            // are tested in 3D and out-of-plane element rotation is respected.
            Float4x4 w2d = canvas.CanvasToWorld.Invert();
            Float3 rayO = Float4x4.TransformPoint(ray.Origin, w2d);
            Float3 rayD = Float4x4.TransformPoint(ray.Origin + ray.Direction, w2d) - rayO;
            int dfs = 0;
            GameObject? localHit = null;
            int localDfs = -1;
            WalkRecurse(canvas, canvas.GameObject, designPt, rayO, rayD, scissor: null, ref dfs, ref localHit, ref localDfs);
            if (localHit == null) continue;

            bestT = t;
            bestGO = localHit;
            bestCanvas = canvas;
            bestDesign = designPt;
        }
    }

    /// <summary>The scene's primary rendering camera: the enabled camera with the highest Depth.</summary>
    private static Camera? ResolveMainCamera(Scene scene)
    {
        Camera? best = null;
        foreach (GameObject go in scene.ActiveObjects)
        {
            Camera? c = go.GetComponent<Camera>();
            if (c == null || !c.EnabledInHierarchy) continue;
            if (best == null || c.Depth > best.Depth) best = c;
        }
        return best;
    }

    /// <summary>
    /// Intersects a world-space ray with a world canvas's plane (the design-pixel Z=0 plane mapped
    /// through <see cref="GameCanvas.CanvasToWorld"/>) and returns the hit point in the canvas's
    /// design-pixel space plus the ray distance. False if the ray is parallel or hits behind the origin.
    /// </summary>
    private static bool WorldRayToDesign(GameCanvas canvas, Ray ray, out Float2 designPt, out float t)
    {
        designPt = Float2.Zero;

        Float4x4 toWorld = canvas.CanvasToWorld;
        Float3 p0 = Float4x4.TransformPoint(Float3.Zero, toWorld);
        Float3 p1 = Float4x4.TransformPoint(new Float3(1f, 0f, 0f), toWorld);
        Float3 p2 = Float4x4.TransformPoint(new Float3(0f, 1f, 0f), toWorld);
        Plane plane = new Plane(p0, p1, p2);

        if (!ray.Intersects(plane, out t) || t < 0f) return false;

        Float3 world = ray.Origin + ray.Direction * t;
        Float3 local = Float4x4.TransformPoint(world, toWorld.Invert());
        designPt = new Float2(local.X, local.Y);
        return true;
    }

    private static bool ScreenToDesign(GameCanvas canvas, Float2 screenPos, Float2 windowSize, out Float2 designPt)
    {
        float scale = canvas.ScaleFactor;
        if (scale < 0.001f) { designPt = Float2.Zero; return false; }

        designPt = new Float2(screenPos.X / scale, (windowSize.Y - screenPos.Y) / scale);
        return true;
    }

    // pt is the pointer on the canvas plane (used for the RectMask scissor, which is canvas-aligned);
    // (rayO, rayD) is the same pointer as a design-space ray, intersected with each element's own quad
    // so out-of-plane (3D) element rotation is respected.
    private static void WalkRecurse(GameCanvas canvas, GameObject parent, Float2 pt, Float3 rayO, Float3 rayD, Rect? scissor, ref int dfs, ref GameObject? bestGO, ref int bestDfs)
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
                    bool inside = RayHitsRect(canvas, rt, rayO, rayD);

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

            WalkRecurse(canvas, child, pt, rayO, rayD, childScissor, ref dfs, ref bestGO, ref bestDfs);
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

    /// <summary>
    /// True if a ray hits the element's quad. <paramref name="model"/> maps the element's pivot-centered
    /// local space into the ray's space; the ray is intersected with the element's actual (possibly
    /// out-of-plane tilted) plane rather than a flat projection, so 3D rotation is respected. Runtime
    /// passes a canvas-design-space ray + <see cref="GameCanvas.BuildRectModel"/>; the editor passes a
    /// world-space ray + world model.
    /// </summary>
    public static bool RayHitsRect(Float4x4 model, RectTransform rt, Float3 rayOrigin, Float3 rayDir, out float t)
    {
        t = 0f;
        Rect cr = rt.ComputedRect;
        if (cr.Size.X <= 0 || cr.Size.Y <= 0) return false;

        Float4x4 inv = model.Invert();
        Float3 lo = Float4x4.TransformPoint(rayOrigin, inv);
        Float3 ld = Float4x4.TransformPoint(rayOrigin + rayDir, inv) - lo;
        if (Maths.Abs(ld.Z) < 1e-9f) return false;

        t = -lo.Z / ld.Z;
        if (t < 0f) return false;

        Float3 lh = lo + ld * t;
        Float2 pivot = rt.Pivot;
        float w = cr.Size.X, h = cr.Size.Y;
        return lh.X >= -pivot.X * w && lh.X <= (1f - pivot.X) * w
            && lh.Y >= -pivot.Y * h && lh.Y <= (1f - pivot.Y) * h;
    }

    /// <summary>Ray-vs-element test in canvas design space (element model = <see cref="GameCanvas.BuildRectModel"/>).</summary>
    internal static bool RayHitsRect(GameCanvas canvas, RectTransform rt, Float3 rayOrigin, Float3 rayDir)
        => RayHitsRect(canvas.BuildRectModel(rt), rt, rayOrigin, rayDir, out _);

}
