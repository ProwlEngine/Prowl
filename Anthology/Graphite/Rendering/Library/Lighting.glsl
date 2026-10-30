// Forward lighting: local lights from the H-Forward trees, directional lights from the world's buffer with the sun first,
// ambient light and fog. Directional lights are in lux, points and spots in candela, rects, disks and tubes in nits.
// Spheres, tubes, rects and disks light diffuse by their luminance times their cosine weighted solid angle and specular
// from their most representative point, the lobe widened by the light's size so their energy stays right. A surface
// model walks the lights itself, giving each its Graphite_LightFrame and shading the Graphite_LightContribution it gets
// back. Everything these return is pre-exposed by Graphite_Exposure().
//
// Positions are relative, as every position in Graphite shaders is, so the camera sits at the origin. See Core.glsl.
// Spot and directional lights cast shadows through Graphite/Shadows.glsl.
#pragma once

#include "Graphite/Trees.glsl"
#include "Graphite/Shadows.glsl"
#include "Graphite/PBR/Microfacet.glsl"
#include "Graphite/Textures.glsl"

// Cookies and profiles are compiled in while a light of the world has one
#pragma variant global GRAPHITE_LIGHT_MASKS

#define GRAPHITE_LIGHT_POINT 1
#define GRAPHITE_LIGHT_SPOT 2
#define GRAPHITE_LIGHT_RECT 3
#define GRAPHITE_LIGHT_DISK 4
#define GRAPHITE_LIGHT_TUBE 5

struct Graphite_LightRecord
{
    vec4 positionRange;      // position in its tree's space, its region's or the view's, range
    vec4 colorIntensity;
    vec4 directionType;      // direction it shines or faces, type in bits 0 to 3, shadow 4, cookie 5, profile 6 as uint bits
    vec4 upSize;             // up, rect width, disk radius or tube length
    vec4 shape;              // rect height, source radius, unused
    vec4 spotShadow;         // cos of the outer and inner spot angles, shadow depth and normal bias
    vec4 shadow;             // shadow strength, shadow filter, shadow slot as int bits, layer mask as uint bits
    uvec4 textures;          // cookie, profile and sampler slots
    vec4 hostData;
};

struct Graphite_DirectionalRecord
{
    vec4 directionShadow;    // surface to light direction, shadow enabled
    vec4 color;              // colour times intensity, layer mask as uint bits
    vec4 params;             // sin and cos of the angular radius, unused
    vec4 hostData;
    vec4 shadow;             // shadow depth and normal bias, strength, filter
};

layout(set = 1, binding = 27, std430) readonly buffer Graphite_Directionals { Graphite_DirectionalRecord graphite_Directionals[]; };
layout(set = 1, binding = 22, std430) readonly buffer Graphite_WorldLighting
{
    uvec4 counts;            // directional lights, unused, probes present, unused
    uvec4 reflections;       // the sky probe's cube slot, the probes' sampler slot, the sky probe's mips, whether it is captured
    vec4 skyScale;           // one over the exposure the sky probe and its harmonics were captured at, unused
    vec4 ambientColor;
    vec4 ambientSky;
    vec4 ambientGround;
    vec4 ambientMode;        // uniform, hemisphere, strength, sky
    vec4 fogColor;
    vec4 fogParams;
    vec4 fogStates;          // linear, exponential, exponential squared, use sky
} graphite_World;

// A light as shaders shade it: where it is, how far it reaches and what it is. The rest of a recorded light is read from
// its record where it is used, so a light holds few registers while its shadow is looked up; a light built in the shader
// keeps the rest in its fields.
struct LightSample
{
    vec3  Position;          // camera relative
    float Range;
    int   Type;              // 1 point, 2 spot, 3 rect, 4 disk, 5 tube
    uint  RecordArray;       // the record array and first word of its record, GRAPHITE_TREE_INVALID for a light built in the shader
    uint  RecordWord;
    vec3  Color;             // the rest of a light built in the shader
    float Intensity;
    vec3  Direction;
    float SourceRadius;
    float SpotCos;           // cos(outer)
    float InnerSpotCos;      // cos(inner)
};

// A point light at a camera relative position, every other field at its default, for shaders building lights themselves
LightSample Graphite_NewLightSample(vec3 position, float range, vec3 color, float intensity)
{
    LightSample L;
    L.Position = position;
    L.Range = range;
    L.Type = GRAPHITE_LIGHT_POINT;
    L.RecordArray = GRAPHITE_TREE_INVALID;
    L.RecordWord = 0u;
    L.Color = color;
    L.Intensity = intensity;
    L.Direction = vec3(0.0, 0.0, 1.0);
    L.SourceRadius = 0.0;
    L.SpotCos = -1.0;
    L.InnerSpotCos = 1.0;
    return L;
}

// A word of a light's record, the default for a light built in the shader
vec4 graphite_LightWord(LightSample L, uint word, vec4 fallback)
{
    return L.RecordArray == GRAPHITE_TREE_INVALID ? fallback : Graphite_SpatialVec4(L.RecordArray, L.RecordWord + word);
}

// The direction a spot, rect, disk or tube faces
vec3 Graphite_LightDirection(LightSample L) { return graphite_LightWord(L, 2u, vec4(L.Direction, 0.0)).xyz; }

// The type in the low four bits, then whether it casts 16, has a cookie 32 and has a profile 64
uint Graphite_LightFlags(LightSample L) { return floatBitsToUint(graphite_LightWord(L, 2u, vec4(0.0)).w); }

// The radius of the sphere a point or spot shines from, 0 for a point source
float Graphite_LightSourceRadius(LightSample L) { return graphite_LightWord(L, 4u, vec4(0.0, L.SourceRadius, 0.0, 0.0)).y; }

