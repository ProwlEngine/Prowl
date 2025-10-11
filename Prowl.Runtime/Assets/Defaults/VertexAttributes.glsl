﻿#ifndef SHADER_VERTEXATTRIBUTES
#define SHADER_VERTEXATTRIBUTES
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
		layout (location = 5) in vec3 vertexTangent;
#else
		vec3 vertexTangent = vec3(1.0, 0.0, 0.0);
#endif

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
		const int MAX_BONES = 100;
		uniform mat4 boneTransforms[MAX_BONES];

		// Skinning helper function
		vec4 GetSkinnedPosition(vec3 position)
		{
			vec4 skinnedPos = vec4(0.0);

			// Apply bone transformations based on weights
			// Note: Bone indices are 1-based in the mesh data (0 means no bone)
			for (int i = 0; i < MAX_BONE_INFLUENCE; i++)
			{
				int boneIndex = int(vertexBoneIndices[i]);
				float weight = vertexBoneWeights[i];

				// Skip if no bone assigned (index 0) or no weight
				if (boneIndex > 0 && weight > 0.0 && boneIndex <= MAX_BONES)
				{
					// Bone indices are 1-based, convert to 0-based for array access
					mat4 boneTransform = boneTransforms[boneIndex - 1];
					skinnedPos += (boneTransform * vec4(position, 1.0)) * weight;
				}
			}

			// If no bones affected this vertex, use original position
			float totalWeight = vertexBoneWeights.x + vertexBoneWeights.y + vertexBoneWeights.z + vertexBoneWeights.w;
			if (totalWeight < 0.01)
				skinnedPos = vec4(position, 1.0);

			return skinnedPos;
		}

		// Skinning helper function for normals (doesn't include translation)
		vec3 GetSkinnedNormal(vec3 normal)
		{
			vec3 skinnedNormal = vec3(0.0);

			for (int i = 0; i < MAX_BONE_INFLUENCE; i++)
			{
				int boneIndex = int(vertexBoneIndices[i]);
				float weight = vertexBoneWeights[i];

				if (boneIndex > 0 && weight > 0.0 && boneIndex <= MAX_BONES)
				{
					mat4 boneTransform = boneTransforms[boneIndex - 1];
					// Use mat3 to remove translation component
					skinnedNormal += (mat3(boneTransform) * normal) * weight;
				}
			}

			float totalWeight = vertexBoneWeights.x + vertexBoneWeights.y + vertexBoneWeights.z + vertexBoneWeights.w;
			if (totalWeight < 0.01)
				skinnedNormal = normal;

			return normalize(skinnedNormal);
		}
#endif
#endif
