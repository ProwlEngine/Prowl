// Forward lighting: local lights from the H-Forward trees, directional lights from the world's buffer with the sun first,
// ambient light and fog. Points and spots are Prowl's, the same math. Spheres, tubes, rects and disks light diffuse by
// their irradiance and specular from their most representative point, the lobe widened by the light's size so their
// energy stays right. Every light becomes a Graphite_LightContribution shaded by Graphite_BRDF, which a shader may
// supply itself by defining GRAPHITE_CUSTOM_BRDF before including this.
//
// worldPos is camera relative, as every world position in Graphite shaders is, so the camera sits at the origin.
// Shadows come with the shadow system; until then lights are unshadowed.
#pragma once

#include "Graphite/Trees.glsl"
#include "Graphite/PBR.glsl"
#include "Graphite/Textures.glsl"

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
    vec4 shape;              // rect height, source radius, diffuse scale, specular scale
    vec4 spotShadow;         // cos of the outer and inner spot angles, shadow depth and normal bias
    vec4 shadow;             // shadow strength, shadow quality, shadow slot as int bits, layer mask as uint bits
    uvec4 textures;          // cookie, profile and sampler slots
    vec4 hostData;
};

struct Graphite_DirectionalRecord
{
    vec4 directionShadow;    // surface to light direction, shadow enabled
    vec4 color;              // colour times intensity, layer mask as uint bits
    vec4 params;             // sin and cos of the angular radius, diffuse scale, specular scale
    vec4 hostData;
};

layout(set = 1, binding = 27, std430) readonly buffer Graphite_Directionals { Graphite_DirectionalRecord graphite_Directionals[]; };
layout(set = 1, binding = 22, std430) readonly buffer Graphite_WorldLighting
{
    uvec4 counts;            // directional lights, unused, probes present, unused
    uvec4 reflections;       // the sky probe's cube slot, the probes' sampler slot, the sky probe's mips, whether it is captured
    vec4 ambientColor;
    vec4 ambientSky;
    vec4 ambientGround;
    vec4 ambientMode;        // uniform, hemisphere, strength, sky
    vec4 fogColor;
    vec4 fogParams;
    vec4 fogStates;          // linear, exponential, exponential squared, use sky
} graphite_World;

struct LightSample
{
    vec3  Position;          // camera relative
    float Range;
    vec3  Color;
    float Intensity;
    vec3  Direction;
    int   Type;              // 1 point, 2 spot, 3 rect, 4 disk, 5 tube
    vec3  Up;
    float SourceRadius;
    vec2  Size;
    float DiffuseScale;
    float SpecularScale;
    float SpotCos;           // cos(outer)
    float InnerSpotCos;      // cos(inner)
    float ShadowDepthBias;
    float ShadowNormalBias;
    float ShadowStrength;
    float ShadowQuality;
    int   ShadowSlot;        // -1 if no atlas slot this frame
    int   ShadowEnabled;     // 0 / 1
    uint  Flags;             // cookie 32, profile 64
    uvec3 Textures;          // cookie, profile and sampler slots
    vec4  HostData;
};

// A point light at a camera relative position, every other field at its default, for shaders building lights themselves
LightSample Graphite_NewLightSample(vec3 position, float range, vec3 color, float intensity)
{
    LightSample L;
    L.Position = position;
    L.Range = range;
    L.Color = color;
    L.Intensity = intensity;
    L.Direction = vec3(0.0, 0.0, 1.0);
    L.Type = GRAPHITE_LIGHT_POINT;
    L.Up = vec3(0.0, 1.0, 0.0);
    L.SourceRadius = 0.0;
    L.Size = vec2(0.0);
    L.DiffuseScale = 1.0;
    L.SpecularScale = 1.0;
    L.SpotCos = -1.0;
    L.InnerSpotCos = 1.0;
    L.ShadowDepthBias = 0.0;
    L.ShadowNormalBias = 0.0;
    L.ShadowStrength = 0.0;
    L.ShadowQuality = 0.0;
    L.ShadowSlot = -1;
    L.ShadowEnabled = 0;
    L.Flags = 0u;
    L.Textures = uvec3(0u);
    L.HostData = vec4(0.0);
    return L;
}

float Graphite_LocalLightShadow(LightSample L, vec3 worldPos, vec3 geomNormal) { return 0.0; }

float Graphite_SunShadow(vec3 worldPos, vec3 geomNormal) { return 0.0; }

Graphite_LightRecord Graphite_LightAt(uint array, uint index)
{
    uint w = index * 9u;
    Graphite_LightRecord r;
    r.positionRange = Graphite_SpatialVec4(array, w);
    r.colorIntensity = Graphite_SpatialVec4(array, w + 1u);
    r.directionType = Graphite_SpatialVec4(array, w + 2u);
    r.upSize = Graphite_SpatialVec4(array, w + 3u);
    r.shape = Graphite_SpatialVec4(array, w + 4u);
    r.spotShadow = Graphite_SpatialVec4(array, w + 5u);
    r.shadow = Graphite_SpatialVec4(array, w + 6u);
    r.textures = Graphite_SpatialUVec4(array, w + 7u);
    r.hostData = Graphite_SpatialVec4(array, w + 8u);
    return r;
}

