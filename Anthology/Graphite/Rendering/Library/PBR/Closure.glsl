// The PBR model's closure: PBR_Prepare does everything that depends only on the view once per pixel, PBR_EvaluateLight
// then shades a light from it. Base lobes: EON diffuse under a GGX dielectric with exact Fresnel, beside an F82 metal
// sharing its D and G. Layers combine by albedo scaling with grey throughput, the substrate under the dielectric taking
// 1 - E_spec of the view, with E_spec from the dielectric table at the IOR specular_weight leaves and Turquin's multiple
// scattering compensation on every specular lobe, direct light included. Over the base: a thin film in the specular
// Fresnel, the coat with its absorption, darkening and roughening of the base, then the fuzz. Emission sits under the coat.
// subsurface_weight mixes the diffuse lobe toward subsurface. In bulk that is subsurface_color as diffuse albedo, light
// wrapping past the terminator by an amount from subsurface_radius and subsurface_radius_scale, an approximation of
// volumetric diffusion. Thin walled it is a sheet reflecting 0.5 S (1 - g) and transmitting 0.5 S (1 + g) of the light
// from its far side, both shaped as a white EON lobe of base_diffuse_roughness. Light from the far side passes the coat
// and fuzz on the view's side only.
// transmission_weight mixes the dielectric base under the specular between the opaque base and a translucent one, the
// latter passing 1 - E_spec of what is behind, thin film included. Thin walled transmission goes straight through,
// transmission_color the sheet's transmittance at normal incidence, so color^(1 / mu_t) at the view's refracted cosine.
// A volume with transmission_depth 0 tints by transmission_color, a deeper one absorbs and scatters along its path,
// see Graphite/PBR/Transmission.glsl. The translucent base takes its share only with PBR_TRANSMISSION.
//
// A Lighting block may replace how lights and ambient light are shaded:
//   vec3 Graphite_BRDF(PBR_Closure c, Graphite_LightContribution l), defining GRAPHITE_CUSTOM_BRDF, default PBR_EvaluateLight
//   vec3 Graphite_AmbientBRDF(PBR_Closure c, PBR_Indirect i), defining GRAPHITE_CUSTOM_AMBIENT_BRDF, default PBR_EvaluateAmbient
//
// Material axes, each on while its weight is not zero: PBR_COAT, PBR_FUZZ, PBR_THIN_FILM, PBR_SUBSURFACE,
// PBR_TRANSMISSION, and PBR_ANISOTROPY while either roughness anisotropy is. PBR_THIN_WALLED follows geometry_thin_walled.
// Axis PBR_TIER, global, FULL or LITE, the tier a view draws at. LITE blends at most one probe with the sky, has no thin
// film and no anisotropy, and lights the coat and fuzz lobes from the surroundings only. It keeps thin walled and
// subsurface whole, the sheet's back lighting included.
#pragma once

#if defined(PBR_THIN_WALLED) || defined(PBR_TRANSMISSION)
#define PBR_TRANSMITS
#endif

#if defined(PBR_SUBSURFACE) && defined(PBR_THIN_WALLED)
#define PBR_SHEET
#define GRAPHITE_LIGHT_BACK
#elif defined(PBR_SUBSURFACE)
#define PBR_WRAP
#endif

#ifdef PBR_TIER_LITE
#define GRAPHITE_MAX_BLENDED_PROBES 1
#else
#ifdef PBR_COAT
#define PBR_DIRECT_COAT
#define GRAPHITE_LIGHT_COAT
#endif
#ifdef PBR_FUZZ
#define PBR_DIRECT_FUZZ
#endif
#ifdef PBR_THIN_FILM
#define PBR_FILM
#endif
#ifdef PBR_ANISOTROPY
#define PBR_ANISOTROPIC
#endif
#endif

#include "Graphite/Lighting.glsl"
#include "Graphite/PBR/Params.glsl"
#include "Graphite/PBR/Microfacet.glsl"
#include "Graphite/PBR/Fresnel.glsl"
#include "Graphite/PBR/Diffuse.glsl"
#include "Graphite/PBR/Energy.glsl"
#include "Graphite/PBR/Fuzz.glsl"
#include "Graphite/PBR/ThinFilm.glsl"

struct PBR_Closure
{
    vec3  position;          // camera relative
    vec3  view;              // to the camera
    vec3  geomNormal;        // before normal mapping
    vec3  normal;            // the base lobes' shading normal
    float NdotV;
    float roughness;         // the base specular lobe's, roughened by the coat and after specular antialiasing
    float alpha2;            // its GGX alpha squared
#ifdef PBR_ANISOTROPIC
    vec3  tangent;
    vec3  bitangent;
    vec2  alpha;             // GGX alphas along the tangent and bitangent, filtered
#endif

