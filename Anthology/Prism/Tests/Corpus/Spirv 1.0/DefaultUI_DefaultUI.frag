#version 460 core

// =========================================================================
//  Shader "Default/DefaultUI" - Pass "DefaultUI" - Fragment Stage
//  Resolved from ShaderLab-like source. Includes flattened: ProwlCG, Lighting
//  (which itself pulls in PBR, Shadow, LightBVH).
// =========================================================================

// ---- Per-frame global uniforms (ShaderVariables.glsl) ----
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

#define PROWL_MATRIX_V prowl_MatV
#define PROWL_MATRIX_VP_PREVIOUS prowl_PrevViewProj
#define PROWL_MATRIX_I_V prowl_MatIV
#define PROWL_MATRIX_P prowl_MatP
#define PROWL_MATRIX_VP prowl_MatVP
#define PROWL_MATRIX_I_P prowl_MatIP
#define PROWL_MATRIX_I_VP prowl_MatIVP
#define PROWL_MATRIX_VP_NONJITTERED prowl_MatVP_NonJittered

// ---- Lighting.glsl uniforms (directional light, shadow atlas, cascades,
//      point/spot shadow arrays, fog, ambient, SH probe) ----
#ifndef MAX_SHADOW_CASTERS
#define MAX_SHADOW_CASTERS 4
#endif

layout(std140, set = 0, binding = 2) uniform LightingUniforms
{
    int   _DirectionalLightEnabled;
    vec3  _DirectionalLightDirection;
    vec3  _DirectionalLightColor;
    float _DirectionalLightIntensity;
    int   _DirectionalLightShadowEnabled;
    float _DirectionalLightShadowBias;
    float _DirectionalLightShadowNormalBias;
    float _DirectionalLightShadowStrength;
    float _DirectionalLightShadowQuality;

    vec2 _ShadowAtlasSize;

    int  _CascadeCount;
    mat4 _CascadeShadowMatrix0;
    mat4 _CascadeShadowMatrix1;
    mat4 _CascadeShadowMatrix2;
    mat4 _CascadeShadowMatrix3;
    vec4 _CascadeAtlasParams0;
    vec4 _CascadeAtlasParams1;
    vec4 _CascadeAtlasParams2;
    vec4 _CascadeAtlasParams3;

    mat4 _PointShadowMatrices[MAX_SHADOW_CASTERS * 6];
    vec4 _PointShadowFaceParams[MAX_SHADOW_CASTERS * 6];

    mat4 _SpotShadowMatrices[MAX_SHADOW_CASTERS];
    vec4 _SpotShadowAtlasParams[MAX_SHADOW_CASTERS];

    vec4 _FogColor;
    vec4 _FogParams;
    vec3 _FogStates;

    vec2 _AmbientMode;
    vec4 _AmbientColor;
    vec4 _AmbientSkyColor;
    vec4 _AmbientGroundColor;
    float _AmbientStrength;

    vec4 prowl_SHAr;
    vec4 prowl_SHAg;
    vec4 prowl_SHAb;
    vec4 prowl_SHBr;
    vec4 prowl_SHBg;
    vec4 prowl_SHBb;
    vec4 prowl_SHC;
};

layout(set = 0, binding = 4) uniform sampler2DShadow _ShadowAtlas;

// ---- LightBVH.glsl uniforms ----
#ifndef LBVH_MAX_NODE_VISITS
#define LBVH_MAX_NODE_VISITS 4096
#endif

layout(std140, set = 0, binding = 3) uniform LightBVHUniforms
{
    int _StaticLightTexSize;
    int _StaticLightTexShift;
    int _StaticNodeTexSize;
    int _StaticNodeTexShift;
    int _DynamicLightTexSize;
    int _DynamicLightTexShift;
    int _DynamicNodeTexSize;
    int _DynamicNodeTexShift;

    int _StaticLightRoot;
    int _DynamicLightRoot;
};

layout(set = 0, binding = 5) uniform sampler2D _StaticLightData;
layout(set = 0, binding = 6) uniform sampler2D _StaticLightNodes;
layout(set = 0, binding = 7) uniform sampler2D _DynamicLightData;
layout(set = 0, binding = 8) uniform sampler2D _DynamicLightNodes;

// ---- Material properties (Properties block) ----
layout(std140, set = 1, binding = 0) uniform MaterialUniforms
{
    vec4 _MainColor;
    vec2 _Tiling;
    vec2 _Offset;
};

layout(set = 1, binding = 1) uniform sampler2D _MainTex;

// ---- Pass-local uniforms (rounded-rect clip, RectMask) ----
layout(std140, set = 2, binding = 0) uniform PassUniforms
{
    mat4 _ClipToLocal;
    vec4 _ClipRect;      // minX, minY, maxX, maxY
    float _ClipRadius;
    float _ClipSoftness;
    float _ClipEnable;
};

// =========================================================================
//  ProwlCG.glsl
// =========================================================================
#ifndef PROWL_CG
#define PROWL_CG

#define PROWL_PI            3.14159265359
#define PROWL_TWO_PI        6.28318530718
#define PROWL_FOUR_PI       12.56637061436
#define PROWL_INV_PI        0.31830988618
#define PROWL_INV_TWO_PI    0.15915494309
#define PROWL_INV_FOUR_PI   0.07957747155
#define PROWL_HALF_PI       1.57079632679
#define PROWL_INV_HALF_PI   0.636619772367

// Colors ===========================================================
// http://chilliant.blogspot.com.au/2012/08/srgb-approximations-for-hlsl.html?m=1
vec3 linearToGammaSpace(vec3 lin)
{
    return max(1.055 * pow(max(lin, vec3(0.0)), vec3(0.416666667)) - 0.055, 0.0);
}

// http://chilliant.blogspot.com.au/2012/08/srgb-approximations-for-hlsl.html?m=1
vec3 gammaToLinearSpace(vec3 gamma)
{
    return gamma * (gamma * (gamma * 0.305306011 + 0.682171111) + 0.012522878);
}
// ============================================================================

float linearizeDepth(float depth, float near, float far)
{
    float z = depth * 2.0 - 1.0; // Back to NDC [-1,1] range
    return (2.0 * near * far) / (far + near - z * (far - near));
}

float linearizeDepthFromProjection(float depth) {
    return linearizeDepth(depth, _ProjectionParams.y, _ProjectionParams.z);
}

float getFovFromProjectionMatrix(mat4 proj)
{
    // proj[1][1] is M11, the Y scale
    // FOV = 2 * atan(1/M11)
    return 2.0 * atan(1.0 / proj[1][1]);
}

// ----------------------------------------------------------------------------

vec3 projectAndDivide(mat4 matrix, vec3 pos) {
    vec4 p = matrix * vec4(pos, 1.0);
    return p.xyz / p.w;
}

vec3 getScreenPos(vec2 tc, sampler2D depthSampler) {
	return vec3(tc, texture(depthSampler, tc).x);
}
vec3 getScreenPos(vec2 tc, float depth) {
	return vec3(tc, depth);
}

vec3 getScreenFromViewPos(vec3 viewPos) {
	vec3 p = projectAndDivide(PROWL_MATRIX_P, viewPos);
	return p * 0.5 + 0.5;
}

vec3 getNDCFromScreenPos(vec3 screenPos) {
	return screenPos * 2.0 - 1.0;
}

vec3 getViewFromScreenPos(vec3 screenPos) {
	return projectAndDivide(PROWL_MATRIX_I_P, getNDCFromScreenPos(screenPos));
}

vec3 getViewPos(vec2 tc, sampler2D depthSampler) {
	return getViewFromScreenPos(getScreenPos(tc, depthSampler));
}
vec3 getViewPos(vec2 tc, float depth) {
	return getViewFromScreenPos(getScreenPos(tc, depth));
}

// ----------------------------------------------------------------------------
// Math Utilities

#define saturate(x) clamp(x, 0.0, 1.0)
#define rcp(x) (1.0 / (x))
#define max0(x) max(x, 0.0)

// Min/Max of vector components
float minOf(vec2 v) { return min(v.x, v.y); }
float minOf(vec3 v) { return min(v.x, min(v.y, v.z)); }
float minOf(vec4 v) { return min(v.x, min(v.y, min(v.z, v.w))); }
float maxOf(vec2 v) { return max(v.x, v.y); }
float maxOf(vec3 v) { return max(v.x, max(v.y, v.z)); }
float maxOf(vec4 v) { return max(v.x, max(v.y, max(v.z, v.w))); }

// Squared dot product
float sdot(vec2 x) { return dot(x, x); }
float sdot(vec3 x) { return dot(x, x); }
float sdot(vec4 x) { return dot(x, x); }

// Square function
float sqr(float x) { return x * x; }
vec2 sqr(vec2 x) { return x * x; }
vec3 sqr(vec3 x) { return x * x; }
vec4 sqr(vec4 x) { return x * x; }

// Linear step with saturation
float linearstep(float a, float b, float x) {
	return saturate((x - a) / (b - a));
}

// Fast approximations for trigonometric functions
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
vec3 diagonal3(mat4 m) { return vec3(m[0].x, m[1].y, m[2].z); }

