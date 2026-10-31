// The PBR model's material: every input a factor times its map where the map is set, a map's channel picked for scalar
// inputs and RGB for colours, through its own UV set, scale, rotation and offset. Tangent space normal maps feed
// geometry_normal and geometry_coat_normal on the mesh frame, geometry_tangent_map then tangent_rotation turn
// geometry_tangent in the tangent plane, the latter counter clockwise from the tangent toward the bitangent. See Standard.glsl.
#pragma once

#include "Graphite/Standard/Standard.glsl"

#ifdef GRAPHITE_FRAGMENT

#ifdef GRAPHITE_STANDARD_LIT
    #include "Graphite/PBR/Closure.glsl"
    #include "Graphite/Decals.glsl"
    #ifdef PBR_TRANSMISSION
        #include "Graphite/PBR/Transmission.glsl"
    #endif
    #ifdef GRAPHITE_STANDARD_DEBUG
        #include "Graphite/PBR/Debug.glsl"
    #endif
#endif
#ifdef GRAPHITE_STANDARD_SHADOW_DITHER
    #include "Graphite/PBR/Shadow.glsl"
#endif
#include "Graphite/PBR/Params.glsl"
#include "Graphite/PBR/Microfacet.glsl"

uniform float base_weight;
uniform vec4 base_color;
uniform float base_metalness;
uniform float base_diffuse_roughness;
uniform float specular_weight;
uniform vec4 specular_color;
uniform float specular_roughness;
uniform float specular_roughness_anisotropy;
uniform float specular_ior;
uniform float transmission_weight;
uniform vec4 transmission_color;
uniform float transmission_depth;
uniform vec4 transmission_scatter;
uniform float transmission_scatter_anisotropy;
uniform float transmission_dispersion_scale;
uniform float transmission_dispersion_abbe_number;
uniform float subsurface_weight;
uniform vec4 subsurface_color;
uniform float subsurface_radius;
uniform vec4 subsurface_radius_scale;
uniform float subsurface_scatter_anisotropy;
uniform float coat_weight;
uniform vec4 coat_color;
uniform float coat_roughness;
uniform float coat_roughness_anisotropy;
uniform float coat_ior;
uniform float coat_darkening;
uniform float fuzz_weight;
uniform vec4 fuzz_color;
uniform float fuzz_roughness;
uniform float emission_luminance;
uniform vec4 emission_color;
uniform float thin_film_weight;
uniform float thin_film_thickness;
uniform float thin_film_ior;
uniform float geometry_opacity;
uniform int geometry_thin_walled;
uniform float ambient_occlusion;
uniform float normal_scale;
uniform float coat_normal_scale;
uniform float height_scale;
uniform float alpha_cutoff;
uniform float tangent_rotation;
uniform sampler2D base_weight_map;
uniform sampler2D base_color_map;
uniform sampler2D base_metalness_map;
uniform sampler2D base_diffuse_roughness_map;
uniform sampler2D specular_weight_map;
uniform sampler2D specular_color_map;
uniform sampler2D specular_roughness_map;
uniform sampler2D specular_roughness_anisotropy_map;
uniform sampler2D transmission_weight_map;
uniform sampler2D transmission_color_map;
uniform sampler2D subsurface_weight_map;
uniform sampler2D subsurface_color_map;
uniform sampler2D subsurface_radius_map;
uniform sampler2D coat_weight_map;
uniform sampler2D coat_color_map;
uniform sampler2D coat_roughness_map;
uniform sampler2D coat_roughness_anisotropy_map;
uniform sampler2D coat_darkening_map;
uniform sampler2D fuzz_weight_map;
uniform sampler2D fuzz_color_map;
uniform sampler2D fuzz_roughness_map;
uniform sampler2D emission_luminance_map;
uniform sampler2D emission_color_map;
uniform sampler2D thin_film_weight_map;
uniform sampler2D thin_film_thickness_map;
uniform sampler2D geometry_opacity_map;
uniform sampler2D ambient_occlusion_map;
uniform sampler2D geometry_normal_map;
uniform sampler2D geometry_coat_normal_map;
uniform sampler2D geometry_tangent_map;
uniform sampler2D geometry_height_map;

