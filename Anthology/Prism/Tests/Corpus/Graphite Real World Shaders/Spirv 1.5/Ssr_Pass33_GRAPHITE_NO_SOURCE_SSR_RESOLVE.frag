#version 460
#extension GL_GOOGLE_include_directive : enable
#line 1 "Graphite/Ssr"

#extension GL_GOOGLE_cpp_style_line_directive : require
#line 7 "Graphite/Ssr"

#line 1 "Graphite/Fullscreen.glsl"


#pragma variantGRAPHITE_LAYERED
#pragma variantGRAPHITE_NO_SOURCE



































layout(location = 0) in vec2 vUV;

layout(location = 0) out vec4 outColor;






#line 9 "Graphite/Ssr"
#line 1 "Graphite/Internal/Ssr.glsl"
#pragma variantSSRCOPYRAYCASTBLURRESOLVETEMPORALREPROJECTCOMBINE







#line 1 "Graphite/Internal/PostView.glsl"



#line 1 "Graphite/ViewData.glsl"



struct Graphite_ViewData
{
    mat4 view;
    mat4 projection;
    mat4 viewProjection;
    mat4 inverseViewProjection;
    mat4 previousViewProjection;
    vec4 cameraHigh;
    vec4 cameraLow;
    vec4 previousCameraHigh;
    vec4 previousCameraLow;
    vec4 screen;
    vec4 jitter;
    vec4 time;
    vec4 depth;
    vec4 treeOffset;
};
#line 5 "Graphite/Internal/PostView.glsl"
#line 1 "Graphite/Octahedral.glsl"



vec2 Graphite_OctWrap(vec2 v)
{
    return(1.0 - abs(v.yx)) * vec2(v.x >= 0.0 ? 1.0 : - 1.0, v.y >= 0.0 ? 1.0 : - 1.0);
}


vec2 Graphite_OctEncode(vec3 n)
{
    n /= abs(n.x) + abs(n.y) + abs(n.z);
    return n.z >= 0.0 ? n.xy : Graphite_OctWrap(n.xy);
}

vec3 Graphite_OctDecode(vec2 e)
{
    vec3 n = vec3(e, 1.0 - abs(e.x) - abs(e.y));
    if (n.z < 0.0)
        n.xy = Graphite_OctWrap(n.xy);
    return normalize(n);
}
#line 6 "Graphite/Internal/PostView.glsl"

layout(std430, set = 0, binding = 0) readonly buffer Graphite_PostViews { Graphite_ViewData graphite_PostViews[]; };


Graphite_ViewData Graphite_PostView() { return graphite_PostViews[0]; }


bool Graphite_PostOrthographic(Graphite_ViewData view) { return abs(view.projection[2][3]) < 0.5; }


vec3 Graphite_PostViewPosition(Graphite_ViewData view, vec2 uv, float depth)
{
    vec4 p = view.inverseViewProjection * vec4(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0, depth, 1.0);
    return mat3(view.view) * (p.xyz / p.w);
}


vec3 Graphite_PostRelativePosition(Graphite_ViewData view, vec2 uv, float depth)
{
    vec4 p = view.inverseViewProjection * vec4(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0, depth, 1.0);
    return p.xyz / p.w;
}


vec2 Graphite_PostPreviousUV(Graphite_ViewData view, vec3 relative)
{
    vec3 moved = relative + (view.cameraHigh.xyz - view.previousCameraHigh.xyz) + (view.cameraLow.xyz - view.previousCameraLow.xyz);
    vec4 clip = view.previousViewProjection * vec4(moved, 1.0);
    vec2 ndc = clip.xy / clip.w;
    return vec2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5);
}
#line 10 "Graphite/Internal/Ssr.glsl"



