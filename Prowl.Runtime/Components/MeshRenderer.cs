// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Graphite;

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Renders a static mesh with one or more materials (one per submesh).
/// For single-material meshes, use Materials[0] or the legacy Material property.
/// </summary>
[AddComponentMenu("Rendering/Mesh Renderer")]
[ComponentIcon("\uf1b2")] // Cube
public class MeshRenderer : MonoBehaviour
{
    public AssetRef<Mesh> Mesh;

    /// <summary>Materials array one per submesh. Legacy single-material meshes use index 0.</summary>
    public List<AssetRef<Material>> Materials = new();

    /// <summary>Legacy single-material accessor. Gets/sets Materials[0].</summary>
    public AssetRef<Material> Material
    {
        get => Materials.Count > 0 ? Materials[0] : default;
        set { if (Materials.Count == 0) Materials.Add(value); else Materials[0] = value; }
    }

    /// <summary>Index into <c>Scene.BakedLighting.Lightmaps</c>, or -1 if this renderer isn't
    /// lightmapped. Assigned by the lightmap bake. Lightmap-static is driven by <c>GameObject.IsStatic</c>.</summary>
    [HideInInspector] public int LightmapIndex = -1;

    /// <summary>UV2 → atlas transform: <c>uv2 * xy + zw</c>. Assigned by the lightmap bake.</summary>
    [HideInInspector] public Float4 LightmapScaleOffset = new(1, 1, 0, 0);

    // Per-instance property blocks, reused across frames so a static scene collects without allocating.
    // The command buffer snapshots these at encode time, so mutating them next frame is safe.
    [System.NonSerialized] private PropertySet[] _propCache;

    public override void OnRenderCollect(SceneCuller culler)
    {
        var mesh = Mesh.Res;
        if (mesh == null || Materials.Count == 0) return;

        int subCount = mesh.SubMeshCount;
        if (_propCache == null || _propCache.Length != subCount)
        {
            _propCache = new PropertySet[subCount];
            for (int i = 0; i < subCount; i++)
                _propCache[i] = new PropertySet();
        }

        // LocalToWorldMatrix is cached on Transform, so this is cheap for a static renderer.
        Float4x4 world = Transform.LocalToWorldMatrix;

        // AssetRef<T> caches its resolved instance as a side effect of .Res - List<T>'s indexer
        // returns value-type elements by copy, so Materials[s].Res would mutate a throwaway copy
        // and never actually cache anything. CollectionsMarshal.AsSpan gives a ref to the real
        // backing elements so the cache (and the async-load dedup it drives) actually sticks.
        var materials = CollectionsMarshal.AsSpan(Materials);
        for (int s = 0; s < subCount; s++)
        {
            Material? mat = null;
            if (s < materials.Length)
                mat = materials[s].Res;
            else if (materials.Length > 0)
                mat = materials[^1].Res;

            if (mat == null) continue;

            PropertySet props = _propCache[s];
            props.Clear();
            props.SetInt("_ObjectID", InstanceID);
            // A blend-shape mesh forces the BLENDSHAPES shader variant (keyword is mesh-derived).
            // MeshRenderer doesn't drive morph weights, so pin the morph loop to a no-op rather than
            // inherit a stale count from a previous skinned draw using the same program.
            if (mesh.HasBlendShapes)
                props.SetInt("morphActiveCount", 0);

            culler.Add(new MeshRenderable(
                mesh, mat, world,
                GameObject.LayerIndex, props, subMeshIndex: subCount > 1 ? s : -1));
        }
    }

    /// <summary>
    /// Raycast against this renderer's mesh in world space.
    /// </summary>
    public bool Raycast(Ray worldRay, out float distance)
    {
        distance = float.MaxValue;
        var mesh = Mesh.Res;
        if (mesh == null) return false;

        Float4x4 worldToLocal = Transform.WorldToLocalMatrix;
        Float3 localOrigin = Float4x4.TransformPoint(worldRay.Origin, worldToLocal);
        Float3 localDirRaw = Float4x4.TransformPoint(worldRay.Origin + worldRay.Direction, worldToLocal) - localOrigin;
        Float3 localDir = Float3.Normalize(localDirRaw);
        var localRay = new Ray(localOrigin, localDir);

        if (!localRay.Intersects(mesh.bounds, out _, out _))
            return false;

        if (mesh.Raycast(localRay, out float localDist))
        {
            Float3 localHit = localOrigin + localDir * localDist;
            Float3 worldHit = Float4x4.TransformPoint(localHit, Transform.LocalToWorldMatrix);
            distance = Float3.Distance(worldRay.Origin, worldHit);
            return true;
        }
        return false;
    }
}
