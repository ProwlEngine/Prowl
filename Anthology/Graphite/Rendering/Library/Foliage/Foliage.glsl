// The Foliage surface model's fragment side, included before the Surface function: the PBR light with a translucency
// lobe, so leaves and grass glow with the light behind them, and the frame leant toward up by foliage_normal_lean, so
// thin cards and canopies light softly like the ground they cover rather than by each flat face.
#pragma once

#include "Graphite/Wind.glsl"

uniform float translucency;
uniform vec4 translucency_color;
uniform float foliage_normal_lean;

#ifdef GRAPHITE_FRAGMENT

void Foliage_Inputs(inout Graphite_SurfaceData i)
{
    i.geometryNormal = normalize(mix(i.geometryNormal, vec3(0.0, 1.0, 0.0), clamp(foliage_normal_lean, 0.0, 1.0)));
}

#if defined(GRAPHITE_STANDARD_LIT)

#include "Graphite/Foliage/FoliageLight.glsl"

vec3 Graphite_BRDF(PBR_Closure c, Graphite_LightContribution l)
{
    return Foliage_Light(c, l, translucency_color.rgb, translucency);
}

#endif

#ifdef GRAPHITE_STANDARD_GBUFFER
// Custom holds the translucency colour, and the translucency over its range of 2
void Foliage_GBuffer(inout Graphite_GBuffer g, PBR_Params p)
{
    g.model = GRAPHITE_MODEL_FOLIAGE;
    g.custom = vec4(translucency_color.rgb, translucency * 0.5);
}
#endif

#endif
