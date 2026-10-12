// Debug views: a forward pass hands its surface to Graphite_DebugView, which returns what the view's GRAPHITE_DEBUG
// value shows in place of the lit colour, and calls Graphite_DebugInputs before lighting so the LIGHTING view shades a
// white surface. A view picks the value through ViewDesc.Keywords, OFF draws as usual and compiles both hooks away.
// World positions are camera relative, normals in world space.
#pragma once

#pragma variant global GRAPHITE_DEBUG OFF UNLIT ALBEDO NORMALS ROUGHNESS METALLIC AO EMISSION LIGHTING CASCADES LIGHTMAP_DENSITY

#include "Graphite/Lighting.glsl"

#ifdef GRAPHITE_FRAGMENT

// White and non metallic for the LIGHTING view, so the result is the light alone
void Graphite_DebugInputs(inout vec3 albedo, inout float metallic)
{
#ifdef GRAPHITE_DEBUG_LIGHTING
    albedo = vec3(1.0);
    metallic = 0.0;
#endif
}

#ifdef GRAPHITE_DEBUG_CASCADES
// The sun's cascade holding a position, -1 for none, picked as its shadow picks it
int Graphite_DebugCascade(vec3 position)
{
    if (Graphite_DirectionalCount() == 0u)
        return -1;
    Graphite_Cascade head = graphite_Cascades[0];
    uint count = uint(head.params.z);
    if (count == 0u || dot(position, head.fade.xyz) > head.fade.w)
        return -1;
    for (uint c = 0u; c < count; c++)
    {
        if (Graphite_InCascade(position, c, 3.0))
            return int(c);
    }
    for (uint c = 0u; c < count; c++)
    {
        if (Graphite_InCascade(position, c, 0.0))
            return int(c);
    }
    return -1;
}
#endif

#ifdef GRAPHITE_DEBUG_LIGHTMAP_DENSITY
// A checker over the object's lightmap texels, blue where it has no lightmap and is lit by probes
vec3 Graphite_DebugLightmapDensity(vec3 albedo, vec2 lightmapUV)
{
    vec3 grey = vec3(dot(albedo, vec3(0.2126, 0.7152, 0.0722)) * 0.5 + 0.25);
#ifndef GRAPHITE_NO_OBJECT
    Graphite_ObjectData object = Graphite_Object();
    uint slot = object.lightmapTexture;
    if (slot != GRAPHITE_NO_LIGHTMAP && slot != GRAPHITE_LIGHTMAP_LOADING)
    {
        ivec2 size = textureSize(sampler2D(Graphite_Textures2D[nonuniformEXT(slot)], Graphite_Samplers[nonuniformEXT(object.lightmapSampler & 0xFFFFu)]), 0);
        vec2 texel = floor(lightmapUV * vec2(size));
        float check = mod(texel.x + texel.y, 2.0);
        return grey * mix(vec3(1.0, 0.55, 0.2), vec3(0.2, 0.75, 0.3), check);
    }
#endif
    return grey * vec3(0.3, 0.45, 1.0);
}
#endif

vec3 Graphite_DebugView(vec3 color, vec3 albedo, vec3 normal, float roughness, float metallic, float ao, vec3 emission, vec3 position, vec2 lightmapUV)
{
#if defined(GRAPHITE_DEBUG_UNLIT)
    return albedo + emission;
#elif defined(GRAPHITE_DEBUG_ALBEDO)
    return albedo;
#elif defined(GRAPHITE_DEBUG_NORMALS)
    return dot(normal, normal) > 0.0 ? normalize(normal) * 0.5 + 0.5 : vec3(0.5);
#elif defined(GRAPHITE_DEBUG_ROUGHNESS)
    return vec3(roughness);
#elif defined(GRAPHITE_DEBUG_METALLIC)
    return vec3(metallic);
#elif defined(GRAPHITE_DEBUG_AO)
    return vec3(ao);
#elif defined(GRAPHITE_DEBUG_EMISSION)
    return emission;
#elif defined(GRAPHITE_DEBUG_CASCADES)
    const vec3 tints[4] = vec3[4](vec3(1.0, 0.3, 0.3), vec3(0.3, 1.0, 0.3), vec3(0.3, 0.5, 1.0), vec3(1.0, 1.0, 0.3));
    int cascade = Graphite_DebugCascade(position);
    vec3 grey = vec3(dot(albedo, vec3(0.2126, 0.7152, 0.0722)) * 0.5 + 0.25);
    return cascade < 0 ? grey * 0.3 : grey * tints[min(cascade, 3)];
#elif defined(GRAPHITE_DEBUG_LIGHTMAP_DENSITY)
    return Graphite_DebugLightmapDensity(albedo, lightmapUV);
#else
    return color;
#endif
}

#endif
