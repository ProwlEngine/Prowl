#version 460 core

// =============================================================
//  ShaderVariables.glsl (inlined, standalone)
// =============================================================

// Global Uniform Buffer containing per-frame shared rendering data
// Uses std140 layout for compatibility across all GPUs
// This data is uploaded once per frame and is constant across all draw calls
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

// Per-object uniforms (set per draw call)
layout(std140, set = 0, binding = 1) uniform PerObjectUniforms
{
    mat4 prowl_ObjectToWorld;
    mat4 prowl_WorldToObject;
    mat4 prowl_PrevObjectToWorld;
    int _ObjectID;
};

#define PROWL_MATRIX_M prowl_ObjectToWorld
#define PROWL_MATRIX_M_PREVIOUS prowl_PrevObjectToWorld

// Derived matrices
mat4 prowl_MatMV = prowl_MatV * prowl_ObjectToWorld;
mat4 prowl_MatMVP = prowl_MatVP * prowl_ObjectToWorld;
mat4 prowl_MatTMV = transpose(prowl_MatV * prowl_ObjectToWorld);
mat4 prowl_MatITMV = transpose(prowl_WorldToObject * prowl_MatIV);

#define PROWL_MATRIX_MV prowl_MatMV
#define PROWL_MATRIX_MVP prowl_MatMVP
#define PROWL_MATRIX_T_MV prowl_MatTMV
#define PROWL_MATRIX_IT_MV prowl_MatITMV

// =============================================================
//  VertexAttributes.glsl (inlined, standalone)
//  Baseline mesh attribute path. UV, normals, colors, tangents
//  present. No GPU instancing, skinning, or blend shapes.
// =============================================================

#define HAS_UV
#define HAS_NORMALS
#define HAS_COLORS
#define HAS_TANGENTS

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

// =============================================================
//  Standalone test entry point
// =============================================================

void main()
{
    vec4 clipPos = TransformClip(vertexPosition);
    vec3 worldNormal = TransformDirection(vertexNormal);
    vec4 instColor = GetInstanceColor();

    // Fold normal and color into w harmlessly so they are not eliminated as dead code.
    gl_Position = clipPos + vec4(0.0, 0.0, 0.0, (worldNormal.x + instColor.x) * 0.0);
}
