#version 460 core

#define HAS_UV
#define HAS_NORMALS

// =============================================================
//  ShaderVariables.glsl
// =============================================================
#ifndef SHADER_SHADERVARIABLES
#define SHADER_SHADERVARIABLES

// Global per-frame data, uploaded once per frame and constant across all draw calls.
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

// Per-object uniforms (set per draw call).
layout(std140, set = 0, binding = 1) uniform PerObjectUniforms
{
    mat4 prowl_ObjectToWorld;
    mat4 prowl_WorldToObject;
    mat4 prowl_PrevObjectToWorld;
    int _ObjectID;
};

#define PROWL_MATRIX_M prowl_ObjectToWorld
#define PROWL_MATRIX_M_PREVIOUS prowl_PrevObjectToWorld

mat4 prowl_MatMV = prowl_MatV * prowl_ObjectToWorld;
mat4 prowl_MatMVP = prowl_MatVP * prowl_ObjectToWorld;
mat4 prowl_MatTMV = transpose(prowl_MatV * prowl_ObjectToWorld);
mat4 prowl_MatITMV = transpose(prowl_WorldToObject * prowl_MatIV);

#define PROWL_MATRIX_MV prowl_MatMV
#define PROWL_MATRIX_MVP prowl_MatMVP
#define PROWL_MATRIX_T_MV prowl_MatTMV
#define PROWL_MATRIX_IT_MV prowl_MatITMV

#endif

// =============================================================
//  ProwlCG.glsl
// =============================================================
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

// =============================================================
//  VertexAttributes.glsl
// =============================================================
#ifndef SHADER_VERTEXATTRIBUTES
#define SHADER_VERTEXATTRIBUTES

// =============================================================
//  Vertex Input Attributes
// =============================================================

		layout (location = 0) in vec3 vertexPosition;

#ifdef HAS_UV
		layout (location = 1) in vec2 vertexTexCoord0;
#else
		vec2 vertexTexCoord0 = vec2(0.0, 0.0);
#endif

#ifdef HAS_UV2
		layout (location = 2) in vec2 vertexTexCoord1;
#else
		vec2 vertexTexCoord1 = vec2(0.0, 0.0);
#endif

#ifdef HAS_NORMALS
		layout (location = 3) in vec3 vertexNormal;
#else
		vec3 vertexNormal = vec3(0.0, 1.0, 0.0);
#endif

#ifdef HAS_COLORS
		layout (location = 4) in vec4 vertexColor;
#else
		vec4 vertexColor = vec4(1.0, 1.0, 1.0, 1.0);
#endif

#ifdef HAS_TANGENTS
		layout (location = 5) in vec4 vertexTangent; // xyz = tangent direction, w = bitangent sign
#else
		vec4 vertexTangent = vec4(1.0, 0.0, 0.0, 1.0);
#endif

// =============================================================
//  GPU Instancing
//  When GPU_INSTANCING is defined, per-instance data is provided
//  via vertex attributes at locations 8-13.
// =============================================================

#ifdef GPU_INSTANCING
		layout(location = 8)  in vec4 instanceModelRow0;
		layout(location = 9)  in vec4 instanceModelRow1;
		layout(location = 10) in vec4 instanceModelRow2;
		layout(location = 11) in vec4 instanceModelRow3;
		layout(location = 12) in vec4 instanceColor;
		layout(location = 13) in vec4 instanceCustomData;
#endif

// =============================================================
//  Skeletal Animation (Skinning)
// =============================================================