// Trigonometric utilities
vec2 cossin(float x) { return vec2(cos(x), sin(x)); }

// ----------------------------------------------------------------------------
// Color Utilities

// Luminance calculation (Rec. 709 coefficients)
float luminance(vec3 color) {
	return dot(color, vec3(0.2126, 0.7152, 0.0722));
}

// ----------------------------------------------------------------------------
// Random Number Generation

uint triple32(uint x) {
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

struct NoiseGenerator {
	uint currentNum;
};

NoiseGenerator createNoiseGenerator(vec4 glFragCoord) {
    // Create seed from pixel coordinates
    uint x = uint(glFragCoord.x);
    uint y = uint(glFragCoord.y);
    uint seed = triple32(x + y * uint(_ScreenParams.x));
	return NoiseGenerator(seed);
}

NoiseGenerator createNoiseGenerator(vec4 glFragCoord, uint frameIndex) {
    // Create seed from pixel coordinates + frame index for temporal variation
    uint x = uint(glFragCoord.x);
    uint y = uint(glFragCoord.y);
    uint seed = triple32(x + y * uint(_ScreenParams.x) + frameIndex * 1973u);
	return NoiseGenerator(seed);
}

uint randNext(inout NoiseGenerator gen)
{
    gen.currentNum = triple32(gen.currentNum);
    return gen.currentNum;
}

uvec2 randNext2(inout NoiseGenerator gen) { return uvec2(randNext(gen), randNext(gen)); }
uvec3 randNext3(inout NoiseGenerator gen) { return uvec3(randNext2(gen), randNext(gen)); }
uvec4 randNext4(inout NoiseGenerator gen) { return uvec4(randNext3(gen), randNext(gen)); }

float randNextF(inout NoiseGenerator gen) { return float(randNext(gen)) / float(0xffffffffu); }
vec2 randNext2F(inout NoiseGenerator gen) { return vec2(randNext2(gen)) / float(0xffffffffu); }
vec3 randNext3F(inout NoiseGenerator gen) { return vec3(randNext3(gen)) / float(0xffffffffu); }
vec4 randNext4F(inout NoiseGenerator gen) { return vec4(randNext4(gen)) / float(0xffffffffu); }

// Simple hash-based random for per-pixel variation
float hash1(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 443.897);
	p3 += dot(p3, p3.zyx + 19.19);
	return fract((p3.x + p3.y) * p3.z);
}

vec2 hash2(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * vec3(443.897, 441.423, 437.195));
	p3 += dot(p3, p3.yzx + 19.19);
	return fract((p3.xx + p3.yz) * p3.zy);
}

// ----------------------------------------------------------------------------
// Sampling Utilities

// Sample cosine-weighted hemisphere around a normal
vec3 SampleCosineHemisphere(vec3 normal, vec2 xy) {
	float phi = PROWL_TWO_PI * xy.x;
	float cosTheta = xy.y * 2.0 - 1.0;
	float sinTheta = sqrt(saturate(1.0 - cosTheta * cosTheta));
	vec3 hemisphere = vec3(cossin(phi) * sinTheta, cosTheta);

	vec3 cosineVector = normalize(normal + hemisphere);
	return cosineVector * fastSign(dot(cosineVector, normal));
}

// ----------------------------------------------------------------------------
// Depth Utilities

// Convert screen-space depth to view-space depth
float ScreenToViewDepth(float depth) {
	float z = depth * 2.0 - 1.0; // Back to NDC
	return -PROWL_MATRIX_P[3].z / (PROWL_MATRIX_P[2].z + z);
}

// ----------------------------------------------------------------------------
// Reprojection Utilities

// Reproject current screen position to previous frame using motion
vec3 Reproject(vec2 screenUV, float depth, mat4 prevViewProj) {
	// Get current view space position
	vec3 viewPos = getViewPos(screenUV, depth);

	// Transform to world space
	vec4 worldPos = PROWL_MATRIX_I_V * vec4(viewPos, 1.0);

	// Transform to previous frame's clip space
	vec4 prevClip = prevViewProj * worldPos;

	// Perspective divide
	vec3 prevNDC = prevClip.xyz / prevClip.w;

	// Convert to screen space [0,1]
	vec3 prevScreen = prevNDC * 0.5 + 0.5;

	return prevScreen;
}

