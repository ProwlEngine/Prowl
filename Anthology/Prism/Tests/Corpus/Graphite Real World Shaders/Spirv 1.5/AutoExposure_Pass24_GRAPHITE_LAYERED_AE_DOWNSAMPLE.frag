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


layout(set = 0, binding = 0) uniform sampler2DArray Graphite_Source;
vec4 Graphite_SampleSource(vec2 uv) { return texture(Graphite_Source, vec3(uv, float(gl_ViewIndex))); }


#line 9 "Graphite/AutoExposure"
#line 1 "Graphite/Internal/AutoExposure.glsl"
#pragma variantAEAPPLYEXTRACTDOWNSAMPLEADAPT
#pragma variantAE_LAYERED_SOURCE




float Luminance(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }






















layout(set = 0, binding = 1) uniform sampler2D _MainTex;

void main()
{
    vec2 texel = 1.0 / vec2(textureSize(_MainTex, 0));
    float s0 = texture(_MainTex, vUV + vec2(- 0.5, - 0.5) * texel).r;
    float s1 = texture(_MainTex, vUV + vec2(0.5, - 0.5) * texel).r;
    float s2 = texture(_MainTex, vUV + vec2(- 0.5, 0.5) * texel).r;
    float s3 = texture(_MainTex, vUV + vec2(0.5, 0.5) * texel).r;
    outColor = vec4( (s0 + s1 + s2 + s3) * 0.25, 0.0, 0.0, 1.0);
}



































#line 10 "Graphite/AutoExposure"

