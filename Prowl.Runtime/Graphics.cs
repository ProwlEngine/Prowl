// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.GraphicsBackend;
using Prowl.Runtime.GraphicsBackend.Primitives;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

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

    /// <param name="sortPosition">World-space origin for depth sorting. Should be a stable position (e.g., particle system transform, terrain chunk center) to avoid flickering.</param>
    public InstancedMeshRenderable(
        Mesh mesh,
        Material material,
        InstanceData[] instanceData,
        Float3 sortPosition,
        int layerIndex = 0,
        PropertyState? sharedProperties = null,
        AABB? bounds = null)
    {
        _mesh = mesh;
        _material = material;
        _instanceData = instanceData;
        _layerIndex = layerIndex;
        _sharedProperties = sharedProperties ?? new PropertyState();
        _sortPosition = sortPosition;

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
}

public static class Graphics
{
    public static GraphicsDevice Device { get; internal set; }

#warning TODO: Move these to a separate class "GraphicsCapabilities" and add more, Their Assigned by GLDevice which is very ugly
    public static int MaxTextureSize { get; internal set; }
    public static int MaxCubeMapTextureSize { get; internal set; }
    public static int MaxArrayTextureLayers { get; internal set; }
    public static int MaxFramebufferColorAttachments { get; internal set; }

    public static Float2 ScreenSize => new(Window.InternalWindow.FramebufferSize.X, Window.InternalWindow.FramebufferSize.Y);
    public static IntRect ScreenRect => new(0, 0, Window.InternalWindow.FramebufferSize.X, Window.InternalWindow.FramebufferSize.Y);

    // ============================================================================
    // QUEUED RENDERING API - Unity-style Graphics.DrawMesh/DrawMeshInstanced
    // ============================================================================

    /// <summary>
    /// Queues a single mesh to be rendered by pushing it to the scene's render queue.
    /// The mesh will be rendered during the next frame with the specified material and transform.
    /// </summary>
    /// <param name="scene">Scene to push the renderable to</param>
    /// <param name="mesh">Mesh to render</param>
    /// <param name="transform">World transform matrix</param>
    /// <param name="material">Material to render with</param>
    /// <param name="layer">Layer index for culling and sorting (default: 0)</param>
    /// <param name="properties">Optional per-object property overrides</param>
    public static void DrawMesh(Scene scene, Mesh mesh, Float4x4 transform, Material material, int layer = 0, PropertyState? properties = null)
    {
        if (scene == null || mesh == null || material == null) return;

        var renderable = new MeshRenderable(mesh, material, transform, layer, properties);
        scene.PushRenderable(renderable);
    }

    /// <summary>
    /// Queues multiple instances of a mesh to be rendered with GPU instancing.
    /// Automatically handles batching for large instance counts (>1023 instances).
    /// </summary>
    /// <param name="scene">Scene to push the renderable to</param>
    /// <param name="mesh">Mesh to render</param>
    /// <param name="transforms">Array of world transforms (one per instance)</param>
    /// <param name="material">Material to render with</param>
    /// <param name="worldOrigin">World-space origin for depth sorting (e.g., particle system transform position, terrain chunk center)</param>
    /// <param name="layer">Layer index for culling and sorting (default: 0)</param>
    /// <param name="properties">Optional shared properties for all instances</param>
    /// <param name="bounds">Optional custom bounds for culling. If null, computed from mesh bounds.</param>
    /// <param name="maxBatchSize">Maximum instances per batch (default: 1023)</param>
    public static void DrawMeshInstanced(Scene scene, Mesh mesh, Float4x4[] transforms, Material material, Float3 worldOrigin, int layer = 0, PropertyState? properties = null, AABB? bounds = null, int maxBatchSize = 1023)
    {
        if (scene == null || mesh == null || material == null || transforms == null || transforms.Length == 0) return;

        // Automatic batching for >1023 instances by default
        int remainingInstances = transforms.Length;
        int offset = 0;

        while (remainingInstances > 0)
        {
            int batchSize = Maths.Min(remainingInstances, maxBatchSize);

            // Create instance data for this batch
            var instanceData = new Rendering.InstanceData[batchSize];
            for (int i = 0; i < batchSize; i++)
            {
                instanceData[i] = new Rendering.InstanceData(transforms[offset + i]);
            }

            // Push batch to scene
            var renderable = new InstancedMeshRenderable(mesh, material, instanceData, worldOrigin, layer, properties, bounds);
            scene.PushRenderable(renderable);

            remainingInstances -= batchSize;
            offset += batchSize;
        }
    }