// Check if reprojection is valid (no disocclusion)
bool IsReprojectionValid(vec2 prevUV, float currentDepth, float prevDepth, vec3 currentNormal, vec3 prevNormal) {
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

// Apply a tangent-space normal map to a world-space normal.
// If tangents are available (HAS_TANGENTS), constructs a TBN matrix and transforms
// the sampled normal. Otherwise returns the interpolated world normal as-is.
//
// Usage:
//   vec3 worldNormal = ApplyNormalMap(normalTex, uv, vNormal, vTangent, vBitangent);
//
// For shaders without normal maps, just use normalize(vNormal) directly.
vec3 ApplyNormalMap(sampler2D normalTex, vec2 uv, vec3 normal, vec3 tangent, vec3 bitangent)
{
#ifdef HAS_TANGENTS
    mat3 TBN = mat3(normalize(tangent), normalize(bitangent), normalize(normal));
    vec3 normalTS = texture(normalTex, uv).rgb * 2.0 - 1.0;
    return normalize(TBN * normalTS);
#else
    return normalize(normal);
#endif
}

// Encode a world-space normal to view-space [0,1] range for the depth-normals pre-pass.
vec4 EncodeViewNormal(vec3 worldNormal)
{
    vec3 viewNormal = normalize(mat3(PROWL_MATRIX_V) * worldNormal);
    return vec4(viewNormal * 0.5 + 0.5, 1.0);
}

// ----------------------------------------------------------------------------


#endif

// =========================================================================
//  PBR.glsl
// =========================================================================
#ifndef PBR_FUNCTIONS
#define PBR_FUNCTIONS

#ifndef MATH_PI
#define MATH_PI
const float PI = 3.14159265359;
#endif

// =============================================================
//  Standard Isotropic BRDF (GGX + Smith + Disney Diffuse)
// =============================================================

// GGX/Trowbridge-Reitz Normal Distribution Function
// Input roughness is perceptual roughness [0,1] squared internally to get alpha.
float DistributionGGX(vec3 N, vec3 H, float roughness)
{
    float a2 = roughness * roughness; // alpha = perceptualRoughness^2, a2 = alpha
    float NdotH = max(dot(N, H), 0.0);
    float NdotH2 = NdotH * NdotH;

    float denom = (NdotH2 * (a2 - 1.0) + 1.0);
    denom = PI * denom * denom;

    return a2 / denom;
}

// Schlick-GGX Geometry Function (single direction)
// Uses k = (roughness + 1)^2 / 8 for direct/analytical lights.
float GeometrySchlickGGX(float NdotV, float roughness)
{
    float r = (roughness + 1.0);
    float k = (r * r) / 8.0;

    return NdotV / (NdotV * (1.0 - k) + k);
}

// Smith Geometry Function (both directions combined)
// Uses abs(NdotV) to handle backface normals gracefully.
float GeometrySmith(vec3 N, vec3 V, vec3 L, float roughness)
{
    float NdotV = abs(dot(N, V));
    float NdotL = max(dot(N, L), 0.0);
    float ggx2 = GeometrySchlickGGX(NdotV, roughness);
    float ggx1 = GeometrySchlickGGX(NdotL, roughness);

    return ggx1 * ggx2;
}

// Schlick Fresnel Approximation (fast exp2 variant)
vec3 FresnelSchlick(float cosTheta, vec3 F0)
{
    return F0 + (1.0 - F0) * exp2(-9.28 * cosTheta);
}

// Fresnel with roughness for IBL/indirect specular (Lazarov Environmental BRDF)
vec3 FresnelSchlickRoughness(float cosTheta, vec3 F0, float roughness)
{
    vec3 F90 = max(vec3(1.0 - roughness), F0);
    return F0 + (F90 - F0) * exp2(-9.28 * cosTheta);
}

// =============================================================
//  Disney Diffuse (from Burley 2012)
//  Roughness-dependent diffuse that darkens at grazing angles
//  for rough surfaces more physically accurate than Lambertian.
// =============================================================

float DisneyDiffuse(float NdotV, float NdotL, float LdotH, float perceptualRoughness)
{
    float fd90 = 0.5 + 2.0 * LdotH * LdotH * perceptualRoughness;
    float lightScatter = 1.0 + (fd90 - 1.0) * exp2(-8.35 * NdotL);
    float viewScatter = 1.0 + (fd90 - 1.0) * exp2(-8.35 * NdotV);
    return lightScatter * viewScatter / PI;
}

// =============================================================
//  Anisotropic BRDF (Far Cry 4 / GGX Aniso)
//  For brushed metal, hair, and directional surfaces.
// =============================================================

// Anisotropic GGX NDF
// mt and mb are SQUARED roughness values (mt = roughnessT^2, mb = roughnessB^2)
float DistributionGGXAniso(float TdotH, float BdotH, float NdotH, float mt, float mb)
{
    float d = TdotH * TdotH / (mt * mt) + BdotH * BdotH / (mb * mb) + NdotH * NdotH;
    return 1.0 / (PI * mt * mb * d * d);
}

// Anisotropic Smith Joint Visibility (Far Cry 4)
// mt and mb are SQUARED roughness values (mt = roughnessT^2, mb = roughnessB^2)
float GeometrySmithAniso(
    float TdotV, float BdotV, float NdotV,
    float TdotL, float BdotL, float NdotL,
    float mt, float mb)
{
    float lambdaV = NdotL * sqrt(mt * TdotV * TdotV + mb * BdotV * BdotV + NdotV * NdotV);
    float lambdaL = NdotV * sqrt(mt * TdotL * TdotL + mb * BdotL * BdotL + NdotL * NdotL);
    return 0.5 / max(1e-5, lambdaV + lambdaL);
}

// =============================================================
//  Specular Anti-Aliasing (Geometric Normal Filtering)
//  Reduces specular shimmer on normal-mapped surfaces by
//  increasing roughness where normal variance is high.
//  Based on: Kaplanyan & Hill, "Filtering Distributions of Normals"
// =============================================================

#define SPEC_AA_VARIANCE 0.5
#define SPEC_AA_THRESHOLD 0.1

float ApplySpecularAA(float roughness, vec3 worldNormal)
{
    vec3 ddxN = dFdx(worldNormal);
    vec3 ddyN = dFdy(worldNormal);
    float variance = SPEC_AA_VARIANCE * (dot(ddxN, ddxN) + dot(ddyN, ddyN));
    float kernelRoughnessSq = min(2.0 * variance, SPEC_AA_THRESHOLD);
    float squaredRoughness = clamp(roughness * roughness + kernelRoughnessSq, 0.0, 1.0);
    return sqrt(squaredRoughness);
}

// =============================================================
//  Parallax Occlusion Mapping (POM)
//  Linear ray march + secant-method binary refinement.
//
//  Usage:
//    vec2 pomUV = ParallaxOcclusionMapping(heightTex, uv, viewDirTS, heightScale, steps);
//
//  viewDirTS must be in tangent space (use transpose(TBN) * viewDir).
//  Height is sampled from the G channel of the height texture.
// =============================================================

vec2 ParallaxOcclusionMapping(sampler2D heightTex, vec2 uv, vec3 viewDirTS, float heightScale, int linearSteps)
{
    // Slope damping: reduce parallax at grazing angles
    float slopeDamp = 1.0 - clamp(dot(viewDirTS, vec3(0.0, 0.0, 1.0)), 0.0, 1.0);

    // Maximum UV offset based on view angle and height scale
    vec2 vMaxOffset = (viewDirTS.xy / -viewDirTS.z) * heightScale * (1.0 - slopeDamp * slopeDamp);

    float fStepSize = 1.0 / float(linearSteps);
    vec2 finalStepSize = fStepSize * vMaxOffset;

    // Pre-compute texture derivatives for correct mip selection
    vec2 dx = dFdx(uv);
    vec2 dy = dFdy(uv);

    // Linear ray march from surface (1.0) downward
    float fCurrRayHeight = 1.0;
    vec2 vCurrOffset = vec2(0.0);
    vec2 vLastOffset = vec2(0.0);
    float fLastSampledHeight = 1.0;
    float fCurrSampledHeight = 1.0;

    for (int i = 0; i < linearSteps; i++)
    {
        fCurrSampledHeight = textureGrad(heightTex, uv + vCurrOffset, dx, dy).g;

        if (fCurrSampledHeight > fCurrRayHeight)
            break;

        fCurrRayHeight -= fStepSize;
        vLastOffset = vCurrOffset;
        vCurrOffset += finalStepSize;
        fLastSampledHeight = fCurrSampledHeight;
    }

    // Secant-method binary refinement (3 iterations)
    float pt0 = fCurrRayHeight + fStepSize;
    float pt1 = fCurrRayHeight;
    float delta0 = pt0 - fLastSampledHeight;
    float delta1 = pt1 - fCurrSampledHeight;

    for (int j = 0; j < 3; j++)
    {
        float intersectionHeight = (pt0 * delta1 - pt1 * delta0) / (delta1 - delta0);
        vCurrOffset = (1.0 - intersectionHeight) * finalStepSize * float(linearSteps);

        fCurrSampledHeight = textureGrad(heightTex, uv + vCurrOffset, dx, dy).g;

        float delta = intersectionHeight - fCurrSampledHeight;
        if (delta < 0.0)
        {
            delta1 = delta;
            pt1 = intersectionHeight;
        }
        else
        {
            delta0 = delta;
            pt0 = intersectionHeight;
        }
    }

    return uv + vCurrOffset;
}

// =============================================================
//  Translucency
//  Two modes controlled by scatteringPower:
//    - scatteringPower == 0: Wrapped diffuse + GGX backscatter (foliage, thin surfaces)
//    - scatteringPower > 0:  Spherical Gaussian approximation (skin, wax)
//  Reference: Colin Barre-Brisebois, GDC 2011
//
//  translucency: per-pixel thickness/translucency value (from texture B channel)
//  scatteringPower: Gaussian falloff exponent (0 = wrapped mode, 1-8 = gaussian)
//  distortion: how much the normal bends the light direction through the surface
//  scale: overall translucency intensity multiplier
// =============================================================

vec3 CalculateTranslucency(vec3 lightDir, vec3 viewDir, vec3 normal,
                           float translucency, float scatteringPower,
                           float distortion, float scale, vec3 lightColor)
{
    if (translucency <= 0.0) return vec3(0.0);

    vec3 lightScattering = vec3(0.0);

    if (scatteringPower < 0.001)
    {
        // Mode 1: Wrapped diffuse + GGX backscatter lobe
        // foliage, thin cloth, paper
        float wrap = 0.5;
        float wrappedNdotL = clamp((dot(-normal, lightDir) + wrap) / ((1.0 + wrap) * (1.0 + wrap)), 0.0, 1.0);

        // GGX-shaped backscatter approximation
        float VdotL = clamp(dot(viewDir, -lightDir), 0.0, 1.0);
        float a2 = 0.49; // 0.7^2
        float d = (VdotL * a2 - VdotL) * VdotL + 1.0;
        float GGX = (a2 / PI) / (d * d);

        lightScattering = wrappedNdotL * GGX * translucency * lightColor;
    }
    else
    {
        // Mode 2: Spherical Gaussian approximation
        // skin, wax, subsurface materials
        vec3 transLightDir = lightDir + normal * distortion;
        float transDot = dot(-transLightDir, viewDir);
        transDot = exp2(clamp(transDot, 0.0, 1.0) * scatteringPower - scatteringPower) * translucency;

        lightScattering = transDot * lightColor;
    }

    return lightScattering * scale;
}

#endif

// =========================================================================
//  Shadow.glsl (no include guard in the original source; wrapped here for
//  safe dedupe-by-name discipline)
// =========================================================================
#ifndef PROWL_SHADOW_INLINED
#define PROWL_SHADOW_INLINED

// Precomputed Poisson disk offsets for rotated PCF sampling
const vec2 POISSON_DISK_8[8] = vec2[](
    vec2(-0.613392,  0.617481),
    vec2( 0.170019, -0.040254),
    vec2(-0.299417, -0.792901),
    vec2( 0.645680, -0.530998),
    vec2( 0.454148,  0.516511),
    vec2(-0.507431,  0.281182),
    vec2(-0.177186, -0.283153),
    vec2( 0.100558,  0.765839)
);

// Simple hash function for random rotation in PCF sampling
float InterleavedGradientNoise(vec2 position) {
    vec3 magic = vec3(0.06711056, 0.00583715, 52.9829189);
    return fract(magic.z * fract(dot(position, magic.xy)));
}

// Reconstruct world position from depth buffer
vec3 WorldPosFromDepth(float depth, vec2 texCoord) {
    float z = depth * 2.0 - 1.0;
    vec4 clipSpacePosition = vec4(texCoord * 2.0 - 1.0, z, 1.0);
    vec4 worldSpacePosition = PROWL_MATRIX_I_VP * clipSpacePosition;
    worldSpacePosition /= worldSpacePosition.w;
    return worldSpacePosition.xyz;
}

// Sample shadow from atlas with PCF filtering
// Parameters:
//   shadowAtlas: The shadow atlas texture
//   atlasCoords: UV coordinates in the atlas (normalized 0-1)
//   shadowMin/Max: Atlas boundaries for clamping (prevents bleeding)
//   currentDepth: Fragment depth in light space
//   shadowQuality: 0 = hard shadows, 1 = soft shadows
//   shadowStrength: Multiplier for shadow intensity
//   filterRadius: Soft PCF kernel radius in atlas texels (callers scale this per cascade so the
//                 penumbra stays roughly constant in world space; ignored on the hard path)
float SampleShadowPCF(
    sampler2DShadow shadowAtlas,
    vec2 atlasCoords,
    vec2 shadowMin,
    vec2 shadowMax,
    float currentDepth,
    float shadowQuality,
    float shadowStrength,
    float filterRadius)
{
    // Hardware depth comparison (GL_COMPARE_REF_TO_TEXTURE + GL_LEQUAL): texture() returns the
    // filtered fraction that is LIT (currentDepth <= storedDepth). With LINEAR filtering on the
    // atlas each fetch is a hardware 2x2 PCF tap, so even the hard path is bilinearly filtered.
    float lit;

    // Check shadow quality: 0 = Hard, 1 = Soft
    if (shadowQuality < 0.5) {
        // Hard shadows - single hardware-compared (2x2) sample
        lit = texture(shadowAtlas, vec3(atlasCoords, currentDepth));
    } else {
        // Soft shadows - rotated Poisson disk, each tap a hardware 2x2 comparison
        vec2 texelSize = vec2(1.0) / vec2(textureSize(shadowAtlas, 0));
        float randomRotation = InterleavedGradientNoise(gl_FragCoord.xy) * 6.283185;
        float s = sin(randomRotation);
        float c = cos(randomRotation);
        mat2 rotationMatrix = mat2(c, -s, s, c);

        vec2 texelScale = texelSize * filterRadius;
        lit = 0.0;
        for(int i = 0; i < 8; i++) {
            vec2 offset = (rotationMatrix * POISSON_DISK_8[i]) * texelScale;
            vec2 sampleCoords = clamp(atlasCoords + offset, shadowMin, shadowMax);
            lit += texture(shadowAtlas, vec3(sampleCoords, currentDepth));
        }
        lit /= 8.0;
    }

    float shadow = 1.0 - lit;       // fraction occluded
    return shadow * shadowStrength;
}

// Calculate slope-scale bias based on surface angle to light
// Parameters:
//   worldNormal: Surface normal in world space
//   lightDirection: Direction to light (normalized)
//   baseBias: Base shadow bias value
// Returns: Combined bias value
float CalculateSlopeBias(vec3 worldNormal, vec3 lightDirection, float baseBias) {
    float cosTheta = clamp(dot(normalize(worldNormal), normalize(lightDirection)), 0.0, 1.0);
    // tan(acos(cosTheta)) == sin/cos, without the two transcendentals.
    float sinTheta = sqrt(max(1.0 - cosTheta * cosTheta, 0.0));
    float slopeScaleBias = baseBias * (sinTheta / max(cosTheta, 1e-4));
    slopeScaleBias = clamp(slopeScaleBias, 0.0, baseBias * 2.0);
    return baseBias + slopeScaleBias;
}

// Convert shadow space coordinates to atlas UVs
// Parameters:
//   projCoords: Projected coordinates from shadow matrix (already in 0-1 range)
//   atlasParams: xy = atlas position, z = atlas size
//   atlasSize: Total atlas texture size
// Returns: Normalized atlas coordinates and boundaries for clamping
void GetAtlasCoordinates(
    vec3 projCoords,
    vec4 atlasParams,
    float atlasSize,
    out vec2 atlasCoords,
    out vec2 shadowMin,
    out vec2 shadowMax)
{
    // Map projected coords to atlas region
    atlasCoords.x = atlasParams.x + (projCoords.x * atlasParams.z);
    atlasCoords.y = atlasParams.y + (projCoords.y * atlasParams.z);

    // Calculate shadow map boundaries to prevent bleeding
    vec2 texelSize = vec2(1.0) / atlasSize;
    shadowMin = vec2(atlasParams.x, atlasParams.y) / atlasSize + texelSize * 0.5;
    shadowMax = vec2(atlasParams.x + atlasParams.z, atlasParams.y + atlasParams.z) / atlasSize - texelSize * 0.5;

    // Normalize to 0-1 range
    atlasCoords /= atlasSize;
}

#endif

// =========================================================================
//  LightBVH.glsl
//  Stackless rope-BVH light traversal for forward rendering.
//
//  Each Scene maintains two BVHs (static + dynamic). Per fragment the shader walks both trees
//  and accumulates contributions from every light whose AABB contains worldPos. Layout MUST
//  stay in lockstep with `LightBVH.cs` and `LightBVHTextures.cs`.
//
//  LIGHT DATA TEXTURE (RGBA32F, square, power-of-2 width):
//    slot s, linear texel base = s * 5:
//      +0 : Position.xyz, Range
//      +1 : Color.rgb,    Intensity
//      +2 : Direction.xyz, TypeAndFlags   (low 2 bits: type, bit 2: ShadowEnabled)
//      +3 : SpotCos, InnerSpotCos, ShadowBias, ShadowNormalBias
//           (only fetched when type==Spot OR ShadowEnabled)
//      +4 : ShadowStrength, ShadowQuality, ShadowSlotAsFloat, padding
//           (only fetched when ShadowEnabled)
//
//  NODE TEXTURE (RGBA32F, square, power-of-2 width):
//    node n, linear texel base = n * 2:
//      +0 : Min.xyz, Hit  (floatBitsToInt to recover signed int)
//      +1 : Max.xyz, Miss
//
//    Internal: Hit = first child index. Miss = "escape" index (-1 = traversal complete).
//    Leaf:     Hit = -(slot + 1).         Miss = escape.
// =========================================================================
#ifndef PROWL_LIGHT_BVH
#define PROWL_LIGHT_BVH

struct LightSample
{
    vec3  Position;
    float Range;
    vec3  Color;
    float Intensity;
    vec3  Direction;
    int   Type;            // 0 directional, 1 point, 2 spot
    float SpotCos;         // cos(outer)
    float InnerSpotCos;    // cos(inner)
    float ShadowBias;
    float ShadowNormalBias;
    float ShadowStrength;
    float ShadowQuality;
    int   ShadowSlot;      // -1 if no atlas slot this frame
    int   ShadowEnabled;   // 0 / 1
};

// Wrap a linear texel index into a 2D coordinate. `dim` must be a power of 2 and `shift` must
// equal log2(dim). The shift makes the y-coordinate a bit-shift instead of an integer division
// (`/` against a uniform divisor compiles to a real `idiv` on most GPUs).
ivec2 LBVH_Coord(int linearIdx, int dim, int shift)
{
    int mask = dim - 1;
    return ivec2(linearIdx & mask, linearIdx >> shift);
}

// Lazy load: only fetch the texels each light type / shadow state actually needs. An unshadowed
// point light reads 3 texels; a shadowed spot light reads 5. Particle systems with hundreds of
// unshadowed point lights save ~40 % of light-data bandwidth here.
LightSample LBVH_FetchLight(sampler2D tex, int dim, int shift, int slot)
{
    int base = slot * 5;
    vec4 t0 = texelFetch(tex, LBVH_Coord(base + 0, dim, shift), 0);
    vec4 t1 = texelFetch(tex, LBVH_Coord(base + 1, dim, shift), 0);
    vec4 t2 = texelFetch(tex, LBVH_Coord(base + 2, dim, shift), 0);

    int typeAndFlags = floatBitsToInt(t2.w);
    int type = typeAndFlags & 3;
    int shadowEnabled = (typeAndFlags >> 2) & 1;

    LightSample L;
    L.Position      = t0.xyz;
    L.Range         = t0.w;
    L.Color         = t1.rgb;
    L.Intensity     = t1.w;
    L.Direction     = t2.xyz;
    L.Type          = type;
    L.ShadowEnabled = shadowEnabled;

    // Texel 3 carries spot cosines AND shadow biases. Both spot lighting and shadow sampling
    // need it; everything else (an unshadowed point) skips it entirely.
    if (type == 2 || shadowEnabled != 0)
    {
        vec4 t3 = texelFetch(tex, LBVH_Coord(base + 3, dim, shift), 0);
        L.SpotCos          = t3.x;
        L.InnerSpotCos     = t3.y;
        L.ShadowBias       = t3.z;
        L.ShadowNormalBias = t3.w;
    }
    else
    {
        // Defaults that make the spot-cone smoothstep pass through (1.0) and shadow biases
        // harmless if some downstream code reads them anyway.
        L.SpotCos          = -1.0;
        L.InnerSpotCos     =  1.0;
        L.ShadowBias       =  0.0;
        L.ShadowNormalBias =  0.0;
    }

    if (shadowEnabled != 0)
    {
        vec4 t4 = texelFetch(tex, LBVH_Coord(base + 4, dim, shift), 0);
        L.ShadowStrength = t4.x;
        L.ShadowQuality  = t4.y;
        L.ShadowSlot     = floatBitsToInt(t4.z);
    }
    else
    {
        L.ShadowStrength = 0.0;
        L.ShadowQuality  = 0.0;
        L.ShadowSlot     = -1;
    }
    return L;
}

// Stateless iterator over a rope BVH. Initialize with LBVH_Begin, then call LBVH_Next in a
// loop until it returns -1. Each non-negative return is a slot index whose AABB contains
// `worldPos`; the caller fetches its light data via LBVH_FetchLight.
struct LBVH_Iter
{
    int current;
};

void LBVH_Begin(out LBVH_Iter it, int root)
{
    it.current = root;
}

int LBVH_Next(inout LBVH_Iter it, sampler2D nodeTex, int dim, int shift, vec3 worldPos)
{
    int budget = LBVH_MAX_NODE_VISITS;
    while (it.current >= 0 && budget > 0)
    {
        budget--;
        int base = it.current * 2;
        vec4 lo = texelFetch(nodeTex, LBVH_Coord(base + 0, dim, shift), 0);
        vec4 hi = texelFetch(nodeTex, LBVH_Coord(base + 1, dim, shift), 0);

        int hitNext  = floatBitsToInt(lo.w);
        int missNext = floatBitsToInt(hi.w);

        if (hitNext < 0)
        {
            // Leaf. The leaf's AABB is the light's range cube (pos +- range, axis-aligned), so
            // testing against the inscribed sphere is tighter (~50% smaller hit volume) and
            // strictly cheaper (1 dot vs 6 compares). Saves us from running 5 texelFetches +
            // PBR + shadow sampling for fragments that landed in the AABB corners but are
            // outside the actual sphere of influence.
            vec3 c = (lo.xyz + hi.xyz) * 0.5;
            float r = (hi.x - lo.x) * 0.5; // symmetric, same in y/z
            vec3 d = worldPos - c;
            it.current = missNext;
            if (dot(d, d) <= r * r)
                return -hitNext - 1;
        }
        else
        {
            // Internal node: AABB test (cheaper to compare than to sphere-test against the
            // bounds of an arbitrary union).
            bool inside =
                worldPos.x >= lo.x && worldPos.x <= hi.x &&
                worldPos.y >= lo.y && worldPos.y <= hi.y &&
                worldPos.z >= lo.z && worldPos.z <= hi.z;
            it.current = inside ? hitNext : missNext;
        }
    }
    return -1;
}

#endif

// =========================================================================
//  Lighting.glsl
//  Forward lighting utilities for Prowl Engine.
//  Provides: CalculateForwardLighting(), CalculateAmbient(), ApplyFog()
//
//  Light data lives in two BVHs (static + dynamic). Per fragment we walk both trees and
//  accumulate contributions from every light whose tight AABB contains worldPos. The single
//  directional light is uploaded separately and evaluated unconditionally (no BVH).
// =========================================================================
#ifndef PROWL_LIGHTING
#define PROWL_LIGHTING

// ============================================================
//  Helpers preserved for shader-graph access
// ============================================================

// Unit direction FROM the surface TO the camera, world-space.
vec3 GetWorldViewDir(vec3 worldPos)
{
    return normalize(_WorldSpaceCameraPos.xyz - worldPos);
}

// Unit direction FROM the surface TO the camera in tangent space.
vec3 GetTangentViewDir(vec3 worldPos, vec3 worldNormal, vec3 worldTangent, vec3 worldBitangent)
{
    vec3 vWorld = GetWorldViewDir(worldPos);
    mat3 tbnT = transpose(mat3(normalize(worldTangent), normalize(worldBitangent), normalize(worldNormal)));
    return normalize(tbnT * vWorld);
}

// ============================================================
//  Shadow sampling
// ============================================================

float SampleDirectionalShadow(vec3 worldPos, vec3 worldNormal)
{
    if (_CascadeCount == 0) return 0.0;

    // Compare squared distance against squared cascade splits to avoid the per-fragment sqrt.
    // worldDistance was distance(...) * 2.0, so the squared form is dot(d,d) * 4.0.
    vec3 toCamera = worldPos - _WorldSpaceCameraPos.xyz;
    float worldDistSq = dot(toCamera, toCamera) * 4.0;

    mat4 cascadeMatrix;
    vec4 cascadeParams;

    if (_CascadeCount >= 1 && worldDistSq <= _CascadeAtlasParams0.w * _CascadeAtlasParams0.w) {
        cascadeMatrix = _CascadeShadowMatrix0;
        cascadeParams = _CascadeAtlasParams0;
    } else if (_CascadeCount >= 2 && worldDistSq <= _CascadeAtlasParams1.w * _CascadeAtlasParams1.w) {
        cascadeMatrix = _CascadeShadowMatrix1;
        cascadeParams = _CascadeAtlasParams1;
    } else if (_CascadeCount >= 3 && worldDistSq <= _CascadeAtlasParams2.w * _CascadeAtlasParams2.w) {
        cascadeMatrix = _CascadeShadowMatrix2;
        cascadeParams = _CascadeAtlasParams2;
    } else if (_CascadeCount >= 4 && worldDistSq <= _CascadeAtlasParams3.w * _CascadeAtlasParams3.w) {
        cascadeMatrix = _CascadeShadowMatrix3;
        cascadeParams = _CascadeAtlasParams3;
    } else {
        // Beyond all cascades: clamp to last available.
        if      (_CascadeCount == 1) { cascadeMatrix = _CascadeShadowMatrix0; cascadeParams = _CascadeAtlasParams0; }
        else if (_CascadeCount == 2) { cascadeMatrix = _CascadeShadowMatrix1; cascadeParams = _CascadeAtlasParams1; }
        else if (_CascadeCount == 3) { cascadeMatrix = _CascadeShadowMatrix2; cascadeParams = _CascadeAtlasParams2; }
        else                         { cascadeMatrix = _CascadeShadowMatrix3; cascadeParams = _CascadeAtlasParams3; }
    }

    if (cascadeParams.z <= 0.0) return 0.0;

    vec3 worldPosBiased = worldPos + normalize(worldNormal) * _DirectionalLightShadowNormalBias;
    vec4 lightSpacePos = cascadeMatrix * vec4(worldPosBiased, 1.0);
    vec3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
    projCoords = projCoords * 0.5 + 0.5;

    if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
        return 0.0;

    float atlasSize = _ShadowAtlasSize.x;
    vec2 atlasCoords, shadowMin, shadowMax;
    GetAtlasCoordinates(projCoords, cascadeParams, atlasSize, atlasCoords, shadowMin, shadowMax);

    float slopeBias = CalculateSlopeBias(worldNormal, _DirectionalLightDirection, _DirectionalLightShadowBias);
    float texelWorldSize = ((cascadeParams.w * 4.0) / (cascadeParams.z * atlasSize)) * 8.0;
    float currentDepth = projCoords.z - (slopeBias + texelWorldSize);

    // World-normalized PCF radius: a fixed texel kernel blurs far more world space in the coarse far
    // cascades than the fine near ones, so the softness (and the shadow) visibly jumps at each split.
    // Scale the kernel to hold the penumbra roughly constant in world units. cascadeParams.w = world
    // span, .z = tile resolution. Clamped so near cascades don't over-spread the 8 taps and far cascades
    // don't collapse below the hardware tap.
    const float shadowPenumbraWorld = 0.04; // target penumbra in world units (Tunable)
    float worldPerTexel = cascadeParams.w / max(cascadeParams.z, 1.0);
    float pcfRadius = clamp(shadowPenumbraWorld / worldPerTexel, 0.75, 4.0);

    return SampleShadowPCF(_ShadowAtlas, atlasCoords, shadowMin, shadowMax,
                           currentDepth, _DirectionalLightShadowQuality, _DirectionalLightShadowStrength, pcfRadius);
}

float SamplePointShadow(LightSample L, int shadowSlot, vec3 worldPos, vec3 worldNormal)
{
    vec3 lightToFrag = worldPos - L.Position;
    vec3 absDir = abs(lightToFrag);

    int faceIndex = 0;
    if (absDir.x >= absDir.y && absDir.x >= absDir.z)
        faceIndex = lightToFrag.x > 0.0 ? 0 : 1;
    else if (absDir.y >= absDir.x && absDir.y >= absDir.z)
        faceIndex = lightToFrag.y > 0.0 ? 2 : 3;
    else
        faceIndex = lightToFrag.z > 0.0 ? 4 : 5;

    int idx = shadowSlot * 6 + faceIndex;
    mat4 shadowMatrix = _PointShadowMatrices[idx];
    vec4 faceParams = _PointShadowFaceParams[idx];

    vec3 worldPosBiased = worldPos + normalize(worldNormal) * L.ShadowNormalBias;
    vec4 lightSpacePos = shadowMatrix * vec4(worldPosBiased, 1.0);
    vec3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
    projCoords = projCoords * 0.5 + 0.5;

    if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
        return 0.0;

    float atlasSize = _ShadowAtlasSize.x;
    vec2 atlasCoords, shadowMin, shadowMax;
    GetAtlasCoordinates(projCoords, faceParams, atlasSize, atlasCoords, shadowMin, shadowMax);

    float finalBias = CalculateSlopeBias(worldNormal, normalize(lightToFrag), L.ShadowBias);
    float currentDepth = projCoords.z - finalBias;

    return SampleShadowPCF(_ShadowAtlas, atlasCoords, shadowMin, shadowMax,
                           currentDepth, L.ShadowQuality, L.ShadowStrength, 1.5);
}

float SampleSpotShadow(LightSample L, int shadowSlot, vec3 worldPos, vec3 worldNormal)
{
    vec4 atlasParams = _SpotShadowAtlasParams[shadowSlot];
    if (atlasParams.z <= 0.0) return 0.0;

    vec3 worldPosBiased = worldPos + normalize(worldNormal) * L.ShadowNormalBias;
    vec4 lightSpacePos = _SpotShadowMatrices[shadowSlot] * vec4(worldPosBiased, 1.0);
    vec3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
    projCoords = projCoords * 0.5 + 0.5;

    if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
        return 0.0;

    float atlasSize = _ShadowAtlasSize.x;
    vec2 atlasCoords, shadowMin, shadowMax;
    GetAtlasCoordinates(projCoords, atlasParams, atlasSize, atlasCoords, shadowMin, shadowMax);

    float finalBias = CalculateSlopeBias(worldNormal, L.Direction, L.ShadowBias);
    float currentDepth = projCoords.z - finalBias;

    return SampleShadowPCF(_ShadowAtlas, atlasCoords, shadowMin, shadowMax,
                           currentDepth, L.ShadowQuality, L.ShadowStrength, 1.5);
}

// ============================================================
//  Per-light evaluation (BVH leaf -> radiance)
// ============================================================

vec3 EvaluateLocalLight(LightSample L, vec3 worldPos, vec3 worldNormal, vec3 viewDir,
                        vec3 albedo, float metallic, float roughness, float ao, vec3 F0)
{
    // BVH only emits point + spot leaves; directional has its own path. The leaf-level sphere
    // test in LBVH_Next has already rejected fragments past Range, so we don't repeat it here.
    //
    // Two remaining rejections before PBR:
    //   1) Spot cone reject (outer-cone-cosine test before any GGX work).
    //   2) Backface reject (NdotL <= 0 contributes 0; skip GGX/Smith/Fresnel/shadow sampling).
    vec3 lightToPixel = worldPos - L.Position;
    float dist2 = dot(lightToPixel, lightToPixel);
    float dist = sqrt(dist2);
    vec3 lightDir = -lightToPixel * (1.0 / max(dist, 1e-6));

    // Spot pre-check: if outside the outer cone the smoothstep returns 0 anyway. The axis cosine
    // is reused below for the cone falloff, so normalize the direction only once.
    float spotAxisCos = 0.0;
    if (L.Type == 2) {
        spotAxisCos = dot(normalize(L.Direction), -lightDir);
        if (spotAxisCos <= L.SpotCos) return vec3(0.0);
    }

    float NdotL = dot(worldNormal, lightDir);
    if (NdotL <= 0.0) return vec3(0.0);

    // Physical inverse-square with smooth window cutoff.
    //   1 / d^2   pure inverse-square in absolute world units
    //   (1 - (d/r)^4)^2   smooth cutoff to 0 at d == Range
    // Range here is purely the cutoff distance, not a scale Intensity is what you tune for
    // visual brightness, in roughly physical units.
    float invR2 = 1.0 / (L.Range * L.Range);
    float factor = dist2 * invR2;            // (d / r)^2
    float window = clamp(1.0 - factor * factor, 0.0, 1.0);
    window *= window;                          // (1 - (d/r)^4)^2
    float invSqr = 1.0 / max(dist2, 0.01);     // 1/d^2 with origin guard
    float attenuation = invSqr * window;

    if (L.Type == 2)
        attenuation *= smoothstep(L.SpotCos, L.InnerSpotCos, spotAxisCos);

    if (attenuation <= 0.0001) return vec3(0.0);

    vec3 halfDir = normalize(lightDir + viewDir);
    vec3 radiance = L.Color * (L.Intensity * 8.0) * attenuation;

    float NdotV = abs(dot(worldNormal, viewDir));
    float LdotH = max(dot(lightDir, halfDir), 0.0);

    float NDF = DistributionGGX(worldNormal, halfDir, roughness);
    float G = GeometrySmith(worldNormal, viewDir, lightDir, roughness);
    vec3 F = FresnelSchlick(LdotH, F0);

    vec3 kS = F;
    vec3 kD = (vec3(1.0) - kS) * (1.0 - metallic);

    vec3 numerator = NDF * G * F;
    float denominator = 4.0 * NdotV * NdotL + 0.0001;
    vec3 specular = numerator / denominator;

    float diffuseTerm = DisneyDiffuse(NdotV, NdotL, LdotH, roughness);
    vec3 diffuse = kD * albedo * diffuseTerm;

    float shadowFactor;
#ifdef SG_NO_SHADOWS
    shadowFactor = 1.0;
#else
    float shadow = 0.0;
    if (L.ShadowEnabled != 0 && L.ShadowSlot >= 0) {
        if (L.Type == 1)
            shadow = SamplePointShadow(L, L.ShadowSlot, worldPos, worldNormal);
        else
            shadow = SampleSpotShadow(L, L.ShadowSlot, worldPos, worldNormal);
    }
    shadowFactor = 1.0 - shadow;
#endif

    return (diffuse + specular) * radiance * NdotL * shadowFactor * ao;
}

vec3 EvaluateLocalLightAniso(LightSample L, vec3 worldPos, vec3 worldNormal, vec3 viewDir,
                              vec3 worldTangent, vec3 worldBitangent,
                              vec3 albedo, float metallic, float mt, float mb,
                              float perceptualRoughness, float ao, vec3 F0)
{
    // BVH leaf already culled fragments past Range; skip the redundant dist check.
    vec3 lightToPixel = worldPos - L.Position;
    float dist2 = dot(lightToPixel, lightToPixel);
    float dist = sqrt(dist2);
    vec3 lightDir = -lightToPixel * (1.0 / max(dist, 1e-6));

    float spotAxisCos = 0.0;
    if (L.Type == 2) {
        spotAxisCos = dot(normalize(L.Direction), -lightDir);
        if (spotAxisCos <= L.SpotCos) return vec3(0.0);
    }

    float NdotL = dot(worldNormal, lightDir);
    if (NdotL <= 0.0) return vec3(0.0);

    // Physical inverse-square + smooth window cutoff. See EvaluateLocalLight.
    float invR2 = 1.0 / (L.Range * L.Range);
    float factor = dist2 * invR2;
    float window = clamp(1.0 - factor * factor, 0.0, 1.0);
    window *= window;
    float invSqr = 1.0 / max(dist2, 0.01);
    float attenuation = invSqr * window;

    if (L.Type == 2)
        attenuation *= smoothstep(L.SpotCos, L.InnerSpotCos, spotAxisCos);

    if (attenuation <= 0.0001) return vec3(0.0);

    vec3 halfDir = normalize(lightDir + viewDir);
    float NdotV = abs(dot(worldNormal, viewDir));
    float LdotH = max(dot(lightDir, halfDir), 0.0);
    float NdotH = max(dot(worldNormal, halfDir), 0.0);

    float TdotH = dot(worldTangent, halfDir);
    float BdotH = dot(worldBitangent, halfDir);
    float TdotV = dot(worldTangent, viewDir);
    float BdotV = dot(worldBitangent, viewDir);
    float TdotL = dot(worldTangent, lightDir);
    float BdotL = dot(worldBitangent, lightDir);

    float D = DistributionGGXAniso(TdotH, BdotH, NdotH, mt, mb);
    float V = GeometrySmithAniso(TdotV, BdotV, NdotV, TdotL, BdotL, NdotL, mt, mb);
    vec3 F = FresnelSchlick(LdotH, F0);

    float specularTerm = max(0.0, (V * D) * PI * NdotL);
    vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);

    float diffuseTerm = DisneyDiffuse(NdotV, NdotL, LdotH, perceptualRoughness) * NdotL;

    float shadowFactor;
#ifdef SG_NO_SHADOWS
    shadowFactor = 1.0;
#else
    float shadow = 0.0;
    if (L.ShadowEnabled != 0 && L.ShadowSlot >= 0) {
        if (L.Type == 1)
            shadow = SamplePointShadow(L, L.ShadowSlot, worldPos, worldNormal);
        else
            shadow = SampleSpotShadow(L, L.ShadowSlot, worldPos, worldNormal);
    }
    shadowFactor = 1.0 - shadow;
#endif

    vec3 lightColor = L.Color * (L.Intensity * 8.0) * attenuation * shadowFactor;
    return (kD * albedo * diffuseTerm + specularTerm * F) * lightColor * ao;
}