float Luminance(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

vec3 SsrViewNormal(Graphite_ViewData view, vec2 encoded) { return normalize(mat3(view.view) * Graphite_OctDecode(encoded)); }


vec3 SsrCameraToSurface(Graphite_ViewData view, vec2 uv, vec3 viewPos)
{
    if (Graphite_PostOrthographic(view))
        return normalize(Graphite_PostViewPosition(view, uv, 0.0) - Graphite_PostViewPosition(view, uv, 1.0));
    return normalize(viewPos);
}





























































































































layout(set = 0, binding = 1) uniform sampler2D _RayCast;
layout(set = 0, binding = 2) uniform sampler2D _CameraDepthTexture;
layout(set = 0, binding = 3) uniform sampler2D _CameraNormalsTexture;
layout(set = 0, binding = 4) uniform sampler2D _MaterialTexture;
layout(set = 0, binding = 5) uniform sampler2D _Noise;
layout(set = 0, binding = 6, std140) uniform _GraphiteGlobal { vec2 _NoiseSize; vec4 _JitterSizeAndOffset; vec2 _ResolveSize; vec2 _ScreenSize; float _BRDFBias; float _EdgeFactor; float _MaxMipMap; int _RayReuse; int _UseNormalization; int _Fireflies; };









layout(set = 0, binding = 7) uniform sampler2D _Scene0;
layout(set = 0, binding = 8) uniform sampler2D _Scene1;
layout(set = 0, binding = 9) uniform sampler2D _Scene2;
layout(set = 0, binding = 10) uniform sampler2D _Scene3;
layout(set = 0, binding = 11) uniform sampler2D _Scene4;

const vec2 resolveOffset[4] = vec2[] (vec2(0, 0), vec2(2, - 2), vec2(- 2, - 2), vec2(0, 2));


vec3 SamplePyramid(vec2 uv, float mip)
{
    if (mip <= 1.0)
        return mix(texture(_Scene0, uv).rgb, texture(_Scene1, uv).rgb, mip);
    if (mip <= 2.0)
        return mix(texture(_Scene1, uv).rgb, texture(_Scene2, uv).rgb, mip - 1.0);
    if (mip <= 3.0)
        return mix(texture(_Scene2, uv).rgb, texture(_Scene3, uv).rgb, mip - 2.0);
    return mix(texture(_Scene3, uv).rgb, texture(_Scene4, uv).rgb, clamp(mip - 3.0, 0.0, 1.0));
}


float BrdfWeight(vec3 V, vec3 L, vec3 N, float roughness)
{
    vec3 H = normalize(L + V);
    float NdotH = clamp(dot(N, H), 0.0, 1.0);
    float NdotL = clamp(dot(N, L), 0.0, 1.0);
    float NdotV = clamp(dot(N, V), 0.0, 1.0);
    float m = roughness * roughness;
    float m2 = m * m;
    float d = (NdotH * m2 - NdotH) * NdotH + 1.0;
    float D = m2 / (3.14159265359 * d * d);
    float visL = NdotV * sqrt( (NdotL - NdotL * m2) * NdotL + m2);
    float visV = NdotL * sqrt( (NdotV - NdotV * m2) * NdotV + m2);
    float Vis = 0.5 / max(1e-5, visL + visV);
    return D * Vis * (3.14159265359 / 4.0);
}

float EdgeFade(vec2 pos, float value)
{
    float d = min(1.0 - max(pos.x, pos.y), min(pos.x, pos.y));
    return clamp(value > 0.0 ? d / value : 1.0, 0.0, 1.0);
}

void main()
{
    Graphite_ViewData view = Graphite_PostView();
    vec3 viewNormal = SsrViewNormal(view, texture(_CameraNormalsTexture, vUV).xy);
    float roughness = texture(_MaterialTexture, vUV).r;
    float depth = max(texture(_CameraDepthTexture, vUV).r, 1e-7);
    vec3 viewPos = Graphite_PostViewPosition(view, vUV, depth);
    vec3 V = - SsrCameraToSurface(view, vUV, viewPos);

    vec2 bn = texture(_Noise, (vUV + _JitterSizeAndOffset.zw) * _ScreenSize / _NoiseSize).rg * 2.0 - 1.0;
    mat2 rot = mat2(bn.x, bn.y, - bn.y, bn.x);
    int numResolve = _RayReuse == 1 ? 4 : 1;
    float NdotV = clamp(dot(viewNormal, V), 0.0, 1.0);
    float coneTangent = mix(0.0, roughness * (1.0 - _BRDFBias), NdotV * sqrt(roughness));
    float maxMip = _MaxMipMap - 1.0;

    vec4 result = vec4(0.0);
    float weightSum = 0.0;
    for (int i = 0; i < numResolve; i ++)
    {
        vec2 offset = rot * (resolveOffset[i] / _ResolveSize);
        vec4 rd = texture(_RayCast, vUV + offset);
        vec2 hitUV = rd.xy;
        float pdf = rd.z;
        float mask = rd.w;
        if (mask <= 0.0)
            continue;
        float hitDepth = max(texture(_CameraDepthTexture, hitUV).r, 1e-7);
        vec3 hitViewPos = Graphite_PostViewPosition(view, hitUV, hitDepth);
        float weight = 1.0;
        if (_UseNormalization == 1)
            weight = BrdfWeight(V, normalize(hitViewPos - viewPos), viewNormal, roughness) / max(1e-5, pdf);
        float coneRadius = coneTangent * length(hitUV - vUV);
        float mip = clamp(log2(max(1e-5, coneRadius) * max(_ResolveSize.x, _ResolveSize.y)), 0.0, maxMip);
        vec4 s;
        s.rgb = SamplePyramid(hitUV, mip);
        s.a = EdgeFade(hitUV, _EdgeFactor) * mask;
        if (_Fireflies == 1)
            s.rgb /= 1.0 + Luminance(s.rgb);
        result += s * weight;
        weightSum += weight;
    }
    result /= max(1e-5, weightSum);
    if (_Fireflies == 1)
        result.rgb /= 1.0 - min(Luminance(result.rgb), 0.95);
    outColor = max(vec4(1e-5), result);
}






















































































#line 10 "Graphite/Ssr"

