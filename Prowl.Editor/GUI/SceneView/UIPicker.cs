// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

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

        foreach (GameObject go in scene.ActiveObjects)
        {
            GameCanvas? canvas = go.GetComponent<GameCanvas>();
            if (canvas == null || !canvas.EnabledInHierarchy) continue;

            canvas.RebuildIfDirty();

            (GameObject? localHit, float tHit) = PickTopmost(canvas, canvas.GameObject, ray);
            if (localHit == null) continue;

            // Prefer higher canvas sort order, then the nearer hit along the ray.
            bool wins =
                canvas.SortOrder > bestSortOrder ||
                (canvas.SortOrder == bestSortOrder && tHit < bestT - 1e-4f);

            if (wins)
            {
                bestGO = localHit;
                bestCanvas = canvas;
                bestSortOrder = canvas.SortOrder;
                bestT = tHit;
            }
        }

        if (bestGO == null || bestCanvas == null) return false;
        hit = new Hit(bestGO, bestCanvas, bestT);
        return true;
    }

    /// <summary>
    /// Top-most (highest draw order) pickable element hit by the world ray, or (null, 0). Later siblings
    /// and children draw on top, so they're tested first and each subtree is descended before its own
    /// element - the first hit is top-most. Each element is intersected as its own 3D quad (via
    /// <see cref="UIRaycaster.RayHitsRect"/>), so out-of-plane rotation is respected. Mask clipping is
    /// intentionally ignored so masked/clipped elements are still selectable.
    /// </summary>
    private static (GameObject? go, float t) PickTopmost(GameCanvas canvas, GameObject parent, Ray ray)
    {
        var children = parent.Children;
        for (int i = children.Count - 1; i >= 0; i--)
        {
            GameObject child = children[i];
            if (!child.EnabledInHierarchy) continue;
            if (child.GetComponent<GameCanvas>() != null) continue; // nested canvas owns its own tree

            (GameObject? go, float t) deeper = PickTopmost(canvas, child, ray);
            if (deeper.go != null) return deeper;

            RectTransform? rt = child.RectTransform;
            if (rt != null && IsPickable(child))
            {
                Float4x4 model = canvas.CanvasToWorld * canvas.BuildRectModel(rt);
                if (UIRaycaster.RayHitsRect(model, rt, ray.Origin, ray.Direction, out float t))
                    return (child, t);
            }
        }
        return (null, 0f);
    }

    /// <summary>A GameObject is a pick candidate if it carries any enabled UI component - including a
    /// bare <see cref="RectMask"/>, so masks are selectable too.</summary>
    private static bool IsPickable(GameObject go)
    {
        foreach (UIBehaviour ui in go.GetComponents<UIBehaviour>())
            if (ui.EnabledInHierarchy) return true;
        return false;
    }
}
