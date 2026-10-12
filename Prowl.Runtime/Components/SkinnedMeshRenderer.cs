// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Graphite.Rendering;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using GMaterial = Prowl.Graphite.Rendering.Material;
using Material = Prowl.Runtime.Resources.Material;

namespace Prowl.Runtime;

/// <summary>
/// Renders a skinned mesh deformed by bone Transforms.
/// Bones are referenced by path (relative to this GO's root)
/// Paths like "Armature/Hips/Spine" are resolved via Transform.Find().
/// Supports multiple materials via submeshes.
/// Graphite skins and morphs the mesh on the GPU from the skin matrices and blend shape weights pushed here.
/// </summary>
[AddComponentMenu("Rendering/Skinned Mesh Renderer")]
[ComponentIcon("\uf1b3")] // Cubes
public class SkinnedMeshRenderer : Component, IMaterialRenderer, IColorTint, IRenderSync
{
    int IMaterialRenderer.MaterialSlotCount => Materials.Count;
    Material? IMaterialRenderer.GetMaterial(int slot) => MaterialSlots.Get(Materials, slot);
    void IMaterialRenderer.SetMaterial(int slot, Material? material) => MaterialSlots.Set(Materials, slot, material);
    Color IColorTint.Tint { get => MainColor; set => MainColor = value; }

    /// <summary>The mesh to render (may contain submeshes).</summary>
    public Mesh? SharedMesh;

    /// <summary>Materials array one per submesh. If fewer materials than submeshes, last material is reused.</summary>
    public List<Material> Materials = [];

    /// <summary>Path to the root bone, relative to this GO's hierarchy root.</summary>
    [SerializeField]
    public string? RootBonePath;

    /// <summary>
    /// Paths to all bone Transforms, indexed to match Mesh.BindPoses.
    /// Each path is relative to this GO's hierarchy root (e.g. "Armature/Hips/Spine").
    /// </summary>
    [SerializeField]
    public string[]? BonePaths;

    /// <summary>Tint every material draws with in place of its own _MainColor.</summary>
    public Color MainColor = Color.White;

    public ShadowCasting ShadowCasting = ShadowCasting.On;
    public bool ReceiveShadows = true;

    // Resolved at runtime
    [System.NonSerialized] private Transform? _rootBone;
    [System.NonSerialized] private Transform?[]? _bones;
    [System.NonSerialized] private Float4x4[]? _skinMatrices;
    [System.NonSerialized] private bool _resolved;

    // Skinning is only recomputed when the skeleton actually moves. The dirty check is the sum of every
    // bone's Transform.Version plus the renderer's ancestors: versions are monotonic, so an unchanged sum
    // means a static pose and the whole skin loop is skipped.
    [System.NonSerialized] private ulong _lastSkeletonVersion = ulong.MaxValue;
    [System.NonSerialized] private Mesh? _skinnedMesh;
    [System.NonSerialized] private AABB _cachedBounds;
    // Reused per-recompute memo so bones sharing ancestors don't re-walk the chain to root each:
    // O(bones + depth) world-matrix builds instead of O(bones * depth).
    [System.NonSerialized] private readonly Dictionary<Transform, Float4x4> _worldMemo = [];

    [System.NonSerialized, NotHeld] private readonly RenderableState _renderable = new();
    [System.NonSerialized, NotHeld] private GMaterial[] _baseMaterials = [];
    [System.NonSerialized, NotHeld] private GMaterial[] _gpuMaterials = [];
    [System.NonSerialized, NotHeld] private OwnedMaterial[] _tinted = [];

    /// <summary>
    /// Per-blend-shape weights (0-100). Length tracks the mesh's blend-shape count.
    /// Serialized so hand-posed weights persist; animation drives these at runtime.
    /// </summary>
    [SerializeField, HideInInspector]
    private float[]? _blendShapeWeights;

    /// <summary>The renderable in the scene's world, invalid while nothing draws.</summary>
    public RenderableHandle Renderable => _renderable.Handle;

    /// <summary>Number of blend shapes on the shared mesh (0 if none).</summary>
    public int BlendShapeCount
    {
        get { Mesh? mesh = SharedMesh; return mesh.IsValid() ? mesh.BlendShapeCount : 0; }
    }

    /// <summary>Index of a blend shape by name, or -1 if not found.</summary>
    public int GetBlendShapeIndex(string name)
    {
        Mesh? mesh = SharedMesh;
        return mesh.IsValid() ? mesh.GetBlendShapeIndex(name) : -1;
    }