// A spot's outer then inner cone cosine
vec2 Graphite_LightSpotCos(LightSample L) { return graphite_LightWord(L, 5u, vec4(L.SpotCos, L.InnerSpotCos, 0.0, 0.0)).xy; }

// The up direction of a rect, disk or cookie
vec3 Graphite_LightUp(LightSample L) { return graphite_LightWord(L, 3u, vec4(0.0, 1.0, 0.0, 0.0)).xyz; }

// A rect's width and height, a disk's radius or a tube's length in x
vec2 Graphite_LightSize(LightSample L) { return vec2(graphite_LightWord(L, 3u, vec4(0.0)).w, graphite_LightWord(L, 4u, vec4(0.0)).x); }

// The cookie, profile and sampler slots
uvec3 Graphite_LightTextures(LightSample L) { return floatBitsToUint(graphite_LightWord(L, 7u, vec4(0.0)).xyz); }

// The host's data for custom lighting, LightDesc.HostData
vec4 Graphite_LightHostData(LightSample L) { return graphite_LightWord(L, 8u, vec4(0.0)); }

// A light's colour times its intensity
vec3 Graphite_LightRadiance(LightSample L)
{
    vec4 colorIntensity = graphite_LightWord(L, 1u, vec4(L.Color, L.Intensity));
    return colorIntensity.rgb * colorIntensity.w;
}

// How much of a local light reaches a surface past its shadow, 1 lit
float Graphite_LocalLightShadow(LightSample L, vec3 relativePos, vec3 geomNormal)
{
    if (L.RecordArray == GRAPHITE_TREE_INVALID || (L.Type != GRAPHITE_LIGHT_POINT && L.Type != GRAPHITE_LIGHT_SPOT) || !Graphite_ReceivesShadows())
        return 1.0;
    int slot = floatBitsToInt(Graphite_SpatialVec4(L.RecordArray, L.RecordWord + 6u).z);
    if (slot < 0)
        return 1.0;
    return Graphite_LocalShadow(slot, L.Type == GRAPHITE_LIGHT_POINT, L.Position, Graphite_LightDirection(L), Graphite_LightSourceRadius(L), relativePos, geomNormal);
}

// Reads where a recorded light is and what it is. offset and offsetLow move camera relative positions into its tree.
void Graphite_ReadLight(uint array, uint index, vec3 offset, vec3 offsetLow, out LightSample L)
{
    uint w = index * 9u;
    vec4 positionRange = Graphite_SpatialVec4(array, w);
    precise vec3 position = (positionRange.xyz - offset) - offsetLow;
    L.Position = position;
    L.Range = positionRange.w;
    L.Type = int(floatBitsToUint(Graphite_SpatialVec4(array, w + 2u).w) & 15u);
    L.RecordArray = array;
    L.RecordWord = w;
    L.Color = vec3(0.0);
    L.Intensity = 0.0;
    L.Direction = vec3(0.0, 0.0, 1.0);
    L.SourceRadius = 0.0;
    L.SpotCos = -1.0;
    L.InnerSpotCos = 1.0;
}

// The lights whose spheres hold a point and that light the surface's layers, the first GRAPHITE_MAX_HITS of them, found
// before any is read so walking the trees and shading the lights never hold their registers at once. Queries share the
// hit list, so shade a point's lights before beginning another query.
struct Graphite_LightIterator
{
    uint next;
    uint count;
};

Graphite_LightIterator Graphite_BeginLights(vec3 relativePos)
{
    Graphite_LightIterator it;
    it.next = 0u;
    it.count = 0u;
    uint layers = Graphite_SurfaceLayers();
    Graphite_TreeQuery q;
    Graphite_BeginQuery(q, relativePos, GRAPHITE_SET_LIGHTS);
    uint array, first, last;
    while (it.count < uint(GRAPHITE_MAX_HITS) && Graphite_NextRun(q, array, first, last))
    {
        for (uint k = first; k < last && it.count < uint(GRAPHITE_MAX_HITS); k++)
        {
            vec4 positionRange = Graphite_SpatialVec4(array, k * 9u);
            precise vec3 position = (positionRange.xyz - q.offset) - q.offsetLow;
            vec3 d = relativePos - position;
            if (dot(d, d) <= positionRange.w * positionRange.w && (floatBitsToUint(Graphite_SpatialVec4(array, k * 9u + 6u).w) & layers) != 0u)
                graphite_Hits[it.count++] = uvec2(k | ((array & 1u) << 31), q.region);
        }
    }
    return it;
}

bool Graphite_NextLight(inout Graphite_LightIterator it, out LightSample L)
{
    if (it.next >= it.count)
        return false;
    uvec2 hit = graphite_Hits[it.next++];
    vec3 offset, offsetLow;
    Graphite_HitOffsets(GRAPHITE_SET_LIGHTS, hit.y, offset, offsetLow);
    Graphite_ReadLight(GRAPHITE_SET_LIGHTS * 2u + (hit.x >> 31), hit.x & 0x7FFFFFFFu, offset, offsetLow, L);
    return true;
}

struct Graphite_DirectionalLight
{
    vec3  direction;         // surface to light
    vec3  color;             // colour times intensity
    float sinRadius;         // of the disk's angular radius
    float cosRadius;
    bool  shadow;
    uint  layers;
    vec4  hostData;
    float shadowDepthBias;
    float shadowNormalBias;
    float shadowStrength;
    float shadowFilter;
};

uint Graphite_DirectionalCount() { return graphite_World.counts.x; }