// ============================================================
//  Directional evaluator
// ============================================================

vec3 EvaluateDirectional(vec3 worldPos, vec3 worldNormal, vec3 viewDir,
                         vec3 albedo, float metallic, float roughness, float ao, vec3 F0)
{
    if (_DirectionalLightEnabled == 0) return vec3(0.0);

    // Prowl convention: a directional light's Transform.Forward points FROM the surface TO
    // the sun, so it already IS the surface-to-light "L" vector. The shadow camera flips it
    // separately (it wants the shining direction). Don't negate here.
    vec3 lightDir = normalize(_DirectionalLightDirection);
    vec3 halfDir = normalize(lightDir + viewDir);
    vec3 radiance = _DirectionalLightColor * (_DirectionalLightIntensity * 8.0);

    float NdotL = max(dot(worldNormal, lightDir), 0.0);
    float NdotV = abs(dot(worldNormal, viewDir));
    float LdotH = max(dot(lightDir, halfDir), 0.0);

    float NDF = DistributionGGX(worldNormal, halfDir, roughness);
    float G = GeometrySmith(worldNormal, viewDir, lightDir, roughness);
    vec3 F = FresnelSchlick(LdotH, F0);

    vec3 kS = F;
    vec3 kD = (vec3(1.0) - kS) * (1.0 - metallic);

    vec3 numerator = NDF * G * F;
    float denominator = 4.0 * NdotV * NdotL + 0.0001;
    vec3 specular = numerator / denominator;

    float diffuseTerm = DisneyDiffuse(NdotV, NdotL, LdotH, roughness);
    vec3 diffuse = kD * albedo * diffuseTerm;

    float shadowFactor;
#ifdef SG_NO_SHADOWS
    shadowFactor = 1.0;
#else
    float shadow = (_DirectionalLightShadowEnabled != 0)
        ? SampleDirectionalShadow(worldPos, worldNormal) : 0.0;
    shadowFactor = 1.0 - shadow;
#endif

    return (diffuse + specular) * radiance * NdotL * shadowFactor * ao;
}

