#version 460 core

// SSR - Resolve pass
// Gather neighbour rays, weight by BRDF/pdf, pick a pyramid level by a roughness cone
// (contact hardening + glossy blur), firefly suppression, screen-edge fade.

layout(std140, set = 0, binding = 0) uniform GlobalUniforms
{
    mat4 prowl_MatV;
    mat4 prowl_MatIV;
    mat4 prowl_MatP;
    mat4 prowl_MatVP;
    mat4 prowl_PrevViewProj;
    mat4 prowl_MatIP;
    mat4 prowl_MatIVP;
    mat4 prowl_MatVP_NonJittered;
    vec3 _WorldSpaceCameraPos;
    float _padding0;
    vec4 _ProjectionParams;
    vec4 _ScreenParams;
    vec2 _CameraJitter;
    vec2 _CameraPreviousJitter;
    vec4 _Time;
    vec4 _SinTime;
    vec4 _CosTime;
    vec4 prowl_DeltaTime;
};

#define PROWL_MATRIX_I_P prowl_MatIP

#define PROWL_PI 3.14159265359

layout(std140, set = 2, binding = 0) uniform PassUniforms
{
    vec2 _NoiseSize;
    vec4 _JitterSizeAndOffset;
    vec2 _ResolveSize;
    float _BRDFBias;
    float _EdgeFactor;
    float _MaxMipMap;
    int _RayReuse;
    int _UseNormalization;
    int _Fireflies;
};

layout(set = 2, binding = 1) uniform sampler2D _RayCast;
layout(set = 2, binding = 2) uniform sampler2D _CameraDepthTexture;
layout(set = 2, binding = 3) uniform sampler2D _CameraNormalsTexture;
layout(set = 2, binding = 4) uniform sampler2D _CameraMotionVectorsTexture; // .b roughness
layout(set = 2, binding = 5) uniform sampler2D _Noise;

// Convolved scene-colour pyramid (level 0 = sharp ... level 4 = blurriest).
layout(set = 2, binding = 6) uniform sampler2D _Scene0;
layout(set = 2, binding = 7) uniform sampler2D _Scene1;
layout(set = 2, binding = 8) uniform sampler2D _Scene2;
layout(set = 2, binding = 9) uniform sampler2D _Scene3;
layout(set = 2, binding = 10) uniform sampler2D _Scene4;

layout(location = 1) in vec2 TexCoords;
layout(location = 0) out vec4 fragColor;

// ---- ProwlCG: view-space reconstruction from screen-space depth ----
vec3 projectAndDivide(mat4 matrix, vec3 pos)
{
    vec4 p = matrix * vec4(pos, 1.0);
    return p.xyz / p.w;
}

vec3 getScreenPos(vec2 tc, float depth)
{
    return vec3(tc, depth);
}

vec3 getNDCFromScreenPos(vec3 screenPos)
{
    return screenPos * 2.0 - 1.0;
}

vec3 getViewFromScreenPos(vec3 screenPos)
{
    return projectAndDivide(PROWL_MATRIX_I_P, getNDCFromScreenPos(screenPos));
}

vec3 getViewPos(vec2 tc, float depth)
{
    return getViewFromScreenPos(getScreenPos(tc, depth));
}

// ---- ProwlCG: colour utilities ----
float luminance(vec3 color)
{
    return dot(color, vec3(0.2126, 0.7152, 0.0722));
}

const vec2 resolveOffset[4] = vec2[]( vec2(0,0), vec2(2,-2), vec2(-2,-2), vec2(0,2) );

vec3 samplePyramid(vec2 uv, float mip)
{
    vec3 c = mix(texture(_Scene0, uv).rgb, texture(_Scene1, uv).rgb, clamp(mip, 0.0, 1.0));
    c = mix(c, texture(_Scene2, uv).rgb, clamp(mip - 1.0, 0.0, 1.0));
    c = mix(c, texture(_Scene3, uv).rgb, clamp(mip - 2.0, 0.0, 1.0));
    c = mix(c, texture(_Scene4, uv).rgb, clamp(mip - 3.0, 0.0, 1.0));
    return c;
}

