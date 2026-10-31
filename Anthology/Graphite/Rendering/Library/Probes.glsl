// Reflection probes and the sky probe, and the occlusion reflections take. Positions are relative.
#pragma once

#include "Graphite/Core.glsl"
#include "Graphite/Trees.glsl"
#include "Graphite/Textures.glsl"
#include "Graphite/WorldLighting.glsl"

// Lagarde's specular occlusion of a lobe of GGX alpha from ambient occlusion
float Graphite_SpecularOcclusion(float NdotV, float ao, float alpha)
{
    return clamp(pow(NdotV + ao, exp2(-16.0 * alpha - 1.0)) - 1.0 + ao, 0.0, 1.0);
}

// How much of a reflection along R stays above the geometric surface, so normal maps never reflect from below it
float Graphite_HorizonOcclusion(vec3 R, vec3 geomNormal)
{
    float horizon = min(1.0 + dot(R, geomNormal), 1.0);
    return horizon * horizon;
}

// The sky and every probe have their own cube prefiltered by roughness across its mips, found by its bindless slot.
// Captured probes live in regions like static lights, walked with the same trees.
struct Graphite_ReflectionProbe
{
    vec4 row0;               // the box's axes, its centre in its region's space in w
    vec4 row1;
    vec4 row2;
    vec4 box;                // box half size, blend distance
    vec4 params;             // intensity over the capture's exposure, box projection, priority, the cube's slot and mips (slot | mips << 16) as uint bits
};

Graphite_ReflectionProbe Graphite_ProbeAt(uint array, uint index)
{
    uint w = index * 5u;
    Graphite_ReflectionProbe p;
    p.row0 = Graphite_SpatialVec4(array, w);
    p.row1 = Graphite_SpatialVec4(array, w + 1u);
    p.row2 = Graphite_SpatialVec4(array, w + 2u);
    p.box = Graphite_SpatialVec4(array, w + 3u);
    p.params = Graphite_SpatialVec4(array, w + 4u);
    return p;
}

#ifndef GRAPHITE_MAX_BLENDED_PROBES
#define GRAPHITE_MAX_BLENDED_PROBES 4
#endif

bool Graphite_ReflectionProbesReady() { return graphite_World.reflections.w != 0u; }

// A prefiltered cube's texels along dir, its mip picked by roughness, as they were captured
vec3 Graphite_SampleProbeCube(uint slot, uint mips, vec3 dir, float roughness)
{
    return GRAPHITE_SAMPLE_CUBE_LOD(slot, graphite_World.reflections.y, dir, roughness * (float(mips) - 1.0)).rgb;
}

// The sky probe's light along dir, pre-exposed
vec3 Graphite_SampleSkyProbe(vec3 dir, float roughness)
{
    return Graphite_SampleProbeCube(graphite_World.reflections.x, graphite_World.reflections.z, dir, roughness) * (graphite_World.skyScale.x * Graphite_Exposure());
}

// A camera relative point in the probe's box space. offset and offsetLow move camera relative positions into its region.
vec3 Graphite_ProbeLocal(Graphite_ReflectionProbe probe, vec3 relativePos, vec3 offset, vec3 offsetLow)
{
    precise vec3 center = (vec3(probe.row0.w, probe.row1.w, probe.row2.w) - offset) - offsetLow;
    precise vec3 q = relativePos - center;
    return vec3(dot(probe.row0.xyz, q), dot(probe.row1.xyz, q), dot(probe.row2.xyz, q));
}

// Projects the reflection onto the probe's box, so it lands where the reflected surface really is. p is the point in box space.
vec3 Graphite_SampleReflectionProbe(Graphite_ReflectionProbe probe, vec3 p, vec3 dir, float roughness)
{
    uint cube = floatBitsToUint(probe.params.w);
    if (probe.params.y > 0.5)
    {
        vec3 halfSize = probe.box.xyz;
        vec3 d = vec3(dot(probe.row0.xyz, dir), dot(probe.row1.xyz, dir), dot(probe.row2.xyz, dir));
        d = mix(vec3(1e-6), d, greaterThanEqual(abs(d), vec3(1e-6)));
        vec3 far = max((halfSize - p) / d, (-halfSize - p) / d);
        float t = max(min(min(far.x, far.y), far.z), 0.0);
        vec3 hit = p + d * t;

        // The lobe's footprint shrinks with the distance to what it reflects, so close surfaces reflect sharper
        roughness *= sqrt(clamp(t / max(length(hit), 1e-4), 0.0, 1.0));
        dir = probe.row0.xyz * hit.x + probe.row1.xyz * hit.y + probe.row2.xyz * hit.z;
    }

    return Graphite_SampleProbeCube(cube & 0xFFFFu, cube >> 16, dir, roughness) * (probe.params.x * Graphite_Exposure());
}

// Walks the reflection probes whose box holds the point, giving each with the point in its box space
struct Graphite_ProbeIterator
{
    Graphite_TreeQuery query;
    uint tree;
    uint k;
    uint last;
};

