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



layout(set = 0, binding = 1, std140) uniform _GraphiteGlobal { vec2 _Resolution; float _ChromaticIntensity; float _ChromaticDistortion; };

float Luminance(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }















void main()
{
    vec2 center = vUV - 0.5;
    float dist2 = dot(center, center);
    float intensity = _ChromaticIntensity / _Resolution.x;
    vec2 offsetR = center * (1.0 + dist2 * _ChromaticDistortion) * intensity;
    vec2 offsetB = center * (1.0 - dist2 * _ChromaticDistortion) * intensity;
    vec4 source = Graphite_SampleSource(vUV);
    outColor = vec4(Graphite_SampleSource(vUV + offsetR).r, source.g, Graphite_SampleSource(vUV - offsetB).b, source.a);
}




























































































































#line 10 "Graphite/Cinematic"

