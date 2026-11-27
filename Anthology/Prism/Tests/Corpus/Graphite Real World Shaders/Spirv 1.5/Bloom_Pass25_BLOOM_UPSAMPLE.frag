#version 460
#extension GL_GOOGLE_include_directive : enable
#line 1 "Graphite/Bloom"

#extension GL_GOOGLE_cpp_style_line_directive : require
#line 7 "Graphite/Bloom"

#line 1 "Graphite/Fullscreen.glsl"


#pragma variantGRAPHITE_LAYERED
#pragma variantGRAPHITE_NO_SOURCE



































layout(location = 0) in vec2 vUV;

layout(location = 0) out vec4 outColor;


layout(set = 0, binding = 0) uniform sampler2D Graphite_Source;
vec4 Graphite_SampleSource(vec2 uv) { return texture(Graphite_Source, uv); }


#line 9 "Graphite/Bloom"
#line 1 "Graphite/Internal/Bloom.glsl"
#pragma variantBLOOMCOMPOSITEPREFILTERDOWNSAMPLEUPSAMPLE



float Luminance(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }















































layout(set = 0, binding = 1) uniform sampler2D _MainTex;
layout(set = 0, binding = 2) uniform sampler2D _HighTex;
layout(set = 0, binding = 3, std140) uniform _GraphiteGlobal { float _Scatter; };

void main()
{
    vec2 halfpixel = 0.5 / vec2(textureSize(_MainTex, 0));
    vec4 sum = texture(_MainTex, vUV + vec2(- halfpixel.x * 2.0, 0.0));
    sum += texture(_MainTex, vUV + vec2(- halfpixel.x, halfpixel.y)) * 2.0;
    sum += texture(_MainTex, vUV + vec2(0.0, halfpixel.y * 2.0));
    sum += texture(_MainTex, vUV + vec2(halfpixel.x, halfpixel.y)) * 2.0;
    sum += texture(_MainTex, vUV + vec2(halfpixel.x * 2.0, 0.0));
    sum += texture(_MainTex, vUV + vec2(halfpixel.x, - halfpixel.y)) * 2.0;
    sum += texture(_MainTex, vUV + vec2(0.0, - halfpixel.y * 2.0));
    sum += texture(_MainTex, vUV + vec2(- halfpixel.x, - halfpixel.y)) * 2.0;
    outColor = mix(texture(_HighTex, vUV), sum / 12.0, _Scatter);
}










#line 10 "Graphite/Bloom"

