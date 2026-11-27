// Fuzz: Zeltner, Burley and Chiang's practical multiple scattering sheen, a linearly transformed cosine lobe with
// MaterialX's analytic fits to its albedo and matrix. Roughness is fuzz_roughness as it is, not squared.
#pragma once

#include "Graphite/PBR/Microfacet.glsl"

// The roughness the fits cover
#define PBR_FUZZ_MIN_ROUGHNESS 0.01

// The fuzz layer's directional albedo at cosine NdotV, a Gaussian fit
float PBR_FuzzAlbedo(float NdotV, float r)
{
    float s = r * (0.0206607 + 1.58491 * r) / (0.0379424 + r * (1.32227 + r));
    float m = r * (-0.193854 + r * (-1.14885 + r * (1.7932 - 0.95943 * r * r))) / (0.046391 + r);
    float o = r * (0.000654023 + (-0.0207818 + 0.119681 * r) * r) / (1.26264 + r * (-1.92021 + r));
    float x = (NdotV - m) / s;
    return clamp(exp(-0.5 * x * x) / (s * sqrt(2.0 * GRAPHITE_PI)) + o, 0.0, 1.0);
}

// The inverse LTC matrix's a and b, rational fits
vec2 PBR_FuzzLtc(float NdotV, float r)
{
    float a = (2.58126 * NdotV + 0.813703 * r) * r / (1.0 + 0.310327 * NdotV * NdotV + 2.60994 * NdotV * r);
    float b = sqrt(1.0 - NdotV) * (r - 1.0) * r * r * r / (0.0000254053 + 1.71228 * NdotV - 1.71506 * NdotV * r + 1.34174 * r * r);
    return vec2(a, b);
}

// The view's direction in the tangent plane, the LTC frame's X with the normal as Z
vec3 PBR_FuzzAxis(vec3 N, vec3 V, float NdotV)
{
    vec3 x = V - N * NdotV;
    float len2 = dot(x, x);
    return len2 > 1e-10 ? x * inversesqrt(len2) : PBR_TangentFor(N, vec3(1.0, 0.0, 0.0));
}

// The fuzz lobe's density toward L over solid angle, the cosine included, so the layer reflects its albedo times this
float PBR_FuzzLobe(vec3 L, vec3 N, vec3 X, vec2 ltc)
{
    vec3 w = vec3(dot(L, X), dot(L, cross(N, X)), dot(L, N));
    vec3 wo = vec3(ltc.x * w.x + ltc.y * w.z, ltc.x * w.y, w.z);
    float k = ltc.x / dot(wo, wo);
    return max(w.z, 0.0) / GRAPHITE_PI * k * k;
}