    float metalness;
    float dielectricEta;     // the relative IOR specular_weight leaves, at least 1, for the Fresnel and energy tables
    float refractionEta;     // specular_ior from outside, at least 1, which light refracts into the base by through any coat
    vec3  specularColor;
    float dielectricScale;   // (1 - metalness) times the dielectric lobe's Turquin factor, less the film's share
    vec3  metalF0;
    vec3  metalTint;         // the F82 tint of specular_color
    vec3  metalScale;        // metalness times specular_weight times the metal lobe's Turquin factor, less the film's share
#ifdef PBR_FILM
    vec3  dielectricFilm;    // the film's reflectance over the dielectric, its share and Turquin factor applied
    vec3  metalFilm;         // the same over the metal
    vec3  film;              // the film's reflectance at the view, dielectric and metal blended
#endif
    vec3  dielectricTransmission;  // 1 - E of the dielectric reflection, thin film included, what passes into the base

    float diffuseRoughness;
    float diffuseWeight;     // the grey throughput reaching the diffuse lobe, (1 - metalness) (1 - E_spec)
    vec3  diffuseSingle;     // EON's single scattering rho A / pi, times the throughput
    vec3  diffuseMultiple;   // EON's multiple scattering lobe on the view's side, times the throughput
#ifdef PBR_WRAP
    vec3  subsurfaceAlbedo;  // subsurface_weight times subsurface_color times the throughput
    vec3  subsurfaceWrap;    // how far past the terminator light wraps, per channel
#endif
#ifdef PBR_SHEET
    float sheetSingle;       // a white EON lobe's terms at the view, the sheet's shape
    float sheetMultiple;
    vec3  sheetReflection;   // 0.5 S (1 - g) times subsurface_weight and the throughput
    vec3  sheetTransmission; // 0.5 S (1 + g) the same way, lit from the far side
#endif
#ifdef PBR_TRANSMITS
    vec3  transmission;      // the translucent base's share of what is behind, tinted unless a volume absorbs, below the coat and fuzz
#endif

    vec3  specularAlbedo;    // the compensated directional albedo of the base specular lobes, tinted
    vec3  diffuseAlbedo;     // the diffuse lobe's directional albedo times its throughput

#ifdef PBR_COAT
    float coatWeight;
    vec3  coatNormal;
    float coatNdotV;
    float coatRoughness;     // after specular antialiasing
    float coatAlpha2;
#ifdef PBR_ANISOTROPIC
    vec3  coatTangent;
    vec3  coatBitangent;
    vec2  coatAlpha;
#endif
    float coatEta;
    float coatScale;         // coat_weight times the coat's Turquin factor
    float coatAlbedo;        // coat_weight times the coat's compensated directional albedo E_coat
    float coatThroughput;    // 1 - E_coat
    vec3  coatLogColor;      // log2 of coat_color, the absorption over both refracted paths at normal incidence
    float coatInvMuO;        // 1 over the view's cosine refracted into the coat
    vec3  darkening;         // the modulated darkening factor lerp(1, Delta, C coat_darkening)
    vec3  coatedBase;        // what reaches the base and back toward the view, absorption at the view's angle on both paths
#endif

#ifdef PBR_FUZZ
    vec3  fuzzNormal;
    vec3  fuzzAxis;          // the LTC frame's X
    vec2  fuzzLtc;           // the inverse LTC matrix's a and b
    vec3  fuzzAlbedo;        // fuzz_weight times fuzz_color times E_fuzz
    float fuzzThroughput;    // 1 - fuzz_weight E_fuzz, untinted
#endif

    vec3  emission;          // nits, before the exposure, under the coat
    float opacity;
    float occlusion;         // ambient_occlusion
};

// The light reaching a surface from its surroundings, occlusion applied
struct PBR_Indirect
{
    vec3 irradiance;         // diffuse light over pi from the lightmap, probes or sky, so a white Lambert surface reflects it as is
#ifdef PBR_SHEET
    vec3 irradianceBack;     // the same on the far side, the normal flipped
#endif
    vec3 specular;          // the base specular lobe's probe radiance, its specular and horizon occlusion applied
#ifdef PBR_COAT
    vec3 coat;               // the coat lobe's, the same way
#endif
};