vec3 EvaluateDirectionalAniso(vec3 worldPos, vec3 worldNormal, vec3 viewDir,
                              vec3 worldTangent, vec3 worldBitangent,
                              vec3 albedo, float metallic, float mt, float mb,
                              float perceptualRoughness, float ao, vec3 F0)
{
    if (_DirectionalLightEnabled == 0) return vec3(0.0);

    vec3 lightDir = normalize(_DirectionalLightDirection);
    vec3 halfDir = normalize(lightDir + viewDir);

    float NdotL = max(dot(worldNormal, lightDir), 0.0);
    float NdotV = abs(dot(worldNormal, viewDir));
    float LdotH = max(dot(lightDir, halfDir), 0.0);
    float NdotH = max(dot(worldNormal, halfDir), 0.0);

    float TdotH = dot(worldTangent, halfDir);
    float BdotH = dot(worldBitangent, halfDir);
    float TdotV = dot(worldTangent, viewDir);
    float BdotV = dot(worldBitangent, viewDir);
    float TdotL = dot(worldTangent, lightDir);
    float BdotL = dot(worldBitangent, lightDir);

    float D = DistributionGGXAniso(TdotH, BdotH, NdotH, mt, mb);
    float V = GeometrySmithAniso(TdotV, BdotV, NdotV, TdotL, BdotL, NdotL, mt, mb);
    vec3 F = FresnelSchlick(LdotH, F0);

    float specularTerm = max(0.0, (V * D) * PI * NdotL);
    vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);
    float diffuseTerm = DisneyDiffuse(NdotV, NdotL, LdotH, perceptualRoughness) * NdotL;

    float shadowFactor;
