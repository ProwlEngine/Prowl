// Linearly transformed cosines (Heitz et al. 2016): GGX of a roughness seen at N.V as a clamped cosine under a matrix, so
// the share of the lobe over a rect, a disk or a tube is the cosine's integral over the shape that matrix's inverse makes
// of it. A disk is taken as the octagon of its area, which the matrix keeps a polygon. Graphite_Ltc holds the inverse across sqrt(1 - N.V) and roughness, fitted to the GGX of
// Graphite/PBR/Microfacet.glsl without Fresnel by the rendering tests (LtcTables.cs), its middle entry 1. The surface
// multiplies the share by its lobe's directional albedo, which holds its Fresnel.
#pragma once

#include "Graphite/Core.glsl"
#include "Graphite/Tables.glsl"

layout(set = 1, binding = 42) uniform texture2D Graphite_Ltc;

#define GRAPHITE_LTC_SIZE 64.0

// The frame of a lobe, x along the view across the normal, then the table's inverse matrix: world to the cosine's space
mat3 Graphite_LtcSpace(vec3 N, vec3 V, float alpha)
{
    float NdotV = clamp(dot(N, V), 1e-4, 1.0);
    vec3 t1 = V - N * dot(N, V);
    float t1Length = length(t1);
    t1 = t1Length > 1e-5 ? t1 / t1Length : normalize(cross(N, abs(N.y) < 0.99 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0)));
    vec3 t2 = cross(N, t1);
    vec2 uv = (vec2(sqrt(1.0 - NdotV), sqrt(max(alpha, 0.0))) * (GRAPHITE_LTC_SIZE - 1.0) + 0.5) / GRAPHITE_LTC_SIZE;
    vec4 m = textureLod(sampler2D(Graphite_Ltc, Graphite_TableSampler), uv, 0.0);
    mat3 inverse = mat3(vec3(m.x, 0.0, m.z), vec3(0.0, 1.0, 0.0), vec3(m.y, 0.0, m.w));
    return inverse * transpose(mat3(t1, t2, N));
}

// The lobe's density toward the unit direction w, for a light so small the density is even across it
float Graphite_LtcDensity(mat3 space, vec3 w)
{
    vec3 o = space * w;
    float o2 = dot(o, o);
    return max(o.z, 0.0) * abs(determinant(space)) / (GRAPHITE_PI * o2 * o2);
}

// The cosine over a polygon of up to 8 corners in its space, clipped by the horizon
float graphite_LtcPolygon(vec3 q[8], int corners)
{
    vec3 v[9];
    int count = 0;
    for (int i = 0; i < corners; i++)
    {
        vec3 a = q[i], b = q[(i + 1) % corners];
        if (a.z >= 0.0)
            v[count++] = a;
        if ((a.z >= 0.0) != (b.z >= 0.0))
            v[count++] = a + (b - a) * (a.z / (a.z - b.z));
    }
    if (count < 3)
        return 0.0;
    float sum = 0.0;
    for (int i = 0; i < count; i++)
    {
        vec3 a = normalize(v[i]), b = normalize(v[(i + 1) % count]);
        vec3 axb = cross(a, b);
        float s = length(axb);
        if (s > 1e-12)
            sum += atan(s, dot(a, b)) * axb.z / s;
    }
    return abs(sum) / (2.0 * GRAPHITE_PI);
}

// The cosine over a quad of corners in its space, clipped by the horizon
float Graphite_LtcQuad(vec3 p0, vec3 p1, vec3 p2, vec3 p3)
{
    vec3 q[8] = vec3[8](p0, p1, p2, p3, p0, p0, p0, p0);
    return graphite_LtcPolygon(q, 4);
}

// The cosine over a disk of centre c and half axes x and y in its space, as the octagon of its area: corners at
// sqrt(pi / (2 sqrt 2)) of its radius
float Graphite_LtcDisk(vec3 c, vec3 x, vec3 y)
{
    const float scale = 1.0539199, diagonal = 1.0539199 * 0.70710678;
    vec3 dx = x * diagonal, dy = y * diagonal;
    vec3 q[8] = vec3[8](c + x * scale, c + dx + dy, c + y * scale, c - dx + dy, c - x * scale, c - dx - dy, c - y * scale, c + dx - dy);
    return graphite_LtcPolygon(q, 8);
}

// The cosine over a strip from p1 to p2 of width vector w in its space, clipped by the horizon, the strip seen with
// dw = |(t x w).p| dl / |p|^3 integrated in closed form along it
float Graphite_LtcStrip(vec3 p1, vec3 p2, vec3 w)
{
    if (p1.z <= 0.0 && p2.z <= 0.0)
        return 0.0;
    if (p1.z < 0.0)
        p1 = (p1 * p2.z - p2 * p1.z) / (p2.z - p1.z);
    if (p2.z < 0.0)
        p2 = (p2 * p1.z - p1 * p2.z) / (p1.z - p2.z);
    vec3 segment = p2 - p1;
    float segmentLength = length(segment);
    if (segmentLength < 1e-8)
        return 0.0;
    vec3 t = segment / segmentLength;
    float l1 = dot(p1, t), l2 = dot(p2, t);
    vec3 po = p1 - t * l1;
    float d = max(length(po), 1e-7);
    float width = abs(dot(cross(t, w), po)) / d;
    float d2 = d * d;
    float fpo = l2 / (d * (d2 + l2 * l2)) + atan(l2 / d) / d2 - l1 / (d * (d2 + l1 * l1)) - atan(l1 / d) / d2;
    float fwt = l2 * l2 / (d * (d2 + l2 * l2)) - l1 * l1 / (d * (d2 + l1 * l1));
    return max(width * (fpo * po.z + fwt * t.z) / (2.0 * GRAPHITE_PI), 0.0);
}