// The closure of a surface seen from view. mapVariance is the normal map's own variance, Toksvig's, for specular antialiasing.
PBR_Closure PBR_Prepare(PBR_Params p, vec3 relativePos, vec3 view, vec3 geomNormal, float mapVariance)
{
    PBR_Closure c;
    c.position = relativePos;
    c.view = view;
    c.geomNormal = geomNormal;
    c.normal = normalize(p.geometryNormal);
    c.NdotV = max(dot(c.normal, view), 1e-4);

    float C = 0.0;
    float rb = clamp(p.specularRoughness, 0.0, 1.0);
    float specularRoughness = rb;
    float baseEta = max(p.specularIor, 1e-4);
    float eta = baseEta;
    float outer = 1.0;
#ifdef PBR_COAT
    C = clamp(p.coatWeight, 0.0, 1.0);
    float rc = clamp(p.coatRoughness, 0.0, 1.0);
    float nc = max(p.coatIor, 1e-4);
    rb = mix(rb, pow(min(1.0, rb * rb * rb * rb + 2.0 * rc * rc * rc * rc), 0.25), C);
    eta = mix(baseEta, nc > baseEta ? nc / baseEta : baseEta / nc, C);
    outer = mix(1.0, nc, C);
#endif

    float alpha = PBR_Alpha(rb);
#ifdef GRAPHITE_FRAGMENT
    float variance = PBR_FilterVariance(c.normal, mapVariance);
#else
    float variance = 0.0;
#endif
    c.alpha2 = clamp(alpha * alpha + variance, PBR_MIN_ALPHA * PBR_MIN_ALPHA, 1.0);
    c.roughness = sqrt(sqrt(c.alpha2));
#ifdef PBR_ANISOTROPIC
    c.tangent = PBR_TangentFor(c.normal, p.geometryTangent);
    c.bitangent = cross(c.normal, c.tangent);
    vec2 anisotropic = PBR_AnisotropicAlpha(rb, p.specularRoughnessAnisotropy);
    c.alpha = sqrt(clamp(anisotropic * anisotropic + variance, PBR_MIN_ALPHA * PBR_MIN_ALPHA, 1.0));
#endif

    float M = clamp(p.baseMetalness, 0.0, 1.0);
    float weight = max(p.specularWeight, 0.0);
    vec3 dfg = PBR_Dfg(c.NdotV, c.roughness);
#ifdef PBR_ANISOTROPIC
    vec3 isotropicDfg = dfg;
    dfg = PBR_AnisotropicDfg(c.NdotV, c.roughness, p.specularRoughnessAnisotropy, PBR_Azimuth(c.tangent, c.bitangent, view));
#endif
    float E1 = PBR_DfgE1(dfg);

    float unmodulatedF0 = (eta - 1.0) / (eta + 1.0);
    unmodulatedF0 *= unmodulatedF0;
    eta = PBR_ModulatedEta(eta, weight);
    eta = eta < 1.0 ? 1.0 / eta : eta;
    float dielectricTurquin = PBR_TurquinFactor(E1, PBR_DielectricFavg(eta));
    float dielectricE = PBR_DielectricE(c.NdotV, c.roughness, eta) * dielectricTurquin;
#ifdef PBR_ANISOTROPIC
    dielectricE *= PBR_AnisotropicScale(dfg, isotropicDfg, eta);
#endif
    c.metalness = M;
    c.dielectricEta = eta;
    c.refractionEta = baseEta < 1.0 ? 1.0 / baseEta : baseEta;
    c.specularColor = p.specularColor;
    c.dielectricScale = (1.0 - M) * dielectricTurquin;

    vec3 rho = clamp(p.baseWeight * p.baseColor, 0.0, 1.0);
    c.metalF0 = rho;
    c.metalTint = PBR_F82Tint(rho, p.specularColor);
    c.metalScale = M * weight * PBR_TurquinFactor(E1, PBR_MetalFavg(rho, p.specularColor) * weight);
    vec3 metalE = PBR_TurquinMetal(dfg, rho, p.specularColor, weight);
    vec3 dielectricE3 = vec3(dielectricE);

#ifdef PBR_FILM
    float filmWeight = p.thinFilmThickness > 0.0 ? clamp(p.thinFilmWeight, 0.0, 1.0) : 0.0;
    float filmCos = PBR_RefractedCosine(c.NdotV, outer);
    vec3 filmDielectric = PBR_ThinFilmDielectric(filmCos, outer, p.thinFilmIor, outer * PBR_ModulatedEta(baseEta / outer, weight), p.thinFilmThickness);
    vec3 filmMetal = PBR_ThinFilmMetal(filmCos, outer, p.thinFilmIor, rho, p.specularColor, p.thinFilmThickness);
    vec3 dielectricFilmTurquin = PBR_TurquinFactor(E1, filmDielectric);
    vec3 metalFilmTurquin = PBR_TurquinFactor(E1, filmMetal * weight);
    dielectricE3 = mix(dielectricE3, filmDielectric * E1 * dielectricFilmTurquin, filmWeight);
    metalE = mix(metalE, weight * filmMetal * E1 * metalFilmTurquin, filmWeight);
    c.dielectricFilm = (1.0 - M) * filmWeight * filmDielectric * dielectricFilmTurquin;
    c.metalFilm = M * weight * filmWeight * filmMetal * metalFilmTurquin;
    c.dielectricScale *= 1.0 - filmWeight;
    c.metalScale *= 1.0 - filmWeight;
    c.film = mix(filmDielectric, filmMetal, M);
#endif
    c.dielectricTransmission = 1.0 - dielectricE3;
    c.specularAlbedo = (1.0 - M) * dielectricE3 * p.specularColor + M * metalE;

    float rd = clamp(p.baseDiffuseRoughness, 0.0, 1.0);
    float average = PBR_FonAverage(rd);
    c.diffuseRoughness = rd;
    c.diffuseWeight = (1.0 - M) * (1.0 - Graphite_Luminance(dielectricE3));
    float opaque = c.diffuseWeight;
#ifdef PBR_TRANSMITS
    float T = clamp(p.transmissionWeight, 0.0, 1.0);
    vec3 tint = clamp(p.transmissionColor, 0.0, 1.0);
#ifdef PBR_THIN_WALLED
    tint = pow(tint, vec3(1.0 / max(PBR_RefractedCosine(c.NdotV, c.refractionEta), 1e-4)));
#else
    tint = p.transmissionDepth > 0.0 ? vec3(1.0) : tint;
#endif
    c.transmission = (1.0 - M) * T * tint * c.dielectricTransmission;
#ifdef PBR_TRANSMISSION
    opaque *= 1.0 - T;
#endif
#endif
    float S = 0.0;
#ifdef PBR_SUBSURFACE
    S = clamp(p.subsurfaceWeight, 0.0, 1.0);
    vec3 subsurface = S * opaque * clamp(p.subsurfaceColor, 0.0, 1.0);
#endif
    float diffuse = (1.0 - S) * opaque;
    float multipleShape = max(1e-7, 1.0 - PBR_FonAlbedo(c.NdotV, rd)) / (max(1e-7, 1.0 - average) * GRAPHITE_PI);
    c.diffuseSingle = rho * (diffuse / ((1.0 + PBR_FON_C1 * rd) * GRAPHITE_PI));
    c.diffuseMultiple = PBR_EonMultipleAlbedo(rho, average) * (diffuse * multipleShape);
    c.diffuseAlbedo = PBR_EonAlbedo(rho, rd, c.NdotV) * diffuse;
#ifdef PBR_SHEET
    float g = clamp(p.subsurfaceScatterAnisotropy, -1.0, 1.0);
    c.sheetSingle = 1.0 / ((1.0 + PBR_FON_C1 * rd) * GRAPHITE_PI);
    c.sheetMultiple = multipleShape;
    c.sheetReflection = subsurface * (0.5 * (1.0 - g));
    c.sheetTransmission = subsurface * (0.5 * (1.0 + g));
    c.diffuseAlbedo += c.sheetReflection;
#elif defined(PBR_WRAP)
    vec3 reach = max(p.subsurfaceRadius, 0.0) * max(p.subsurfaceRadiusScale, 0.0);
    c.subsurfaceAlbedo = subsurface;
    c.subsurfaceWrap = reach / (1.0 + reach);
    c.diffuseAlbedo += subsurface;
#endif

    c.emission = p.emissionColor * p.emissionLuminance;

#ifdef PBR_COAT
    c.coatWeight = C;
    c.coatNormal = normalize(p.geometryCoatNormal);
    c.coatNdotV = max(dot(c.coatNormal, view), 1e-4);
    float coatAlpha = PBR_Alpha(rc);
#ifdef GRAPHITE_FRAGMENT
    float coatVariance = PBR_FilterVariance(c.coatNormal, 0.0);
#else
    float coatVariance = 0.0;
#endif
    c.coatAlpha2 = clamp(coatAlpha * coatAlpha + coatVariance, PBR_MIN_ALPHA * PBR_MIN_ALPHA, 1.0);
    c.coatRoughness = sqrt(sqrt(c.coatAlpha2));
#ifdef PBR_ANISOTROPIC
    c.coatTangent = PBR_TangentFor(c.coatNormal, p.geometryCoatTangent);
    c.coatBitangent = cross(c.coatNormal, c.coatTangent);
    vec2 coatAnisotropic = PBR_AnisotropicAlpha(rc, p.coatRoughnessAnisotropy);
    c.coatAlpha = sqrt(clamp(coatAnisotropic * coatAnisotropic + coatVariance, PBR_MIN_ALPHA * PBR_MIN_ALPHA, 1.0));
#endif
    c.coatEta = nc < 1.0 ? 1.0 / nc : nc;
    vec3 coatDfg = PBR_Dfg(c.coatNdotV, c.coatRoughness);
    float coatScale = 1.0;
#ifdef PBR_ANISOTROPIC
    vec3 coatIsotropic = coatDfg;
    coatDfg = PBR_AnisotropicDfg(c.coatNdotV, c.coatRoughness, p.coatRoughnessAnisotropy, PBR_Azimuth(c.coatTangent, c.coatBitangent, view));
    coatScale = PBR_AnisotropicScale(coatDfg, coatIsotropic, c.coatEta);
#endif
    float coatTurquin = PBR_TurquinFactor(PBR_DfgE1(coatDfg), PBR_DielectricFavg(c.coatEta));
    float coatE = PBR_DielectricE(c.coatNdotV, c.coatRoughness, c.coatEta) * coatScale * coatTurquin;
    c.coatScale = C * coatTurquin;
    c.coatAlbedo = C * coatE;
    c.coatThroughput = 1.0 - coatE;
    c.coatLogColor = log2(clamp(p.coatColor, 1e-6, 1.0));
    c.coatInvMuO = 1.0 / max(PBR_RefractedCosine(c.coatNdotV, c.coatEta), 1e-4);

    float coatF = PBR_FresnelDielectric(c.coatNdotV, nc);
    float Kr = 1.0 - (1.0 - PBR_FresnelAverage(nc)) / (nc * nc);
    float rDielectric = mix(1.0, specularRoughness, clamp(weight * unmodulatedF0, 0.0, 1.0));
    float K = mix(coatF, Kr, mix(rDielectric, specularRoughness, M));
    vec3 Eb =mix(mix(rho, clamp(p.subsurfaceColor, 0.0, 1.0), S), weight * rho, M);
    vec3 delta = (1.0 - K) / max(1.0 - Eb * K, 1e-4);
    c.darkening = mix(vec3(1.0), delta, C * clamp(p.coatDarkening, 0.0, 1.0));

    vec3 viewAbsorption = exp2(c.coatLogColor * c.coatInvMuO);
    c.coatedBase = mix(vec3(1.0), c.coatThroughput * viewAbsorption, C) * c.darkening;
    c.emission *= mix(vec3(1.0), viewAbsorption * (1.0 - coatF), C);
#endif

#ifdef PBR_FUZZ
    float fuzzWeight = clamp(p.fuzzWeight, 0.0, 1.0);
    float fuzzRoughness = clamp(p.fuzzRoughness, PBR_FUZZ_MIN_ROUGHNESS, 1.0);
#ifdef PBR_COAT
    c.fuzzNormal = normalize(mix(c.normal, c.coatNormal, C));
#else
    c.fuzzNormal = c.normal;
#endif
    float fuzzNdotV = clamp(dot(c.fuzzNormal, view), 1e-4, 1.0);
    float fuzzE = PBR_FuzzAlbedo(fuzzNdotV, fuzzRoughness);
    c.fuzzAxis = PBR_FuzzAxis(c.fuzzNormal, view, fuzzNdotV);
    c.fuzzLtc = PBR_FuzzLtc(fuzzNdotV, fuzzRoughness);
    c.fuzzAlbedo = fuzzWeight * fuzzE * clamp(p.fuzzColor, 0.0, 1.0);
    c.fuzzThroughput = 1.0 - fuzzWeight * fuzzE;
#endif

    c.opacity = clamp(p.geometryOpacity, 0.0, 1.0);
    c.occlusion = clamp(p.occlusion, 0.0, 1.0);
    return c;
}

