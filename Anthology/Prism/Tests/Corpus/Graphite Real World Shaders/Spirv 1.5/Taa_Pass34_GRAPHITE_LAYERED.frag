#version 460
#extension GL_GOOGLE_include_directive : enable
#line 1 "Graphite/Taa"

#extension GL_GOOGLE_cpp_style_line_directive : require
#line 7 "Graphite/Taa"

#line 1 "Graphite/Fullscreen.glsl"


#pragma variantGRAPHITE_LAYERED
#pragma variantGRAPHITE_NO_SOURCE

#extension GL_EXT_multiview : require

































layout(location = 0) in vec2 vUV;

layout(location = 0) out vec4 outColor;


layout(set = 0, binding = 0) uniform sampler2DArray Graphite_Source;
vec4 Graphite_SampleSource(vec2 uv) { return texture(Graphite_Source, vec3(uv, float(gl_ViewIndex))); }


#line 9 "Graphite/Taa"
#line 1 "Graphite/Internal/Taa.glsl"
#pragma variantTAA_SHARPEN





float Taa_MaxChannel(vec3 c) { return max(c.r, max(c.g, c.b)); }


vec3 Taa_Tonemap(vec3 c) { return c / (1.0 + Taa_MaxChannel(c)); }
vec3 Taa_InverseTonemap(vec3 c) { return c / max(1.0 - Taa_MaxChannel(c), 1e-4); }























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
#line 36 "Graphite/Internal/Taa.glsl"

layout(set = 0, binding = 2) uniform sampler2DArray _History;
layout(set = 0, binding = 3) uniform sampler2DArray _Motion;
layout(set = 0, binding = 4) uniform sampler2DArray _Depth;
layout(set = 0, binding = 5, std140) uniform _GraphiteGlobal { vec2 _Resolution; float _HistoryValid; float _BlendFactor; float _MotionBlendFactor; float _MotionScale; };






vec4 Taa_SampleHistory(vec2 uv, vec2 texelSize)
{
    vec2 position = uv * _Resolution;
    vec2 center = floor(position - 0.5) + 0.5;
    vec2 f = position - center;
    vec2 f2 = f * f;
    vec2 f3 = f2 * f;
    vec2 w0 = f2 - 0.5 * (f3 + f);
    vec2 w1 = 1.5 * f3 - 2.5 * f2 + 1.0;
    vec2 w2 = - 1.5 * f3 + 2.0 * f2 + 0.5 * f;
    vec2 w3 = 0.5 * (f3 - f2);
    vec2 w12 = w1 + w2;
    vec2 tc12 = (center + w2 / w12) * texelSize;
    vec2 tc0 = (center - 1.0) * texelSize;
    vec2 tc3 = (center + 2.0) * texelSize;
    vec4 result =
        (texture(_History, vec3(vec2(tc12.x, tc0.y), float(gl_ViewIndex))) * w12.x + texture(_History, vec3(vec2(tc0.x, tc0.y), float(gl_ViewIndex))) * w0.x + texture(_History, vec3(vec2(tc3.x, tc0.y), float(gl_ViewIndex))) * w3.x) * w0.y +
        (texture(_History, vec3(vec2(tc12.x, tc12.y), float(gl_ViewIndex))) * w12.x + texture(_History, vec3(vec2(tc0.x, tc12.y), float(gl_ViewIndex))) * w0.x + texture(_History, vec3(vec2(tc3.x, tc12.y), float(gl_ViewIndex))) * w3.x) * w12.y +
        (texture(_History, vec3(vec2(tc12.x, tc3.y), float(gl_ViewIndex))) * w12.x + texture(_History, vec3(vec2(tc0.x, tc3.y), float(gl_ViewIndex))) * w0.x + texture(_History, vec3(vec2(tc3.x, tc3.y), float(gl_ViewIndex))) * w3.x) * w3.y;
    return max(result, vec4(0.0));
}

vec3 Taa_ToYCoCg(vec3 rgb) { return vec3(0.25 * rgb.r + 0.5 * rgb.g + 0.25 * rgb.b, 0.5 * rgb.r - 0.5 * rgb.b, - 0.25 * rgb.r + 0.5 * rgb.g - 0.25 * rgb.b); }
vec3 Taa_FromYCoCg(vec3 c) { return vec3(c.x + c.y - c.z, c.x + c.z, c.x - c.y - c.z); }


