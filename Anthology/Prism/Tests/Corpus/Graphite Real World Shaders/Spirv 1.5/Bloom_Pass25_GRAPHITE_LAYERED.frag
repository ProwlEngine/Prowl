#version 460
#extension GL_GOOGLE_include_directive : enable
#line 1 "Graphite/Bloom"

#extension GL_GOOGLE_cpp_style_line_directive : require
#line 7 "Graphite/Bloom"

#line 1 "Graphite/Fullscreen.glsl"


#pragma variantGRAPHITE_LAYERED
#pragma variantGRAPHITE_NO_SOURCE

#extension GL_EXT_multiview : require

































layout(location = 0) in vec2 vUV;

layout(location = 0) out vec4 outColor;


layout(set = 0, binding = 0) uniform sampler2DArray Graphite_Source;
vec4 Graphite_SampleSource(vec2 uv) { return texture(Graphite_Source, vec3(uv, float(gl_ViewIndex))); }


#line 9 "Graphite/Bloom"
#line 1 "Graphite/Internal/Bloom.glsl"
#pragma variantBLOOMCOMPOSITEPREFILTERDOWNSAMPLEUPSAMPLE



float Luminance(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

































































layout(set = 0, binding = 1) uniform sampler2DArray _BloomTex;
layout(set = 0, binding = 2, std140) uniform _GraphiteGlobal { float _Intensity; };

void main()
{
    vec4 color = Graphite_SampleSource(vUV);
    outColor = vec4(color.rgb + texture(_BloomTex, vec3(vUV, float(gl_ViewIndex))).rgb * _Intensity * 2.0, color.a);
}

#line 10 "Graphite/Bloom"

