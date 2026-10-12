// Prowl's GTAO, written as the view's screen lighting visibility: depth point sampled to the AO resolution, the horizon
// search over slices with blue noise scrolled per frame, temporal accumulation clamped to the 3x3 neighbourhood, then a
// depth aware blur. Normals are rebuilt from depth, which is all the prepass leaves, and history is reprojected through
// depth and last frame's view, as motion is not drawn yet. Positions are in the view matrix's space, the forward axis
// read from the projection, so either handedness works.

#include "Graphite/Internal/PostView.glsl"

#define GTAO_PI 3.14159265359
#define GTAO_HALF_PI 1.57079632679

float Gtao_FastAcos(float x)
{
    float y = abs(x);
    float p = -0.0187293 * y + 0.0742610;
    p = p * y - 0.2121144;
    p = p * y + 1.5707288;
    p = p * sqrt(1.0 - y);
    return x >= 0.0 ? p : GTAO_PI - p;
}

#if defined(GTAO_DEPTH)
uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;

void main()
{
    ivec2 full = ivec2(vUV * vec2(Graphite_Size(_CameraDepthTexture, 0)));
    outColor = vec4(Graphite_Fetch(_CameraDepthTexture, full, 0).r, 0.0, 0.0, 1.0);
}
#elif defined(GTAO_AO)
uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
uniform sampler2D _Noise;
uniform int _Slices;
uniform int _DirectionSamples;
uniform float _Radius;
uniform float _Intensity;
uniform vec2 _NoiseScale;
uniform vec2 _JitterOffset;

Graphite_ViewData gtao_View;

vec3 ViewPosition(vec2 uv, float depth) { return Graphite_PostViewPosition(gtao_View, uv, depth); }

// The direction toward the camera, the same everywhere under an orthographic projection
vec3 TowardCamera(vec2 uv, vec3 viewPos)
{
    if (Graphite_PostOrthographic(gtao_View))
        return normalize(ViewPosition(uv, 1.0) - ViewPosition(uv, 0.0));
    return -normalize(viewPos);
}

// A normal from the neighbours closest in position on each axis, facing the camera
vec3 NormalFromDepth(vec2 uv, vec3 center, vec3 viewDir)
{
    vec2 texel = 1.0 / vec2(Graphite_Size(_CameraDepthTexture, 0));
    vec3 l = ViewPosition(uv - vec2(texel.x, 0.0), Graphite_Sample(_CameraDepthTexture, uv - vec2(texel.x, 0.0)).r);
    vec3 r = ViewPosition(uv + vec2(texel.x, 0.0), Graphite_Sample(_CameraDepthTexture, uv + vec2(texel.x, 0.0)).r);
    vec3 u = ViewPosition(uv - vec2(0.0, texel.y), Graphite_Sample(_CameraDepthTexture, uv - vec2(0.0, texel.y)).r);
    vec3 d = ViewPosition(uv + vec2(0.0, texel.y), Graphite_Sample(_CameraDepthTexture, uv + vec2(0.0, texel.y)).r);
    vec3 dx = dot(r - center, r - center) < dot(center - l, center - l) ? r - center : center - l;
    vec3 dy = dot(d - center, d - center) < dot(center - u, center - u) ? d - center : center - u;
    vec3 n = cross(dx, dy);
    if (!(dot(n, n) > 1e-20))
        return viewDir;
    n = normalize(n);
    return dot(n, viewDir) < 0.0 ? -n : n;
}

void SampleHorizonCos(vec2 coord, vec2 offset, vec3 viewPos, vec3 viewDir, vec2 falloff, inout float cHorizonCos)
{
    vec2 sTexCoord = coord + offset;
    if (sTexCoord.x < 0.0 || sTexCoord.x > 1.0 || sTexCoord.y < 0.0 || sTexCoord.y > 1.0)
        return;
    float sDepth = Graphite_Sample(_CameraDepthTexture, sTexCoord).r;
    if (sDepth <= 0.0)
        return;
    vec3 sHorizonV = ViewPosition(sTexCoord, sDepth) - viewPos;
    float sLenV = dot(sHorizonV, sHorizonV);

    // Neighbours rebuilt to almost the centre are depth precision noise, not occlusion
    float minLen = 0.0015 * abs(viewPos.z);
    if (sLenV < minLen * minLen)
        return;
    float sHorizonCos = dot(sHorizonV, viewDir) * inversesqrt(sLenV);
    sHorizonCos = mix(sHorizonCos, cHorizonCos, clamp((sLenV - falloff.x) / (falloff.y - falloff.x), 0.0, 1.0));
    cHorizonCos = max(sHorizonCos, cHorizonCos);
}