#ifdef SG_NO_SHADOWS
    shadowFactor = 1.0;
#else
    float shadow = (_DirectionalLightShadowEnabled != 0)
        ? SampleDirectionalShadow(worldPos, worldNormal) : 0.0;
    shadowFactor = 1.0 - shadow;
#endif

    vec3 lightColor = _DirectionalLightColor * (_DirectionalLightIntensity * 8.0) * shadowFactor;
    return (kD * albedo * diffuseTerm + specularTerm * F) * lightColor * ao;
}

// ============================================================
//  Forward lighting entry points (BVH-driven)
// ============================================================

vec3 CalculateForwardLighting(vec3 worldPos, vec3 worldNormal, vec3 viewDir,
                              vec3 albedo, float metallic, float roughness, float ao)
{
    roughness = ApplySpecularAA(roughness, worldNormal);
    vec3 F0 = mix(vec3(0.04), albedo, metallic);

    vec3 totalLight = EvaluateDirectional(worldPos, worldNormal, viewDir, albedo, metallic, roughness, ao, F0);

    // Static tree.
    if (_StaticLightRoot >= 0) {
        LBVH_Iter it;
        LBVH_Begin(it, _StaticLightRoot);
        int slot;
        while ((slot = LBVH_Next(it, _StaticLightNodes, _StaticNodeTexSize, _StaticNodeTexShift, worldPos)) >= 0) {
            LightSample L = LBVH_FetchLight(_StaticLightData, _StaticLightTexSize, _StaticLightTexShift, slot);
            totalLight += EvaluateLocalLight(L, worldPos, worldNormal, viewDir, albedo, metallic, roughness, ao, F0);
        }
    }

    // Dynamic tree.
    if (_DynamicLightRoot >= 0) {
        LBVH_Iter it;
        LBVH_Begin(it, _DynamicLightRoot);
        int slot;
        while ((slot = LBVH_Next(it, _DynamicLightNodes, _DynamicNodeTexSize, _DynamicNodeTexShift, worldPos)) >= 0) {
            LightSample L = LBVH_FetchLight(_DynamicLightData, _DynamicLightTexSize, _DynamicLightTexShift, slot);
            totalLight += EvaluateLocalLight(L, worldPos, worldNormal, viewDir, albedo, metallic, roughness, ao, F0);
        }
    }

    return totalLight;
}

