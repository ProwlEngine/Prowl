// Shadows: spot lights' tiles in the world's pages and each directional light's cascades in the view's own textures,
// every read going through Graphite_SampleShadow. The functions return visibility, 1 lit, and take camera relative
// positions. Receivers move toward the light and along their geometric normal by the light's biases in texels.
#pragma once

#include "Graphite/Core.glsl"
#include "Graphite/Textures.glsl"

struct Graphite_ShadowFace
{
    mat4 lightToClip;        // light relative position to clip, reversed Z
    vec4 rect;               // tile origin and size in page uv, world texel size one unit from the light
    vec4 params;             // page slot as uint bits, fade, one over the page size, unused
};

struct Graphite_Cascade
{
    mat4 viewToClip;         // camera relative position to clip, reversed Z
    vec4 sphere;             // camera relative centre, radius
    vec4 params;             // texture slot as uint bits, one over its size, the light's cascade count, blend fraction
    vec4 fade;               // view forward, shadow distance
};

layout(set = 1, binding = 28) uniform samplerShadow Graphite_ShadowSampler;
layout(set = 1, binding = 29, std430) readonly buffer Graphite_ShadowFaces { Graphite_ShadowFace graphite_ShadowFaces[]; };
layout(set = 1, binding = 30, std430) readonly buffer Graphite_Cascades { Graphite_Cascade graphite_Cascades[]; };

#define GRAPHITE_SHADOW_FACES 6u
#define GRAPHITE_CASCADES 4u
#define GRAPHITE_SHADOW_HARD 0.0

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

// The lit fraction at uv of a tile of a depth texture, hard or a 5x5 tent from 9 comparisons, every tap half a texel inside the tile
float Graphite_SampleShadow(uint slot, vec4 rect, float inverseSize, vec3 coords, float filtering)
{
    vec2 tileMin = rect.xy + 0.5 * inverseSize;
    vec2 tileMax = rect.xy + rect.z - 0.5 * inverseSize;
    if (filtering == GRAPHITE_SHADOW_HARD)
        return texture(sampler2DShadow(Graphite_Textures2D[nonuniformEXT(slot)], Graphite_ShadowSampler), vec3(clamp(coords.xy, tileMin, tileMax), coords.z));

    // Three bilinear taps per axis reproduce a five texel tent exactly: weights 4 - 3f, 7 and 1 + 3f summing to 12
    vec2 texel = coords.xy / inverseSize;
    vec2 base = floor(texel + 0.5);
    vec2 f = texel + 0.5 - base;
    base -= 0.5;
    vec2 w0 = 4.0 - 3.0 * f;
    vec2 w2 = 1.0 + 3.0 * f;
    vec2 o0 = (3.0 - 2.0 * f) / w0 - 2.0;
    vec2 o1 = (3.0 + f) / 7.0;
    vec2 o2 = f / w2 + 2.0;
    vec3 wx = vec3(w0.x, 7.0, w2.x), wy = vec3(w0.y, 7.0, w2.y);
    vec3 ox = vec3(o0.x, o1.x, o2.x), oy = vec3(o0.y, o1.y, o2.y);

    float lit = 0.0;
    for (int j = 0; j < 3; j++)
    {
        for (int i = 0; i < 3; i++)
        {
            vec2 uv = clamp((base + vec2(ox[i], oy[j])) * inverseSize, tileMin, tileMax);
            lit += wx[i] * wy[j] * texture(sampler2DShadow(Graphite_Textures2D[nonuniformEXT(slot)], Graphite_ShadowSampler), vec3(uv, coords.z));
        }
    }
    return lit / 144.0;
}

// A spot light's shadow at a camera relative position, the light's position camera relative too
float Graphite_SpotShadow(int shadowSlot, vec3 lightPosition, vec3 lightDirection, float depthBias, float normalBias, float strength, float filtering, vec3 position, vec3 geomNormal)
{
    Graphite_ShadowFace face = graphite_ShadowFaces[uint(shadowSlot) * GRAPHITE_SHADOW_FACES];
    float fade = face.params.y;
    if (fade <= 0.0 || face.rect.z <= 0.0)
        return 1.0;

    vec3 toPosition = position - lightPosition;
    float distance = length(toPosition);
    float axisDistance = max(dot(toPosition, lightDirection), 0.0);
    vec3 biased = Graphite_ShadowBias(position, geomNormal, -toPosition / max(distance, 1e-6), face.rect.w * axisDistance, depthBias, normalBias);
    vec4 clip = face.lightToClip * vec4(biased - lightPosition, 1.0);
    if (clip.w <= 0.0 || any(greaterThan(abs(clip.xy), vec2(clip.w))))
        return 1.0;

    float lit = Graphite_SampleShadow(floatBitsToUint(face.params.x), face.rect, face.params.z, Graphite_ShadowCoords(clip, face.rect), filtering);
    return mix(1.0, lit, strength * fade);
}

