#version 460
#extension GL_GOOGLE_include_directive : enable
#line 1 "Graphite/Tonemap"

#extension GL_GOOGLE_cpp_style_line_directive : require
#line 7 "Graphite/Tonemap"

#line 1 "Graphite/Fullscreen.glsl"


#pragma variantGRAPHITE_LAYERED
#pragma variantGRAPHITE_NO_SOURCE



































layout(location = 0) in vec2 vUV;

layout(location = 0) out vec4 outColor;


layout(set = 0, binding = 0) uniform sampler2D Graphite_Source;
vec4 Graphite_SampleSource(vec2 uv) { return texture(Graphite_Source, uv); }


#line 9 "Graphite/Tonemap"
#line 1 "Graphite/Internal/Tonemap.glsl"
#pragma variantTONEMAPCLAMPMELONACESACES_SIMPLEAGXREINHARD_SIMPLEREINHARD_LUMAREINHARD_WHITEROMBINDAHOUSEUNCHARTED2



layout(set = 0, binding = 1, std140) uniform _GraphiteGlobal { float _Contrast; float _Saturation; };



















































const mat3 agxTransform = mat3(
    0.842479062253094, 0.0423282422610123, 0.0423756549057051,
    0.0784335999999992, 0.878468636469772, 0.0784336,
    0.0792237451477643, 0.0791661274605434, 0.879142973793104);

const mat3 agxTransformInverse = mat3(
    1.19687900512017, - 0.0528968517574562, - 0.0529716355144438,
    - 0.0980208811401368, 1.15190312990417, - 0.0980434501171241,
    - 0.0990297440797205, - 0.0989611768448433, 1.15107367264116);

vec3 agxDefaultContrastApproximation(vec3 x)
{
    vec3 x2 = x * x;
    vec3 x4 = x2 * x2;
    return 15.5 * x4 * x2 - 40.14 * x4 * x + 31.96 * x4 - 6.868 * x2 * x + 0.4298 * x2 + 0.1191 * x - 0.00232;
}

vec3 Tonemap(vec3 color)
{
    const float minEv = - 12.47393;
    const float maxEv = 4.026069;
    color = agxTransform * color;
    color = clamp(log2(max(color, vec3(1e-10))), minEv, maxEv);
    color = (color - minEv) / (maxEv - minEv);
    color = agxDefaultContrastApproximation(color);


    float luma = dot(color, vec3(0.2126729, 0.7151522, 0.0721750));
    color = pow(max(color * vec3(1.1), vec3(0.0)), vec3(1.2));
    color = max(luma + 1.3 * (color - luma), vec3(0.0));

    color = agxTransformInverse * color;
    return pow(max(vec3(0.0), color), vec3(2.2));
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