// The world's directional lights, the sun at 0
Graphite_DirectionalLight Graphite_GetDirectional(uint index)
{
    Graphite_DirectionalRecord r = graphite_Directionals[index];
    Graphite_DirectionalLight d;
    d.direction = normalize(r.directionShadow.xyz);
    d.color = r.color.rgb;
    d.sinRadius = r.params.x;
    d.cosRadius = r.params.y;
    d.shadow = r.directionShadow.w > 0.5;
    d.layers = floatBitsToUint(r.color.w);
    d.hostData = r.hostData;
    d.shadowDepthBias = r.shadow.x;
    d.shadowNormalBias = r.shadow.y;
    d.shadowStrength = r.shadow.z;
    d.shadowFilter = r.shadow.w;
    return d;
}

// Where and how a point is lit, each specular lobe with its own normal and GGX alpha. A model defines GRAPHITE_LIGHT_COAT
// to have the coat lobe lit and GRAPHITE_LIGHT_BACK to have the light reaching its back, else neither is computed.
struct Graphite_LightFrame
{
    vec3  position;          // camera relative
    vec3  view;              // to the camera
    vec3  geomNormal;        // before normal mapping, for shadow offsets
    vec3  baseNormal;
    vec3  coatNormal;
    float baseAlpha;
    float coatAlpha;
};

struct Graphite_LightContribution
{
    vec3  radiance;          // colour times intensity times the exposure, with shadow, cookie and profile
    vec3  diffuseL;          // unit direction to the light for the diffuse lobe
    float diffuse;           // irradiance per unit radiance: N.L over distance squared for a point, the shape's cosine weighted solid angle for a shape, windowed
    vec3  specularL;         // unit direction to the light's most representative point for the base normal
    float specular;          // N.L of specularL over distance squared, windowed, scaled and times Graphite_AreaEnergy at the base alpha
    vec2  extent;            // the light's angular extent across two axes seen along specularL, 0 for a point
#ifdef GRAPHITE_LIGHT_COAT
    vec3  coatL;             // as specularL for the coat normal
    float coat;              // as specular for the coat normal and alpha
#endif
#ifdef GRAPHITE_LIGHT_BACK
    float diffuseBack;       // as diffuse on the back, the normal flipped
#endif
    float attenuation;       // irradiance per unit radiance facing the light
};

// A smooth window, reaching zero at the range
float Graphite_Window(float dist2, float range)
{
    float factor = dist2 / max(range * range, 1e-6);
    float window = clamp(1.0 - factor * factor, 0.0, 1.0);
    return window * window;
}

// The energy of a specular lobe widened by a light's angular extent across two axes, ex and ey, the widths adding in quadrature
float Graphite_AreaEnergy(float alpha, float ex, float ey)
{
    float a = max(alpha, PBR_MIN_ALPHA);
    return (a * a) / (min(sqrt(a * a + ex * ex), 1.0) * min(sqrt(a * a + ey * ey), 1.0));
}

// The cosine weighted solid angle of a sphere, or of a disk facing the point, whose halfSize angle has sin squared sinSigma2
// and whose centre is at cosTheta to the normal, clipped by the horizon
float Graphite_SphereIrradiance(float cosTheta, float sinSigma2)
{
    if (sinSigma2 >= 1.0)
        return PI;
    if (cosTheta * cosTheta > sinSigma2)
        return PI * sinSigma2 * max(cosTheta, 0.0);
    float sinTheta = sqrt(max(1.0 - cosTheta * cosTheta, 0.0));
    float x = sqrt(max(1.0 / sinSigma2 - 1.0, 0.0));
    float y = clamp(-x * cosTheta / max(sinTheta, 1e-6), -1.0, 1.0);
    float sinThetaSqrtY = sinTheta * sqrt(max(1.0 - y * y, 0.0));
    return max((cosTheta * acos(y) - x * sinThetaSqrtY) * sinSigma2 + atan(sinThetaSqrtY, max(x, 1e-6)), 0.0);
}

float Graphite_EdgeIntegral(vec3 a, vec3 b, vec3 n)
{
    vec3 axb = cross(a, b);
    float s = length(axb);
    return s > 1e-12 ? atan(s, dot(a, b)) * dot(axb, n) / s : 0.0;
}

// The cosine weighted solid angle of a quad of corners relative to the point, clipped by the horizon
float Graphite_QuadIrradiance(vec3 p0, vec3 p1, vec3 p2, vec3 p3, vec3 n)
{
    vec3 q[4] = vec3[4](p0, p1, p2, p3);
    vec3 v[8];
    int count = 0;
    for (int i = 0; i < 4; i++)
    {
        vec3 a = q[i], b = q[(i + 1) & 3];
        float da = dot(a, n), db = dot(b, n);
        if (da >= 0.0)
            v[count++] = a;
        if ((da >= 0.0) != (db >= 0.0))
            v[count++] = a + (b - a) * (da / (da - db));
    }
    if (count < 3)
        return 0.0;
    float sum = 0.0;
    for (int i = 0; i < count; i++)
    {
        vec3 a = v[i], b = v[(i + 1) % count];
        float la = length(a), lb = length(b);
        if (la > 1e-6 && lb > 1e-6)
            sum += Graphite_EdgeIntegral(a / la, b / lb, n);
    }
    return 0.5 * abs(sum);
}

// The cosine weighted solid angle of the side of a thin tube of radius from a to b, relative to the point, clipped by the
// horizon. Its width shrinks seen along the tube, so it is integrated over the angle seen from the tube's nearest point.
float Graphite_TubeIrradiance(vec3 a, vec3 b, float radius, vec3 n)
{
    vec3 d = b - a;
    float len = length(d);
    if (len < 1e-4)
        return 0.0;
    vec3 u = d / len;
    float na = dot(n, a), nu = dot(n, u);
    float s0 = 0.0, s1 = len;
    if (abs(nu) > 1e-6)
    {
        float horizon = -na / nu;
        if (nu > 0.0)
            s0 = max(s0, horizon);
        else
            s1 = min(s1, horizon);
    }
    else if (na < 0.0)
    {
        return 0.0;
    }
    if (s1 <= s0)
        return 0.0;

    float bu = dot(a, u);
    vec3 perp = a - u * bu;
    float perpLength = length(perp);
    float h = max(perpLength, radius);
    float np = dot(n, perp) / max(perpLength, 1e-6);
    float t0 = atan((s0 + bu) / h), t1 = atan((s1 + bu) / h);
    float across = 0.5 * (t1 - t0) + 0.25 * (sin(2.0 * t1) - sin(2.0 * t0));
    float along = 0.5 * (sin(t1) * sin(t1) - sin(t0) * sin(t0));
    return max(2.0 * radius * (np * across + nu * along) / h, 0.0);
}

