// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Runtime.InteropServices;

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
    public Mesh? Mesh;

    /// <summary>Materials array one per submesh. Legacy single-material meshes use index 0.</summary>
    public List<Material> Materials = new();

    /// <summary>Legacy single-material accessor. Gets/sets Materials[0].</summary>
    public Material? Material
    {
        get => Materials.Count > 0 ? Materials[0] : default;
        set { if (Materials.Count == 0) Materials.Add(value); else Materials[0] = value; }
    }


    // Per-instance property blocks and renderables, reused across frames so a static scene collects without allocating.
    // The command buffer snapshots these at encode time, so mutating them next frame is safe.
    [System.NonSerialized] private PropertyState[] _propCache;
    [System.NonSerialized] private MeshRenderable[] _renderableCache;
    [System.NonSerialized] private Mesh _propMesh;

    public override void OnRenderCollect(Camera camera, List<IRenderable> renderables, List<IRenderableLight> lights)
    {
        // Something still loading is skipped this frame rather than waited on.
        var mesh = Mesh;
        if (mesh is not { IsLoaded: true } || Materials.Count == 0) return;

        int subCount = mesh.SubMeshCount;
        if (_propCache == null || _propCache.Length != subCount)
        {
            _propCache = new PropertyState[subCount];
            _renderableCache = new MeshRenderable[subCount];
            for (int i = 0; i < subCount; i++)
                _propCache[i] = new PropertyState();
        }

        // LocalToWorldMatrix is cached on Transform, so this is cheap for a static renderer.
        Float4x4 world = Transform.LocalToWorldMatrix;
        Float3 giAnchor = Float4x4.TransformPoint(mesh.bounds.Center, world);

        for (int s = 0; s < subCount; s++)
        {
            Material? mat = s < Materials.Count ? Materials[s] : Materials[^1];
            if (mat is not { IsLoaded: true }) continue;

            // Refilled in place, so a renderer whose values did not change keeps its draw snapshot. A new mesh
            // or GI mode would leave keys behind, so those start from empty.
            PropertyState props = _propCache[s];
            if (_propMesh != mesh) props.Clear();
            int giMode = props.GetInt("_GIMode");
            FillProperties(props, mesh, giAnchor);
            if (props.GetInt("_GIMode") != giMode)
            {
                props.Clear();
                FillProperties(props, mesh, giAnchor);
            }

            MeshRenderable renderable = _renderableCache[s] ??= new MeshRenderable(mesh, mat, world, 0);
            renderable.Set(mesh, mat, world, GameObject.LayerIndex, props, subMeshIndex: subCount > 1 ? s : -1);
            renderables.Add(renderable);
        }
        _propMesh = mesh;
    }

    private void FillProperties(PropertyState props, Mesh mesh, Float3 giAnchor)
    {
        props.SetInt("_ObjectID", InstanceID);
        // A blend-shape mesh forces the BLENDSHAPES shader variant (keyword is mesh-derived).
        // MeshRenderer doesn't drive morph weights, so pin the morph loop to a no-op rather than
        // inherit a stale count from a previous skinned draw using the same program.
        if (mesh.HasBlendShapes)
            props.SetInt("morphActiveCount", 0);
        LightmapBinding.Fill(props, GameObject, giAnchor, mesh.HasUV2);
    }

    /// <summary>
    /// Raycast against this renderer's mesh in world space.
    /// </summary>
    public bool Raycast(Ray worldRay, out float distance)
    {
        distance = float.MaxValue;
        var mesh = Mesh;
        if (mesh is not { IsLoaded: true }) return false;

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
