// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Echo;

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
    int IMaterialRenderer.MaterialSlotCount => materials.Count;
    Material? IMaterialRenderer.GetMaterial(int slot) => MaterialSlots.Get(materials, slot);
    void IMaterialRenderer.SetMaterial(int slot, Material? material) { MaterialSlots.Set(materials, slot, material); Wake(); }

    [SerializeField] private Mesh? mesh;
    [SerializeField] private List<Material> materials = [];
    [SerializeField] private ShadowCasting shadowCasting = ShadowCasting.On;
    [SerializeField] private bool receiveShadows = true;

    public Mesh? Mesh
    {
        get => mesh;
        set { mesh = value; Wake(); }
    }

    /// <summary>Materials, one per submesh. Submeshes past the list draw with its last material. Set a new list to change them.</summary>
    public IReadOnlyList<Material> Materials
    {
        get => materials;
        set { materials = [.. value]; Wake(); }
    }

    /// <summary>Gets or sets Materials[0].</summary>
    public Material? Material
    {
        get => materials.Count > 0 ? materials[0] : default;
        set
        {
            if (materials.Count == 0) materials.Add(value!); else materials[0] = value!;
            Wake();
        }
    }

    public ShadowCasting ShadowCasting
    {
        get => shadowCasting;
        set { shadowCasting = value; Wake(); }
    }

    public bool ReceiveShadows
    {
        get => receiveShadows;
        set { receiveShadows = value; Wake(); }
    }

    Mesh? IRenderSync.SyncedMesh => mesh;

    // Syncs it again next frame, for a setting that changed
    private void Wake() => SceneRendering.Wake(this, GameObject);

    [System.NonSerialized, NotHeld] private readonly RenderableState _renderable = new();
    [System.NonSerialized, NotHeld] private GMaterial[] _gpuMaterials = [];

    /// <summary>The renderable in the scene's world, invalid while nothing draws.</summary>
    public RenderableHandle Renderable => _renderable.Handle;

    /// <summary>Syncs the renderer again next frame, for a change its setters cannot see, like a material list edited in place.</summary>
    public void MarkVisuallyDirty() => Wake();

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
        return RenderableState.MaterialsReady(Materials) && !(lightmap.Lightmapped && lightmap.Texture == null);
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
