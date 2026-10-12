// Prowl's stochastic screen space reflections. RayCast traces a GGX importance sampled ray per pixel against the depth,
// SceneBlur builds the convolved scene pyramid, Resolve gathers neighbouring rays weighted by BRDF over pdf and picks a
// pyramid level by a roughness cone, Temporal reprojects and clamps against history, Reproject moves last frame's
// result by motion for one bounce of feedback, and Combine adds the reflection over the scene by Fresnel. Depth is
// reversed, normals are the G-buffer's world normals turned into the view matrix's space, and roughness and metallic
// come from the material target.

#include "Graphite/Internal/PostView.glsl"

#define SSR_PI 3.14159265359

float Luminance(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

vec3 SsrViewNormal(Graphite_ViewData view, vec2 encoded) { return normalize(mat3(view.view) * Graphite_OctDecode(encoded)); }

// From the camera to the surface, the same everywhere under an orthographic projection
vec3 SsrCameraToSurface(Graphite_ViewData view, vec2 uv, vec3 viewPos)
{
    if (Graphite_PostOrthographic(view))
        return normalize(Graphite_PostViewPosition(view, uv, 0.0) - Graphite_PostViewPosition(view, uv, 1.0));
    return normalize(viewPos);
}

#if defined(SSR_RAYCAST)
uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
uniform GRAPHITE_SCREEN_TEXTURE _CameraNormalsTexture;
uniform GRAPHITE_SCREEN_TEXTURE _MaterialTexture;
uniform sampler2D _Noise;
uniform vec4 _JitterSizeAndOffset;
uniform float _NumSteps;
uniform float _BRDFBias;
uniform vec2 _ScreenSize;

Graphite_ViewData ssr_View;

// GGX half vector importance sample, with its pdf
vec4 ImportanceSampleGGX(vec2 Xi, float roughness)
{
    float m = roughness * roughness;
    float m2 = m * m;
    float phi = 2.0 * SSR_PI * Xi.x;
    float cosT = sqrt((1.0 - Xi.y) / (1.0 + (m2 - 1.0) * Xi.y));
    float sinT = sqrt(max(1e-5, 1.0 - cosT * cosT));
    vec3 h = vec3(sinT * cos(phi), sinT * sin(phi), cosT);
    float d = (cosT * m2 - cosT) * cosT + 1.0;
    float D = m2 / (SSR_PI * d * d);
    return vec4(h, D * cosT);
}

vec3 TangentToView(vec3 n, vec3 h)
{
    vec3 up = abs(n.z) < 0.999 ? vec3(0.0, 0.0, 1.0) : vec3(1.0, 0.0, 0.0);
    vec3 t = normalize(cross(up, n));
    vec3 b = cross(n, t);
    return t * h.x + b * h.y + n * h.z;
}

// uv and reversed depth of a view space position
vec3 ProjectToScreen(vec3 viewPos)
{
    vec4 clip = ssr_View.projection * vec4(viewPos, 1.0);
    vec3 ndc = clip.xyz / clip.w;
    return vec3(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5, ndc.z);
}

// Steps in screen space until the ray passes behind the depth, then refines by halving
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
        float sceneDepth = Graphite_Sample(_CameraDepthTexture, cur.xy).r;
        if (sceneDepth > 0.0 && cur.z < sceneDepth - 0.0001)
        {
            crossed = true;
            break;
        }
        cur += step;
    }
    if (!crossed)
        return false;
    vec3 lo = cur - step, hi = cur;
    for (int j = 0; j < 5; j++)
    {
        vec3 mid = (lo + hi) * 0.5;
        if (mid.z < Graphite_Sample(_CameraDepthTexture, mid.xy).r)
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
    float depth = Graphite_Sample(_CameraDepthTexture, vUV).r;
    if (depth <= 0.0)
    {
        outColor = vec4(0.0);
        return;
    }
    vec3 viewNormal = SsrViewNormal(ssr_View, Graphite_Sample(_CameraNormalsTexture, vUV).xy);
    float roughness = Graphite_Sample(_MaterialTexture, vUV).r;
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
#elif defined(SSR_BLUR)
uniform GRAPHITE_SCREEN_TEXTURE _MainTex;
uniform vec2 _BlurDir;

// Separable 7 tap Gaussian over the source texels, downsampling on the vertical write
void main()
{
    vec2 step = _BlurDir / vec2(Graphite_Size(_MainTex, 0));
    vec4 c = Graphite_Sample(_MainTex, vUV) * 0.474;
    c += Graphite_Sample(_MainTex, vUV + step * 1.0) * 0.233;
    c += Graphite_Sample(_MainTex, vUV - step * 1.0) * 0.233;
    c += Graphite_Sample(_MainTex, vUV + step * 2.0) * 0.028;
    c += Graphite_Sample(_MainTex, vUV - step * 2.0) * 0.028;
    c += Graphite_Sample(_MainTex, vUV + step * 3.0) * 0.001;
    c += Graphite_Sample(_MainTex, vUV - step * 3.0) * 0.001;
    outColor = c;
}
#elif defined(SSR_RESOLVE)
uniform GRAPHITE_SCREEN_TEXTURE _RayCast;
uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
uniform GRAPHITE_SCREEN_TEXTURE _CameraNormalsTexture;
uniform GRAPHITE_SCREEN_TEXTURE _MaterialTexture;
uniform sampler2D _Noise;
uniform vec2 _NoiseSize;
uniform vec4 _JitterSizeAndOffset;
uniform vec2 _ResolveSize;
uniform vec2 _ScreenSize;
uniform float _BRDFBias;
uniform float _EdgeFactor;
uniform float _MaxMipMap;
uniform int _RayReuse;
uniform int _UseNormalization;
uniform int _Fireflies;
uniform GRAPHITE_SCREEN_TEXTURE _Scene0;
uniform GRAPHITE_SCREEN_TEXTURE _Scene1;
uniform GRAPHITE_SCREEN_TEXTURE _Scene2;
uniform GRAPHITE_SCREEN_TEXTURE _Scene3;
uniform GRAPHITE_SCREEN_TEXTURE _Scene4;

const vec2 resolveOffset[4] = vec2[](vec2(0, 0), vec2(2, -2), vec2(-2, -2), vec2(0, 2));

// Only the two levels around the mip contribute
vec3 SamplePyramid(vec2 uv, float mip)
{
    if (mip <= 1.0)
        return mix(Graphite_Sample(_Scene0, uv).rgb, Graphite_Sample(_Scene1, uv).rgb, mip);
    if (mip <= 2.0)
        return mix(Graphite_Sample(_Scene1, uv).rgb, Graphite_Sample(_Scene2, uv).rgb, mip - 1.0);
    if (mip <= 3.0)
        return mix(Graphite_Sample(_Scene2, uv).rgb, Graphite_Sample(_Scene3, uv).rgb, mip - 2.0);
    return mix(Graphite_Sample(_Scene3, uv).rgb, Graphite_Sample(_Scene4, uv).rgb, clamp(mip - 3.0, 0.0, 1.0));
}

// Smith GGX visibility times the GGX distribution times pi over 4, matched with one over the pdf
float BrdfWeight(vec3 V, vec3 L, vec3 N, float roughness)
{
    vec3 H = normalize(L + V);
    float NdotH = clamp(dot(N, H), 0.0, 1.0);
    float NdotL = clamp(dot(N, L), 0.0, 1.0);
    float NdotV = clamp(dot(N, V), 0.0, 1.0);
    float m = roughness * roughness;
    float m2 = m * m;
    float d = (NdotH * m2 - NdotH) * NdotH + 1.0;
    float D = m2 / (SSR_PI * d * d);
    float visL = NdotV * sqrt((NdotL - NdotL * m2) * NdotL + m2);
    float visV = NdotL * sqrt((NdotV - NdotV * m2) * NdotV + m2);
    float Vis = 0.5 / max(1e-5, visL + visV);
    return D * Vis * (SSR_PI / 4.0);
}

float EdgeFade(vec2 pos, float value)
{
    float d = min(1.0 - max(pos.x, pos.y), min(pos.x, pos.y));
    return clamp(value > 0.0 ? d / value : 1.0, 0.0, 1.0);
}

void main()
{
    Graphite_ViewData view = Graphite_PostView();
    vec3 viewNormal = SsrViewNormal(view, Graphite_Sample(_CameraNormalsTexture, vUV).xy);
    float roughness = Graphite_Sample(_MaterialTexture, vUV).r;
    float depth = max(Graphite_Sample(_CameraDepthTexture, vUV).r, 1e-7);
    vec3 viewPos = Graphite_PostViewPosition(view, vUV, depth);
    vec3 V = -SsrCameraToSurface(view, vUV, viewPos);

    vec2 bn = texture(_Noise, (vUV + _JitterSizeAndOffset.zw) * _ScreenSize / _NoiseSize).rg * 2.0 - 1.0;
    mat2 rot = mat2(bn.x, bn.y, -bn.y, bn.x);
    int numResolve = _RayReuse == 1 ? 4 : 1;
    float NdotV = clamp(dot(viewNormal, V), 0.0, 1.0);
    float coneTangent = mix(0.0, roughness * (1.0 - _BRDFBias), NdotV * sqrt(roughness));
    float maxMip = _MaxMipMap - 1.0;

    vec4 result = vec4(0.0);
    float weightSum = 0.0;
    for (int i = 0; i < numResolve; i++)
    {
        vec2 offset = rot * (resolveOffset[i] / _ResolveSize);
        vec4 rd = Graphite_Sample(_RayCast, vUV + offset);
        vec2 hitUV = rd.xy;
        float pdf = rd.z;
        float mask = rd.w;
        if (mask <= 0.0)
            continue;
        float hitDepth = max(Graphite_Sample(_CameraDepthTexture, hitUV).r, 1e-7);
        vec3 hitViewPos = Graphite_PostViewPosition(view, hitUV, hitDepth);
        float weight = 1.0;
        if (_UseNormalization == 1)
            weight = BrdfWeight(V, normalize(hitViewPos - viewPos), viewNormal, roughness) / max(1e-5, pdf);
        float coneRadius = coneTangent * length(hitUV - vUV);
        float mip = clamp(log2(max(1e-5, coneRadius) * max(_ResolveSize.x, _ResolveSize.y)), 0.0, maxMip);
        vec4 s;
        s.rgb = SamplePyramid(hitUV, mip);
        s.a = EdgeFade(hitUV, _EdgeFactor) * mask;
        if (_Fireflies == 1)
            s.rgb /= 1.0 + Luminance(s.rgb);
        result += s * weight;
        weightSum += weight;
    }
    result /= max(1e-5, weightSum);
    if (_Fireflies == 1)
        result.rgb /= 1.0 - min(Luminance(result.rgb), 0.95);
    outColor = max(vec4(1e-5), result);
}
#elif defined(SSR_TEMPORAL)
uniform GRAPHITE_SCREEN_TEXTURE _MainTex;
uniform GRAPHITE_SCREEN_TEXTURE _PreviousBuffer;
uniform GRAPHITE_SCREEN_TEXTURE _RayCast;
uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
uniform GRAPHITE_SCREEN_TEXTURE _CameraMotionVectorsTexture;
uniform int _ReflectionVelocity;
uniform float _TScale;
uniform float _TResponse;
uniform vec2 _ScreenSize;

// The history follows the reflected geometry: the hit's depth reprojected through last frame's view
vec2 ReflectionVelocity(vec2 uv)
{
    Graphite_ViewData view = Graphite_PostView();
    vec4 rd = Graphite_Sample(_RayCast, uv);
    float hitDepth = max(Graphite_Sample(_CameraDepthTexture, rd.w > 0.0 ? rd.xy : uv).r, 1e-7);
    return uv - Graphite_PostPreviousUV(view, Graphite_PostRelativePosition(view, uv, hitDepth));
}

void main()
{
    vec2 velocity = _ReflectionVelocity == 1 ? ReflectionVelocity(vUV) : Graphite_Sample(_CameraMotionVectorsTexture, vUV).rg;
    vec4 current = Graphite_Sample(_MainTex, vUV);
    vec4 previous = Graphite_Sample(_PreviousBuffer, vUV - velocity);
    vec2 du = vec2(1.0 / _ScreenSize.x, 0.0);
    vec2 dv = vec2(0.0, 1.0 / _ScreenSize.y);
    vec4 mn = current, mx = current;
    for (int x = -1; x <= 1; x++)
    {
        for (int y = -1; y <= 1; y++)
        {
            vec4 c = Graphite_Sample(_MainTex, vUV + du * float(x) + dv * float(y));
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
#elif defined(SSR_REPROJECT)
uniform GRAPHITE_SCREEN_TEXTURE _MainTex;
uniform GRAPHITE_SCREEN_TEXTURE _CameraMotionVectorsTexture;

void main() { outColor = Graphite_Sample(_MainTex, vUV - Graphite_Sample(_CameraMotionVectorsTexture, vUV).rg); }
#elif defined(SSR_COMBINE)
#include "Graphite/PBR.glsl"

uniform GRAPHITE_SCREEN_TEXTURE _ReflectionBuffer;
uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
uniform GRAPHITE_SCREEN_TEXTURE _CameraNormalsTexture;
uniform GRAPHITE_SCREEN_TEXTURE _MaterialTexture;
uniform int _UseFresnel;

void main()
{
    Graphite_ViewData view = Graphite_PostView();
    vec3 sceneColor = Graphite_SampleSource(vUV).rgb;
    vec4 reflection = Graphite_Sample(_ReflectionBuffer, vUV);
    vec2 material = Graphite_Sample(_MaterialTexture, vUV).rg;
    vec3 viewNormal = SsrViewNormal(view, Graphite_Sample(_CameraNormalsTexture, vUV).xy);
    float depth = max(Graphite_Sample(_CameraDepthTexture, vUV).r, 1e-7);
    vec3 V = -SsrCameraToSurface(view, vUV, Graphite_PostViewPosition(view, vUV, depth));
    float NdotV = clamp(dot(viewNormal, V), 0.0, 1.0);

    // Forward rendering has no albedo target, so it is approximated from the tonemapped scene colour
    vec3 approxAlbedo = sceneColor / (1.0 + sceneColor);
    vec3 F0 = mix(vec3(0.04), approxAlbedo, material.g);
    float mask = reflection.a * reflection.a;
    vec3 refl = reflection.rgb;
    if (_UseFresnel == 1)
        refl *= EnvBRDF(F0, material.r, NdotV);

    // Bounds runaway HDR and the one bounce feedback diverging without Fresnel
    refl = min(refl, vec3(8.0));
    outColor = vec4(sceneColor + refl * mask, 1.0);
}
#else
uniform GRAPHITE_SCREEN_TEXTURE _MainTex;

void main() { outColor = Graphite_Sample(_MainTex, vUV); }
#endif