#ifdef PBR_PARALLAX

#if defined(PBR_TIER_LITE) && !defined(_SURFACE_CUTOUT)
    #define PBR_PARALLAX_OFFSET
#endif

// How UV1 changes with UV0 at this pixel, from their screen derivatives, zero where UV0 does not change
mat2 pbr_UV0ToUV1(Graphite_SurfaceData i)
{
    mat2 uv0 = mat2(dFdx(i.uv), dFdy(i.uv));
    mat2 uv1 = mat2(dFdx(i.uv1), dFdy(i.uv1));
    float det = determinant(uv0);
    return abs(det) > 1e-20 ? uv1 * inverse(uv0) : mat2(0.0);
}

float pbr_Height(vec2 uv, vec2 dx, vec2 dy)
{
    return textureGrad(geometry_height_map, uv, dx, dy)[geometry_height_map_Channel];
}

// Moves both UV sets to where the view ray, entering at the surface, meets the height field of geometry_height_map:
// white at the surface, black height_scale below it in the map's own texture units, so the relief keeps its shape
// however the map tiles. toViewer points back along the view ray. FULL views and every depth pass march the ray,
// parallax occlusion mapping refined by the secant method. LITE views take one offset from the height under the pixel,
// limited by the view's slope, except for cutouts, whose alpha test must match their depth passes. The caller checks
// the mesh has tangents.
void PBR_Parallax(inout Graphite_SurfaceData i, vec3 toViewer)
{
    mat2 toUV1 = pbr_UV0ToUV1(i);
    vec2 uv = Graphite_StandardUV(geometry_height_map_UV, geometry_height_map_ST, geometry_height_map_Rotation, i);
    vec2 dx = dFdx(uv);
    vec2 dy = dFdy(uv);
    vec3 v = vec3(dot(toViewer, i.tangent), dot(toViewer, i.bitangent), dot(toViewer, i.geometryNormal));
    if (!geometry_height_map_Present || v.z < 1e-3)
        return;

    // The ray's drift over the tangent plane per unit of depth, as UV0, then into the map's texture space through its UV set, scale and rotation
    vec2 drift = -v.xy / v.z;
    vec2 scaled = (geometry_height_map_UV == 0 ? drift : toUV1 * drift) * geometry_height_map_ST.xy;
    vec2 rotation = geometry_height_map_Rotation;
    vec2 inMap = vec2(rotation.x * scaled.x - rotation.y * scaled.y, rotation.y * scaled.x + rotation.x * scaled.y);
    float texels = length(inMap);
    if (texels < 1e-8)
        return;
    float perDepth = height_scale * length(drift) / texels;
    vec2 mapStep = inMap * perDepth;

#ifdef PBR_PARALLAX_OFFSET
    float depth = (1.0 - pbr_Height(uv, dx, dy)) * v.z;
#else
    // Depths along the ray bracketing where it passes below the surface, gap being the surface's depth minus the ray's
    float above = 0.0;
    float gapAbove = 1.0 - pbr_Height(uv, dx, dy);
    float below = 0.0;
    float gapBelow = gapAbove;
    if (gapAbove > 0.0)
    {
        int steps = int(mix(32.0, 8.0, v.z));
        float layer = 1.0 / float(steps);
        for (int s = 1; s <= steps; s++)
        {
            below = float(s) * layer;
            gapBelow = 1.0 - pbr_Height(uv + mapStep * below, dx, dy) - below;
            if (gapBelow <= 0.0)
                break;
            above = below;
            gapAbove = gapBelow;
        }
        for (int r = 0; r < 3; r++)
        {
            float across = gapAbove - gapBelow;
            if (!(across > 1e-6))
                break;
            float middle = above + (below - above) * gapAbove / across;
            float gap = 1.0 - pbr_Height(uv + mapStep * middle, dx, dy) - middle;
            if (gap > 0.0)
            {
                above = middle;
                gapAbove = gap;
            }
            else
            {
                below = middle;
                gapBelow = gap;
            }
        }
    }
    float across = gapAbove - gapBelow;
    float depth = across > 1e-6 ? above + (below - above) * gapAbove / across : below;
#endif
    vec2 uv0Step = drift * perDepth;
    i.uv += uv0Step * depth;
    i.uv1 += toUV1 * uv0Step * depth;
}

