// Shadows: spot lights' tiles in the world's pages and each directional light's cascades in the view's own textures,
// every read going through Graphite_SampleShadow. The functions return visibility, 1 lit, and take camera relative
// positions. Receivers move toward the light and along their geometric normal by the light's biases in texels.
#pragma once

#include "Graphite/Core.glsl"
#include "Graphite/Textures.glsl"

struct Graphite_Cascade
{
    mat4 viewToClip;         // camera relative position to clip, reversed Z
    vec4 sphere;             // camera relative centre, radius
    vec4 params;             // texture slot as uint bits, one over its size, the light's cascade count, blend fraction
    vec4 fade;               // view forward, shadow distance
    vec4 depth;              // world distance from depth 1 to depth 0 along the light, unused
};

layout(set = 1, binding = 28) uniform samplerShadow Graphite_ShadowSampler;
// Six faces per local light of three vec4s each: the tile's origin and size in page uv and the world size of a texel one
// unit from the light, then the page slot as uint bits, the fade times the shadow's strength, one over the page size and
// the filter, then near and far along the face's axis and the depth and normal biases. A spot light uses its first face,
// its second and third holding its light to clip matrix.
layout(set = 1, binding = 29, std430) readonly buffer Graphite_ShadowFaces { vec4 graphite_ShadowFaces[]; };

vec4 Graphite_FaceRect(uint face) { return graphite_ShadowFaces[face * 3u]; }
vec4 Graphite_FaceParams(uint face) { return graphite_ShadowFaces[face * 3u + 1u]; }
vec4 Graphite_FaceDepth(uint face) { return graphite_ShadowFaces[face * 3u + 2u]; }
layout(set = 1, binding = 30, std430) readonly buffer Graphite_Cascades { Graphite_Cascade graphite_Cascades[]; };

// Contact hardening is compiled in while a light of the world uses it
#pragma variant global GRAPHITE_CONTACT_SHADOWS

#define GRAPHITE_SHADOW_FACES 6u
#define GRAPHITE_CASCADES 4u
#define GRAPHITE_SHADOW_HARD 0.0
#define GRAPHITE_SHADOW_CONTACT 2.0
#define GRAPHITE_SHADOW_TAPS 16

// Whether the surface takes shadows, its renderable's ReceiveShadows flag
bool Graphite_ReceivesShadows()
{
#if defined(GRAPHITE_FRAGMENT) && !defined(GRAPHITE_NO_OBJECT)
    return (Graphite_Object().flags & 4u) != 0u;
#else
    return true;
#endif
}

// Moves a receiver toward the light and out along its geometric normal, both in texels of texelWorld at the receiver
vec3 Graphite_ShadowBias(vec3 position, vec3 geomNormal, vec3 toLight, float texelWorld, float depthBias, float normalBias)
{
    float NdotL = clamp(dot(geomNormal, toLight), 0.0, 1.0);
    float sinTheta = sqrt(1.0 - NdotL * NdotL);
    return position + (toLight * depthBias + geomNormal * (normalBias * sinTheta)) * texelWorld;
}

// Where clip space lands in a tile, uv in its texture, and the depth to compare
vec3 Graphite_ShadowCoords(vec4 clip, vec4 rect)
{
    vec3 ndc = clip.xyz / clip.w;
    vec2 local = vec2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5);
    return vec3(rect.xy + local * rect.z, clamp(ndc.z, 0.0, 1.0));
}

// One of the tent's three taps along an axis, f the texel fraction there: its weight and its offset from the base texel
vec2 graphite_TentTap(int tap, float f)
{
    if (tap == 0)
    {
        float w = 4.0 - 3.0 * f;
        return vec2(w, (3.0 - 2.0 * f) / w - 2.0);
    }
    if (tap == 1)
        return vec2(7.0, (3.0 + f) / 7.0);
    float w = 1.0 + 3.0 * f;
    return vec2(w, f / w + 2.0);
}

