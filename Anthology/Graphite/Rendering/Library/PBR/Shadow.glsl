// How much light a PBR caster lets through to its shadow, grey: T = 1 - alpha (1 - (1 - M) T_dielectric), alpha the
// coverage, M base_metalness. T_dielectric = transmission_weight (1 - E_spec) times the transmittance, plus thin walled
// subsurface's 0.5 S (1 + g) of what the opaque base keeps. E_spec is the base dielectric's at the light's angle, without
// coat, film or anisotropy. A volume passes transmission_color, one transmission_depth's worth of its extinction.
// Shadow casters dither their coverage by it, see Graphite_StandardShadowDiscards.
#pragma once

#include "Graphite/PBR/Params.glsl"
#include "Graphite/PBR/Energy.glsl"
#include "Graphite/PBR/Fresnel.glsl"


float PBR_ShadowTransmittance(PBR_Params p, float alpha, float NdotL)
{
    float eta = PBR_ModulatedEta(max(p.specularIor, 1e-4), max(p.specularWeight, 0.0));
    eta = eta < 1.0 ? 1.0 / eta : eta;
    float r = clamp(p.specularRoughness, 0.0, 1.0);
    float E1 = PBR_DfgE1(PBR_Dfg(NdotL, r));
    float passes = 1.0 - PBR_DielectricE(NdotL, r, eta) * PBR_TurquinFactor(E1, PBR_DielectricFavg(eta));

    float dielectric = 0.0;
    float opaque = passes;
#ifdef PBR_TRANSMISSION
    float T = clamp(p.transmissionWeight, 0.0, 1.0);
#ifdef PBR_THIN_WALLED
    float ior = max(p.specularIor, 1e-4);
    vec3 tint = pow(clamp(p.transmissionColor, 0.0, 1.0), vec3(1.0 / max(PBR_RefractedCosine(NdotL, ior < 1.0 ? 1.0 / ior : ior), 1e-4)));
#else
    vec3 tint = p.transmissionColor;
#endif
    dielectric += T * passes * Graphite_Luminance(clamp(tint, 0.0, 1.0));
    opaque *= 1.0 - T;
#endif
#if defined(PBR_SUBSURFACE) && defined(PBR_THIN_WALLED)
    float S = clamp(p.subsurfaceWeight, 0.0, 1.0);
    float g = clamp(p.subsurfaceScatterAnisotropy, -1.0, 1.0);
    dielectric += opaque * S * Graphite_Luminance(clamp(p.subsurfaceColor, 0.0, 1.0)) * 0.5 * (1.0 + g);
#endif
    float M = clamp(p.baseMetalness, 0.0, 1.0);
    return 1.0 - clamp(alpha, 0.0, 1.0) * (1.0 - (1.0 - M) * dielectric);
}
