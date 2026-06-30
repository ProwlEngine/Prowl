module VertexAttributes;

__exported import ShaderVariables;
import VariantAttributes;

// Global mesh keywords. Shaders that call the Transform* / GetInstance* helpers below get all three
// permutation axes; each shader still declares its own vertex input struct and fills a MeshVertex.
[variant("false") variant("true")]
extern static const bool SKINNED;
[variant("false") variant("true")]
extern static const bool GPU_INSTANCING;
[variant("false") variant("true")]
extern static const bool BLENDSHAPES;

// =============================================================
//  Skeletal Animation (Skinning)
//  Bone matrices stored in a RGBA32F texture - no uniform array size limit.
//  Layout: each bone = 4 consecutive texels (one per column of float4x4).
// =============================================================

public struct SkinData
{
    public Sampler2D<float4> boneMatrixTexture;
    public int boneCount;
}
public ParameterBlock<SkinData> Skin;

public static const int MAX_BONE_INFLUENCE = 4;

// Texels store the matrix columns; transpose so mul(M, v) matches the original column-major GLSL.
public float4x4 GetBoneMatrix(int boneIndex)
{
    int texOffset = boneIndex * 4;
    float4 col0 = Skin.boneMatrixTexture.Load(int3(texOffset + 0, 0, 0));
    float4 col1 = Skin.boneMatrixTexture.Load(int3(texOffset + 1, 0, 0));
    float4 col2 = Skin.boneMatrixTexture.Load(int3(texOffset + 2, 0, 0));
    float4 col3 = Skin.boneMatrixTexture.Load(int3(texOffset + 3, 0, 0));
    return transpose(float4x4(col0, col1, col2, col3));
}

public float4 GetSkinnedPosition(float3 position, float4 boneIndices, float4 boneWeights)
{
    float4 skinnedPos = float4(0.0);
    for (int i = 0; i < MAX_BONE_INFLUENCE; i++)
    {
        int boneIndex = int(boneIndices[i]);
        float weight = boneWeights[i];
        if (boneIndex > 0 && weight > 0.0 && boneIndex <= Skin.boneCount)
        {
            float4x4 boneTransform = GetBoneMatrix(boneIndex - 1);
            skinnedPos += mul(boneTransform, float4(position, 1.0)) * weight;
        }
    }
    float totalWeight = boneWeights.x + boneWeights.y + boneWeights.z + boneWeights.w;
    if (totalWeight < 0.01)
        skinnedPos = float4(position, 1.0);
    return skinnedPos;
}

public float3 GetSkinnedNormal(float3 normal, float4 boneIndices, float4 boneWeights)
{
    float3 skinnedNormal = float3(0.0);
    for (int i = 0; i < MAX_BONE_INFLUENCE; i++)
    {
        int boneIndex = int(boneIndices[i]);
        float weight = boneWeights[i];
        if (boneIndex > 0 && weight > 0.0 && boneIndex <= Skin.boneCount)
        {
            float4x4 boneTransform = GetBoneMatrix(boneIndex - 1);
            skinnedNormal += mul((float3x3)boneTransform, normal) * weight;
        }
    }
    float totalWeight = boneWeights.x + boneWeights.y + boneWeights.z + boneWeights.w;
    if (totalWeight < 0.01)
        skinnedNormal = normal;
    return normalize(skinnedNormal);
}

// =============================================================
//  Blend Shapes (Morph Targets)
//  Per-mesh delta textures (RGBA32F) hold one "layer" per blend-shape frame.
//  Deltas are laid out linearly as idx = layer * morphVertexCount + vertexID
//  and tiled into 2D: (idx % morphTexWidth, idx / morphTexWidth).
// =============================================================

public struct MorphData
{
    public Sampler2D<float4> morphPositionTexture;
    public Sampler2D<float4> morphNormalTexture;
    public Sampler2D<float4> morphTangentTexture;
    public Sampler2D<float4> morphWeightTexture;
    public int morphActiveCount;
    public int morphTexWidth;
    public int morphVertexCount;
    public int morphHasNormals;
    public int morphHasTangents;
}
public ParameterBlock<MorphData> Morph;

public float3 SampleMorphDelta(Sampler2D<float4> tex, int layer, uint vertexID)
{
    int idx = layer * Morph.morphVertexCount + int(vertexID);
    return tex.Load(int3(idx % Morph.morphTexWidth, idx / Morph.morphTexWidth, 0)).xyz;
}