    /// <summary>
    /// Queues multiple instances with per-instance colors.
    /// Automatically handles batching for large instance counts (>1023 instances).
    /// </summary>
    /// <param name="worldOrigin">World-space origin for depth sorting (e.g., particle system transform position, terrain chunk center)</param>
    public static void DrawMeshInstanced(Scene scene, Mesh mesh, Float4x4[] transforms, Material material, Float4[] colors, Float3 worldOrigin, int layer = 0, PropertyState? properties = null, AABB? bounds = null, int maxBatchSize = 1023)
    {
        if (scene == null || mesh == null || material == null || transforms == null || transforms.Length == 0) return;

        // Automatic batching for >1023 instances by default
        int remainingInstances = transforms.Length;
        int offset = 0;

        while (remainingInstances > 0)
        {
            int batchSize = Maths.Min(remainingInstances, maxBatchSize);

            // Create instance data for this batch with colors
            var instanceData = new Rendering.InstanceData[batchSize];
            for (int i = 0; i < batchSize; i++)
            {
                int idx = offset + i;
                Float4 color = idx < colors.Length ? colors[idx] : new Float4(1, 1, 1, 1);
                instanceData[i] = new Rendering.InstanceData(transforms[idx], color);
            }

            // Push batch to scene
            var renderable = new InstancedMeshRenderable(mesh, material, instanceData, worldOrigin, layer, properties, bounds);
            scene.PushRenderable(renderable);

            remainingInstances -= batchSize;
            offset += batchSize;
        }
    }

    /// <summary>
    /// Queues multiple instances with optional per-instance colors and custom data.
    /// This is the most flexible overload for custom per-instance data (UV offsets, lifetimes, etc.)
    /// Automatically handles batching for large instance counts.
    /// </summary>
    /// <param name="scene">Scene to push the renderable to</param>
    /// <param name="mesh">Mesh to render</param>
    /// <param name="transforms">Array of world transforms (one per instance)</param>
    /// <param name="material">Material to render with</param>
    /// <param name="worldOrigin">World-space origin for depth sorting (e.g., particle system transform position, terrain chunk center)</param>
    /// <param name="colors">Optional per-instance colors (RGBA). If null, defaults to white.</param>
    /// <param name="customData">Optional per-instance custom data (4 floats). Useful for UV offsets, lifetimes, etc.</param>
    /// <param name="layer">Layer index for culling and sorting (default: 0)</param>
    /// <param name="properties">Optional shared properties for all instances</param>
    /// <param name="bounds">Optional custom bounds for culling. If null, computed from mesh bounds.</param>
    /// <param name="maxBatchSize">Maximum instances per batch (default: 1023)</param>
    public static void DrawMeshInstanced(
        Scene scene,
        Mesh mesh,
        Float4x4[] transforms,
        Material material,
        Float3 worldOrigin,
        Float4[]? colors = null,
        Float4[]? customData = null,
        int layer = 0,
        PropertyState? properties = null,
        AABB? bounds = null,
        int maxBatchSize = 1023)
    {
        if (scene == null || mesh == null || material == null || transforms == null || transforms.Length == 0) return;

        // Automatic batching for >maxBatchSize instances
        int remainingInstances = transforms.Length;
        int offset = 0;

        while (remainingInstances > 0)
        {
            int batchSize = Maths.Min(remainingInstances, maxBatchSize);

            // Build InstanceData from separate arrays
            var instanceData = new Rendering.InstanceData[batchSize];
            for (int i = 0; i < batchSize; i++)
            {
                int idx = offset + i;
                Float4 color = colors != null && idx < colors.Length ? colors[idx] : new Float4(1, 1, 1, 1);
                Float4 custom = customData != null && idx < customData.Length ? customData[idx] : Float4.Zero;
                instanceData[i] = new Rendering.InstanceData(transforms[idx], color, custom);
            }

            // Push batch to scene
            var renderable = new InstancedMeshRenderable(mesh, material, instanceData, worldOrigin, layer, properties, bounds);
            scene.PushRenderable(renderable);

            remainingInstances -= batchSize;
            offset += batchSize;
        }
    }

    public static void Initialize()
    {
        Device = new GraphicsDevice();
        Device.Initialize(true);
    }

    public static void StartFrame()
    {
        Device.UnbindFramebuffer();
        Device.Viewport(0, 0, (uint)Window.InternalWindow.FramebufferSize.X, (uint)Window.InternalWindow.FramebufferSize.Y);
        Device.SetState(new(), true);

        Device.BindVertexArray(null);
        Device.Clear(0, 0, 0, 1, ClearFlags.Color | ClearFlags.Depth | ClearFlags.Stencil);

        ShadowAtlas.TryInitialize();
        ShadowAtlas.Clear();
    }

    public static void EndFrame()
    {
        RenderTexture.UpdatePool();
    }

    public static void Dispose()
    {
        Device.Dispose();
    }
}
