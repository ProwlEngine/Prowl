// Every input of the PBR model, OpenPBR Surface v1.1.1, as a plain value. Fields are the spec's parameter names in
// camel case (base_color is baseColor), so they never meet the material properties of the same snake case names.
// Lengths are in scene units, thin_film_thickness in micrometres, emission_luminance in nits. Directions are world space.
#pragma once

struct PBR_Params
{
    float baseWeight;
    vec3  baseColor;
    float baseMetalness;
    float baseDiffuseRoughness;

    float specularWeight;
    vec3  specularColor;
    float specularRoughness;
    float specularRoughnessAnisotropy;
    float specularIor;

    float transmissionWeight;
    vec3  transmissionColor;
    float transmissionDepth;
    vec3  transmissionScatter;
    float transmissionScatterAnisotropy;
    float transmissionDispersionScale;
    float transmissionDispersionAbbeNumber;

    float subsurfaceWeight;
    vec3  subsurfaceColor;
    float subsurfaceRadius;
    vec3  subsurfaceRadiusScale;
    float subsurfaceScatterAnisotropy;

    float coatWeight;
    vec3  coatColor;
    float coatRoughness;
    float coatRoughnessAnisotropy;
    float coatIor;
    float coatDarkening;

    float fuzzWeight;
    vec3  fuzzColor;
    float fuzzRoughness;

    float emissionLuminance;
    vec3  emissionColor;

    float thinFilmWeight;
    float thinFilmThickness;
    float thinFilmIor;

    float geometryOpacity;
    bool  geometryThinWalled;
    vec3  geometryNormal;
    vec3  geometryTangent;
    vec3  geometryCoatNormal;
    vec3  geometryCoatTangent;

    float occlusion;            // ambient_occlusion, baked on indirect light only
};

// The spec's defaults, the frame given
PBR_Params PBR_DefaultParams(vec3 normal, vec3 tangent)
{
    PBR_Params p;
    p.baseWeight = 1.0;
    p.baseColor = vec3(0.8);
    p.baseMetalness = 0.0;
    p.baseDiffuseRoughness = 0.0;
    p.specularWeight = 1.0;
    p.specularColor = vec3(1.0);
    p.specularRoughness = 0.3;
    p.specularRoughnessAnisotropy = 0.0;
    p.specularIor = 1.5;
    p.transmissionWeight = 0.0;
    p.transmissionColor = vec3(1.0);
    p.transmissionDepth = 0.0;
    p.transmissionScatter = vec3(0.0);
    p.transmissionScatterAnisotropy = 0.0;
    p.transmissionDispersionScale = 0.0;
    p.transmissionDispersionAbbeNumber = 20.0;
    p.subsurfaceWeight = 0.0;
    p.subsurfaceColor = vec3(0.8);
    p.subsurfaceRadius = 1.0;
    p.subsurfaceRadiusScale = vec3(1.0, 0.5, 0.25);
    p.subsurfaceScatterAnisotropy = 0.0;
    p.coatWeight = 0.0;
    p.coatColor = vec3(1.0);
    p.coatRoughness = 0.0;
    p.coatRoughnessAnisotropy = 0.0;
    p.coatIor = 1.6;
    p.coatDarkening = 1.0;
    p.fuzzWeight = 0.0;
    p.fuzzColor = vec3(1.0);
    p.fuzzRoughness = 0.5;
    p.emissionLuminance = 0.0;
    p.emissionColor = vec3(1.0);
    p.thinFilmWeight = 0.0;
    p.thinFilmThickness = 0.5;
    p.thinFilmIor = 1.4;
    p.geometryOpacity = 1.0;
    p.geometryThinWalled = false;
    p.geometryNormal = normal;
    p.geometryTangent = tangent;
    p.geometryCoatNormal = normal;
    p.geometryCoatTangent = tangent;
    p.occlusion = 1.0;
    return p;
}
