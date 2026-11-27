// A coat, fuzz or bulk subsurface lobe over a closure prepared without one, for surface models that are the Lit
// surface plus one lobe: each lights through these from a few values of its own, so a closure without the lobe pays
// nothing for it. The lobe's terms are found each time it lights. Included after Graphite/PBR/Closure.glsl, whose own
// PBR_COAT, PBR_FUZZ and PBR_SUBSURFACE lobes these follow, without a coat's colour or normal and without a
// subsurface radius.
#pragma once

struct PBR_CoatLobe
{
    float weight;
    float alpha2;
    float eta;
    float albedo;   // weight times the coat's directional albedo
    vec3  base;     // what reaches the base and comes back through the coat, darkening included
};

// The coat over a closure whose base Model_Params of the coat's surface model already roughened and set under the coat
PBR_CoatLobe PBR_CoatOver(PBR_Closure c, float weight, float roughness, float ior, float darkening)
{
    PBR_CoatLobe coat;
    float C = clamp(weight, 0.0, 1.0);
    float nc = max(ior, 1e-4);
    float alpha = PBR_Alpha(clamp(roughness, 0.0, 1.0));
    coat.weight = C;
    coat.alpha2 = clamp(alpha * alpha, PBR_MIN_ALPHA * PBR_MIN_ALPHA, 1.0);
    coat.eta = nc < 1.0 ? 1.0 / nc : nc;
    vec3 dfg = PBR_Dfg(c.NdotV, sqrt(sqrt(coat.alpha2)));
    float f0 = (coat.eta - 1.0) / (coat.eta + 1.0);
    float E = f0 * f0 * dfg.x + dfg.y;
    coat.albedo = C * E;

    float coatF = PBR_FresnelDielectric(c.NdotV, nc);
    float Kr = 1.0 - (1.0 - PBR_FresnelAverage(nc)) / (nc * nc);
    float baseF0 = (c.dielectricEta - 1.0) / (c.dielectricEta + 1.0);
    float rDielectric = mix(1.0, c.roughness, clamp(baseF0 * baseF0, 0.0, 1.0));
    float K = mix(coatF, Kr, mix(rDielectric, c.roughness, c.metalness));
    vec3 delta = (1.0 - K) / max(1.0 - c.metalF0 * K, 1e-4);
    coat.base = mix(vec3(1.0), vec3(1.0 - E), C) * mix(vec3(1.0), delta, C * clamp(darkening, 0.0, 1.0));
    return coat;
}

vec3 PBR_CoatLight(PBR_Closure c, Graphite_LightContribution l, PBR_CoatLobe coat)
{
    vec3 L = l.specularL;
    vec3 H = normalize(L + c.view);
    float NdotL = clamp(dot(c.normal, L), 0.0, 1.0);
    float NdotH = clamp(dot(c.normal, H), 0.0, 1.0);
    float LdotH = clamp(dot(L, H), 0.0, 1.0);
    float lobe = l.shareSpecular ? coat.albedo
        : coat.weight * pbr_Fresnel(LdotH, coat.eta) * PBR_DistributionGGX(NdotH, coat.alpha2) * PBR_VisibilitySmithGGX(c.NdotV, NdotL, coat.alpha2);
    return PBR_EvaluateLight(c, l) * coat.base + lobe * l.specular * l.radiance;
}

vec3 PBR_CoatAmbient(PBR_Closure c, PBR_Indirect i, PBR_CoatLobe coat)
{
    vec3 reflected = vec3(0.0);
    if (Graphite_ReflectionProbesReady())
    {
        vec4 screen = vec4(0.0);
        float visibility = 1.0;
#ifdef GRAPHITE_GBUFFER_READ
        Graphite_ScreenLighting(screen, visibility);
#endif
        float alpha = sqrt(coat.alpha2);
        float occlusion = Graphite_SpecularOcclusion(c.NdotV, c.occlusion, alpha) * Graphite_HorizonOcclusion(reflect(-c.view, c.normal), c.geomNormal);
        reflected = PBR_ProbeLight(PBR_FindProbes(c), screen, visibility, c.normal, c.view, c.NdotV, c.occlusion, alpha) * occlusion;
    }
    return PBR_EvaluateAmbient(c, i) * coat.base + coat.albedo * reflected;
}

// How much of the light fuzz of a weight and roughness takes at the closure's view
float PBR_FuzzOver(PBR_Closure c, float weight, float roughness)
{
    return clamp(weight, 0.0, 1.0) * PBR_FuzzAlbedo(c.NdotV, clamp(roughness, PBR_FUZZ_MIN_ROUGHNESS, 1.0));
}

vec3 PBR_FuzzLight(PBR_Closure c, Graphite_LightContribution l, vec3 tint, float weight, float roughness)
{
    float r = clamp(roughness, PBR_FUZZ_MIN_ROUGHNESS, 1.0);
    float taken = PBR_FuzzOver(c, weight, r);
    float lobe = PBR_FuzzLobe(l.diffuseL, c.normal, PBR_FuzzAxis(c.normal, c.view, c.NdotV), PBR_FuzzLtc(c.NdotV, r));
    return PBR_EvaluateLight(c, l) * (1.0 - taken) + taken * clamp(tint, 0.0, 1.0) * (lobe * l.attenuation) * l.radiance;
}

vec3 PBR_FuzzAmbient(PBR_Closure c, PBR_Indirect i, vec3 tint, float weight, float roughness)
{
    float taken = PBR_FuzzOver(c, weight, roughness);
    return PBR_EvaluateAmbient(c, i) * (1.0 - taken) + taken * clamp(tint, 0.0, 1.0) * i.irradiance;
}

// How far past the terminator light wraps for the default subsurface radius and its scale
const vec3 PBR_SUBSURFACE_WRAP = vec3(0.5, 0.3333333, 0.2);

// Bulk subsurface taking a weight of the diffuse lobe: its light wraps past the terminator, so the lights behind the
// surface must reach it, see PBR_LIGHT_WRAPS
vec3 PBR_SubsurfaceLight(PBR_Closure c, Graphite_LightContribution l, vec3 tint, float weight)
{
    float S = clamp(weight, 0.0, 1.0);
    vec3 specular = l.shareSpecular ? c.specularAlbedo : PBR_EvaluateSpecular(c, l.specularL);
    vec3 albedo = S * c.diffuseWeight * clamp(tint, 0.0, 1.0);
    vec3 w = PBR_SUBSURFACE_WRAP;
    float behind = min(dot(c.normal, l.diffuseL), 0.0);
    vec3 wrap = albedo / GRAPHITE_PI * max(l.diffuse + l.attenuation * (w + behind), 0.0) / ((1.0 + w) * (1.0 + w));
    return (PBR_EvaluateDiffuse(c, l.diffuseL) * (l.diffuse * (1.0 - S)) + specular * l.specular + wrap) * l.radiance;
}

vec3 PBR_SubsurfaceAmbient(PBR_Closure c, PBR_Indirect i, vec3 tint, float weight)
{
    float S = clamp(weight, 0.0, 1.0);
    return (c.diffuseAlbedo * (1.0 - S) + S * c.diffuseWeight * clamp(tint, 0.0, 1.0)) * i.irradiance + c.specularAlbedo * i.specular;
}
