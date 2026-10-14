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


layout(set = 0, binding = 0) uniform sampler2D Graphite_Source;
vec4 Graphite_SampleSource(vec2 uv) { return texture(Graphite_Source, uv); }


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
#line 10 "Graphite/Internal/Ssr.glsl"



float Luminance(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

vec3 SsrViewNormal(Graphite_ViewData view, vec2 encoded) { return normalize(mat3(view.view) * Graphite_OctDecode(encoded)); }


vec3 SsrCameraToSurface(Graphite_ViewData view, vec2 uv, vec3 viewPos)
{
    if (Graphite_PostOrthographic(view))
        return normalize(Graphite_PostViewPosition(view, uv, 0.0) - Graphite_PostViewPosition(view, uv, 1.0));
    return normalize(viewPos);
}


layout(set = 0, binding = 2) uniform sampler2D _CameraDepthTexture;
layout(set = 0, binding = 3) uniform sampler2D _CameraNormalsTexture;
layout(set = 0, binding = 4) uniform sampler2D _MaterialTexture;
layout(set = 0, binding = 5) uniform sampler2D _Noise;
layout(set = 0, binding = 6, std140) uniform _GraphiteGlobal { vec4 _JitterSizeAndOffset; float _NumSteps; float _BRDFBias; vec2 _ScreenSize; };




Graphite_ViewData ssr_View;


vec4 ImportanceSampleGGX(vec2 Xi, float roughness)
{
    float m = roughness * roughness;
    float m2 = m * m;
    float phi = 2.0 * 3.14159265359 * Xi.x;
    float cosT = sqrt( (1.0 - Xi.y) / (1.0 + (m2 - 1.0) * Xi.y));
    float sinT = sqrt(max(1e-5, 1.0 - cosT * cosT));
    vec3 h = vec3(sinT * cos(phi), sinT * sin(phi), cosT);
    float d = (cosT * m2 - cosT) * cosT + 1.0;
    float D = m2 / (3.14159265359 * d * d);
    return vec4(h, D * cosT);
}

vec3 TangentToView(vec3 n, vec3 h)
{
    vec3 up = abs(n.z) < 0.999 ? vec3(0.0, 0.0, 1.0) : vec3(1.0, 0.0, 0.0);
    vec3 t = normalize(cross(up, n));
    vec3 b = cross(n, t);
    return t * h.x + b * h.y + n * h.z;
}


vec3 ProjectToScreen(vec3 viewPos)
{
    vec4 clip = ssr_View.projection * vec4(viewPos, 1.0);
    vec3 ndc = clip.xyz / clip.w;
    return vec3(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5, ndc.z);
}


bool Trace(vec3 startScreen, vec3 reflViewDir, float jitter, out vec2 hitUV)
{
    vec3 startView = Graphite_PostViewPosition(ssr_View, startScreen.xy, startScreen.z);
    vec3 endScreen = ProjectToScreen(startView + reflViewDir * 100.0);
    vec3 delta = endScreen - startScreen;
    hitUV = startScreen.xy;
    if (length(delta.xy) < 1e-4)
        return false;
    float steps = min(max(abs(delta.x) * _ScreenSize.x, abs(delta.y) * _ScreenSize.y), _NumSteps);
    vec3 step = delta / max(steps, 1.0);
    vec3 cur = startScreen + step * (1.0 + jitter);
    bool crossed = false;
    for (float i = 1.0; i < steps; i += 1.0)
    {
        if (cur.x < 0.0 || cur.x > 1.0 || cur.y < 0.0 || cur.y > 1.0)
            return false;
        float sceneDepth = texture(_CameraDepthTexture, cur.xy).r;
        if (sceneDepth > 0.0 && cur.z < sceneDepth - 0.0001)
        {
            crossed = true;
            break;
        }
        cur += step;
    }
    if (! crossed)
        return false;
    vec3 lo = cur - step, hi = cur;
    for (int j = 0; j < 5; j ++)
    {
        vec3 mid = (lo + hi) * 0.5;
        if (mid.z < texture(_CameraDepthTexture, mid.xy).r)
            hi = mid;
        else
            lo = mid;
    }
    hitUV = hi.xy;
    return true;
}

void main()
{
    ssr_View = Graphite_PostView();
    float depth = texture(_CameraDepthTexture, vUV).r;
    if (depth <= 0.0)
    {
        outColor = vec4(0.0);
        return;
    }
    vec3 viewNormal = SsrViewNormal(ssr_View, texture(_CameraNormalsTexture, vUV).xy);
    float roughness = texture(_MaterialTexture, vUV).r;
    vec3 viewPos = Graphite_PostViewPosition(ssr_View, vUV, depth);
    vec3 V = SsrCameraToSurface(ssr_View, vUV, viewPos);

    vec2 Xi = texture(_Noise, (vUV + _JitterSizeAndOffset.zw) * _JitterSizeAndOffset.xy).rg;
    Xi.y = mix(Xi.y, 0.0, _BRDFBias);
    vec4 H = ImportanceSampleGGX(Xi, roughness);
    vec3 h = TangentToView(viewNormal, H.xyz);
    vec3 reflDir = reflect(V, h);
    float jitter = (Xi.x + Xi.y) * (1.0 / max(_NumSteps, 1.0));
    vec2 hitUV;
    bool hit = Trace(vec3(vUV, depth), reflDir, jitter, hitUV);
    outColor = hit ? vec4(hitUV, H.w, 1.0) : vec4(0.0);
}

















































































































































































































#line 10 "Graphite/Ssr"

