#version 460
#extension GL_GOOGLE_include_directive : enable
#line 1 "Graphite/Gtao"

#extension GL_GOOGLE_cpp_style_line_directive : require
#line 7 "Graphite/Gtao"

#line 1 "Graphite/Fullscreen.glsl"


#pragma variantGRAPHITE_LAYERED
#pragma variantGRAPHITE_NO_SOURCE

#extension GL_EXT_multiview : require

































layout(location = 0) in vec2 vUV;

layout(location = 0) out vec4 outColor;


layout(set = 0, binding = 0) uniform sampler2DArray Graphite_Source;
vec4 Graphite_SampleSource(vec2 uv) { return texture(Graphite_Source, vec3(uv, float(gl_ViewIndex))); }


#line 9 "Graphite/Gtao"
#line 1 "Graphite/Internal/Gtao.glsl"
#pragma variantGTAOCOPYDEPTHAOTEMPORALBLUR






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

layout(std430, set = 0, binding = 1) readonly buffer Graphite_PostViews { Graphite_ViewData graphite_PostViews[]; };


Graphite_ViewData Graphite_PostView() { return graphite_PostViews[int(gl_ViewIndex)]; }


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
#line 9 "Graphite/Internal/Gtao.glsl"




float Gtao_FastAcos(float x)
{
    float y = abs(x);
    float p = - 0.0187293 * y + 0.0742610;
    p = p * y - 0.2121144;
    p = p * y + 1.5707288;
    p = p * sqrt(1.0 - y);
    return x >= 0.0 ? p : 3.14159265359 - p;
}










layout(set = 0, binding = 2) uniform sampler2DArray _CameraDepthTexture;
layout(set = 0, binding = 3) uniform sampler2D _Noise;
layout(set = 0, binding = 4, std140) uniform _GraphiteGlobal { int _Slices; int _DirectionSamples; float _Radius; float _Intensity; vec2 _NoiseScale; vec2 _JitterOffset; };






Graphite_ViewData gtao_View;

vec3 ViewPosition(vec2 uv, float depth) { return Graphite_PostViewPosition(gtao_View, uv, depth); }


vec3 TowardCamera(vec2 uv, vec3 viewPos)
{
    if (Graphite_PostOrthographic(gtao_View))
        return normalize(ViewPosition(uv, 1.0) - ViewPosition(uv, 0.0));
    return - normalize(viewPos);
}


vec3 NormalFromDepth(vec2 uv, vec3 center, vec3 viewDir)
{
    vec2 texel = 1.0 / vec2(textureSize(_CameraDepthTexture, 0).xy);
    vec3 l = ViewPosition(uv - vec2(texel.x, 0.0), texture(_CameraDepthTexture, vec3(uv - vec2(texel.x, 0.0), float(gl_ViewIndex))).r);
    vec3 r = ViewPosition(uv + vec2(texel.x, 0.0), texture(_CameraDepthTexture, vec3(uv + vec2(texel.x, 0.0), float(gl_ViewIndex))).r);
    vec3 u = ViewPosition(uv - vec2(0.0, texel.y), texture(_CameraDepthTexture, vec3(uv - vec2(0.0, texel.y), float(gl_ViewIndex))).r);
    vec3 d = ViewPosition(uv + vec2(0.0, texel.y), texture(_CameraDepthTexture, vec3(uv + vec2(0.0, texel.y), float(gl_ViewIndex))).r);
    vec3 dx = dot(r - center, r - center) < dot(center - l, center - l) ? r - center : center - l;
    vec3 dy = dot(d - center, d - center) < dot(center - u, center - u) ? d - center : center - u;
    vec3 n = cross(dx, dy);
    if (! (dot(n, n) > 1e-20))
        return viewDir;
    n = normalize(n);
    return dot(n, viewDir) < 0.0 ? - n : n;
}