// The lit fraction at uv of a tile of a depth texture, hard or a 5x5 tent from 9 comparisons, every tap half a texel inside the tile
float Graphite_SampleShadow(uint slot, vec4 rect, float inverseSize, vec3 coords, float filtering)
{
    vec2 tileMin = rect.xy + 0.5 * inverseSize;
    vec2 tileMax = rect.xy + rect.z - 0.5 * inverseSize;
    if (filtering == GRAPHITE_SHADOW_HARD)
        return texture(sampler2DShadow(Graphite_ShadowTextures2D[nonuniformEXT(slot)], Graphite_ShadowSampler), vec3(clamp(coords.xy, tileMin, tileMax), coords.z));

    // Three bilinear taps per axis reproduce a five texel tent exactly: weights 4 - 3f, 7 and 1 + 3f summing to 12, each
    // tap's worked out where it is taken so none are held across the others
    vec2 texel = coords.xy / inverseSize;
    vec2 base = floor(texel + 0.5);
    vec2 f = texel + 0.5 - base;
    base -= 0.5;

    float lit = 0.0;
    for (int j = 0; j < 3; j++)
    {
        vec2 y = graphite_TentTap(j, f.y);
        for (int i = 0; i < 3; i++)
        {
            vec2 x = graphite_TentTap(i, f.x);
            vec2 uv = clamp((base + vec2(x.y, y.y)) * inverseSize, tileMin, tileMax);
            lit += x.x * y.x * texture(sampler2DShadow(Graphite_ShadowTextures2D[nonuniformEXT(slot)], Graphite_ShadowSampler), vec3(uv, coords.z));
        }
    }
    return lit / 144.0;
}

// A point of a Vogel disc of count points of radius 1, turned by angle
vec2 Graphite_VogelDisc(int index, int count, float angle)
{
    float r = sqrt((float(index) + 0.5) / float(count));
    float theta = float(index) * 2.39996323 + angle;
    return r * vec2(cos(theta), sin(theta));
}

// An angle from interleaved gradient noise on the pixel, so neighbouring pixels sample a disc turned differently
float Graphite_ShadowNoiseAngle()
{
#ifdef GRAPHITE_FRAGMENT
    vec3 magic = vec3(0.06711056, 0.00583715, 52.9829189);
    return 6.28318531 * fract(magic.z * fract(dot(gl_FragCoord.xy, magic.xy)));
#else
    return 0.0;
#endif
}

// Two directions square to a direction and to each other
void Graphite_ShadowFrame(vec3 direction, out vec3 t1, out vec3 t2)
{
    t1 = normalize(cross(abs(direction.y) < 0.99 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0), direction));
    t2 = cross(direction, t1);
}

// The cube face a direction from a point light falls in
uint Graphite_CubeFace(vec3 d)
{
    vec3 b = abs(d);
    return b.x >= b.y && b.x >= b.z ? (d.x > 0.0 ? 0u : 1u) : b.y >= b.z ? (d.y > 0.0 ? 2u : 3u) : (d.z > 0.0 ? 4u : 5u);
}

// A light relative position in a cube face's view, the rows the faces are drawn with, its axis distance last
vec3 Graphite_CubeView(uint face, vec3 d)
{
    switch (face)
    {
        case 0u: return vec3(-d.z, -d.y, d.x);
        case 1u: return vec3(d.z, -d.y, -d.x);
        case 2u: return vec3(d.x, d.z, d.y);
        case 3u: return vec3(d.x, -d.z, -d.y);
        case 4u: return vec3(d.x, -d.y, d.z);
        default: return vec3(-d.x, -d.y, -d.z);
    }
}

