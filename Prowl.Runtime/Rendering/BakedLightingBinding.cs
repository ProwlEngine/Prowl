// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;
using Prowl.Vector;

using GLightmapBinding = Prowl.Graphite.Rendering.LightmapBinding;

namespace Prowl.Runtime.Rendering;

/// <summary>Where a renderer's surface landed in its scene's baked lightmaps.</summary>
internal static class BakedLightingBinding
{
    /// <summary>
    /// The lightmap a renderer samples, from the placement its scene's bake recorded under its identifier. None for a
    /// renderer the bake did not place, which Graphite lights with the scene's light probes instead. A page still
    /// loading binds no texture, which lights the renderer with ambient light until it arrives.
    /// </summary>
    public static GLightmapBinding For(GameObject renderer)
    {
        Scene? scene = renderer.IsValid() ? renderer.Scene : null;
        if (!scene.IsValid() || scene.BakedLighting.Placements.Count == 0) return GLightmapBinding.None;

        Scene.LightmapPlacement? placement = scene.BakedLighting.PlacementFor(renderer.Identifier);
        if (placement is not { } p || p.Index < 0 || p.Index >= scene.BakedLighting.Lightmaps.Count)
            return GLightmapBinding.None;

        Texture2D? lightmap = scene.BakedLighting.Lightmaps[p.Index];
        if (lightmap.IsNotValid()) return GLightmapBinding.None;
        if (lightmap.State == AssetState.Unloaded) _ = lightmap.LoadAsync();
        return new GLightmapBinding(lightmap.IsLoaded ? lightmap.GpuIfLoaded : null, p.ScaleOffset);
    }
}
