// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.OrigamiUI.Gizmo;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Runtime.UI;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Editor;

internal static class UIPicker
{
    /// <summary>Result of a successful pick.</summary>
    internal readonly struct Hit
    {
        public readonly GameObject GameObject;
        public readonly GameCanvas Canvas;
        public readonly float RayT;

        public Hit(GameObject go, GameCanvas canvas, float rayT)
        {
            GameObject = go;
            Canvas = canvas;
            RayT = rayT;
        }
    }

    public static GameObject? Pick(Scene? scene, Ray ray) =>
        TryPick(scene, ray, out Hit hit) ? hit.GameObject : null;

    public static bool TryPick(Scene? scene, Ray ray, out Hit hit)
    {
        hit = default;
        if (scene == null) return false;

        GameObject? bestGO = null;
        GameCanvas? bestCanvas = null;
        int bestSortOrder = int.MinValue;
        float bestT = float.PositiveInfinity;
        int bestDfs = -1;

        foreach (GameObject go in scene.ActiveObjects)
        {
            GameCanvas? canvas = go.GetComponent<GameCanvas>();
            if (canvas == null || !canvas.EnabledInHierarchy) continue;

            canvas.RebuildIfDirty();

            Float4x4 c2w = canvas.CanvasToWorld;
            Float3 originW = Float4x4.TransformPoint(Float3.Zero, c2w);
            Float3 rightW = Float4x4.TransformPoint(Float3.UnitX, c2w) - originW;
            Float3 upW = Float4x4.TransformPoint(Float3.UnitY, c2w) - originW;
            if (Float3.LengthSquared(rightW) < 1e-12f || Float3.LengthSquared(upW) < 1e-12f) continue;

            Float3 normalW = Float3.Normalize(Float3.Cross(Float3.Normalize(rightW), Float3.Normalize(upW)));
            if (!GizmoUtils.IntersectPlane(normalW, originW, ray.Origin, ray.Direction, out float tHit))
                continue;
            if (tHit < 0) continue;

            Float3 worldHit = ray.Origin + ray.Direction * tHit;
            Float3 designHit = Float4x4.TransformPoint(worldHit, c2w.Invert());
            Float2 pt = new(designHit.X, designHit.Y);

            int dfs = 0;
            GameObject? localHit = null;
            int localDfs = -1;
            WalkRecurse(canvas, canvas.GameObject, pt, scissor: null, ref dfs, ref localHit, ref localDfs);
            if (localHit == null) continue;

            bool wins =
                canvas.SortOrder > bestSortOrder ||
                (canvas.SortOrder == bestSortOrder && tHit < bestT - 1e-4f) ||
                (canvas.SortOrder == bestSortOrder && System.Math.Abs(tHit - bestT) <= 1e-4f && localDfs > bestDfs);

            if (wins)
            {
                bestGO = localHit;
                bestCanvas = canvas;
                bestSortOrder = canvas.SortOrder;
                bestT = tHit;
                bestDfs = localDfs;
            }
        }

        if (bestGO == null || bestCanvas == null) return false;
        hit = new Hit(bestGO, bestCanvas, bestT);
        return true;
    }

    private static void WalkRecurse(GameCanvas canvas, GameObject parent, Float2 pt, Rect? scissor, ref int dfs, ref GameObject? bestGO, ref int bestDfs)
    {
        foreach (GameObject child in parent.Children)
        {
            if (!child.EnabledInHierarchy) continue;
            if (child.GetComponent<GameCanvas>() != null) continue; // nested canvas owns its own tree

            // ---- RectMask: intersect parent scissor; bail if pointer is outside the clip. ----
            Rect? childScissor = scissor;
            RectMask? rectMask = child.GetComponent<RectMask>();
            if (rectMask != null && rectMask.EnabledInHierarchy)
            {
                Rect mr = rectMask.GetClipRectInCanvasPixels();
                childScissor = scissor is null ? mr : UIRaycaster.IntersectRect(scissor.Value, mr);
                if (childScissor.Value.Size.X <= 0f || childScissor.Value.Size.Y <= 0f) continue;
            }
            if (childScissor is { } cs && !UIRaycaster.RectContainsPoint(cs, pt))
                continue;


            RectTransform? rt = child.RectTransform;
            if (rt != null)
            {
                bool inside = UIRaycaster.ContainsCanvasPoint(canvas, rt, pt);

                foreach (UIBehaviour ui in child.GetComponents<UIBehaviour>())
                {
                    if (!ui.EnabledInHierarchy) continue;

                    if (ui is RectMask) continue;
                    if (inside)
                    {
                        bestGO = child;
                        bestDfs = dfs;
                    }
                    dfs++;
                }
            }

            WalkRecurse(canvas, child, pt, childScissor, ref dfs, ref bestGO, ref bestDfs);
        }
    }
}