// Reads a light when the camera relative point is inside its sphere and the surface is on one of its layers. offset and
// offsetLow move camera relative positions into the light's tree.
bool Graphite_FetchLight(uint array, uint index, vec3 worldPos, vec3 offset, vec3 offsetLow, uint layers, out LightSample L)
{
    vec4 t0 = Graphite_SpatialVec4(array, index * 9u);
    precise vec3 position = (t0.xyz - offset) - offsetLow;
    vec3 d = worldPos - position;
    if (dot(d, d) > t0.w * t0.w)
        return false;

    Graphite_LightRecord r = Graphite_LightAt(array, index);
    if ((floatBitsToUint(r.shadow.w) & layers) == 0u)
        return false;

    uint flags = floatBitsToUint(r.directionType.w);
    L.Position = position;
    L.Range = t0.w;
    L.Color = r.colorIntensity.rgb;
    L.Intensity = r.colorIntensity.w;
    L.Direction = r.directionType.xyz;
    L.Type = int(flags & 15u);
    L.Up = r.upSize.xyz;
    L.Size = vec2(r.upSize.w, r.shape.x);
    L.SourceRadius = r.shape.y;
    L.DiffuseScale = r.shape.z;
    L.SpecularScale = r.shape.w;
    L.ShadowEnabled = int((flags >> 4) & 1u);
    L.SpotCos = r.spotShadow.x;
    L.InnerSpotCos = r.spotShadow.y;
    L.ShadowDepthBias = r.spotShadow.z;
    L.ShadowNormalBias = r.spotShadow.w;
    L.ShadowStrength = r.shadow.x;
    L.ShadowQuality = r.shadow.y;
    L.ShadowSlot = floatBitsToInt(r.shadow.z);
    L.Flags = flags;
    L.Textures = r.textures.xyz;
    L.HostData = r.hostData;
    return true;
}

// Walks the static light regions the view sees, then its tree of moving lights, returning every light whose sphere holds the point
struct Graphite_LightIterator
{
    Graphite_TreeQuery query;
    uint tree;
    uint k;
    uint last;
    uint layers;
};

Graphite_LightIterator Graphite_BeginLights(vec3 worldPos)
{
    Graphite_LightIterator it;
    Graphite_BeginQuery(it.query, worldPos, GRAPHITE_SET_LIGHTS);
    it.tree = 0u;
    it.k = 0u;
    it.last = 0u;
    it.layers = Graphite_SurfaceLayers();
    return it;
}

bool Graphite_NextLight(inout Graphite_LightIterator it, out LightSample L)
{
    for (;;)
    {
        while (it.k < it.last)
        {
            uint index = it.k++;
            if (Graphite_FetchLight(it.tree, index, it.query.position, it.query.offset, it.query.offsetLow, it.layers, L))
                return true;
        }

        if (!Graphite_NextRun(it.query, it.tree, it.k, it.last))
            return false;
    }
    return false;
}

struct Graphite_DirectionalLight
{
    vec3  direction;         // surface to light
    vec3  color;             // colour times intensity
    float sinRadius;         // of the disk's angular radius
    float cosRadius;
    bool  shadow;
    uint  layers;
    float diffuseScale;
    float specularScale;
    vec4  hostData;
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
    d.diffuseScale = r.params.z;
    d.specularScale = r.params.w;
    d.hostData = r.hostData;
    return d;
}

// Unit direction from the surface to the camera, world space
vec3 GetWorldViewDir(vec3 worldPos)
{
    return normalize(-worldPos);
}

// Unit direction from the surface to the camera, in tangent space
vec3 GetTangentViewDir(vec3 worldPos, vec3 worldNormal, vec3 worldTangent, vec3 worldBitangent)
{
    vec3 viewDir = GetWorldViewDir(worldPos);
    return normalize(vec3(dot(viewDir, normalize(worldTangent)), dot(viewDir, normalize(worldBitangent)), dot(viewDir, normalize(worldNormal))));
}

// ============================================================
//  Surfaces and what a light gives them
// ============================================================

struct Graphite_Surface
{
    vec3  position;          // camera relative
    vec3  normal;            // shading normal
    vec3  geomNormal;        // before normal mapping, for shadow offsets
    vec3  view;              // to the camera
    vec3  albedo;
    float metallic;
    float roughness;         // Prowl's, the GGX alpha
    float ao;
    vec3  F0;
};

Graphite_Surface Graphite_MakeSurface(vec3 worldPos, vec3 worldNormal, vec3 geomNormal, vec3 viewDir, vec3 albedo, float metallic, float roughness, float ao)
{
    Graphite_Surface s;
    s.position = worldPos;
    s.normal = worldNormal;
    s.geomNormal = geomNormal;
    s.view = viewDir;
    s.albedo = albedo;
    s.metallic = metallic;
    s.roughness = roughness;
    s.ao = ao;
    s.F0 = mix(vec3(0.04), albedo, metallic);
    return s;
}

struct Graphite_LightContribution
{
    vec3  radiance;          // colour times intensity times 8, with shadow, cookie and profile
    vec3  diffuseL;          // unit direction to the light for the diffuse lobe
    float diffuse;           // irradiance per unit radiance: N.L over distance squared for a point, the shape's integral otherwise, windowed and scaled
    vec3  specularL;         // unit direction to the light's most representative point
    float specular;          // N.L of specularL over distance squared, windowed and scaled
    vec2  extent;            // the light's angular extent across two axes, which widens a specular lobe, 0 for a point
    float attenuation;       // the falloff alone, for translucency
};

// Prowl's window, reaching zero at the range
float Graphite_Window(float dist2, float range)
{
    float factor = dist2 / max(range * range, 1e-6);
    float window = clamp(1.0 - factor * factor, 0.0, 1.0);
    return window * window;
}

