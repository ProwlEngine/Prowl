module ProwlCG;

__exported import ShaderVariables;

public static const float PROWL_PI          = 3.14159265359;
public static const float PROWL_TWO_PI      = 6.28318530718;
public static const float PROWL_FOUR_PI     = 12.56637061436;
public static const float PROWL_INV_PI      = 0.31830988618;
public static const float PROWL_INV_TWO_PI  = 0.15915494309;
public static const float PROWL_INV_FOUR_PI = 0.07957747155;
public static const float PROWL_HALF_PI     = 1.57079632679;
public static const float PROWL_INV_HALF_PI = 0.636619772367;

// Colors ===========================================================
// http://chilliant.blogspot.com.au/2012/08/srgb-approximations-for-hlsl.html?m=1
public float3 linearToGammaSpace(float3 lin)
{
    return max(1.055 * pow(max(lin, float3(0.0)), float3(0.416666667)) - 0.055, 0.0);
}

// http://chilliant.blogspot.com.au/2012/08/srgb-approximations-for-hlsl.html?m=1
public float3 gammaToLinearSpace(float3 gamma)
{
    return gamma * (gamma * (gamma * 0.305306011 + 0.682171111) + 0.012522878);
}
// ============================================================================

public float linearizeDepth(float depth, float near, float far)
{
    float z = depth * 2.0 - 1.0; // Back to NDC [-1,1] range
    return (2.0 * near * far) / (far + near - z * (far - near));
}

public float linearizeDepthFromProjection(float depth) {
    return linearizeDepth(depth, Frame._ProjectionParams.y, Frame._ProjectionParams.z);
}

public float getFovFromProjectionMatrix(float4x4 proj)
{
    // proj[1][1] is M11, the Y scale
    // FOV = 2 * atan(1/M11)
    return 2.0 * atan(1.0 / proj[1][1]);
}

// ----------------------------------------------------------------------------

public float3 projectAndDivide(float4x4 matrix, float3 pos) {
    float4 p = mul(matrix, float4(pos, 1.0));
    return p.xyz / p.w;
}

public float3 getScreenPos(float2 tc, Sampler2D<float4> depthSampler) {
    return float3(tc, depthSampler.Sample(tc).x);
}
public float3 getScreenPos(float2 tc, float depth) {
    return float3(tc, depth);
}

public float3 getScreenFromViewPos(float3 viewPos) {
    float3 p = projectAndDivide(Frame.prowl_MatP, viewPos);
    return p * 0.5 + 0.5;
}

public float3 getNDCFromScreenPos(float3 screenPos) {
    return screenPos * 2.0 - 1.0;
}

public float3 getViewFromScreenPos(float3 screenPos) {
    return projectAndDivide(Frame.prowl_MatIP, getNDCFromScreenPos(screenPos));
}

public float3 getViewPos(float2 tc, Sampler2D<float4> depthSampler) {
    return getViewFromScreenPos(getScreenPos(tc, depthSampler));
}
public float3 getViewPos(float2 tc, float depth) {
    return getViewFromScreenPos(getScreenPos(tc, depth));
}

// ----------------------------------------------------------------------------
// Math Utilities

public float max0(float x) { return max(x, 0.0); }
public float2 max0(float2 x) { return max(x, 0.0); }
public float3 max0(float3 x) { return max(x, 0.0); }
public float4 max0(float4 x) { return max(x, 0.0); }

// Min/Max of vector components
public float minOf(float2 v) { return min(v.x, v.y); }
public float minOf(float3 v) { return min(v.x, min(v.y, v.z)); }
public float minOf(float4 v) { return min(v.x, min(v.y, min(v.z, v.w))); }
public float maxOf(float2 v) { return max(v.x, v.y); }
public float maxOf(float3 v) { return max(v.x, max(v.y, v.z)); }
public float maxOf(float4 v) { return max(v.x, max(v.y, max(v.z, v.w))); }

// Squared dot product
public float sdot(float2 x) { return dot(x, x); }
public float sdot(float3 x) { return dot(x, x); }
public float sdot(float4 x) { return dot(x, x); }

// Square function
public float sqr(float x) { return x * x; }
public float2 sqr(float2 x) { return x * x; }
public float3 sqr(float3 x) { return x * x; }
public float4 sqr(float4 x) { return x * x; }

// Linear step with saturation
public float linearstep(float a, float b, float x) {
    return saturate((x - a) / (b - a));
}

// Fast approximations for trigonometric functions
public float fastSign(float x) {
    return x >= 0.0 ? 1.0 : -1.0;
}

// Fast acos approximation using polynomial
public float fastAcos(float x) {
    float y = abs(x);
    float p = -0.0187293 * y + 0.0742610;
    p = p * y - 0.2121144;
    p = p * y + 1.5707288;
    p = p * sqrt(1.0 - y);
    return x >= 0.0 ? p : PROWL_PI - p;
}

// Extract diagonal from matrix
public float2 diagonal2(float4x4 m) { return float2(m[0][0], m[1][1]); }
public float3 diagonal3(float4x4 m) { return float3(m[0][0], m[1][1], m[2][2]); }

// Trigonometric utilities
public float2 cossin(float x) { return float2(cos(x), sin(x)); }

// ----------------------------------------------------------------------------
// Color Utilities

// Luminance calculation (Rec. 709 coefficients)
public float luminance(float3 color) {
    return dot(color, float3(0.2126, 0.7152, 0.0722));
}

// ----------------------------------------------------------------------------
// Random Number Generation

public uint triple32(uint x) {
    // https://nullprogram.com/blog/2018/07/31/
    x ^= x >> 17;
    x *= 0xed5ad4bbu;
    x ^= x >> 11;
    x *= 0xac4c1b51u;
    x ^= x >> 15;
    x *= 0x31848babu;
    x ^= x >> 14;
    return uint(x);
}

