using System.Linq;
using Veldrid;

namespace Prowl.Runtime.RenderPipelines;

public sealed class MeshRenderable : Renderable
{
    public readonly Mesh mesh;

    public MeshRenderable(Mesh mesh, Material material, Bounds Bounds, Matrix4x4 matrix, byte layer, PropertyState? properties = null) : base(material, Bounds, matrix, layer, properties)
    {
        this.mesh = mesh;
    }

    public override int IndexCount => mesh.IndexCount;
    public override IndexFormat IndexFormat => mesh.IndexFormat;

    public override void SetDrawData(CommandList commandList, VertexLayoutDescription[] resources) => mesh.SetDrawData(commandList, resources);

    public override void Draw(RenderingContext context, DrawSettings settings)
    {
        if (Material != null && Material.Shader.IsAvailable)
        {
            CommandBuffer cmd = new("Mesh Renderable");
            Material.Properties.ApplyOverride(Properties);
            for (int i = 0; i < Material.Shader.Res.Passes.Count(); i++)
            {
                cmd.SetMaterial(Material, i);
                cmd.DrawSingle(this);
            }
            context.ExecuteCommandBuffer(cmd);
        }
    }
}