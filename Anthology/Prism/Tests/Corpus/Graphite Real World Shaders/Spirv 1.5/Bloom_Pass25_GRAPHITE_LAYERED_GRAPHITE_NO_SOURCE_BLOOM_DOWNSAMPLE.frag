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






#line 9 "Graphite/Bloom"
#line 1 "Graphite/Internal/Bloom.glsl"
#pragma variantBLOOMCOMPOSITEPREFILTERDOWNSAMPLEUPSAMPLE



float Luminance(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }


































layout(set = 0, binding = 0) uniform sampler2DArray _MainTex;

void main()
{
    vec2 texel = 1.0 / vec2(textureSize(_MainTex, 0).xy);
    vec4 sum = texture(_MainTex, vec3(vUV, float(gl_ViewIndex))) * 4.0;
    sum += texture(_MainTex, vec3(vUV - texel, float(gl_ViewIndex)));
    sum += texture(_MainTex, vec3(vUV + texel, float(gl_ViewIndex)));
    sum += texture(_MainTex, vec3(vUV + vec2(texel.x, - texel.y), float(gl_ViewIndex)));
    sum += texture(_MainTex, vec3(vUV - vec2(texel.x, - texel.y), float(gl_ViewIndex)));
    outColor = sum / 8.0;
}




























#line 10 "Graphite/Bloom"