// The point of a sphere around center nearest the ray from the surface along R
vec3 Graphite_SpherePoint(vec3 center, float radius, vec3 R)
{
    vec3 toRay = dot(center, R) * R - center;
    return center + toRay * clamp(radius / max(length(toRay), 1e-6), 0.0, 1.0);
}

// The point of a rect or disk nearest where the ray along R meets its plane, center relative, facing n
vec3 Graphite_PlanePoint(vec3 center, vec3 n, vec3 right, vec3 up, vec2 halfSize, bool disk, vec3 R)
{
    float facing = dot(R, n);
    vec3 hit = facing < -1e-4 ? R * (dot(center, n) / facing) : center + (R - n * facing) * 1e4;
    vec3 local = hit - center;
    vec2 p = vec2(dot(local, right), dot(local, up));
    if (disk)
        p *= min(1.0, halfSize.x / max(length(p), 1e-6));
    else
        p = clamp(p, -halfSize, halfSize);
    return center + right * p.x + up * p.y;
}

// A sphere's specular for one lobe from its point nearest the lobe's reflected ray
float graphite_SphereSpecular(vec3 toLight, float radius, float dist, float attenuation, vec3 N, vec3 V, float alpha, out vec3 L)
{
    vec3 p = Graphite_SpherePoint(toLight, radius, reflect(-V, N));
    L = dot(p, p) > 1e-12 ? normalize(p) : N;
    float extent = radius / (2.0 * max(dist, 1e-4));
    return max(dot(N, L), 0.0) * attenuation * Graphite_AreaEnergy(alpha, extent, extent);
}

// A tube's specular for one lobe: the point of the segment from a along ab nearest the reflected ray, then of the capsule
// around it, seen with its projected area
float graphite_TubeSpecular(vec3 a, vec3 ab, vec3 direction, float radius, float tubeLength, float window, vec3 N, vec3 V, float alpha, out vec3 L, out vec2 extent)
{
    vec3 R = reflect(-V, N);
    float rab = dot(R, ab);
    float t = clamp((dot(R, a) * rab - dot(a, ab)) / max(dot(ab, ab) - rab * rab, 1e-8), 0.0, 1.0);
    vec3 onLine = a + ab * t;
    vec3 p = Graphite_SpherePoint(onLine, radius, R);
    L = dot(p, p) > 1e-12 ? normalize(p) : N;
    float lineDist = max(length(onLine), 1e-4);
    float along = dot(onLine, direction) / lineDist;
    float projected = 2.0 * radius * tubeLength * sqrt(max(1.0 - along * along, 0.0)) + PI * radius * radius;
    extent = vec2(radius / (2.0 * lineDist), tubeLength / (PI * lineDist));
    return max(dot(N, L), 0.0) * projected * window / max(dot(onLine, onLine), 0.01) * Graphite_AreaEnergy(alpha, extent.x, extent.y);
}

// A rect or disk's specular for one lobe from its point nearest where the reflected ray meets its plane
float graphite_PlaneSpecular(vec3 center, vec3 n, vec3 right, vec3 up, vec2 halfSize, vec2 size, bool disk, float area, float window,
                             vec3 N, vec3 V, float alpha, out vec3 L, out vec2 extent)
{
    vec3 p = Graphite_PlanePoint(center, n, right, up, halfSize, disk, reflect(-V, N));
    float pDist2 = max(dot(p, p), 1e-8);
    float pDist = sqrt(pDist2);
    L = p / pDist;
    float pFront = max(dot(-L, n), 0.0);
    extent = disk ? vec2(halfSize.x / (2.0 * pDist)) : size / (2.0 * sqrt(PI) * pDist);
    return max(dot(N, L), 0.0) * area * pFront * window / max(pDist2, 0.01) * Graphite_AreaEnergy(alpha, extent.x, extent.y);
}

// The cookie and profile in a point or spot's own space, w the direction from it to the surface
vec3 Graphite_LightMask(LightSample L, vec3 w)
{
    vec3 mask = vec3(1.0);
#ifdef GRAPHITE_LIGHT_MASKS
    uint flags = Graphite_LightFlags(L);
    if ((flags & 96u) == 0u)
        return mask;
    vec3 up = Graphite_LightUp(L);
    vec3 direction = Graphite_LightDirection(L);
    uvec3 textures = Graphite_LightTextures(L);
    vec3 right = cross(up, direction);
    vec3 local = vec3(dot(w, right), dot(w, up), dot(w, direction));
    if ((flags & 32u) != 0u)
    {
        if (L.Type == GRAPHITE_LIGHT_SPOT)
        {
            float outer = Graphite_LightSpotCos(L).x;
            float tanOuter = sqrt(max(1.0 - outer * outer, 0.0)) / max(outer, 1e-4);
            vec2 uv = local.xy / (max(local.z, 1e-4) * tanOuter) * 0.5 + 0.5;
            mask *= GRAPHITE_SAMPLE_2D_LOD(textures.x, textures.z, uv, 0.0).rgb;
        }
        else
        {
            mask *= GRAPHITE_SAMPLE_CUBE_LOD(textures.x, textures.z, local, 0.0).rgb;
        }
    }
    if ((flags & 64u) != 0u)
    {
        // The angle from the direction across U's texel centres, the angle around it from up across V
        float width = float(textureSize(sampler2D(Graphite_Textures2D[nonuniformEXT(textures.y)], Graphite_Samplers[nonuniformEXT(textures.z)]), 0).x);
        float u = acos(clamp(local.z, -1.0, 1.0)) / PI;
        vec2 uv = vec2((u * (width - 1.0) + 0.5) / width, fract(atan(local.x, local.y) / (2.0 * PI)));
        mask *= GRAPHITE_SAMPLE_2D_LOD(textures.y, textures.z, uv, 0.0).r;
    }
#endif
    return mask;
}

