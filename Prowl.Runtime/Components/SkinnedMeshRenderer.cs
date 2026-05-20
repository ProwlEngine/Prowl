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
/// Bones are referenced by path (relative to this GO's root)
/// Paths like "Armature/Hips/Spine" are resolved via Transform.Find().
/// Supports multiple materials via submeshes.
/// Bone matrices are uploaded via a float texture (no uniform array size limit).
/// </summary>
[AddComponentMenu("Rendering/Skinned Mesh Renderer")]
[ComponentIcon("\uf1b3")] // Cubes
public class SkinnedMeshRenderer : MonoBehaviour
{
    /// <summary>The mesh to render (may contain submeshes).</summary>
    public AssetRef<Mesh> SharedMesh;

    /// <summary>Materials array one per submesh. If fewer materials than submeshes, last material is reused.</summary>
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

    // Bone matrix texture each bone is 4 RGBA32F texels (one per matrix row)
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
        // During import, hierarchy is not yet reparented root is correct
        Transform hierarchyRoot = Transform;
        while (hierarchyRoot.Parent != null) hierarchyRoot = hierarchyRoot.Parent;

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

    /// <summary>
    /// Find the correct search root for bone path resolution.
    /// Walks up from this GO to find the topmost ancestor, then tries to resolve.
    /// If paths don't resolve from the absolute root (e.g. after reparenting),
    /// tries progressively lower ancestors until paths work.
    /// </summary>
    private Transform? FindSearchRoot()
    {
        // Collect ancestors from this GO up to root
        var ancestors = new System.Collections.Generic.List<Transform>();
        Transform current = Transform;
        while (current != null)
        {
            ancestors.Add(current);
            current = current.Parent;
        }

        // Try from topmost ancestor first (original import behavior)
        // If that fails (reparented), try each ancestor going down until paths resolve
        string? testPath = RootBonePath ?? (BonePaths?.Length > 0 ? BonePaths[0] : null);
        if (string.IsNullOrEmpty(testPath)) return ancestors[^1]; // fallback to absolute root

        for (int i = ancestors.Count - 1; i >= 0; i--)
        {
            if (ancestors[i].Find(testPath) != null)
                return ancestors[i];
        }

        return ancestors[^1]; // fallback
    }

    private void Resolve()
    {
        if (_resolved) return;
        _resolved = true;

        Transform? searchRoot = FindSearchRoot();
        if (searchRoot == null) return;

        // Resolve root bone by path
        if (!string.IsNullOrEmpty(RootBonePath))
            _rootBone = searchRoot.Find(RootBonePath);

        // Resolve bone array by paths
        if (BonePaths != null)
        {
            _bones = new Transform?[BonePaths.Length];
            for (int i = 0; i < BonePaths.Length; i++)
            {
                if (!string.IsNullOrEmpty(BonePaths[i]))
                    _bones[i] = searchRoot.Find(BonePaths[i]);
            }
        }
    }

    public override void OnEnable()
    {
        _resolved = false; // Force re-resolve on enable
    }

    /// <summary>
    /// Compute world-space AABB from bone positions, padded by the mesh's max vertex extent.
    /// Much cheaper than transforming every vertex, and correct for frustum culling.
    /// </summary>
    private AABB ComputeSkinnedBounds(Mesh mesh)
    {
        if (_bones == null || _bones.Length == 0)
            return mesh.bounds.TransformBy(Transform.LocalToWorldMatrix);

        // Find the max distance any vertex can be from its bone (approximated from bind-pose bounds)
        float padding = MathF.Max(MathF.Max(mesh.bounds.Size.X, mesh.bounds.Size.Y), mesh.bounds.Size.Z) * 0.5f;

        Float3 min = new Float3(float.MaxValue);
        Float3 max = new Float3(float.MinValue);

        for (int i = 0; i < _bones.Length; i++)
        {
            if (_bones[i] == null) continue;
            Float3 boneWorldPos = _bones[i].Position;
            min = new Float3(MathF.Min(min.X, boneWorldPos.X), MathF.Min(min.Y, boneWorldPos.Y), MathF.Min(min.Z, boneWorldPos.Z));
            max = new Float3(MathF.Max(max.X, boneWorldPos.X), MathF.Max(max.Y, boneWorldPos.Y), MathF.Max(max.Z, boneWorldPos.Z));
        }

        // Pad by mesh extent so vertices attached to edge bones aren't clipped
        Float3 pad = new Float3(padding);
        return new AABB(min - pad, max + pad);
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
        // So we can upload the whole array directly 4 texels per matrix, boneCount*4 texels total
        fixed (Float4x4* ptr = matrices)
        {
            _boneTexture!.SetDataPtr(ptr, 0, 0, (uint)(boneCount * 4), 1);
        }
    }

    public override void OnRenderCollect(Camera camera, List<IRenderable> renderables, List<IRenderableLight> lights)
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

        // Compute world-space bounds from bone positions (cheap, avoids culling issues)
        AABB worldBounds = ComputeSkinnedBounds(mesh);

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

            renderables.Add(new SkinnedMeshRenderable(
                mesh, mat, Transform.LocalToWorldMatrix,
                GameObject.LayerIndex, worldBounds, props, subMeshIndex: s));
        }
    }
}
