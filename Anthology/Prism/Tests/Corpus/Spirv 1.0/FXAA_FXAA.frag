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
    vec2 _Resolution;
    float _EdgeThresholdMin;
    float _EdgeThresholdMax;
    float _SubpixelQuality;
};

layout(set = 2, binding = 1) uniform sampler2D _MainTex;

layout(location = 0) out vec4 OutputColor;

layout(location = 1) in vec2 TexCoords;

        float GetLuminance(vec3 color) {
            return dot(color, vec3(0.299, 0.587, 0.114));
        }

        vec3 FXAA311(vec2 texCoord) {
            float quality[12] = float[12](1.0, 1.0, 1.0, 1.0, 1.0, 1.5, 2.0, 2.0, 2.0, 2.0, 4.0, 8.0);
            int iterations = 12;

            vec2 inverseScreenSize = 1.0 / _Resolution;
            ivec2 texelCoord = ivec2(texCoord * _Resolution);

            vec3 colorCenter = texelFetch(_MainTex, texelCoord, 0).rgb;

            float lumaCenter = GetLuminance(colorCenter);
            float lumaDown   = GetLuminance(texelFetchOffset(_MainTex, texelCoord, 0, ivec2( 0, -1)).rgb);
            float lumaUp     = GetLuminance(texelFetchOffset(_MainTex, texelCoord, 0, ivec2( 0,  1)).rgb);
            float lumaLeft   = GetLuminance(texelFetchOffset(_MainTex, texelCoord, 0, ivec2(-1,  0)).rgb);
            float lumaRight  = GetLuminance(texelFetchOffset(_MainTex, texelCoord, 0, ivec2( 1,  0)).rgb);

            float lumaMin = min(lumaCenter, min(min(lumaDown, lumaUp), min(lumaLeft, lumaRight)));
            float lumaMax = max(lumaCenter, max(max(lumaDown, lumaUp), max(lumaLeft, lumaRight)));

            float lumaRange = lumaMax - lumaMin;

            // Early exit if no edge detected
            if (lumaRange < max(_EdgeThresholdMin, lumaMax * _EdgeThresholdMax)) {
                return colorCenter;
            }

            // Sample corners
            float lumaDownLeft  = GetLuminance(texelFetchOffset(_MainTex, texelCoord, 0, ivec2(-1, -1)).rgb);
            float lumaUpRight   = GetLuminance(texelFetchOffset(_MainTex, texelCoord, 0, ivec2( 1,  1)).rgb);
            float lumaUpLeft    = GetLuminance(texelFetchOffset(_MainTex, texelCoord, 0, ivec2(-1,  1)).rgb);
            float lumaDownRight = GetLuminance(texelFetchOffset(_MainTex, texelCoord, 0, ivec2( 1, -1)).rgb);

            float lumaDownUp    = lumaDown + lumaUp;
            float lumaLeftRight = lumaLeft + lumaRight;

            float lumaLeftCorners  = lumaDownLeft  + lumaUpLeft;
            float lumaDownCorners  = lumaDownLeft  + lumaDownRight;
            float lumaRightCorners = lumaDownRight + lumaUpRight;
            float lumaUpCorners    = lumaUpRight   + lumaUpLeft;

            // Detect horizontal/vertical edge
            float edgeHorizontal = abs(-2.0 * lumaLeft   + lumaLeftCorners ) +
                                   abs(-2.0 * lumaCenter + lumaDownUp      ) * 2.0 +
                                   abs(-2.0 * lumaRight  + lumaRightCorners);
            float edgeVertical   = abs(-2.0 * lumaUp     + lumaUpCorners   ) +
                                   abs(-2.0 * lumaCenter + lumaLeftRight   ) * 2.0 +
                                   abs(-2.0 * lumaDown   + lumaDownCorners );

            bool isHorizontal = (edgeHorizontal >= edgeVertical);

            float luma1 = isHorizontal ? lumaDown : lumaLeft;
            float luma2 = isHorizontal ? lumaUp : lumaRight;
            float gradient1 = luma1 - lumaCenter;
            float gradient2 = luma2 - lumaCenter;

            bool is1Steepest = abs(gradient1) >= abs(gradient2);
            float gradientScaled = 0.25 * max(abs(gradient1), abs(gradient2));

            float stepLength = isHorizontal ? inverseScreenSize.y : inverseScreenSize.x;

            float lumaLocalAverage = 0.0;

            if (is1Steepest) {
                stepLength = -stepLength;
                lumaLocalAverage = 0.5 * (luma1 + lumaCenter);
            } else {
                lumaLocalAverage = 0.5 * (luma2 + lumaCenter);
            }

            vec2 currentUv = texCoord;
            if (isHorizontal) {
                currentUv.y += stepLength * 0.5;
            } else {
                currentUv.x += stepLength * 0.5;
            }

            vec2 offset = isHorizontal ? vec2(inverseScreenSize.x, 0.0) : vec2(0.0, inverseScreenSize.y);

            vec2 uv1 = currentUv - offset;
            vec2 uv2 = currentUv + offset;

            float lumaEnd1 = GetLuminance(texture(_MainTex, uv1).rgb);
            float lumaEnd2 = GetLuminance(texture(_MainTex, uv2).rgb);
            lumaEnd1 -= lumaLocalAverage;
            lumaEnd2 -= lumaLocalAverage;

            bool reached1 = abs(lumaEnd1) >= gradientScaled;
            bool reached2 = abs(lumaEnd2) >= gradientScaled;
            bool reachedBoth = reached1 && reached2;

            if (!reached1) {
                uv1 -= offset;
            }
            if (!reached2) {
                uv2 += offset;
            }

            if (!reachedBoth) {
                for (int i = 2; i < iterations; i++) {
                    if (!reached1) {
                        lumaEnd1 = GetLuminance(texture(_MainTex, uv1).rgb);
                        lumaEnd1 = lumaEnd1 - lumaLocalAverage;
                    }
                    if (!reached2) {
                        lumaEnd2 = GetLuminance(texture(_MainTex, uv2).rgb);
                        lumaEnd2 = lumaEnd2 - lumaLocalAverage;
                    }

                    reached1 = abs(lumaEnd1) >= gradientScaled;
                    reached2 = abs(lumaEnd2) >= gradientScaled;
                    reachedBoth = reached1 && reached2;

                    if (!reached1) {
                        uv1 -= offset * quality[i];
                    }
                    if (!reached2) {
                        uv2 += offset * quality[i];
                    }

                    if (reachedBoth) break;
                }
            }

            float distance1 = isHorizontal ? (texCoord.x - uv1.x) : (texCoord.y - uv1.y);
            float distance2 = isHorizontal ? (uv2.x - texCoord.x) : (uv2.y - texCoord.y);

            bool isDirection1 = distance1 < distance2;
            float distanceFinal = min(distance1, distance2);

            float edgeThickness = (distance1 + distance2);

            float pixelOffset = -distanceFinal / edgeThickness + 0.5;

            bool isLumaCenterSmaller = lumaCenter < lumaLocalAverage;

            bool correctVariation = ((isDirection1 ? lumaEnd1 : lumaEnd2) < 0.0) != isLumaCenterSmaller;

            float finalOffset = correctVariation ? pixelOffset : 0.0;

            // Sub-pixel antialiasing
            float lumaAverage = (1.0 / 12.0) * (2.0 * (lumaDownUp + lumaLeftRight) + lumaLeftCorners + lumaRightCorners);
            float subPixelOffset1 = clamp(abs(lumaAverage - lumaCenter) / lumaRange, 0.0, 1.0);
            float subPixelOffset2 = (-2.0 * subPixelOffset1 + 3.0) * subPixelOffset1 * subPixelOffset1;
            float subPixelOffsetFinal = subPixelOffset2 * subPixelOffset2 * _SubpixelQuality;

            finalOffset = max(finalOffset, subPixelOffsetFinal);

            // Compute final UV coordinates
            vec2 finalUv = texCoord;
            if (isHorizontal) {
                finalUv.y += finalOffset * stepLength;
            } else {
                finalUv.x += finalOffset * stepLength;
            }

            return texture(_MainTex, finalUv).rgb;
        }

        void main()
        {
            vec4 base = texture(_MainTex, TexCoords);
            vec3 color = FXAA311(TexCoords);
            OutputColor = vec4(color, base.a);
        }
