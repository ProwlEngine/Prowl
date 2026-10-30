// Belcour and Barla's thin film iridescence, the Airy sum with spectral antialiasing over two orders, ported from
// MaterialX's mx_fresnel_airy. eta1 is the medium above the film, eta2 the film, the base below. Colours come out in
// MaterialX's CIE RGB, whose white is XYZ (1, 1, 1), clamped to 0 to 1.
#pragma once

#include "Graphite/PBR/Microfacet.glsl"
#include "Graphite/PBR/Fresnel.glsl"
#include "Graphite/PBR/Energy.glsl"

#define PBR_AIRY_ORDERS 2

// The film's interference over its optical path difference in metres, Gaussian fits to the XYZ sensitivities
vec3 pbr_FilmSensitivity(float opd, vec3 shift)
{
    float phase = 2.0 * PI * opd;
    vec3 val = vec3(5.4856e-13, 4.4201e-13, 5.2481e-13);
    vec3 pos = vec3(1.6810e+06, 1.7953e+06, 2.2084e+06);
    vec3 var = vec3(4.3278e+09, 9.3046e+09, 6.6121e+09);
    vec3 xyz = val * sqrt(2.0 * PI * var) * cos(pos * phase + shift) * exp(-var * phase * phase);
    xyz.x += 9.7470e-14 * sqrt(2.0 * PI * 4.5282e+09) * cos(2.2399e+06 * phase + shift.x) * exp(-4.5282e+09 * phase * phase);
    return xyz / 1.0685e-7;
}

// The Airy sum for one polarization: R12 the top interface's reflectance, R23 and phi23 the base's
vec3 pbr_FilmPolarization(float R12, vec3 R23, vec3 phi23, float phi21, float opd)
{
    float T121 = 1.0 - R12;
    vec3 Rs = T121 * T121 * R23 / max(1.0 - R12 * R23, vec3(1e-7));
    vec3 I = R12 + Rs;
    vec3 r123 = sqrt(max(R12 * R23, 0.0));
    vec3 Cm = Rs - T121;
    for (int m = 1; m <= PBR_AIRY_ORDERS; m++)
    {
        Cm *= r123;
        I += Cm * 2.0 * pbr_FilmSensitivity(float(m) * opd, float(m) * (phi23 + phi21));
    }
    return I;
}

struct PBR_Film
{
    vec2  R12;               // the top interface's (Rp, Rs)
    vec2  phi21;
    float cosThetaT;         // inside the film
    float eta2;
    float opd;               // optical path difference, metres
};

// The film's top interface for a ray at cosTheta in eta1, thickness in micrometres
PBR_Film PBR_FilmTop(float cosTheta, float eta1, float filmIor, float thickness)
{
    PBR_Film f;
    f.eta2 = max(filmIor, eta1);
    float ratio = eta1 / f.eta2;
    f.cosThetaT = sqrt(max(1.0 - (1.0 - cosTheta * cosTheta) * ratio * ratio, 0.0));
    f.R12 = f.cosThetaT <= 0.0 ? vec2(1.0) : PBR_FresnelDielectricPolarized(cosTheta, f.eta2 / eta1);
    float cosB = cos(atan(f.eta2 / eta1));
    f.phi21 = vec2(cosTheta < cosB ? 0.0 : PI, PI);
    f.opd = 2.0 * f.eta2 * f.cosThetaT * thickness * 1.0e-6;
    return f;
}

vec3 pbr_FilmResolve(PBR_Film f, vec3 R23p, vec3 R23s, vec3 phi23p, vec3 phi23s)
{
    const mat3 XYZ_TO_RGB = mat3(2.3706743, -0.5138850, 0.0052982, -0.9000405, 1.4253036, -0.0146949, -0.4706338, 0.0885814, 1.0093968);
    vec3 I = pbr_FilmPolarization(f.R12.x, R23p, phi23p, f.phi21.x, f.opd)
        + pbr_FilmPolarization(f.R12.y, R23s, phi23s, f.phi21.y, f.opd);
    return clamp(XYZ_TO_RGB * (0.5 * I), 0.0, 1.0);
}

// The reflectance of a film over a dielectric base of IOR eta3, both absolute like eta1
vec3 PBR_ThinFilmDielectric(float cosTheta, float eta1, float filmIor, float eta3, float thickness)
{
    PBR_Film f = PBR_FilmTop(cosTheta, eta1, filmIor, thickness);
    vec2 R23 = PBR_FresnelDielectricPolarized(f.cosThetaT, eta3 / f.eta2);

    // The base's phase per polarization, MaterialX's conductor phase with no extinction
    float sin2 = 1.0 - f.cosThetaT * f.cosThetaT;
    float A = eta3 * eta3 - f.eta2 * f.eta2 * sin2;
    float U = sqrt(max(0.5 * (A + abs(A)), 0.0));
    float V = sqrt(max(0.5 * (abs(A) - A), 0.0));
    float phiS = atan(2.0 * f.eta2 * V * f.cosThetaT, U * U + V * V - f.eta2 * f.eta2 * f.cosThetaT * f.cosThetaT);
    float phiP = atan(-2.0 * f.eta2 * eta3 * eta3 * f.cosThetaT * V, eta3 * eta3 * eta3 * eta3 * f.cosThetaT * f.cosThetaT - f.eta2 * f.eta2 * (U * U + V * V));
    return pbr_FilmResolve(f, vec3(R23.x), vec3(R23.y), vec3(phiP), vec3(phiS));
}

// The reflectance of a film over an F82 metal of normal reflectance F0 and tint, the effective IOR from F0 for the phase.
// Each polarization takes the Schlick reflectance, so a white metal stays white.
vec3 PBR_ThinFilmMetal(float cosTheta, float eta1, float filmIor, vec3 F0, vec3 tint, float thickness)
{
    PBR_Film f = PBR_FilmTop(cosTheta, eta1, filmIor, thickness);
    vec3 R23 = PBR_FresnelF82(f.cosThetaT, F0, PBR_F82Tint(F0, tint));
    vec3 sqrtF0 = sqrt(clamp(F0, 0.01, 0.99));
    vec3 eta3 = eta1 * (1.0 + sqrtF0) / (1.0 - sqrtF0);
    vec3 phi23 = mix(vec3(0.0), vec3(PI), lessThan(eta3, vec3(f.eta2)));
    return pbr_FilmResolve(f, R23, R23, phi23, phi23);
}
