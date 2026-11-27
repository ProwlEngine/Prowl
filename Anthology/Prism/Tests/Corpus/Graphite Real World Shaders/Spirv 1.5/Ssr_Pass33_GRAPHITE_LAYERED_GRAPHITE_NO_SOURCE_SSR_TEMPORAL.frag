#version 460
#extension GL_GOOGLE_include_directive : enable
#line 1 "Graphite/Ssr"

#extension GL_GOOGLE_cpp_style_line_directive : require
#line 7 "Graphite/Ssr"

#line 1 "Graphite/Fullscreen.glsl"


#pragma variantGRAPHITE_LAYERED
#pragma variantGRAPHITE_NO_SOURCE

#extension GL_EXT_multiview : require

































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
#line 10 "Graphite/Internal/Ssr.glsl"



float Luminance(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

vec3 SsrViewNormal(Graphite_ViewData view, vec2 encoded) { return normalize(mat3(view.view) * Graphite_OctDecode(encoded)); }


vec3 SsrCameraToSurface(Graphite_ViewData view, vec2 uv, vec3 viewPos)
{
    if (Graphite_PostOrthographic(view))
        return normalize(Graphite_PostViewPosition(view, uv, 0.0) - Graphite_PostViewPosition(view, uv, 1.0));
    return normalize(viewPos);
}







































































































































































































































layout(set = 0, binding = 1) uniform sampler2DArray _MainTex;
layout(set = 0, binding = 2) uniform sampler2DArray _PreviousBuffer;
layout(set = 0, binding = 3) uniform sampler2DArray _RayCast;
layout(set = 0, binding = 4) uniform sampler2DArray _CameraDepthTexture;
layout(set = 0, binding = 5) uniform sampler2DArray _CameraMotionVectorsTexture;
layout(set = 0, binding = 6, std140) uniform _GraphiteGlobal { int _ReflectionVelocity; float _TScale; float _TResponse; vec2 _ScreenSize; };





vec2 ReflectionVelocity(vec2 uv)
{
    Graphite_ViewData view = Graphite_PostView();
    vec4 rd = texture(_RayCast, vec3(uv, float(gl_ViewIndex)));
    float hitDepth = max(texture(_CameraDepthTexture, vec3(rd.w > 0.0 ? rd.xy : uv, float(gl_ViewIndex))).r, 1e-7);
    return uv - Graphite_PostPreviousUV(view, Graphite_PostRelativePosition(view, uv, hitDepth));
}

void main()
{
    vec2 velocity = _ReflectionVelocity == 1 ? ReflectionVelocity(vUV) : texture(_CameraMotionVectorsTexture, vec3(vUV, float(gl_ViewIndex))).rg;
    vec4 current = texture(_MainTex, vec3(vUV, float(gl_ViewIndex)));
    vec4 previous = texture(_PreviousBuffer, vec3(vUV - velocity, float(gl_ViewIndex)));
    vec2 du = vec2(1.0 / _ScreenSize.x, 0.0);
    vec2 dv = vec2(0.0, 1.0 / _ScreenSize.y);
    vec4 mn = current, mx = current;
    for (int x = - 1; x <= 1; x ++)
    {
        for (int y = - 1; y <= 1; y ++)
        {
            vec4 c = texture(_MainTex, vec3(vUV + du * float(x) + dv * float(y), float(gl_ViewIndex)));
            mn = min(mn, c);
            mx = max(mx, c);
        }
    }
    vec4 center = (mn + mx) * 0.5;
    mn = (mn - center) * _TScale + center;
    mx = (mx - center) * _TScale + center;
    previous = clamp(previous, mn, mx);
    outColor = mix(current, previous, clamp(_TResponse * (1.0 - length(velocity) * 8.0), 0.0, 1.0));
}











































#line 10 "Graphite/Ssr"