// A point light's shadow: the cube face the biased position lies in, every face's tile the same size
float Graphite_PointShadow(int shadowSlot, vec3 lightPosition, float depthBias, float normalBias, float strength, float filtering, vec3 position, vec3 geomNormal)
{
    uint first = uint(shadowSlot) * GRAPHITE_SHADOW_FACES;
    Graphite_ShadowFace any = graphite_ShadowFaces[first];
    float fade = any.params.y;
    if (fade <= 0.0 || any.rect.z <= 0.0)
        return 1.0;

    vec3 toPosition = position - lightPosition;
    float distance = length(toPosition);
    vec3 a = abs(toPosition);
    float axisDistance = max(a.x, max(a.y, a.z));
    vec3 biased = Graphite_ShadowBias(position, geomNormal, -toPosition / max(distance, 1e-6), any.rect.w * axisDistance, depthBias, normalBias);

    // The face picked from the biased position, so the offset never pushes it off its face
    vec3 d = biased - lightPosition;
    vec3 b = abs(d);
    uint face = b.x >= b.y && b.x >= b.z ? (d.x > 0.0 ? 0u : 1u) : b.y >= b.z ? (d.y > 0.0 ? 2u : 3u) : (d.z > 0.0 ? 4u : 5u);
    Graphite_ShadowFace f = graphite_ShadowFaces[first + face];
    if (f.rect.z <= 0.0)
        return 1.0;
    vec4 clip = f.lightToClip * vec4(d, 1.0);
    if (clip.w <= 0.0)
        return 1.0;

    float lit = Graphite_SampleShadow(floatBitsToUint(f.params.x), f.rect, f.params.z, Graphite_ShadowCoords(clip, f.rect), filtering);
    return mix(1.0, lit, strength * fade);
}

// Whether p is inside a cascade's sphere, at least inset texels in from its edge
bool Graphite_InCascade(vec3 p, Graphite_Cascade c, float inset)
{
    vec3 d = p - c.sphere.xyz;
    float r = c.sphere.w * (1.0 - 2.0 * inset * c.params.y);
    return dot(d, d) <= r * r;
}

float Graphite_CascadeLit(Graphite_Cascade c, vec3 position, vec3 geomNormal, vec3 toLight, float depthBias, float normalBias, float filtering)
{
    float texelWorld = 2.0 * c.sphere.w * c.params.y;
    vec3 biased = Graphite_ShadowBias(position, geomNormal, toLight, texelWorld, depthBias, normalBias);
    vec4 rect = vec4(0.0, 0.0, 1.0, 0.0);
    return Graphite_SampleShadow(floatBitsToUint(c.params.x), rect, c.params.y, Graphite_ShadowCoords(c.viewToClip * vec4(biased, 1.0), rect), filtering);
}

// A directional light's shadow at a camera relative position: the sharpest cascade holding it, blended into the next
// over the last part of its sphere, fading out over the last tenth of the shadow distance
float Graphite_DirectionalShadow(uint directional, vec3 toLight, float depthBias, float normalBias, float strength, float filtering, vec3 position, vec3 geomNormal)
{
    uint first = directional * GRAPHITE_CASCADES;
    Graphite_Cascade head = graphite_Cascades[first];
    uint count = uint(head.params.z);
    if (count == 0u)
        return 1.0;
    float viewDepth = dot(position, head.fade.xyz);
    float distance = head.fade.w;
    if (viewDepth > distance)
        return 1.0;

    // Three texels of room keeps the bias and the filter inside the map, points at a cascade's very edge take any that holds them
    int pick = -1;
    for (uint c = 0u; c < count && pick < 0; c++)
    {
        if (Graphite_InCascade(position, graphite_Cascades[first + c], 3.0))
            pick = int(c);
    }
    for (uint c = 0u; c < count && pick < 0; c++)
    {
        if (Graphite_InCascade(position, graphite_Cascades[first + c], 0.0))
            pick = int(c);
    }
    if (pick < 0)
        return 1.0;

    Graphite_Cascade cascade = graphite_Cascades[first + uint(pick)];
    float lit = Graphite_CascadeLit(cascade, position, geomNormal, toLight, depthBias, normalBias, filtering);

    float blend = cascade.params.w;
    if (blend > 0.0 && uint(pick) + 1u < count)
    {
        vec3 d = position - cascade.sphere.xyz;
        float inner = cascade.sphere.w * (1.0 - 2.0 * 3.0 * cascade.params.y);
        float t = sqrt(dot(d, d)) / max(inner, 1e-6);
        float w = clamp((t - (1.0 - blend)) / blend, 0.0, 1.0);
        Graphite_Cascade next = graphite_Cascades[first + uint(pick) + 1u];
        if (w > 0.0 && Graphite_InCascade(position, next, 3.0))
            lit = mix(lit, Graphite_CascadeLit(next, position, geomNormal, toLight, depthBias, normalBias, filtering), w);
    }

    float fade = smoothstep(distance * 0.9, distance, viewDepth);
    return mix(1.0, lit, strength * (1.0 - fade));
}
