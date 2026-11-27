#version 460 core

// #include "ProwlCG" -> #include "ShaderVariables" resolved: of everything those files
// provide, only _Time (for FILM_GRAIN) is actually referenced, so only the global
// per-frame UBO is inlined below. Per-object data is skipped entirely (this pass is
// fullscreen post-process, no per-object transform is needed).
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

layout(location = 0) out vec4 OutputColor;

layout(location = 1) in vec2 TexCoords;

layout(std140, set = 2, binding = 0) uniform PassUniforms
{
    vec2 _Resolution;

    // Vignette
    float _VignetteIntensity;
    float _VignetteSmoothness;
    float _VignetteRoundness;

    // Chromatic Aberration
    float _ChromaticIntensity;
    float _ChromaticDistortion;

    // Film Grain
    float _GrainIntensity;
    float _GrainResponse;

    // Color Grading
    float _PostExposure;
    float _Contrast;
    float _Saturation;
    float _Temperature;
    vec4 _Lift;
    vec4 _Gamma;
    vec4 _Gain;

    // LUT
    float _LUTContribution;
    float _LUTSize;

    // Sharpen (CAS)
    float _SharpenAmount;
    float _SharpenRadius;

    // Edge Detection
    float _EdgeIntensity;
    vec4 _EdgeColor;
    float _EdgeBackgroundFade;

    // Pixelation
    float _PixelSize;

    // God Rays
    float _GodRayIntensity;
    float _GodRayDecay;
    float _GodRayDensity;
    float _GodRayWeight;
    int _GodRaySamples;
    float _GodRayThreshold;
    vec2 _SunScreenPos;
};

layout(set = 2, binding = 1) uniform sampler2D _MainTex;
layout(set = 2, binding = 2) uniform sampler2D _LUTTex;
layout(set = 2, binding = 3) uniform sampler2D _CameraDepthTexture;

// ── Utility functions ─────────────────────────────

float getLuminance(vec3 c) {
    return dot(c, vec3(0.2126, 0.7152, 0.0722));
}