#endif

// The material's inputs at a point, the vertex colour multiplying the base colour and opacity. mapVariance is the
// normal map's own variance, Toksvig's from a three channel texel, for specular antialiasing. Normal and tangent maps
// need the mesh's tangents. A lobe's maps are read only while its axis is on, the thin film's and anisotropy's only at
// the FULL tier, as the closure reads nothing of a lobe that is off.
PBR_Params PBR_MaterialParams(Graphite_SurfaceData i, bool hasTangents, out float mapVariance)
{
    PBR_Params p = PBR_DefaultParams(i.geometryNormal, i.tangent);
    p.baseWeight = base_weight;
    p.baseColor = base_color.rgb;
    p.baseMetalness = base_metalness;
    p.baseDiffuseRoughness = base_diffuse_roughness;
    p.specularWeight = specular_weight;
    p.specularColor = specular_color.rgb;
    p.specularRoughness = specular_roughness;
    p.specularRoughnessAnisotropy = specular_roughness_anisotropy;
    p.specularIor = specular_ior;
    p.transmissionWeight = transmission_weight;
    p.transmissionColor = transmission_color.rgb;
    p.transmissionDepth = transmission_depth;
    p.transmissionScatter = transmission_scatter.rgb;
    p.transmissionScatterAnisotropy = transmission_scatter_anisotropy;
    p.transmissionDispersionScale = transmission_dispersion_scale;
    p.transmissionDispersionAbbeNumber = transmission_dispersion_abbe_number;
    p.subsurfaceWeight = subsurface_weight;
    p.subsurfaceColor = subsurface_color.rgb;
    p.subsurfaceRadius = subsurface_radius;
    p.subsurfaceRadiusScale = subsurface_radius_scale.rgb;
    p.subsurfaceScatterAnisotropy = subsurface_scatter_anisotropy;
    p.coatWeight = coat_weight;
    p.coatColor = coat_color.rgb;
    p.coatRoughness = coat_roughness;
    p.coatRoughnessAnisotropy = coat_roughness_anisotropy;
    p.coatIor = coat_ior;
    p.coatDarkening = coat_darkening;
    p.fuzzWeight = fuzz_weight;
    p.fuzzColor = fuzz_color.rgb;
    p.fuzzRoughness = fuzz_roughness;
    p.emissionLuminance = emission_luminance;
    p.emissionColor = emission_color.rgb;
    p.thinFilmWeight = thin_film_weight;
    p.thinFilmThickness = thin_film_thickness;
    p.thinFilmIor = thin_film_ior;
    p.geometryOpacity = geometry_opacity;
    p.occlusion = ambient_occlusion;
    p.geometryThinWalled = geometry_thin_walled != 0;

    if (base_weight_map_Present)
        p.baseWeight *= texture(base_weight_map, Graphite_StandardUV(base_weight_map_UV, base_weight_map_ST, base_weight_map_Rotation, i))[base_weight_map_Channel];
    if (base_color_map_Present)
        p.baseColor *= texture(base_color_map, Graphite_StandardUV(base_color_map_UV, base_color_map_ST, base_color_map_Rotation, i)).rgb;
    if (base_metalness_map_Present)
        p.baseMetalness *= texture(base_metalness_map, Graphite_StandardUV(base_metalness_map_UV, base_metalness_map_ST, base_metalness_map_Rotation, i))[base_metalness_map_Channel];
    if (base_diffuse_roughness_map_Present)
        p.baseDiffuseRoughness *= texture(base_diffuse_roughness_map, Graphite_StandardUV(base_diffuse_roughness_map_UV, base_diffuse_roughness_map_ST, base_diffuse_roughness_map_Rotation, i))[base_diffuse_roughness_map_Channel];
    if (specular_weight_map_Present)
        p.specularWeight *= texture(specular_weight_map, Graphite_StandardUV(specular_weight_map_UV, specular_weight_map_ST, specular_weight_map_Rotation, i))[specular_weight_map_Channel];
    if (specular_color_map_Present)
        p.specularColor *= texture(specular_color_map, Graphite_StandardUV(specular_color_map_UV, specular_color_map_ST, specular_color_map_Rotation, i)).rgb;
    if (specular_roughness_map_Present)
        p.specularRoughness *= texture(specular_roughness_map, Graphite_StandardUV(specular_roughness_map_UV, specular_roughness_map_ST, specular_roughness_map_Rotation, i))[specular_roughness_map_Channel];
#if defined(PBR_ANISOTROPY) && !defined(PBR_TIER_LITE)
    if (specular_roughness_anisotropy_map_Present)
        p.specularRoughnessAnisotropy *= texture(specular_roughness_anisotropy_map, Graphite_StandardUV(specular_roughness_anisotropy_map_UV, specular_roughness_anisotropy_map_ST, specular_roughness_anisotropy_map_Rotation, i))[specular_roughness_anisotropy_map_Channel];
#endif
#ifdef PBR_TRANSMISSION
    if (transmission_weight_map_Present)
        p.transmissionWeight *= texture(transmission_weight_map, Graphite_StandardUV(transmission_weight_map_UV, transmission_weight_map_ST, transmission_weight_map_Rotation, i))[transmission_weight_map_Channel];
    if (transmission_color_map_Present)
        p.transmissionColor *= texture(transmission_color_map, Graphite_StandardUV(transmission_color_map_UV, transmission_color_map_ST, transmission_color_map_Rotation, i)).rgb;
#endif
#ifdef PBR_SUBSURFACE
    if (subsurface_weight_map_Present)
        p.subsurfaceWeight *= texture(subsurface_weight_map, Graphite_StandardUV(subsurface_weight_map_UV, subsurface_weight_map_ST, subsurface_weight_map_Rotation, i))[subsurface_weight_map_Channel];
    if (subsurface_color_map_Present)
        p.subsurfaceColor *= texture(subsurface_color_map, Graphite_StandardUV(subsurface_color_map_UV, subsurface_color_map_ST, subsurface_color_map_Rotation, i)).rgb;
    if (subsurface_radius_map_Present)
        p.subsurfaceRadius *= texture(subsurface_radius_map, Graphite_StandardUV(subsurface_radius_map_UV, subsurface_radius_map_ST, subsurface_radius_map_Rotation, i))[subsurface_radius_map_Channel];
#endif
#ifdef PBR_COAT
    if (coat_weight_map_Present)
        p.coatWeight *= texture(coat_weight_map, Graphite_StandardUV(coat_weight_map_UV, coat_weight_map_ST, coat_weight_map_Rotation, i))[coat_weight_map_Channel];
    if (coat_color_map_Present)
        p.coatColor *= texture(coat_color_map, Graphite_StandardUV(coat_color_map_UV, coat_color_map_ST, coat_color_map_Rotation, i)).rgb;
    if (coat_roughness_map_Present)
        p.coatRoughness *= texture(coat_roughness_map, Graphite_StandardUV(coat_roughness_map_UV, coat_roughness_map_ST, coat_roughness_map_Rotation, i))[coat_roughness_map_Channel];
    if (coat_darkening_map_Present)
        p.coatDarkening *= texture(coat_darkening_map, Graphite_StandardUV(coat_darkening_map_UV, coat_darkening_map_ST, coat_darkening_map_Rotation, i))[coat_darkening_map_Channel];
#if defined(PBR_ANISOTROPY) && !defined(PBR_TIER_LITE)
    if (coat_roughness_anisotropy_map_Present)
        p.coatRoughnessAnisotropy *= texture(coat_roughness_anisotropy_map, Graphite_StandardUV(coat_roughness_anisotropy_map_UV, coat_roughness_anisotropy_map_ST, coat_roughness_anisotropy_map_Rotation, i))[coat_roughness_anisotropy_map_Channel];
#endif
#endif
#ifdef PBR_FUZZ
    if (fuzz_weight_map_Present)
        p.fuzzWeight *= texture(fuzz_weight_map, Graphite_StandardUV(fuzz_weight_map_UV, fuzz_weight_map_ST, fuzz_weight_map_Rotation, i))[fuzz_weight_map_Channel];
    if (fuzz_color_map_Present)
        p.fuzzColor *= texture(fuzz_color_map, Graphite_StandardUV(fuzz_color_map_UV, fuzz_color_map_ST, fuzz_color_map_Rotation, i)).rgb;
    if (fuzz_roughness_map_Present)
        p.fuzzRoughness *= texture(fuzz_roughness_map, Graphite_StandardUV(fuzz_roughness_map_UV, fuzz_roughness_map_ST, fuzz_roughness_map_Rotation, i))[fuzz_roughness_map_Channel];
#endif
    if (emission_luminance_map_Present)
        p.emissionLuminance *= texture(emission_luminance_map, Graphite_StandardUV(emission_luminance_map_UV, emission_luminance_map_ST, emission_luminance_map_Rotation, i))[emission_luminance_map_Channel];
    if (emission_color_map_Present)
        p.emissionColor *= texture(emission_color_map, Graphite_StandardUV(emission_color_map_UV, emission_color_map_ST, emission_color_map_Rotation, i)).rgb;
#if defined(PBR_THIN_FILM) && !defined(PBR_TIER_LITE)
    if (thin_film_weight_map_Present)
        p.thinFilmWeight *= texture(thin_film_weight_map, Graphite_StandardUV(thin_film_weight_map_UV, thin_film_weight_map_ST, thin_film_weight_map_Rotation, i))[thin_film_weight_map_Channel];
    if (thin_film_thickness_map_Present)
        p.thinFilmThickness *= texture(thin_film_thickness_map, Graphite_StandardUV(thin_film_thickness_map_UV, thin_film_thickness_map_ST, thin_film_thickness_map_Rotation, i))[thin_film_thickness_map_Channel];
#endif
    if (geometry_opacity_map_Present)
        p.geometryOpacity *= texture(geometry_opacity_map, Graphite_StandardUV(geometry_opacity_map_UV, geometry_opacity_map_ST, geometry_opacity_map_Rotation, i))[geometry_opacity_map_Channel];
    if (ambient_occlusion_map_Present)
        p.occlusion *= texture(ambient_occlusion_map, Graphite_StandardUV(ambient_occlusion_map_UV, ambient_occlusion_map_ST, ambient_occlusion_map_Rotation, i))[ambient_occlusion_map_Channel];

    p.baseColor *= i.vertexColor.rgb;
    p.geometryOpacity *= i.vertexColor.a;

    mapVariance = 0.0;
    if (!hasTangents)
        return p;
    if (geometry_normal_map_Present)
    {
        vec3 texel = texture(geometry_normal_map, Graphite_StandardUV(geometry_normal_map_UV, geometry_normal_map_ST, geometry_normal_map_Rotation, i)).rgb;
        mapVariance = PBR_ToksvigVariance(length(texel * 2.0 - 1.0));
        p.geometryNormal = Graphite_SurfaceNormalMap(i, texel, normal_scale);
    }
#ifdef PBR_COAT
    if (geometry_coat_normal_map_Present)
        p.geometryCoatNormal = Graphite_SurfaceNormalMap(i, texture(geometry_coat_normal_map, Graphite_StandardUV(geometry_coat_normal_map_UV, geometry_coat_normal_map_ST, geometry_coat_normal_map_Rotation, i)).rgb, coat_normal_scale);
#endif
#if defined(PBR_ANISOTROPY) && !defined(PBR_TIER_LITE)
    vec2 direction = vec2(1.0, 0.0);
    if (geometry_tangent_map_Present)
    {
        vec2 mapped = texture(geometry_tangent_map, Graphite_StandardUV(geometry_tangent_map_UV, geometry_tangent_map_ST, geometry_tangent_map_Rotation, i)).rg * 2.0 - 1.0;
        if (dot(mapped, mapped) > 1e-4)
            direction = mapped;
    }
    if (tangent_rotation != 0.0)
    {
        vec2 turn = vec2(cos(tangent_rotation), sin(tangent_rotation));
        direction = vec2(turn.x * direction.x - turn.y * direction.y, turn.y * direction.x + turn.x * direction.y);
    }
    if (geometry_tangent_map_Present || tangent_rotation != 0.0)
        p.geometryTangent = normalize(i.tangent * direction.x + i.bitangent * direction.y);
#endif
    return p;
}

#endif