// The frame Graphite's light loop lights, isotropic alphas for the representative points
Graphite_LightFrame PBR_LightFrame(PBR_Closure c)
{
    Graphite_LightFrame f;
    f.position = c.position;
    f.view = c.view;
    f.geomNormal = c.geomNormal;
    f.baseNormal = c.normal;
    f.baseAlpha = sqrt(c.alpha2);
#ifdef PBR_COAT
    f.coatNormal = c.coatNormal;
    f.coatAlpha = sqrt(c.coatAlpha2);
#else
    f.coatNormal = c.normal;
    f.coatAlpha = f.baseAlpha;
#endif
    return f;
}

// An EON lobe toward L from its single and multiple scattering terms at the view, without the cosine
vec3 pbr_EonLobe(PBR_Closure c, vec3 single, vec3 multiple, vec3 L)
{
    float muI = clamp(dot(c.normal, L), 0.0, 1.0);
    float s = dot(L, c.view) - muI * c.NdotV;
    float sOverT = s > 0.0 ? s / max(max(muI, c.NdotV), 1e-6) : s;
    return single * (1.0 + c.diffuseRoughness * sOverT) + multiple * max(1e-7, 1.0 - PBR_FonAlbedo(muI, c.diffuseRoughness));
}

// The base lobes' diffuse BRDF toward the light along L, its throughput applied, without the cosine
vec3 PBR_EvaluateDiffuse(PBR_Closure c, vec3 L)
{
    vec3 color = pbr_EonLobe(c, c.diffuseSingle, c.diffuseMultiple, L);
#ifdef PBR_SHEET
    color += c.sheetReflection * pbr_EonLobe(c, vec3(c.sheetSingle), vec3(c.sheetMultiple), L);
#endif
    return color;
}