// The normal shadows offset along, turned toward a light behind a surface lit through its back
vec3 graphite_ShadowNormal(vec3 geomNormal, vec3 L)
{
#ifdef GRAPHITE_LIGHT_BACK
    return dot(geomNormal, L) < 0.0 ? -geomNormal : geomNormal;
#else
    return geomNormal;
#endif
}

// What a local light gives a frame. backLit keeps lights behind the surface.
bool Graphite_ContributeLocal(LightSample L, Graphite_LightFrame f, bool backLit, out Graphite_LightContribution c)
{
    vec3 toLight = L.Position - f.position;
    float dist2 = dot(toLight, toLight);
    float dist = sqrt(dist2);
    vec3 Lc = toLight * (1.0 / max(dist, 1e-6));
    float window = Graphite_Window(dist2, L.Range);
    vec3 N = f.baseNormal;
    float NdotL = dot(N, Lc);

    c.radiance = vec3(Graphite_Exposure());
    c.diffuseL = Lc;
    c.specularL = Lc;
    c.extent = vec2(0.0);
#ifdef GRAPHITE_LIGHT_COAT
    c.coatL = Lc;
#endif
    float shadow = 1.0;

    if (L.Type == GRAPHITE_LIGHT_POINT || L.Type == GRAPHITE_LIGHT_SPOT)
    {
        float cone = 1.0;
        if (L.Type == GRAPHITE_LIGHT_SPOT)
        {
            float axisCos = dot(normalize(Graphite_LightDirection(L)), -Lc);
            vec2 spotCos = Graphite_LightSpotCos(L);
            if (axisCos <= spotCos.x)
                return false;
            cone = smoothstep(spotCos.x, spotCos.y, axisCos);
        }

        float radius = Graphite_LightSourceRadius(L);
        bool sphere = radius > 1e-4;
        float facing = NdotL;
#ifdef GRAPHITE_LIGHT_COAT
        facing = max(facing, dot(f.coatNormal, Lc));
#endif
#ifdef GRAPHITE_LIGHT_BACK
        facing = max(facing, -NdotL);
#endif
        if (facing <= 0.0 && !backLit && !sphere)
            return false;

        c.attenuation = window / max(dist2, 0.01) * cone;
        if (c.attenuation <= 0.0001)
            return false;

        // The shadow before the rest of the light, so its working values and the light's are never held together, and a
        // light wholly shadowed here does no more
#ifndef GRAPHITE_NO_SHADOWS
        shadow = Graphite_LocalLightShadow(L, f.position, graphite_ShadowNormal(f.geomNormal, Lc));
        if (shadow <= 0.0)
            return false;
#endif

        if (!sphere)
        {
            c.diffuse = max(NdotL, 0.0) * c.attenuation;
            c.specular = c.diffuse;
#ifdef GRAPHITE_LIGHT_COAT
            c.coat = max(dot(f.coatNormal, Lc), 0.0) * c.attenuation;
#endif
#ifdef GRAPHITE_LIGHT_BACK
            c.diffuseBack = max(-NdotL, 0.0) * c.attenuation;
#endif
        }
        else
        {
            float sinSigma2 = min(radius * radius / max(dist2, 1e-8), 1.0);
            float toNits = window * cone / (PI * radius * radius);
            c.diffuse = Graphite_SphereIrradiance(NdotL, sinSigma2) * toNits;
#ifdef GRAPHITE_LIGHT_BACK
            c.diffuseBack = Graphite_SphereIrradiance(-NdotL, sinSigma2) * toNits;
#endif
            c.specular = graphite_SphereSpecular(toLight, radius, dist, c.attenuation, N, f.view, f.baseAlpha, c.specularL);
#ifdef GRAPHITE_LIGHT_COAT
            c.coat = graphite_SphereSpecular(toLight, radius, dist, c.attenuation, f.coatNormal, f.view, f.coatAlpha, c.coatL);
#endif
            c.extent = vec2(radius / (2.0 * max(dist, 1e-4)));
        }
        c.radiance *= Graphite_LightMask(L, -Lc);
    }
    else if (L.Type == GRAPHITE_LIGHT_TUBE)
    {
        float tubeLength = Graphite_LightSize(L).x;
        float tubeRadius = Graphite_LightSourceRadius(L);
        vec3 direction = normalize(Graphite_LightDirection(L));
        vec3 axis = direction * (0.5 * tubeLength);
        vec3 a = toLight - axis, b = toLight + axis;
        vec3 ab = b - a;
        float t = clamp(-dot(a, ab) / max(dot(ab, ab), 1e-8), 0.0, 1.0);
        vec3 nearest = a + ab * t;
        c.diffuseL = dot(nearest, nearest) > 1e-12 ? normalize(nearest) : N;
        // The side, then the two caps together as a sphere at its middle
        float sinSigma2 = min(tubeRadius * tubeRadius / max(dist2, 1e-8), 1.0);
        c.diffuse = (Graphite_TubeIrradiance(a, b, tubeRadius, N) + Graphite_SphereIrradiance(NdotL, sinSigma2)) * window;
#ifdef GRAPHITE_LIGHT_BACK
        c.diffuseBack = (Graphite_TubeIrradiance(a, b, tubeRadius, -N) + Graphite_SphereIrradiance(-NdotL, sinSigma2)) * window;
#endif
        c.attenuation = (2.0 * tubeRadius * tubeLength + PI * tubeRadius * tubeRadius) * window / max(dist2, 0.01);
        c.specular = graphite_TubeSpecular(a, ab, direction, tubeRadius, tubeLength, window, N, f.view, f.baseAlpha, c.specularL, c.extent);
#ifdef GRAPHITE_LIGHT_COAT
        vec2 coatExtent;
        c.coat = graphite_TubeSpecular(a, ab, direction, tubeRadius, tubeLength, window, f.coatNormal, f.view, f.coatAlpha, c.coatL, coatExtent);
#endif
    }
    else
    {
        // Rects and disks shine from their front, the side their direction faces
        vec3 n = normalize(Graphite_LightDirection(L));
        float front = dot(-Lc, n);
        if (front <= 0.0)
            return false;
        vec3 up = Graphite_LightUp(L);
        vec2 size = Graphite_LightSize(L);
        vec3 right = cross(up, n);
        bool disk = L.Type == GRAPHITE_LIGHT_DISK;
        vec2 halfSize = disk ? vec2(size.x) : 0.5 * size;
        float area = disk ? PI * halfSize.x * halfSize.x : size.x * size.y;
        if (area <= 0.0)
            return false;
        c.attenuation = area * window * front / max(dist2, 0.01);

        if (disk)
        {
            float r2 = halfSize.x * halfSize.x;
            float sinSigma2 = r2 / (r2 + dist2);
            c.diffuse = Graphite_SphereIrradiance(NdotL, sinSigma2) * front * window;
#ifdef GRAPHITE_LIGHT_BACK
            c.diffuseBack = Graphite_SphereIrradiance(-NdotL, sinSigma2) * front * window;
#endif
        }
        else
        {
            vec3 hx = right * halfSize.x, hy = up * halfSize.y;
            vec3 p0 = toLight - hx - hy, p1 = toLight + hx - hy, p2 = toLight + hx + hy, p3 = toLight - hx + hy;
            c.diffuse = Graphite_QuadIrradiance(p0, p1, p2, p3, N) * window;
#ifdef GRAPHITE_LIGHT_BACK
            c.diffuseBack = Graphite_QuadIrradiance(p0, p1, p2, p3, -N) * window;
#endif
        }

        c.specular = graphite_PlaneSpecular(toLight, n, right, up, halfSize, size, disk, area, window, N, f.view, f.baseAlpha, c.specularL, c.extent);
#ifdef GRAPHITE_LIGHT_COAT
        vec2 coatExtent;
        c.coat = graphite_PlaneSpecular(toLight, n, right, up, halfSize, size, disk, area, window, f.coatNormal, f.view, f.coatAlpha, c.coatL, coatExtent);
#endif
    }

    float lit = c.diffuse + c.specular;
#ifdef GRAPHITE_LIGHT_COAT
    lit += c.coat;
#endif
#ifdef GRAPHITE_LIGHT_BACK
    lit += c.diffuseBack;
#endif
    if (lit <= 0.0 && !backLit)
        return false;

    c.radiance *= Graphite_LightRadiance(L) * shadow;
    return true;
}

