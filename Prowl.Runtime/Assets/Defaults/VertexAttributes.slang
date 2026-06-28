module VertexAttributes;

__exported import ShaderVariables;

// =============================================================
//  Vertex attributes are declared per-shader in each vertex
//  input struct. The helpers below take the values they need as
//  explicit parameters instead of reading global `in` variables.
// =============================================================

// =============================================================
//  Skeletal Animation (Skinning)
// =============================================================

#ifdef SKINNED
static const int MAX_BONE_INFLUENCE = 4;

// Bone matrices stored in a RGBA32F texture - no uniform array size limit.
// Layout: each bone = 4 consecutive texels (one per column of the bone matrix).
public struct SkinningData
{
    public Sampler2D<float4> boneMatrixTexture;
    public int boneCount;
}
public ParameterBlock<SkinningData> Skin;

// Returns the bone matrix as float4x4(col0,col1,col2,col3); its rows are the stored
// columns, so apply it with mul(vector, matrix) to reproduce the GLSL `matrix * vector`.
public float4x4 GetBoneMatrix(int boneIndex)
{
    int texOffset = boneIndex * 4;
    float4 col0 = Skin.boneMatrixTexture.Load(int3(int2(texOffset + 0, 0), 0));
    float4 col1 = Skin.boneMatrixTexture.Load(int3(int2(texOffset + 1, 0), 0));
    float4 col2 = Skin.boneMatrixTexture.Load(int3(int2(texOffset + 2, 0), 0));
    float4 col3 = Skin.boneMatrixTexture.Load(int3(int2(texOffset + 3, 0), 0));
    return float4x4(col0, col1, col2, col3);
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
            skinnedPos += mul(float4(position, 1.0), boneTransform) * weight;
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
            skinnedNormal += mul(normal, (float3x3)boneTransform) * weight;
        }
    }
    float totalWeight = boneWeights.x + boneWeights.y + boneWeights.z + boneWeights.w;
    if (totalWeight < 0.01)
        skinnedNormal = normal;
    return normalize(skinnedNormal);
}
#endif

// =============================================================
//  Blend Shapes (Morph Targets)
//  Per-mesh delta textures (RGBA32F) hold one "layer" per blend-shape frame.
//  Deltas are laid out linearly as idx = layer * morphVertexCount + vertexID
//  and tiled into 2D: (idx % morphTexWidth, idx / morphTexWidth).
//  The renderer uploads only the ACTIVE layers (non-zero weight) into
//  morphWeightTexture, one texel each: (layerIndex, weight, 0, 0).
//  Morphing is applied to the rest-pose vertex BEFORE skinning.
// =============================================================

#ifdef BLENDSHAPES
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

float3 SampleMorphDelta(Sampler2D<float4> tex, int layer, uint vertexID)
{
    int idx = layer * Morph.morphVertexCount + int(vertexID);
    return tex.Load(int3(int2(idx % Morph.morphTexWidth, idx / Morph.morphTexWidth), 0)).xyz;
}

public float3 GetMorphedPosition(float3 position, uint vertexID)
{
    for (int i = 0; i < Morph.morphActiveCount; i++)
    {
        float2 lw = Morph.morphWeightTexture.Load(int3(int2(i, 0), 0)).xy;
        position += SampleMorphDelta(Morph.morphPositionTexture, int(lw.x), vertexID) * lw.y;
    }
    return position;
}

public float3 GetMorphedNormal(float3 normal, uint vertexID)
{
    if (Morph.morphHasNormals == 0) return normal;
    for (int i = 0; i < Morph.morphActiveCount; i++)
    {
        float2 lw = Morph.morphWeightTexture.Load(int3(int2(i, 0), 0)).xy;
        normal += SampleMorphDelta(Morph.morphNormalTexture, int(lw.x), vertexID) * lw.y;
    }
    return normal;
}

