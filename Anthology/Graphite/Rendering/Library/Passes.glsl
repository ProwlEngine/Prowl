// Outputs of the built in pass types. The forward opaque pass writes the thin G-buffer next to its colour: the world
// normal, octahedral in a signed two channel target, and roughness and metallic. Screen space effects read it.
#pragma once

#ifdef GRAPHITE_FRAGMENT
layout(location = 1) out vec2 graphite_GBufferNormal;
layout(location = 2) out vec2 graphite_GBufferMaterial;
#endif

vec2 Graphite_OctWrap(vec2 v)
{
    return (1.0 - abs(v.yx)) * vec2(v.x >= 0.0 ? 1.0 : -1.0, v.y >= 0.0 ? 1.0 : -1.0);
}

// A unit normal folded onto an octahedron, in -1 to 1
vec2 Graphite_OctEncode(vec3 n)
{
    n /= abs(n.x) + abs(n.y) + abs(n.z);
    return n.z >= 0.0 ? n.xy : Graphite_OctWrap(n.xy);
}

vec3 Graphite_OctDecode(vec2 e)
{
    vec3 n = vec3(e, 1.0 - abs(e.x) - abs(e.y));
    if (n.z < 0.0)
        n.xy = Graphite_OctWrap(n.xy);
    return normalize(n);
}

#ifdef GRAPHITE_FRAGMENT
void Graphite_WriteGBuffer(vec3 worldNormal, float roughness, float metallic)
{
    graphite_GBufferNormal = Graphite_OctEncode(normalize(worldNormal));
    graphite_GBufferMaterial = vec2(roughness, metallic);
}
#endif
