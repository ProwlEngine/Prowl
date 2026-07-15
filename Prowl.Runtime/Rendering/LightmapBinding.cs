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
    /// <param name="scene">Scene that owns the renderer (provides baked lightmaps + probe volume).</param>
    /// <param name="lightmapIndex">Renderer's lightmap index, or -1.</param>
    /// <param name="scaleOffset">Renderer's lightmap UV scale/offset.</param>
    /// <param name="worldPos">Renderer bounds-center world position, used to sample probe SH for dynamic objects.</param>
    /// <param name="meshHasUV2">Whether the renderer's mesh has a UV2 set. The bake samples the lightmap from UV2
    /// when present, else falls back to the primary UVs (UV0), so this selects the runtime sampling UV set.</param>
    public static void Fill(PropertyState props, Scene? scene, int lightmapIndex, Float4 scaleOffset, Float3 worldPos, bool meshHasUV2)
    {
        // 1) Baked lightmap (static, lightmapped). A renderer with a valid index IS lightmapped, so it
        // commits to baked GI here and never falls through to probe SH below: probes would light it
        // with a completely different (wrong) result, and on reload the lightmap pages stream in async,
        // so a lightmapped surface would flash probe-lit until its page arrived. Bind the page once it
        // has loaded; while it's still streaming use plain ambient (reading .Res queued the load, so a
        // later frame picks up the real lightmap). Note: binding an unloaded page would fall back to the
        // shared white texture, which RGBM-decodes to a blown-out (8,8,8) - hence the explicit ambient.
        if (scene != null && lightmapIndex >= 0 && lightmapIndex < scene.BakedLighting.Lightmaps.Count)
        {
            // AssetRef<T> caches its resolved instance as a side effect of .Res - List<T>'s indexer
            // returns value-type elements by copy, so Lightmaps[i].Res would resolve into a throwaway
            // copy and never cache anything, forcing a real disk reload on every single call once the
            // weak-ref cache lets the previous copy's instance be collected. CollectionsMarshal.AsSpan
            // gives a ref to the real backing element so the resolved instance actually sticks.
            ref var lightmap = ref CollectionsMarshal.AsSpan(scene.BakedLighting.Lightmaps)[lightmapIndex];
            if (lightmap.Res.IsValid())
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
        var vol = scene?.ProbeVolume;
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
