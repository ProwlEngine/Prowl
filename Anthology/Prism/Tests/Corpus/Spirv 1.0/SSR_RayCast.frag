#version 460 core

// SSR - RayCast pass
// Per-pixel GGX importance-sampled reflection ray -> hit UV + pdf + mask.

layout(std140, set = 0, binding = 0) uniform GlobalUniforms
{
    mat4 prowl_MatV;
    mat4 prowl_MatIV;
    mat4 prowl_MatP;
    mat4 prowl_MatVP;
    mat4 prowl_PrevViewProj;
    mat4 prowl_MatIP;
    mat4 prowl_MatIVP;
    mat4 prowl_MatVP_NonJittered;
    vec3 _WorldSpaceCameraPos;
    float _padding0;
    vec4 _ProjectionParams;
    vec4 _ScreenParams;
    vec2 _CameraJitter;
    vec2 _CameraPreviousJitter;
    vec4 _Time;
    vec4 _SinTime;
    vec4 _CosTime;
    vec4 prowl_DeltaTime;
};

#define PROWL_MATRIX_P prowl_MatP
#define PROWL_MATRIX_I_P prowl_MatIP

#define PROWL_PI     3.14159265359
#define PROWL_TWO_PI 6.28318530718

layout(std140, set = 2, binding = 0) uniform PassUniforms
{
    vec4 _JitterSizeAndOffset; // xy = rayUVsize/noiseSize, zw = per-frame offset
    float _NumSteps;
    float _BRDFBias;
};

layout(set = 2, binding = 1) uniform sampler2D _CameraDepthTexture;
layout(set = 2, binding = 2) uniform sampler2D _CameraNormalsTexture;
layout(set = 2, binding = 3) uniform sampler2D _CameraMotionVectorsTexture; // .b roughness
layout(set = 2, binding = 4) uniform sampler2D _Noise;

layout(location = 1) in vec2 TexCoords;
layout(location = 0) out vec4 rayData; // xy = hit UV, z = pdf, w = mask

// ---- ProwlCG: view-space reconstruction from screen-space depth ----
vec3 projectAndDivide(mat4 matrix, vec3 pos)
{
    vec4 p = matrix * vec4(pos, 1.0);
    return p.xyz / p.w;
}

vec3 getScreenPos(vec2 tc, float depth)
{
    return vec3(tc, depth);
}

vec3 getNDCFromScreenPos(vec3 screenPos)
{
    return screenPos * 2.0 - 1.0;
}

vec3 getViewFromScreenPos(vec3 screenPos)
{
    return projectAndDivide(PROWL_MATRIX_I_P, getNDCFromScreenPos(screenPos));
}

vec3 getViewPos(vec2 tc, float depth)
{
    return getViewFromScreenPos(getScreenPos(tc, depth));
}

// GGX microfacet half-vector importance sample (Karis / UE4). Returns half-vector + pdf.
vec4 importanceSampleGGX(vec2 Xi, float roughness)
{
    float m = roughness * roughness;
    float m2 = m * m;
    float phi = PROWL_TWO_PI * Xi.x;
    float cosT = sqrt((1.0 - Xi.y) / (1.0 + (m2 - 1.0) * Xi.y));
    float sinT = sqrt(max(1e-5, 1.0 - cosT * cosT));
    vec3 h = vec3(sinT * cos(phi), sinT * sin(phi), cosT);
    float d = (cosT * m2 - cosT) * cosT + 1.0;
    float D = m2 / (PROWL_PI * d * d);
    return vec4(h, D * cosT);
}

vec3 tangentToView(vec3 n, vec3 h)
{
    vec3 up = abs(n.z) < 0.999 ? vec3(0.0, 0.0, 1.0) : vec3(1.0, 0.0, 0.0);
    vec3 t = normalize(cross(up, n));
    vec3 b = cross(n, t);
    return t * h.x + b * h.y + n * h.z;
}

vec3 projectToScreen(vec3 viewPos)
{
    vec4 clip = PROWL_MATRIX_P * vec4(viewPos, 1.0);
    clip.xyz /= clip.w;
    return vec3(clip.xy * 0.5 + 0.5, clip.z);
}

// March in screen space: step until the ray passes behind the depth buffer, then refine.
bool trace(vec3 startScreen, vec3 reflViewDir, float jitter, out vec2 hitUV)
{
    vec3 startView = getViewPos(startScreen.xy, startScreen.z);
    vec3 endScreen = projectToScreen(startView + reflViewDir * 100.0);
    vec3 delta = endScreen - startScreen;
    hitUV = startScreen.xy;
    if (length(delta.xy) < 1e-4) return false;

    float steps = min(max(abs(delta.x) * _ScreenParams.x, abs(delta.y) * _ScreenParams.y), _NumSteps);
    vec3 step = delta / max(steps, 1.0);
    vec3 cur = startScreen + step * (1.0 + jitter);

    bool crossed = false;
    for (float i = 1.0; i < steps; i += 1.0)
    {
        if (cur.x < 0.0 || cur.x > 1.0 || cur.y < 0.0 || cur.y > 1.0) return false;
        float sceneDepth = texture(_CameraDepthTexture, cur.xy).r;
        if (sceneDepth < 1.0 && cur.z > sceneDepth + 0.0001) { crossed = true; break; }
        cur += step;
    }
    if (!crossed) return false;

    vec3 lo = cur - step, hi = cur;
    for (int j = 0; j < 5; j++)
    {
        vec3 mid = (lo + hi) * 0.5;
        float d = texture(_CameraDepthTexture, mid.xy).r;
        if (mid.z > d) hi = mid; else lo = mid;
    }
    hitUV = hi.xy;
    return true;
}

void main()
{
    float depth = texture(_CameraDepthTexture, TexCoords).r;
    if (depth >= 1.0) { rayData = vec4(0.0); return; }

    vec4 nd = texture(_CameraNormalsTexture, TexCoords);
    if (length(nd.xyz) < 0.01) { rayData = vec4(0.0); return; }
    vec3 viewNormal = normalize(nd.xyz * 2.0 - 1.0);
    float roughness = texture(_CameraMotionVectorsTexture, TexCoords).b;

    vec3 viewPos = getViewPos(TexCoords, depth);
    vec3 V = normalize(viewPos); // camera -> surface

    // Blue noise (R channel) for the stochastic sample, tiled + per-frame offset.
    vec2 Xi = texture(_Noise, (TexCoords + _JitterSizeAndOffset.zw) * _JitterSizeAndOffset.xy).rg;
    Xi.y = mix(Xi.y, 0.0, _BRDFBias); // bias toward the mirror direction

    vec4 H = importanceSampleGGX(Xi, roughness);
    vec3 h = tangentToView(viewNormal, H.xyz);
    vec3 reflDir = reflect(V, h);

    float jitter = (Xi.x + Xi.y) * (1.0 / max(_NumSteps, 1.0));

    vec2 hitUV;
    bool hit = trace(vec3(TexCoords, depth), reflDir, jitter, hitUV);

    rayData = hit ? vec4(hitUV, H.w, 1.0) : vec4(0.0);
}