// A light relative position in a face's clip space, reversed Z: a cube face's 90 degree view worked out from its near and
// far, a spot light's through its matrix
vec4 Graphite_FaceClip(uint first, uint face, bool cube, vec3 d)
{
    if (!cube)
    {
        uint w = first * 3u + 3u;
        return mat4(graphite_ShadowFaces[w], graphite_ShadowFaces[w + 1u], graphite_ShadowFaces[w + 2u], graphite_ShadowFaces[w + 3u]) * vec4(d, 1.0);
    }
    vec3 v = Graphite_CubeView(face - first, d);
    vec2 nearFar = Graphite_FaceDepth(face).xy;
    return vec4(v.xy, nearFar.x * (nearFar.y - v.z) / (nearFar.y - nearFar.x), v.z);
}

// The receiver's plane by its geometric normal, square to the light when it has none
vec3 Graphite_ShadowPlane(vec3 geomNormal, vec3 toLight)
{
    return dot(geomNormal, geomNormal) > 1e-8 ? geomNormal : toLight;
}

// A depth of a perspective tile as the distance along its axis
float Graphite_FaceAxis(vec2 nearFar, float depth)
{
    return nearFar.x * nearFar.y / (depth * (nearFar.y - nearFar.x) + nearFar.x);
}

struct Graphite_LocalTap
{
    vec3 coords;
    uint face;
};

// One tap of a local light's soft shadow: the receiver moved square to the light, then along the light's ray back onto its
// plane so the tap's depth follows the surface, in the face the tap falls in, a point light's picked per tap so discs
// cross cube faces without seams. Positions are relative to the light.
Graphite_LocalTap Graphite_LocalShadowTap(uint first, bool cube, vec3 receiver, vec3 normal, vec3 t1, vec3 t2, vec2 offset)
{
    vec3 q = receiver + t1 * offset.x + t2 * offset.y;
    float along = dot(normal, q);
    q *= clamp(abs(along) > 1e-6 ? dot(normal, receiver) / along : 1.0, 0.5, 2.0);
    Graphite_LocalTap tap;
    tap.face = cube ? first + Graphite_CubeFace(q) : first;
    tap.coords = Graphite_ShadowCoords(Graphite_FaceClip(first, tap.face, cube, q), Graphite_FaceRect(tap.face));
    return tap;
}

// Contact hardening for a spot or point light. Blockers are searched as far as one can shift across the light's size,
// which is from the near plane, the centre included, and their distance along the axis taken against each tap's own;
// the penumbra is the light's radius times how far past the blockers the receiver lies, no wider than the search.
float Graphite_LocalContact(uint first, bool cube, uint centre, vec3 receiver, vec3 normal, float lightRadius, float receiverAxis)
{
    vec4 centreRect = Graphite_FaceRect(centre);
    float inverseSize = Graphite_FaceParams(centre).z;
    float texelWorld = centreRect.w * max(receiverAxis, 1e-4);
    float largest = centreRect.z / inverseSize * 0.125;
    float near = Graphite_FaceDepth(centre).x;
    float search = clamp(lightRadius * max(receiverAxis - near, 0.0) / (max(receiverAxis, 1e-4) * near * centreRect.w), 1.5, largest);
    vec3 t1, t2;
    Graphite_ShadowFrame(normalize(receiver), t1, t2);
    float angle = Graphite_ShadowNoiseAngle();

    float ratio = 0.0;
    int found = 0;
    for (int i = 0; i <= GRAPHITE_SHADOW_TAPS; i++)
    {
        vec2 offset = i < GRAPHITE_SHADOW_TAPS ? Graphite_VogelDisc(i, GRAPHITE_SHADOW_TAPS, angle) * search * texelWorld : vec2(0.0);
        Graphite_LocalTap tap = Graphite_LocalShadowTap(first, cube, receiver, normal, t1, t2, offset);
        vec4 rect = Graphite_FaceRect(tap.face);
        vec2 texel = clamp(tap.coords.xy / inverseSize, rect.xy / inverseSize, (rect.xy + rect.z) / inverseSize - 1.0);
        float depth = texelFetch(sampler2D(Graphite_Textures2D[nonuniformEXT(floatBitsToUint(Graphite_FaceParams(tap.face).x))], Graphite_Samplers[0]), ivec2(texel), 0).r;
        if (depth > tap.coords.z)
        {
            vec2 nearFar = Graphite_FaceDepth(tap.face).xy;
            ratio += Graphite_FaceAxis(nearFar, depth) / Graphite_FaceAxis(nearFar, tap.coords.z);
            found++;
        }
    }
    if (found == 0)
        return 1.0;

    float radius = clamp(lightRadius * (float(found) / max(ratio, 1e-4) - 1.0) / texelWorld, 1.5, search);
    float lit = 0.0;
    for (int i = 0; i < GRAPHITE_SHADOW_TAPS; i++)
    {
        Graphite_LocalTap tap = Graphite_LocalShadowTap(first, cube, receiver, normal, t1, t2, Graphite_VogelDisc(i, GRAPHITE_SHADOW_TAPS, angle) * radius * texelWorld);
        vec4 rect = Graphite_FaceRect(tap.face);
        vec2 uv = clamp(tap.coords.xy, rect.xy + 0.5 * inverseSize, rect.xy + rect.z - 0.5 * inverseSize);
        lit += texture(sampler2DShadow(Graphite_ShadowTextures2D[nonuniformEXT(floatBitsToUint(Graphite_FaceParams(tap.face).x))], Graphite_ShadowSampler), vec3(uv, tap.coords.z));
    }
    return lit / float(GRAPHITE_SHADOW_TAPS);
}