void SampleHorizonCos(vec2 coord, vec2 offset, vec3 viewPos, vec3 viewDir, vec2 falloff, inout float cHorizonCos)
{
    vec2 sTexCoord = coord + offset;
    if (sTexCoord.x < 0.0 || sTexCoord.x > 1.0 || sTexCoord.y < 0.0 || sTexCoord.y > 1.0)
        return;
    float sDepth = texture(_CameraDepthTexture, vec3(sTexCoord, float(gl_ViewIndex))).r;
    if (sDepth <= 0.0)
        return;
    vec3 sHorizonV = ViewPosition(sTexCoord, sDepth) - viewPos;
    float sLenV = dot(sHorizonV, sHorizonV);


    float minLen = 0.0015 * abs(viewPos.z);
    if (sLenV < minLen * minLen)
        return;
    float sHorizonCos = dot(sHorizonV, viewDir) * inversesqrt(sLenV);
    sHorizonCos = mix(sHorizonCos, cHorizonCos, clamp( (sLenV - falloff.x) / (falloff.y - falloff.x), 0.0, 1.0));
    cHorizonCos = max(sHorizonCos, cHorizonCos);
}

float CalculateGTAO(vec2 coord, vec3 viewPos, vec3 viewDir, vec3 normal, vec2 dither)
{
    float viewDistance = length(viewPos);
    float rSliceCount = 1.0 / float(_Slices);
    float rSampleCount = 1.0 / float(_DirectionSamples);
    float radius = _Radius * clamp(0.25 + viewDistance / 64.0, 0.0, 1.0);



    float radiusToScreen = Graphite_PostOrthographic(gtao_View) ? 1.0 : 1.0 / viewDistance;
    vec2 scale = vec2(gtao_View.projection[0][0], - gtao_View.projection[1][1]);
    vec2 sRadius = rSampleCount * radius * radiusToScreen * abs(scale);
    sRadius = max(sRadius, vec2(1.5) / vec2(textureSize(_CameraDepthTexture, 0).xy)) * sign(scale);
    vec2 falloff = (radius * vec2(1.0, 4.0)) * (radius * vec2(1.0, 4.0));

    float visibility = 0.0;
    for (int slice = 0; slice < _Slices; ++ slice)
    {
        float slicePhi = (float(slice) + dither.x) * (3.14159265359 * rSliceCount);
        vec3 directionV = vec3(cos(slicePhi), sin(slicePhi), 0.0);
        vec3 orthoDirectionV = directionV - dot(directionV, viewDir) * viewDir;
        vec3 axisV = cross(directionV, viewDir);
        vec3 projNormalV = normal - axisV * dot(normal, axisV);
        float lenV = dot(projNormalV, projNormalV);
        float normV = inversesqrt(lenV);
        lenV *= normV;
        float sgnN = dot(orthoDirectionV, projNormalV) >= 0.0 ? 1.0 : - 1.0;
        float cosN = clamp(dot(projNormalV, viewDir) * normV, 0.0, 1.0);
        float n = sgnN * Gtao_FastAcos(cosN);

        vec2 cHorizonCos = vec2(- 1.0);
        vec2 stepDir = directionV.xy * sRadius;
        for (int samp = 0; samp < _DirectionSamples; ++ samp)
        {
            vec2 offset = (float(samp) + dither.y) * stepDir;
            SampleHorizonCos(coord, offset, viewPos, viewDir, falloff, cHorizonCos.x);
            SampleHorizonCos(coord, - offset, viewPos, viewDir, falloff, cHorizonCos.y);
        }

        vec2 h = n + clamp(vec2(Gtao_FastAcos(cHorizonCos.x), - Gtao_FastAcos(cHorizonCos.y)) - n, - 1.57079632679, 1.57079632679);
        h = cosN + 2.0 * h * sin(n) - cos(2.0 * h - n);
        visibility += lenV * (h.x + h.y);
    }
    return 0.25 * rSliceCount * visibility;
}

void main()
{
    gtao_View = Graphite_PostView();
    float depth = texture(_CameraDepthTexture, vec3(vUV, float(gl_ViewIndex))).r;
    if (depth <= 0.0)
    {
        outColor = vec4(1.0);
        return;
    }
    vec3 viewPos = ViewPosition(vUV, depth);
    vec3 viewDir = TowardCamera(vUV, viewPos);
    vec3 normal = NormalFromDepth(vUV, viewPos, viewDir);
    vec2 noise = texture(_Noise, vUV * _NoiseScale + _JitterOffset).rg;
    float ao = pow(clamp(CalculateGTAO(vUV, viewPos, viewDir, normal, noise), 0.0, 1.0), _Intensity);
    outColor = vec4(ao, ao, ao, 1.0);
}



































































#line 10 "Graphite/Gtao"

