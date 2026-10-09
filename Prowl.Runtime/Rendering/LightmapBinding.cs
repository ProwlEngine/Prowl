// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.InteropServices;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Fills the per-object GI instance properties the Standard shader's <c>CalculateGI</c> reads:
/// <c>_GIMode</c> (0 = realtime ambient, 1 = baked lightmap, 2 = light-probe SH) plus the lightmap
/// texture/scale-offset or the packed SH uniforms. Called per renderable in <c>OnRenderCollect</c>.
/// </summary>
public static class LightmapBinding
{
    /// <param name="props">The renderable's per-object property state.</param>
    /// <param name="renderer">The object being drawn. Its scene holds the baked lightmaps and probes, and
    /// its identifier is what the bake recorded a placement under.</param>
    /// <param name="worldPos">Renderer bounds-center world position, used to sample probe SH for dynamic objects.</param>
    /// <param name="meshHasUV2">Whether the renderer's mesh has a UV2 set. The bake samples the lightmap from UV2
    /// when present, else falls back to the primary UVs (UV0), so this selects the runtime sampling UV set.</param>
    public static void Fill(PropertyState props, GameObject renderer, Float3 worldPos, bool meshHasUV2)
    {
        Scene? scene = renderer.IsValid() ? renderer.Scene : null;

        // Where this object's surface landed in this scene's bake, if it was baked at all. Looked up
        // per object rather than stored on the component: it describes this scene's atlas, so it is the
        // scene's to remember, and a prefab has no say in it.
        Scene.LightmapPlacement? placement = scene.IsValid()
            ? scene!.BakedLighting.PlacementFor(renderer.Identifier)
            : null;

        Fill(props, scene, placement?.Index ?? -1, placement?.ScaleOffset ?? new Float4(1, 1, 0, 0), worldPos, meshHasUV2);
    }

    /// <summary>The same for a draw whose lightmap placement is already known, such as merged static geometry.</summary>
    internal static void Fill(PropertyState props, Scene? scene, int lightmapIndex, Float4 scaleOffset, Float3 worldPos, bool meshHasUV2)
    {
        // 1) Baked lightmap (static, lightmapped). A renderer with a valid index IS lightmapped, so it
        // commits to baked GI here and never falls through to probe SH below: probes would light it
        // with a completely different (wrong) result. A page still loading gets plain ambient, since
        // binding it would fall back to the shared white texture, which RGBM-decodes to a blown-out (8,8,8).
        if (scene != null && lightmapIndex >= 0 && lightmapIndex < scene.BakedLighting.Lightmaps.Count)
        {
            Texture2D? lightmap = scene.BakedLighting.Lightmaps[lightmapIndex];
            if (lightmap is { IsLoaded: true })
            {
                props.SetInt("_GIMode", 1);
                props.SetInt("_LightmapUV", meshHasUV2 ? 1 : 0);
                props.SetVector("_LightmapScaleOffset", scaleOffset);
                props.SetTexture("_Lightmap", lightmap);
            }
            else
            {
                props.SetInt("_GIMode", 0);
            }
            return;
        }

        // 2) Light-probe SH for renderers with NO baked lightmap (index -1): dynamic / non-static
        //    objects such as skinned characters, when the scene has baked probes.
        var vol = scene.IsValid() ? scene.ProbeVolume : null;
        if (vol != null && vol.HasProbes)
        {
            var p = vol.SampleSH(worldPos).ToShaderCoefficients();
            props.SetInt("_GIMode", 2);
            props.SetVector("prowl_SHAr", p.SHAr);
            props.SetVector("prowl_SHAg", p.SHAg);
            props.SetVector("prowl_SHAb", p.SHAb);
            props.SetVector("prowl_SHBr", p.SHBr);
            props.SetVector("prowl_SHBg", p.SHBg);
            props.SetVector("prowl_SHBb", p.SHBb);
            props.SetVector("prowl_SHC", p.SHC);
            return;
        }

        // 3) No baked data: realtime ambient (unchanged behavior).
        props.SetInt("_GIMode", 0);
    }
}
