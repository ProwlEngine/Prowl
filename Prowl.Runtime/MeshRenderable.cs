// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

public class MeshRenderable : IRenderable
{
    private Mesh _mesh;
    private Material _material;
    private Float4x4 _transform;
    private int _layerIndex;
    private PropertyState _properties;

    public MeshRenderable(Mesh mesh, Material material, Float4x4 matrix, int layerIndex, PropertyState? propertyBlock = null)
    {
        _mesh = mesh;
        _material = material;
        _transform = matrix;
        _layerIndex = layerIndex;
        _properties = propertyBlock ?? new();
    }

    public Material GetMaterial() => _material;
    public int GetLayer() => _layerIndex;

    public Float3 GetPosition()
    {
        // Extract position from the transform matrix (4th column)
        return new Float3(_transform[0, 3], _transform[1, 3], _transform[2, 3]);
    }

    public void GetRenderingData(ViewerData viewer, out PropertyState properties, out Mesh mesh, out Float4x4 model, out InstanceData[]? instanceData)
    {
        mesh = _mesh;
        properties = _properties;
        model = _transform;
        instanceData = null; // Single instance rendering
    }

    public void GetCullingData(out bool isRenderable, out AABB bounds)
    {
        isRenderable = true;
        //bounds = Bounds.CreateFromMinMax(new Vector3(999999), new Vector3(999999));
        bounds = _mesh.bounds.TransformBy(_transform);
    }
}