// The energy of a specular lobe widened by a light's angular extent across two axes, ex and ey, the widths adding in quadrature
float Graphite_AreaEnergy(float roughness, float ex, float ey)
{
    float a = max(roughness, GRAPHITE_MIN_ROUGHNESS);
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

// The irradiance per unit intensity of a line from a to b, relative to the point, its intensity spread evenly along it
float Graphite_LineIrradiance(vec3 a, vec3 b, vec3 n)
{
    vec3 d = b - a;
    float len = length(d);
    if (len < 1e-3)
    {
        vec3 m = 0.5 * (a + b);
        float m2 = max(dot(m, m), 1e-4);
        return max(dot(n, m), 0.0) / (m2 * sqrt(m2));
    }
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

    // The integral of n.(a + s u) / |a + s u|^3 along the line in closed form, from the line's nearest point to the
    // origin, so it stays exact on the line itself
    float bu = dot(a, u);
    vec3 perp = a - u * bu;
    float D = max(dot(perp, perp), 1e-20);
    float np = dot(n, perp);
    float q0 = max(length(a + u * s0), 1e-6), q1 = max(length(a + u * s1), 1e-6);
    float f0 = np * (s0 + bu) / (D * q0) - nu / q0;
    float f1 = np * (s1 + bu) / (D * q1) - nu / q1;
    return max(f1 - f0, 0.0) / len;
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

// The cookie and profile in a point or spot's own space, w the direction from it to the surface
vec3 Graphite_LightMask(LightSample L, vec3 w)
{
    vec3 mask = vec3(1.0);
    if ((L.Flags & 96u) == 0u)
        return mask;
    vec3 right = cross(L.Up, L.Direction);
    vec3 local = vec3(dot(w, right), dot(w, L.Up), dot(w, L.Direction));
    if ((L.Flags & 32u) != 0u)
    {
        if (L.Type == GRAPHITE_LIGHT_SPOT)
        {
            float tanOuter = sqrt(max(1.0 - L.SpotCos * L.SpotCos, 0.0)) / max(L.SpotCos, 1e-4);
            vec2 uv = local.xy / (max(local.z, 1e-4) * tanOuter) * 0.5 + 0.5;
            mask *= GRAPHITE_SAMPLE_2D_LOD(L.Textures.x, L.Textures.z, uv, 0.0).rgb;
        }
        else
        {
            mask *= GRAPHITE_SAMPLE_CUBE_LOD(L.Textures.x, L.Textures.z, local, 0.0).rgb;
        }
    }
    if ((L.Flags & 64u) != 0u)
    {
        // The angle from the direction across U's texel centres, the angle around it from up across V
        float width = float(textureSize(sampler2D(Graphite_Textures2D[nonuniformEXT(L.Textures.y)], Graphite_Samplers[nonuniformEXT(L.Textures.z)]), 0).x);
        float u = acos(clamp(local.z, -1.0, 1.0)) / PI;
        vec2 uv = vec2((u * (width - 1.0) + 0.5) / width, fract(atan(local.x, local.y) / (2.0 * PI)));
        mask *= GRAPHITE_SAMPLE_2D_LOD(L.Textures.y, L.Textures.z, uv, 0.0).r;
    }
    return mask;
}

// What a local light gives a surface. backLit keeps lights behind the surface, for translucency.
bool Graphite_ContributeLocal(LightSample L, Graphite_Surface s, bool backLit, out Graphite_LightContribution c)
{
    vec3 toLight = L.Position - s.position;
    float dist2 = dot(toLight, toLight);
    float dist = sqrt(dist2);
    vec3 Lc = toLight * (1.0 / max(dist, 1e-6));
    float window = Graphite_Window(dist2, L.Range);
    vec3 N = s.normal;
    vec3 R = reflect(-s.view, N);

    c.radiance = L.Color * (L.Intensity * 8.0);
    c.diffuseL = Lc;
    c.specularL = Lc;
    c.extent = vec2(0.0);

    if (L.Type == GRAPHITE_LIGHT_POINT || L.Type == GRAPHITE_LIGHT_SPOT)
    {
        float cone = 1.0;
        if (L.Type == GRAPHITE_LIGHT_SPOT)
        {
            float axisCos = dot(normalize(L.Direction), -Lc);
            if (axisCos <= L.SpotCos)
                return false;
            cone = smoothstep(L.SpotCos, L.InnerSpotCos, axisCos);
        }

        float NdotL = dot(N, Lc);
        bool sphere = L.SourceRadius > 1e-4;
        if (NdotL <= 0.0 && !backLit && !sphere)
            return false;

        c.attenuation = window / max(dist2, 0.01) * cone;
        if (c.attenuation <= 0.0001)
            return false;

        if (!sphere)
        {
            c.diffuse = max(NdotL, 0.0) * c.attenuation;
            c.specular = c.diffuse;
        }
        else
        {
            float r = L.SourceRadius;
            c.diffuse = Graphite_SphereIrradiance(NdotL, min(r * r / max(dist2, 1e-8), 1.0)) / (PI * r * r) * window * cone;
            vec3 p = Graphite_SpherePoint(toLight, r, R);
            c.specularL = dot(p, p) > 1e-12 ? normalize(p) : N;
            c.specular = max(dot(N, c.specularL), 0.0) * c.attenuation;
            c.extent = vec2(r / (2.0 * max(dist, 1e-4)));
        }
        c.radiance *= Graphite_LightMask(L, -Lc);
    }
    else if (L.Type == GRAPHITE_LIGHT_TUBE)
    {
        c.attenuation = window / max(dist2, 0.01);
        vec3 axis = normalize(L.Direction) * (0.5 * L.Size.x);
        vec3 a = toLight - axis, b = toLight + axis;
        vec3 ab = b - a;
        float t = clamp(-dot(a, ab) / max(dot(ab, ab), 1e-8), 0.0, 1.0);
        vec3 nearest = a + ab * t;
        c.diffuseL = dot(nearest, nearest) > 1e-12 ? normalize(nearest) : N;
        c.diffuse = Graphite_LineIrradiance(a, b, N) * window;

        // The point of the segment nearest the reflected ray, then of the capsule around it
        float rab = dot(R, ab);
        float tr = clamp((dot(R, a) * rab - dot(a, ab)) / max(dot(ab, ab) - rab * rab, 1e-8), 0.0, 1.0);
        vec3 onLine = a + ab * tr;
        vec3 p = Graphite_SpherePoint(onLine, L.SourceRadius, R);
        c.specularL = dot(p, p) > 1e-12 ? normalize(p) : N;
        float lineDist = max(length(onLine), 1e-4);
        c.specular = max(dot(N, c.specularL), 0.0) * window / max(dot(onLine, onLine), 0.01);
        c.extent = vec2(L.SourceRadius / (2.0 * lineDist), L.Size.x / (PI * lineDist));
    }
    else
    {
        // Rects and disks shine from their front, the side their direction faces
        vec3 n = normalize(L.Direction);
        float front = dot(-Lc, n);
        if (front <= 0.0)
            return false;
        c.attenuation = window * front / max(dist2, 0.01);
        vec3 right = cross(L.Up, n);
        bool disk = L.Type == GRAPHITE_LIGHT_DISK;
        vec2 halfSize = disk ? vec2(L.Size.x) : 0.5 * L.Size;
        float area = disk ? PI * halfSize.x * halfSize.x : L.Size.x * L.Size.y;
        if (area <= 0.0)
            return false;

        if (disk)
        {
            float r2 = halfSize.x * halfSize.x;
            c.diffuse = Graphite_SphereIrradiance(dot(N, Lc), r2 / (r2 + dist2)) * front / area * window;
        }
        else
        {
            vec3 hx = right * halfSize.x, hy = L.Up * halfSize.y;
            c.diffuse = Graphite_QuadIrradiance(toLight - hx - hy, toLight + hx - hy, toLight + hx + hy, toLight - hx + hy, N) / area * window;
        }

        vec3 p = Graphite_PlanePoint(toLight, n, right, L.Up, halfSize, disk, R);
        float pDist2 = max(dot(p, p), 1e-8);
        float pDist = sqrt(pDist2);
        c.specularL = p / pDist;
        float pFront = max(dot(-c.specularL, n), 0.0);
        c.extent = disk ? vec2(halfSize.x / (2.0 * pDist)) : L.Size / (2.0 * sqrt(PI) * pDist);
        c.specular = max(dot(N, c.specularL), 0.0) * pFront * window / max(pDist2, 0.01);
    }

    c.diffuse *= L.DiffuseScale;
    c.specular *= L.SpecularScale;
    if (c.diffuse <= 0.0 && c.specular <= 0.0 && !backLit)
        return false;

#ifndef SG_NO_SHADOWS
    c.radiance *= 1.0 - Graphite_LocalLightShadow(L, s.position, s.geomNormal);
#endif
    return true;
}

float MainDirectionalShadowFactor(vec3 worldPos, vec3 geomNormal)
{
#ifdef SG_NO_SHADOWS
    return 1.0;
#else
    return 1.0 - Graphite_SunShadow(worldPos, geomNormal);
#endif
}

// What a directional light gives a surface, its specular from the nearest direction inside its disk. index 0 is the sun.
bool Graphite_ContributeDirectional(Graphite_DirectionalLight d, uint index, Graphite_Surface s, out Graphite_LightContribution c)
{
    if ((d.layers & Graphite_SurfaceLayers()) == 0u)
        return false;
    float shadow = index == 0u ? MainDirectionalShadowFactor(s.position, s.geomNormal) : 1.0;
    float NdotL = max(dot(s.normal, d.direction), 0.0);
    c.radiance = d.color * 8.0 * shadow;
    c.diffuseL = d.direction;
    c.diffuse = NdotL * d.diffuseScale;
    c.attenuation = 1.0;
    c.specularL = d.direction;
    c.specular = NdotL;
    c.extent = vec2(0.0);
    if (d.sinRadius > 0.0)
    {
        vec3 R = reflect(-s.view, s.normal);
        float RdotL = dot(R, d.direction);
        vec3 across = R - d.direction * RdotL;
        if (RdotL < d.cosRadius && dot(across, across) > 1e-8)
            c.specularL = normalize(d.direction * d.cosRadius + normalize(across) * d.sinRadius);
        else if (RdotL >= d.cosRadius)
            c.specularL = R;
        c.specular = max(dot(s.normal, c.specularL), 0.0);
        c.extent = vec2(0.5 * d.sinRadius);
    }
    c.specular *= d.specularScale;
    return true;
}

// ============================================================
//  BRDF
// ============================================================

// Prowl's BRDF: GGX, Smith and Fresnel for specular, Disney diffuse, each lobe from its own light direction
vec3 Graphite_DefaultBRDF(Graphite_Surface s, Graphite_LightContribution c)
{
    float NdotV = abs(dot(s.normal, s.view));

    vec3 Hs = normalize(c.specularL + s.view);
    float NdotLs = max(dot(s.normal, c.specularL), 0.0);
    vec3 F = FresnelSchlick(max(dot(c.specularL, Hs), 0.0), s.F0);
    vec3 specular = DistributionGGX(s.normal, Hs, s.roughness) * GeometrySmith(s.normal, s.view, c.specularL, s.roughness) * F
        / (4.0 * NdotV * NdotLs + 0.0001);

    vec3 Hd = normalize(c.diffuseL + s.view);
    vec3 kD = (vec3(1.0) - F) * (1.0 - s.metallic);
    vec3 diffuse = kD * s.albedo * DisneyDiffuse(NdotV, max(dot(s.normal, c.diffuseL), 0.0), max(dot(c.diffuseL, Hd), 0.0), s.roughness);

    return (diffuse * c.diffuse + specular * (c.specular * Graphite_AreaEnergy(s.roughness, c.extent.x, c.extent.y))) * c.radiance * s.ao;
}

// The BRDF every light goes through. Define GRAPHITE_CUSTOM_BRDF and write this function to shade with your own.
vec3 Graphite_BRDF(Graphite_Surface s, Graphite_LightContribution c);

#ifndef GRAPHITE_CUSTOM_BRDF
vec3 Graphite_BRDF(Graphite_Surface s, Graphite_LightContribution c)
{
    return Graphite_DefaultBRDF(s, c);
}
#endif

// Every directional and local light on a surface through Graphite_BRDF
vec3 Graphite_ShadeLights(Graphite_Surface s)
{
    vec3 total = vec3(0.0);
    Graphite_LightContribution c;
    for (uint i = 0u; i < Graphite_DirectionalCount(); i++)
    {
        if (Graphite_ContributeDirectional(Graphite_GetDirectional(i), i, s, c))
            total += Graphite_BRDF(s, c);
    }

    Graphite_LightIterator it = Graphite_BeginLights(s.position);
    LightSample L;
    while (Graphite_NextLight(it, L))
    {
        if (Graphite_ContributeLocal(L, s, false, c))
            total += Graphite_BRDF(s, c);
    }
    return total;
}

// ============================================================
//  Prowl's entry points
// ============================================================

vec3 EvaluateLocalLight(LightSample L, vec3 worldPos, vec3 worldNormal, vec3 geomNormal, vec3 viewDir,
                        vec3 albedo, float metallic, float roughness, float ao, vec3 F0)
{
    Graphite_Surface s = Graphite_MakeSurface(worldPos, worldNormal, geomNormal, viewDir, albedo, metallic, roughness, ao);
    s.F0 = F0;
    Graphite_LightContribution c;
    return Graphite_ContributeLocal(L, s, false, c) ? Graphite_BRDF(s, c) : vec3(0.0);
}

// Prowl's anisotropic BRDF on a light's contribution
vec3 Graphite_AnisoBRDF(Graphite_Surface s, Graphite_LightContribution c, vec3 worldTangent, vec3 worldBitangent, float mt, float mb)
{
    vec3 lightDir = c.specularL;
    vec3 halfDir = normalize(lightDir + s.view);
    float NdotL = max(dot(s.normal, lightDir), 0.0);
    float NdotV = abs(dot(s.normal, s.view));
    float LdotH = max(dot(lightDir, halfDir), 0.0);
    float NdotH = max(dot(s.normal, halfDir), 0.0);

    float D = DistributionGGXAniso(dot(worldTangent, halfDir), dot(worldBitangent, halfDir), NdotH, mt, mb);
    float V = GeometrySmithAniso(dot(worldTangent, s.view), dot(worldBitangent, s.view), NdotV,
        dot(worldTangent, lightDir), dot(worldBitangent, lightDir), NdotL, mt, mb);
    vec3 F = FresnelSchlick(LdotH, s.F0);

    vec3 Hd = normalize(c.diffuseL + s.view);
    vec3 kD = (vec3(1.0) - F) * (1.0 - s.metallic);
    float diffuse = DisneyDiffuse(NdotV, max(dot(s.normal, c.diffuseL), 0.0), max(dot(c.diffuseL, Hd), 0.0), s.roughness);

    // The lobe widened by the light's extent along its own two axes
    float at = max(mt, GRAPHITE_MIN_ROUGHNESS * GRAPHITE_MIN_ROUGHNESS), ab = max(mb, GRAPHITE_MIN_ROUGHNESS * GRAPHITE_MIN_ROUGHNESS);
    float energy = at * ab / (min(sqrt(at * at + c.extent.x * c.extent.x), 1.0) * min(sqrt(ab * ab + c.extent.y * c.extent.y), 1.0));
    return (kD * s.albedo * diffuse * c.diffuse + max(0.0, V * D) * PI * F * (c.specular * energy)) * c.radiance * s.ao;
}

vec3 EvaluateLocalLightAniso(LightSample L, vec3 worldPos, vec3 worldNormal, vec3 geomNormal, vec3 viewDir,
                              vec3 worldTangent, vec3 worldBitangent,
                              vec3 albedo, float metallic, float mt, float mb,
                              float perceptualRoughness, float ao, vec3 F0)
{
    Graphite_Surface s = Graphite_MakeSurface(worldPos, worldNormal, geomNormal, viewDir, albedo, metallic, perceptualRoughness, ao);
    s.F0 = F0;
    Graphite_LightContribution c;
    return Graphite_ContributeLocal(L, s, false, c) ? Graphite_AnisoBRDF(s, c, worldTangent, worldBitangent, mt, mb) : vec3(0.0);
}

// One directional light. lightColor is color * intensity.
vec3 ShadeDirectional(vec3 lightDir, vec3 lightColor, float shadowFactor, vec3 worldNormal, vec3 viewDir,
                      vec3 albedo, float metallic, float roughness, float ao, vec3 F0)
{
    Graphite_Surface s = Graphite_MakeSurface(vec3(0.0), worldNormal, worldNormal, viewDir, albedo, metallic, roughness, ao);
    s.F0 = F0;
    Graphite_LightContribution c;
    float NdotL = max(dot(worldNormal, lightDir), 0.0);
    c.radiance = lightColor * 8.0 * shadowFactor;
    c.diffuseL = lightDir;
    c.specularL = lightDir;
    c.diffuse = NdotL;
    c.specular = NdotL;
    c.attenuation = 1.0;
    c.extent = vec2(0.0);
    return Graphite_BRDF(s, c);
}

vec3 EvaluateDirectional(vec3 worldPos, vec3 worldNormal, vec3 geomNormal, vec3 viewDir,
                         vec3 albedo, float metallic, float roughness, float ao, vec3 F0)
{
    Graphite_Surface s = Graphite_MakeSurface(worldPos, worldNormal, geomNormal, viewDir, albedo, metallic, roughness, ao);
    s.F0 = F0;
    vec3 total = vec3(0.0);
    Graphite_LightContribution c;
    for (uint i = 0u; i < Graphite_DirectionalCount(); i++)
    {
        if (Graphite_ContributeDirectional(Graphite_GetDirectional(i), i, s, c))
            total += Graphite_BRDF(s, c);
    }
    return total;
}

// geomNormal is the interpolated surface normal before normal mapping, used to offset shadow lookups
vec3 CalculateForwardLighting(vec3 worldPos, vec3 worldNormal, vec3 geomNormal, vec3 viewDir,
                              vec3 albedo, float metallic, float roughness, float ao)
{
    roughness = ApplySpecularAA(roughness, worldNormal);
    return Graphite_ShadeLights(Graphite_MakeSurface(worldPos, worldNormal, geomNormal, viewDir, albedo, metallic, roughness, ao));
}

vec3 CalculateForwardLightingAniso(vec3 worldPos, vec3 worldNormal, vec3 geomNormal, vec3 viewDir,
                                    vec3 worldTangent, vec3 worldBitangent,
                                    vec3 albedo, float metallic,
                                    float roughness, float anisotropy, float ao)
{
    roughness = ApplySpecularAA(roughness, worldNormal);
    float roughnessT = roughness * (1.0 + anisotropy);
    float roughnessB = roughness * (1.0 - anisotropy);
    float mt = roughnessT * roughnessT;
    float mb = roughnessB * roughnessB;
    Graphite_Surface s = Graphite_MakeSurface(worldPos, worldNormal, geomNormal, viewDir, albedo, metallic, roughness, ao);

    vec3 total = vec3(0.0);
    Graphite_LightContribution c;
    for (uint i = 0u; i < Graphite_DirectionalCount(); i++)
    {
        if (Graphite_ContributeDirectional(Graphite_GetDirectional(i), i, s, c))
            total += Graphite_AnisoBRDF(s, c, worldTangent, worldBitangent, mt, mb);
    }

    Graphite_LightIterator it = Graphite_BeginLights(worldPos);
    LightSample L;
    while (Graphite_NextLight(it, L))
    {
        if (Graphite_ContributeLocal(L, s, false, c))
            total += Graphite_AnisoBRDF(s, c, worldTangent, worldBitangent, mt, mb);
    }
    return total;
}

// A contribution with translucency: the BRDF where the light is in front, scattering through wherever it is
vec3 Graphite_TranslucentLight(Graphite_Surface s, Graphite_LightContribution c,
                               float translucency, float scatterPower, float scatterDist, float scatterScale)
{
    vec3 result = Graphite_BRDF(s, c);
    if (translucency > 0.0)
    {
        vec3 scatter = CalculateTranslucency(c.diffuseL, s.view, s.normal, translucency, scatterPower, scatterDist, scatterScale,
            c.radiance * (c.attenuation / 8.0));
        result += scatter * s.albedo;
    }
    return result;
}

vec3 EvaluateLocalLightTranslucent(LightSample L, vec3 worldPos, vec3 worldNormal, vec3 geomNormal, vec3 viewDir,
                                    vec3 albedo, float metallic, float roughness, float ao, vec3 F0,
                                    float translucency, float scatterPower, float scatterDist, float scatterScale)
{
    Graphite_Surface s = Graphite_MakeSurface(worldPos, worldNormal, geomNormal, viewDir, albedo, metallic, roughness, ao);
    s.F0 = F0;
    Graphite_LightContribution c;
    return Graphite_ContributeLocal(L, s, true, c) ? Graphite_TranslucentLight(s, c, translucency, scatterPower, scatterDist, scatterScale) : vec3(0.0);
}

// Forward lighting with translucency: the BRDF and scattering share each light's attenuation and shadow
vec3 CalculateForwardLighting(vec3 worldPos, vec3 worldNormal, vec3 geomNormal, vec3 viewDir,
                              vec3 albedo, float metallic, float roughness, float ao,
                              float translucency, float scatterPower,
                              float scatterDist, float scatterScale)
{
    roughness = ApplySpecularAA(roughness, worldNormal);
    Graphite_Surface s = Graphite_MakeSurface(worldPos, worldNormal, geomNormal, viewDir, albedo, metallic, roughness, ao);

    vec3 total = vec3(0.0);
    Graphite_LightContribution c;
    for (uint i = 0u; i < Graphite_DirectionalCount(); i++)
    {
        if (Graphite_ContributeDirectional(Graphite_GetDirectional(i), i, s, c))
            total += Graphite_TranslucentLight(s, c, translucency, scatterPower, scatterDist, scatterScale);
    }

    Graphite_LightIterator it = Graphite_BeginLights(worldPos);
    LightSample L;
    while (Graphite_NextLight(it, L))
    {
        if (Graphite_ContributeLocal(L, s, true, c))
            total += Graphite_TranslucentLight(s, c, translucency, scatterPower, scatterDist, scatterScale);
    }
    return total;
}

// Shading normal doubles as the shadow normal, for surfaces without a separate geometric normal
vec3 CalculateForwardLighting(vec3 worldPos, vec3 worldNormal, vec3 viewDir,
                              vec3 albedo, float metallic, float roughness, float ao)
{
    return CalculateForwardLighting(worldPos, worldNormal, worldNormal, viewDir, albedo, metallic, roughness, ao);
}

vec3 CalculateForwardLightingAniso(vec3 worldPos, vec3 worldNormal, vec3 viewDir,
                                    vec3 worldTangent, vec3 worldBitangent,
                                    vec3 albedo, float metallic,
                                    float roughness, float anisotropy, float ao)
{
    return CalculateForwardLightingAniso(worldPos, worldNormal, worldNormal, viewDir, worldTangent, worldBitangent,
                                         albedo, metallic, roughness, anisotropy, ao);
}

vec3 CalculateForwardLighting(vec3 worldPos, vec3 worldNormal, vec3 viewDir,
                              vec3 albedo, float metallic, float roughness, float ao,
                              float translucency, float scatterPower,
                              float scatterDist, float scatterScale)
{
    return CalculateForwardLighting(worldPos, worldNormal, worldNormal, viewDir, albedo, metallic, roughness, ao,
                                    translucency, scatterPower, scatterDist, scatterScale);
}

// ============================================================
//  Ambient lighting
// ============================================================

vec3 Graphite_SkyAmbient(vec3 worldNormal);

vec3 CalculateAmbient(vec3 worldNormal)
{
    vec3 ambient = graphite_World.ambientColor.rgb * graphite_World.ambientMode.x;
    if (graphite_World.ambientMode.w > 0.0)
        ambient += Graphite_SkyAmbient(worldNormal) * graphite_World.ambientMode.w;

    float upDot = dot(worldNormal, vec3(0.0, 1.0, 0.0));
    ambient += mix(graphite_World.ambientGround.rgb, graphite_World.ambientSky.rgb, upDot * 0.5 + 0.5) * graphite_World.ambientMode.y;

    return ambient;
}

// ============================================================
//  Baked lighting
// ============================================================

struct Graphite_ProbeSH { vec4 shAr; vec4 shAg; vec4 shAb; vec4 shBr; vec4 shBg; vec4 shBb; vec4 shC; };

layout(set = 1, binding = 23, std430) readonly buffer Graphite_ObjectProbes { Graphite_ProbeSH graphite_ObjectProbes[]; };

#define GRAPHITE_NO_LIGHTMAP 0xFFFFFFFFu
#define GRAPHITE_LIGHTMAP_LOADING 0xFFFFFFFEu

// A lightmap texel as light: as is, or colour times alpha times 8 for RGBM
vec3 DecodeLightmap(vec4 texel, uint encoding)
{
    return encoding == 1u ? texel.rgb * (texel.a * 8.0) : texel.rgb;
}

// Diffuse irradiance over pi for a normal from packed order 2 harmonics
vec3 ShadeSH9(Graphite_ProbeSH sh, vec3 n)
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

// The sky's light along a normal, from its harmonics
vec3 Graphite_SkyAmbient(vec3 worldNormal)
{
    return ShadeSH9(graphite_SkyAmbient, worldNormal);
}

#if defined(GRAPHITE_FRAGMENT) && !defined(GRAPHITE_NO_OBJECT)
// The diffuse light that is not realtime: the object's lightmap, ambient while it loads, else its light probes, else the
// ambient light.
// lightmapUV comes from Graphite_LightmapUV in the vertex stage.
vec3 Graphite_IndirectDiffuse(vec3 worldNormal, vec2 lightmapUV)
{
    Graphite_ObjectData object = Graphite_Object();
    if (object.lightmapTexture == GRAPHITE_LIGHTMAP_LOADING)
        return CalculateAmbient(worldNormal) * graphite_World.ambientMode.z;
    if (object.lightmapTexture != GRAPHITE_NO_LIGHTMAP)
        return DecodeLightmap(GRAPHITE_SAMPLE_2D(object.lightmapTexture, object.lightmapSampler & 0xFFFFu, lightmapUV), object.lightmapSampler >> 16);
    if (object.probeIndex != 0xFFFFFFFFu && graphite_World.counts.z != 0u)
        return ShadeSH9(graphite_ObjectProbes[object.probeIndex], worldNormal);
    return CalculateAmbient(worldNormal) * graphite_World.ambientMode.z;
}
#endif

float SpecularOcclusion(float NdotV, float ao, float roughness)
{
    return clamp(pow(NdotV + ao, exp2(-16.0 * roughness - 1.0)) - 1.0 + ao, 0.0, 1.0);
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
    vec4 params;             // intensity, box projection, priority, the cube's slot and mips (slot | mips << 16) as uint bits
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

#define MAX_BLENDED_PROBES 4

bool ReflectionProbesReady() { return graphite_World.reflections.w != 0u; }

// A prefiltered cube's light along dir, its mip picked by roughness
vec3 SampleProbeCube(uint slot, uint mips, vec3 dir, float roughness)
{
    return GRAPHITE_SAMPLE_CUBE_LOD(slot, graphite_World.reflections.y, dir, roughness * (float(mips) - 1.0)).rgb;
}

vec3 SampleSkyProbe(vec3 dir, float roughness)
{
    return SampleProbeCube(graphite_World.reflections.x, graphite_World.reflections.z, dir, roughness);
}

// A camera relative point in the probe's box space. offset and offsetLow move camera relative positions into its region.
vec3 Graphite_ProbeLocal(Graphite_ReflectionProbe probe, vec3 worldPos, vec3 offset, vec3 offsetLow)
{
    precise vec3 center = (vec3(probe.row0.w, probe.row1.w, probe.row2.w) - offset) - offsetLow;
    precise vec3 q = worldPos - center;
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

    return SampleProbeCube(cube & 0xFFFFu, cube >> 16, dir, roughness) * probe.params.x;
}

// Walks the reflection probes whose box holds the point, giving each with the point in its box space
struct Graphite_ProbeIterator
{
    Graphite_TreeQuery query;
    uint tree;
    uint k;
    uint last;
};

Graphite_ProbeIterator Graphite_BeginProbes(vec3 worldPos)
{
    Graphite_ProbeIterator it;
    Graphite_BeginQuery(it.query, worldPos, GRAPHITE_SET_PROBES);
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

// Blends the most important probes around the point by how far inside their boxes it is, the sky filling the rest
vec3 EvaluateReflectionProbes(vec3 worldPos, vec3 N, vec3 V, float roughness)
{
    // Rough lobes lean toward the normal
    float a = roughness * roughness;
    vec3 R = normalize(mix(N, reflect(-V, N), (1.0 - a) * (sqrt(1.0 - a) + a)));

    Graphite_ReflectionProbe kept[MAX_BLENDED_PROBES];
    vec3 local[MAX_BLENDED_PROBES];
    float weight[MAX_BLENDED_PROBES];
    float priority[MAX_BLENDED_PROBES];
    int count = 0;

    Graphite_ProbeIterator it = Graphite_BeginProbes(worldPos);
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

        kept[at] = probe;
        local[at] = p;
        weight[at] = w;
        priority[at] = pr;
        for (int j = at; j > 0 && priority[j] > priority[j - 1]; j--)
        {
            Graphite_ReflectionProbe tk = kept[j]; kept[j] = kept[j - 1]; kept[j - 1] = tk;
            vec3 tl = local[j]; local[j] = local[j - 1]; local[j - 1] = tl;
            float tw = weight[j]; weight[j] = weight[j - 1]; weight[j - 1] = tw;
            float tp = priority[j]; priority[j] = priority[j - 1]; priority[j - 1] = tp;
        }
    }

    vec3 color = vec3(0.0);
    float total = 0.0;
    for (int i = 0; i < count && total < 0.999; i++)
    {
        float w = weight[i] * (1.0 - total);
        color += SampleReflectionProbe(kept[i], local[i], R, roughness) * w;
        total += w;
    }
    if (total < 0.999)
        color += SampleSkyProbe(R, roughness) * (1.0 - total);
    return color;
}

// Specular light from the environment: the reflection probes once the sky is captured, else an approximation from the
// ambient light, Fresnel at the view angle and a roughness falloff, as the Standard shader does. ambientLight is the
// indirect diffuse light times occlusion.
vec3 Graphite_IndirectSpecular(vec3 worldPos, vec3 N, vec3 V, vec3 F0, float roughness, float ao, vec3 ambientLight)
{
    float NdotV = max(dot(N, V), 0.0);
    vec3 F = EnvBRDF(F0, roughness, NdotV);
    if (ReflectionProbesReady())
        return EvaluateReflectionProbes(worldPos, N, V, roughness) * F * SpecularOcclusion(NdotV, ao, roughness);
    float specOcclusion = 1.0 - roughness * roughness;
    return ambientLight * F * mix(specOcclusion, 1.0, 0.25);
}

// ============================================================
//  Fog
// ============================================================

// How blurred the sky is that fog takes its color from, as a sky probe roughness
#define FOG_SKY_ROUGHNESS 0.35

// The fog color seen toward worldPos, either the flat fog color or the sky behind it from the sky probe, so it
// meets whatever sky is drawn seamlessly
vec3 FogColor(vec3 worldPos)
{
    if (graphite_World.fogStates.w < 0.5 || !ReflectionProbesReady())
        return graphite_World.fogColor.rgb;

    // Below the horizon the fog keeps the horizon's color rather than the ground under the sky
    vec3 dir = worldPos;
    dir.y = max(dir.y, 0.0);
    dir = length(dir) > 1e-4 ? normalize(dir) : vec3(0.0, 0.0, 1.0);
    return SampleSkyProbe(dir, FOG_SKY_ROUGHNESS);
}

vec3 ApplyFog(vec3 color, vec3 worldPos)
{
    vec4 states = graphite_World.fogStates;
    if (states.x + states.y + states.z < 0.5)
        return color;

    vec4 params = graphite_World.fogParams;
    float fogCoord = length(worldPos);
    float prowlFog = 0.0;
    prowlFog += (fogCoord * params.z + params.w) * states.x;
    prowlFog += exp2(-fogCoord * params.y) * states.y;
    prowlFog += exp2(-fogCoord * fogCoord * params.x * params.x) * states.z;
    return mix(FogColor(worldPos), color, clamp(prowlFog, 0.0, 1.0));
}
