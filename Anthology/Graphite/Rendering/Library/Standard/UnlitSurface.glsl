// The Unlit model's inputs: base_color times its map and the vertex colour, the map's alpha included, fogged. See
// Standard.glsl.
#pragma once

#define GRAPHITE_STANDARD_UNLIT
#include "Graphite/Standard/Standard.glsl"

#ifdef GRAPHITE_FRAGMENT

#ifdef GRAPHITE_STANDARD_FORWARD
#include "Graphite/Fog.glsl"
#endif

#if defined(GRAPHITE_STANDARD_FORWARD) || defined(GRAPHITE_STANDARD_ALPHA)
uniform vec4 base_color;
uniform sampler2D base_color_map;
#endif

#ifdef _SURFACE_CUTOUT
uniform float alpha_cutoff;
#endif

// What an Unlit Surface function may change
struct Unlit_Params
{
    vec3 baseColor;
    float alpha;
};

#if defined(GRAPHITE_STANDARD_FORWARD) || defined(GRAPHITE_STANDARD_ALPHA)
Unlit_Params Unlit_MaterialParams(Graphite_SurfaceData i)
{
    vec4 color = base_color * i.vertexColor;
    if (base_color_map_Present)
        color *= texture(base_color_map, Graphite_StandardUV(base_color_map_UV, base_color_map_ST, base_color_map_Rotation, i));
    Unlit_Params p;
    p.baseColor = color.rgb;
    p.alpha = color.a;
    return p;
}
#endif

#endif