#ifdef SKINNED
	#ifdef HAS_BONEINDICES
		layout (location = 6) in vec4 vertexBoneIndices;
	#else
		vec4 vertexBoneIndices = vec4(0, 0, 0, 0);
	#endif

	#ifdef HAS_BONEWEIGHTS
		layout (location = 7) in vec4 vertexBoneWeights;
	#else
		vec4 vertexBoneWeights = vec4(0.0, 0.0, 0.0, 0.0);
	#endif

		const int MAX_BONE_INFLUENCE = 4;

		// Bone matrices stored in a RGBA32F texture - no uniform array size limit.
		// Layout: each bone = 4 consecutive texels (one per column of mat4).
		uniform sampler2D boneMatrixTexture;
		uniform int boneCount;

		mat4 GetBoneMatrix(int boneIndex)
		{
			int texOffset = boneIndex * 4;
			vec4 col0 = texelFetch(boneMatrixTexture, ivec2(texOffset + 0, 0), 0);
			vec4 col1 = texelFetch(boneMatrixTexture, ivec2(texOffset + 1, 0), 0);
			vec4 col2 = texelFetch(boneMatrixTexture, ivec2(texOffset + 2, 0), 0);
			vec4 col3 = texelFetch(boneMatrixTexture, ivec2(texOffset + 3, 0), 0);
			return mat4(col0, col1, col2, col3);
		}

		vec4 GetSkinnedPosition(vec3 position)
		{
			vec4 skinnedPos = vec4(0.0);
			for (int i = 0; i < MAX_BONE_INFLUENCE; i++)
			{
				int boneIndex = int(vertexBoneIndices[i]);
				float weight = vertexBoneWeights[i];
				if (boneIndex > 0 && weight > 0.0 && boneIndex <= boneCount)
				{
					mat4 boneTransform = GetBoneMatrix(boneIndex - 1);
					skinnedPos += (boneTransform * vec4(position, 1.0)) * weight;
				}
			}
			float totalWeight = vertexBoneWeights.x + vertexBoneWeights.y + vertexBoneWeights.z + vertexBoneWeights.w;
			if (totalWeight < 0.01)
				skinnedPos = vec4(position, 1.0);
			return skinnedPos;
		}

		vec3 GetSkinnedNormal(vec3 normal)
		{
			vec3 skinnedNormal = vec3(0.0);
			for (int i = 0; i < MAX_BONE_INFLUENCE; i++)
			{
				int boneIndex = int(vertexBoneIndices[i]);
				float weight = vertexBoneWeights[i];
				if (boneIndex > 0 && weight > 0.0 && boneIndex <= boneCount)
				{
					mat4 boneTransform = GetBoneMatrix(boneIndex - 1);
					skinnedNormal += (mat3(boneTransform) * normal) * weight;
				}
			}
			float totalWeight = vertexBoneWeights.x + vertexBoneWeights.y + vertexBoneWeights.z + vertexBoneWeights.w;
			if (totalWeight < 0.01)
				skinnedNormal = normal;
			return normalize(skinnedNormal);
		}
#endif

// =============================================================
//  Blend Shapes (Morph Targets)
//  Per-mesh delta textures (RGBA32F) hold one "layer" per blend-shape frame.
//  Deltas are laid out linearly as idx = layer * morphVertexCount + gl_VertexIndex
//  and tiled into 2D: (idx % morphTexWidth, idx / morphTexWidth).
//  The renderer uploads only the ACTIVE layers (non-zero weight) into
//  morphWeightTexture, one texel each: (layerIndex, weight, 0, 0).
//  Morphing is applied to the rest-pose vertex BEFORE skinning.
// =============================================================

