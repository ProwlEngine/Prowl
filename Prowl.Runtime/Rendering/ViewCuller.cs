// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Frustum and layer culling of a <see cref="SceneCuller"/>'s contents for one camera, with render-order classification and distance sorting.
/// </summary>
public static class ViewCuller
{
    private const float PlaneEpsilon = 0.00001f;

    public static void Cull(SceneCuller culler, Camera camera, ViewCullResults results)
    {
        IReadOnlyList<IRenderable> renderables = culler.Renderables;
        int count = renderables.Count;
        results.Reset(count);

        Frustum frustum = Frustum.FromMatrix(camera.ProjectionMatrix * camera.ViewMatrix);
        results.Frustum = frustum;
        results.ShadowFocus = camera.GetShadowFocusPosition();

        Float3 cameraPosition = camera.Transform.Position;
        LayerMask mask = camera.CullingMask;
        AABB[] worldBounds = results.WorldBounds;
        bool[] renderable = results.Renderable;
        float[] keys = results.SortKeys;

        for (int i = 0; i < count; i++)
        {
            IRenderable r = renderables[i];
            r.GetCullingData(out bool isRenderable, out AABB bounds);
            renderable[i] = isRenderable;
            worldBounds[i] = bounds;

            if (!isRenderable || !mask.HasLayer(r.GetLayer()) || !Intersects(in frustum, in bounds))
            {
                results.Culled.Add(i);
                continue;
            }

            keys[i] = Float3.DistanceSquared(r.GetPosition(), cameraPosition);
            Classify(r.GetMaterial(), results).Add(i);
        }

        results.Opaque.Sort(results.FrontToBack);
        results.Transparent.Sort(results.BackToFront);

        CollectLights(culler.Lights, mask, results);
    }

    /// <summary>
    /// Frustum test only, against bounds already gathered by <see cref="Cull"/>. Appends the surviving renderable indices to <paramref name="output"/>.
    /// </summary>
    public static void CullForFrustum(SceneCuller culler, in Frustum frustum, AABB[] worldBounds, bool[] renderable, List<int> output)
    {
        output.Clear();
        int count = culler.Renderables.Count;
        for (int i = 0; i < count; i++)
        {
            if (renderable[i] && Intersects(in frustum, in worldBounds[i]))
                output.Add(i);
        }
    }

    public static bool Intersects(in Frustum frustum, in AABB bounds)
    {
        Plane[] planes = frustum.Planes;
        for (int i = 0; i < planes.Length; i++)
        {
            Float3 normal = planes[i].Normal;
            Float3 p = new(
                normal.X >= 0f ? bounds.Max.X : bounds.Min.X,
                normal.Y >= 0f ? bounds.Max.Y : bounds.Min.Y,
                normal.Z >= 0f ? bounds.Max.Z : bounds.Min.Z);

            if (Float3.Dot(normal, p) - planes[i].D < -PlaneEpsilon)
                return false;
        }
        return true;
    }

    private static List<int> Classify(Material material, ViewCullResults results)
    {
        if (material.IsNotValid())
            return results.Opaque;

        Shader? shader = material.Shader;
        if (shader.IsNotValid())
            return results.Opaque;

        if (shader.GetPassWithTag(PassTags.RenderOrder, PassTags.Transparent).HasValue)
            return results.Transparent;
        if (shader.GetPassWithTag(PassTags.RenderOrder, PassTags.UI).HasValue)
            return results.UI;
        return results.Opaque;
    }

    private static void CollectLights(IReadOnlyList<IRenderableLight> lights, LayerMask mask, ViewCullResults results)
    {
        for (int i = 0; i < lights.Count; i++)
        {
            IRenderableLight light = lights[i];
            if (light == null || light.GetLightType() != LightType.Directional || !mask.HasLayer(light.GetLayer()))
                continue;

            if (results.Directional == null)
                results.Directional = light;
            results.Lights.Add(light);
        }

        for (int i = 0; i < lights.Count; i++)
        {
            IRenderableLight light = lights[i];
            if (light == null || light.GetLightType() == LightType.Directional || !mask.HasLayer(light.GetLayer()))
                continue;

            results.Lights.Add(light);
        }
    }
}