// How much of a directional light reaches a surface past its shadow, 1 lit. index is its place in the world's buffer.
float Graphite_DirectionalLightShadow(Graphite_DirectionalLight d, uint index, vec3 relativePos, vec3 geomNormal)
{
#ifdef GRAPHITE_NO_SHADOWS
    return 1.0;
#else
    if (!d.shadow || !Graphite_ReceivesShadows())
        return 1.0;
    return Graphite_DirectionalShadow(index, d.direction, d.shadowDepthBias, d.shadowNormalBias, d.shadowStrength, d.shadowFilter, d.sinRadius / max(d.cosRadius, 1e-4), relativePos, geomNormal);
#endif
}

// The sun's shadow, 1 lit
float MainDirectionalShadowFactor(vec3 relativePos, vec3 geomNormal)
{
    if (Graphite_DirectionalCount() == 0u)
        return 1.0;
    return Graphite_DirectionalLightShadow(Graphite_GetDirectional(0u), 0u, relativePos, geomNormal);
}

// The direction inside a directional light's disk nearest a lobe's reflected ray
vec3 graphite_DiskDirection(Graphite_DirectionalLight d, vec3 N, vec3 V)
{
    vec3 R = reflect(-V, N);
    float RdotL = dot(R, d.direction);
    if (RdotL >= d.cosRadius)
        return R;
    vec3 across = R - d.direction * RdotL;
    return dot(across, across) > 1e-8 ? normalize(d.direction * d.cosRadius + normalize(across) * d.sinRadius) : d.direction;
}

// What a directional light gives a frame, each specular lobe from the nearest direction inside its disk. index 0 is the sun.
bool Graphite_ContributeDirectional(Graphite_DirectionalLight d, uint index, Graphite_LightFrame f, out Graphite_LightContribution c)
{
    if ((d.layers & Graphite_SurfaceLayers()) == 0u)
        return false;
    float shadow = Graphite_DirectionalLightShadow(d, index, f.position, graphite_ShadowNormal(f.geomNormal, d.direction));
    float NdotL = dot(f.baseNormal, d.direction);
    c.radiance = d.color * (Graphite_Exposure() * shadow);
    c.diffuseL = d.direction;
    c.diffuse = max(NdotL, 0.0);
    c.attenuation = 1.0;
    c.specularL = d.direction;
    c.specular = c.diffuse;
    c.extent = vec2(0.0);
#ifdef GRAPHITE_LIGHT_COAT
    c.coatL = d.direction;
    c.coat = max(dot(f.coatNormal, d.direction), 0.0);
#endif
#ifdef GRAPHITE_LIGHT_BACK
    c.diffuseBack = max(-NdotL, 0.0);
#endif
    if (d.sinRadius > 0.0)
    {
        float extent = 0.5 * d.sinRadius;
        c.extent = vec2(extent);
        c.specularL = graphite_DiskDirection(d, f.baseNormal, f.view);
        c.specular = max(dot(f.baseNormal, c.specularL), 0.0) * Graphite_AreaEnergy(f.baseAlpha, extent, extent);
#ifdef GRAPHITE_LIGHT_COAT
        c.coatL = graphite_DiskDirection(d, f.coatNormal, f.view);
        c.coat = max(dot(f.coatNormal, c.coatL), 0.0) * Graphite_AreaEnergy(f.coatAlpha, extent, extent);
#endif
    }
    return true;
}

