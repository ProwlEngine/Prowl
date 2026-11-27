#version 460
#extension GL_GOOGLE_include_directive : enable
#line 1 "Graphite/MotionBlur"

#extension GL_GOOGLE_cpp_style_line_directive : require
#line 7 "Graphite/MotionBlur"

#line 1 "Graphite/Fullscreen.glsl"


#pragma variantGRAPHITE_LAYERED
#pragma variantGRAPHITE_NO_SOURCE



































layout(location = 0) in vec2 vUV;

layout(location = 0) out vec4 outColor;


layout(set = 0, binding = 0) uniform sampler2D Graphite_Source;
vec4 Graphite_SampleSource(vec2 uv) { return texture(Graphite_Source, uv); }


#line 9 "Graphite/MotionBlur"
#line 1 "Graphite/Internal/MotionBlur.glsl"




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
#line 6 "Graphite/Internal/MotionBlur.glsl"

layout(set = 0, binding = 2) uniform sampler2D _MotionVectorsTex;
layout(set = 0, binding = 3) uniform sampler2D _CameraDepthTexture;
layout(set = 0, binding = 4, std140) uniform _GraphiteGlobal { vec2 _Resolution; float _Intensity; int _Samples; float _MaxBlurRadius; };




float IGN(vec2 pixel)
{
    const vec3 magic = vec3(0.06711056, 0.00583715, 52.9829189);
    return fract(magic.z * fract(dot(pixel, magic.xy)));
}

float LinearDepth(Graphite_ViewData view, vec2 uv)
{
    return abs(Graphite_PostViewPosition(view, uv, max(texture(_CameraDepthTexture, uv).r, 1e-7)).z);
}

void main()
{
    Graphite_ViewData view = Graphite_PostView();
    vec2 texelSize = 1.0 / _Resolution;
    vec4 center = Graphite_SampleSource(vUV);
    vec3 centerColor = center.rgb;
    vec2 motion = texture(_MotionVectorsTex, vUV).rg * _Intensity;
    float maxBlurUV = _MaxBlurRadius * max(texelSize.x, texelSize.y);
    float motionLen = length(motion);
    if (motionLen > maxBlurUV)
        motion *= maxBlurUV / motionLen;
    if (length(motion * _Resolution) < 0.5)
    {
        outColor = center;
        return;
    }

    float dither = IGN(gl_FragCoord.xy) - 0.5;
    int samples = max(_Samples, 1);
    float centerDepth = LinearDepth(view, vUV);
    vec3 acc = vec3(0.0);
    float totalWeight = 0.0;
    for (int i = 0; i < samples; i ++)
    {
        float t = (float(i) + dither) / float(samples) - 0.5;
        vec2 sampleUV = clamp(vUV + motion * t, vec2(0.0), vec2(1.0));
        float depthWeight = 1.0 / (1.0 + abs(LinearDepth(view, sampleUV) - centerDepth) * 10.0);
        acc += Graphite_SampleSource(sampleUV).rgb * depthWeight;
        totalWeight += depthWeight;
    }
    outColor = vec4(acc / max(totalWeight, 1e-5), center.a);
}
#line 10 "Graphite/MotionBlur"