// Smith-GGX visibility * GGX NDF * PI/4 -> the resolve weight (matched with 1/pdf).
float brdfWeight(vec3 V, vec3 L, vec3 N, float roughness)
{
    vec3 H = normalize(L + V);
    float NdotH = clamp(dot(N, H), 0.0, 1.0);
    float NdotL = clamp(dot(N, L), 0.0, 1.0);
    float NdotV = clamp(dot(N, V), 0.0, 1.0);
    float m = roughness * roughness;
    float m2 = m * m;
    float d = (NdotH * m2 - NdotH) * NdotH + 1.0;
    float D = m2 / (PROWL_PI * d * d);
    float visL = NdotV * sqrt((NdotL - NdotL * m2) * NdotL + m2);
    float visV = NdotL * sqrt((NdotV - NdotV * m2) * NdotV + m2);
    float Vis = 0.5 / max(1e-5, visL + visV);
    return D * Vis * (PROWL_PI / 4.0);
}

float edgeFade(vec2 pos, float value)
{
    float d = min(1.0 - max(pos.x, pos.y), min(pos.x, pos.y));
    return clamp(value > 0.0 ? d / value : 1.0, 0.0, 1.0);
}

void main()
{
    vec3 viewNormal = normalize(texture(_CameraNormalsTexture, TexCoords).xyz * 2.0 - 1.0);
    float roughness = texture(_CameraMotionVectorsTexture, TexCoords).b;
    float depth = texture(_CameraDepthTexture, TexCoords).r;
    vec3 viewPos = getViewPos(TexCoords, depth);
    vec3 V = normalize(-viewPos); // surface -> camera

    // Per-pixel rotation of the reuse offsets from blue noise.
    vec2 bn = texture(_Noise, (TexCoords + _JitterSizeAndOffset.zw) * _ScreenParams.xy / _NoiseSize).rg * 2.0 - 1.0;
    mat2 rot = mat2(bn.x, bn.y, -bn.y, bn.x);

    int numResolve = _RayReuse == 1 ? 4 : 1;
    float NdotV = clamp(dot(viewNormal, V), 0.0, 1.0);
    float coneTangent = mix(0.0, roughness * (1.0 - _BRDFBias), NdotV * sqrt(roughness));
    float maxMip = _MaxMipMap - 1.0;

    vec4 result = vec4(0.0);
    float weightSum = 0.0;
    for (int i = 0; i < numResolve; i++)
    {
        vec2 offset = rot * (resolveOffset[i] / _ResolveSize);
        vec2 nUV = TexCoords + offset;

        vec4 rd = texture(_RayCast, nUV);
        vec2 hitUV = rd.xy;
        float pdf = rd.z;
        float mask = rd.w;
        if (mask <= 0.0) continue;

        float hitDepth = texture(_CameraDepthTexture, hitUV).r;
        vec3 hitViewPos = getViewPos(hitUV, hitDepth);

        float weight = 1.0;
        if (_UseNormalization == 1)
            weight = brdfWeight(V, normalize(hitViewPos - viewPos), viewNormal, roughness) / max(1e-5, pdf);

        float coneRadius = coneTangent * length(hitUV - TexCoords);
        float mip = clamp(log2(max(1e-5, coneRadius) * max(_ResolveSize.x, _ResolveSize.y)), 0.0, maxMip);

        vec4 s;
        s.rgb = samplePyramid(hitUV, mip);
        s.a = edgeFade(hitUV, _EdgeFactor) * mask;
        if (_Fireflies == 1) s.rgb /= 1.0 + luminance(s.rgb);

        result += s * weight;
        weightSum += weight;
    }

    result /= max(1e-5, weightSum);
    if (_Fireflies == 1)
    {
        // Bounded inverse of the per-sample tonemap: cap luminance so a very bright
        // reflection can't divide by ~0 and explode to white.
        float lum = min(luminance(result.rgb), 0.95);
        result.rgb /= 1.0 - lum;
    }
    fragColor = max(vec4(1e-5), result);
}
