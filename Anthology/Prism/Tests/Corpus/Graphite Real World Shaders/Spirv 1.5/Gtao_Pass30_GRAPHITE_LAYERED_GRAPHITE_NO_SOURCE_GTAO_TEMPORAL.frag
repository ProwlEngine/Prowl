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



































































































































layout(set = 0, binding = 1) uniform sampler2DArray _MainTex;
layout(set = 0, binding = 2) uniform sampler2DArray _PreviousBuffer;
layout(set = 0, binding = 3) uniform sampler2DArray _CameraDepthTexture;
layout(set = 0, binding = 4, std140) uniform _GraphiteGlobal { float _TResponse; };

void main()
{
    float current = texture(_MainTex, vec3(vUV, float(gl_ViewIndex))).r;
    float depth = texture(_CameraDepthTexture, vec3(vUV, float(gl_ViewIndex))).r;
    Graphite_ViewData view = Graphite_PostView();
    vec2 prevUV = depth <= 0.0 ? vUV : Graphite_PostPreviousUV(view, Graphite_PostRelativePosition(view, vUV, depth));


    vec2 texel = 1.0 / vec2(textureSize(_MainTex, 0).xy);
    float mn = current, mx = current;
    for (int x = - 1; x <= 1; x ++)
    {
        for (int y = - 1; y <= 1; y ++)
        {
            float s = texture(_MainTex, vec3(vUV + texel * vec2(float(x), float(y)), float(gl_ViewIndex))).r;
            mn = min(mn, s);
            mx = max(mx, s);
        }
    }
    float previous = clamp(texture(_PreviousBuffer, vec3(prevUV, float(gl_ViewIndex))).r, mn, mx);
    float response = (prevUV.x < 0.0 || prevUV.x > 1.0 || prevUV.y < 0.0 || prevUV.y > 1.0) ? 0.0 : _TResponse;
    outColor = vec4(vec3(mix(current, previous, response)), 1.0);
}






































#line 10 "Graphite/Gtao"

