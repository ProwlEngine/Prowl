// Box projected decals, applied in the forward pass before lighting so their albedo, normals, roughness and metallic
// reach both the lighting and the thin G-buffer. Decals live in H-Forward trees like lights: a point query finds the
// ones whose box holds the surface, and up to eight are applied, lowest priority first. A custom surface shader adds its
// own channels by defining GRAPHITE_DECAL_EXTRA and a Graphite_ApplyDecalExtra function, called after each decal with its
// extra textures and host data.
#pragma once

#include "Graphite/Trees.glsl"
#include "Graphite/Textures.glsl"

#define GRAPHITE_MAX_DECALS 8

// Compiled in while the world has a decal
#pragma variant global GRAPHITE_DECALS

struct Graphite_DecalRecord
{
    vec4 row0;               // rows of the inverse of its linear part, its centre in its tree's space in w
    vec4 row1;
    vec4 row2;
    uvec4 order;             // the order it was added in
    vec4 color;              // tint, alpha scales coverage
    uvec4 textures;          // albedo, normal and mask slots in the 2D table, sampler slot
    vec4 params;             // normal strength, priority, angle fade cosine, layer mask as uint bits
    vec4 axis;               // decal +Z in world space, texture flags as uint bits (albedo 1, normal 2, mask 4)
    uvec4 extra;             // two more texture slots for custom surface shaders
    vec4 hostData;
};


#define GRAPHITE_DECAL_TEXTURE(d, slot, uv) GRAPHITE_SAMPLE_2D(slot, d.textures.w, uv)

Graphite_DecalRecord Graphite_DecalAt(uint array, uint index)
{
    uint w = index * 10u;
    Graphite_DecalRecord d;
    d.row0 = Graphite_SpatialVec4(array, w);
    d.row1 = Graphite_SpatialVec4(array, w + 1u);
    d.row2 = Graphite_SpatialVec4(array, w + 2u);
    d.order = Graphite_SpatialUVec4(array, w + 3u);
    d.color = Graphite_SpatialVec4(array, w + 4u);
    d.textures = Graphite_SpatialUVec4(array, w + 5u);
    d.params = Graphite_SpatialVec4(array, w + 6u);
    d.axis = Graphite_SpatialVec4(array, w + 7u);
    d.extra = Graphite_SpatialUVec4(array, w + 8u);
    d.hostData = Graphite_SpatialVec4(array, w + 9u);
    return d;
}

// The unit box position of a camera relative point. offset and offsetLow move camera relative positions into the decal's tree.
vec3 Graphite_DecalLocal(Graphite_DecalRecord d, vec3 relativePos, vec3 offset, vec3 offsetLow)
{
    precise vec3 center = (vec3(d.row0.w, d.row1.w, d.row2.w) - offset) - offsetLow;
    precise vec3 q = relativePos - center;
    return vec3(dot(d.row0.xyz, q), dot(d.row1.xyz, q), dot(d.row2.xyz, q));
}

// How much a decal covers a surface with this geometric normal at this local position, 0 outside it or on surfaces facing away
float Graphite_DecalCoverage(Graphite_DecalRecord d, vec3 local, vec3 geomNormal, uint layers)
{
    if (any(greaterThan(abs(local), vec3(0.5))))
        return 0.0;
    if ((floatBitsToUint(d.params.w) & layers) == 0u)
        return 0.0;

    float facing = dot(geomNormal, d.axis.xyz);
    float threshold = max(d.params.z, 0.0);
    if (facing <= threshold)
        return 0.0;
    float fade = threshold > 0.0 ? clamp((facing - threshold) / max(1.0 - threshold, 1e-4), 0.0, 1.0) : 1.0;
    return fade * d.color.a;
}

void Graphite_ApplyDecal(Graphite_DecalRecord d, vec3 local, float coverage,
                         inout vec3 albedo, inout vec3 normal, inout float roughness, inout float metallic, inout float occlusion)
{
    vec2 uv = local.xy + 0.5;
    uint flags = floatBitsToUint(d.axis.w);

    vec4 color = d.color;
    if ((flags & 1u) != 0u)
        color *= GRAPHITE_DECAL_TEXTURE(d, d.textures.x, uv);
    float alpha = coverage * color.a / max(d.color.a, 1e-4);
    albedo = mix(albedo, color.rgb, alpha);

    if ((flags & 2u) != 0u)
    {
        vec3 tangent;
        tangent.xy = GRAPHITE_DECAL_TEXTURE(d, d.textures.y, uv).xy * 2.0 - 1.0;
        tangent.z = sqrt(max(0.0, 1.0 - dot(tangent.xy, tangent.xy)));
        vec3 decalNormal = normalize(normalize(d.row0.xyz) * tangent.x + normalize(d.row1.xyz) * tangent.y + d.axis.xyz * tangent.z);
        normal = normalize(mix(normal, decalNormal, clamp(alpha * d.params.x, 0.0, 1.0)));
    }

    if ((flags & 4u) != 0u)
    {
        vec3 mask = GRAPHITE_DECAL_TEXTURE(d, d.textures.z, uv).rgb;
        occlusion = mix(occlusion, mask.r, alpha);
        roughness = mix(roughness, mask.g, alpha);
        metallic = mix(metallic, mask.b, alpha);
    }
}

