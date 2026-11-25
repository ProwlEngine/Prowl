// The lighting pass of a deferred view: every pixel of the GBuffer lit as the forward pass lights a surface, onto the
// scene with its top specular lobe, which screen space reflections read. Its depth tests against the scene's, so a
// surface drawn in front by the prepass keeps its pixel. Unlit pixels pass their emission through. See Graphite/GBuffer.glsl.
// With DEFERRED_FOLIAGE it lights the Two Sided Foliage pixels alone, without it every other.
#define GRAPHITE_GBUFFER_READ
#define PBR_NO_SPECULAR_AA
#define GRAPHITE_RECEIVES_SHADOWS graphite_DeferredShadows
#define GRAPHITE_SURFACE_LAYERS graphite_DeferredLayers

bool graphite_DeferredShadows = true;
uint graphite_DeferredLayers = 0xFFFFFFFFu;

#ifdef DEFERRED_FOLIAGE
#define GRAPHITE_CUSTOM_BRDF
#endif

#include "Graphite/PBR/Closure.glsl"
#include "Graphite/PBR/GBuffer.glsl"

#ifdef DEFERRED_FOLIAGE
#include "Graphite/Foliage/FoliageLight.glsl"

vec4 graphite_DeferredCustom = vec4(0.0);

vec3 Graphite_BRDF(PBR_Closure c, Graphite_LightContribution l)
{
    return Foliage_Light(c, l, graphite_DeferredCustom.rgb, graphite_DeferredCustom.a * 2.0);
}
#endif

layout(location = 4) out vec4 outMaterial;

void main()
{
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    uint model = Graphite_GBufferModelAt(pixel);
#ifdef DEFERRED_FOLIAGE
    if (model != GRAPHITE_MODEL_TWO_SIDED_FOLIAGE)
        discard;
#else
    if (model == GRAPHITE_MODEL_NONE || model == GRAPHITE_MODEL_TWO_SIDED_FOLIAGE)
        discard;
#endif

    float depth = Graphite_GBufferDepthAt(pixel);
    Graphite_GBuffer g = Graphite_ReadGBuffer(pixel);
#ifdef DEFERRED_FOLIAGE
    graphite_DeferredCustom = g.custom;
#endif
    Graphite_ViewData view = Graphite_View();
    vec3 relative = Graphite_RelativeFromDepth(view, gl_FragCoord.xy * view.screen.zw, depth);
    graphite_DeferredShadows = g.receivesShadows;
    graphite_DeferredLayers = g.lightLayers;

    vec3 color = g.emission;
    vec3 reflectance = vec3(0.0);
    float roughness = 1.0;
    if (g.model != GRAPHITE_MODEL_UNLIT)
    {
        PBR_Params p = PBR_FromGBuffer(g);
        Graphite_LightIterator lights = Graphite_BeginLights(relative);
        Graphite_ShadowLights(lights, relative, g.normal);
        PBR_Closure c = PBR_Prepare(p, relative, Graphite_ViewDir(relative), g.normal, 0.0);
        vec3 direct = PBR_ShadeLights(c, lights);
        Graphite_ProbeSet probes = PBR_FindProbes(c);
        PBR_Indirect indirect = PBR_GatherIndirectBaked(c, g.baked, probes);
        color += direct + Graphite_AmbientBRDF(c, indirect);
        vec3 topNormal;
        reflectance = PBR_TopReflectance(c, topNormal, roughness);
    }

    outColor = vec4(Graphite_SaturateHalf(Graphite_ApplyFog(color, relative)), 1.0);
    outMaterial = vec4(reflectance, roughness);
    gl_FragDepth = depth;
}
