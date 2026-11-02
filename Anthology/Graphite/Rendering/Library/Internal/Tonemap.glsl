// Tonemapper: the operator _TonemapOperator picks, clamped to 0 to 1, then sRGB encoded. An unknown operator only
// clamps.
#include "Graphite/Math.glsl"

// The operators in TonemapOperator's order
#define TONEMAP_ACES 0
#define TONEMAP_AGX_NEUTRAL 1
#define TONEMAP_AGX_PUNCHY 2
#define TONEMAP_REINHARD 3
#define TONEMAP_ROMBINDAHOUSE 4
#define TONEMAP_UNCHARTED2 5

uniform int _TonemapOperator;

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

vec3 Aces(vec3 color)
{
    color = color * ACESInputMat;
    color = RRTAndODTFit(color);
    return color * ACESOutputMat;
}

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

// AgX's curve already produces display encoded values
vec3 AgX(vec3 color, bool punchy)
{
    const float minEv = -12.47393;
    const float maxEv = 4.026069;
    color = agxTransform * color;
    color = clamp(log2(max(color, vec3(1e-10))), minEv, maxEv);
    color = (color - minEv) / (maxEv - minEv);
    color = agxDefaultContrastApproximation(color);

    if (punchy)
    {
        // The reference Punchy look, an ASC CDL power and saturation
        float luma = Graphite_Luminance(color);
        color = pow(max(color, vec3(0.0)), vec3(1.35));
        color = luma + 1.4 * (color - luma);
    }

    return agxTransformInverse * color;
}

// Extended Reinhard on luminance, white at 2
vec3 Reinhard(vec3 color)
{
    float white = 2.0;
    float luma = Graphite_Luminance(color);
    float mapped = luma * (1.0 + luma / (white * white)) / (1.0 + luma);
    return color * (mapped / max(luma, 1e-5));
}

vec3 RomBinDaHouse(vec3 color)
{
    return exp(-1.0 / (2.72 * color + 0.15));
}

vec3 Uncharted2Curve(vec3 x)
{
    const float A = 0.15, B = 0.50, C = 0.10, D = 0.20, E = 0.02, F = 0.30;
    return ((x * (A * x + C * B) + D * E) / (x * (A * x + B) + D * F)) - E / F;
}

vec3 Uncharted2(vec3 color)
{
    return Uncharted2Curve(color * 2.0) / Uncharted2Curve(vec3(11.2));
}

vec3 Tonemap(vec3 color)
{
    switch (_TonemapOperator)
    {
        case TONEMAP_ACES: return Aces(color);
        case TONEMAP_AGX_NEUTRAL: return AgX(color, false);
        case TONEMAP_AGX_PUNCHY: return AgX(color, true);
        case TONEMAP_REINHARD: return Reinhard(color);
        case TONEMAP_ROMBINDAHOUSE: return RomBinDaHouse(color);
        case TONEMAP_UNCHARTED2: return Uncharted2(color);
        default: return color;
    }
}

vec3 LinearToSrgb(vec3 lin)
{
    lin = max(lin, vec3(0.0));
    return mix(lin * 12.92, 1.055 * pow(lin, vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), lin));
}

void main()
{
    vec4 source = Graphite_SampleSource(vUV);
    vec3 color = clamp(Tonemap(source.rgb), 0.0, 1.0);
    if (_TonemapOperator != TONEMAP_AGX_NEUTRAL && _TonemapOperator != TONEMAP_AGX_PUNCHY)
        color = LinearToSrgb(color);
    outColor = vec4(color, source.a);
}
