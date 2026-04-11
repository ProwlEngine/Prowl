// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Renders a skinned mesh deformed by bone Transforms.
/// Bones are referenced by path (relative to this GO's root), matching Unity's convention.
/// Paths like "Armature/Hips/Spine" are resolved via Transform.Find().
/// Supports multiple materials via submeshes.
/// Bone matrices are uploaded via a float texture (no uniform array size limit).
/// </summary>
[AddComponentMenu("Rendering/Skinned Mesh Renderer")]
public class SkinnedMeshRenderer : MonoBehaviour
{
    /// <summary>The mesh to render (may contain submeshes).</summary>
    public AssetRef<Mesh> SharedMesh;

    /// <summary>Materials array — one per submesh. If fewer materials than submeshes, last material is reused.</summary>
    public List<AssetRef<Material>> Materials = new();

    /// <summary>Path to the root bone, relative to this GO's hierarchy root.</summary>
    [SerializeField]
    public string? RootBonePath;

    /// <summary>
    /// Paths to all bone Transforms, indexed to match Mesh.BindPoses.
    /// Each path is relative to this GO's hierarchy root (e.g. "Armature/Hips/Spine").
    /// </summary>
    [SerializeField]
    public string[]? BonePaths;

    public Color MainColor = Color.White;

    // Resolved at runtime
    [System.NonSerialized] private Transform? _rootBone;
    [System.NonSerialized] private Transform?[]? _bones;
    [System.NonSerialized] private Float4x4[]? _skinMatrices;
    [System.NonSerialized] private bool _resolved;

    // Bone matrix texture — each bone is 4 RGBA32F texels (one per matrix row)
    [System.NonSerialized] private Texture2D? _boneTexture;
    [System.NonSerialized] private int _boneTextureSize; // number of bones the texture was allocated for

    /// <summary>Get the resolved root bone Transform.</summary>
    public Transform? RootBone
    {
        get { Resolve(); return _rootBone; }
    }

    /// <summary>Get the resolved bone Transforms array.</summary>
    public Transform?[]? Bones
    {
        get { Resolve(); return _bones; }
    }

    /// <summary>
    /// Set bone references from live Transforms. Computes paths relative to the hierarchy root.
    /// Call this during import when you have the live GO hierarchy.
    /// </summary>
    public void SetBones(Transform[] boneTransforms, Transform? rootBone)
    {
        Transform hierarchyRoot = GetHierarchyRoot();

        BonePaths = new string[boneTransforms.Length];
        for (int i = 0; i < boneTransforms.Length; i++)
        {
            if (boneTransforms[i] != null)
                BonePaths[i] = Transform.GetRelativePath(boneTransforms[i], hierarchyRoot);
        }

        if (rootBone != null)
            RootBonePath = Transform.GetRelativePath(rootBone, hierarchyRoot);

        // Also cache resolved references immediately
        _bones = new Transform?[boneTransforms.Length];
        Array.Copy(boneTransforms, _bones, boneTransforms.Length);
        _rootBone = rootBone;
        _resolved = true;
    }

    private Transform GetHierarchyRoot()
    {
        Transform root = Transform;
        while (root.Parent != null) root = root.Parent;
        return root;
    }

    private void Resolve()
    {
        if (_resolved) return;
        _resolved = true;

        Transform hierarchyRoot = GetHierarchyRoot();

        // Resolve root bone by path
        if (!string.IsNullOrEmpty(RootBonePath))
            _rootBone = hierarchyRoot.Find(RootBonePath);

        // Resolve bone array by paths
        if (BonePaths != null)
        {
            _bones = new Transform?[BonePaths.Length];
            for (int i = 0; i < BonePaths.Length; i++)
            {
                if (!string.IsNullOrEmpty(BonePaths[i]))
                    _bones[i] = hierarchyRoot.Find(BonePaths[i]);
            }
        }
    }

    public override void OnEnable()
    {
        _resolved = false; // Force re-resolve on enable
    }

