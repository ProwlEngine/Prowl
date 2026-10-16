#pragma variant TAA_SHARPEN
// Prowl's temporal anti aliasing resolve and sharpen, on a fullscreen pass. The resolve blends the jittered frame with the
// history reprojected by the nearest surface's motion, clipped to the neighbourhood's colour spread, in tonemapped YCoCg.
// Sky pixels carry no motion, so they reproject their direction through last frame's view instead. Layered views resolve
// each layer against its own history and view.

float Taa_MaxChannel(vec3 c) { return max(c.r, max(c.g, c.b)); }

// Dividing by the largest channel keeps every channel below 1, so bright HDR pixels can't dominate the blend
vec3 Taa_Tonemap(vec3 c) { return c / (1.0 + Taa_MaxChannel(c)); }
vec3 Taa_InverseTonemap(vec3 c) { return c / max(1.0 - Taa_MaxChannel(c), 1e-4); }

#ifdef TAA_SHARPEN
uniform GRAPHITE_SCREEN_TEXTURE _Resolved;
uniform vec2 _Resolution;
uniform float _Sharpness;

float Taa_Luma(vec3 c) { return dot(c, vec3(0.299, 0.587, 0.114)); }

// Sharpens brightness only, in tonemapped space, after the resolve was stored as history
void main()
{
    vec2 texel = 1.0 / _Resolution;
    vec3 center = Taa_Tonemap(Graphite_Sample(_Resolved, vUV).rgb);
    float blur = (Taa_Luma(Taa_Tonemap(Graphite_Sample(_Resolved, vUV + vec2(-texel.x, 0.0)).rgb))
        + Taa_Luma(Taa_Tonemap(Graphite_Sample(_Resolved, vUV + vec2(texel.x, 0.0)).rgb))
        + Taa_Luma(Taa_Tonemap(Graphite_Sample(_Resolved, vUV + vec2(0.0, -texel.y)).rgb))
        + Taa_Luma(Taa_Tonemap(Graphite_Sample(_Resolved, vUV + vec2(0.0, texel.y)).rgb))) * 0.25;
    float luma = Taa_Luma(center);
    float sharpened = max(luma + (luma - blur) * _Sharpness, 0.0);
    vec3 result = luma > 1e-5 ? center * (sharpened / luma) : center;
    outColor = vec4(Taa_InverseTonemap(min(result, vec3(0.999))), Graphite_Sample(_Resolved, vUV).a);
}
#else
#include "Graphite/Internal/PostView.glsl"

uniform GRAPHITE_SCREEN_TEXTURE _History;
uniform GRAPHITE_SCREEN_TEXTURE _Motion;
uniform GRAPHITE_SCREEN_TEXTURE _Depth;
uniform vec2 _Resolution;
uniform float _HistoryValid;
uniform float _BlendFactor;
uniform float _MotionBlendFactor;
uniform float _MotionScale;

// Catmull Rom history in four bilinear taps per row, sharper than bilinear
vec4 Taa_SampleHistory(vec2 uv, vec2 texelSize)
{
    vec2 position = uv * _Resolution;
    vec2 center = floor(position - 0.5) + 0.5;
    vec2 f = position - center;
    vec2 f2 = f * f;
    vec2 f3 = f2 * f;
    vec2 w0 = f2 - 0.5 * (f3 + f);
    vec2 w1 = 1.5 * f3 - 2.5 * f2 + 1.0;
    vec2 w2 = -1.5 * f3 + 2.0 * f2 + 0.5 * f;
    vec2 w3 = 0.5 * (f3 - f2);
    vec2 w12 = w1 + w2;
    vec2 tc12 = (center + w2 / w12) * texelSize;
    vec2 tc0 = (center - 1.0) * texelSize;
    vec2 tc3 = (center + 2.0) * texelSize;
    vec4 result =
        (Graphite_Sample(_History, vec2(tc12.x, tc0.y)) * w12.x + Graphite_Sample(_History, vec2(tc0.x, tc0.y)) * w0.x + Graphite_Sample(_History, vec2(tc3.x, tc0.y)) * w3.x) * w0.y +
        (Graphite_Sample(_History, vec2(tc12.x, tc12.y)) * w12.x + Graphite_Sample(_History, vec2(tc0.x, tc12.y)) * w0.x + Graphite_Sample(_History, vec2(tc3.x, tc12.y)) * w3.x) * w12.y +
        (Graphite_Sample(_History, vec2(tc12.x, tc3.y)) * w12.x + Graphite_Sample(_History, vec2(tc0.x, tc3.y)) * w0.x + Graphite_Sample(_History, vec2(tc3.x, tc3.y)) * w3.x) * w3.y;
    return max(result, vec4(0.0));
}

vec3 Taa_ToYCoCg(vec3 rgb) { return vec3(0.25 * rgb.r + 0.5 * rgb.g + 0.25 * rgb.b, 0.5 * rgb.r - 0.5 * rgb.b, -0.25 * rgb.r + 0.5 * rgb.g - 0.25 * rgb.b); }
vec3 Taa_FromYCoCg(vec3 c) { return vec3(c.x + c.y - c.z, c.x + c.z, c.x - c.y - c.z); }

// NaN or infinite input would otherwise live forever in the history
vec3 Taa_Sanitize(vec3 c) { return any(isnan(c)) || any(isinf(c)) ? vec3(0.0) : max(c, vec3(0.0)); }

vec3 Taa_Current(vec2 uv) { return Taa_ToYCoCg(Taa_Tonemap(Taa_Sanitize(Graphite_SampleSource(uv).rgb))); }

// The motion of the nearest surface around the pixel, reversed Z so the nearest is the largest depth; the sky's from its direction
vec2 Taa_Motion(vec2 uv, vec2 texelSize)
{
    float nearest = -1.0;
    vec2 nearestUV = uv;
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            vec2 at = uv + vec2(float(x), float(y)) * texelSize;
            float depth = Graphite_Sample(_Depth, at).r;
            if (depth > nearest)
            {
                nearest = depth;
                nearestUV = at;
            }
        }
    }
    if (nearest > 0.0)
        return Graphite_Sample(_Motion, nearestUV).rg;
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
    vec3 boxMin = vec3(1e9), boxMax = vec3(-1e9);
    vec3 current = vec3(0.0);
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
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

    // Toward the motion weight as the pixel moves faster, never without history, or a fast pan shows the raw jitter
    float blendFactor = mix(_BlendFactor, _MotionBlendFactor, clamp(motionLength * 0.1, 0.0, 1.0));
    float lumaDifference = abs(current.x - history.x) / max(max(current.x, history.x), 0.2);
    blendFactor = mix(blendFactor, max(blendFactor, 0.98), (1.0 - lumaDifference) * (1.0 - lumaDifference));
    vec3 result = Taa_FromYCoCg(mix(current, history, blendFactor));
    outColor = vec4(Taa_InverseTonemap(max(result, vec3(0.0))), Graphite_SampleSource(vUV).a);
}
#endif
