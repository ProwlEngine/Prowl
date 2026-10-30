// The GGX energy tables, alpha = r squared with height correlated Smith masking, and Turquin's multiple scattering
// compensation built on them. Texel i of a table of n texels holds the value at i / (n - 1) on its axis, so both ends of
// every axis are exact. The tables are generated and verified by the rendering tests (EnergyTables.cs). Anisotropic
// lobes take their split sum from Graphite_AnisotropicDfg, and the dielectric table scaled by PBR_AnisotropicScale.
#pragma once

#include "Graphite/Core.glsl"
#include "Graphite/PBR/Microfacet.glsl"

layout(set = 1, binding = 37) uniform texture2D Graphite_Dfg;
layout(set = 1, binding = 38) uniform texture3D Graphite_DielectricE;
layout(set = 1, binding = 39) uniform sampler Graphite_TableSampler;
layout(set = 1, binding = 40) uniform texture3D Graphite_AnisotropicDfg;

#define PBR_DFG_SIZE 128.0
#define PBR_DIELECTRIC_SIZE 32.0
#define PBR_ANISOTROPIC_NDOTV 32.0
#define PBR_ANISOTROPIC_ROUGHNESS 16.0
#define PBR_ANISOTROPIC_LEVELS 8.0
#define PBR_ANISOTROPIC_AZIMUTHS 8.0

// The angle the F82 tint is fitted at, cos = 1/7, and mu (1 - mu)^6 there
#define PBR_F82_MU (1.0 / 7.0)
#define PBR_F82_PEAK 0.0566528

vec2 pbr_TableCoord(vec2 x, float size) { return (clamp(x, 0.0, 1.0) * (size - 1.0) + 0.5) / size; }
vec3 pbr_TableCoord(vec3 x, float size) { return (clamp(x, 0.0, 1.0) * (size - 1.0) + 0.5) / size; }

// GGX over (NdotV, r): x and y the split sum scale and bias, so Schlick F0 gives F0 x + y, x + y the directional albedo
// E1 of a white lobe, z the integral the F82 tint correction is subtracted with
vec3 PBR_Dfg(float NdotV, float r)
{
    return textureLod(sampler2D(Graphite_Dfg, Graphite_TableSampler), pbr_TableCoord(vec2(NdotV, r), PBR_DFG_SIZE), 0.0).rgb;
}

float PBR_DfgE1(vec3 dfg) { return dfg.x + dfg.y; }

// The view's azimuth about the normal, 0 along the tangent to 1 along the bitangent, folded into one quadrant
float PBR_Azimuth(vec3 tangent, vec3 bitangent, vec3 view)
{
    return atan(abs(dot(bitangent, view)), max(abs(dot(tangent, view)), 1e-6)) * (2.0 / PI);
}

// PBR_Dfg for the spec's anisotropic GGX of roughness r and anisotropy a, the view at azimuth from PBR_Azimuth. Each
// anisotropy level holds a block of azimuths along z, filtered within the block and blended between the two levels.
// Level t holds a = 1 - (1 - t)^2, crowding toward 1 where alpha_b falls fastest.
vec3 PBR_AnisotropicDfg(float NdotV, float r, float anisotropy, float azimuth)
{
    vec2 xy = (clamp(vec2(NdotV, r), 0.0, 1.0) * (vec2(PBR_ANISOTROPIC_NDOTV, PBR_ANISOTROPIC_ROUGHNESS) - 1.0) + 0.5)
        / vec2(PBR_ANISOTROPIC_NDOTV, PBR_ANISOTROPIC_ROUGHNESS);
    float level = (1.0 - sqrt(1.0 - clamp(anisotropy, 0.0, 1.0))) * (PBR_ANISOTROPIC_LEVELS - 1.0);
    float level0 = min(floor(level), PBR_ANISOTROPIC_LEVELS - 2.0);
    float inBlock = clamp(azimuth, 0.0, 1.0) * (PBR_ANISOTROPIC_AZIMUTHS - 1.0) + 0.5;
    float depth = PBR_ANISOTROPIC_LEVELS * PBR_ANISOTROPIC_AZIMUTHS;
    vec3 a = textureLod(sampler3D(Graphite_AnisotropicDfg, Graphite_TableSampler), vec3(xy, (level0 * PBR_ANISOTROPIC_AZIMUTHS + inBlock) / depth), 0.0).rgb;
    vec3 b = textureLod(sampler3D(Graphite_AnisotropicDfg, Graphite_TableSampler), vec3(xy, ((level0 + 1.0) * PBR_ANISOTROPIC_AZIMUTHS + inBlock) / depth), 0.0).rgb;
    return mix(a, b, level - level0);
}