#ifdef BLENDSHAPES
		uniform sampler2D morphPositionTexture;
		uniform sampler2D morphNormalTexture;
		uniform sampler2D morphTangentTexture;
		uniform sampler2D morphWeightTexture;
		uniform int morphActiveCount;
		uniform int morphTexWidth;
		uniform int morphVertexCount;
		uniform int morphHasNormals;
		uniform int morphHasTangents;

		vec3 SampleMorphDelta(sampler2D tex, int layer)
		{
			int idx = layer * morphVertexCount + gl_VertexIndex;
			return texelFetch(tex, ivec2(idx % morphTexWidth, idx / morphTexWidth), 0).xyz;
		}

		vec3 GetMorphedPosition(vec3 position)
		{
			for (int i = 0; i < morphActiveCount; i++)
			{
				vec2 lw = texelFetch(morphWeightTexture, ivec2(i, 0), 0).xy;
				position += SampleMorphDelta(morphPositionTexture, int(lw.x)) * lw.y;
			}
			return position;
		}

		vec3 GetMorphedNormal(vec3 normal)
		{
			if (morphHasNormals == 0) return normal;
			for (int i = 0; i < morphActiveCount; i++)
			{
				vec2 lw = texelFetch(morphWeightTexture, ivec2(i, 0), 0).xy;
				normal += SampleMorphDelta(morphNormalTexture, int(lw.x)) * lw.y;
			}
			return normal;
		}

		vec3 GetMorphedTangent(vec3 tangent)
		{
			if (morphHasTangents == 0) return tangent;
			for (int i = 0; i < morphActiveCount; i++)
			{
				vec2 lw = texelFetch(morphWeightTexture, ivec2(i, 0), 0).xy;
				tangent += SampleMorphDelta(morphTangentTexture, int(lw.x)) * lw.y;
			}
			return tangent;
		}
#else
		// No-op passthroughs so shaders can call these unconditionally.
		vec3 GetMorphedPosition(vec3 position) { return position; }
		vec3 GetMorphedNormal(vec3 normal) { return normal; }
		vec3 GetMorphedTangent(vec3 tangent) { return tangent; }
#endif

// =============================================================
//  Vertex Utilities
//  Helper functions that handle instancing + skinning so shaders
//  don't need to repeat the same boilerplate.
//
//  Usage:
//    mat4 modelMatrix = GetModelMatrix();
//    mat4 mvpMatrix = GetMVPMatrix();
//    vec4 worldPos = TransformPosition(vertexPosition);          // handles skinning + instancing
//    vec3 worldNrm = TransformNormal(vertexNormal);              // handles skinning + instancing
//    vec3 worldTan = TransformNormal(vertexTangent.xyz);         // works for tangents too
//    vec4 clipPos  = TransformClip(vertexPosition);              // MVP-transformed
//    vec4 vColor   = GetVertexColor();                           // applies instance tint
// =============================================================

// Returns the model (object-to-world) matrix, accounting for GPU instancing.
mat4 GetModelMatrix()
{
#ifdef GPU_INSTANCING
	return mat4(instanceModelRow0, instanceModelRow1, instanceModelRow2, instanceModelRow3);
#else
	return PROWL_MATRIX_M;
#endif
}

// Returns the Model-View-Projection matrix, accounting for GPU instancing.
mat4 GetMVPMatrix()
{
#ifdef GPU_INSTANCING
	return PROWL_MATRIX_VP * GetModelMatrix();
#else
	return PROWL_MATRIX_MVP;
#endif
}

// Transform a position to world space (applies morph, then skinning if active, then model matrix).
vec3 TransformPosition(vec3 position)
{
	position = GetMorphedPosition(position);
	mat4 model = GetModelMatrix();
#ifdef SKINNED
	return (model * GetSkinnedPosition(position)).xyz;
#else
	return (model * vec4(position, 1.0)).xyz;
#endif
}

// Transform a position to clip space (applies morph, then skinning if active, then MVP matrix).
vec4 TransformClip(vec3 position)
{
	position = GetMorphedPosition(position);
	mat4 mvp = GetMVPMatrix();
#ifdef SKINNED
	return mvp * GetSkinnedPosition(position);
#else
	return mvp * vec4(position, 1.0);
#endif
}

// Transform a direction/normal to world space (applies skinning if active, then model rotation).
vec3 TransformDirection(vec3 dir)
{
	mat4 model = GetModelMatrix();
#ifdef SKINNED
	return normalize(mat3(model) * GetSkinnedNormal(dir));
#else
	return normalize(mat3(model) * dir);
#endif
}

// Get vertex color with per-instance tint applied.
vec4 GetInstanceColor()
{
#ifdef GPU_INSTANCING
	return vertexColor * instanceColor;
#else
	return vertexColor;
#endif
}

// Get per-instance custom data (available only with GPU instancing).
vec4 GetInstanceCustomData()
{
#ifdef GPU_INSTANCING
	return instanceCustomData;
#else
	return vec4(0.0);
#endif
}

