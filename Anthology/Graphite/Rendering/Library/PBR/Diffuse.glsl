// EON, the energy preserving Oren Nayar diffuse of Portsmouth, Kutz and Hill, with its approximate FON albedo.
// A white EON surface reflects all it receives at every roughness.
#pragma once

#include "Graphite/PBR/Microfacet.glsl"

#define PBR_FON_C1 (0.5 - 2.0 / (3.0 * GRAPHITE_PI))
#define PBR_FON_C2 (2.0 / 3.0 - 28.0 / (15.0 * GRAPHITE_PI))

// The directional albedo of the single scattering FON lobe of roughness r at cosine mu
float PBR_FonAlbedo(float mu, float r)
{
    float m = 1.0 - clamp(mu, 0.0, 1.0);
    float gOverPi = m * (0.0571085289 + m * (0.491881867 + m * (-0.332181442 + m * 0.0714429953)));
    return (1.0 + r * gOverPi) / (1.0 + PBR_FON_C1 * r);
}

// The FON lobe's albedo averaged over the hemisphere
float PBR_FonAverage(float r) { return (1.0 + PBR_FON_C2 * r) / (1.0 + PBR_FON_C1 * r); }

// The albedo the multiple scattering lobe takes so the whole lobe keeps rho's energy
vec3 PBR_EonMultipleAlbedo(vec3 rho, float averageAlbedo) { return rho * rho * averageAlbedo / (1.0 - rho * (1.0 - averageAlbedo)); }

// The EON lobe's directional albedo at cosine mu
vec3 PBR_EonAlbedo(vec3 rho, float r, float mu)
{
    float e = PBR_FonAlbedo(mu, r);
    return rho * e + PBR_EonMultipleAlbedo(rho, PBR_FonAverage(r)) * (1.0 - e);
}

// The EON BRDF, without the cosine
vec3 PBR_Eon(vec3 rho, float r, vec3 N, vec3 L, vec3 V)
{
    float muI = clamp(dot(N, L), 0.0, 1.0);
    float muO = clamp(dot(N, V), 0.0, 1.0);
    float s = dot(L, V) - muI * muO;
    float sOverT = s > 0.0 ? s / max(max(muI, muO), 1e-6) : s;
    float a = 1.0 / (1.0 + PBR_FON_C1 * r);
    float average = PBR_FonAverage(r);
    vec3 single = rho * (a * (1.0 + r * sOverT) / GRAPHITE_PI);
    vec3 multiple = PBR_EonMultipleAlbedo(rho, average) / GRAPHITE_PI
        * (max(1e-7, 1.0 - PBR_FonAlbedo(muO, r)) * max(1e-7, 1.0 - PBR_FonAlbedo(muI, r)) / max(1e-7, 1.0 - average));
    return single + multiple;
}