// No-op passthrough unless the BLENDSHAPES variant is active.
public float3 GetMorphedPosition(float3 position, uint vertexID)
{
    static if (BLENDSHAPES)
    {
        for (int i = 0; i < Morph.morphActiveCount; i++)
        {
            float2 lw = Morph.morphWeightTexture.Load(int3(i, 0, 0)).xy;
            position += SampleMorphDelta(Morph.morphPositionTexture, int(lw.x), vertexID) * lw.y;
        }
    }
    return position;
}

public float3 GetMorphedNormal(float3 normal, uint vertexID)
{
    static if (BLENDSHAPES)
    {
        if (Morph.morphHasNormals == 0) return normal;
        for (int i = 0; i < Morph.morphActiveCount; i++)
        {
            float2 lw = Morph.morphWeightTexture.Load(int3(i, 0, 0)).xy;
            normal += SampleMorphDelta(Morph.morphNormalTexture, int(lw.x), vertexID) * lw.y;
        }
    }
    return normal;
}

public float3 GetMorphedTangent(float3 tangent, uint vertexID)
{
    static if (BLENDSHAPES)
    {
        if (Morph.morphHasTangents == 0) return tangent;
        for (int i = 0; i < Morph.morphActiveCount; i++)
        {
            float2 lw = Morph.morphWeightTexture.Load(int3(i, 0, 0)).xy;
            tangent += SampleMorphDelta(Morph.morphTangentTexture, int(lw.x), vertexID) * lw.y;
        }
    }
    return tangent;
}

// =============================================================
//  GPU Instancing
//  Instance rows store the model matrix columns (see InstanceData.cs); transpose so
//  mul(M, v) matches the original column-major GLSL.
// =============================================================

public float4x4 GetInstanceMatrix(float4 row0, float4 row1, float4 row2, float4 row3)
{
    return transpose(float4x4(row0, row1, row2, row3));
}

// =============================================================
//  Composition helpers (handle instancing + skinning + morph)
//  The shader fills a MeshVertex from its own input struct, then calls Transform*/GetInstance*.
// =============================================================

public struct MeshVertex
{
    public float3 position;
    public float3 normal;
    public float4 tangent;
    public float4 color;
    public uint vid;
    public float4 boneIndices;
    public float4 boneWeights;
    public float4 instanceRow0;
    public float4 instanceRow1;
    public float4 instanceRow2;
    public float4 instanceRow3;
    public float4 instanceColor;
    public float4 instanceCustomData;
}

public float4x4 GetModelMatrix(MeshVertex v)
{
    static if (GPU_INSTANCING)
        return GetInstanceMatrix(v.instanceRow0, v.instanceRow1, v.instanceRow2, v.instanceRow3);
    else
        return Object.prowl_ObjectToWorld;
}

public float4x4 GetMVPMatrix(MeshVertex v)
{
    static if (GPU_INSTANCING)
        return mul(Frame.prowl_MatVP, GetModelMatrix(v));
    else
        return Object.mvp;
}

// World-space position (applies morph, then skinning if active, then model matrix).
public float3 TransformPosition(MeshVertex v)
{
    float3 position = GetMorphedPosition(v.position, v.vid);

    float4x4 model = GetModelMatrix(v);
    static if (SKINNED)
        return mul(model, GetSkinnedPosition(position, v.boneIndices, v.boneWeights)).xyz;
    else
        return mul(model, float4(position, 1.0)).xyz;
}

// Clip-space position (applies morph, then skinning if active, then MVP matrix).
public float4 TransformClip(MeshVertex v)
{
    float3 position = GetMorphedPosition(v.position, v.vid);

    float4x4 mvp = GetMVPMatrix(v);
    static if (SKINNED)
        return mul(mvp, GetSkinnedPosition(position, v.boneIndices, v.boneWeights));
    else
        return mul(mvp, float4(position, 1.0));
}

// World-space direction (applies skinning if active, then model rotation).
public float3 TransformDirection(MeshVertex v, float3 dir)
{
    float4x4 model = GetModelMatrix(v);
    static if (SKINNED)
        return normalize(mul((float3x3)model, GetSkinnedNormal(dir, v.boneIndices, v.boneWeights)));
    else
        return normalize(mul((float3x3)model, dir));
}

// Vertex color with per-instance tint applied.
public float4 GetInstanceColor(MeshVertex v)
{
    static if (GPU_INSTANCING)
        return v.color * v.instanceColor;
    else
        return v.color;
}

// Per-instance custom data (zero without GPU instancing).
public float4 GetInstanceCustomData(MeshVertex v)
{
    static if (GPU_INSTANCING)
        return v.instanceCustomData;
    else
        return float4(0.0);
}
