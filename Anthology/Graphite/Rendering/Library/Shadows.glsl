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

#define GRAPHITE_SHADOW_FACES 6u
#define GRAPHITE_CASCADES 4u
#define GRAPHITE_SHADOW_HARD 0.0

// Whether the surface takes shadows, its renderable's ReceiveShadows flag. A program without an object may define
// GRAPHITE_RECEIVES_SHADOWS as the expression that says so.
bool Graphite_ReceivesShadows()
{
#if defined(GRAPHITE_FRAGMENT) && !defined(GRAPHITE_NO_OBJECT)
    return (Graphite_Object().flags & 4u) != 0u;
#elif defined(GRAPHITE_RECEIVES_SHADOWS)
    return GRAPHITE_RECEIVES_SHADOWS;
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

// The lit fraction at uv of a tile of a depth texture, hard or a 5x5 tent from 9 comparisons, every tap half a texel inside
// the tile. A shader defining GRAPHITE_HARD_SHADOWS takes every shadow hard.
float Graphite_SampleShadow(uint slot, vec4 rect, float inverseSize, vec3 coords, float filtering)
{
    vec2 tileMin = rect.xy + 0.5 * inverseSize;
    vec2 tileMax = rect.xy + rect.z - 0.5 * inverseSize;
#ifdef GRAPHITE_HARD_SHADOWS
    filtering = GRAPHITE_SHADOW_HARD;
#endif
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

// A spot or point light's shadow at a camera relative position, the light's position camera relative too. A point light's
// cube face is the one the biased position lies in, so the offset never pushes it off its face; every face's tile is the
// same size with the same fade, so the face the unbiased position lies in serves for the bias, read again only when the
// bias crosses into another.
float Graphite_LocalShadow(int shadowSlot, bool cube, vec3 lightPosition, vec3 lightDirection, vec3 position, vec3 geomNormal)
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

    return mix(1.0, Graphite_SampleShadow(floatBitsToUint(params.x), rect, params.z, Graphite_ShadowCoords(clip, rect), filtering), fade);
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
float Graphite_CascadeLit(uint cascade, vec3 position, vec3 geomNormal, vec3 toLight, float depthBias, float normalBias, float filtering)
{
    vec4 params = graphite_Cascades[cascade].params;
    float texelWorld = 2.0 * graphite_Cascades[cascade].sphere.w * params.y;
    vec3 biased = Graphite_ShadowBias(position, geomNormal, toLight, texelWorld, depthBias, normalBias);
    vec4 rect = vec4(0.0, 0.0, 1.0, 0.0);
    return Graphite_SampleShadow(floatBitsToUint(params.x), rect, params.y, Graphite_ShadowCoords(graphite_Cascades[cascade].viewToClip * vec4(biased, 1.0), rect), filtering);
}

// A value from 0 to 1 that differs from point to point: by pixel in a fragment, turning each frame while the view jitters
// so TAA blends it, by position and frame elsewhere
float graphite_ShadowDither(vec3 position)
{
#ifdef GRAPHITE_FRAGMENT
    float noise = fract(52.9829189 * fract(dot(gl_FragCoord.xy, vec2(0.06711056, 0.00583715))));
    vec2 jitter = Graphite_View().jitter.xy;
    if (jitter.x == 0.0 && jitter.y == 0.0)
        return noise;
#else
    float noise = fract(sin(dot(position, vec3(12.9898, 78.233, 37.719))) * 43758.5453);
#endif
    return fract(noise + Graphite_View().time.z * 0.618034);
}

// A directional light's shadow at a camera relative position: the sharpest cascade holding it, dithered into the next
// over the last part of its sphere, fading out over the last tenth of the shadow distance
float Graphite_DirectionalShadow(uint directional, vec3 toLight, float depthBias, float normalBias, float strength, float filtering, vec3 position, vec3 geomNormal)
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

    // How far into the next cascade the blend has gone, which is how many of the points there read the next
    uint cascade = first + uint(pick);
    float blend = graphite_Cascades[cascade].params.w, w = 0.0;
    if (blend > 0.0 && uint(pick) + 1u < count && Graphite_InCascade(position, cascade + 1u, 3.0))
    {
        vec4 sphere = graphite_Cascades[cascade].sphere;
        vec3 d = position - sphere.xyz;
        float inner = sphere.w * (1.0 - 2.0 * 3.0 * graphite_Cascades[cascade].params.y);
        w = clamp((sqrt(dot(d, d)) / max(inner, 1e-6) - (1.0 - blend)) / blend, 0.0, 1.0);
    }
    if (w > 0.0 && graphite_ShadowDither(position) < w)
        cascade++;
    float lit = Graphite_CascadeLit(cascade, position, geomNormal, toLight, depthBias, normalBias, filtering);

    float fade = smoothstep(distance * 0.9, distance, viewDepth);
    return mix(1.0, lit, strength * (1.0 - fade));
}
