// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime.Events;
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

    public override void OnRenderCollect(SceneEvents.OnRenderCollectArgs args)
    {
        var mesh = Mesh.Res;
        if (mesh == null || Materials.Count == 0) return;

        int subCount = mesh.SubMeshCount;
        for (int s = 0; s < subCount; s++)
        {
            Material? mat = null;
            if (s < Materials.Count)
                mat = Materials[s].Res;
            else if (Materials.Count > 0)
                mat = Materials[^1].Res;

            if (mat == null) continue;

            PropertyState props = new();
            props.SetInt("_ObjectID", InstanceID);
            // A blend-shape mesh forces the BLENDSHAPES shader variant (keyword is mesh-derived).
            // MeshRenderer doesn't drive morph weights, so pin the morph loop to a no-op rather than
            // inherit a stale count from a previous skinned draw using the same program.
            if (mesh.HasBlendShapes)
                props.SetInt("morphActiveCount", 0);
            Float3 giAnchor = Float4x4.TransformPoint(mesh.bounds.Center, Transform.LocalToWorldMatrix);
            LightmapBinding.Fill(props, GameObject.Scene, LightmapIndex, LightmapScaleOffset, giAnchor, mesh.HasUV2);

            args.renderables.Add(new MeshRenderable(
                mesh, mat, Transform.LocalToWorldMatrix,
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
