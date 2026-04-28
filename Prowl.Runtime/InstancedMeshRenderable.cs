// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// A simple instanced renderable for drawing multiple instances of a mesh.
/// Useful for drawing many copies of the same object efficiently (trees, grass, particles, etc.)
/// Uses the mesh's cached instance VAO for optimal performance.
/// </summary>
public class InstancedMeshRenderable : IRenderable
{
    private readonly Mesh _mesh;
    private readonly Material _material;
    private readonly int _layerIndex;
    private readonly PropertyState _sharedProperties;
    private readonly AABB _bounds;
    private readonly InstanceData[] _instanceData;
    private readonly Float3 _sortPosition;
    private readonly int _subMeshIndex;

    /// <param name="mesh">Source mesh whose vertex/index buffers are reused for every instance.</param>
    /// <param name="material">Material applied to all instances.</param>
    /// <param name="instanceData">Per-instance transform/colour data uploaded as the instance buffer.</param>
    /// <param name="sortPosition">World-space origin for depth sorting. Should be a stable position (e.g., particle system transform, terrain chunk center) to avoid flickering.</param>
    /// <param name="layerIndex">Layer index used for culling/render-ordering.</param>
    /// <param name="sharedProperties">Optional shared property block applied to the material; null creates an empty one.</param>
    /// <param name="bounds">Optional explicit world-space AABB for culling; falls back to the mesh bounds when null.</param>
    /// <param name="subMeshIndex">Which submesh to draw (0..mesh.SubMeshCount-1), or -1 for the entire mesh.</param>
    public InstancedMeshRenderable(
        Mesh mesh,
        Material material,
        InstanceData[] instanceData,
        Float3 sortPosition,
        int layerIndex = 0,
        PropertyState? sharedProperties = null,
        AABB? bounds = null,
        int subMeshIndex = -1)
    {
        _mesh = mesh;
        _material = material;
        _instanceData = instanceData;
        _layerIndex = layerIndex;
        _sharedProperties = sharedProperties ?? new PropertyState();
        _sortPosition = sortPosition;
        _subMeshIndex = subMeshIndex;

        // Calculate bounds if not provided
        if (bounds.HasValue)
        {
            _bounds = bounds.Value;
        }
        else if (instanceData.Length > 0 && mesh != null)
        {
            // Calculate bounds from all instances
            AABB meshBounds = mesh.bounds;
            Float3 min = new Float3(float.MaxValue);
            Float3 max = new Float3(float.MinValue);

            foreach (var instance in instanceData)
            {
                AABB instanceBounds = meshBounds.TransformBy((Float4x4)instance.GetMatrix());
                min = new Float3(
                    Maths.Min(min.X, instanceBounds.Min.X),
                    Maths.Min(min.Y, instanceBounds.Min.Y),
                    Maths.Min(min.Z, instanceBounds.Min.Z)
                );
                max = new Float3(
                    Maths.Max(max.X, instanceBounds.Max.X),
                    Maths.Max(max.Y, instanceBounds.Max.Y),
                    Maths.Max(max.Z, instanceBounds.Max.Z)
                );
            }

            _bounds = new AABB(min, max);
        }
        else
        {
            _bounds = new AABB(Float3.Zero, Float3.Zero);
        }
    }

    public Material GetMaterial() => _material;
    public int GetLayer() => _layerIndex;
    public int GetSubMeshIndex() => _subMeshIndex;

    public Float3 GetPosition()
    {
        // Return the explicit world origin provided by the caller
        return _sortPosition;
    }

    public void GetRenderingData(ViewerData viewer, out PropertyState properties, out Mesh mesh, out Float4x4 model, out InstanceData[]? instanceData)
    {
        properties = _sharedProperties;
        mesh = _mesh;
        model = Float4x4.Identity; // Not used for instanced rendering
        instanceData = _instanceData; // Return instance data for GPU instancing
    }

    public void GetCullingData(out bool isRenderable, out AABB bounds)
    {
        isRenderable = _instanceData.Length > 0 && _mesh != null && _material != null;
        bounds = _bounds;
    }

    /// <summary>
    /// Creates batched instanced renderables from transform/color/custom arrays.
    /// Automatically splits into batches of maxBatchSize for GPU limits.
    /// </summary>
    public static void CreateBatched(
        List<IRenderable> output,
        Mesh mesh,
        Material material,
        Float4x4[] transforms,
        Float3 sortPosition,
        Float4[]? colors = null,
        Float4[]? customData = null,
        int layer = 0,
        PropertyState? properties = null,
        AABB? bounds = null,
        int maxBatchSize = 1023)
    {
        if (mesh == null || material == null || transforms == null || transforms.Length == 0) return;

        int remaining = transforms.Length;
        int offset = 0;

        while (remaining > 0)
        {
            int batchSize = Maths.Min(remaining, maxBatchSize);

            var instanceData = new InstanceData[batchSize];
            for (int i = 0; i < batchSize; i++)
            {
                int idx = offset + i;
                Float4 color = colors != null && idx < colors.Length ? colors[idx] : new Float4(1, 1, 1, 1);
                Float4 custom = customData != null && idx < customData.Length ? customData[idx] : Float4.Zero;
                instanceData[i] = new InstanceData(transforms[idx], color, custom);
            }

            output.Add(new InstancedMeshRenderable(mesh, material, instanceData, sortPosition, layer, properties, bounds));

            remaining -= batchSize;
            offset += batchSize;
        }
    }
}
