// The PBR model's inputs to and from the GBuffer, see Graphite/GBuffer.glsl. Base colour carries base_weight, specular
// carries specular_weight and specular_ior as one reflectance, so a metal's specular_weight reads back as 1. Roughness
// carries the specular antialiasing of the pass that wrote it, so lighting adds none.
#pragma once

#include "Graphite/GBuffer.glsl"
#include "Graphite/PBR/Params.glsl"
#include "Graphite/PBR/Fresnel.glsl"
#include "Graphite/PBR/Microfacet.glsl"

#ifdef GRAPHITE_FRAGMENT
// mapVariance is the normal map's own variance, as PBR_Prepare takes it
Graphite_GBuffer PBR_ToGBuffer(PBR_Params p, uint model, float mapVariance)
{
    Graphite_GBuffer g = Graphite_NewGBuffer(model);
    g.baseColor = p.baseWeight * p.baseColor;
    g.metalness = p.baseMetalness;
    g.normal = p.geometryNormal;
    float alpha = PBR_Alpha(clamp(p.specularRoughness, 0.0, 1.0));
    g.roughness = sqrt(sqrt(PBR_FilterAlpha2(alpha * alpha, normalize(p.geometryNormal), mapVariance)));
    float eta = PBR_ModulatedEta(max(p.specularIor, 1e-4), max(p.specularWeight, 0.0));
    float r = (eta - 1.0) / (eta + 1.0);
    g.specular = r * r / GRAPHITE_GBUFFER_SPECULAR_RANGE;
    g.occlusion = p.occlusion;
    return g;
}
#endif

PBR_Params PBR_FromGBuffer(Graphite_GBuffer g)
{
    PBR_Params p = PBR_DefaultParams(g.normal, vec3(1.0, 0.0, 0.0));
    p.baseWeight = 1.0;
    p.baseColor = g.baseColor;
    p.baseMetalness = g.metalness;
    p.specularRoughness = g.roughness;
    p.specularWeight = 1.0;
    float r = sqrt(g.specular * GRAPHITE_GBUFFER_SPECULAR_RANGE);
    p.specularIor = (1.0 + r) / (1.0 - r);
    p.occlusion = g.occlusion;
    return p;
}
