#version 460
#extension GL_GOOGLE_include_directive : enable
#line 1 "Graphite/DepthOfField"

#extension GL_GOOGLE_cpp_style_line_directive : require
#line 7 "Graphite/DepthOfField"

#line 1 "Graphite/Fullscreen.glsl"


#pragma variantGRAPHITE_LAYERED
#pragma variantGRAPHITE_NO_SOURCE



































layout(location = 0) in vec2 vUV;

layout(location = 0) out vec4 outColor;


layout(set = 0, binding = 0) uniform sampler2D Graphite_Source;
vec4 Graphite_SampleSource(vec2 uv) { return texture(Graphite_Source, uv); }


#line 9 "Graphite/DepthOfField"
#line 1 "Graphite/Internal/DepthOfField.glsl"
#pragma variantDOFCOMBINECOCPREFILTERBOKEHPOSTFILTERFOCUS
#pragma variantDOF_LAYERED_SOURCE







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
#line 11 "Graphite/Internal/DepthOfField.glsl"

float LinearDepth(Graphite_ViewData view, vec2 uv, float depth)
{
    return abs(Graphite_PostViewPosition(view, uv, max(depth, 1e-7)).z);
}






































































































































layout(set = 0, binding = 2) uniform sampler2DArray _CameraDepthTexture;
float FetchDepth(ivec2 pixel) { return texelFetch(_CameraDepthTexture, ivec3(pixel, 0), 0).r; }
ivec2 DepthSize() { return textureSize(_CameraDepthTexture, 0).xy; }





layout(set = 0, binding = 3) uniform sampler2D _PrevFocusTex;
layout(set = 0, binding = 4, std140) uniform _GraphiteGlobal { float _UseAutoFocus; float _ManualFocusPoint; float _FocusBlend; float _FocusHistoryValid; };





void main()
{
    Graphite_ViewData view = graphite_PostViews[0];
    float target = _ManualFocusPoint;
    if (_UseAutoFocus > 0.5)
    {
        ivec2 size = DepthSize();
        vec2 taps[5] = vec2[] (vec2(0.5, 0.5), vec2(0.48, 0.5), vec2(0.52, 0.5), vec2(0.5, 0.48), vec2(0.5, 0.52));
        target = 1e30;
        for (int i = 0; i < 5; i ++)
        {
            ivec2 pixel = clamp(ivec2(taps[i] * vec2(size)), ivec2(0), size - 1);
            target = min(target, LinearDepth(view, taps[i], FetchDepth(pixel)));
        }
    }
    float focus = _FocusHistoryValid > 0.5 ? mix(texture(_PrevFocusTex, vec2(0.5)).r, target, _FocusBlend) : target;
    outColor = vec4(focus, 0.0, 0.0, 1.0);
}















#line 10 "Graphite/DepthOfField"

