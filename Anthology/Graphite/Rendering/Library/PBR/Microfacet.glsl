// GGX microfacets with alpha = r squared, height correlated Smith masking, and specular antialiasing as roughness
// filtering. Every lobe of the PBR model and Graphite's light loop share this roughness convention.
#pragma once

#include "Graphite/Math.glsl"

#define PBR_MIN_ALPHA GRAPHITE_MIN_ALPHA

// Kaplanyan and Hill's kernel: screen space normal variance scale and the most it may widen alpha squared
#define PBR_SPEC_AA_VARIANCE 0.5
#define PBR_SPEC_AA_THRESHOLD 0.1

float PBR_Alpha(float roughness) { return max(roughness * roughness, PBR_MIN_ALPHA); }

float PBR_DistributionGGX(float NdotH, float alpha2)
{
    float d = (NdotH * alpha2 - NdotH) * NdotH + 1.0;
    return alpha2 / (GRAPHITE_PI * d * d);
}

// Height correlated Smith G2 over 4 NdotV NdotL
float PBR_VisibilitySmithGGX(float NdotV, float NdotL, float alpha2)
{
    float v = NdotL * sqrt(NdotV * NdotV * (1.0 - alpha2) + alpha2);
    float l = NdotV * sqrt(NdotL * NdotL * (1.0 - alpha2) + alpha2);
    return 0.5 / max(v + l, 1e-7);
}

// The spec's anisotropic GGX alphas along the tangent and bitangent for roughness r and anisotropy a, keeping
// alpha_t^2 + alpha_b^2 = 2 r^4 so the energy tables still index with r
vec2 PBR_AnisotropicAlpha(float roughness, float anisotropy)
{
    float b = 1.0 - clamp(anisotropy, 0.0, 1.0);
    float alphaT = roughness * roughness * sqrt(2.0 / (1.0 + b * b));
    return max(vec2(alphaT, b * alphaT), vec2(PBR_MIN_ALPHA));
}

// Anisotropic GGX, H given along the tangent, bitangent and normal
float PBR_DistributionAnisotropic(float TdotH, float BdotH, float NdotH, vec2 alpha)
{
    vec3 h = vec3(TdotH / alpha.x, BdotH / alpha.y, NdotH);
    float d = dot(h, h);
    return 1.0 / (GRAPHITE_PI * alpha.x * alpha.y * d * d);
}

// Height correlated anisotropic Smith G2 over 4 NdotV NdotL
float PBR_VisibilityAnisotropic(vec3 viewTBN, vec3 lightTBN, vec2 alpha)
{
    float v = lightTBN.z * length(vec3(alpha * viewTBN.xy, viewTBN.z));
    float l = viewTBN.z * length(vec3(alpha * lightTBN.xy, lightTBN.z));
    return 0.5 / max(v + l, 1e-7);
}

// A unit tangent perpendicular to normal, from tangent when it is not along the normal
vec3 PBR_TangentFor(vec3 normal, vec3 tangent)
{
    vec3 t = tangent - normal * dot(normal, tangent);
    if (dot(t, t) > 1e-8)
        return normalize(t);
    return abs(normal.y) < 0.999 ? normalize(cross(normal, vec3(0.0, 1.0, 0.0))) : normalize(cross(normal, vec3(1.0, 0.0, 0.0)));
}

// The normal variance of a filtered tangent space normal of this length, Toksvig's (1 - |n|) / |n|
float PBR_ToksvigVariance(float length) { return length < 1.0 ? (1.0 - length) / max(length, 1e-4) : 0.0; }

#ifdef GRAPHITE_FRAGMENT
// What specular antialiasing adds to a lobe's alpha squared: the variance of the normal across the pixel and inside its
// normal map texel
float PBR_FilterVariance(vec3 normal, float mapVariance)
{
    vec3 dx = dFdx(normal);
    vec3 dy = dFdy(normal);
    float variance = PBR_SPEC_AA_VARIANCE * (dot(dx, dx) + dot(dy, dy)) + mapVariance;
    return min(2.0 * variance, PBR_SPEC_AA_THRESHOLD);
}

float PBR_FilterAlpha2(float alpha2, vec3 normal, float mapVariance)
{
    return clamp(alpha2 + PBR_FilterVariance(normal, mapVariance), PBR_MIN_ALPHA * PBR_MIN_ALPHA, 1.0);
}
#endif