vec3 CalculateForwardLightingAniso(vec3 worldPos, vec3 worldNormal, vec3 viewDir,
                                    vec3 worldTangent, vec3 worldBitangent,
                                    vec3 albedo, float metallic,
                                    float roughness, float anisotropy, float ao)
{
    roughness = ApplySpecularAA(roughness, worldNormal);
    float roughnessT = roughness * (1.0 + anisotropy);
    float roughnessB = roughness * (1.0 - anisotropy);
    float mt = roughnessT * roughnessT;
    float mb = roughnessB * roughnessB;

    vec3 F0 = mix(vec3(0.04), albedo, metallic);

    vec3 totalLight = EvaluateDirectionalAniso(worldPos, worldNormal, viewDir, worldTangent, worldBitangent,
                                                albedo, metallic, mt, mb, roughness, ao, F0);

    if (_StaticLightRoot >= 0) {
        LBVH_Iter it;
        LBVH_Begin(it, _StaticLightRoot);
        int slot;
        while ((slot = LBVH_Next(it, _StaticLightNodes, _StaticNodeTexSize, _StaticNodeTexShift, worldPos)) >= 0) {
            LightSample L = LBVH_FetchLight(_StaticLightData, _StaticLightTexSize, _StaticLightTexShift, slot);
            totalLight += EvaluateLocalLightAniso(L, worldPos, worldNormal, viewDir, worldTangent, worldBitangent,
                                                   albedo, metallic, mt, mb, roughness, ao, F0);
        }
    }

    if (_DynamicLightRoot >= 0) {
        LBVH_Iter it;
        LBVH_Begin(it, _DynamicLightRoot);
        int slot;
        while ((slot = LBVH_Next(it, _DynamicLightNodes, _DynamicNodeTexSize, _DynamicNodeTexShift, worldPos)) >= 0) {
            LightSample L = LBVH_FetchLight(_DynamicLightData, _DynamicLightTexSize, _DynamicLightTexShift, slot);
            totalLight += EvaluateLocalLightAniso(L, worldPos, worldNormal, viewDir, worldTangent, worldBitangent,
                                                   albedo, metallic, mt, mb, roughness, ao, F0);
        }
    }

    return totalLight;
}

// ============================================================
//  Local light with translucency (shared attenuation + shadow)
// ============================================================

vec3 EvaluateLocalLightTranslucent(LightSample L, vec3 worldPos, vec3 worldNormal, vec3 viewDir,
                                    vec3 albedo, float metallic, float roughness, float ao, vec3 F0,
                                    float translucency, float scatterPower, float scatterDist, float scatterScale)
{
    vec3 lightToPixel = worldPos - L.Position;
    float dist2 = dot(lightToPixel, lightToPixel);
    float dist = sqrt(dist2);
    vec3 lightDir = -lightToPixel * (1.0 / max(dist, 1e-6));

    float spotAxisCos = 0.0;
    if (L.Type == 2) {
        spotAxisCos = dot(normalize(L.Direction), -lightDir);
        if (spotAxisCos <= L.SpotCos) return vec3(0.0);
    }

    float invR2 = 1.0 / (L.Range * L.Range);
    float factor = dist2 * invR2;
    float window = clamp(1.0 - factor * factor, 0.0, 1.0);
    window *= window;
    float invSqr = 1.0 / max(dist2, 0.01);
    float attenuation = invSqr * window;

    if (L.Type == 2)
        attenuation *= smoothstep(L.SpotCos, L.InnerSpotCos, spotAxisCos);

    if (attenuation <= 0.0001) return vec3(0.0);

    float NdotL = dot(worldNormal, lightDir);

    // Only sample the shadow atlas when something will actually use it: the front-lit PBR
    // term (NdotL > 0) or the translucency term. A back-facing opaque fragment skips the
    // full PCF tap set entirely.
    float shadowFactor = 1.0;
#ifndef SG_NO_SHADOWS
    if ((NdotL > 0.0 || translucency > 0.0) && L.ShadowEnabled != 0 && L.ShadowSlot >= 0) {
        float shadow = (L.Type == 1)
            ? SamplePointShadow(L, L.ShadowSlot, worldPos, worldNormal)
            : SampleSpotShadow(L, L.ShadowSlot, worldPos, worldNormal);
        shadowFactor = 1.0 - shadow;
    }
#endif

    vec3 radiance = L.Color * (L.Intensity * 8.0) * attenuation;
    vec3 result = vec3(0.0);

    // PBR (front-lit only)
    if (NdotL > 0.0) {
        vec3 halfDir = normalize(lightDir + viewDir);
        float NdotV = abs(dot(worldNormal, viewDir));
        float LdotH = max(dot(lightDir, halfDir), 0.0);

        float NDF = DistributionGGX(worldNormal, halfDir, roughness);
        float G = GeometrySmith(worldNormal, viewDir, lightDir, roughness);
        vec3 F = FresnelSchlick(LdotH, F0);
        vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);
        vec3 specular = (NDF * G * F) / (4.0 * NdotV * NdotL + 0.0001);
        float diffuseTerm = DisneyDiffuse(NdotV, NdotL, LdotH, roughness);
        result = (kD * albedo * diffuseTerm + specular) * radiance * NdotL * shadowFactor * ao;
    }

    // Translucency (uses same attenuation and shadow, works regardless of NdotL)
    if (translucency > 0.0) {
        vec3 scatter = CalculateTranslucency(lightDir, viewDir, worldNormal,
                           translucency, scatterPower, scatterDist, scatterScale,
                           L.Color * L.Intensity * attenuation);
        result += scatter * albedo * shadowFactor;
    }

    return result;
}

