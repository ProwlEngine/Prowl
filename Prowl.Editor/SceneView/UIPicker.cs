// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.OrigamiUI.Gizmo;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Runtime.UI;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Editor;

/// <summary>
/// Picks the topmost <see cref="UIBehaviour"/>-bearing GameObject under a scene-view ray.
/// Walks every active <see cref="GameCanvas"/> in the scene, intersects the ray with each
/// canvas's plane (built via <see cref="GameCanvas.CanvasToWorld"/> so it agrees with the
/// rendered UI), and returns the on-top hit by canvas <c>SortOrder</c>, then ray distance,
/// then DFS index — the same precedence <c>BuildRecursive</c> uses to assign render order.
/// </summary>
/// <remarks>
/// Callers should push <see cref="GameCanvas.ScreenSizeOverride"/> to the rendering surface
/// size before calling, so <see cref="GameCanvas.RebuildIfDirty"/> here lays the canvas out
/// against the same size the pipeline will use. <see cref="UISceneEditor"/> and
/// <see cref="Panels.SceneViewPanel"/> both do this around their hit-test paths.
/// </remarks>
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
            WalkRecurse(canvas, canvas.GameObject, pt, ref dfs, ref localHit, ref localDfs);
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

    // Mirrors GameCanvas.BuildRecursive: visit children in order, count one DFS step per
    // drawable UIBehaviour (so the dfs index matches what the renderer uses for SortKey),
    // descend after the parent's behaviours so children draw on top of their parent.
    private static void WalkRecurse(GameCanvas canvas, GameObject parent, Float2 pt, ref int dfs, ref GameObject? bestGO, ref int bestDfs)
    {
        foreach (GameObject child in parent.Children)
        {
            if (!child.EnabledInHierarchy) continue;
            if (child.GetComponent<GameCanvas>() != null) continue; // nested canvas owns its own tree

            RectTransform? rt = child.RectTransform;
            if (rt != null)
            {
                bool inside = UIRaycaster.ContainsCanvasPoint(canvas, rt, pt);

                foreach (UIBehaviour ui in child.GetComponents<UIBehaviour>())
                {
                    if (!ui.EnabledInHierarchy) continue;
                    if (inside)
                    {
                        bestGO = child;
                        bestDfs = dfs;
                    }
                    dfs++;
                }
            }

            WalkRecurse(canvas, child, pt, ref dfs, ref bestGO, ref bestDfs);
        }
    }
}
