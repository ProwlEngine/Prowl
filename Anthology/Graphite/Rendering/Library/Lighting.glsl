// Every part of forward lighting: lights and their shadows, reflection probes, ambient and baked light, and fog.
// A shader needing less includes only its part, as lights bring the GRAPHITE_LIGHT_MASKS and GRAPHITE_CONTACT_SHADOWS
// global axes with them.
#pragma once

#include "Graphite/Lights.glsl"
#include "Graphite/Probes.glsl"
#include "Graphite/Ambient.glsl"
#include "Graphite/Fog.glsl"