    /// <summary>The blend shape's name, or empty if out of range.</summary>
    public string GetBlendShapeName(int index)
    {
        Mesh? mesh = SharedMesh;
        return mesh.IsValid() ? mesh.GetBlendShapeName(index) : string.Empty;
    }

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
        float[] resized = new float[count];
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
    /// Recompute the skin matrices and the bounds of the current pose, both in this renderer's object space.
    /// Called only when the skeleton has actually moved (see the version check in Sync).
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
                Float4x4 boneLocal = worldToLocal * WorldMatrixOf(bone);
                _skinMatrices![i] = boneLocal * mesh.BindPoses[i];

                Float4 p = boneLocal.Translation;
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
        _cachedBounds = anyBone ? Padded(mesh, new AABB(new Float3(minX, minY, minZ), new Float3(maxX, maxY, maxZ))) : mesh.bounds;
    }

    private static AABB Padded(Mesh mesh, AABB bounds)
    {
        float padding = MathF.Max(MathF.Max(mesh.bounds.Size.X, mesh.bounds.Size.Y), mesh.bounds.Size.Z) * 0.5f;
        Float3 pad = new(padding);
        return new AABB(bounds.Min - pad, bounds.Max + pad);
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

    void IRenderSync.Sync(RenderWorld world)
    {
        Mesh? mesh = SharedMesh;
        if (mesh.IsNotValid() || !mesh.IsLoaded || Materials.Count == 0)
        {
            if (mesh.IsValid() && mesh.State == AssetState.Unloaded) _ = mesh.LoadAsync();
            Detach(world);
            return;
        }
        mesh.Upload();

        Resolve();

        Float4x4 matrix = Transform.LocalToWorldMatrix;
        bool skinned = _bones != null && _bones.Length > 0 && mesh.BindPoses != null;
        if (skinned)
        {
            if (_skinMatrices == null || _skinMatrices.Length != _bones!.Length)
            {
                _skinMatrices = new Float4x4[_bones!.Length];
                _lastSkeletonVersion = ulong.MaxValue;
            }

            ulong version = 0;
            for (Transform? t = Transform; t != null; t = t.Parent)
                version += t.Version;
            for (int i = 0; i < _bones.Length; i++)
                if (_bones[i] != null) version += _bones[i]!.Version;

            if (version != _lastSkeletonVersion || _skinnedMesh != mesh)
            {
                _lastSkeletonVersion = version;
                _skinnedMesh = mesh;
                RecomputeSkinning(mesh);
            }
        }

        AABB? bounds = skinned ? _cachedBounds : mesh.HasBlendShapes ? Padded(mesh, mesh.bounds) : null;

        _renderable.Sync(world, new RenderableState.Desc
        {
            Mesh = mesh.GpuIfLoaded,
            Materials = TintedMaterials(mesh.SubMeshCount),
            Transform = (Double4x4)matrix,
            TransformVersion = Transform.WorldVersion,
            Flags = RenderableState.FlagsFor(GameObject, false, ReceiveShadows, motionVectors: true) | ShadowCasting.ToFlags(),
            LayerMask = RenderableState.LayerBit(GameObject.LayerIndex),
            ObjectId = (uint)InstanceID,
            Lightmap = BakedLightingBinding.For(GameObject),
            Bounds = bounds,
        });

        RenderableHandle handle = _renderable.Handle;
        if (!handle.IsValid) return;

        world.SetBones(handle, skinned ? _skinMatrices : ReadOnlySpan<Float4x4>.Empty);

        int shapeCount = mesh.BlendShapeCount;
        if (shapeCount > 0)
        {
            EnsureWeightsArray(shapeCount);
            world.SetBlendShapeWeights(handle, _blendShapeWeights);
        }
        else
        {
            world.SetBlendShapeWeights(handle, ReadOnlySpan<float>.Empty);
        }
    }

    // Each slot's material as a child carrying MainColor as its _MainColor
    private GMaterial[] TintedMaterials(int subMeshCount)
    {
        GMaterial[] materials = RenderableState.MaterialsFor(Materials, subMeshCount, ref _baseMaterials);
        if (_tinted.Length != materials.Length)
        {
            foreach (OwnedMaterial old in _tinted) old.Release();
            _tinted = new OwnedMaterial[materials.Length];
            for (int i = 0; i < _tinted.Length; i++) _tinted[i] = new OwnedMaterial();
        }
        if (_gpuMaterials.Length != materials.Length) _gpuMaterials = new GMaterial[materials.Length];
        for (int i = 0; i < materials.Length; i++)
        {
            _gpuMaterials[i] = _tinted[i].Of(materials[i])!;
            _tinted[i].SetColor("_MainColor", MainColor);
        }
        return _gpuMaterials;
    }

    void IRenderSync.Detach(RenderWorld world) => Detach(world);

    private void Detach(RenderWorld world)
    {
        _renderable.Detach(world);
        foreach (OwnedMaterial tinted in _tinted) tinted.Release();
    }
}
