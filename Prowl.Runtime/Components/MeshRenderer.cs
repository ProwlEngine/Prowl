// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Graphite.Rendering;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using GMaterial = Prowl.Graphite.Rendering.Material;
using Material = Prowl.Runtime.Resources.Material;

namespace Prowl.Runtime;

/// <summary>Whether a renderer casts shadows, and whether it draws at all outside them.</summary>
public enum ShadowCasting
{
    Off = 0,
    On = 1,

    /// <summary>Casts shadows but is not drawn in views.</summary>
    ShadowsOnly = 2,
}

public static class ShadowCastingExtensions
{
    /// <summary>The Graphite renderable flags the setting gives.</summary>
    public static RenderableFlags ToFlags(this ShadowCasting casting) => casting switch
    {
        ShadowCasting.On => RenderableFlags.CastShadows,
        ShadowCasting.ShadowsOnly => RenderableFlags.CastShadows | RenderableFlags.ShadowsOnly,
        _ => RenderableFlags.None,
    };
}

/// <summary>
/// Renders a mesh with one material per submesh. Single material meshes use Materials[0] or <see cref="Material"/>.
/// </summary>
[AddComponentMenu("Rendering/Mesh Renderer")]
[ComponentIcon("")] // Cube
public class MeshRenderer : Component, IMaterialRenderer, IRenderSync
{
    int IMaterialRenderer.MaterialSlotCount => Materials.Count;
    Material? IMaterialRenderer.GetMaterial(int slot) => MaterialSlots.Get(Materials, slot);
    void IMaterialRenderer.SetMaterial(int slot, Material? material) => MaterialSlots.Set(Materials, slot, material);

    public Mesh? Mesh;

    /// <summary>Materials, one per submesh. Submeshes past the list draw with its last material.</summary>
    public List<Material> Materials = [];

    /// <summary>Gets or sets Materials[0].</summary>
    public Material? Material
    {
        get => Materials.Count > 0 ? Materials[0] : default;
        set { if (Materials.Count == 0) Materials.Add(value); else Materials[0] = value; }
    }

    public ShadowCasting ShadowCasting = ShadowCasting.On;
    public bool ReceiveShadows = true;

    [System.NonSerialized, NotHeld] private readonly RenderableState _renderable = new();
    [System.NonSerialized, NotHeld] private GMaterial[] _gpuMaterials = [];

    /// <summary>The renderable in the scene's world, invalid while nothing draws.</summary>
    public RenderableHandle Renderable => _renderable.Handle;

    /// <summary>Kept for callers from before the renderer pushed its own changes. Does nothing.</summary>
    public void MarkVisuallyDirty() { }

    bool IRenderSync.Sync(RenderWorld world)
    {
        Mesh? mesh = Mesh;
        if (mesh.IsNotValid() || !mesh.IsLoaded)
        {
            if (mesh.IsValid() && mesh.State == AssetState.Unloaded) _ = mesh.LoadAsync();
            _renderable.Detach(world);
            return false;
        }
        mesh.Upload();

        Float4x4 matrix = Transform.LocalToWorldMatrix;
        LightmapBinding lightmap = BakedLightingBinding.For(GameObject);
        _renderable.Sync(world, new RenderableState.Desc
        {
            Mesh = mesh.GpuIfLoaded,
            Materials = RenderableState.MaterialsFor(Materials, mesh.SubMeshCount, ref _gpuMaterials),
            Transform = (Double4x4)matrix,
            TransformVersion = Transform.WorldVersion,
            Flags = RenderableState.FlagsFor(GameObject, false, ReceiveShadows) | ShadowCasting.ToFlags(),
            LayerMask = RenderableState.LayerBit(GameObject.LayerIndex),
            ObjectId = (uint)InstanceID,
            Lightmap = lightmap,
        });
        return GameObject.IsStatic && RenderableState.MaterialsReady(Materials) && !(lightmap.Lightmapped && lightmap.Texture == null);
    }

    void IRenderSync.Detach(RenderWorld world) => _renderable.Detach(world);

    /// <summary>
    /// Raycast against this renderer's mesh in world space.
    /// </summary>
    public bool Raycast(Ray worldRay, out float distance)
    {
        distance = float.MaxValue;
        Mesh? mesh = Mesh;
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