    public override void OnDisable()
    {
        _boneTexture?.Dispose();
        _boneTexture = null;
        _boneTextureSize = 0;
    }

    /// <summary>
    /// Ensures the bone matrix texture exists and is large enough for the given bone count.
    /// Each bone occupies 4 texels (one per matrix row) in a single-row RGBA32F texture.
    /// </summary>
    private void EnsureBoneTexture(int boneCount)
    {
        if (_boneTexture != null && _boneTextureSize >= boneCount)
            return;

        _boneTexture?.Dispose();

        // Width = boneCount * 4 (4 texels per mat4), Height = 1
        uint width = (uint)(boneCount * 4);
        _boneTexture = new Texture2D(width, 1, false, TextureImageFormat.Float4);
        _boneTexture.SetTextureFilters(TextureMin.Nearest, TextureMag.Nearest);
        Graphics.SetWrapS(_boneTexture.Handle, TextureWrap.ClampToEdge);
        Graphics.SetWrapT(_boneTexture.Handle, TextureWrap.ClampToEdge);
        _boneTextureSize = boneCount;
    }

    /// <summary>
    /// Uploads bone matrices to the texture. Each mat4 is stored as 4 consecutive RGBA32F texels.
    /// Layout: texel[bone*4+0] = row0, texel[bone*4+1] = row1, texel[bone*4+2] = row2, texel[bone*4+3] = row3
    /// </summary>
    private unsafe void UploadBoneTexture(Float4x4[] matrices)
    {
        int boneCount = matrices.Length;
        EnsureBoneTexture(boneCount);

        // Float4x4 is column-major (c0,c1,c2,c3). We store columns as texels.
        // Shader reconstructs: mat4(col0, col1, col2, col3)
        // Each Float4x4 is 4 Float4 columns laid out contiguously in memory (c0, c1, c2, c3)
        // So we can upload the whole array directly — 4 texels per matrix, boneCount*4 texels total
        fixed (Float4x4* ptr = matrices)
        {
            _boneTexture!.SetDataPtr(ptr, 0, 0, (uint)(boneCount * 4), 1);
        }
    }

    public override void OnRenderCollect()
    {
        var mesh = SharedMesh.Res;
        if (mesh == null || Materials.Count == 0) return;

        Resolve();

        // Compute skinning matrices
        if (_bones != null && _bones.Length > 0 && mesh.BindPoses != null)
        {
            if (_skinMatrices == null || _skinMatrices.Length != _bones.Length)
                _skinMatrices = new Float4x4[_bones.Length];

            Float4x4 worldToLocal = Transform.WorldToLocalMatrix;
            for (int i = 0; i < _bones.Length; i++)
            {
                if (_bones[i] != null && i < mesh.BindPoses.Length)
                    _skinMatrices[i] = worldToLocal * _bones[i].LocalToWorldMatrix * mesh.BindPoses[i];
                else
                    _skinMatrices[i] = Float4x4.Identity;
            }

            // Upload to bone texture
            UploadBoneTexture(_skinMatrices);
        }

        // Render each submesh with its material
        int subCount = mesh.SubMeshCount;
        for (int s = 0; s < subCount; s++)
        {
            Material? mat = null;
            if (s < Materials.Count)
                mat = Materials[s].Res;
            else if (Materials.Count > 0)
                mat = Materials[^1].Res; // Reuse last material for extra submeshes

            if (mat == null) continue;

            PropertyState props = new();
            props.SetInt("_ObjectID", InstanceID);
            props.SetColor("_MainColor", MainColor);
            if (_boneTexture != null)
            {
                props.SetTexture("boneMatrixTexture", _boneTexture);
                props.SetInt("boneCount", _skinMatrices?.Length ?? 0);
            }

            GameObject.Scene.PushRenderable(new MeshRenderable(
                mesh, mat, Transform.LocalToWorldMatrix,
                GameObject.LayerIndex, props, subMeshIndex: s));
        }
    }
}