Graphite_ProbeIterator Graphite_BeginProbes(vec3 relativePos)
{
    Graphite_ProbeIterator it;
    Graphite_BeginQuery(it.query, relativePos, GRAPHITE_SET_PROBES);
    it.k = 0u;
    it.last = 0u;
    return it;
}

bool Graphite_NextProbe(inout Graphite_ProbeIterator it, out Graphite_ReflectionProbe probe, out vec3 local)
{
    for (;;)
    {
        while (it.k < it.last)
        {
            probe = Graphite_ProbeAt(it.tree, it.k++);
            local = Graphite_ProbeLocal(probe, it.query.position, it.query.offset, it.query.offsetLow);
            vec3 inside = probe.box.xyz - abs(local);
            if (min(inside.x, min(inside.y, inside.z)) > 0.0)
                return true;
        }
        if (!Graphite_NextRun(it.query, it.tree, it.k, it.last))
            return false;
    }
    return false;
}

// The most important probes around a point, each kept as its record's place, the point in its box space and its share of
// the blend, the sky taking what is left. Found once, then sampled by every lobe.
struct Graphite_ProbeSet
{
    uvec2 probe[GRAPHITE_MAX_BLENDED_PROBES];
    vec3  local[GRAPHITE_MAX_BLENDED_PROBES];
    float weight[GRAPHITE_MAX_BLENDED_PROBES];
    int   count;
    float sky;
};

// Keeps the highest priority probes holding the point, weighted by how far inside their boxes it is
Graphite_ProbeSet Graphite_FindProbes(vec3 relativePos)
{
    Graphite_ProbeSet set;
    float priority[GRAPHITE_MAX_BLENDED_PROBES];
    int count = 0;

    Graphite_ProbeIterator it = Graphite_BeginProbes(relativePos);
    Graphite_ReflectionProbe probe;
    vec3 p;
    while (Graphite_NextProbe(it, probe, p))
    {
        vec3 inside3 = probe.box.xyz - abs(p);
        float inside = min(inside3.x, min(inside3.y, inside3.z));
        float w = probe.box.w > 0.0 ? clamp(inside / probe.box.w, 0.0, 1.0) : 1.0;
        float pr = probe.params.z;
        int at;
        if (count < GRAPHITE_MAX_BLENDED_PROBES)
            at = count++;
        else if (pr > priority[GRAPHITE_MAX_BLENDED_PROBES - 1])
            at = GRAPHITE_MAX_BLENDED_PROBES - 1;
        else
            continue;

        set.probe[at] = uvec2(it.tree, it.k - 1u);
        set.local[at] = p;
        set.weight[at] = w;
        priority[at] = pr;
        for (int j = at; j > 0 && priority[j] > priority[j - 1]; j--)
        {
            uvec2 tk = set.probe[j]; set.probe[j] = set.probe[j - 1]; set.probe[j - 1] = tk;
            vec3 tl = set.local[j]; set.local[j] = set.local[j - 1]; set.local[j - 1] = tl;
            float tw = set.weight[j]; set.weight[j] = set.weight[j - 1]; set.weight[j - 1] = tw;
            float tp = priority[j]; priority[j] = priority[j - 1]; priority[j - 1] = tp;
        }
    }

    // Each probe's share of what the ones above it left
    float total = 0.0;
    set.count = 0;
    for (int i = 0; i < count && total < 0.999; i++)
    {
        set.weight[i] *= 1.0 - total;
        total += set.weight[i];
        set.count++;
    }
    set.sky = total < 0.999 ? 1.0 - total : 0.0;
    return set;
}

// The direction a lobe of GGX alpha around N samples probes along, rough lobes leaning toward the normal
vec3 Graphite_ProbeDirection(vec3 N, vec3 V, float alpha)
{
    return normalize(mix(N, reflect(-V, N), (1.0 - alpha) * (sqrt(1.0 - alpha) + alpha)));
}

// The kept probes and the sky along dir, blurred for a lobe of GGX alpha, pre-exposed
vec3 Graphite_SampleProbes(Graphite_ProbeSet set, vec3 dir, float alpha)
{
    float roughness = sqrt(alpha);
    vec3 color = vec3(0.0);
    for (int i = 0; i < set.count; i++)
        color += Graphite_SampleReflectionProbe(Graphite_ProbeAt(set.probe[i].x, set.probe[i].y), set.local[i], dir, roughness) * set.weight[i];
    if (set.sky > 0.0)
        color += Graphite_SampleSkyProbe(dir, roughness) * set.sky;
    return color;
}

// The radiance a GGX lobe of alpha around N reflects toward V from the kept probes, before any Fresnel or energy weight
vec3 Graphite_ProbeRadiance(Graphite_ProbeSet set, vec3 N, vec3 V, float alpha)
{
    return Graphite_SampleProbes(set, Graphite_ProbeDirection(N, V, alpha), alpha);
}