public float3 GetMorphedTangent(float3 tangent, uint vertexID)
{
    if (Morph.morphHasTangents == 0) return tangent;
    for (int i = 0; i < Morph.morphActiveCount; i++)
    {
        float2 lw = Morph.morphWeightTexture.Load(int3(int2(i, 0), 0)).xy;
        tangent += SampleMorphDelta(Morph.morphTangentTexture, int(lw.x), vertexID) * lw.y;
    }
    return tangent;
}
#else
// No-op passthroughs so shaders can call these unconditionally.
public float3 GetMorphedPosition(float3 position, uint vertexID) { return position; }
public float3 GetMorphedNormal(float3 normal, uint vertexID) { return normal; }
public float3 GetMorphedTangent(float3 tangent, uint vertexID) { return tangent; }
#endif

// =============================================================
//  Vertex Utilities
//  Helpers that fold morph + (optional) skinning + (optional) instancing
//  into the standard transforms. Under GPU_INSTANCING the model rows and
//  under SKINNED the bone attributes are passed in by the caller.
// =============================================================

// Returns the model (object-to-world) matrix, accounting for GPU instancing.
#ifdef GPU_INSTANCING
// GLSL `mat4(a,b,c,d)` builds columns; transpose(float4x4(...)) reproduces that in Slang.
public float4x4 GetModelMatrix(float4 row0, float4 row1, float4 row2, float4 row3)
{
    return transpose(float4x4(row0, row1, row2, row3));
}
#else
public float4x4 GetModelMatrix()
{
    return Object.prowl_ObjectToWorld;
}
#endif

// Transform a position to world space (morph, then skinning if active, then model matrix).
public float3 TransformPosition(
    float3 position,
    uint vertexID
#ifdef SKINNED
    , float4 boneIndices, float4 boneWeights
#endif
#ifdef GPU_INSTANCING
    , float4 row0, float4 row1, float4 row2, float4 row3
#endif
)
{
    position = GetMorphedPosition(position, vertexID);
#ifdef GPU_INSTANCING
    float4x4 model = GetModelMatrix(row0, row1, row2, row3);
#else
    float4x4 model = GetModelMatrix();
#endif
#ifdef SKINNED
    return mul(model, GetSkinnedPosition(position, boneIndices, boneWeights)).xyz;
#else
    return mul(model, float4(position, 1.0)).xyz;
#endif
}

// Transform a position to clip space (morph, then skinning if active, then MVP matrix).
public float4 TransformClip(
    float3 position,
    uint vertexID
#ifdef SKINNED
    , float4 boneIndices, float4 boneWeights
#endif
#ifdef GPU_INSTANCING
    , float4 row0, float4 row1, float4 row2, float4 row3
#endif
)
{
    position = GetMorphedPosition(position, vertexID);
#ifdef GPU_INSTANCING
    float4x4 mvp = mul(Frame.prowl_MatVP, GetModelMatrix(row0, row1, row2, row3));
#else
    float4x4 mvp = Object.mvp;
#endif
#ifdef SKINNED
    return mul(mvp, GetSkinnedPosition(position, boneIndices, boneWeights));
#else
    return mul(mvp, float4(position, 1.0));
#endif
}

// Transform a direction/normal to world space (skinning if active, then model rotation).
public float3 TransformDirection(
    float3 dir
#ifdef SKINNED
    , float4 boneIndices, float4 boneWeights
#endif
#ifdef GPU_INSTANCING
    , float4 row0, float4 row1, float4 row2, float4 row3
#endif
)
{
#ifdef GPU_INSTANCING
    float4x4 model = GetModelMatrix(row0, row1, row2, row3);
#else
    float4x4 model = GetModelMatrix();
#endif
#ifdef SKINNED
    return normalize(mul((float3x3)model, GetSkinnedNormal(dir, boneIndices, boneWeights)));
#else
    return normalize(mul((float3x3)model, dir));
#endif
}

// Vertex color with per-instance tint applied.
public float4 GetInstanceColor(
    float4 vertexColor
#ifdef GPU_INSTANCING
    , float4 instanceColor
#endif
)
{
#ifdef GPU_INSTANCING
    return vertexColor * instanceColor;
#else
    return vertexColor;
#endif
}