// One tap of a directional light's soft shadow: the receiver moved square to the light, then along it back onto its plane
vec3 Graphite_CascadeTap(Graphite_Cascade c, vec3 receiver, vec3 normal, vec3 toLight, vec3 t1, vec3 t2, vec2 offset)
{
    vec3 q = receiver + t1 * offset.x + t2 * offset.y;
    float facing = dot(normal, toLight);
    float slide = abs(facing) > 1e-4 ? dot(normal, receiver - q) / facing : 0.0;
    float limit = 4.0 * length(offset);
    q += toLight * clamp(slide, -limit, limit);
    return Graphite_ShadowCoords(c.viewToClip * vec4(q, 1.0), vec4(0.0, 0.0, 1.0, 0.0));
}

// Contact hardening for a directional light. Blockers are searched as far as the disc reaches from no further than the
// cascade's own depth toward the light, the centre included, kept inside the cascade; the penumbra is the mean distance
// from blocker to receiver times the tangent of the disc's angular radius, no wider than the search.
float Graphite_CascadeContact(Graphite_Cascade c, vec3 receiver, vec3 normal, vec3 toLight, float tanRadius, float texelWorld)
{
    float inverseSize = c.params.y;
    uint slot = floatBitsToUint(c.params.x);
    vec3 centre = Graphite_ShadowCoords(c.viewToClip * vec4(receiver, 1.0), vec4(0.0, 0.0, 1.0, 0.0));
    vec2 edge = min(centre.xy, 1.0 - centre.xy) / inverseSize - 1.0;
    float largest = max(min(0.125 / inverseSize, min(edge.x, edge.y)), 1.5);
    float reach = min((1.0 - centre.z) * c.depth.x, 2.0 * c.sphere.w);
    float search = clamp(reach * tanRadius / texelWorld, 1.5, largest);
    vec3 t1, t2;
    Graphite_ShadowFrame(toLight, t1, t2);
    float angle = Graphite_ShadowNoiseAngle();
    float last = 1.0 / inverseSize - 1.0;

    float gap = 0.0;
    int found = 0;
    for (int i = 0; i <= GRAPHITE_SHADOW_TAPS; i++)
    {
        vec2 offset = i < GRAPHITE_SHADOW_TAPS ? Graphite_VogelDisc(i, GRAPHITE_SHADOW_TAPS, angle) * search * texelWorld : vec2(0.0);
        vec3 coords = Graphite_CascadeTap(c, receiver, normal, toLight, t1, t2, offset);
        float depth = texelFetch(sampler2D(Graphite_Textures2D[nonuniformEXT(slot)], Graphite_Samplers[0]), ivec2(clamp(coords.xy / inverseSize, vec2(0.0), vec2(last))), 0).r;
        if (depth > coords.z)
        {
            gap += depth - coords.z;
            found++;
        }
    }
    if (found == 0)
        return 1.0;

    float radius = clamp(gap / float(found) * c.depth.x * tanRadius / texelWorld, 1.5, search);
    float lit = 0.0;
    for (int i = 0; i < GRAPHITE_SHADOW_TAPS; i++)
    {
        vec3 coords = Graphite_CascadeTap(c, receiver, normal, toLight, t1, t2, Graphite_VogelDisc(i, GRAPHITE_SHADOW_TAPS, angle) * radius * texelWorld);
        vec2 uv = clamp(coords.xy, vec2(0.5 * inverseSize), vec2(1.0 - 0.5 * inverseSize));
        lit += texture(sampler2DShadow(Graphite_ShadowTextures2D[nonuniformEXT(slot)], Graphite_ShadowSampler), vec3(uv, coords.z));
    }
    return lit / float(GRAPHITE_SHADOW_TAPS);
}

