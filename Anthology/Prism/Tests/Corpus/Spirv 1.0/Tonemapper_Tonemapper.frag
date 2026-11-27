#version 460 core

// Variant selection: single tonemap operator baked in for this build (TONEMAP_ACES)
#define TONEMAP_ACES

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


layout(std140, set = 2, binding = 0) uniform PassUniforms
{
	float Contrast;
	float Saturation;
};

layout(set = 2, binding = 1) uniform sampler2D _MainTex;

layout(location = 0) out vec4 OutputColor;

layout(location = 1) in vec2 TexCoords;

#ifdef TONEMAP_MELON
		// Shifts red to Yellow, green to Yellow, blue to Cyan,
		vec3 HueShift(vec3 In)
		{
			float A = max(In.x, In.y);
			return vec3(A, max(A, In.z), In.z);
		}

		float Cmax(vec3 In)
		{
			return max(max(In.r, In.g), In.b);
		}

		// Made by TripleMelon
		vec3 MelonTonemap(vec3 color)
		{
			// remaps the colors to [0-1] range
			// tested to be as close ti ACES contrast levels as possible
			color = pow(color, vec3(1.56, 1.56, 1.56));
			color = color/(color + 0.84);

			// governs the transition to white for high color intensities
			float factor = Cmax(color) * 0.15; // multiply by 0.15 to get a similar look to ACES
			factor = factor / (factor + 1); // remaps the factor to [0-1] range
			factor *= factor; // smooths the transition to white

			// shift the hue for high intensities (for a more pleasing look).
			color = mix(color, HueShift(color), factor); // can be removed for more neutral colors
			color = mix(color, vec3(1.0, 1.0, 1.0), factor); // shift to white for high intensities

		    return color;
		}
#endif

#ifdef TONEMAP_ACES
		// from https://github.com/TheRealMJP/BakingLab/blob/master/BakingLab/ACES.hlsl
		const mat3 ACESInputMat = mat3
		(
		    0.59719, 0.35458, 0.04823,
		    0.07600, 0.90834, 0.01566,
		    0.02840, 0.13383, 0.83777
		);

		// ODT_SAT => XYZ => D60_2_D65 => sRGB
		const mat3 ACESOutputMat = mat3
		(
		     1.60475, -0.53108, -0.07367,
		    -0.10208,  1.10813, -0.00605,
		    -0.00327, -0.07276,  1.07602
		);

		vec3 RRTAndODTFit(vec3 v)
		{
		    vec3 a = v * (v + 0.0245786) - 0.000090537;
		    vec3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
		    return a / b;
		}

		vec3 ACESFitted(vec3 color)
		{
		    color = color * ACESInputMat;
		    color = RRTAndODTFit(color);
		    color = color * ACESOutputMat;
		    return color;
		}
#endif

#ifdef TONEMAP_ACES_SIMPLE
		vec3 ACESSimple(vec3 c)
		{
		    float a = 2.51;
		    float b = 0.03;
		    float y = 2.43;
		    float d = 0.59;
		    float e = 0.14;
		    return (c * (a * c + b)) / (c * (y * c + d) + e);
		}
#endif

#ifdef TONEMAP_AGX
		const mat3 agxTransform = mat3(
		    0.842479062253094, 0.0423282422610123, 0.0423756549057051,
		    0.0784335999999992, 0.878468636469772,  0.0784336,
		    0.0792237451477643, 0.0791661274605434, 0.879142973793104);

		const mat3 agxTransformInverse = mat3(
		     1.19687900512017,  -0.0528968517574562, -0.0529716355144438,
		    -0.0980208811401368, 1.15190312990417,   -0.0980434501171241,
		    -0.0990297440797205,-0.0989611768448433,  1.15107367264116);

		vec3 agxDefaultContrastApproximation(vec3 x)
		{
		    vec3 x2 = x * x;
		    vec3 x4 = x2 * x2;
		    return 15.5 * x4 * x2 - 40.14 * x4 * x + 31.96 * x4 - 6.868 * x2 * x + 0.4298 * x2 + 0.1191 * x - 0.00232;
		}

		float agxLuminance(vec3 c)
		{
		    return dot(c, vec3(0.2126729, 0.7151522, 0.0721750));
		}

		vec3 AgX(vec3 color)
		{
		    const float minEv = -12.47393;
		    const float maxEv = 4.026069;

		    color = agxTransform * color;
		    color = clamp(log2(max(color, vec3(1e-10))), minEv, maxEv);
		    color = (color - minEv) / (maxEv - minEv);
		    color = agxDefaultContrastApproximation(color);

		    // Punchy look
		    const vec3 slope = vec3(1.1);
		    const vec3 power = vec3(1.2);
		    const float sat = 1.3;
		    float luma = agxLuminance(color);
		    color = pow(max(color * slope, vec3(0.0)), power);
		    color = max(luma + sat * (color - luma), vec3(0.0));

		    // EOTF back to linear
		    color = agxTransformInverse * color;
		    color = pow(max(vec3(0.0), color), vec3(2.2));
		    return color;
		}