#endif

// =============================================================
//  Material Uniforms (from Properties block)
// =============================================================

layout(std140, set = 1, binding = 0) uniform MaterialUniforms
{
    int _HasHoles;
    int _LayerCount;

    float _Layer0Tiling;
    float _Layer0Roughness;
    float _Layer0Metallic;

    float _Layer1Tiling;
    float _Layer1Roughness;
    float _Layer1Metallic;

    float _Layer2Tiling;
    float _Layer2Roughness;
    float _Layer2Metallic;

    float _Layer3Tiling;
    float _Layer3Roughness;
    float _Layer3Metallic;

    float _TerrainSize;
    float _TerrainHeight;

    vec2 _BrushPosition;
    float _BrushRadius;
    float _BrushFalloff;
    float _BrushVisible;
};

layout(set = 1, binding = 1) uniform sampler2D _Heightmap;

// =============================================================
//  Pass Uniforms (pass-body-local, not Properties-declared)
// =============================================================

layout(std140, set = 2, binding = 0) uniform PassUniforms
{
    mat4 _TerrainWorldToLocal;
    mat4 _TerrainLocalToWorld;
};

layout(location = 14) out vec2 texCoord0;
layout(location = 15) out vec3 worldPos;
layout(location = 16) out vec3 worldNormal;

// Vertex UV -> texel-center UV remap
vec2 hmSampleUV(vec2 uv)
{
    vec2 s = vec2(textureSize(_Heightmap, 0));
    return uv * (s - 1.0) / s + 0.5 / s;
}

float sampleHeight(vec2 uv) { return texture(_Heightmap, hmSampleUV(uv)).r * _TerrainHeight; }

void main()
{
#ifdef GPU_INSTANCING
    mat4 instanceModel = mat4(instanceModelRow0, instanceModelRow1, instanceModelRow2, instanceModelRow3);
    vec4 worldPos4 = instanceModel * vec4(vertexPosition, 1.0);
    vec3 terrainLocal = (_TerrainWorldToLocal * worldPos4).xyz;
    vec2 terrainUV = terrainLocal.xz / _TerrainSize;
    texCoord0 = terrainUV;

    // Displace: add height along terrain-local Y, transformed back to world
    // terrainLocal with height applied
    float height = sampleHeight(terrainUV);
    vec3 displacedLocal = vec3(terrainLocal.x, height, terrainLocal.z);
    vec3 worldPosition = (_TerrainLocalToWorld * vec4(displacedLocal, 1.0)).xyz;

    // Normal via central differences
    float hmSize = float(textureSize(_Heightmap, 0).x);
    float vertStep = hmSize > 1.0 ? (1.0 / (hmSize - 1.0)) : 0.001;

    float hR = sampleHeight(terrainUV + vec2(vertStep, 0.0));
    float hL = sampleHeight(terrainUV - vec2(vertStep, 0.0));
    float hU = sampleHeight(terrainUV + vec2(0.0, vertStep));
    float hD = sampleHeight(terrainUV - vec2(0.0, vertStep));

    float wStep = vertStep * _TerrainSize;
    float slopeX = (hR - hL) / (wStep * 2.0);
    float slopeZ = (hU - hD) / (wStep * 2.0);

    vec3 localNormal = normalize(vec3(-slopeX, 1.0, -slopeZ));
    worldNormal = normalize((_TerrainLocalToWorld * vec4(localNormal, 0.0)).xyz);

    worldPos = worldPosition;
    gl_Position = PROWL_MATRIX_VP * vec4(worldPosition, 1.0);
#else
    gl_Position = PROWL_MATRIX_MVP * vec4(vertexPosition, 1.0);
    texCoord0 = vertexTexCoord0;
    worldPos = (PROWL_MATRIX_M * vec4(vertexPosition, 1.0)).xyz;
    worldNormal = normalize((PROWL_MATRIX_M * vec4(0.0, 1.0, 0.0, 0.0)).xyz);
#endif
}
