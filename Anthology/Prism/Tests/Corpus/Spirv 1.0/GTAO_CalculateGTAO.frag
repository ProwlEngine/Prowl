#version 460 core

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

#define PROWL_PI      3.14159265359
#define PROWL_HALF_PI 1.57079632679

#define saturate(x) clamp(x, 0.0, 1.0)
#define rcp(x) (1.0 / (x))

layout(std140, set = 2, binding = 0) uniform PassUniforms
{
    int _Slices;
    int _DirectionSamples;
    float _Radius;
    float _Intensity;
    vec2 _NoiseScale;   // ao-res / noise-res, tiles the blue noise 1:1 per pixel
    vec2 _JitterOffset; // per-frame Halton offset so temporal accumulation converges
};

layout(set = 2, binding = 1) uniform sampler2D _CameraDepthTexture;
layout(set = 2, binding = 2) uniform sampler2D _CameraNormalsTexture; // View-space normals from depth pre-pass
layout(set = 2, binding = 3) uniform sampler2D _Noise;

layout(location = 1) in vec2 TexCoords;

layout(location = 0) out vec4 aoOutput;

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

// ---- ProwlCG: math utilities ----
float sdot(vec3 x) { return dot(x, x); }
vec2 sqr(vec2 x) { return x * x; }

float linearstep(float a, float b, float x) {
    return saturate((x - a) / (b - a));
}

float fastSign(float x) {
    return x >= 0.0 ? 1.0 : -1.0;
}

// Fast acos approximation using polynomial
float fastAcos(float x) {
    float y = abs(x);
    float p = -0.0187293 * y + 0.0742610;
    p = p * y - 0.2121144;
    p = p * y + 1.5707288;
    p = p * sqrt(1.0 - y);
    return x >= 0.0 ? p : PROWL_PI - p;
}

// Extract diagonal from matrix
vec2 diagonal2(mat4 m) { return vec2(m[0].x, m[1].y); }

void SampleHorizonCos(vec2 coord, vec2 offset, vec3 viewPos, vec3 viewDir, vec2 falloff, inout float cHorizonCos) {
    vec2 sTexCoord = coord + offset;

    // Check bounds
    if (sTexCoord.x < 0.0 || sTexCoord.x > 1.0 || sTexCoord.y < 0.0 || sTexCoord.y > 1.0)
        return;

    float sDepth = texture(_CameraDepthTexture, sTexCoord).r;
    if (sDepth >= 1.0) return;

    vec3 sHorizonV = getViewPos(sTexCoord, sDepth) - viewPos;

    float sLenV = sdot(sHorizonV);
    // Reject samples that reconstruct to essentially the same view position as the center.
    // On large flat / distant surfaces, depth-buffer precision makes neighbours collapse
    // together; the resulting near-zero horizon vector is pure quantization noise, not real
    // occlusion. Scale the threshold with view depth since depth precision worsens with distance.
    float minLen = 0.0015 * abs(viewPos.z);
    if (sLenV < minLen * minLen) return;
    float sNormV = inversesqrt(sLenV);

    float sHorizonCos = dot(sHorizonV, viewDir) * sNormV;
    sHorizonCos = mix(sHorizonCos, cHorizonCos, linearstep(falloff.x, falloff.y, sLenV));
    cHorizonCos = max(sHorizonCos, cHorizonCos);
}

float CalculateGTAO(vec2 coord, vec3 viewPos, vec3 normal, vec2 dither) {
    float viewDistance = sdot(viewPos);
    float norm = inversesqrt(viewDistance);
    viewDistance *= norm;

    vec3 viewDir = viewPos * -norm;

    int sliceCount = _Slices;
    float rSliceCount = 1.0 / float(sliceCount);

    int sampleCount = _DirectionSamples;
    float rSampleCount = 1.0 / float(sampleCount);

    float radius = _Radius * saturate(0.25 + viewDistance * rcp(64.0));
    vec2 sRadius = rSampleCount * radius * norm * diagonal2(PROWL_MATRIX_P);
    // Floor the per-sample step so consecutive samples are at least ~1.5 texels apart in the
    // (full-res) depth buffer. Without this, distant/flat surfaces sample sub-texel and the
    // horizon estimate degenerates into depth-precision noise.
    sRadius = max(sRadius, vec2(1.5) / _ScreenParams.xy);
    vec2 falloff = sqr(radius * vec2(1.0, 4.0));

    float visibility = 0.0;

    for (int slice = 0; slice < sliceCount; ++slice) {
        float slicePhi = (float(slice) + dither.x) * (PROWL_PI * rSliceCount);

        vec3 directionV = vec3(cos(slicePhi), sin(slicePhi), 0.0);
        vec3 orthoDirectionV = directionV - dot(directionV, viewDir) * viewDir;
        vec3 axisV = cross(directionV, viewDir);
        vec3 projNormalV = normal - axisV * dot(normal, axisV);

        float lenV = sdot(projNormalV);
        float normV = inversesqrt(lenV);
        lenV *= normV;

        float sgnN = fastSign(dot(orthoDirectionV, projNormalV));
        float cosN = saturate(dot(projNormalV, viewDir) * normV);
        float n = sgnN * fastAcos(cosN);

        vec2 cHorizonCos = vec2(-1.0);

        for (int samp = 0; samp < sampleCount; ++samp) {
            vec2 stepDir = directionV.xy * sRadius;
            vec2 offset = (float(samp) + dither.y) * stepDir;

            SampleHorizonCos(coord, offset, viewPos, viewDir, falloff, cHorizonCos.x);
            SampleHorizonCos(coord, -offset, viewPos, viewDir, falloff, cHorizonCos.y);
        }

        vec2 h = n + clamp(vec2(fastAcos(cHorizonCos.x), -fastAcos(cHorizonCos.y)) - n, -PROWL_HALF_PI, PROWL_HALF_PI);
        h = cosN + 2.0 * h * sin(n) - cos(2.0 * h - n);

        visibility += lenV * (h.x + h.y);
    }

    return 0.25 * rSliceCount * visibility;
}

vec3 ApproxMultiBounce(float ao, vec3 albedo) {
    vec3 a = 2.0404 * albedo - 0.3324;
    vec3 b = 4.7951 * albedo - 0.6417;
    vec3 c = 2.7552 * albedo + 0.6903;

    return max(vec3(ao), ((ao * a - b) * ao + c) * ao);
}

void main()
{
    float depth = texture(_CameraDepthTexture, TexCoords).r;

    // Sky
    if (depth >= 1.0) {
        aoOutput = vec4(1.0);
        return;
    }

    // Get view space data
    vec3 viewPos = getViewPos(TexCoords, depth);

    // Get view space normal from GBuffer
    vec4 normalData = texture(_CameraNormalsTexture, TexCoords);
    vec3 viewNormal = normalize(normalData.xyz * 2.0 - 1.0);

    // Blue-noise dither (R channel of the built-in noise texture), scrolled each frame by a
    // Halton offset so temporal accumulation converges. .r drives the slice angle, .g the step.
    vec2 noise = texture(_Noise, TexCoords * _NoiseScale + _JitterOffset).rg;

    // Calculate GTAO
    float ao = CalculateGTAO(TexCoords, viewPos, viewNormal, noise);

    // Apply intensity
    ao = pow(saturate(ao), _Intensity);

    aoOutput = vec4(ao, ao, ao, 1.0);
}