// What anisotropy does to the dielectric table at relative IOR eta: the ratio of the anisotropic and isotropic
// Schlick split sums at its F0
float PBR_AnisotropicScale(vec3 anisotropicDfg, vec3 isotropicDfg, float eta)
{
    float f0 = (eta - 1.0) / (eta + 1.0);
    f0 *= f0;
    return (f0 * anisotropicDfg.x + anisotropicDfg.y) / max(f0 * isotropicDfg.x + isotropicDfg.y, 1e-6);
}

// The directional albedo of GGX reflection with exact dielectric Fresnel at relative IOR eta, clamped to at least 1.
// eta is the modulated one, coat and specular_weight included.
float PBR_DielectricE(float NdotV, float r, float eta)
{
    eta = max(eta, 1.0);
    float sqrtF0 = (eta - 1.0) / (eta + 1.0);
    return textureLod(sampler3D(Graphite_DielectricE, Graphite_TableSampler), pbr_TableCoord(vec3(NdotV, r, sqrtF0), PBR_DIELECTRIC_SIZE), 0.0).r;
}

// The F82 tint's a, so F(mu) = F_schlick(mu) - a mu (1 - mu)^6 reflects specularColor times Schlick at cos 1/7
vec3 PBR_F82Tint(vec3 F0, vec3 specularColor)
{
    float m = 1.0 - PBR_F82_MU;
    float m5 = m * m * m * m * m;
    vec3 schlick = F0 + (1.0 - F0) * m5;
    return schlick * (1.0 - specularColor) / PBR_F82_PEAK;
}

// Single scattering directional albedo of an F82 metal, before specular_weight
vec3 PBR_MetalE(vec3 dfg, vec3 F0, vec3 specularColor)
{
    return F0 * dfg.x + dfg.y - PBR_F82Tint(F0, specularColor) * dfg.z;
}

// The cosine weighted hemispherical average of the F82 metal's Fresnel
vec3 PBR_MetalFavg(vec3 F0, vec3 specularColor)
{
    return F0 + (1.0 - F0) / 21.0 - PBR_F82Tint(F0, specularColor) / 126.0;
}

// The cosine weighted hemispherical average of exact dielectric Fresnel, Kulla and Conty's fit, eta at least 1
float PBR_DielectricFavg(float eta)
{
    eta = max(eta, 1.0);
    return (eta - 1.0) / (4.08567 + 1.00071 * eta);
}

// Turquin's factor on a single scattering lobe, for direct light as well as its albedo
vec3 PBR_TurquinFactor(float E1, vec3 Favg) { return 1.0 + Favg * (1.0 - E1) / max(E1, 1e-4); }
float PBR_TurquinFactor(float E1, float Favg) { return 1.0 + Favg * (1.0 - E1) / max(E1, 1e-4); }

// Compensated directional albedo of the F82 metal lobe, specular_weight included
vec3 PBR_TurquinMetal(vec3 dfg, vec3 F0, vec3 specularColor, float specularWeight)
{
    vec3 E = PBR_MetalE(dfg, F0, specularColor) * specularWeight;
    return E * PBR_TurquinFactor(PBR_DfgE1(dfg), PBR_MetalFavg(F0, specularColor) * specularWeight);
}

// Compensated directional albedo of the dielectric reflection lobe at modulated relative IOR eta
float PBR_TurquinDielectric(vec3 dfg, float NdotV, float r, float eta)
{
    return PBR_DielectricE(NdotV, r, eta) * PBR_TurquinFactor(PBR_DfgE1(dfg), PBR_DielectricFavg(eta));
}
