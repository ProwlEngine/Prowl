// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

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
    /// <param name="worldPos">Object world position, used to sample probe SH for dynamic objects.</param>
    public static void Fill(PropertyState props, Scene? scene, int lightmapIndex, Float4 scaleOffset, Float3 worldPos)
    {
        // 1) Baked lightmap (static, lightmapped).
        if (scene != null && lightmapIndex >= 0 && lightmapIndex < scene.BakedLighting.Lightmaps.Count)
        {
            props.SetInt("_GIMode", 1);
            props.SetVector("_LightmapScaleOffset", scaleOffset);
            props.SetTexture("_Lightmap", scene.BakedLighting.Lightmaps[lightmapIndex]);
            return;
        }

        // 2) Light-probe SH (everything else, when the scene has baked probes).
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
