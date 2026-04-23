// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

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

    public override void OnRenderCollect(Camera camera, List<IRenderable> renderables, List<IRenderableLight> lights)
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

            renderables.Add(new MeshRenderable(
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
