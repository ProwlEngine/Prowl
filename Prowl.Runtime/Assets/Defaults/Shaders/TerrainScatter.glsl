// Procedural terrain detail placement, vertex stage only.
//
// Every blade is derived from its own cell on a fixed world grid, so its position, size and
// orientation depend only on where it is, never on where the camera is. Cascades sample every
// 2^level-th cell of that same grid, which makes each one a strict subset of the cascade inside
// it: distance thins the field out, it never moves a blade.
//
// A cascade's own values come from its draw item: custom0 is the fine grid cell of its lower left
// corner (xy), instances per side (z) and its level (w), custom1 its centre in terrain local XZ (xy)
// with the inner and outer radius of its band (zw).
#pragma once

#include "Graphite/Vertex.glsl"
#include "Prowl/TerrainHeight.glsl"

uniform sampler2D _HolesMap;
uniform int _HasHoles;
uniform float _ScatterCellSize;    // world size of one fine cell
uniform int _ScatterMaxLevel;      // level of the outermost ring
uniform sampler2D _DetailMap;
uniform int _DetailChannel;

// How far a blade may wander, as a power of two in fine cells. Mirrored by
// TerrainDetailRenderer.kMaxJitterLevel, which pads the cascades to match.
#define SCATTER_MAX_JITTER 3

// Value noise matching TerrainMeshDetailRenderer.NoiseAt so painted variation looks the same on
// both paths (broad dry/healthy patches rather than per-blade static).
float scatterHashN(int x, int z)
{
    uint h = uint(x * 73856093 ^ z * 19349663);
    h ^= h >> 16u; h *= 0x45d9f3bu; h ^= h >> 16u;
    return float(h & 0xFFFFu) / 65535.0;
}

float scatterNoise(float x, float z)
{
    int ix = int(floor(x)), iz = int(floor(z));
    float fx = x - float(ix), fz = z - float(iz);
    float sx = fx * fx * (3.0 - 2.0 * fx);
    float sz = fz * fz * (3.0 - 2.0 * fz);
    float n0 = mix(scatterHashN(ix, iz), scatterHashN(ix + 1, iz), sx);
    float n1 = mix(scatterHashN(ix, iz + 1), scatterHashN(ix + 1, iz + 1), sx);
    return mix(n0, n1, sz);
}

// One well-mixed value per cell and salt. Each component is an independent hash rather than a
// step of one sequence: chained generators leave their low bits correlated, which lines the
// blades up along the cell grid instead of filling it.
float scatterRandom(ivec2 cell, uint salt)
{
    uint h = uint(cell.x) * 0x8da6b343u + uint(cell.y) * 0xd8163841u + salt * 0xcb1ab31fu;
    h ^= h >> 15u; h *= 0x2c1b3c6du;
    h ^= h >> 12u; h *= 0x297a2d39u;
    h ^= h >> 15u;
    return float(h >> 8u) / 16777215.0;
}

struct ScatterBlade
{
    bool valid;
    vec2 terrainUV;   // 0..1 across the terrain
    vec2 localXZ;     // terrain-local position
    float density;    // painted density at this spot, 0..1
    float rotation;   // 0..2pi
    float windPhase;  // 0..2pi
    float noise;      // smooth 0..1 variation for size and colour
    float fade;       // shrinks blades the next ring out will not draw
};

