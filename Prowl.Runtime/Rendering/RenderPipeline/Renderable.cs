using Veldrid;

namespace Prowl.Runtime.RenderPipelines;

/// <summary>
/// A High-Level abstraction of IGeometryDrawData which provides data about the object.
/// This is used to render objects in the scene with Culling and Sorting.
/// </summary>
public abstract class Renderable(Material material, Matrix4x4 matrix, byte layer, Bounds? bounds = null, PropertyState? properties = null) : IGeometryDrawData
{
    public readonly Material Material = material;
    public readonly Matrix4x4 Matrix = matrix;
    public readonly Bounds? Bounds = bounds;
    public readonly Bounds? WorldBounds = bounds?.Transform(matrix) ?? null;
    public readonly byte Layer = layer;
    public readonly PropertyState? Properties = properties;

    public abstract int IndexCount { get; }
    public abstract IndexFormat IndexFormat { get; }

    public abstract void SetDrawData(CommandList commandList, VertexLayoutDescription[] resources);
    public abstract void Draw(CommandBuffer buffer, int pass, RenderingContext context);
}