// Hash-based noise
float hash12(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

// ── Effect implementations ────────────────────────

vec2 applyPixelation(vec2 uv) {
    vec2 pixelCount = _Resolution / _PixelSize;
    return floor(uv * pixelCount) / pixelCount;
}

vec3 applyChromaticAberration(vec2 uv) {
    // Barrel distortion-based chromatic aberration
    vec2 center = uv - 0.5;
    float dist2 = dot(center, center);

    // Apply barrel distortion per channel with different magnitudes
    float intensity = _ChromaticIntensity / _Resolution.x;
    float distortion = _ChromaticDistortion;

    // Red shifts outward, blue shifts inward
    vec2 offsetR = center * (1.0 + dist2 * distortion) * intensity;
    vec2 offsetB = center * (1.0 - dist2 * distortion) * intensity;

    float r = texture(_MainTex, uv + offsetR).r;
    float g = texture(_MainTex, uv).g;
    float b = texture(_MainTex, uv - offsetB).b;
    return vec3(r, g, b);
}

// Contrast Adaptive Sharpening (CAS)
// Reference: Lou Kramer, FidelityFX CAS, AMD Developer Day 2019
vec3 applyContrastAdaptiveSharpening(ivec2 texcoord) {
    int r = int(_SharpenRadius);
    vec3 a = texelFetch(_MainTex, texcoord + ivec2( 0, -1) * r, 0).rgb;
    vec3 b = texelFetch(_MainTex, texcoord + ivec2(-1,  0) * r, 0).rgb;
    vec3 c = texelFetch(_MainTex, texcoord, 0).rgb;
    vec3 d = texelFetch(_MainTex, texcoord + ivec2( 1,  0) * r, 0).rgb;
    vec3 e = texelFetch(_MainTex, texcoord + ivec2( 0,  1) * r, 0).rgb;

    float mn = min(a.g, min(b.g, min(c.g, min(d.g, e.g))));
    float mx = max(a.g, max(b.g, max(c.g, max(d.g, e.g))));

    float w = sqrt(min(1.0 - mx, mn) / max(mx, 0.001)) * mix(-0.125, -0.2, _SharpenAmount);
    return (w * (a + b + d + e) + c) / (4.0 * w + 1.0);
}

float sobelEdge(vec2 uv) {
    vec2 texel = 1.0 / _Resolution;

    float tl = getLuminance(texture(_MainTex, uv + vec2(-texel.x,  texel.y)).rgb);
    float t  = getLuminance(texture(_MainTex, uv + vec2(     0.0,  texel.y)).rgb);
    float tr = getLuminance(texture(_MainTex, uv + vec2( texel.x,  texel.y)).rgb);
    float l  = getLuminance(texture(_MainTex, uv + vec2(-texel.x,      0.0)).rgb);
    float r  = getLuminance(texture(_MainTex, uv + vec2( texel.x,      0.0)).rgb);
    float bl = getLuminance(texture(_MainTex, uv + vec2(-texel.x, -texel.y)).rgb);
    float b  = getLuminance(texture(_MainTex, uv + vec2(     0.0, -texel.y)).rgb);
    float br = getLuminance(texture(_MainTex, uv + vec2( texel.x, -texel.y)).rgb);

    float gx = -tl - 2.0*l - bl + tr + 2.0*r + br;
    float gy = -tl - 2.0*t - tr + bl + 2.0*b + br;

    return sqrt(gx*gx + gy*gy);
}

vec3 applyColorGrading(vec3 color) {
    // Post-exposure (EV stops)
    color *= exp2(_PostExposure);

    // Contrast (around mid-gray 0.18)
    color = mix(vec3(0.18), color, 1.0 + _Contrast);

    // Temperature (approximate Planckian locus shift)
    color.r *= 1.0 + _Temperature * 0.1;
    color.b *= 1.0 - _Temperature * 0.1;

    // Saturation
    float lum = getLuminance(color);
    color = mix(vec3(lum), color, 1.0 + _Saturation);

    // Lift / Gamma / Gain (ASC CDL style)
    // Lift affects shadows, Gain affects highlights, Gamma adjusts midtones
    vec3 lift = _Lift.rgb;
    vec3 gamma = _Gamma.rgb;
    vec3 gain = _Gain.rgb;

    // Apply gain (multiply)
    color *= (vec3(1.0) + gain);

    // Apply lift (add to shadows weighted by inverse luminance)
    color += lift * (1.0 - color);

    // Apply gamma (power curve on midtones)
    // gamma offset of 0 = no change, positive = brighten mids, negative = darken mids
    vec3 gammaExp = 1.0 / max(vec3(1.0) + gamma, vec3(0.01));
    color = pow(max(color, vec3(0.0)), gammaExp);

    return max(color, vec3(0.0));
}

// Sample a strip-format LUT texture (e.g., 256x16 for 16^3 LUT)
vec3 applyLUT(vec3 color) {
    float size = _LUTSize;
    float invSize = 1.0 / size;
    float sliceSize = 1.0 / size; // width of one slice in UV space

    // Clamp input to valid range
    color = clamp(color, 0.0, 1.0);

    // Blue channel selects the slice pair
    float blueScaled = color.b * (size - 1.0);
    float sliceLow = floor(blueScaled);
    float sliceHigh = min(sliceLow + 1.0, size - 1.0);
    float sliceFrac = blueScaled - sliceLow;

    // Half-texel offset for proper sampling
    float halfTexel = 0.5 * invSize;

    // UV within a single slice (red = x, green = y)
    vec2 innerUV = vec2(
        halfTexel + color.r * (1.0 - invSize),
        halfTexel + color.g * (1.0 - invSize)
    );

    // Offset to the correct slice
    vec2 uv1 = vec2(innerUV.x * sliceSize + sliceLow * sliceSize, innerUV.y);
    vec2 uv2 = vec2(innerUV.x * sliceSize + sliceHigh * sliceSize, innerUV.y);

    vec3 graded1 = texture(_LUTTex, uv1).rgb;
    vec3 graded2 = texture(_LUTTex, uv2).rgb;

    vec3 graded = mix(graded1, graded2, sliceFrac);
    return mix(color, graded, _LUTContribution);
}

vec3 applyFilmGrain(vec3 color, vec2 uv) {
    float noise = hash12(uv * _Resolution + fract(_Time.y) * 1000.0) * 2.0 - 1.0;
    float lum = getLuminance(color);
    float response = mix(1.0, lum, _GrainResponse);
    color += noise * _GrainIntensity * response;
    return max(color, vec3(0.0));
}

vec3 applyVignette(vec3 color, vec2 uv) {
    vec2 d = abs(uv - 0.5) * 2.0;
    d = mix(d, vec2(length(d)), _VignetteRoundness);
    float vfactor = pow(clamp(1.0 - dot(d, d) * _VignetteIntensity, 0.0, 1.0), _VignetteSmoothness + 0.01);
    return color * vfactor;
}

vec3 applyGodRays(vec3 color, vec2 uv) {
    vec2 deltaUV = (uv - _SunScreenPos) * _GodRayDensity / float(_GodRaySamples);
    vec2 sampleUV = uv;
    float illumination = 0.0;
    float decay = 1.0;

    int sampleCount = min(_GodRaySamples, 128);
    for (int i = 0; i < sampleCount; i++) {
        sampleUV -= deltaUV;
        vec2 clampedUV = clamp(sampleUV, 0.001, 0.999);

        // Depth test sky pixels (depth ~1.0) contribute light
        float depth = texture(_CameraDepthTexture, clampedUV).r;
        float sky = step(0.9999, depth);

        // Luminance threshold only bright sky areas contribute
        vec3 sampleColor = texture(_MainTex, clampedUV).rgb;
        float brightness = getLuminance(sampleColor);
        float bright = sky * step(_GodRayThreshold, brightness);

        illumination += bright * decay * _GodRayWeight;
        decay *= _GodRayDecay;
    }

    // Warm-tinted light shafts
    return color + illumination * _GodRayIntensity * vec3(1.0, 0.95, 0.85);
}

void main()
{
    vec2 uv = TexCoords;

    // ── Pixelation (modify UV first) ──────────────
    uv = applyPixelation(uv);

    // ── Sample color ──────────────────────────────
    vec3 color = applyChromaticAberration(uv);

    float alpha = texture(_MainTex, TexCoords).a;

    // ── Sharpen (CAS) ─────────────────────────────
    color = applyContrastAdaptiveSharpening(ivec2(uv * _Resolution));

    // ── Edge Detection ────────────────────────────
    float edge = sobelEdge(uv) * _EdgeIntensity;
    edge = clamp(edge, 0.0, 1.0);
    color = mix(color * _EdgeBackgroundFade, _EdgeColor.rgb, edge);

    // ── Color Grading ─────────────────────────────
    color = applyColorGrading(color);

    // ── LUT ───────────────────────────────────────
    color = applyLUT(color);

    // ── God Rays ──────────────────────────────────
    color = applyGodRays(color, TexCoords);

    // ── Film Grain ────────────────────────────────
    color = applyFilmGrain(color, uv);

    // ── Vignette (last applies to final image) ──
    color = applyVignette(color, uv);

    OutputColor = vec4(color, alpha);
}
