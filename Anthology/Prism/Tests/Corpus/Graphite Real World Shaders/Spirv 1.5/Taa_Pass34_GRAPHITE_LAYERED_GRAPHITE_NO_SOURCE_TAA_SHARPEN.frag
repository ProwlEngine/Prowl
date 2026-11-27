#version 460
#extension GL_GOOGLE_include_directive : enable
#line 1 "Graphite/Taa"

#extension GL_GOOGLE_cpp_style_line_directive : require
#line 7 "Graphite/Taa"

#line 1 "Graphite/Fullscreen.glsl"


#pragma variantGRAPHITE_LAYERED
#pragma variantGRAPHITE_NO_SOURCE

#extension GL_EXT_multiview : require

































layout(location = 0) in vec2 vUV;

layout(location = 0) out vec4 outColor;






#line 9 "Graphite/Taa"
#line 1 "Graphite/Internal/Taa.glsl"
#pragma variantTAA_SHARPEN





float Taa_MaxChannel(vec3 c) { return max(c.r, max(c.g, c.b)); }


vec3 Taa_Tonemap(vec3 c) { return c / (1.0 + Taa_MaxChannel(c)); }
vec3 Taa_InverseTonemap(vec3 c) { return c / max(1.0 - Taa_MaxChannel(c), 1e-4); }


layout(set = 0, binding = 0) uniform sampler2DArray _Resolved;
layout(set = 0, binding = 1, std140) uniform _GraphiteGlobal { vec2 _Resolution; float _Sharpness; };


float Taa_Luma(vec3 c) { return dot(c, vec3(0.299, 0.587, 0.114)); }


void main()
{
    vec2 texel = 1.0 / _Resolution;
    vec3 center = Taa_Tonemap(texture(_Resolved, vec3(vUV, float(gl_ViewIndex))).rgb);
    float blur = (Taa_Luma(Taa_Tonemap(texture(_Resolved, vec3(vUV + vec2(- texel.x, 0.0), float(gl_ViewIndex))).rgb))
        + Taa_Luma(Taa_Tonemap(texture(_Resolved, vec3(vUV + vec2(texel.x, 0.0), float(gl_ViewIndex))).rgb))
        + Taa_Luma(Taa_Tonemap(texture(_Resolved, vec3(vUV + vec2(0.0, - texel.y), float(gl_ViewIndex))).rgb))
        + Taa_Luma(Taa_Tonemap(texture(_Resolved, vec3(vUV + vec2(0.0, texel.y), float(gl_ViewIndex))).rgb))) * 0.25;
    float luma = Taa_Luma(center);
    float sharpened = max(luma + (luma - blur) * _Sharpness, 0.0);
    vec3 result = luma > 1e-5 ? center * (sharpened / luma) : center;
    outColor = vec4(Taa_InverseTonemap(min(result, vec3(0.999))), texture(_Resolved, vec3(vUV, float(gl_ViewIndex))).a);
}























































































































#line 10 "Graphite/Taa"

