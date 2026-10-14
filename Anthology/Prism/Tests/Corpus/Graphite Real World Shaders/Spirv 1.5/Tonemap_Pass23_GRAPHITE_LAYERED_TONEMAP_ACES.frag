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






















const mat3 ACESInputMat = mat3(
    0.59719, 0.35458, 0.04823,
    0.07600, 0.90834, 0.01566,
    0.02840, 0.13383, 0.83777);

const mat3 ACESOutputMat = mat3(
     1.60475, - 0.53108, - 0.07367,
    - 0.10208, 1.10813, - 0.00605,
    - 0.00327, - 0.07276, 1.07602);

vec3 RRTAndODTFit(vec3 v)
{
    vec3 a = v * (v + 0.0245786) - 0.000090537;
    vec3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
    return a / b;
}

vec3 Tonemap(vec3 color)
{
    color = color * ACESInputMat;
    color = RRTAndODTFit(color);
    return color * ACESOutputMat;
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

