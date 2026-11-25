// What a deferred view's lighting program starts with, before the light functions of the renderer's shading models and
// Graphite/Internal/DeferredLighting.glsl: the closure and the lights, shaded through a Graphite_BRDF the program
// writes, which picks the light function of the pixel's shading model. See ShadingModels.
#pragma once

#define GRAPHITE_GBUFFER_READ
#define PBR_NO_SPECULAR_AA
#define GRAPHITE_CUSTOM_BRDF
#define GRAPHITE_RECEIVES_SHADOWS graphite_DeferredShadows
#define GRAPHITE_SURFACE_LAYERS graphite_DeferredLayers

bool graphite_DeferredShadows = true;
uint graphite_DeferredLayers = 0xFFFFFFFFu;

// The shading model and Custom values of the pixel being lit
uint graphite_DeferredModel = 0u;
vec4 graphite_DeferredCustom = vec4(0.0);

#include "Graphite/PBR/Closure.glsl"
#include "Graphite/PBR/GBuffer.glsl"
