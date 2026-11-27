#version 460
#extension GL_GOOGLE_include_directive : enable
#line 1 "Graphite/AutoExposure"

#extension GL_GOOGLE_cpp_style_line_directive : require
#line 7 "Graphite/AutoExposure"

#line 1 "Graphite/Fullscreen.glsl"


#pragma variantGRAPHITE_LAYERED
#pragma variantGRAPHITE_NO_SOURCE

#extension GL_EXT_multiview : require

































layout(location = 0) in vec2 vUV;

layout(location = 0) out vec4 outColor;






#line 9 "Graphite/AutoExposure"
#line 1 "Graphite/Internal/AutoExposure.glsl"
#pragma variantAEAPPLYEXTRACTDOWNSAMPLEADAPT
#pragma variantAE_LAYERED_SOURCE




float Luminance(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }



layout(set = 0, binding = 0) uniform sampler2DArray _MainTex;
vec3 SampleMain(vec2 uv) { return texture(_MainTex, vec3(uv, 0.0)).rgb; }
vec2 MainSize() { return vec2(textureSize(_MainTex, 0).xy); }






void main()
{
    vec2 texel = 1.0 / MainSize();
    float l0 = log(max(Luminance(SampleMain(vUV + vec2(- 0.5, - 0.5) * texel)), 0.0001));
    float l1 = log(max(Luminance(SampleMain(vUV + vec2(0.5, - 0.5) * texel)), 0.0001));
    float l2 = log(max(Luminance(SampleMain(vUV + vec2(- 0.5, 0.5) * texel)), 0.0001));
    float l3 = log(max(Luminance(SampleMain(vUV + vec2(0.5, 0.5) * texel)), 0.0001));
    outColor = vec4( (l0 + l1 + l2 + l3) * 0.25, 0.0, 0.0, 1.0);
}















































#line 10 "Graphite/AutoExposure"