// A spot or point light's shadow at a camera relative position, the light's position camera relative too. A point light's
// cube face is the one the biased position lies in, so the offset never pushes it off its face; every face's tile is the
// same size with the same fade, so the face the unbiased position lies in serves for the bias, read again only when the
// bias crosses into another.
float Graphite_LocalShadow(int shadowSlot, bool cube, vec3 lightPosition, vec3 lightDirection, float lightRadius, vec3 position, vec3 geomNormal)
{
    uint first = uint(shadowSlot) * GRAPHITE_SHADOW_FACES;
    vec3 toPosition = position - lightPosition;
    uint index = cube ? first + Graphite_CubeFace(toPosition) : first;
    vec4 rect = Graphite_FaceRect(index);
    vec4 params = Graphite_FaceParams(index);
    float fade = params.y;
    if (fade <= 0.0 || rect.z <= 0.0)
        return 1.0;
    vec2 bias = Graphite_FaceDepth(index).zw;
    float filtering = params.w;

    vec3 toLight = -toPosition / max(length(toPosition), 1e-6);
    vec3 a = abs(toPosition);
    float axisDistance = cube ? max(a.x, max(a.y, a.z)) : max(dot(toPosition, lightDirection), 0.0);
    vec3 d = Graphite_ShadowBias(position, geomNormal, toLight, rect.w * axisDistance, bias.x, bias.y) - lightPosition;
    if (cube)
    {
        uint biased = first + Graphite_CubeFace(d);
        if (biased != index)
        {
            index = biased;
            rect = Graphite_FaceRect(index);
            params = Graphite_FaceParams(index);
            if (rect.z <= 0.0)
                return 1.0;
        }
    }
    vec4 clip = Graphite_FaceClip(first, index, cube, d);
    if (clip.w <= 0.0 || (!cube && any(greaterThan(abs(clip.xy), vec2(clip.w)))))
        return 1.0;

#ifdef GRAPHITE_CONTACT_SHADOWS
    if (filtering == GRAPHITE_SHADOW_CONTACT && lightRadius > 0.0)
        return mix(1.0, Graphite_LocalContact(first, cube, index, d, Graphite_ShadowPlane(geomNormal, toLight), lightRadius, clip.w), fade);
#endif
    return mix(1.0, Graphite_SampleShadow(floatBitsToUint(params.x), rect, params.z, Graphite_ShadowCoords(clip, rect), min(filtering, 1.0)), fade);
}