// ============================================================
//  Ambient lighting
// ============================================================

vec3 Graphite_SkyAmbient(vec3 worldNormal);

// The environment's ambient light along a normal, irradiance over pi in nits, pre-exposed
vec3 Graphite_AmbientLight(vec3 worldNormal)
{
    vec3 ambient = graphite_World.ambientColor.rgb * graphite_World.ambientMode.x;
    if (graphite_World.ambientMode.w > 0.0)
        ambient += Graphite_SkyAmbient(worldNormal) * graphite_World.ambientMode.w;

    float upDot = dot(worldNormal, vec3(0.0, 1.0, 0.0));
    ambient += mix(graphite_World.ambientGround.rgb, graphite_World.ambientSky.rgb, upDot * 0.5 + 0.5) * graphite_World.ambientMode.y;

    return ambient * Graphite_Exposure();
}

// ============================================================
//  Baked lighting
// ============================================================

struct Graphite_ProbeSH { vec4 shAr; vec4 shAg; vec4 shAb; vec4 shBr; vec4 shBg; vec4 shBb; vec4 shC; };

layout(set = 1, binding = 23, std430) readonly buffer Graphite_ObjectProbes { Graphite_ProbeSH graphite_ObjectProbes[]; };

#define GRAPHITE_NO_LIGHTMAP 0xFFFFFFFFu
#define GRAPHITE_LIGHTMAP_LOADING 0xFFFFFFFEu

// A lightmap texel as light: as is, or colour times alpha times 8 for RGBM, times the lightmap's scale
vec3 DecodeLightmap(vec4 texel, uint encoding, float scale)
{
    return (encoding == 1u ? texel.rgb * (texel.a * 8.0) : texel.rgb) * scale;
}

// Diffuse irradiance over pi for a normal from packed order 2 harmonics
vec3 Graphite_ShadeSH9(Graphite_ProbeSH sh, vec3 n)
{
    vec4 nrm = vec4(n, 1.0);
    vec3 x;
    x.r = dot(sh.shAr, nrm);
    x.g = dot(sh.shAg, nrm);
    x.b = dot(sh.shAb, nrm);

    vec4 vB = n.xyzz * n.yzzx;
    vec3 x1;
    x1.r = dot(sh.shBr, vB);
    x1.g = dot(sh.shBg, vB);
    x1.b = dot(sh.shBb, vB);

    float vC = n.x * n.x - n.y * n.y;
    vec3 x2 = sh.shC.rgb * vC;

    return max(x + x1 + x2, vec3(0.0));
}

layout(set = 1, binding = 26, std430) readonly buffer Graphite_SkyAmbientSH { Graphite_ProbeSH graphite_SkyAmbient; };

// The sky's light along a normal from its harmonics, not exposed
vec3 Graphite_SkyAmbient(vec3 worldNormal)
{
    return Graphite_ShadeSH9(graphite_SkyAmbient, worldNormal) * graphite_World.skyScale.x;
}

// The view's screen lighting, written by its ScreenLightingFeature after the prepass: visibility, and indirect irradiance
// with its confidence. Views without one read full visibility and no irradiance.
layout(set = 1, binding = 31) uniform texture2DArray Graphite_ScreenVisibility;
layout(set = 1, binding = 32) uniform texture2DArray Graphite_ScreenIrradiance;
layout(set = 1, binding = 33) uniform sampler Graphite_ScreenSampler;

#ifdef GRAPHITE_FRAGMENT
vec3 graphite_ScreenCoord() { return vec3(gl_FragCoord.xy * Graphite_View().screen.zw, float(gl_ViewIndex)); }

// How much indirect light reaches this pixel, from the view's screen lighting
float Graphite_ScreenOcclusion() { return texture(sampler2DArray(Graphite_ScreenVisibility, Graphite_ScreenSampler), graphite_ScreenCoord()).r; }

// Indirect irradiance the view's screen lighting found at this pixel, pre-exposed like the scene colour it comes from,
// and how far to trust it over the baked light
vec4 Graphite_ScreenIndirect() { return texture(sampler2DArray(Graphite_ScreenIrradiance, Graphite_ScreenSampler), graphite_ScreenCoord()); }
#endif

#if defined(GRAPHITE_FRAGMENT) && !defined(GRAPHITE_NO_OBJECT)
// The diffuse light that is not realtime: the object's lightmap, ambient while it loads, else its light probes, else the
// ambient light, occluded by the view's screen lighting and blended toward the irradiance it found, pre-exposed.
// lightmapUV comes from Graphite_LightmapUV in the vertex stage.
vec3 graphite_BakedDiffuse(vec3 worldNormal, vec2 lightmapUV);

vec3 Graphite_IndirectDiffuse(vec3 worldNormal, vec2 lightmapUV)
{
    vec4 screen = Graphite_ScreenIndirect();
    return mix(graphite_BakedDiffuse(worldNormal, lightmapUV) * Graphite_ScreenOcclusion(), screen.rgb, screen.a);
}

