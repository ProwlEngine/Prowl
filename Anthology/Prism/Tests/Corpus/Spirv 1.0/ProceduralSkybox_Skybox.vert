#version 460 core

// ============================================================
// ShaderVariables - global per-frame data
// ============================================================

layout(std140, set = 0, binding = 0) uniform GlobalUniforms
{
    // Camera matrices
    mat4 prowl_MatV;
    mat4 prowl_MatIV;
    mat4 prowl_MatP;
    mat4 prowl_MatVP;
    mat4 prowl_PrevViewProj;
    mat4 prowl_MatIP;
    mat4 prowl_MatIVP;
    mat4 prowl_MatVP_NonJittered;

    // Camera parameters
    vec3 _WorldSpaceCameraPos;
    float _padding0;

    vec4 _ProjectionParams;
    vec4 _ScreenParams;
    vec2 _CameraJitter;
    vec2 _CameraPreviousJitter;

    // Time parameters
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

// ============================================================
// ProwlCG
// ============================================================

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

// ============================================================
// Pass uniforms (loose pass-body-local uniforms, not from Properties)
// ============================================================

layout(std140, set = 2, binding = 0) uniform PassUniforms
{
    vec2 Resolution;
    vec3 _SunDir;
    float fogDensity;
};

// ============================================================
// Pass body
// ============================================================

	layout(location = 0) in vec3 vertexPosition;

layout(location = 1) out vec3 vSkyColor;
layout(location = 2) out vec3 vDirection;
layout(location = 3) out vec3 vSunDir;

#define PI 3.141592
#define iSteps 16
#define jSteps 8

vec2 rsi(vec3 r0, vec3 rd, float sr) {
    // ray-sphere intersection that assumes
    // the sphere is centered at the origin.
    // No intersection when result.x > result.y
    float a = dot(rd, rd);
    float b = 2.0 * dot(rd, r0);
    float c = dot(r0, r0) - (sr * sr);
    float d = (b*b) - 4.0*a*c;
    if (d < 0.0) return vec2(1e5,-1e5);
    return vec2(
        (-b - sqrt(d))/(2.0*a),
        (-b + sqrt(d))/(2.0*a)
    );
}

vec3 atmosphere(vec3 r, vec3 r0, vec3 pSun, float iSun, float rPlanet, float rAtmos, vec3 kRlh, float kMie, float shRlh, float shMie, float g) {
    // Normalize the sun and view directions.
    pSun = normalize(pSun);
    r = normalize(r);

    // Calculate the step size of the primary ray.
    vec2 p = rsi(r0, r, rAtmos);
    if (p.x > p.y) return vec3(0,0,0);
    p.y = min(p.y, rsi(r0, r, rPlanet).x);
    float iStepSize = (p.y - p.x) / float(iSteps);

    // Initialize the primary ray time.
    float iTime = 0.0;

    // Initialize accumulators for Rayleigh and Mie scattering.
    vec3 totalRlh = vec3(0,0,0);
    vec3 totalMie = vec3(0,0,0);

    // Initialize optical depth accumulators for the primary ray.
    float iOdRlh = 0.0;
    float iOdMie = 0.0;

    // Calculate the Rayleigh and Mie phases.
    float mu = dot(r, pSun);
    float mumu = mu * mu;
    float gg = g * g;
    float pRlh = 3.0 / (16.0 * PI) * (1.0 + mumu);
    float pMie = 3.0 / (8.0 * PI) * ((1.0 - gg) * (mumu + 1.0)) / (pow(1.0 + gg - 2.0 * mu * g, 1.5) * (2.0 + gg));

    // Sample the primary ray.
    for (int i = 0; i < iSteps; i++) {

        // Calculate the primary ray sample position.
        vec3 iPos = r0 + r * (iTime + iStepSize * 0.5);

        // Calculate the height of the sample.
        float iHeight = length(iPos) - rPlanet;

        // Calculate the optical depth of the Rayleigh and Mie scattering for this step.
        float odStepRlh = exp(-iHeight / shRlh) * iStepSize;
        float odStepMie = exp(-iHeight / shMie) * iStepSize;

        // Accumulate optical depth.
        iOdRlh += odStepRlh;
        iOdMie += odStepMie;

        // Calculate the step size of the secondary ray.
        float jStepSize = rsi(iPos, pSun, rAtmos).y / float(jSteps);

        // Initialize the secondary ray time.
        float jTime = 0.0;

        // Initialize optical depth accumulators for the secondary ray.
        float jOdRlh = 0.0;
        float jOdMie = 0.0;

        // Sample the secondary ray.
        for (int j = 0; j < jSteps; j++) {

            // Calculate the secondary ray sample position.
            vec3 jPos = iPos + pSun * (jTime + jStepSize * 0.5);

            // Calculate the height of the sample.
            float jHeight = length(jPos) - rPlanet;

            // Accumulate the optical depth.
            jOdRlh += exp(-jHeight / shRlh) * jStepSize;
            jOdMie += exp(-jHeight / shMie) * jStepSize;

            // Increment the secondary ray time.
            jTime += jStepSize;
        }

        // Calculate attenuation.
        vec3 attn = exp(-(kMie * (iOdMie + jOdMie) + kRlh * (iOdRlh + jOdRlh)));

        // Accumulate scattering.
        totalRlh += odStepRlh * attn;
        totalMie += odStepMie * attn;

        // Increment the primary ray time.
        iTime += iStepSize;

    }

    // Calculate and return the final color.
    return iSun * (pRlh * kRlh * totalRlh + pMie * kMie * totalMie);
}

vec3 atmosphereNoMie(vec3 r, vec3 r0, vec3 pSun, float iSun, float rPlanet, float rAtmos, vec3 kRlh, float shRlh) {
    // Normalize the sun and view directions.
    pSun = normalize(pSun);
    r = normalize(r);

    // Calculate the step size of the primary ray.
    vec2 p = rsi(r0, r, rAtmos);
    if (p.x > p.y) return vec3(0,0,0);
    p.y = min(p.y, rsi(r0, r, rPlanet).x);
    float iStepSize = (p.y - p.x) / float(iSteps);

    // Initialize the primary ray time.
    float iTime = 0.0;

    // Initialize accumulators for Rayleigh.
    vec3 totalRlh = vec3(0,0,0);

    // Initialize optical depth accumulators for the primary ray.
    float iOdRlh = 0.0;

    // Calculate the Rayleigh phase.
    float mu = dot(r, pSun);
    float mumu = mu * mu;
    float pRlh = 3.0 / (16.0 * PI) * (1.0 + mumu);

    // Sample the primary ray.
    for (int i = 0; i < iSteps; i++) {

        // Calculate the primary ray sample position.
        vec3 iPos = r0 + r * (iTime + iStepSize * 0.5);

        // Calculate the height of the sample.
        float iHeight = length(iPos) - rPlanet;

        // Calculate the optical depth of the Rayleigh scattering for this step.
        float odStepRlh = exp(-iHeight / shRlh) * iStepSize;

        // Accumulate optical depth.
        iOdRlh += odStepRlh;

        // Calculate the step size of the secondary ray.
        float jStepSize = rsi(iPos, pSun, rAtmos).y / float(jSteps);

        // Initialize the secondary ray time.
        float jTime = 0.0;

        // Initialize optical depth accumulators for the secondary ray.
        float jOdRlh = 0.0;

        // Sample the secondary ray.
        for (int j = 0; j < jSteps; j++) {

            // Calculate the secondary ray sample position.
            vec3 jPos = iPos + pSun * (jTime + jStepSize * 0.5);

            // Calculate the height of the sample.
            float jHeight = length(jPos) - rPlanet;

            // Accumulate the optical depth.
            jOdRlh += exp(-jHeight / shRlh) * jStepSize;

            // Increment the secondary ray time.
            jTime += jStepSize;
        }

        // Calculate attenuation.
        vec3 attn = exp(-(kRlh * (iOdRlh + jOdRlh)));

        // Accumulate scattering.
        totalRlh += odStepRlh * attn;

        // Increment the primary ray time.
        iTime += iStepSize;

    }

    // Calculate and return the final color.
    return iSun * (pRlh * kRlh * totalRlh);
}

void main()
{
    // Remove translation from view matrix to keep skybox centered around camera
    mat4 viewNoTranslation = PROWL_MATRIX_V;
    viewNoTranslation[3][0] = 0.0;
    viewNoTranslation[3][1] = 0.0;
    viewNoTranslation[3][2] = 0.0;

    gl_Position = PROWL_MATRIX_P * viewNoTranslation * vec4(vertexPosition, 1.0);
    gl_Position.z = gl_Position.w;

    // Sky
    vDirection = vertexPosition;
    vSunDir = normalize(_SunDir);
    vSkyColor = atmosphere(
        normalize(vDirection),          // normalized ray direction
        vec3(0,6372e3,0),               // ray origin
        vSunDir,                        // position of the sun
        22.0,                           // intensity of the sun
        6371e3,                         // radius of the planet in meters
        6471e3,                         // radius of the atmosphere in meters
        vec3(5.5e-6, 13.0e-6, 22.4e-6), // Rayleigh scattering coefficient
        21e-6,                          // Mie scattering coefficient
        8e3,                            // Rayleigh scale height
        1.2e3,                          // Mie scale height
        0.758                           // Mie preferred scattering direction
    );
}