#ifdef PBR_SHEET
// The sheet's transmission toward the view of light from the far side along L, mirrored through the sheet, without the cosine
vec3 PBR_EvaluateSheetTransmission(PBR_Closure c, vec3 L)
{
    return c.sheetTransmission * pbr_EonLobe(c, vec3(c.sheetSingle), vec3(c.sheetMultiple), reflect(L, c.normal));
}
#endif

#ifdef PBR_WRAP
// Bulk subsurface lit by a light along L: Lambert with the light wrapping past the terminator, normalized so it keeps
// its energy. irradiance and attenuation are the light's facing and full irradiance per unit radiance.
vec3 PBR_EvaluateWrap(PBR_Closure c, vec3 L, float irradiance, float attenuation)
{
    float behind = min(dot(c.normal, L), 0.0);
    vec3 w = c.subsurfaceWrap;
    return c.subsurfaceAlbedo / GRAPHITE_PI * max(irradiance + attenuation * (w + behind), 0.0) / ((1.0 + w) * (1.0 + w));
}
#endif

// The base lobes' specular BRDF toward the light along L, Turquin compensated, without the cosine
vec3 PBR_EvaluateSpecular(PBR_Closure c, vec3 L)
{
    vec3 H = normalize(L + c.view);
    float NdotL = clamp(dot(c.normal, L), 0.0, 1.0);
    float NdotH = clamp(dot(c.normal, H), 0.0, 1.0);
    float LdotH = clamp(dot(L, H), 0.0, 1.0);
#ifdef PBR_ANISOTROPIC
    float dv = PBR_DistributionAnisotropic(dot(c.tangent, H), dot(c.bitangent, H), NdotH, c.alpha)
        * PBR_VisibilityAnisotropic(vec3(dot(c.tangent, c.view), dot(c.bitangent, c.view), c.NdotV), vec3(dot(c.tangent, L), dot(c.bitangent, L), NdotL), c.alpha);
#else
    float dv = PBR_DistributionGGX(NdotH, c.alpha2) * PBR_VisibilitySmithGGX(c.NdotV, NdotL, c.alpha2);
#endif
    vec3 dielectric = vec3(c.dielectricScale * PBR_FresnelDielectric(LdotH, c.dielectricEta));
    vec3 metal = c.metalScale * PBR_FresnelF82(LdotH, c.metalF0, c.metalTint);
#ifdef PBR_FILM
    dielectric += c.dielectricFilm;
    metal += c.metalFilm;
#endif
    return (c.specularColor * dielectric + metal) * dv;
}

