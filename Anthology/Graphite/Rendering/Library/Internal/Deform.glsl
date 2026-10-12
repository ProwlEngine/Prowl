// Deforming renderables' copies of their meshes. Mode 0 copies words, filling a new copy from its mesh or a morphing
// copy's rest pose. Mode 2 adds one blend shape frame's weighted deltas to a copy, a thread per vertex the frame moves.
// Mode 1 skins a copy's positions, normals and tangents, from the mesh's rest pose or the copy's morphed one, with up to
// four bones a vertex, as Prowl does: bone index 0 is none, as are indices past the bones, under 0.01 total weight
// keeps the pose, and normals and tangents are normalized unless they cancel to nothing.
Compute
{
    layout(local_size_x = 64) in;

    layout(set = 0, binding = 0, std430) readonly buffer Graphite_DeformSource { uint source[]; };
    layout(set = 0, binding = 1, std430) buffer Graphite_DeformTarget { uint target[]; };
    layout(set = 0, binding = 2, std430) readonly buffer Graphite_SkinMatrices { mat4 bones[]; };
    layout(push_constant) uniform Push
    {
        uint mode;
        uint first;
        uint count;
        uint streams;          // bit 0 normals, bit 1 tangents, bit 2 skin the copy's own pose
        uint sourceWord;       // the words copied, the rest positions, or a frame's vertex indices
        uint targetWord;
        uint normal;
        uint targetNormal;
        uint tangent;
        uint targetTangent;
        uint boneIndices;
        uint boneWeights;
        uint boneBase;
        uint boneCount;
        uint deltas;           // a frame's position deltas
        float weight;          // the frame's weight
    } push;

    vec3 LoadVec3(uint word) { return uintBitsToFloat(uvec3(source[word], source[word + 1u], source[word + 2u])); }
    vec3 LoadTarget(uint word) { return uintBitsToFloat(uvec3(target[word], target[word + 1u], target[word + 2u])); }
    vec3 LoadPose(uint word, uint targetWord) { return (push.streams & 4u) != 0u ? LoadTarget(targetWord) : LoadVec3(word); }

    void StoreVec3(uint word, vec3 v)
    {
        uvec3 bits = floatBitsToUint(v);
        target[word] = bits.x;
        target[word + 1u] = bits.y;
        target[word + 2u] = bits.z;
    }

    void AddVec3(uint word, vec3 delta) { StoreVec3(word, LoadTarget(word) + delta); }

    vec3 SafeNormalize(vec3 v)
    {
        float lengthSquared = dot(v, v);
        return !(lengthSquared > 1e-20) ? v : v * inversesqrt(lengthSquared);
    }

    void main()
    {
        uint i = gl_GlobalInvocationID.x + push.first;
        if (i >= push.count)
            return;
        if (push.mode == 0u)
        {
            target[push.targetWord + i] = source[push.sourceWord + i];
            return;
        }
        if (push.mode == 2u)
        {
            uint vertex = source[push.sourceWord + i];
            AddVec3(push.targetWord + vertex * 3u, LoadVec3(push.deltas + i * 3u) * push.weight);
            if ((push.streams & 1u) != 0u)
                AddVec3(push.targetNormal + vertex * 3u, LoadVec3(push.normal + i * 3u) * push.weight);
            if ((push.streams & 2u) != 0u)
                AddVec3(push.targetTangent + vertex * 4u, LoadVec3(push.tangent + i * 3u) * push.weight);
            return;
        }

        uint a = source[push.boneIndices + i * 2u], b = source[push.boneIndices + i * 2u + 1u];
        uvec4 index = uvec4(a & 0xFFFFu, a >> 16, b & 0xFFFFu, b >> 16);
        vec4 weight = vec4(unpackUnorm2x16(source[push.boneWeights + i * 2u]), unpackUnorm2x16(source[push.boneWeights + i * 2u + 1u]));
        mat4 skin = mat4(0.0);
        for (int k = 0; k < 4; k++)
        {
            if (index[k] > 0u && weight[k] > 0.0 && index[k] <= push.boneCount)
                skin += bones[push.boneBase + index[k] - 1u] * weight[k];
        }
        bool rest = weight.x + weight.y + weight.z + weight.w < 0.01;
        mat3 linear = rest ? mat3(1.0) : mat3(skin);

        vec3 position = LoadPose(push.sourceWord + i * 3u, push.targetWord + i * 3u);
        StoreVec3(push.targetWord + i * 3u, rest ? position : (skin * vec4(position, 1.0)).xyz);
        if ((push.streams & 1u) != 0u)
            StoreVec3(push.targetNormal + i * 3u, SafeNormalize(linear * LoadPose(push.normal + i * 3u, push.targetNormal + i * 3u)));
        if ((push.streams & 2u) != 0u)
        {
            StoreVec3(push.targetTangent + i * 4u, SafeNormalize(linear * LoadPose(push.tangent + i * 4u, push.targetTangent + i * 4u)));
            target[push.targetTangent + i * 4u + 3u] = source[push.tangent + i * 4u + 3u];
        }
    }
}
