using System.Linq;
using Veldrid;

namespace Prowl.Runtime.RenderPipelines;

public sealed class MeshRenderable : Renderable
{
    public readonly Mesh mesh;

    public MeshRenderable(Mesh mesh, Material material, Matrix4x4 matrix, byte layer, Bounds? bounds = null, PropertyState? properties = null) : base(material, matrix, layer, bounds, properties)
    {
        this.mesh = mesh;
    }

    public override int IndexCount => mesh.IndexCount;
    public override IndexFormat IndexFormat => mesh.IndexFormat;
    
    public override void SetDrawData(CommandList commandList, VertexLayoutDescription[] resources) => mesh.SetDrawData(commandList, resources);

    public override void Draw(CommandBuffer buffer, int pass, RenderingContext context)
    {
        buffer.SetMaterial(Material, pass);
        buffer.DrawSingle(this);
    }
}