vec3 graphite_BakedDiffuse(vec3 worldNormal, vec2 lightmapUV)
{
    Graphite_ObjectData object = Graphite_Object();
    if (object.lightmapTexture == GRAPHITE_LIGHTMAP_LOADING)
        return Graphite_AmbientLight(worldNormal) * graphite_World.ambientMode.z;
    if (object.lightmapTexture != GRAPHITE_NO_LIGHTMAP)
        return DecodeLightmap(GRAPHITE_SAMPLE_2D(object.lightmapTexture, object.lightmapSampler & 0xFFFFu, lightmapUV), object.lightmapSampler >> 16, object.lightmapScale) * Graphite_Exposure();
    if (object.probeIndex != 0xFFFFFFFFu && graphite_World.counts.z != 0u)
        return Graphite_ShadeSH9(graphite_ObjectProbes[object.probeIndex], worldNormal) * Graphite_Exposure();
    return Graphite_AmbientLight(worldNormal) * graphite_World.ambientMode.z;
}
#endif

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


// ============================================================
//  Reflection probes
// ============================================================

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

#ifndef MAX_BLENDED_PROBES
#define MAX_BLENDED_PROBES 4
#endif

bool Graphite_ReflectionProbesReady() { return graphite_World.reflections.w != 0u; }

// A prefiltered cube's texels along dir, its mip picked by roughness, as they were captured
vec3 SampleProbeCube(uint slot, uint mips, vec3 dir, float roughness)
{
    return GRAPHITE_SAMPLE_CUBE_LOD(slot, graphite_World.reflections.y, dir, roughness * (float(mips) - 1.0)).rgb;
}

// The sky probe's light along dir, pre-exposed
vec3 Graphite_SampleSkyProbe(vec3 dir, float roughness)
{
    return SampleProbeCube(graphite_World.reflections.x, graphite_World.reflections.z, dir, roughness) * (graphite_World.skyScale.x * Graphite_Exposure());
}

// A camera relative point in the probe's box space. offset and offsetLow move camera relative positions into its region.
vec3 Graphite_ProbeLocal(Graphite_ReflectionProbe probe, vec3 relativePos, vec3 offset, vec3 offsetLow)
{
    precise vec3 center = (vec3(probe.row0.w, probe.row1.w, probe.row2.w) - offset) - offsetLow;
    precise vec3 q = relativePos - center;
    return vec3(dot(probe.row0.xyz, q), dot(probe.row1.xyz, q), dot(probe.row2.xyz, q));
}

// Projects the reflection onto the probe's box, so it lands where the reflected surface really is. p is the point in box space.
vec3 SampleReflectionProbe(Graphite_ReflectionProbe probe, vec3 p, vec3 dir, float roughness)
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

    return SampleProbeCube(cube & 0xFFFFu, cube >> 16, dir, roughness) * (probe.params.x * Graphite_Exposure());
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
    uvec2 probe[MAX_BLENDED_PROBES];
    vec3  local[MAX_BLENDED_PROBES];
    float weight[MAX_BLENDED_PROBES];
    int   count;
    float sky;
};

// Keeps the highest priority probes holding the point, weighted by how far inside their boxes it is
Graphite_ProbeSet Graphite_FindProbes(vec3 relativePos)
{
    Graphite_ProbeSet set;
    float priority[MAX_BLENDED_PROBES];
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
        if (count < MAX_BLENDED_PROBES)
            at = count++;
        else if (pr > priority[MAX_BLENDED_PROBES - 1])
            at = MAX_BLENDED_PROBES - 1;
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
        color += SampleReflectionProbe(Graphite_ProbeAt(set.probe[i].x, set.probe[i].y), set.local[i], dir, roughness) * set.weight[i];
    if (set.sky > 0.0)
        color += Graphite_SampleSkyProbe(dir, roughness) * set.sky;
    return color;
}

// The radiance a GGX lobe of alpha around N reflects toward V from the kept probes, before any Fresnel or energy weight
vec3 Graphite_ProbeRadiance(Graphite_ProbeSet set, vec3 N, vec3 V, float alpha)
{
    return Graphite_SampleProbes(set, Graphite_ProbeDirection(N, V, alpha), alpha);
}

// ============================================================
//  Fog
// ============================================================

// How blurred the sky is that fog takes its color from, as a sky probe roughness
#define FOG_SKY_ROUGHNESS 0.35

// The fog color seen toward relativePos, either the flat fog color in nits or the sky behind it from the sky probe, so
// it meets whatever sky is drawn seamlessly, pre-exposed
vec3 FogColor(vec3 relativePos)
{
    if (graphite_World.fogStates.w < 0.5 || !Graphite_ReflectionProbesReady())
        return graphite_World.fogColor.rgb * Graphite_Exposure();

    // Below the horizon the fog keeps the horizon's color rather than the ground under the sky
    vec3 dir = relativePos;
    dir.y = max(dir.y, 0.0);
    dir = length(dir) > 1e-4 ? normalize(dir) : vec3(0.0, 0.0, 1.0);
    return Graphite_SampleSkyProbe(dir, FOG_SKY_ROUGHNESS);
}

vec3 Graphite_ApplyFog(vec3 color, vec3 relativePos)
{
    vec4 states = graphite_World.fogStates;
    if (states.x + states.y + states.z < 0.5)
        return color;

    vec4 params = graphite_World.fogParams;
    float fogCoord = length(relativePos);
    float prowlFog = 0.0;
    prowlFog += (fogCoord * params.z + params.w) * states.x;
    prowlFog += exp2(-fogCoord * params.y) * states.y;
    prowlFog += exp2(-fogCoord * fogCoord * params.x * params.x) * states.z;
    return mix(FogColor(relativePos), color, clamp(prowlFog, 0.0, 1.0));
}
