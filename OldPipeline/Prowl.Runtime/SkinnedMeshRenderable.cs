// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// A renderable for skinned meshes that uses pre-computed world-space bounds
/// (derived from bone positions) instead of the static mesh bind-pose bounds.
/// This ensures correct frustum culling for animated/deformed meshes.
/// </summary>
public class SkinnedMeshRenderable : IRenderable
{
    private Mesh _mesh;
    private Material _material;
    private Float4x4 _transform;
    private int _layerIndex;
    private PropertyState _properties;
    private int _subMeshIndex;
    private AABB _worldBounds;
    private Float4x4? _worldToObject;

    public SkinnedMeshRenderable(Mesh mesh, Material material, Float4x4 matrix, int layerIndex, AABB worldBounds, PropertyState? propertyBlock = null, int subMeshIndex = -1)
    {
        _mesh = mesh;
        _material = material;
        _transform = matrix;
        _layerIndex = layerIndex;
        _worldBounds = worldBounds;
        _properties = propertyBlock ?? new();
        _subMeshIndex = subMeshIndex;
    }

    public Material GetMaterial() => _material;
    public int GetLayer() => _layerIndex;

    public Float3 GetPosition()
    {
        return new Float3(_transform[0, 3], _transform[1, 3], _transform[2, 3]);
    }

    public void GetRenderingData(ViewerData viewer, out PropertyState properties, out Mesh mesh, out Float4x4 model, out InstanceData[]? instanceData)
    {
        mesh = _mesh;
        properties = _properties;
        model = _transform;
        instanceData = null;
    }

    public int GetSubMeshIndex() => _subMeshIndex;

    // The transform is fixed for this renderable's lifetime (one frame); invert once and reuse
    // across every render pass instead of re-inverting per pass.
    public Float4x4 GetWorldToObjectMatrix(in Float4x4 model) => _worldToObject ??= _transform.Invert();

    public void GetCullingData(out bool isRenderable, out AABB bounds)
    {
        isRenderable = true;
        bounds = _worldBounds; // Already in world space, no transform needed
    }
}
