// The standard surface's fragment stage, Graphite_StandardFragment, included after Graphite/Standard/Standard.glsl and
// the shader's Surface function. See Standard.glsl.
#pragma once

#include "Graphite/Standard/Standard.glsl"

#ifdef GRAPHITE_FRAGMENT

#if defined(GRAPHITE_STANDARD_FORWARD) || defined(GRAPHITE_STANDARD_SHADOW_DITHER) || defined(_SURFACE_CUTOUT)
// The inputs from the varyings, the frame flipped on back faces. Depth passes keep the frame at its defaults.
Graphite_SurfaceData graphite_StandardInputs()
{
    Graphite_SurfaceData i = Graphite_NewSurfaceData(vUV, vUV1);
    i.vertexColor = vColor;
    i.relativePos = vRelativePos;
    i.viewDir = Graphite_ViewDir(vRelativePos);
#ifdef GRAPHITE_STANDARD_LIT
    i.geometryNormal = normalize(vNormal);
    i.tangent = normalize(vTangent);
    i.bitangent = normalize(vBitangent);
    if (!gl_FrontFacing)
    {
        i.geometryNormal = -i.geometryNormal;
        i.tangent = -i.tangent;
        i.bitangent = -i.bitangent;
    }
#endif
    return i;
}
#endif

#ifdef GRAPHITE_STANDARD_FORWARD

layout(location = 0) out vec4 fragColor;

#ifdef GRAPHITE_STANDARD_UNLIT

void Graphite_StandardFragment()
{
    Graphite_SurfaceData i = graphite_StandardInputs();
    Unlit_Params p = Unlit_MaterialParams(i);
#ifdef GRAPHITE_SURFACE_FUNCTION
    Surface(p, i);
#endif
#ifdef _SURFACE_CUTOUT
    if (p.alpha < alpha_cutoff)
        discard;
#endif

    vec3 color = Graphite_SaturateHalf(Graphite_ApplyFog(p.baseColor, vRelativePos));
#ifdef GRAPHITE_STANDARD_DEBUG
    color = Graphite_DebugView(color, p.baseColor, vec3(0.0), 1.0, 0.0, 1.0, vec3(0.0), vRelativePos, vec2(0.0));
#endif
#ifdef _SURFACE_TRANSPARENT
    fragColor = vec4(color, p.alpha);
#else
    Graphite_WriteGBuffer(vec3(0.0, 1.0, 0.0), vec3(0.0), 1.0);
    fragColor = vec4(color, 1.0);
#endif
}

#else

// The PBR model: material inputs, decals, the Surface function, then the closure lit by every light and its
// surroundings. Transparent surfaces write premultiplied colour with geometry_opacity as coverage, which the
// "Output" = "Premultiplied" tag of the model's passes has SurfaceSettings blend once. Transmissive ones always draw
// so, adding what they let through from behind, and write their motion and their coverage as the reactive mask, see
// PBR_ReactiveCoverage.
void Graphite_StandardFragment()
{
    Graphite_SurfaceData i = graphite_StandardInputs();
    float mapVariance;
    PBR_Params p = PBR_MaterialParams(i, vHasTangents > 0.5, mapVariance);
    Graphite_ApplyDecals(vRelativePos, i.geometryNormal, p.baseColor, p.geometryNormal, p.specularRoughness, p.baseMetalness, p.occlusion);
#ifdef GRAPHITE_SURFACE_FUNCTION
    Surface(p, i);
#endif
#ifdef _SURFACE_CUTOUT
    if (p.geometryOpacity < alpha_cutoff)
        discard;
#endif
#ifdef GRAPHITE_STANDARD_DEBUG
    Graphite_DebugInputs(p.baseColor, p.baseMetalness);
#endif

    PBR_Closure c = PBR_Prepare(p, vRelativePos, i.viewDir, i.geometryNormal, mapVariance);
    vec3 direct = PBR_ShadeLights(c);
    Graphite_ProbeSet probes = PBR_FindProbes(c);
    PBR_Indirect indirect = PBR_GatherIndirect(c, vLightmapUV, probes);
    vec3 ambient = Graphite_AmbientBRDF(c, indirect);
    vec3 transmitted = vec3(0.0);
    vec3 transmittedWhite = vec3(0.0);
    float pathLength = 0.0;
#ifdef PBR_TRANSMISSION
    PBR_Transmitted behind = PBR_GatherTransmitted(c, p, probes);
    transmitted = PBR_Transmittance(c) * behind.light;
    transmittedWhite = PBR_Transmittance(c) * behind.throughput;
    pathLength = behind.pathLength;
#endif
    vec3 color = Graphite_SaturateHalf(Graphite_ApplyFog(direct + ambient + transmitted + c.emission * Graphite_Exposure(), vRelativePos));
#ifdef GRAPHITE_STANDARD_DEBUG
    color = PBR_DebugView(color, p, c, direct, ambient, indirect, vLightmapUV, transmitted, transmittedWhite, pathLength);
#endif
#if defined(_SURFACE_TRANSPARENT) || defined(PBR_TRANSMISSION)
    fragColor = vec4(color * c.opacity, c.opacity);
#ifdef PBR_TRANSMISSION
    Graphite_WriteMotion(graphite_MotionCurrent, graphite_MotionPrevious);
    Graphite_WriteReactive(PBR_ReactiveCoverage(c, p));
#endif
#else
    vec3 topNormal;
    float topRoughness;
    vec3 topReflectance = PBR_TopReflectance(c, topNormal, topRoughness);
    Graphite_WriteGBuffer(topNormal, topReflectance, topRoughness);
    fragColor = vec4(color, 1.0);
#endif
}

