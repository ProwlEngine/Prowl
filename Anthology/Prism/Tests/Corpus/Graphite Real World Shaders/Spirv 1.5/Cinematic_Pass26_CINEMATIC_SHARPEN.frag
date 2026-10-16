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



layout(set = 0, binding = 1, std140) uniform _GraphiteGlobal { vec2 _Resolution; float _SharpenAmount; float _SharpenRadius; };

float Luminance(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }





























vec3 Texel(ivec2 texel) { return texelFetch(Graphite_Source, clamp(texel, ivec2(0), ivec2(_Resolution) - 1), 0).rgb; }


void main()
{
    ivec2 texcoord = ivec2(vUV * _Resolution);
    int r = int(_SharpenRadius);
    vec3 a = Texel(texcoord + ivec2(0, - 1) * r);
    vec3 b = Texel(texcoord + ivec2(- 1, 0) * r);
    vec3 c = Texel(texcoord);
    vec3 d = Texel(texcoord + ivec2(1, 0) * r);
    vec3 e = Texel(texcoord + ivec2(0, 1) * r);
    float mn = min(a.g, min(b.g, min(c.g, min(d.g, e.g))));
    float mx = max(a.g, max(b.g, max(c.g, max(d.g, e.g))));
    float w = sqrt(min(1.0 - mx, mn) / max(mx, 0.001)) * mix(- 0.125, - 0.2, _SharpenAmount);
    outColor = vec4( (w * (a + b + d + e) + c) / (4.0 * w + 1.0), Graphite_SampleSource(vUV).a);
}







































































































#line 10 "Graphite/Cinematic"

