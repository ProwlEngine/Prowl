// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;
using Prowl.Vector;

using GLightmapBinding = Prowl.Graphite.Rendering.LightmapBinding;

namespace Prowl.Runtime.Rendering;

/// <summary>Where a renderer's surface landed in its scene's baked lightmaps.</summary>
internal static class BakedLightingBinding
{
    /// <summary>A renderer's resolved placement and the lighting version it came from.</summary>
    internal struct Cache
    {
        public SceneLighting? Lighting;
        public int Version;
        public LightmapPlacement? Placement;
    }

    /// <summary>
    /// The placement a renderer uses and the lighting it is in. Resolved from its section's lighting under its identifier,
    /// and again only when that lighting changes, so a renderer that moves sections or gets a fresh identifier keeps it.
    /// </summary>
    internal static (SceneLighting? Lighting, LightmapPlacement? Placement) Resolve(GameObject renderer, ref Cache cache)
    {
        SceneLighting? current = renderer.IsValid() ? renderer.Section?.Lighting : null;
        bool stale = cache.Lighting == null || cache.Lighting.Version != cache.Version
            || (!ReferenceEquals(cache.Lighting, current) && cache.Placement == null);
        if (stale && current != null)
            cache = new Cache { Lighting = current, Version = current.Version, Placement = current.PlacementFor(renderer.Identifier) };
        return (cache.Lighting, cache.Placement);
    }

    /// <summary>
    /// The lightmap a renderer samples, from the placement its scene's bake recorded under its identifier. None for a
    /// renderer the bake did not place, which Graphite lights with the scene's light probes instead. A page still
    /// loading binds no texture, which lights the renderer with ambient light until it arrives.
    /// </summary>
    public static GLightmapBinding For(GameObject renderer, ref Cache cache)
    {
        (SceneLighting? lighting, LightmapPlacement? placement) = Resolve(renderer, ref cache);
        if (lighting == null || placement is not { } p || p.Index < 0 || p.Index >= lighting.Lightmaps.Count)
            return GLightmapBinding.None;

        Texture2D? lightmap = lighting.Lightmaps[p.Index];
        if (lightmap.IsNotValid()) return GLightmapBinding.None;
        if (lightmap.State == AssetState.Unloaded) _ = lightmap.LoadAsync();
        return new GLightmapBinding(lightmap.IsLoaded ? lightmap.GpuIfLoaded : null, p.ScaleOffset, scale: lighting.LightmapScale);
    }
}
