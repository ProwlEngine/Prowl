#version 460
#extension GL_GOOGLE_include_directive : enable
#line 1 "Graphite/Tonemap"

#extension GL_GOOGLE_cpp_style_line_directive : require
#line 7 "Graphite/Tonemap"

#line 1 "Graphite/Fullscreen.glsl"


#pragma variantGRAPHITE_LAYERED
#pragma variantGRAPHITE_NO_SOURCE

#extension GL_EXT_multiview : require

































layout(location = 0) in vec2 vUV;

layout(location = 0) out vec4 outColor;


layout(set = 0, binding = 0) uniform sampler2DArray Graphite_Source;
vec4 Graphite_SampleSource(vec2 uv) { return texture(Graphite_Source, vec3(uv, float(gl_ViewIndex))); }


#line 9 "Graphite/Tonemap"
#line 1 "Graphite/Internal/Tonemap.glsl"
#pragma variantTONEMAPCLAMPMELONACESACES_SIMPLEAGXREINHARD_SIMPLEREINHARD_LUMAREINHARD_WHITEROMBINDAHOUSEUNCHARTED2



layout(set = 0, binding = 1, std140) uniform _GraphiteGlobal { float _Contrast; float _Saturation; };




























































































vec3 Tonemap(vec3 color)
{
    float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
    return color * ( (luma / (1.0 + luma)) / max(luma, 1e-5));
}




























vec3 LinearToGamma(vec3 lin)
{
    return max(1.055 * pow(max(lin, vec3(0.0)), vec3(0.416666667)) - 0.055, 0.0);
}

void main()
{
    vec3 color = LinearToGamma(clamp(Tonemap(Graphite_SampleSource(vUV).rgb), 0.0, 1.0));

    float t = (1.0 - _Contrast) / 2.0;
    mat4 contrast = mat4(_Contrast, 0, 0, 0, 0, _Contrast, 0, 0, 0, 0, _Contrast, 0, t, t, t, 1);
    vec3 luminance = vec3(0.3086, 0.6094, 0.0820);
    float oneMinusSat = 1.0 - _Saturation;
    mat4 saturation = mat4(
        vec4(vec3(luminance.x * oneMinusSat) + vec3(_Saturation, 0, 0), 0),
        vec4(vec3(luminance.y * oneMinusSat) + vec3(0, _Saturation, 0), 0),
        vec4(vec3(luminance.z * oneMinusSat) + vec3(0, 0, _Saturation), 0),
        vec4(0, 0, 0, 1));
    outColor = vec4( (contrast * saturation * vec4(color, 1.0)).rgb, 1.0);
}
#line 10 "Graphite/Tonemap"