public struct NoiseGenerator {
    public uint currentNum;
}

public NoiseGenerator createNoiseGenerator(float4 glFragCoord) {
    // Create seed from pixel coordinates
    uint x = uint(glFragCoord.x);
    uint y = uint(glFragCoord.y);
    uint seed = triple32(x + y * uint(Frame._ScreenParams.x));
    NoiseGenerator gen;
    gen.currentNum = seed;
    return gen;
}

public NoiseGenerator createNoiseGenerator(float4 glFragCoord, uint frameIndex) {
    // Create seed from pixel coordinates + frame index for temporal variation
    uint x = uint(glFragCoord.x);
    uint y = uint(glFragCoord.y);
    uint seed = triple32(x + y * uint(Frame._ScreenParams.x) + frameIndex * 1973u);
    NoiseGenerator gen;
    gen.currentNum = seed;
    return gen;
}

public uint randNext(inout NoiseGenerator gen)
{
    gen.currentNum = triple32(gen.currentNum);
    return gen.currentNum;
}

public uint2 randNext2(inout NoiseGenerator gen) { return uint2(randNext(gen), randNext(gen)); }
public uint3 randNext3(inout NoiseGenerator gen) { return uint3(randNext2(gen), randNext(gen)); }
public uint4 randNext4(inout NoiseGenerator gen) { return uint4(randNext3(gen), randNext(gen)); }

public float randNextF(inout NoiseGenerator gen) { return float(randNext(gen)) / float(0xffffffffu); }
public float2 randNext2F(inout NoiseGenerator gen) { return float2(randNext2(gen)) / float(0xffffffffu); }
public float3 randNext3F(inout NoiseGenerator gen) { return float3(randNext3(gen)) / float(0xffffffffu); }
public float4 randNext4F(inout NoiseGenerator gen) { return float4(randNext4(gen)) / float(0xffffffffu); }

// Simple hash-based random for per-pixel variation
public float hash1(float2 p) {
    float3 p3 = frac(float3(p.xyx) * 443.897);
    p3 += dot(p3, p3.zyx + 19.19);
    return frac((p3.x + p3.y) * p3.z);
}

public float2 hash2(float2 p) {
    float3 p3 = frac(float3(p.xyx) * float3(443.897, 441.423, 437.195));
    p3 += dot(p3, p3.yzx + 19.19);
    return frac((p3.xx + p3.yz) * p3.zy);
}

// ----------------------------------------------------------------------------
// Sampling Utilities

// Sample cosine-weighted hemisphere around a normal
public float3 SampleCosineHemisphere(float3 normal, float2 xy) {
    float phi = PROWL_TWO_PI * xy.x;
    float cosTheta = xy.y * 2.0 - 1.0;
    float sinTheta = sqrt(saturate(1.0 - cosTheta * cosTheta));
    float3 hemisphere = float3(cossin(phi) * sinTheta, cosTheta);

    float3 cosineVector = normalize(normal + hemisphere);
    return cosineVector * fastSign(dot(cosineVector, normal));
}

// ----------------------------------------------------------------------------
// Depth Utilities

// Convert screen-space depth to view-space depth
public float ScreenToViewDepth(float depth) {
    float z = depth * 2.0 - 1.0; // Back to NDC
    return -Frame.prowl_MatP[2][3] / (Frame.prowl_MatP[2][2] + z);
}

// ----------------------------------------------------------------------------
// Reprojection Utilities

// Reproject current screen position to previous frame using motion
public float3 Reproject(float2 screenUV, float depth, float4x4 prevViewProj) {
    // Get current view space position
    float3 viewPos = getViewPos(screenUV, depth);

    // Transform to world space
    float4 worldPos = mul(Frame.prowl_MatIV, float4(viewPos, 1.0));

    // Transform to previous frame's clip space
    float4 prevClip = mul(prevViewProj, worldPos);

    // Perspective divide
    float3 prevNDC = prevClip.xyz / prevClip.w;

    // Convert to screen space [0,1]
    float3 prevScreen = prevNDC * 0.5 + 0.5;

    return prevScreen;
}

// Check if reprojection is valid (no disocclusion)
public bool IsReprojectionValid(float2 prevUV, float currentDepth, float prevDepth, float3 currentNormal, float3 prevNormal) {
    // Out of screen
    if (prevUV.x < 0.0 || prevUV.x > 1.0 || prevUV.y < 0.0 || prevUV.y > 1.0)
        return false;

    // Depth discontinuity check
    float depthDiff = abs(currentDepth - prevDepth);
    if (depthDiff > 0.01)
        return false;

    // Normal discontinuity check
    float normalDot = dot(currentNormal, prevNormal);
    if (normalDot < 0.9)
        return false;

    return true;
}

// Normal Mapping ===========================================================

// Apply a tangent-space normal map to a world-space normal using a TBN basis.
// Callers without tangents should use normalize(normal) directly (via static if on their
// HAS_TANGENTS variant) rather than calling this.
public float3 ApplyNormalMap(Sampler2D<float4> normalTex, float2 uv, float3 normal, float3 tangent, float3 bitangent)
{
    float3x3 TBN = float3x3(normalize(tangent), normalize(bitangent), normalize(normal));
    float3 normalTS = normalTex.Sample(uv).rgb * 2.0 - 1.0;
    return normalize(mul(normalTS, TBN));
}

// Encode a world-space normal to view-space [0,1] range for the depth-normals pre-pass.
public float4 EncodeViewNormal(float3 worldNormal)
{
    float3 viewNormal = normalize(mul((float3x3)Frame.prowl_MatV, worldNormal));
    return float4(viewNormal * 0.5 + 0.5, 1.0);
}

// ----------------------------------------------------------------------------