// Whether p is inside a cascade's sphere, at least inset texels in from its edge
bool Graphite_InCascade(vec3 p, uint cascade, float inset)
{
    vec4 sphere = graphite_Cascades[cascade].sphere;
    vec3 d = p - sphere.xyz;
    float r = sphere.w * (1.0 - 2.0 * inset * graphite_Cascades[cascade].params.y);
    return dot(d, d) <= r * r;
}

// Cascades are read by index and field where used, so the sun's shadow holds few registers
float Graphite_CascadeLit(uint cascade, vec3 position, vec3 geomNormal, vec3 toLight, float depthBias, float normalBias, float filtering, float tanRadius)
{
    vec4 params = graphite_Cascades[cascade].params;
    float texelWorld = 2.0 * graphite_Cascades[cascade].sphere.w * params.y;
    vec3 biased = Graphite_ShadowBias(position, geomNormal, toLight, texelWorld, depthBias, normalBias);
#ifdef GRAPHITE_CONTACT_SHADOWS
    if (filtering == GRAPHITE_SHADOW_CONTACT && tanRadius > 0.0)
        return Graphite_CascadeContact(graphite_Cascades[cascade], biased, Graphite_ShadowPlane(geomNormal, toLight), toLight, tanRadius, texelWorld);
#endif
    vec4 rect = vec4(0.0, 0.0, 1.0, 0.0);
    return Graphite_SampleShadow(floatBitsToUint(params.x), rect, params.y, Graphite_ShadowCoords(graphite_Cascades[cascade].viewToClip * vec4(biased, 1.0), rect), min(filtering, 1.0));
}

// A directional light's shadow at a camera relative position: the sharpest cascade holding it, blended into the next
// over the last part of its sphere, fading out over the last tenth of the shadow distance
float Graphite_DirectionalShadow(uint directional, vec3 toLight, float depthBias, float normalBias, float strength, float filtering, float tanRadius, vec3 position, vec3 geomNormal)
{
    uint first = directional * GRAPHITE_CASCADES;
    uint count = uint(graphite_Cascades[first].params.z);
    if (count == 0u)
        return 1.0;
    vec4 fadeAxis = graphite_Cascades[first].fade;
    float viewDepth = dot(position, fadeAxis.xyz);
    float distance = fadeAxis.w;
    if (viewDepth > distance)
        return 1.0;

    // Three texels of room keeps the bias and the filter inside the map, points at a cascade's very edge take any that holds them
    int pick = -1;
    for (uint c = 0u; c < count && pick < 0; c++)
    {
        if (Graphite_InCascade(position, first + c, 3.0))
            pick = int(c);
    }
    for (uint c = 0u; c < count && pick < 0; c++)
    {
        if (Graphite_InCascade(position, first + c, 0.0))
            pick = int(c);
    }
    if (pick < 0)
        return 1.0;

    // How far into the next cascade the blend has gone, both read through one call so only one is inlined
    uint cascade = first + uint(pick);
    float blend = graphite_Cascades[cascade].params.w, w = 0.0;
    if (blend > 0.0 && uint(pick) + 1u < count && Graphite_InCascade(position, cascade + 1u, 3.0))
    {
        vec4 sphere = graphite_Cascades[cascade].sphere;
        vec3 d = position - sphere.xyz;
        float inner = sphere.w * (1.0 - 2.0 * 3.0 * graphite_Cascades[cascade].params.y);
        w = clamp((sqrt(dot(d, d)) / max(inner, 1e-6) - (1.0 - blend)) / blend, 0.0, 1.0);
    }
    float lit = 0.0;
    for (uint k = 0u; k < 2u; k++)
    {
        float weight = k == 0u ? 1.0 - w : w;
        if (weight > 0.0)
            lit += weight * Graphite_CascadeLit(cascade + k, position, geomNormal, toLight, depthBias, normalBias, filtering, tanRadius);
    }

    float fade = smoothstep(distance * 0.9, distance, viewDepth);
    return mix(1.0, lit, strength * (1.0 - fade));
}
