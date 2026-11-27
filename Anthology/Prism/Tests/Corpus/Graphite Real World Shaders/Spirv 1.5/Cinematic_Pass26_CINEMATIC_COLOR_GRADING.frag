#version 460
#extension GL_GOOGLE_include_directive : enable
#line 1 "Graphite/Cinematic"

#extension GL_GOOGLE_cpp_style_line_directive : require
#line 7 "Graphite/Cinematic"

#line 1 "Graphite/Fullscreen.glsl"


#pragma variantGRAPHITE_LAYERED
#pragma variantGRAPHITE_NO_SOURCE



































layout(location = 0) in vec2 vUV;

layout(location = 0) out vec4 outColor;


layout(set = 0, binding = 0) uniform sampler2D Graphite_Source;
vec4 Graphite_SampleSource(vec2 uv) { return texture(Graphite_Source, uv); }


#line 9 "Graphite/Cinematic"
#line 1 "Graphite/Internal/Cinematic.glsl"
#pragma variantCINEMATICVIGNETTEPIXELATIONCHROMATIC_ABERRATIONSHARPENEDGE_DETECTIONCOLOR_GRADINGLUTFILM_GRAIN



layout(set = 0, binding = 1, std140) uniform _GraphiteGlobal { vec2 _Resolution; float _PostExposure; float _Contrast; float _Saturation; float _Temperature; vec4 _Lift; vec4 _Gamma; vec4 _Gain; };

float Luminance(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }
















































































void main()
{
    vec4 source = Graphite_SampleSource(vUV);
    vec3 color = source.rgb * exp2(_PostExposure);
    color = mix(vec3(0.18), color, 1.0 + _Contrast);
    color.r *= 1.0 + _Temperature * 0.1;
    color.b *= 1.0 - _Temperature * 0.1;
    color = mix(vec3(Luminance(color)), color, 1.0 + _Saturation);


    color *= vec3(1.0) + _Gain.rgb;
    color += _Lift.rgb * (1.0 - color);
    color = pow(max(color, vec3(0.0)), 1.0 / max(vec3(1.0) + _Gamma.rgb, vec3(0.01)));
    outColor = vec4(max(color, vec3(0.0)), source.a);
}






















































#line 10 "Graphite/Cinematic"

