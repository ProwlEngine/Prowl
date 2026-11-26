// What a deferred view's lighting program starts with, before the light functions of the renderer's shading models and
// Graphite/Internal/DeferredLighting.glsl: the closure and the lights, shaded through a Graphite_BRDF and a
// Graphite_AmbientBRDF the program writes, which pick the functions of the pixel's shading model. See ShadingModels.
#pragma once
#pragma variant global PBR_TIER FULL LITE MAX
#pragma variant GRAPHITE_CONTACT_SHADOWS

// Local lights' cookies and profiles, which cost a flag test for the lights without them
#define GRAPHITE_LIGHT_MASKS 1

#define GRAPHITE_GBUFFER_READ
#define PBR_NO_SPECULAR_AA
#define GRAPHITE_CUSTOM_BRDF
#define GRAPHITE_CUSTOM_AMBIENT_BRDF
#define GRAPHITE_RECEIVES_SHADOWS graphite_DeferredShadows
#define GRAPHITE_SURFACE_LAYERS graphite_DeferredLayers

#define PBR_LIGHT_WRAPS graphite_DeferredWraps
#ifdef GRAPHITE_CONTACT_SHADOWS
#define GRAPHITE_CONTACT_SHADOW graphite_ContactShadow

float graphite_ContactShadow(vec3 position, vec3 toLight, float distance, int directional);

// The sun's contact shadow at the pixel, marched before the pixel is shaded so the march and the shading never hold
// their working values at once
float graphite_SunContact = 1.0;
#endif
#define GRAPHITE_LIGHT_FRONT_WHEN graphite_DeferredFront

bool graphite_DeferredShadows = true;
uint graphite_DeferredLayers = 0xFFFFFFFFu;

// Whether the lights behind the pixel light it too, set for the pixels of a model marked LightWraps
bool graphite_DeferredWraps = false;

// Whether the pixel takes nothing from a directional light behind it, as the models lit by the PBR model's own light do
bool graphite_DeferredFront = false;

// The shading model and Custom values of the pixel being lit
uint graphite_DeferredModel = 0u;
vec4 graphite_DeferredCustom = vec4(0.0);

#include "Graphite/PBR/Closure.glsl"
#include "Graphite/PBR/GBuffer.glsl"

#ifdef GRAPHITE_CONTACT_SHADOWS
// The view's contact shadows: x how far a ray reaches as a part of the point's distance from the camera, y its steps,
// z how thick what the depth shows is taken to be, as a part of that distance, w 1 for local lights too
uniform vec4 Graphite_ContactShadows;

// How far in front of the camera a reversed Z depth lies, under either kind of projection
float graphite_DepthDistance(mat4 projection, float depth)
{
    return abs((projection[3][2] - depth * projection[3][3]) / (depth * projection[2][3] - projection[2][2]));
}

// A short march through the view's depth from a point toward a light, started at a different place each pixel and
// frame: 0 where something the depth shows stands between them, else 1
float graphite_MarchContactShadow(vec3 position, vec3 toLight, float distance)
{
    vec4 settings = Graphite_ContactShadows;
    Graphite_ViewData view = Graphite_View();
    vec4 from = view.viewProjection * vec4(position, 1.0);
    float depth = graphite_DepthDistance(view.projection, from.z / from.w);
    float thickness = settings.z * depth;
    vec4 to = view.viewProjection * vec4(position + toLight * min(settings.x * depth, distance), 1.0);
    if (to.w <= 0.0)
        return 1.0;
    float steps = max(settings.y, 1.0);
    float start = fract(52.9829189 * fract(dot(gl_FragCoord.xy + view.time.z * 5.588238, vec2(0.06711056, 0.00583715))));
    for (float i = 0.0; i < steps; i++)
    {
        vec4 clip = mix(from, to, (i + start) / steps);
        vec3 ndc = clip.xyz / clip.w;
        vec2 uv = vec2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5);
        if (any(lessThan(uv, vec2(0.0))) || any(greaterThanEqual(uv, vec2(1.0))))
            return 1.0;
        float behind = graphite_DepthDistance(view.projection, ndc.z) - graphite_DepthDistance(view.projection, Graphite_GBufferDepthAt(ivec2(uv * view.screen.xy)));
        if (behind > thickness * 0.1 && behind < thickness)
            return 0.0;
    }
    return 1.0;
}

float graphite_ContactShadow(vec3 position, vec3 toLight, float distance, int directional)
{
    if (directional == 0)
        return graphite_SunContact;
    if (directional < 0 && Graphite_ContactShadows.w < 0.5)
        return 1.0;
    return graphite_MarchContactShadow(position, toLight, distance);
}

// Marches the sun's contact shadow for a point that faces it, for graphite_ContactShadow to give when the sun is shaded
void graphite_FindSunContact(vec3 position, vec3 normal, bool receivesShadows)
{
    graphite_SunContact = 1.0;
    if (!receivesShadows || Graphite_DirectionalCount() == 0u)
        return;
    Graphite_DirectionalLight sun = Graphite_GetDirectional(0u);
    if (sun.shadow && dot(normal, sun.direction) > 0.0)
        graphite_SunContact = graphite_MarchContactShadow(position, sun.direction, 1e30);
}
#endif