#endif

#ifdef TONEMAP_REINHARD_SIMPLE
		vec3 ReinhardSimple(vec3 color)
		{
			float exposure = 1.5;
			return color * (exposure / (1.0 + color / exposure));
		}
#endif

#ifdef TONEMAP_REINHARD_LUMA
		vec3 ReinhardLuma(vec3 color)
		{
			float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
			float toneMappedLuma = luma / (1.0 + luma);
			return color * (toneMappedLuma / max(luma, 1e-5));
		}
#endif

#ifdef TONEMAP_REINHARD_WHITE
		vec3 ReinhardWhitePreserving(vec3 color)
		{
			float white = 2.0;
			float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
			float toneMappedLuma = luma * (1.0 + luma / (white * white)) / (1.0 + luma);
			return color * (toneMappedLuma / max(luma, 1e-5));
		}
#endif

#ifdef TONEMAP_ROMBINDAHOUSE
		vec3 RomBinDaHouse(vec3 color)
		{
			return exp(-1.0 / (2.72 * color + 0.15));
		}
#endif

#ifdef TONEMAP_UNCHARTED2
		vec3 Uncharted2(vec3 color)
		{
			float A = 0.15;
			float B = 0.50;
			float C = 0.10;
			float D = 0.20;
			float E = 0.02;
			float F = 0.30;
			float W = 11.2;
			float exposure = 2.0;
			color *= exposure;
			color = ((color * (A * color + C * B) + D * E) / (color * (A * color + B) + D * F)) - E / F;
			float white = ((W * (A * W + C * B) + D * E) / (W * (A * W + B) + D * F)) - E / F;
			return color / white;
		}
#endif

		vec3 Tonemap(vec3 color)
		{
#ifdef TONEMAP_MELON
			color = MelonTonemap(color);
#endif
#ifdef TONEMAP_ACES
			color = ACESFitted(color);
#endif
#ifdef TONEMAP_ACES_SIMPLE
			color = ACESSimple(color);
#endif
#ifdef TONEMAP_AGX
			color = AgX(color);
#endif
#ifdef TONEMAP_REINHARD_SIMPLE
			color = ReinhardSimple(color);
#endif
#ifdef TONEMAP_REINHARD_LUMA
			color = ReinhardLuma(color);
#endif
#ifdef TONEMAP_REINHARD_WHITE
			color = ReinhardWhitePreserving(color);
#endif
#ifdef TONEMAP_ROMBINDAHOUSE
			color = RomBinDaHouse(color);
#endif
#ifdef TONEMAP_UNCHARTED2
			color = Uncharted2(color);
#endif

		    // Clamp to [0, 1] (also acts as the None/Clamp tonemap when no keyword set)
		  	color = clamp(color, 0.0, 1.0);

			return color;
		}

		mat4 contrastMatrix()
		{
			float t = (1.0 - Contrast) / 2.0;

		    return mat4(Contrast, 0, 0, 0,
		                0, Contrast, 0, 0,
		                0, 0, Contrast, 0,
		                t, t, t, 1);

		}


		mat4 saturationMatrix()
		{
		    vec3 luminance = vec3(0.3086, 0.6094, 0.0820);

		    float oneMinusSat = 1.0 - Saturation;

		    vec3 red = vec3(luminance.x * oneMinusSat);
		    red+= vec3(Saturation, 0, 0);

		    vec3 green = vec3(luminance.y * oneMinusSat);
		    green += vec3(0, Saturation, 0);

		    vec3 blue = vec3(luminance.z * oneMinusSat);
		    blue += vec3(0, 0, Saturation);

		    return mat4(red,     0,
		                green,   0,
		                blue,    0,
		                0, 0, 0, 1);
		}

		void main()
		{
			vec4 base = texture(_MainTex, TexCoords);

			vec3 color = base.rgb;

			color = Tonemap(color);

			// Gamma Correct
            color = linearToGammaSpace(color);

			OutputColor = contrastMatrix() * saturationMatrix() * vec4(color, base.a);
		}