// Resolve one instance into a blade. Returns valid = false when the instance falls outside the
// ring's annulus, off the terrain, or loses the density test, and the caller collapses the quad.
ScatterBlade scatterResolve(int instanceID, float noiseSpread)
{
    ScatterBlade blade;
    blade.valid = false;
    blade.terrainUV = vec2(0.0);
    blade.localXZ = vec2(0.0);
    blade.density = 0.0;
    blade.rotation = 0.0;
    blade.windPhase = 0.0;
    blade.noise = 0.0;
    blade.fade = 1.0;

    vec4 cascade = Graphite_Object().custom0;
    vec4 band = Graphite_Object().custom1;
    int n = int(cascade.z);
    int scatterLevel = int(cascade.w);
    int stride = 1 << scatterLevel;
    ivec2 fine = ivec2(cascade.xy) + ivec2(instanceID % n, instanceID / n) * stride;

    // The coarsest ring that still draws this cell decides how far its blade may wander. A blade
    // kept by a ring whose cells are 8 fine cells apart spreads across all 8, so distant rings
    // fill their spacing evenly instead of sitting on a visible lattice.
    int lsbX = findLSB(uint(fine.x));
    int lsbZ = findLSB(uint(fine.y));
    int level = min(lsbX < 0 ? 31 : lsbX, lsbZ < 0 ? 31 : lsbZ);
    int jitterLevel = clamp(level, 0, min(_ScatterMaxLevel, SCATTER_MAX_JITTER));
    float spread = float(1 << jitterLevel);

    float jitterX = scatterRandom(fine, 0u);
    float jitterZ = scatterRandom(fine, 1u);
    float rotation = scatterRandom(fine, 2u);
    float keep = scatterRandom(fine, 3u);

    vec2 localXZ = (vec2(fine) + 0.5 + (vec2(jitterX, jitterZ) - 0.5) * spread) * _ScatterCellSize;

    // Rings are circles around the camera, so grass reaches equally far in every direction
    float dist = length(localXZ - band.xy);
    if (dist < band.z || dist >= band.w) return blade;

    vec2 uv = localXZ / _TerrainSize;
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) return blade;

    if (_HasHoles > 0 && texture(_HolesMap, uv).r < 0.5) return blade;

    float density = texture(_DetailMap, uv)[_DetailChannel];
    if (keep >= density) return blade;

    // A blade shrinks away across the band that drops it, so density decays smoothly outward
    // instead of stepping down at a cascade edge. In the last cascade every blade is dropped,
    // which is what fades the field out at the draw distance.
    bool dropped = level == scatterLevel || scatterLevel == _ScatterMaxLevel;
    if (dropped)
    {
        blade.fade = 1.0 - smoothstep(band.z, band.w, dist);
        if (blade.fade <= 0.0) return blade;
    }

    blade.valid = true;
    blade.terrainUV = uv;
    blade.localXZ = localXZ;
    blade.density = density;
    blade.rotation = rotation * 6.28318531;
    blade.windPhase = jitterX * 6.28318531;
    blade.noise = scatterNoise(localXZ.x * noiseSpread, localXZ.y * noiseSpread);
    return blade;
}

// ============================================================================
//  Grass blades: a quad per instance, standing on the terrain and bent by the wind
// ============================================================================

uniform float _WindStrength;
uniform float _WindSpeed;
uniform float _Billboard;
uniform float _AlignToNormal;

// Prototype parameters
uniform vec4 _ProtoSize;          // min width, max width, min height, max height
uniform vec4 _ProtoHealthyColor;
uniform vec4 _ProtoDryColor;
uniform float _ProtoNoiseSpread;
uniform float _ProtoBendFactor;

// Spherical wind zones, nearest first. Sphere xyz = centre, w = radius. Params = strength, turbulence, pulse
// magnitude, pulse frequency.
uniform int _WindZoneCount;
uniform vec4 _WindZoneSphere0;
uniform vec4 _WindZoneSphere1;
uniform vec4 _WindZoneSphere2;
uniform vec4 _WindZoneSphere3;
uniform vec4 _WindZoneParams0;
uniform vec4 _WindZoneParams1;
uniform vec4 _WindZoneParams2;
uniform vec4 _WindZoneParams3;

// Downwash, the way air behaves under something hovering: it comes straight down through
// the middle, spreads outward across the ground and dies at the rim. The middle is calm,
// not the strongest point. Returns a horizontal push, matching WindZone.SampleWind.
vec2 sampleWindZone(vec4 sphere, vec4 p, vec3 worldPosition, float time)
{
    vec3 centre = sphere.xyz;
    float radius = max(sphere.w, 1e-4);

    vec2 toPoint = worldPosition.xz - centre.xz;
    float dist = length(toPoint);
    float height = abs(worldPosition.y - centre.y);
    if (dist >= radius || height >= radius) return vec2(0.0);

    // Height falls off on its own, so a zone parked high overhead barely stirs the ground
    float vertical = 1.0 - smoothstep(0.0, radius, height);
    float r = dist / radius;
    vec2 outward = dist > 1e-4 ? toPoint / dist : vec2(0.0);

    // Calm eye, peaking where the column spreads, then a long decay to the rim
    float outflow = smoothstep(0.0, 0.22, r) * (1.0 - smoothstep(0.35, 1.0, r));

    // Gust fronts sweep outward. Time is a phase on a wave running out from the middle, never a
    // multiplier on the zone's position, so nudging the zone does not scramble the field.
    float gustSpeed = 0.35 * (1.0 + p.x);
    float ringPhase = r * 3.0 - time * gustSpeed;
    float front = scatterNoise(outward.x * 2.5 + ringPhase, outward.y * 2.5) * 2.0 - 1.0;
    float wobble = scatterNoise(outward.x * 2.5, outward.y * 2.5 + ringPhase * 1.3 + 17.0) * 2.0 - 1.0;

    float gust = 1.0 + front * p.y + sin((time * p.w - r * 2.0) * 6.28318531) * p.z;

    // Once a blade is flat, pushing harder is invisible but turning the push is not
    float twist = wobble * p.y * 0.8;
    float cs = cos(twist), sn = sin(twist);
    vec2 flow = vec2(outward.x * cs - outward.y * sn, outward.x * sn + outward.y * cs);

    return flow * max(p.x * outflow * vertical * gust, 0.0);
}

