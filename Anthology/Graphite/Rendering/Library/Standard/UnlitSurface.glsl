// The Unlit model's inputs: a texture times a tint and the vertex colour. See Standard.glsl.
#pragma once

#ifdef GRAPHITE_FRAGMENT

#if defined(GRAPHITE_STANDARD_FORWARD) || defined(GRAPHITE_STANDARD_ALPHA)
uniform sampler2D _MainTex;
uniform vec4 _MainTex_ST;
uniform vec4 _MainColor;
uniform int _MainTexUV;
#endif

#ifdef _SURFACE_CUTOUT
uniform float _AlphaCutoff;
#endif

// What an Unlit Surface function may change
struct Unlit_Params
{
    vec3 BaseColor;
    float Alpha;
};

#if defined(GRAPHITE_STANDARD_FORWARD) || defined(GRAPHITE_STANDARD_ALPHA)
Unlit_Params Unlit_MaterialParams(Graphite_SurfaceData i)
{
    vec4 texel = texture(_MainTex, Graphite_StandardUV(_MainTexUV, _MainTex_ST, i)) * _MainColor * i.VertexColor;
    Unlit_Params p;
    p.BaseColor = texel.rgb;
    p.Alpha = texel.a;
    return p;
}
#endif

#endif