// ============================================================
//  Forward lighting with translucency (single-pass, unified)
//  PBR + translucency share the same attenuation and shadow.
// ============================================================

vec3 CalculateForwardLighting(vec3 worldPos, vec3 worldNormal, vec3 viewDir,
                              vec3 albedo, float metallic, float roughness, float ao,
                              float translucency, float scatterPower,
                              float scatterDist, float scatterScale)
{
    roughness = ApplySpecularAA(roughness, worldNormal);
    vec3 F0 = mix(vec3(0.04), albedo, metallic);
    bool hasTrans = translucency > 0.0;

    vec3 totalLight = vec3(0.0);

    // ---- Directional light ----
    if (_DirectionalLightEnabled != 0) {
        vec3 lightDir = normalize(_DirectionalLightDirection);
        vec3 radiance = _DirectionalLightColor * (_DirectionalLightIntensity * 8.0);

        float shadowFactor;
#ifdef SG_NO_SHADOWS
        shadowFactor = 1.0;
#else
        float shadow = (_DirectionalLightShadowEnabled != 0)
            ? SampleDirectionalShadow(worldPos, worldNormal) : 0.0;
        shadowFactor = 1.0 - shadow;
#endif

        // PBR
        float NdotL = max(dot(worldNormal, lightDir), 0.0);
        if (NdotL > 0.0) {
            vec3 halfDir = normalize(lightDir + viewDir);
            float NdotV = abs(dot(worldNormal, viewDir));
            float LdotH = max(dot(lightDir, halfDir), 0.0);

            float NDF = DistributionGGX(worldNormal, halfDir, roughness);
            float G = GeometrySmith(worldNormal, viewDir, lightDir, roughness);
            vec3 F = FresnelSchlick(LdotH, F0);
            vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);
            vec3 specular = (NDF * G * F) / (4.0 * NdotV * NdotL + 0.0001);
            float diffuseTerm = DisneyDiffuse(NdotV, NdotL, LdotH, roughness);
            totalLight += (kD * albedo * diffuseTerm + specular) * radiance * NdotL * shadowFactor * ao;
        }

        // Translucency (same shadow, no distance attenuation for directional)
        if (hasTrans) {
            vec3 scatter = CalculateTranslucency(lightDir, viewDir, worldNormal,
                               translucency, scatterPower, scatterDist, scatterScale,
                               _DirectionalLightColor * _DirectionalLightIntensity);
            totalLight += scatter * albedo * shadowFactor;
        }
    }

    // ---- Static BVH ----
    if (_StaticLightRoot >= 0) {
        LBVH_Iter it;
        LBVH_Begin(it, _StaticLightRoot);
        int slot;
        while ((slot = LBVH_Next(it, _StaticLightNodes, _StaticNodeTexSize, _StaticNodeTexShift, worldPos)) >= 0) {
            LightSample L = LBVH_FetchLight(_StaticLightData, _StaticLightTexSize, _StaticLightTexShift, slot);
            totalLight += EvaluateLocalLightTranslucent(L, worldPos, worldNormal, viewDir,
                              albedo, metallic, roughness, ao, F0,
                              translucency, scatterPower, scatterDist, scatterScale);
        }
    }

    // ---- Dynamic BVH ----
    if (_DynamicLightRoot >= 0) {
        LBVH_Iter it;
        LBVH_Begin(it, _DynamicLightRoot);
        int slot;
        while ((slot = LBVH_Next(it, _DynamicLightNodes, _DynamicNodeTexSize, _DynamicNodeTexShift, worldPos)) >= 0) {
            LightSample L = LBVH_FetchLight(_DynamicLightData, _DynamicLightTexSize, _DynamicLightTexShift, slot);
            totalLight += EvaluateLocalLightTranslucent(L, worldPos, worldNormal, viewDir,
                              albedo, metallic, roughness, ao, F0,
                              translucency, scatterPower, scatterDist, scatterScale);
        }
    }

    return totalLight;
}

// ============================================================
//  Ambient lighting
// ============================================================

vec3 CalculateAmbient(vec3 worldNormal)
{
    vec3 ambient = vec3(0.0);
    ambient += _AmbientColor.rgb * _AmbientMode.x;

    float upDot = dot(worldNormal, vec3(0.0, 1.0, 0.0));
    ambient += mix(_AmbientGroundColor.rgb, _AmbientSkyColor.rgb, upDot * 0.5 + 0.5) * _AmbientMode.y;

    return ambient;
}

// ============================================================
//  Light-probe spherical harmonics (per-object, set by the pipeline for dynamic objects)
// ============================================================

// Evaluate per-object SH-L2 (the standard ShadeSH9) -> diffuse irradiance E/pi for a normal.
vec3 ShadeSH9(vec3 n)
{
    vec4 nrm = vec4(n, 1.0);
    vec3 x;
    x.r = dot(prowl_SHAr, nrm);
    x.g = dot(prowl_SHAg, nrm);
    x.b = dot(prowl_SHAb, nrm);

    vec4 vB = n.xyzz * n.yzzx;   // (xy, yz, z^2, zx)
    vec3 x1;
    x1.r = dot(prowl_SHBr, vB);
    x1.g = dot(prowl_SHBg, vB);
    x1.b = dot(prowl_SHBb, vB);

    float vC = n.x * n.x - n.y * n.y;
    vec3 x2 = prowl_SHC.rgb * vC;

    return max(x + x1 + x2, vec3(0.0));
}

// ============================================================
//  Fog
// ============================================================

vec3 ApplyFog(vec3 color, vec3 worldPos)
{
    if (_FogStates.x + _FogStates.y + _FogStates.z < 0.5)
        return color;

    float fogCoord = length(worldPos - _WorldSpaceCameraPos.xyz);
    float prowlFog = 0.0;
    prowlFog += (fogCoord * _FogParams.z + _FogParams.w) * _FogStates.x;
    prowlFog += exp2(-fogCoord * _FogParams.y) * _FogStates.y;
    prowlFog += exp2(-fogCoord * fogCoord * _FogParams.x * _FogParams.x) * _FogStates.z;
    return mix(_FogColor.rgb, color, clamp(prowlFog, 0.0, 1.0));
}

#endif
// =========================================================================
//  Pass "DefaultUI" - Fragment
// =========================================================================

layout (location = 0) out vec4 fragColor;

layout(location = 1) in vec2 texCoord0;
layout(location = 2) in vec3 worldPos;
layout(location = 3) in vec4 vColor;

// Per-item rounded-rect clip (RectMask). _ClipToLocal maps the fragment's world position
// into the mask's local space (so the clip follows rotation/scale); the fragment is tested
// against _ClipRect with _ClipRadius rounded corners and a _ClipSoftness edge.
float uiClipCoverage(vec3 worldPosition)
{
	if (_ClipEnable < 0.5) return 1.0;
	vec2 p = (_ClipToLocal * vec4(worldPosition, 1.0)).xy;
	vec2 c = (_ClipRect.xy + _ClipRect.zw) * 0.5;
	vec2 e = (_ClipRect.zw - _ClipRect.xy) * 0.5 - vec2(_ClipRadius);
	vec2 d = abs(p - c) - e;
	float dist = length(max(d, vec2(0.0))) + min(max(d.x, d.y), 0.0) - _ClipRadius;
	float soft = max(_ClipSoftness, max(fwidth(dist), 1e-4));
	return clamp(0.5 - dist / soft, 0.0, 1.0);
}

void main()
{
	vec4 albedo = texture(_MainTex, texCoord0) * vColor * _MainColor;
	fragColor = albedo * uiClipCoverage(worldPos);
}