#endif

#elif defined(GRAPHITE_STANDARD_BACK_DEPTH)

layout(location = 0) out float fragDistance;

// Back depth: the far side's distance along the pixel's view ray, cut out where the alpha test fails
void Graphite_StandardFragment()
{
#ifdef _SURFACE_CUTOUT
    Graphite_SurfaceData i = graphite_StandardInputs();
    float mapVariance;
    PBR_Params p = PBR_MaterialParams(i, false, mapVariance);
#ifdef GRAPHITE_SURFACE_FUNCTION
    Surface(p, i);
#endif
    if (p.geometryOpacity < alpha_cutoff)
        discard;
#endif
    fragDistance = dot(vRelativePos, Graphite_PixelRay());
}

#else

#ifdef GRAPHITE_STANDARD_SHADOW_DITHER
// A 4 x 4 ordered dither the shadow filter averages evenly into the fraction let through. An orthographic shadow view
// takes it on the light's texel grid from the world origin, so it stays on the caster as the cascades refit.
bool graphite_ShadowDiscards(float transmittance)
{
    Graphite_ViewData v = Graphite_View();
    vec2 cell = gl_FragCoord.xy;
    if (v.projection[3][3] > 0.5)
    {
        float texel = 2.0 / abs(v.projection[0][0] * v.screen.x);
        vec2 high = (v.view * vec4(v.cameraHigh.xyz, 0.0)).xy / texel;
        vec2 low = (v.view * vec4(v.cameraLow.xyz + vRelativePos, 0.0)).xy / texel;
        cell = mod(high, 4096.0) + low;
    }
    vec2 texel = floor(cell);
    ivec2 p = ivec2(mod(texel, 4.0));
    const float bayer[16] = float[](0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0);
    // Each 4 x 4 block offsets its thresholds by its own fraction, so the mean is exact between the sixteenths
    uvec2 block = uvec2(ivec2(floor(texel / 4.0)));
    uint h = block.x * 1664525u + block.y * 1013904223u;
    h ^= h >> 16u;
    h *= 2246822519u;
    h ^= h >> 13u;
    float offset = float(h >> 8u) / 16777216.0;
    return (bayer[p.y * 4 + p.x] + offset) / 16.0 < transmittance;
}

// The cosine between the face and the light's ray through this fragment, for any projection
float graphite_ShadowCosine()
{
    vec4 clip = Graphite_View().viewProjection * vec4(vRelativePos, 1.0);
    vec3 ray = Graphite_ViewRay(clip.xy / clip.w);
    vec3 normal = normalize(cross(dFdx(vRelativePos), dFdy(vRelativePos)));
    return clamp(abs(dot(normal, ray)), 1e-4, 1.0);
}
#endif

// Prepass and shadow caster: depth only, cut out where the alpha test fails. Dithered casters then let their
// transmittance through.
void Graphite_StandardFragment()
{
#if defined(_SURFACE_CUTOUT) || defined(GRAPHITE_STANDARD_SHADOW_DITHER)
    Graphite_SurfaceData i = graphite_StandardInputs();
#ifdef GRAPHITE_STANDARD_UNLIT
    Unlit_Params p = Unlit_MaterialParams(i);
#ifdef GRAPHITE_SURFACE_FUNCTION
    Surface(p, i);
#endif
#ifdef _SURFACE_CUTOUT
    if (p.alpha < alpha_cutoff)
        discard;
#endif
#ifdef GRAPHITE_STANDARD_SHADOW_DITHER
    if (graphite_ShadowDiscards(1.0 - p.alpha))
        discard;
#endif
#else
#ifdef GRAPHITE_STANDARD_SHADOW_DITHER
    float cosine = graphite_ShadowCosine();
#endif
    float mapVariance;
    PBR_Params p = PBR_MaterialParams(i, false, mapVariance);
#ifdef GRAPHITE_SURFACE_FUNCTION
    Surface(p, i);
#endif
#ifdef _SURFACE_CUTOUT
    if (p.geometryOpacity < alpha_cutoff)
        discard;
#endif
#ifdef GRAPHITE_STANDARD_SHADOW_DITHER
#if defined(_SURFACE_TRANSPARENT) || defined(PBR_TRANSMISSION)
    float coverage = p.geometryOpacity;
#else
    float coverage = 1.0;
#endif
    if (graphite_ShadowDiscards(PBR_ShadowTransmittance(p, coverage, cosine)))
        discard;
#endif
#endif
#endif
}

#endif

#endif
