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

		// Skin matrices, four columns a bone, in a storage buffer or a float texture where the vertex stage has none
		uniform int boneCount;

	#ifdef PROWL_VERTEX_STORAGE_BUFFERS
		layout(std430) readonly buffer ProwlBones { vec4 _Bones[]; };

		vec4 BoneTexel(int i) { return _Bones[i]; }
	#else
		uniform sampler2D _BoneTex;
		uniform int _BoneTexShift;

		vec4 BoneTexel(int i) { return texelFetch(_BoneTex, ivec2(i & ((1 << _BoneTexShift) - 1), i >> _BoneTexShift), 0); }
	#endif

		mat4 GetBoneMatrix(int boneIndex)
		{
			int t = boneIndex * 4;
			return mat4(BoneTexel(t), BoneTexel(t + 1), BoneTexel(t + 2), BoneTexel(t + 3));
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
			// Opposing bone transforms can cancel to zero here; normalizing that is 0/0.
			// Returned unnormalized in that case, for TransformDirection's guard to catch.
			float lenSq = dot(skinnedNormal, skinnedNormal);
			return !(lenSq > 1e-20) ? skinnedNormal : skinnedNormal * inversesqrt(lenSq);
		}
#endif

// =============================================================
//  Blend Shapes (Morph Targets)
//  One per-mesh delta table holds a "layer" per blend-shape frame, each delta at
//  layer * morphVertexCount + gl_VertexID: positions from 0, normals from morphNormalBase and
//  tangents from morphTangentBase (-1 when the mesh has none).
//  The renderer writes only the ACTIVE layers (non-zero weight) to a weight table,
//  one vec4 each: (layerIndex, weight, 0, 0).
//  Morphing is applied to the rest-pose vertex BEFORE skinning.
// =============================================================

#ifdef BLENDSHAPES
		uniform int morphActiveCount;
		uniform int morphVertexCount;
		uniform int morphNormalBase;
		uniform int morphTangentBase;

	#ifdef PROWL_VERTEX_STORAGE_BUFFERS
		layout(std430) readonly buffer ProwlMorphDeltas { vec4 _MorphDeltas[]; };
		layout(std430) readonly buffer ProwlMorphWeights { vec4 _MorphWeights[]; };

		vec4 MorphDeltaTexel(int i) { return _MorphDeltas[i]; }
		vec4 MorphWeightTexel(int i) { return _MorphWeights[i]; }
	#else
		uniform sampler2D _MorphDeltaTex;
		uniform int _MorphDeltaTexShift;
		uniform sampler2D _MorphWeightTex;
		uniform int _MorphWeightTexShift;

		vec4 MorphDeltaTexel(int i) { return texelFetch(_MorphDeltaTex, ivec2(i & ((1 << _MorphDeltaTexShift) - 1), i >> _MorphDeltaTexShift), 0); }
		vec4 MorphWeightTexel(int i) { return texelFetch(_MorphWeightTex, ivec2(i & ((1 << _MorphWeightTexShift) - 1), i >> _MorphWeightTexShift), 0); }
	#endif

		vec3 MorphDelta(int base, int layer)
		{
			return MorphDeltaTexel(base + layer * morphVertexCount + gl_VertexID).xyz;
		}

		vec3 GetMorphedPosition(vec3 position)
		{
			for (int i = 0; i < morphActiveCount; i++)
			{
				vec2 lw = MorphWeightTexel(i).xy;
				position += MorphDelta(0, int(lw.x)) * lw.y;
			}
			return position;
		}

		vec3 GetMorphedNormal(vec3 normal)
		{
			if (morphNormalBase < 0) return normal;
			for (int i = 0; i < morphActiveCount; i++)
			{
				vec2 lw = MorphWeightTexel(i).xy;
				normal += MorphDelta(morphNormalBase, int(lw.x)) * lw.y;
			}
			return normal;
		}

		vec3 GetMorphedTangent(vec3 tangent)
		{
			if (morphTangentBase < 0) return tangent;
			for (int i = 0; i < morphActiveCount; i++)
			{
				vec2 lw = MorphWeightTexel(i).xy;
				tangent += MorphDelta(morphTangentBase, int(lw.x)) * lw.y;
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
//
// The normalize is guarded because a zero-length input is not exotic: a vertex no triangle
// references, one whose face normals cancel, or a degenerate-UV tangent all arrive here as exactly
// zero, and normalize(vec3(0)) is 0/0. A single NaN normal or tangent spreads through the whole
// tangent frame and every lighting term that touches it, so it is stopped at the source.
vec3 TransformDirection(vec3 dir)
{
	mat4 model = GetModelMatrix();
#ifdef SKINNED
	vec3 world = mat3(model) * GetSkinnedNormal(dir);
#else
	vec3 world = mat3(model) * dir;
#endif
	float lenSq = dot(world, world);
	// Written as !(> eps) rather than (< eps) so a NaN input takes the fallback too.
	return !(lenSq > 1e-20) ? vec3(0.0, 1.0, 0.0) : world * inversesqrt(lenSq);
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
