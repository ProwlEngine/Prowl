// Fresnel for the PBR model: exact unpolarized dielectric Fresnel, the specular_weight modulation of the IOR, and
// Hoffman's F82 tint for metals.
#pragma once

// Exact unpolarized Fresnel reflectance at relative IOR eta, 1 past total internal reflection
float PBR_FresnelDielectric(float cosTheta, float eta)
{
    float c = clamp(cosTheta, 0.0, 1.0);
    float g2 = eta * eta - 1.0 + c * c;
    if (g2 <= 0.0)
        return 1.0;
    float g = sqrt(g2);
    float a = (g - c) / (g + c);
    float b = (c * (g + c) - 1.0) / (c * (g - c) + 1.0);
    return 0.5 * a * a * (1.0 + b * b);
}

// Exact dielectric Fresnel per polarization, (Rp, Rs), 1 past total internal reflection
vec2 PBR_FresnelDielectricPolarized(float cosTheta, float eta)
{
    float cos2 = clamp(cosTheta * cosTheta, 0.0, 1.0);
    float sin2 = 1.0 - cos2;
    float t0 = max(eta * eta - sin2, 0.0);
    float t1 = t0 + cos2;
    float t2 = 2.0 * sqrt(t0) * sqrt(cos2);
    float rs = (t1 - t2) / max(t1 + t2, 1e-9);
    float t3 = cos2 * t0 + sin2 * sin2;
    float t4 = t2 * sin2;
    return vec2(rs * (t3 - t4) / max(t3 + t4, 1e-9), rs);
}

// The cosine weighted average of dielectric Fresnel, d'Eon's fit, within 0.2% for eta 1 to 3, mirrored below 1
float PBR_FresnelAverage(float eta)
{
    float e = eta < 1.0 ? 1.0 / eta : eta;
    float average = log((10893.0 * e - 1438.2) / (-774.4 * e * e + 10212.0 * e + 1.0));
    return eta < 1.0 ? 1.0 - eta * eta * (1.0 - average) : average;
}

// The cosine of a ray at cosine mu refracted into a medium of relative IOR eta
float PBR_RefractedCosine(float mu, float eta) { return sqrt(max(1.0 - (1.0 - mu * mu) / (eta * eta), 0.0)); }

// The IOR specular_weight leaves: eps = sign(eta - 1) sqrt(clamp(weight F0(eta), 0, 0.99999)), eta' = (1 + eps) / (1 - eps)
float PBR_ModulatedEta(float eta, float weight)
{
    float r = (eta - 1.0) / (eta + 1.0);
    float eps = sign(eta - 1.0) * sqrt(clamp(weight * r * r, 0.0, 0.99999));
    return (1.0 + eps) / (1.0 - eps);
}

// Hoffman's F82 metal Fresnel, tint the PBR_F82Tint of its edge colour
vec3 PBR_FresnelF82(float cosTheta, vec3 F0, vec3 tint)
{
    float c = clamp(cosTheta, 0.0, 1.0);
    float m = 1.0 - c;
    float m5 = m * m * m * m * m;
    return clamp(F0 + (1.0 - F0) * m5 - tint * (c * m5 * m), 0.0, 1.0);
}
