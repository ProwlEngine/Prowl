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


layout(set = 0, binding = 1, std140) uniform _GraphiteGlobal { float _Threshold; float _SoftKnee; float _Clamp; float _AntiFlicker; };





vec3 Prefilter(vec3 color)
{
    color = min(max(color, vec3(0.0)), vec3(_Clamp));
    float brightness = max(color.r, max(color.g, color.b));
    float knee = _Threshold * _SoftKnee + 1e-5;
    float soft = clamp(brightness - _Threshold + knee, 0.0, 2.0 * knee);
    soft = soft * soft / (4.0 * knee);
    return color * (max(soft, brightness - _Threshold) / max(brightness, 1e-5));
}

void main()
{
    vec2 texel = 1.0 / vec2(textureSize(Graphite_Source, 0));
    vec2 offsets[4] = vec2[] (vec2(- 1.0, - 1.0), vec2(1.0, - 1.0), vec2(- 1.0, 1.0), vec2(1.0, 1.0));
    vec3 sum = vec3(0.0);
    float weightSum = 0.0;
    for (int i = 0; i < 4; i ++)
    {
        vec3 s = Prefilter(Graphite_SampleSource(vUV + offsets[i] * texel).rgb);
        float w = _AntiFlicker > 0.5 ? 1.0 / (1.0 + Luminance(s)) : 1.0;
        sum += s * w;
        weightSum += w;
    }
    outColor = vec4(sum / weightSum, 1.0);
}









































#line 10 "Graphite/Bloom"

