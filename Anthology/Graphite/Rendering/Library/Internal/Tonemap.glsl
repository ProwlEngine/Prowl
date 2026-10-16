#pragma variant TONEMAP CLAMP MELON ACES ACES_SIMPLE AGX REINHARD_SIMPLE REINHARD_LUMA REINHARD_WHITE ROMBINDAHOUSE UNCHARTED2
// Prowl's tonemapper: one operator by define, clamped to 0 to 1, encoded to display gamma, then contrast and saturation
// in display space. Without an operator define it only clamps.

uniform float _Contrast;
uniform float _Saturation;

#ifdef TONEMAP_MELON
// Shifts red to yellow, green to yellow, blue to cyan
vec3 HueShift(vec3 c)
{
    float a = max(c.x, c.y);
    return vec3(a, max(a, c.z), c.z);
}

vec3 Tonemap(vec3 color)
{
    color = pow(color, vec3(1.56));
    color = color / (color + 0.84);
    float factor = max(max(color.r, color.g), color.b) * 0.15;
    factor = factor / (factor + 1.0);
    factor *= factor;
    color = mix(color, HueShift(color), factor);
    return mix(color, vec3(1.0), factor);
}
#elif defined(TONEMAP_ACES)
// ACES fitted, row major as written
const mat3 ACESInputMat = mat3(
    0.59719, 0.35458, 0.04823,
    0.07600, 0.90834, 0.01566,
    0.02840, 0.13383, 0.83777);

const mat3 ACESOutputMat = mat3(
     1.60475, -0.53108, -0.07367,
    -0.10208,  1.10813, -0.00605,
    -0.00327, -0.07276,  1.07602);

vec3 RRTAndODTFit(vec3 v)
{
    vec3 a = v * (v + 0.0245786) - 0.000090537;
    vec3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
    return a / b;
}

vec3 Tonemap(vec3 color)
{
    color = color * ACESInputMat;
    color = RRTAndODTFit(color);
    return color * ACESOutputMat;
}
#elif defined(TONEMAP_ACES_SIMPLE)
vec3 Tonemap(vec3 c)
{
    return (c * (2.51 * c + 0.03)) / (c * (2.43 * c + 0.59) + 0.14);
}
#elif defined(TONEMAP_AGX)
const mat3 agxTransform = mat3(
    0.842479062253094, 0.0423282422610123, 0.0423756549057051,
    0.0784335999999992, 0.878468636469772, 0.0784336,
    0.0792237451477643, 0.0791661274605434, 0.879142973793104);

const mat3 agxTransformInverse = mat3(
    1.19687900512017, -0.0528968517574562, -0.0529716355144438,
    -0.0980208811401368, 1.15190312990417, -0.0980434501171241,
    -0.0990297440797205, -0.0989611768448433, 1.15107367264116);

vec3 agxDefaultContrastApproximation(vec3 x)
{
    vec3 x2 = x * x;
    vec3 x4 = x2 * x2;
    return 15.5 * x4 * x2 - 40.14 * x4 * x + 31.96 * x4 - 6.868 * x2 * x + 0.4298 * x2 + 0.1191 * x - 0.00232;
}

vec3 Tonemap(vec3 color)
{
    const float minEv = -12.47393;
    const float maxEv = 4.026069;
    color = agxTransform * color;
    color = clamp(log2(max(color, vec3(1e-10))), minEv, maxEv);
    color = (color - minEv) / (maxEv - minEv);
    color = agxDefaultContrastApproximation(color);

    // Punchy look
    float luma = dot(color, vec3(0.2126729, 0.7151522, 0.0721750));
    color = pow(max(color * vec3(1.1), vec3(0.0)), vec3(1.2));
    color = max(luma + 1.3 * (color - luma), vec3(0.0));

    color = agxTransformInverse * color;
    return pow(max(vec3(0.0), color), vec3(2.2));
}
#elif defined(TONEMAP_REINHARD_SIMPLE)
vec3 Tonemap(vec3 color)
{
    float exposure = 1.5;
    return color * (exposure / (1.0 + color / exposure));
}
#elif defined(TONEMAP_REINHARD_LUMA)
vec3 Tonemap(vec3 color)
{
    float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
    return color * ((luma / (1.0 + luma)) / max(luma, 1e-5));
}
#elif defined(TONEMAP_REINHARD_WHITE)
vec3 Tonemap(vec3 color)
{
    float white = 2.0;
    float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
    float mapped = luma * (1.0 + luma / (white * white)) / (1.0 + luma);
    return color * (mapped / max(luma, 1e-5));
}
#elif defined(TONEMAP_ROMBINDAHOUSE)
vec3 Tonemap(vec3 color)
{
    return exp(-1.0 / (2.72 * color + 0.15));
}
#elif defined(TONEMAP_UNCHARTED2)
vec3 Uncharted2Curve(vec3 x)
{
    const float A = 0.15, B = 0.50, C = 0.10, D = 0.20, E = 0.02, F = 0.30;
    return ((x * (A * x + C * B) + D * E) / (x * (A * x + B) + D * F)) - E / F;
}

vec3 Tonemap(vec3 color)
{
    return Uncharted2Curve(color * 2.0) / Uncharted2Curve(vec3(11.2));
}
#else
vec3 Tonemap(vec3 color) { return color; }
#endif

vec3 LinearToGamma(vec3 lin)
{
    return max(1.055 * pow(max(lin, vec3(0.0)), vec3(0.416666667)) - 0.055, 0.0);
}

void main()
{
    vec4 source = Graphite_SampleSource(vUV);
    vec3 color = LinearToGamma(clamp(Tonemap(source.rgb), 0.0, 1.0));

    float t = (1.0 - _Contrast) / 2.0;
    mat4 contrast = mat4(_Contrast, 0, 0, 0, 0, _Contrast, 0, 0, 0, 0, _Contrast, 0, t, t, t, 1);
    vec3 luminance = vec3(0.3086, 0.6094, 0.0820);
    float oneMinusSat = 1.0 - _Saturation;
    mat4 saturation = mat4(
        vec4(vec3(luminance.x * oneMinusSat) + vec3(_Saturation, 0, 0), 0),
        vec4(vec3(luminance.y * oneMinusSat) + vec3(0, _Saturation, 0), 0),
        vec4(vec3(luminance.z * oneMinusSat) + vec3(0, 0, _Saturation), 0),
        vec4(0, 0, 0, 1));
    outColor = vec4((contrast * saturation * vec4(color, source.a)).rgb, source.a);
}
