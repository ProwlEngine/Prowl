// Box projected decals, applied in the forward pass before lighting so their albedo, normals, roughness and metallic
// reach both the lighting and the thin G-buffer. Decals live in H-Forward trees like lights: a point query finds the
// ones whose box holds the surface, and up to eight are applied, lowest priority first.
#pragma once

#extension GL_EXT_nonuniform_qualifier : require

#include "Graphite/Trees.glsl"

#define GRAPHITE_MAX_DECALS 8

struct Graphite_DecalRecord
{
    vec4 row0;               // rows mapping its tree's space to the unit box
    vec4 row1;
    vec4 row2;
    vec4 color;              // tint, alpha scales coverage
    uvec4 textures;          // albedo, normal and mask slots in the 2D table, sampler slot
    vec4 params;             // normal strength, priority, angle fade cosine, layer mask as uint bits
    vec4 axis;               // decal +Z in world space, texture flags as uint bits (albedo 1, normal 2, mask 4)
};

layout(set = 0, binding = 13, std430) readonly buffer Graphite_StaticDecals { Graphite_DecalRecord graphite_StaticDecals[]; };
layout(set = 1, binding = 5, std430) readonly buffer Graphite_ViewDecals { Graphite_DecalRecord graphite_ViewDecals[]; };

layout(set = 0, binding = 0) uniform texture2D Graphite_Textures2D[16384];
layout(set = 1, binding = 0) uniform sampler Graphite_Samplers[64];

// Samplers cannot live in locals, so the table is indexed at each read
#define GRAPHITE_DECAL_TEXTURE(d, slot, uv) texture(sampler2D(Graphite_Textures2D[nonuniformEXT(slot)], Graphite_Samplers[nonuniformEXT(d.textures.w)]), uv)

Graphite_DecalRecord Graphite_DecalAt(uint tree, uint index)
{
    return tree == GRAPHITE_STATIC_DECALS ? graphite_StaticDecals[index] : graphite_ViewDecals[index];
}

vec3 Graphite_DecalLocal(Graphite_DecalRecord d, vec3 point)
{
    vec4 p = vec4(point, 1.0);
    return vec3(dot(d.row0, p), dot(d.row1, p), dot(d.row2, p));
}

// How much a decal covers a surface with this geometric normal at this local position, 0 outside it
float Graphite_DecalCoverage(Graphite_DecalRecord d, vec3 local, vec3 geomNormal, uint layers)
{
    if (any(greaterThan(abs(local), vec3(0.5))))
        return 0.0;
    if ((floatBitsToUint(d.params.w) & layers) == 0u)
        return 0.0;

    float fade = 1.0;
    if (d.params.z > 0.0)
    {
        float facing = dot(geomNormal, d.axis.xyz);
        if (facing <= d.params.z)
            return 0.0;
        fade = clamp((facing - d.params.z) / max(1.0 - d.params.z, 1e-4), 0.0, 1.0);
    }
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
        vec3 tangent = GRAPHITE_DECAL_TEXTURE(d, d.textures.y, uv).xyz * 2.0 - 1.0;
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

// Applies every decal whose box holds worldPos, camera relative, to the surface. geomNormal is the normal before
// normal mapping, used to fade decals on surfaces turned away from them.
void Graphite_ApplyDecals(vec3 worldPos, vec3 geomNormal,
                          inout vec3 albedo, inout vec3 normal, inout float roughness, inout float metallic, inout float occlusion)
{
    Graphite_ViewData view = Graphite_View();
    uint layers = Graphite_SurfaceLayers();
    uint hitTree[GRAPHITE_MAX_DECALS];
    uint hitIndex[GRAPHITE_MAX_DECALS];
    float hitPriority[GRAPHITE_MAX_DECALS];
    int hits = 0;

    for (uint pass = 0u; pass < 2u; pass++)
    {
        uint tree = pass == 0u ? GRAPHITE_STATIC_DECALS : GRAPHITE_VIEW_DECALS;
        uint root = pass == 0u ? view.treeRoots.z : view.treeRoots.w;
        if (root == GRAPHITE_TREE_INVALID)
            continue;

        vec3 point = pass == 0u ? worldPos + view.staticDecalOffset.xyz : worldPos;
        Graphite_TreeWalk walk;
        Graphite_BeginTree(walk, tree, root);
        uint first, last;
        while (Graphite_NextLeaf(walk, point, first, last))
        {
            for (uint i = first; i < last; i++)
            {
                Graphite_DecalRecord d = Graphite_DecalAt(tree, i);
                if (Graphite_DecalCoverage(d, Graphite_DecalLocal(d, point), geomNormal, layers) <= 0.0)
                    continue;

                // Keep the hits sorted by priority, dropping the lowest when full
                float priority = d.params.y;
                int at = hits;
                if (hits == GRAPHITE_MAX_DECALS)
                {
                    if (priority <= hitPriority[0])
                        continue;
                    for (int k = 0; k < hits - 1; k++)
                    {
                        hitTree[k] = hitTree[k + 1];
                        hitIndex[k] = hitIndex[k + 1];
                        hitPriority[k] = hitPriority[k + 1];
                    }
                    at = hits - 1;
                }
                else
                {
                    hits++;
                }
                while (at > 0 && hitPriority[at - 1] > priority)
                {
                    hitTree[at] = hitTree[at - 1];
                    hitIndex[at] = hitIndex[at - 1];
                    hitPriority[at] = hitPriority[at - 1];
                    at--;
                }
                hitTree[at] = tree;
                hitIndex[at] = i;
                hitPriority[at] = priority;
            }
        }
    }

    for (int h = 0; h < hits; h++)
    {
        Graphite_DecalRecord d = Graphite_DecalAt(hitTree[h], hitIndex[h]);
        vec3 point = hitTree[h] == GRAPHITE_STATIC_DECALS ? worldPos + view.staticDecalOffset.xyz : worldPos;
        vec3 local = Graphite_DecalLocal(d, point);
        Graphite_ApplyDecal(d, local, Graphite_DecalCoverage(d, local, geomNormal, layers), albedo, normal, roughness, metallic, occlusion);
    }
}
