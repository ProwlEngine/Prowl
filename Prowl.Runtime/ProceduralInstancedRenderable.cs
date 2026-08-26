// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// A renderable that draws N instances of a mesh with no per-instance buffer at all. The vertex
/// shader derives each instance from <c>gl_InstanceID</c>, so nothing is uploaded per frame and the
/// whole batch is a single draw call.
/// </summary>
public interface IProceduralInstanced
{
    /// <summary>How many instances to draw. Zero skips the batch.</summary>
    int InstanceCount { get; }
}

/// <summary>
/// Draws a mesh <see cref="InstanceCount"/> times straight from its own vertex buffers. Used by the
/// terrain detail rings, where blade placement is a pure function of world position.
/// </summary>
public sealed class ProceduralInstancedRenderable : IRenderable, IProceduralInstanced
{
    private Mesh _mesh;
    private Material _material;
    private PropertyState _properties;
    private AABB _bounds;
    private Float3 _sortPosition;
    private int _layerIndex;
    private int _subMeshIndex;

    public int InstanceCount { get; private set; }

    public ProceduralInstancedRenderable(Mesh mesh, Material material, PropertyState properties)
    {
        _mesh = mesh;
        _material = material;
        _properties = properties;
        _subMeshIndex = -1;
    }

    /// <summary>Repoint an existing renderable at this frame's state, so nothing allocates per frame.</summary>
    public void Set(Mesh mesh, Material material, PropertyState properties, int instanceCount,
        Float3 sortPosition, AABB bounds, int layerIndex, int subMeshIndex = -1)
    {
        _mesh = mesh;
        _material = material;
        _properties = properties;
        InstanceCount = instanceCount;
        _sortPosition = sortPosition;
        _bounds = bounds;
        _layerIndex = layerIndex;
        _subMeshIndex = subMeshIndex;
    }

    public Material GetMaterial() => _material;
    public int GetLayer() => _layerIndex;
    public int GetSubMeshIndex() => _subMeshIndex;
    public Float3 GetPosition() => _sortPosition;

    public void GetRenderingData(ViewerData viewer, out PropertyState properties, out Mesh mesh, out Float4x4 model, out InstanceData[]? instanceData)
    {
        properties = _properties;
        mesh = _mesh;
        model = Float4x4.Identity;
        instanceData = null; // placement is derived in the shader, there is nothing to upload
    }

    public void GetCullingData(out bool isRenderable, out AABB bounds)
    {
        isRenderable = InstanceCount > 0 && _mesh != null && _material != null;
        bounds = _bounds;
    }
}
