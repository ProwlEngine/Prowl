#pragma variant DOF COMBINE COC PREFILTER BOKEH POSTFILTER FOCUS
// Bokeh depth of field with separate near and far fields. CoC: a signed circle of confusion in full resolution
// pixels, negative in front of focus. Prefilter: the colour of the taps sharing the block's strongest CoC, and that CoC,
// at the blur resolution. Bokeh: the far field from a disk the size of the pixel's own blur, taking samples whose blur
// reaches back, and the near field from a disk the size of the largest blur, weighted so full coverage sums to 1.
// Postfilter: a tent over both. Combine: sharp, then far by the pixel's CoC, then near by its coverage. Focus: the focus
// distance eased toward its target. The disk is the center then rings of 7, 14, 21 and 28 points.

#include "Graphite/Internal/PostView.glsl"

float LinearDepth(Graphite_ViewData view, vec2 uv, float depth)
{
    return abs(Graphite_PostViewPosition(view, uv, max(depth, 1e-7)).z);
}

#if defined(DOF_COC)
uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
uniform sampler2D _FocusTex;
uniform float _FocusStrength;
uniform float _MaxBlurRadius;
uniform vec2 _Resolution;

void main()
{
    Graphite_ViewData view = Graphite_PostView();
    float focus = max(texture(_FocusTex, vec2(0.5)).r, 1e-3);
    float depth = max(LinearDepth(view, vUV, Graphite_Sample(_CameraDepthTexture, vUV).r), 1e-4);
    float maxCoC = _MaxBlurRadius * 0.01 * _Resolution.y;
    float coc = (depth - focus) / depth * _FocusStrength * 0.01 * _Resolution.y;
    outColor = vec4(clamp(coc, -maxCoC, maxCoC), 0.0, 0.0, 1.0);
}
#elif defined(DOF_PREFILTER)
uniform GRAPHITE_SCREEN_TEXTURE _CoCTex;
uniform float _Downscale;

#define MAX_GRID 4

void main()
{
    vec2 fullTexel = 1.0 / vec2(Graphite_Size(Graphite_Source, 0));
    ivec2 cocSize = Graphite_Size(_CoCTex, 0);
    int n = clamp(int(floor(_Downscale * 0.5 + 0.5)), 1, MAX_GRID);
    vec3 colors[MAX_GRID * MAX_GRID];
    float cocs[MAX_GRID * MAX_GRID];
    int taps = 0;
    float cocMin = 0.0;
    float cocMax = 0.0;
    for (int y = 0; y < n; y++)
    {
        for (int x = 0; x < n; x++)
        {
            vec2 uv = vUV + (vec2(float(x), float(y)) * 2.0 + 1.0 - float(n)) * fullTexel;
            vec3 c = Graphite_SampleSource(uv).rgb;
            if (any(isnan(c)) || any(isinf(c)))
                c = vec3(0.0);
            colors[taps] = max(c, vec3(0.0));
            ivec2 cocPixel = clamp(ivec2(uv * vec2(cocSize)), ivec2(0), cocSize - 1);
            cocs[taps] = Graphite_Fetch(_CoCTex, cocPixel, 0).r;
            cocMin = min(cocMin, cocs[taps]);
            cocMax = max(cocMax, cocs[taps]);
            taps++;
        }
    }

    // Only taps sharing the block's strongest blur colour it, so a sharp edge does not tint a blurred background
    float chosen = -cocMin > cocMax ? cocMin : cocMax;
    vec3 color = vec3(0.0);
    float weightSum = 0.0;
    for (int i = 0; i < taps; i++)
    {
        float w = 1.0 - clamp(abs(cocs[i] - chosen) / max(abs(chosen), 1.0), 0.0, 1.0);
        color += colors[i] * w;
        weightSum += w;
    }
    outColor = vec4(color / max(weightSum, 1e-4), chosen / _Downscale);
}
#elif defined(DOF_BOKEH)
layout(location = 1) out vec4 outNear;

uniform GRAPHITE_SCREEN_TEXTURE _MainTex;
uniform int _Rings;
uniform float _MaxCoC;

