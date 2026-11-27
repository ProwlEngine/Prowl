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



layout(set = 0, binding = 1, std140) uniform _GraphiteGlobal { vec2 _Resolution; float _EdgeIntensity; vec4 _EdgeColor; float _EdgeBackgroundFade; };

float Luminance(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }



















































float LumaAt(vec2 uv) { return Luminance(Graphite_SampleSource(uv).rgb); }


void main()
{
    vec2 texel = 1.0 / _Resolution;
    float tl = LumaAt(vUV + vec2(- texel.x, texel.y));
    float t = LumaAt(vUV + vec2(0.0, texel.y));
    float tr = LumaAt(vUV + vec2(texel.x, texel.y));
    float l = LumaAt(vUV + vec2(- texel.x, 0.0));
    float r = LumaAt(vUV + vec2(texel.x, 0.0));
    float bl = LumaAt(vUV + vec2(- texel.x, - texel.y));
    float b = LumaAt(vUV + vec2(0.0, - texel.y));
    float br = LumaAt(vUV + vec2(texel.x, - texel.y));
    float gx = - tl - 2.0 * l - bl + tr + 2.0 * r + br;
    float gy = - tl - 2.0 * t - tr + bl + 2.0 * b + br;
    float edge = clamp(sqrt(gx * gx + gy * gy) * _EdgeIntensity, 0.0, 1.0);
    vec4 source = Graphite_SampleSource(vUV);
    outColor = vec4(mix(source.rgb * _EdgeBackgroundFade, _EdgeColor.rgb, edge), source.a);
}














































































#line 10 "Graphite/Cinematic"

