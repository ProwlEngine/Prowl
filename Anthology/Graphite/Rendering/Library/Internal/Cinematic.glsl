#pragma variant CINEMATIC VIGNETTE PIXELATION CHROMATIC_ABERRATION SHARPEN EDGE_DETECTION COLOR_GRADING LUT FILM_GRAIN
// Prowl's cinematic effects on display referred colour, one per define, each its own pass: pixelation, chromatic
// aberration, CAS sharpening, Sobel edges, colour grading, a strip LUT, film grain and vignette.

uniform vec2 _Resolution;

float Luminance(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

#if defined(CINEMATIC_PIXELATION)
uniform float _PixelSize;

void main()
{
    vec2 pixelCount = _Resolution / _PixelSize;
    vec2 uv = floor(vUV * pixelCount) / pixelCount;
    outColor = vec4(Graphite_SampleSource(uv).rgb, Graphite_SampleSource(vUV).a);
}
#elif defined(CINEMATIC_CHROMATIC_ABERRATION)
uniform float _ChromaticIntensity;
uniform float _ChromaticDistortion;

// Barrel distortion per channel, red outward and blue inward
void main()
{
    vec2 center = vUV - 0.5;
    float dist2 = dot(center, center);
    float intensity = _ChromaticIntensity / _Resolution.x;
    vec2 offsetR = center * (1.0 + dist2 * _ChromaticDistortion) * intensity;
    vec2 offsetB = center * (1.0 - dist2 * _ChromaticDistortion) * intensity;
    vec4 source = Graphite_SampleSource(vUV);
    outColor = vec4(Graphite_SampleSource(vUV + offsetR).r, source.g, Graphite_SampleSource(vUV - offsetB).b, source.a);
}
#elif defined(CINEMATIC_SHARPEN)
uniform float _SharpenAmount;
uniform float _SharpenRadius;

vec3 Texel(ivec2 texel) { return Graphite_Fetch(Graphite_Source, clamp(texel, ivec2(0), ivec2(_Resolution) - 1), 0).rgb; }

// Contrast adaptive sharpening
void main()
{
    ivec2 texcoord = ivec2(vUV * _Resolution);
    int r = int(_SharpenRadius);
    vec3 a = Texel(texcoord + ivec2(0, -1) * r);
    vec3 b = Texel(texcoord + ivec2(-1, 0) * r);
    vec3 c = Texel(texcoord);
    vec3 d = Texel(texcoord + ivec2(1, 0) * r);
    vec3 e = Texel(texcoord + ivec2(0, 1) * r);
    float mn = min(a.g, min(b.g, min(c.g, min(d.g, e.g))));
    float mx = max(a.g, max(b.g, max(c.g, max(d.g, e.g))));
    float w = sqrt(min(1.0 - mx, mn) / max(mx, 0.001)) * mix(-0.125, -0.2, _SharpenAmount);
    outColor = vec4((w * (a + b + d + e) + c) / (4.0 * w + 1.0), Graphite_SampleSource(vUV).a);
}
#elif defined(CINEMATIC_EDGE_DETECTION)
uniform float _EdgeIntensity;
uniform vec4 _EdgeColor;
uniform float _EdgeBackgroundFade;

float LumaAt(vec2 uv) { return Luminance(Graphite_SampleSource(uv).rgb); }

// Sobel edges drawn in the edge colour over the faded image
void main()
{
    vec2 texel = 1.0 / _Resolution;
    float tl = LumaAt(vUV + vec2(-texel.x, texel.y));
    float t = LumaAt(vUV + vec2(0.0, texel.y));
    float tr = LumaAt(vUV + vec2(texel.x, texel.y));
    float l = LumaAt(vUV + vec2(-texel.x, 0.0));
    float r = LumaAt(vUV + vec2(texel.x, 0.0));
    float bl = LumaAt(vUV + vec2(-texel.x, -texel.y));
    float b = LumaAt(vUV + vec2(0.0, -texel.y));
    float br = LumaAt(vUV + vec2(texel.x, -texel.y));
    float gx = -tl - 2.0 * l - bl + tr + 2.0 * r + br;
    float gy = -tl - 2.0 * t - tr + bl + 2.0 * b + br;
    float edge = clamp(sqrt(gx * gx + gy * gy) * _EdgeIntensity, 0.0, 1.0);
    vec4 source = Graphite_SampleSource(vUV);
    outColor = vec4(mix(source.rgb * _EdgeBackgroundFade, _EdgeColor.rgb, edge), source.a);
}
#elif defined(CINEMATIC_COLOR_GRADING)
uniform float _PostExposure;
uniform float _Contrast;
uniform float _Saturation;
uniform float _Temperature;
uniform vec4 _Lift;
uniform vec4 _Gamma;
uniform vec4 _Gain;

void main()
{
    vec4 source = Graphite_SampleSource(vUV);
    vec3 color = source.rgb * exp2(_PostExposure);
    color = mix(vec3(0.18), color, 1.0 + _Contrast);
    color.r *= 1.0 + _Temperature * 0.1;
    color.b *= 1.0 - _Temperature * 0.1;
    color = mix(vec3(Luminance(color)), color, 1.0 + _Saturation);

    // Lift, gamma and gain, ASC CDL style
    color *= vec3(1.0) + _Gain.rgb;
    color += _Lift.rgb * (1.0 - color);
    color = pow(max(color, vec3(0.0)), 1.0 / max(vec3(1.0) + _Gamma.rgb, vec3(0.01)));
    outColor = vec4(max(color, vec3(0.0)), source.a);
}
#elif defined(CINEMATIC_LUT)
uniform sampler2D _LUTTex;
uniform float _LUTContribution;
uniform float _LUTSize;

// A strip LUT, its slices side by side, blue picking the slice
void main()
{
    vec4 source = Graphite_SampleSource(vUV);
    float size = _LUTSize;
    float invSize = 1.0 / size;
    vec3 color = clamp(source.rgb, 0.0, 1.0);
    float blueScaled = color.b * (size - 1.0);
    float sliceLow = floor(blueScaled);
    float sliceHigh = min(sliceLow + 1.0, size - 1.0);
    float sliceFrac = blueScaled - sliceLow;
    float halfTexel = 0.5 * invSize;
    vec2 innerUV = vec2(halfTexel + color.r * (1.0 - invSize), halfTexel + color.g * (1.0 - invSize));
    vec3 graded1 = texture(_LUTTex, vec2(innerUV.x * invSize + sliceLow * invSize, innerUV.y)).rgb;
    vec3 graded2 = texture(_LUTTex, vec2(innerUV.x * invSize + sliceHigh * invSize, innerUV.y)).rgb;
    outColor = vec4(mix(color, mix(graded1, graded2, sliceFrac), _LUTContribution), source.a);
}
#elif defined(CINEMATIC_FILM_GRAIN)
uniform float _Time;
uniform float _GrainIntensity;
uniform float _GrainResponse;

float Hash12(vec2 p)
{
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

void main()
{
    vec4 source = Graphite_SampleSource(vUV);
    float noise = Hash12(vUV * _Resolution + fract(_Time) * 1000.0) * 2.0 - 1.0;
    vec3 color = source.rgb + noise * _GrainIntensity * mix(1.0, Luminance(source.rgb), _GrainResponse);
    outColor = vec4(max(color, vec3(0.0)), source.a);
}
#else
uniform float _VignetteIntensity;
uniform float _VignetteSmoothness;
uniform float _VignetteRoundness;

void main()
{
    vec4 source = Graphite_SampleSource(vUV);
    vec2 d = abs(vUV - 0.5) * 2.0;
    d = mix(d, vec2(length(d)), _VignetteRoundness);
    outColor = vec4(source.rgb * pow(clamp(1.0 - dot(d, d) * _VignetteIntensity, 0.0, 1.0), _VignetteSmoothness + 0.01), source.a);
}
#endif