vec3 Taa_Sanitize(vec3 c) { return any(isnan(c)) || any(isinf(c)) ? vec3(0.0) : max(c, vec3(0.0)); }

vec3 Taa_Current(vec2 uv) { return Taa_ToYCoCg(Taa_Tonemap(Taa_Sanitize(Graphite_SampleSource(uv).rgb))); }


vec2 Taa_Motion(vec2 uv, vec2 texelSize)
{
    float nearest = - 1.0;
    vec2 nearestUV = uv;
    for (int y = - 1; y <= 1; y ++)
    {
        for (int x = - 1; x <= 1; x ++)
        {
            vec2 at = uv + vec2(float(x), float(y)) * texelSize;
            float depth = texture(_Depth, vec3(at, float(gl_ViewIndex))).r;
            if (depth > nearest)
            {
                nearest = depth;
                nearestUV = at;
            }
        }
    }
    if (nearest > 0.0)
        return texture(_Motion, vec3(nearestUV, float(gl_ViewIndex))).rg;
    Graphite_ViewData view = Graphite_PostView();
    vec4 previous = view.previousViewProjection * view.inverseViewProjection * vec4(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0, 0.0, 1.0);
    if (abs(previous.w) < 1e-8)
        return vec2(0.0);
    vec2 previousUV = vec2(previous.x / previous.w * 0.5 + 0.5, 0.5 - previous.y / previous.w * 0.5);
    return uv - previousUV;
}

void main()
{
    vec2 texelSize = 1.0 / _Resolution;
    vec3 m1 = vec3(0.0), m2 = vec3(0.0);
    vec3 boxMin = vec3(1e9), boxMax = vec3(- 1e9);
    vec3 current = vec3(0.0);
    for (int y = - 1; y <= 1; y ++)
    {
        for (int x = - 1; x <= 1; x ++)
        {
            vec3 s = Taa_Current(vUV + vec2(float(x), float(y)) * texelSize);
            if (x == 0 && y == 0)
                current = s;
            m1 += s;
            m2 += s * s;
            boxMin = min(boxMin, s);
            boxMax = max(boxMax, s);
        }
    }

    vec2 motion = Taa_Motion(vUV, texelSize);
    vec2 historyUV = vUV - motion;
    if (_HistoryValid < 0.5 || any(lessThan(historyUV, vec2(0.0))) || any(greaterThan(historyUV, vec2(1.0))))
    {
        outColor = vec4(Taa_InverseTonemap(max(Taa_FromYCoCg(current), vec3(0.0))), Graphite_SampleSource(vUV).a);
        return;
    }

    vec3 history = Taa_ToYCoCg(Taa_Tonemap(Taa_Sanitize(Taa_SampleHistory(historyUV, texelSize).rgb)));
    m1 /= 9.0;
    m2 /= 9.0;
    vec3 sigma = sqrt(max(m2 - m1 * m1, vec3(0.0)));
    float motionLength = length(motion * _Resolution);
    float gamma = mix(1.25, 0.75, clamp(motionLength * _MotionScale, 0.0, 1.0));
    vec3 clampMin = m1 - gamma * sigma;
    vec3 clampMax = m1 + gamma * sigma;
    clampMin.yz = max(clampMin.yz, boxMin.yz);
    clampMax.yz = min(clampMax.yz, boxMax.yz);
    history = clamp(history, clampMin, clampMax);


    float blendFactor = mix(_BlendFactor, _MotionBlendFactor, clamp(motionLength * 0.1, 0.0, 1.0));
    float lumaDifference = abs(current.x - history.x) / max(max(current.x, history.x), 0.2);
    blendFactor = mix(blendFactor, max(blendFactor, 0.98), (1.0 - lumaDifference) * (1.0 - lumaDifference));
    vec3 result = Taa_FromYCoCg(mix(current, history, blendFactor));
    outColor = vec4(Taa_InverseTonemap(max(result, vec3(0.0))), Graphite_SampleSource(vUV).a);
}

#line 10 "Graphite/Taa"