float CalculateGTAO(vec2 coord, vec3 viewPos, vec3 viewDir, vec3 normal, vec2 dither)
{
    float viewDistance = length(viewPos);
    float rSliceCount = 1.0 / float(_Slices);
    float rSampleCount = 1.0 / float(_DirectionSamples);
    float radius = _Radius * clamp(0.25 + viewDistance / 64.0, 0.0, 1.0);

    // A world radius covers less of the screen further away, under perspective only. Screen steps follow the
    // projection's signs, the uv running down the screen.
    float radiusToScreen = Graphite_PostOrthographic(gtao_View) ? 1.0 : 1.0 / viewDistance;
    vec2 scale = vec2(gtao_View.projection[0][0], -gtao_View.projection[1][1]);
    vec2 sRadius = rSampleCount * radius * radiusToScreen * abs(scale);
    sRadius = max(sRadius, vec2(1.5) / vec2(Graphite_Size(_CameraDepthTexture, 0))) * sign(scale);
    vec2 falloff = (radius * vec2(1.0, 4.0)) * (radius * vec2(1.0, 4.0));

    float visibility = 0.0;
    for (int slice = 0; slice < _Slices; ++slice)
    {
        float slicePhi = (float(slice) + dither.x) * (GTAO_PI * rSliceCount);
        vec3 directionV = vec3(cos(slicePhi), sin(slicePhi), 0.0);
        vec3 orthoDirectionV = directionV - dot(directionV, viewDir) * viewDir;
        vec3 axisV = cross(directionV, viewDir);
        vec3 projNormalV = normal - axisV * dot(normal, axisV);
        float lenV = dot(projNormalV, projNormalV);
        float normV = inversesqrt(lenV);
        lenV *= normV;
        float sgnN = dot(orthoDirectionV, projNormalV) >= 0.0 ? 1.0 : -1.0;
        float cosN = clamp(dot(projNormalV, viewDir) * normV, 0.0, 1.0);
        float n = sgnN * Gtao_FastAcos(cosN);

        vec2 cHorizonCos = vec2(-1.0);
        vec2 stepDir = directionV.xy * sRadius;
        for (int samp = 0; samp < _DirectionSamples; ++samp)
        {
            vec2 offset = (float(samp) + dither.y) * stepDir;
            SampleHorizonCos(coord, offset, viewPos, viewDir, falloff, cHorizonCos.x);
            SampleHorizonCos(coord, -offset, viewPos, viewDir, falloff, cHorizonCos.y);
        }

        vec2 h = n + clamp(vec2(Gtao_FastAcos(cHorizonCos.x), -Gtao_FastAcos(cHorizonCos.y)) - n, -GTAO_HALF_PI, GTAO_HALF_PI);
        h = cosN + 2.0 * h * sin(n) - cos(2.0 * h - n);
        visibility += lenV * (h.x + h.y);
    }
    return 0.25 * rSliceCount * visibility;
}

void main()
{
    gtao_View = Graphite_PostView();
    float depth = Graphite_Sample(_CameraDepthTexture, vUV).r;
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
#elif defined(GTAO_TEMPORAL)
uniform GRAPHITE_SCREEN_TEXTURE _MainTex;
uniform GRAPHITE_SCREEN_TEXTURE _PreviousBuffer;
uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
uniform float _TResponse;

void main()
{
    float current = Graphite_Sample(_MainTex, vUV).r;
    float depth = Graphite_Sample(_CameraDepthTexture, vUV).r;
    Graphite_ViewData view = Graphite_PostView();
    vec2 prevUV = depth <= 0.0 ? vUV : Graphite_PostPreviousUV(view, Graphite_PostRelativePosition(view, vUV, depth));

    // Bound the history to the current 3x3 range so it cannot ghost where occlusion changed
    vec2 texel = 1.0 / vec2(Graphite_Size(_MainTex, 0));
    float mn = current, mx = current;
    for (int x = -1; x <= 1; x++)
    {
        for (int y = -1; y <= 1; y++)
        {
            float s = Graphite_Sample(_MainTex, vUV + texel * vec2(float(x), float(y))).r;
            mn = min(mn, s);
            mx = max(mx, s);
        }
    }
    float previous = clamp(Graphite_Sample(_PreviousBuffer, prevUV).r, mn, mx);
    float response = (prevUV.x < 0.0 || prevUV.x > 1.0 || prevUV.y < 0.0 || prevUV.y > 1.0) ? 0.0 : _TResponse;
    outColor = vec4(vec3(mix(current, previous, response)), 1.0);
}
#elif defined(GTAO_BLUR)
uniform GRAPHITE_SCREEN_TEXTURE _MainTex;
uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
uniform vec2 _BlurDirection;
uniform float _BlurRadius;
uniform vec2 _ScreenSize;

float LinearDepth(Graphite_ViewData view, vec2 uv)
{
    return abs(Graphite_PostViewPosition(view, uv, max(Graphite_Sample(_CameraDepthTexture, uv).r, 1e-7)).z);
}

// Depth aware, its taps a texel of the full resolution apart as Prowl's are, the depth difference taken as a fraction
// of the centre's depth
void main()
{
    Graphite_ViewData view = Graphite_PostView();
    vec2 texelSize = 1.0 / _ScreenSize;
    float centerDepth = LinearDepth(view, vUV);
    vec4 result = Graphite_Sample(_MainTex, vUV);
    float totalWeight = 1.0;
    for (int i = -2; i <= 2; i++)
    {
        if (i == 0)
            continue;
        vec2 sampleUV = vUV + _BlurDirection * texelSize * (float(i) * _BlurRadius);
        float depthDiff = abs(centerDepth - LinearDepth(view, sampleUV)) / max(centerDepth, 1e-4);
        float weight = exp(-depthDiff * 100.0) * exp(-0.5 * float(i * i) / 2.0);
        result += Graphite_Sample(_MainTex, sampleUV) * weight;
        totalWeight += weight;
    }
    outColor = result / totalWeight;
}
#else
uniform GRAPHITE_SCREEN_TEXTURE _MainTex;

void main() { outColor = Graphite_Sample(_MainTex, vUV); }
#endif
