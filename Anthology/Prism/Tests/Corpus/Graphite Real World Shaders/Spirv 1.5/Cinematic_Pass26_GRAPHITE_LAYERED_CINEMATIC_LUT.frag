#version 460
#extension GL_GOOGLE_include_directive : enable
#line 1 "Graphite/Cinematic"

#extension GL_GOOGLE_cpp_style_line_directive : require
#line 7 "Graphite/Cinematic"

#line 1 "Graphite/Fullscreen.glsl"


#pragma variantGRAPHITE_LAYERED
#pragma variantGRAPHITE_NO_SOURCE

#extension GL_EXT_multiview : require

































layout(location = 0) in vec2 vUV;

layout(location = 0) out vec4 outColor;


layout(set = 0, binding = 0) uniform sampler2DArray Graphite_Source;
vec4 Graphite_SampleSource(vec2 uv) { return texture(Graphite_Source, vec3(uv, float(gl_ViewIndex))); }


#line 9 "Graphite/Cinematic"
#line 1 "Graphite/Internal/Cinematic.glsl"
#pragma variantCINEMATICVIGNETTEPIXELATIONCHROMATIC_ABERRATIONSHARPENEDGE_DETECTIONCOLOR_GRADINGLUTFILM_GRAIN



layout(set = 0, binding = 1, std140) uniform _GraphiteGlobal { vec2 _Resolution; float _LUTContribution; float _LUTSize; };

float Luminance(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }
































































































layout(set = 0, binding = 2) uniform sampler2D _LUTTex;




void main()
{
    vec4 source = Graphite_SampleSource(vUV);
    float size = _LUTSize;
    float invSize = 1.0 / size;
    vec3 color = clamp(source.rgb, 0.0, 1.0);
    float blueScaled = color.b * (size - 1.0);
    float sliceLow = floor(blueScaled);
    float sliceHigh = min(sliceLow + 1.0, size - 1.0);
    float sliceFrac = blueScaled - sliceLow;
    float halfTexel = 0.5 * invSize;
    vec2 innerUV = vec2(halfTexel + color.r * (1.0 - invSize), halfTexel + color.g * (1.0 - invSize));
    vec3 graded1 = texture(_LUTTex, vec2(innerUV.x * invSize + sliceLow * invSize, innerUV.y)).rgb;
    vec3 graded2 = texture(_LUTTex, vec2(innerUV.x * invSize + sliceHigh * invSize, innerUV.y)).rgb;
    outColor = vec4(mix(color, mix(graded1, graded2, sliceFrac), _LUTContribution), source.a);
}
































#line 10 "Graphite/Cinematic"

