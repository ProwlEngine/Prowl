// What a deferred view's lighting program starts with, before the light functions of the renderer's shading models and
// Graphite/Internal/DeferredLighting.glsl: the closure and the lights, shaded through a Graphite_BRDF and a
// Graphite_AmbientBRDF the program writes, which pick the functions of the pixel's shading model. See ShadingModels.
#pragma once
#pragma variant global PBR_TIER FULL LITE MAX

// Local lights' cookies and profiles, which cost a flag test for the lights without them
#define GRAPHITE_LIGHT_MASKS 1

#define GRAPHITE_GBUFFER_READ
#define PBR_NO_SPECULAR_AA
#define GRAPHITE_CUSTOM_BRDF
#define GRAPHITE_CUSTOM_AMBIENT_BRDF
#define GRAPHITE_RECEIVES_SHADOWS graphite_DeferredShadows
#define GRAPHITE_SURFACE_LAYERS graphite_DeferredLayers

#define PBR_LIGHT_WRAPS graphite_DeferredWraps
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
