#version 460
#extension GL_GOOGLE_include_directive : enable
#line 1 "Graphite/GodRays"

#extension GL_GOOGLE_cpp_style_line_directive : require
#line 7 "Graphite/GodRays"

#line 1 "Graphite/Fullscreen.glsl"


#pragma variantGRAPHITE_LAYERED
#pragma variantGRAPHITE_NO_SOURCE



































layout(location = 0) in vec2 vUV;

layout(location = 0) out vec4 outColor;


layout(set = 0, binding = 0) uniform sampler2D Graphite_Source;
vec4 Graphite_SampleSource(vec2 uv) { return texture(Graphite_Source, uv); }


#line 9 "Graphite/GodRays"
#line 1 "Graphite/Internal/GodRays.glsl"




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
#line 6 "Graphite/Internal/GodRays.glsl"

layout(set = 0, binding = 2) uniform sampler2D _CameraDepthTexture;
layout(set = 0, binding = 3, std140) uniform _GraphiteGlobal { vec3 _SunDirection; float _GodRayIntensity; float _GodRayDecay; float _GodRayDensity; float _GodRayWeight; int _GodRaySamples; float _GodRayThreshold; };







void main()
{
    Graphite_ViewData view = Graphite_PostView();
    vec4 clip = view.viewProjection * vec4(- _SunDirection * 10000.0, 1.0);
    vec2 sunUV = vec2(clip.x / clip.w * 0.5 + 0.5, 0.5 - clip.y / clip.w * 0.5);

    vec4 color = Graphite_SampleSource(vUV);
    vec2 deltaUV = (vUV - sunUV) * _GodRayDensity / float(_GodRaySamples);
    vec2 sampleUV = vUV;
    float illumination = 0.0;
    float decay = 1.0;
    int sampleCount = min(_GodRaySamples, 128);
    for (int i = 0; i < sampleCount; i ++)
    {
        sampleUV -= deltaUV;
        vec2 clamped = clamp(sampleUV, 0.001, 0.999);
        float sky = texture(_CameraDepthTexture, clamped).r <= 0.0001 ? 1.0 : 0.0;
        float brightness = dot(Graphite_SampleSource(clamped).rgb, vec3(0.2126, 0.7152, 0.0722));
        illumination += sky * step(_GodRayThreshold, brightness) * decay * _GodRayWeight;
        decay *= _GodRayDecay;
    }
    outColor = vec4(color.rgb + illumination * _GodRayIntensity * vec3(1.0, 0.95, 0.85), color.a);
}
#line 10 "Graphite/GodRays"

