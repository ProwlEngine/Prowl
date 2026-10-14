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



layout(set = 0, binding = 1, std140) uniform _GraphiteGlobal { vec2 _Resolution; float _Time; float _GrainIntensity; float _GrainResponse; };

float Luminance(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }


























































































































float Hash12(vec2 p)
{
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract( (p3.x + p3.y) * p3.z);
}

void main()
{
    vec4 source = Graphite_SampleSource(vUV);
    float noise = Hash12(vUV * _Resolution + fract(_Time) * 1000.0) * 2.0 - 1.0;
    vec3 color = source.rgb + noise * _GrainIntensity * mix(1.0, Luminance(source.rgb), _GrainResponse);
    outColor = vec4(max(color, vec3(0.0)), source.a);
}













#line 10 "Graphite/Cinematic"

