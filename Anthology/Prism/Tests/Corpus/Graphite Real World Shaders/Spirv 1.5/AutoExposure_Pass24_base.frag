#version 460
#extension GL_GOOGLE_include_directive : enable
#line 1 "Graphite/AutoExposure"

#extension GL_GOOGLE_cpp_style_line_directive : require
#line 7 "Graphite/AutoExposure"

#line 1 "Graphite/Fullscreen.glsl"


#pragma variantGRAPHITE_LAYERED
#pragma variantGRAPHITE_NO_SOURCE



































layout(location = 0) in vec2 vUV;

layout(location = 0) out vec4 outColor;


layout(set = 0, binding = 0) uniform sampler2D Graphite_Source;
vec4 Graphite_SampleSource(vec2 uv) { return texture(Graphite_Source, uv); }


#line 9 "Graphite/AutoExposure"
#line 1 "Graphite/Internal/AutoExposure.glsl"
#pragma variantAEAPPLYEXTRACTDOWNSAMPLEADAPT
#pragma variantAE_LAYERED_SOURCE




float Luminance(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }























































layout(set = 0, binding = 1) uniform sampler2D _AdaptedTex;
layout(set = 0, binding = 2, std140) uniform _GraphiteGlobal { float _ExposureComp; float _MinExposure; float _MaxExposure; };



void main()
{
    vec4 color = Graphite_SampleSource(vUV);
    float exposure = 0.18 / max(texture(_AdaptedTex, vec2(0.5)).r, 0.0001);
    exposure = clamp(exposure * exp2(_ExposureComp), _MinExposure, _MaxExposure);
    outColor = vec4(color.rgb * exposure, color.a);
}

#line 10 "Graphite/AutoExposure"