#ifdef PBR_COAT
// The coat lobe's BRDF toward the light along L, coat_weight and Turquin applied, without the cosine
float PBR_EvaluateCoat(PBR_Closure c, vec3 L)
{
    vec3 N = c.coatNormal;
    vec3 H = normalize(L + c.view);
    float NdotL = clamp(dot(N, L), 0.0, 1.0);
    float NdotH = clamp(dot(N, H), 0.0, 1.0);
    float LdotH = clamp(dot(L, H), 0.0, 1.0);
#ifdef PBR_ANISOTROPIC
    float dv = PBR_DistributionAnisotropic(dot(c.coatTangent, H), dot(c.coatBitangent, H), NdotH, c.coatAlpha)
        * PBR_VisibilityAnisotropic(vec3(dot(c.coatTangent, c.view), dot(c.coatBitangent, c.view), c.coatNdotV), vec3(dot(c.coatTangent, L), dot(c.coatBitangent, L), NdotL), c.coatAlpha);
#else
    float dv = PBR_DistributionGGX(NdotH, c.coatAlpha2) * PBR_VisibilitySmithGGX(c.coatNdotV, NdotL, c.coatAlpha2);
#endif
    return c.coatScale * PBR_FresnelDielectric(LdotH, c.coatEta) * dv;
}

