// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Graphite.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using GLightmapBinding = Prowl.Graphite.Rendering.LightmapBinding;
using GMaterial = Prowl.Graphite.Rendering.Material;
using Material = Prowl.Runtime.Resources.Material;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// One renderable a component keeps in a world, and what it last pushed, so a frame where nothing changed costs a few
/// comparisons. Components fill a <see cref="Desc"/> each frame and call <see cref="Sync"/>.
/// </summary>
public sealed class RenderableState
{
    /// <summary>What the renderable should be this frame.</summary>
    public struct Desc
    {
        public GpuMesh? Mesh;
        public GMaterial[] Materials;
        public Double4x4 Transform;
        public uint TransformVersion;
        public RenderableFlags Flags;
        public uint LayerMask;
        public uint ObjectId;
        public GLightmapBinding Lightmap;
        public Float4 Custom0, Custom1;
        public AABB? Bounds;
        public sbyte SortBias;
    }

    private RenderableHandle _handle;
    private GpuMesh? _mesh;
    private GMaterial[] _materials = [];
    private uint _transformVersion;
    private RenderableFlags _flags;
    private uint _layerMask;
    private uint _objectId;
    private GLightmapBinding _lightmap;
    private Float4 _custom0, _custom1;
    private AABB? _bounds;
    private sbyte _sortBias;
    private RenderWorld? _world;

    /// <summary>The renderable, invalid while there is nothing to draw.</summary>
    public RenderableHandle Handle => _handle;

    public bool IsAlive => _world != null && _handle.IsValid && _world.IsAlive(_handle);

    /// <summary>Adds, updates or removes the renderable to match <paramref name="desc"/>. Nothing draws without a mesh and a material.</summary>
    public void Sync(RenderWorld world, in Desc desc)
    {
        if (_world != null && _world != world) Detach(_world);

        if (desc.Mesh == null || desc.Mesh.IsDisposed || desc.Materials.Length == 0)
        {
            Detach(world);
            return;
        }

        if (!_handle.IsValid || !world.IsAlive(_handle))
        {
            RenderableDesc add = new()
            {
                Mesh = desc.Mesh,
                Transform = desc.Transform,
                LayerMask = desc.LayerMask,
                Flags = desc.Flags,
                Custom0 = desc.Custom0,
                Custom1 = desc.Custom1,
                ObjectId = desc.ObjectId,
                Lightmap = desc.Lightmap,
                SortBias = desc.SortBias,
            };
            _handle = world.Add(add, desc.Materials);
            _world = world;
            _mesh = desc.Mesh;
            _materials = (GMaterial[])desc.Materials.Clone();
            _transformVersion = desc.TransformVersion;
            _flags = desc.Flags;
            _layerMask = desc.LayerMask;
            _objectId = desc.ObjectId;
            _lightmap = desc.Lightmap;
            _custom0 = desc.Custom0;
            _custom1 = desc.Custom1;
            _sortBias = desc.SortBias;
            _bounds = desc.Bounds;
            if (desc.Bounds is { } bounds) world.SetBounds(_handle, bounds);
            return;
        }

        if (_mesh != desc.Mesh) { world.SetMesh(_handle, desc.Mesh); _mesh = desc.Mesh; }
        if (!SameMaterials(desc.Materials)) { world.SetMaterials(_handle, desc.Materials); _materials = (GMaterial[])desc.Materials.Clone(); }
        if (_transformVersion != desc.TransformVersion) { world.SetTransform(_handle, desc.Transform); _transformVersion = desc.TransformVersion; }
        if (_flags != desc.Flags) { world.SetFlags(_handle, desc.Flags); _flags = desc.Flags; }
        if (_layerMask != desc.LayerMask) { world.SetLayerMask(_handle, desc.LayerMask); _layerMask = desc.LayerMask; }
        if (_objectId != desc.ObjectId) { world.SetObjectId(_handle, desc.ObjectId); _objectId = desc.ObjectId; }
        if (!_lightmap.Equals(desc.Lightmap)) { world.SetLightmap(_handle, desc.Lightmap); _lightmap = desc.Lightmap; }
        if (_custom0 != desc.Custom0 || _custom1 != desc.Custom1) { world.SetCustomData(_handle, desc.Custom0, desc.Custom1); (_custom0, _custom1) = (desc.Custom0, desc.Custom1); }
        if (_sortBias != desc.SortBias) { world.SetSortBias(_handle, desc.SortBias); _sortBias = desc.SortBias; }
        if (!Nullable.Equals(_bounds, desc.Bounds))
        {
            if (desc.Bounds is { } bounds) world.SetBounds(_handle, bounds);
            else world.ClearBounds(_handle);
            _bounds = desc.Bounds;
        }
    }

    private bool SameMaterials(GMaterial[] materials)
    {
        if (materials.Length != _materials.Length) return false;
        for (int i = 0; i < materials.Length; i++)
            if (materials[i] != _materials[i]) return false;
        return true;
    }

    /// <summary>Removes the renderable from the world it is in.</summary>
    public void Detach(RenderWorld world)
    {
        if (_handle.IsValid && world.IsAlive(_handle))
            world.Remove(_handle);
        _handle = default;
        _world = null;
        _mesh = null;
        _materials = [];
    }

    /// <summary>The flags a component's shadow settings and static flag give.</summary>
    public static RenderableFlags FlagsFor(GameObject gameObject, bool castShadows, bool receiveShadows, bool motionVectors = false)
    {
        RenderableFlags flags = RenderableFlags.None;
        if (castShadows) flags |= RenderableFlags.CastShadows;
        if (receiveShadows) flags |= RenderableFlags.ReceiveShadows;
        if (gameObject.IsStatic) flags |= RenderableFlags.Static;
        if (motionVectors) flags |= RenderableFlags.MotionVectors;
        return flags;
    }

    /// <summary>A layer index as a layer mask bit.</summary>
    public static uint LayerBit(int layerIndex) => 1u << Math.Clamp(layerIndex, 0, 31);

    /// <summary>
    /// The Graphite materials for each submesh: the renderer's material for that slot, its last for slots past its
    /// list, the default material for a slot whose material is not loaded yet. Empty when nothing can draw.
    /// </summary>
    public static GMaterial[] MaterialsFor(System.Collections.Generic.IReadOnlyList<Material?> materials, int subMeshCount, ref GMaterial[] buffer)
    {
        if (materials.Count == 0 || subMeshCount == 0) return [];
        if (buffer.Length != subMeshCount) buffer = new GMaterial[subMeshCount];
        for (int s = 0; s < subMeshCount; s++)
        {
            Material? material = s < materials.Count ? materials[s] : materials[^1];
            GMaterial? gpu = material.IsValid() && material.IsLoaded ? material.GpuMaterial : null;
            if (gpu == null)
            {
                if (material.IsValid() && material.State == AssetState.Unloaded) _ = material.LoadAsync();
                gpu = DefaultGpuMaterial;
            }
            if (gpu == null) return [];
            buffer[s] = gpu;
        }
        return buffer;
    }

    private static Material? s_default;

    /// <summary>The Graphite material drawn where a renderer's own is not ready.</summary>
    public static GMaterial? DefaultGpuMaterial
    {
        get
        {
            if (s_default.IsNotValid()) s_default = new Material(Shader.LoadDefault(DefaultShader.Lit));
            return s_default.GpuMaterial;
        }
    }
}
