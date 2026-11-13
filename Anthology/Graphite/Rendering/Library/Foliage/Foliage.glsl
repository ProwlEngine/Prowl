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

// Light from behind passes through, tinted by the diffuse albedo, wrapped around the far side and brightest looking
// toward the light
vec3 Graphite_BRDF(PBR_Closure c, Graphite_LightContribution l)
{
    vec3 front = PBR_EvaluateLight(c, l);
    float behind = max(dot(-c.normal, l.diffuseL), 0.0) * 0.6 + 0.4 * max(-dot(c.geomNormal, l.diffuseL), 0.0);
    float toward = pow(max(dot(-c.view, l.diffuseL), 0.0), 4.0);
    vec3 through = c.diffuseAlbedo * translucency_color.rgb * (translucency * (behind + toward) * l.attenuation);
    return front + through * l.radiance;
}

#endif

#endif