// What of the base reaches a light along L and the view through the coat: albedo scaling, absorption over both
// refracted paths, darkening
vec3 PBR_CoatedBase(PBR_Closure c, vec3 L)
{
#ifdef PBR_DIRECT_COAT
    float invMuI = 1.0 / max(PBR_RefractedCosine(clamp(dot(c.coatNormal, L), 0.0, 1.0), c.coatEta), 1e-4);
    vec3 absorption = exp2(c.coatLogColor * (0.5 * (invMuI + c.coatInvMuO)));
    return mix(vec3(1.0), c.coatThroughput * absorption, c.coatWeight) * c.darkening;
#else
    return c.coatedBase;
#endif
}
#endif

// What a light gives the closure, each lobe from its own direction
vec3 PBR_EvaluateLight(PBR_Closure c, Graphite_LightContribution l)
{
    vec3 color = PBR_EvaluateDiffuse(c, l.diffuseL) * l.diffuse + PBR_EvaluateSpecular(c, l.specularL) * l.specular;
#ifdef PBR_WRAP
    color += PBR_EvaluateWrap(c, l.diffuseL, l.diffuse, l.attenuation);
#endif
#ifdef PBR_SHEET
    vec3 back = PBR_EvaluateSheetTransmission(c, l.diffuseL) * l.diffuseBack;
#endif
#ifdef PBR_COAT
    color *= PBR_CoatedBase(c, l.diffuseL);
#ifdef PBR_SHEET
    back *= c.coatedBase;
#endif
#ifdef PBR_DIRECT_COAT
    color += PBR_EvaluateCoat(c, l.coatL) * l.coat;
#endif
#endif
#ifdef PBR_SHEET
    color += back;
#endif
#ifdef PBR_FUZZ
    color *= c.fuzzThroughput;
#ifdef PBR_DIRECT_FUZZ
    color += c.fuzzAlbedo * (PBR_FuzzLobe(l.diffuseL, c.fuzzNormal, c.fuzzAxis, c.fuzzLtc) * l.attenuation);
#endif
#endif
    return color * l.radiance;
}

// What the surroundings give the closure
vec3 PBR_EvaluateAmbient(PBR_Closure c, PBR_Indirect i)
{
    vec3 color = c.diffuseAlbedo * i.irradiance + c.specularAlbedo * i.specular;
#ifdef PBR_SHEET
    color += c.sheetTransmission * i.irradianceBack;
#endif
#ifdef PBR_COAT
    color = color * c.coatedBase + c.coatAlbedo * i.coat;
#endif
#ifdef PBR_FUZZ
    color = color * c.fuzzThroughput + c.fuzzAlbedo * i.irradiance;
#endif
    return color;
}

#ifdef PBR_TRANSMITS
// What of the light from behind reaches the view through the translucent base, the coat and fuzz on the view's side
vec3 PBR_Transmittance(PBR_Closure c)
{
    vec3 transmittance = c.transmission;
#ifdef PBR_COAT
    transmittance *= c.coatedBase;
#endif
#ifdef PBR_FUZZ
    transmittance *= c.fuzzThroughput;
#endif
    return transmittance;
}
#endif

vec3 Graphite_BRDF(PBR_Closure c, Graphite_LightContribution l);

#ifndef GRAPHITE_CUSTOM_BRDF
vec3 Graphite_BRDF(PBR_Closure c, Graphite_LightContribution l) { return PBR_EvaluateLight(c, l); }
#endif

vec3 Graphite_AmbientBRDF(PBR_Closure c, PBR_Indirect i);