#ifdef GRAPHITE_DECAL_EXTRA
// Applies a decal's own channels, its uv the decal's 0 to 1 square: sample GRAPHITE_DECAL_TEXTURE(d, d.extra.x, uv)
void Graphite_ApplyDecalExtra(Graphite_DecalRecord d, vec2 uv, float coverage);
#endif

// Whether a decal applies before another: lower priority first, then the one added first
bool Graphite_DecalBefore(float priority, uint sequence, float otherPriority, uint otherSequence)
{
    return priority < otherPriority || (priority == otherPriority && sequence < otherSequence);
}

// Applies every decal whose box holds relativePos, camera relative, to the surface. geomNormal is the normal before
// normal mapping, used to fade decals on surfaces turned away from them.
void Graphite_ApplyDecals(vec3 relativePos, vec3 geomNormal,
                          inout vec3 albedo, inout vec3 normal, inout float roughness, inout float metallic, inout float occlusion)
{
#ifndef GRAPHITE_DECALS
    return;
#endif
    uint layers = Graphite_SurfaceLayers();
    uint hitTree[GRAPHITE_MAX_DECALS];
    uint hitIndex[GRAPHITE_MAX_DECALS];
    float hitPriority[GRAPHITE_MAX_DECALS];
    uint hitSequence[GRAPHITE_MAX_DECALS];
    vec3 hitLocal[GRAPHITE_MAX_DECALS];
    float hitCoverage[GRAPHITE_MAX_DECALS];
    int hits = 0;

    Graphite_TreeQuery query;
    Graphite_BeginQuery(query, relativePos, GRAPHITE_SET_DECALS);
    uint tree, first, last;
    while (Graphite_NextRun(query, tree, first, last))
    {
        for (uint i = first; i < last; i++)
        {
            Graphite_DecalRecord d = Graphite_DecalAt(tree, i);
            vec3 local = Graphite_DecalLocal(d, relativePos, query.offset, query.offsetLow);
            float coverage = Graphite_DecalCoverage(d, local, geomNormal, layers);
            if (coverage <= 0.0)
                continue;

            // Keep the hits sorted by priority then the order they were added, dropping the lowest when full
            float priority = d.params.y;
            uint sequence = d.order.x;
            int at = hits;
            if (hits == GRAPHITE_MAX_DECALS)
            {
                if (Graphite_DecalBefore(priority, sequence, hitPriority[0], hitSequence[0]))
                    continue;
                for (int k = 0; k < hits - 1; k++)
                {
                    hitTree[k] = hitTree[k + 1];
                    hitIndex[k] = hitIndex[k + 1];
                    hitPriority[k] = hitPriority[k + 1];
                    hitSequence[k] = hitSequence[k + 1];
                    hitLocal[k] = hitLocal[k + 1];
                    hitCoverage[k] = hitCoverage[k + 1];
                }
                at = hits - 1;
            }
            else
            {
                hits++;
            }
            while (at > 0 && Graphite_DecalBefore(priority, sequence, hitPriority[at - 1], hitSequence[at - 1]))
            {
                hitTree[at] = hitTree[at - 1];
                hitIndex[at] = hitIndex[at - 1];
                hitPriority[at] = hitPriority[at - 1];
                hitSequence[at] = hitSequence[at - 1];
                hitLocal[at] = hitLocal[at - 1];
                hitCoverage[at] = hitCoverage[at - 1];
                at--;
            }
            hitTree[at] = tree;
            hitIndex[at] = i;
            hitPriority[at] = priority;
            hitSequence[at] = sequence;
            hitLocal[at] = local;
            hitCoverage[at] = coverage;
        }
    }

    for (int h = 0; h < hits; h++)
    {
        Graphite_DecalRecord d = Graphite_DecalAt(hitTree[h], hitIndex[h]);
        Graphite_ApplyDecal(d, hitLocal[h], hitCoverage[h], albedo, normal, roughness, metallic, occlusion);
#ifdef GRAPHITE_DECAL_EXTRA
        Graphite_ApplyDecalExtra(d, hitLocal[h].xy + 0.5, hitCoverage[h]);
#endif
    }
}
