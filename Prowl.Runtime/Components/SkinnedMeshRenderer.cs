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

    /// <summary>Index into <c>Scene.BakedLighting.Lightmaps</c>, or -1 if not lightmapped. Assigned by the bake.</summary>
    [HideInInspector] public int LightmapIndex = -1;

    /// <summary>UV2 → atlas transform: <c>uv2 * xy + zw</c>. Assigned by the lightmap bake.</summary>
    [HideInInspector] public Float4 LightmapScaleOffset = new(1, 1, 0, 0);

    // Resolved at runtime
    [System.NonSerialized] private Transform? _rootBone;
    [System.NonSerialized] private Transform?[]? _bones;
    [System.NonSerialized] private Float4x4[]? _skinMatrices;
    [System.NonSerialized] private bool _resolved;

    // Skinning is only recomputed when the skeleton actually moves. The dirty check is the sum of every
    // bone's Transform.Version plus the renderer's - versions are monotonic, so an unchanged sum means a
    // static pose (skip the whole skin loop + texture upload). This also dedups redundant per-camera
    // OnRenderCollect calls in the same frame (nothing changed between cameras).
    [System.NonSerialized] private ulong _lastSkeletonVersion = ulong.MaxValue;
    [System.NonSerialized] private AABB _cachedBounds;
    // Reused per-recompute memo so bones sharing ancestors don't re-walk the chain to root each:
    // O(bones + depth) world-matrix builds instead of O(bones * depth).
    [System.NonSerialized] private readonly Dictionary<Transform, Float4x4> _worldMemo = new();

    // Bone matrix texture each bone is 4 RGBA32F texels (one per matrix row)
    [System.NonSerialized] private Texture2D? _boneTexture;
    [System.NonSerialized] private int _boneTextureSize; // number of bones the texture was allocated for

    /// <summary>
    /// Per-blend-shape weights (0-100). Length tracks the mesh's blend-shape count.
    /// Serialized so hand-posed weights persist; animation drives these at runtime.
    /// </summary>
    [SerializeField, HideInInspector]
    private float[]? _blendShapeWeights;

    // Active-morph-layer weight texture (rebuilt per frame from current weights): one texel per
    // active layer = (layerIndex, weight, 0, 0). Reused across frames.
    [System.NonSerialized] private Texture2D? _morphWeightTexture;
    [System.NonSerialized] private int _morphWeightCapacity;
    [System.NonSerialized] private Float4[]? _morphWeightData; // reused upload buffer, sized to capacity
    [System.NonSerialized] private readonly List<Float4> _activeMorphLayers = new();

    /// <summary>Number of blend shapes on the shared mesh (0 if none).</summary>
    public int BlendShapeCount => SharedMesh.Res?.BlendShapeCount ?? 0;

    /// <summary>Index of a blend shape by name, or -1 if not found.</summary>
    public int GetBlendShapeIndex(string name) => SharedMesh.Res?.GetBlendShapeIndex(name) ?? -1;

    /// <summary>The blend shape's name, or empty if out of range.</summary>
    public string GetBlendShapeName(int index) => SharedMesh.Res?.GetBlendShapeName(index) ?? string.Empty;

    /// <summary>Get a blend-shape weight (0-100). Returns 0 for invalid indices.</summary>
    public float GetBlendShapeWeight(int index)
    {
        if (_blendShapeWeights == null || index < 0 || index >= _blendShapeWeights.Length) return 0f;
        return _blendShapeWeights[index];
    }

    /// <summary>Set a blend-shape weight (0-100, may exceed for over/under-shooting morphs).</summary>
    public void SetBlendShapeWeight(int index, float weight)
    {
        int count = BlendShapeCount;
        if (index < 0 || index >= count) return;
        EnsureWeightsArray(count);
        _blendShapeWeights![index] = weight;
    }

    /// <summary>Set a blend-shape weight by name.</summary>
    public void SetBlendShapeWeight(string name, float weight)
    {
        int index = GetBlendShapeIndex(name);
        if (index >= 0) SetBlendShapeWeight(index, weight);
    }

    private void EnsureWeightsArray(int count)
    {
        if (_blendShapeWeights != null && _blendShapeWeights.Length == count) return;
        var resized = new float[count];
        if (_blendShapeWeights != null)
            Array.Copy(_blendShapeWeights, resized, Math.Min(_blendShapeWeights.Length, count));
        _blendShapeWeights = resized;
    }

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
    /// Recompute the skinning matrices, world-space bounds and bone texture for the current pose.
    /// Called only when the skeleton has actually moved (see the version check in OnRenderCollect).
    /// Each bone's world matrix is built once via <see cref="WorldMatrixOf"/> (memoized so shared
    /// ancestors aren't re-walked) and reused for both the skin matrix and the bounds corner.
    /// </summary>
    private void RecomputeSkinning(Mesh mesh)
    {
        _worldMemo.Clear();
        Float4x4 worldToLocal = Transform.WorldToLocalMatrix;

        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        bool anyBone = false;

        for (int i = 0; i < _bones!.Length; i++)
        {
            Transform? bone = _bones[i];
            if (bone != null && i < mesh.BindPoses!.Length)
            {
                Float4x4 boneWorld = WorldMatrixOf(bone);
                _skinMatrices![i] = worldToLocal * boneWorld * mesh.BindPoses[i];

                // Bone world position is the translation column of its world matrix (reuses the walk above).
                Float4 p = boneWorld.Translation;
                if (p.X < minX) minX = p.X; if (p.Y < minY) minY = p.Y; if (p.Z < minZ) minZ = p.Z;
                if (p.X > maxX) maxX = p.X; if (p.Y > maxY) maxY = p.Y; if (p.Z > maxZ) maxZ = p.Z;
                anyBone = true;
            }
            else
            {
                _skinMatrices![i] = Float4x4.Identity;
            }
        }

        // Pad by the mesh's max extent so vertices attached to edge bones aren't clipped.
        if (anyBone)
        {
            float padding = MathF.Max(MathF.Max(mesh.bounds.Size.X, mesh.bounds.Size.Y), mesh.bounds.Size.Z) * 0.5f;
            Float3 pad = new Float3(padding);
            _cachedBounds = new AABB(new Float3(minX, minY, minZ) - pad, new Float3(maxX, maxY, maxZ) + pad);
        }
        else
        {
            _cachedBounds = mesh.bounds.TransformBy(Transform.LocalToWorldMatrix);
        }

        UploadBoneTexture(_skinMatrices!);
    }

    /// <summary>
    /// World matrix of a Transform, memoized in <see cref="_worldMemo"/> for the current recompute.
    /// Mirrors <see cref="Transform.LocalToWorldMatrix"/> but avoids re-walking shared ancestor chains.
    /// </summary>
    private Float4x4 WorldMatrixOf(Transform t)
    {
        if (_worldMemo.TryGetValue(t, out Float4x4 cached))
            return cached;

        Float4x4 local = Float4x4.CreateTRS(t.LocalPosition, t.LocalRotation, t.LocalScale);
        Transform? parent = t.Parent;
        Float4x4 world = parent != null ? WorldMatrixOf(parent) * local : local;
        _worldMemo[t] = world;
        return world;
    }

    public override void OnDisable()
    {
        _boneTexture?.Dispose();
        _boneTexture = null;
        _boneTextureSize = 0;

        _morphWeightTexture?.Dispose();
        _morphWeightTexture = null;
        _morphWeightCapacity = 0;
        _morphWeightData = null;
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

    // Set by PrepareBlendShapes each frame; consumed by ApplyBlendShapeProps per submesh.
    [System.NonSerialized] private bool _morphReady;
    [System.NonSerialized] private int _morphActiveCount;

    /// <summary>
    /// Once per frame: builds the mesh's static delta textures (first use), resolves the current
    /// weights into active morph layers, and uploads the per-renderer weight texture. Cheap when idle.
    /// </summary>
    private void PrepareBlendShapes(Mesh mesh)
    {
        _morphReady = false;
        _morphActiveCount = 0;

        int shapeCount = mesh.BlendShapeCount;
        EnsureWeightsArray(shapeCount);

        // Skip all morph work (including building the delta textures) while every weight is zero,
        // so un-morphed meshes cost no VRAM or per-frame upload. The shader loop is a no-op then.
        bool anyActive = false;
        for (int s = 0; s < shapeCount; s++)
            if (MathF.Abs(_blendShapeWeights![s]) >= 1e-4f) { anyActive = true; break; }
        if (!anyActive)
            return;

        mesh.EnsureMorphTextures();
        if (mesh.MorphPositionTexture == null)
            return; // Morph textures unavailable (e.g. data too large) shader loop stays a no-op.

        // Resolve weights -> active (layer, weight) pairs, interpolating between frames by weight.
        _activeMorphLayers.Clear();
        for (int s = 0; s < shapeCount; s++)
        {
            float w = _blendShapeWeights![s];
            if (MathF.Abs(w) < 1e-4f) continue;

            int frameCount = mesh.GetBlendShapeFrameCount(s);
            if (frameCount <= 0) continue;

            if (frameCount == 1)
            {
                float fw = mesh.GetBlendShapeFrameWeight(s, 0);
                AddMorphLayer(mesh.GetMorphLayerIndex(s, 0), fw != 0f ? w / fw : 0f);
                continue;
            }

            float first = mesh.GetBlendShapeFrameWeight(s, 0);
            float last = mesh.GetBlendShapeFrameWeight(s, frameCount - 1);
            if (w <= first)
            {
                AddMorphLayer(mesh.GetMorphLayerIndex(s, 0), first != 0f ? w / first : 0f);
            }
            else if (w >= last)
            {
                AddMorphLayer(mesh.GetMorphLayerIndex(s, frameCount - 1), 1f);
            }
            else
            {
                int i = 0;
                while (i < frameCount - 1 && mesh.GetBlendShapeFrameWeight(s, i + 1) <= w) i++;
                float a = mesh.GetBlendShapeFrameWeight(s, i);
                float b = mesh.GetBlendShapeFrameWeight(s, i + 1);
                float t = b > a ? (w - a) / (b - a) : 0f;
                AddMorphLayer(mesh.GetMorphLayerIndex(s, i), 1f - t);
                AddMorphLayer(mesh.GetMorphLayerIndex(s, i + 1), t);
            }
        }

        _morphActiveCount = _activeMorphLayers.Count;
        UploadMorphWeightTexture();
        _morphReady = true;
    }

    /// <summary>Binds the morph uniforms onto a submesh's <see cref="PropertyState"/>. Cheap dictionary writes.</summary>
    private void ApplyBlendShapeProps(Mesh mesh, PropertyState props)
    {
        if (!_morphReady)
        {
            props.SetInt("morphActiveCount", 0);
            return;
        }

        Texture2D posTex = mesh.MorphPositionTexture!;
        props.SetTexture("morphPositionTexture", posTex);
        props.SetTexture("morphNormalTexture", mesh.MorphNormalTexture ?? posTex);
        props.SetTexture("morphTangentTexture", mesh.MorphTangentTexture ?? posTex);
        props.SetTexture("morphWeightTexture", _morphWeightTexture!);
        props.SetInt("morphActiveCount", _morphActiveCount);
        props.SetInt("morphTexWidth", mesh.MorphTexWidth);
        props.SetInt("morphVertexCount", mesh.VertexCount);
        props.SetInt("morphHasNormals", mesh.MorphHasNormals ? 1 : 0);
        props.SetInt("morphHasTangents", mesh.MorphHasTangents ? 1 : 0);
    }

    private void AddMorphLayer(int layer, float weight)
    {
        if (MathF.Abs(weight) < 1e-5f) return;
        _activeMorphLayers.Add(new Float4(layer, weight, 0f, 0f));
    }

    private void UploadMorphWeightTexture()
    {
        int count = Math.Max(1, _activeMorphLayers.Count); // keep a valid 1-wide texture even when idle
        if (_morphWeightTexture == null || _morphWeightCapacity < count)
        {
            _morphWeightTexture?.Dispose();
            _morphWeightTexture = new Texture2D((uint)count, 1, false, TextureImageFormat.Float4);
            _morphWeightTexture.SetTextureFilters(TextureMin.Nearest, TextureMag.Nearest);
            Graphics.SetWrapS(_morphWeightTexture.Handle, TextureWrap.ClampToEdge);
            Graphics.SetWrapT(_morphWeightTexture.Handle, TextureWrap.ClampToEdge);
            _morphWeightCapacity = count;
            _morphWeightData = new Float4[count];
        }

        // Pack into the reusable buffer (sized to the texture). The tail beyond the active count is
        // never sampled (the shader loops only over morphActiveCount), so stale tail data is fine.
        var data = _morphWeightData!;
        for (int i = 0; i < _activeMorphLayers.Count; i++)
            data[i] = _activeMorphLayers[i];
        _morphWeightTexture!.SetData<Float4>(data.AsMemory(0, _morphWeightCapacity), 0, 0, (uint)_morphWeightCapacity, 1);
    }

    public override void OnRenderCollect(Camera camera, List<IRenderable> renderables, List<IRenderableLight> lights)
    {
        var mesh = SharedMesh.Res;
        if (mesh == null || Materials.Count == 0) return;

        Resolve();

        AABB worldBounds;
        if (_bones != null && _bones.Length > 0 && mesh.BindPoses != null)
        {
            if (_skinMatrices == null || _skinMatrices.Length != _bones.Length)
            {
                _skinMatrices = new Float4x4[_bones.Length];
                _lastSkeletonVersion = ulong.MaxValue; // force a recompute after a bone-count change
            }

            // Dirty check: sum of monotonic Transform.Versions across every bone and the renderer's own
            // ancestor chain (so moving a shared character root, which bumps none of the bones, still
            // refreshes the world-space bounds used for culling). Unchanged sum => static pose, so the
            // cached skin matrices, bone texture and bounds from last frame are still valid.
            ulong version = 0;
            for (Transform? t = Transform; t != null; t = t.Parent)
                version += t.Version;
            for (int i = 0; i < _bones.Length; i++)
                if (_bones[i] != null) version += _bones[i]!.Version;

            if (version != _lastSkeletonVersion || _boneTexture == null)
            {
                _lastSkeletonVersion = version;
                RecomputeSkinning(mesh);
            }

            worldBounds = _cachedBounds;
        }
        else
        {
            worldBounds = mesh.bounds.TransformBy(Transform.LocalToWorldMatrix);
        }

        // Resolve blend-shape weights -> active morph layers + weight texture (once per frame).
        if (mesh.HasBlendShapes)
            PrepareBlendShapes(mesh);

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
            Float3 giAnchor = Float4x4.TransformPoint(mesh.bounds.Center, Transform.LocalToWorldMatrix);
            LightmapBinding.Fill(props, GameObject.Scene, LightmapIndex, LightmapScaleOffset, giAnchor, mesh.HasUV2);
            if (_boneTexture != null)
            {
                props.SetTexture("boneMatrixTexture", _boneTexture);
                props.SetInt("boneCount", _skinMatrices?.Length ?? 0);
            }

            if (mesh.HasBlendShapes)
                ApplyBlendShapeProps(mesh, props);

            renderables.Add(new SkinnedMeshRenderable(
                mesh, mat, Transform.LocalToWorldMatrix,
                GameObject.LayerIndex, worldBounds, props, subMeshIndex: s));
        }
    }
}