#ifndef GRAPHITE_CUSTOM_AMBIENT_BRDF
vec3 Graphite_AmbientBRDF(PBR_Closure c, PBR_Indirect i) { return PBR_EvaluateAmbient(c, i); }
#endif

// Every directional and local light through Graphite_BRDF
vec3 PBR_ShadeLights(PBR_Closure c)
{
    Graphite_LightFrame f = PBR_LightFrame(c);
    vec3 total = vec3(0.0);
    Graphite_LightContribution l;
    for (uint i = 0u; i < Graphite_DirectionalCount(); i++)
    {
        if (Graphite_ContributeDirectional(Graphite_GetDirectional(i), i, f, l))
            total += Graphite_BRDF(c, l);
    }

#ifdef PBR_WRAP
    bool wraps = true;
#else
    bool wraps = false;
#endif
    Graphite_LightIterator it = Graphite_BeginLights(c.position);
    Graphite_LightSample L;
    while (Graphite_NextLight(it, L))
    {
        if (Graphite_ContributeLocal(L, f, wraps, l))
            total += Graphite_BRDF(c, l);
    }
    return total;
}

#ifdef GRAPHITE_FRAGMENT
// How much of the base specular lobe's reflection reaches the surface: specular occlusion from its own and the view's
// screen occlusion, and horizon occlusion against the geometric normal
float PBR_BaseSpecularOcclusion(PBR_Closure c)
{
    float ao = c.occlusion * Graphite_ScreenOcclusion();
    return Graphite_SpecularOcclusion(c.NdotV, ao, sqrt(c.alpha2)) * Graphite_HorizonOcclusion(reflect(-c.view, c.normal), c.geomNormal);
}

#ifdef PBR_COAT
float PBR_CoatSpecularOcclusion(PBR_Closure c)
{
    float ao = c.occlusion * Graphite_ScreenOcclusion();
    return Graphite_SpecularOcclusion(c.coatNdotV, ao, sqrt(c.coatAlpha2)) * Graphite_HorizonOcclusion(reflect(-c.view, c.coatNormal), c.geomNormal);
}
#endif

// The probes around the closure, found once per pixel for every lobe sampling them, none before the sky is captured
Graphite_ProbeSet PBR_FindProbes(PBR_Closure c)
{
    Graphite_ProbeSet probes;
    probes.count = 0;
    probes.sky = 0.0;
    if (Graphite_ReflectionProbesReady())
        probes = Graphite_FindProbes(c.position);
    return probes;
}

// The surroundings' light on the closure, diffuse from Graphite_IndirectDiffuse with screen occlusion applied
PBR_Indirect PBR_GatherIndirect(PBR_Closure c, vec2 lightmapUV, Graphite_ProbeSet probes)
{
    PBR_Indirect i;
    i.irradiance = Graphite_IndirectDiffuse(c.normal, lightmapUV) * c.occlusion;
#ifdef PBR_SHEET
    i.irradianceBack = Graphite_IndirectDiffuse(-c.normal, lightmapUV) * c.occlusion;
#endif
    i.specular = vec3(0.0);
#ifdef PBR_COAT
    i.coat = vec3(0.0);
#endif
    if (Graphite_ReflectionProbesReady())
    {
        i.specular = Graphite_ProbeRadiance(probes, c.normal, c.view, sqrt(c.alpha2)) * PBR_BaseSpecularOcclusion(c);
#ifdef PBR_COAT
        i.coat = Graphite_ProbeRadiance(probes, c.coatNormal, c.view, sqrt(c.coatAlpha2)) * PBR_CoatSpecularOcclusion(c);
#endif
    }
    return i;
}

// The top specular lobe the G-buffer holds for screen space reflections, the one reflecting most: its normal, its
// reflectance as the ambient light weighs it, occlusion included, and its roughness
vec3 PBR_TopReflectance(PBR_Closure c, out vec3 normal, out float roughness)
{
    vec3 reflectance = c.specularAlbedo * PBR_BaseSpecularOcclusion(c);
    normal = c.normal;
    roughness = c.roughness;
#ifdef PBR_COAT
    reflectance *= c.coatedBase;
    float coat = c.coatAlbedo * PBR_CoatSpecularOcclusion(c);
    if (coat > Graphite_Luminance(reflectance))
    {
        reflectance = vec3(coat);
        normal = c.coatNormal;
        roughness = c.coatRoughness;
    }
#endif
#ifdef PBR_FUZZ
    reflectance *= c.fuzzThroughput;
#endif
    return reflectance;
}
#endif
