// The PBR model's debug views beside Graphite/Debug.glsl's: each lobe lit alone, the base specular albedo E_spec,
// the diffuse throughput, direct and indirect light apart, the furnace, the closure under a white environment with
// every light off, which shows 1 wherever the surface keeps its energy, the thin film's reflectance at the view and the
// coat's darkening factor. The base lobes show through the coat and fuzz above them, and the coat through the fuzz.
// SUBSURFACE shows the subsurface lobe alone, the sheet's transmission included, BACK only the light reaching a thin
// walled sheet from its far side. DIFFUSE leaves subsurface out. TRANSMISSION shows what a transmissive surface lets
// through from behind, THICKNESS the refracted path's length through its volume, both 0 without PBR_TRANSMISSION. The
// furnace sees a white background through transmissive surfaces.
//   DIFFUSE SPECULAR SPECULAR_ALBEDO THROUGHPUT DIRECT INDIRECT FURNACE COAT FUZZ THIN_FILM DARKENING SUBSURFACE BACK
//   TRANSMISSION THICKNESS
#pragma once

#include "Graphite/Debug.glsl"
#include "Graphite/PBR/Closure.glsl"

#ifdef GRAPHITE_FRAGMENT

vec3 pbr_DebugSubsurfaceAlbedo(PBR_Closure c)
{
#if defined(PBR_SHEET)
    return c.sheetReflection;
#elif defined(PBR_WRAP)
    return c.subsurfaceAlbedo;
#else
    return vec3(0.0);
#endif
}

void pbr_DebugNoDiffuse(inout PBR_Closure c)
{
    c.diffuseSingle = vec3(0.0);
    c.diffuseMultiple = vec3(0.0);
    c.diffuseAlbedo = pbr_DebugSubsurfaceAlbedo(c);
}

void pbr_DebugNoSubsurface(inout PBR_Closure c)
{
    c.diffuseAlbedo -= pbr_DebugSubsurfaceAlbedo(c);
#if defined(PBR_SHEET)
    c.sheetReflection = vec3(0.0);
#elif defined(PBR_WRAP)
    c.subsurfaceAlbedo = vec3(0.0);
#endif
}

void pbr_DebugNoBack(inout PBR_Closure c)
{
#ifdef PBR_SHEET
    c.sheetTransmission = vec3(0.0);
#endif
}

void pbr_DebugNoSpecular(inout PBR_Closure c)
{
    c.dielectricScale = 0.0;
    c.metalScale = vec3(0.0);
#ifdef PBR_FILM
    c.dielectricFilm = vec3(0.0);
    c.metalFilm = vec3(0.0);
#endif
    c.specularAlbedo = vec3(0.0);
}

void pbr_DebugNoCoat(inout PBR_Closure c)
{
#ifdef PBR_COAT
    c.coatScale = 0.0;
    c.coatAlbedo = 0.0;
#endif
}

void pbr_DebugNoFuzz(inout PBR_Closure c)
{
#ifdef PBR_FUZZ
    c.fuzzAlbedo = vec3(0.0);
#endif
}

// transmitted is what the surface lets through from behind, transmittedWhite the same of a white background
vec3 PBR_DebugView(vec3 color, PBR_Params p, PBR_Closure c, vec3 direct, vec3 ambient, PBR_Indirect indirect, vec2 lightmapUV, vec3 transmitted, vec3 transmittedWhite, float pathLength)
{
#if defined(GRAPHITE_DEBUG_DIFFUSE) || defined(GRAPHITE_DEBUG_SPECULAR) || defined(GRAPHITE_DEBUG_COAT) || defined(GRAPHITE_DEBUG_FUZZ) || defined(GRAPHITE_DEBUG_SUBSURFACE) || defined(GRAPHITE_DEBUG_BACK)
    PBR_Closure lobe = c;
#ifndef GRAPHITE_DEBUG_DIFFUSE
    pbr_DebugNoDiffuse(lobe);
#endif
#ifndef GRAPHITE_DEBUG_SPECULAR
    pbr_DebugNoSpecular(lobe);
#endif
#ifndef GRAPHITE_DEBUG_COAT
    pbr_DebugNoCoat(lobe);
#endif
#ifndef GRAPHITE_DEBUG_FUZZ
    pbr_DebugNoFuzz(lobe);
#endif
#ifndef GRAPHITE_DEBUG_SUBSURFACE
    pbr_DebugNoSubsurface(lobe);
#ifndef GRAPHITE_DEBUG_BACK
    pbr_DebugNoBack(lobe);
#endif
#endif
    return PBR_ShadeLights(lobe) + Graphite_AmbientBRDF(lobe, indirect);
#elif defined(GRAPHITE_DEBUG_SPECULAR_ALBEDO)
    return c.specularAlbedo;
#elif defined(GRAPHITE_DEBUG_THROUGHPUT)
    return vec3(c.diffuseWeight);
#elif defined(GRAPHITE_DEBUG_DIRECT)
    return direct;
#elif defined(GRAPHITE_DEBUG_INDIRECT)
    return ambient;
#elif defined(GRAPHITE_DEBUG_FURNACE)
    PBR_Indirect white;
    white.irradiance = vec3(1.0);
#ifdef PBR_SHEET
    white.irradianceBack = vec3(1.0);
#endif
    white.specular = vec3(1.0);
#ifdef PBR_COAT
    white.coat = vec3(1.0);
#endif
    return Graphite_AmbientBRDF(c, white) + transmittedWhite;
#elif defined(GRAPHITE_DEBUG_TRANSMISSION)
    return transmitted;
#elif defined(GRAPHITE_DEBUG_THICKNESS)
    return vec3(pathLength);
#elif defined(GRAPHITE_DEBUG_THIN_FILM)
#ifdef PBR_FILM
    return c.film;
#else
    return vec3(0.0);
#endif
#elif defined(GRAPHITE_DEBUG_DARKENING)
#ifdef PBR_COAT
    return c.darkening;
#else
    return vec3(1.0);
#endif
#else
    return Graphite_DebugView(color, p.baseWeight * p.baseColor, c.normal, c.roughness, c.metalness, c.occlusion, c.emission, c.position, lightmapUV);
#endif
}

#endif
