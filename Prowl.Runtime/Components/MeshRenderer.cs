using Prowl.Icons;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Serialization;
using System.Numerics;
using Material = Prowl.Runtime.Resources.Material;
using Mesh = Prowl.Runtime.Resources.Mesh;

namespace Prowl.Runtime.Components;

[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Shapes}  Mesh Renderer")]
public class MeshRenderer : MonoBehaviour, ISerializable
{
    public override RenderingOrder RenderOrder => RenderingOrder.Opaque;

    public AssetRef<Mesh> Mesh;
    public AssetRef<Material> Material;
    public Color mainColor = Color.white;

    public void OnRenderObject()
    {
        if (Mesh.IsAvailable && Material.IsAvailable)
        {
            for (int i = 0; i < Material.Res!.PassCount; i++)
            {
                Material.Res!.SetPass(i);
                Graphics.DrawMeshNow(Mesh.Res!, GameObject.Global, Material.Res!, GameObject.GlobalPrevious);
                Material.Res!.EndPass();
            }
        }
    }

    public void OnRenderObjectDepth()
    {
        if (Mesh.IsAvailable)
        {
            // MatDepth should never be null here
            //var mvp = Matrix4x4.Multiply(Matrix4x4.Multiply(Graphics.MatDepthProjection, Graphics.MatDepthView), this.GameObject.Global);
            var mvp = Matrix4x4.Identity;
            mvp = Matrix4x4.Multiply(mvp, this.GameObject.Global);
            mvp = Matrix4x4.Multiply(mvp, Graphics.MatDepthView);
            mvp = Matrix4x4.Multiply(mvp, Graphics.MatDepthProjection);
            Material.Res!.SetMatrix("mvp",Matrix4x4.Transpose(mvp));
            Material.Res!.SetShadowPass(true);
            Graphics.DrawMeshNowDirect(Mesh.Res!);
            Material.Res!.EndPass();
        }
    }

    public CompoundTag Serialize(string tagName, TagSerializer.SerializationContext ctx)
    {
        CompoundTag compoundTag = new CompoundTag(tagName);
        compoundTag.Add(TagSerializer.Serialize(Mesh, "Mesh", ctx));
        compoundTag.Add(TagSerializer.Serialize(Material, "Material", ctx));
        return compoundTag;
    }

    public void Deserialize(CompoundTag value, TagSerializer.SerializationContext ctx)
    {
        Mesh = TagSerializer.Deserialize<AssetRef<Mesh>>(value["Mesh"], ctx);
        Material = TagSerializer.Deserialize<AssetRef<Material>>(value["Material"], ctx);
    }
}