void main()
{
    vec2 texel = 1.0 / vec2(Graphite_Size(_MainTex, 0));
    vec4 center = Graphite_Sample(_MainTex, vUV);
    int count = 1 + 7 * _Rings * (_Rings + 1) / 2;
    const float margin = 1.0;

    // Far field: a disk the size of this pixel's own blur, taking samples whose own far blur reaches back here
    vec4 far = vec4(center.rgb, 1.0);
    float farRadius = max(center.a, 0.0);
    if (farRadius >= 1.0)
    {
        for (int ring = 1; ring <= _Rings; ring++)
        {
            int points = ring * 7;
            float radius = float(ring) / float(_Rings);
            for (int j = 0; j < points; j++)
            {
                float angle = GRAPHITE_TWO_PI * float(j) / float(points);
                vec2 disp = vec2(cos(angle), sin(angle)) * radius * farRadius;
                vec4 s = Graphite_Sample(_MainTex, vUV + disp * texel);
                far += vec4(s.rgb, 1.0) * clamp((s.a - length(disp) + margin) / margin, 0.0, 1.0);
            }
        }
    }
    far.rgb /= far.a;

    // Near field: a disk the size of the largest blur, each sample covering pi c squared of it
    float area = _MaxCoC * _MaxCoC / float(count);
    vec4 near = vec4(0.0);
    for (int ring = 0; ring <= _Rings; ring++)
    {
        int points = ring == 0 ? 1 : ring * 7;
        float radius = float(ring) / float(max(_Rings, 1));
        for (int j = 0; j < points; j++)
        {
            float angle = GRAPHITE_TWO_PI * float(j) / float(points);
            vec2 disp = vec2(cos(angle), sin(angle)) * radius * _MaxCoC;
            vec4 s = Graphite_Sample(_MainTex, vUV + disp * texel);
            float c = -s.a;
            float reach = clamp((c - length(disp) + margin) / margin, 0.0, 1.0);
            float blurred = smoothstep(0.5, 2.0, c);
            near += vec4(s.rgb, 1.0) * (reach * blurred * area / max(c * c, 1.0));
        }
    }
    near.rgb /= near.a + (near.a == 0.0 ? 1.0 : 0.0);
    outColor = vec4(far.rgb, 1.0);
    outNear = vec4(near.rgb, clamp(near.a, 0.0, 1.0));
}
#elif defined(DOF_POSTFILTER)
layout(location = 1) out vec4 outNear;

uniform GRAPHITE_SCREEN_TEXTURE _FarTex;
uniform GRAPHITE_SCREEN_TEXTURE _NearTex;

void main()
{
    vec2 h = 0.5 / vec2(Graphite_Size(_FarTex, 0));
    outColor = (Graphite_Sample(_FarTex, vUV + vec2(-h.x, -h.y)) + Graphite_Sample(_FarTex, vUV + vec2(h.x, -h.y))
        + Graphite_Sample(_FarTex, vUV + vec2(-h.x, h.y)) + Graphite_Sample(_FarTex, vUV + vec2(h.x, h.y))) * 0.25;
    outNear = (Graphite_Sample(_NearTex, vUV + vec2(-h.x, -h.y)) + Graphite_Sample(_NearTex, vUV + vec2(h.x, -h.y))
        + Graphite_Sample(_NearTex, vUV + vec2(-h.x, h.y)) + Graphite_Sample(_NearTex, vUV + vec2(h.x, h.y))) * 0.25;
}
#elif defined(DOF_FOCUS)
uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
float FetchDepth(ivec2 pixel) { return Graphite_Fetch(_CameraDepthTexture, pixel, 0).r; }
ivec2 DepthSize() { return Graphite_Size(_CameraDepthTexture, 0); }
uniform sampler2D _PrevFocusTex;
uniform float _UseAutoFocus;
uniform float _ManualFocusPoint;
uniform float _FocusBlend;
uniform float _FocusHistoryValid;

// The nearest of a small cross around the first layer's center, unfiltered, so a thin object there holds focus
void main()
{
    Graphite_ViewData view = graphite_PostViews[0];
    float target = _ManualFocusPoint;
    if (_UseAutoFocus > 0.5)
    {
        ivec2 size = DepthSize();
        vec2 taps[5] = vec2[](vec2(0.5, 0.5), vec2(0.48, 0.5), vec2(0.52, 0.5), vec2(0.5, 0.48), vec2(0.5, 0.52));
        target = 1e30;
        for (int i = 0; i < 5; i++)
        {
            ivec2 pixel = clamp(ivec2(taps[i] * vec2(size)), ivec2(0), size - 1);
            target = min(target, LinearDepth(view, taps[i], FetchDepth(pixel)));
        }
    }
    float focus = _FocusHistoryValid > 0.5 ? mix(texture(_PrevFocusTex, vec2(0.5)).r, target, _FocusBlend) : target;
    outColor = vec4(focus, 0.0, 0.0, 1.0);
}
#else
uniform GRAPHITE_SCREEN_TEXTURE _FarTex;
uniform GRAPHITE_SCREEN_TEXTURE _NearTex;
uniform GRAPHITE_SCREEN_TEXTURE _CoCTex;
uniform float _Downscale;

void main()
{
    vec4 original = Graphite_SampleSource(vUV);
    float farAlpha = smoothstep(_Downscale, _Downscale * 2.0, Graphite_Sample(_CoCTex, vUV).r);
    vec3 color = mix(original.rgb, Graphite_Sample(_FarTex, vUV).rgb, farAlpha);
    vec4 near = Graphite_Sample(_NearTex, vUV);
    outColor = vec4(mix(color, near.rgb, near.a), original.a);
}
#endif