vec2 sampleWindZones(vec3 worldPosition, float time)
{
    vec2 wind = vec2(0.0);
    if (_WindZoneCount > 0) wind += sampleWindZone(_WindZoneSphere0, _WindZoneParams0, worldPosition, time);
    if (_WindZoneCount > 1) wind += sampleWindZone(_WindZoneSphere1, _WindZoneParams1, worldPosition, time);
    if (_WindZoneCount > 2) wind += sampleWindZone(_WindZoneSphere2, _WindZoneParams2, worldPosition, time);
    if (_WindZoneCount > 3) wind += sampleWindZone(_WindZoneSphere3, _WindZoneParams3, worldPosition, time);
    return wind;
}

// A blade vertex's offset from its root at a time, bent along an arc rather than dragged sideways: a blade lays
// over in strong wind, it does not stretch. Saturates at a right angle, so it never goes inside out.
vec3 grassOffset(vec3 vertex, vec3 rootAbsolute, vec3 up, vec3 right, vec2 scale, float windPhase, float time)
{
    float sway = sin(time * _WindSpeed + rootAbsolute.x * 0.7 + rootAbsolute.z * 0.4 + windPhase) * _WindStrength;
    vec2 windForce = (vec2(sway, sway * 0.3) + sampleWindZones(rootAbsolute, time)) * _ProtoBendFactor;

    float windMag = length(windForce);
    vec3 bendDir = windMag > 1e-4 ? vec3(windForce.x, 0.0, windForce.y) / windMag : vec3(0.0);

    float bendAngle = 1.7453293 * windMag / (1.0 + windMag);
    float bladeY = max(vertex.y, 0.0);
    float alongUp = bladeY;
    float alongWind = 0.0;
    if (bendAngle > 1e-4)
    {
        alongUp = sin(bendAngle * bladeY) / bendAngle;
        alongWind = (1.0 - cos(bendAngle * bladeY)) / bendAngle;
    }

    // The small lift keeps the base out of the ground
    return right * vertex.x * scale.x + up * (alongUp * scale.y) + bendDir * (alongWind * scale.y) + up * 0.01 * scale.y;
}

// Places this vertex of this instance's blade: its camera relative world position, normal and tint. Hands last
// frame's bend to the motion vectors. False for an instance without a blade, whose quad collapses.
bool grassVertex(out vec3 world, out vec3 normal, out vec4 color)
{
    world = vec3(0.0);
    normal = vec3(0.0, 1.0, 0.0);
    color = vec4(0.0);

    ScatterBlade blade = scatterResolve(int(Graphite_InstanceIndex()), _ProtoNoiseSpread);
    if (!blade.valid)
        return false;

    float sizeT = blade.noise * min(1.0, blade.density * 2.0);
    vec2 scale = vec2(mix(_ProtoSize.x, _ProtoSize.y, sizeT), mix(_ProtoSize.z, _ProtoSize.w, sizeT)) * blade.fade;

    vec3 rootLocal = vec3(blade.localXZ.x, terrainHeight(blade.terrainUV), blade.localXZ.y);
    vec3 root = Graphite_ObjectToWorld(rootLocal);
    vec3 rootAbsolute = Graphite_AbsoluteFromRelative(root);

    vec3 up = _AlignToNormal > 0.5
        ? Graphite_ObjectToWorldNormal(terrainNormal(blade.terrainUV))
        : normalize(Graphite_ObjectToWorldDirection(vec3(0.0, 1.0, 0.0)));

    Graphite_ViewData view = Graphite_View();
    vec3 right = _Billboard > 0.5
        ? vec3(view.view[0][0], view.view[1][0], view.view[2][0])
        : normalize(Graphite_ObjectToWorldDirection(vec3(cos(blade.rotation), 0.0, sin(blade.rotation))));
    right = normalize(right - up * dot(right, up));

    vec3 vertex = vertexPosition;
    float time = view.time.x;
    vec3 offset = grassOffset(vertex, rootAbsolute, up, right, scale, blade.windPhase, time);
    vec3 previousOffset = grassOffset(vertex, rootAbsolute, up, right, scale, blade.windPhase, time - view.time.y);
    Graphite_SetPreviousPosition(rootLocal + inverse(Graphite_Linear(Graphite_ObjectTransform())) * previousOffset);

    world = root + offset;
    normal = normalize(cross(up, right));
    color = mix(_ProtoHealthyColor, _ProtoDryColor, 1.0 - blade.noise);
    return true;
}
