using Prowl.Icons;
using Prowl.Runtime.RenderPipelines;
using Material = Prowl.Runtime.Material;
using Mesh = Prowl.Runtime.Mesh;

namespace Prowl.Runtime;

[ExecuteAlways]
[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Shapes}  Mesh Renderer")]
public class MeshRenderer : MonoBehaviour, ISerializable
{
    public AssetRef<Mesh> Mesh;
    public AssetRef<Material> Material;
    public Color mainColor = Color.white;

    public override void Update()
    {
        if (!Mesh.IsAvailable) return;
        if (!Material.IsAvailable) return;

        PropertyState properties = new();
        properties.SetInt("_ObjectID", this.InstanceID);
        properties.SetColor("_MainColor", mainColor);

        MeshRenderable renderable = new MeshRenderable(Mesh.Res!, Material.Res!, this.Transform.localToWorldMatrix, this.GameObject.layerIndex, Mesh.Res!.bounds, properties);

        Graphics.DrawRenderable(renderable);
    }

    public SerializedProperty Serialize(Serializer.SerializationContext ctx)
    {
        SerializedProperty compoundTag = SerializedProperty.NewCompound();
        compoundTag.Add("Mesh", Serializer.Serialize(Mesh, ctx));
        compoundTag.Add("Material", Serializer.Serialize(Material, ctx));
        compoundTag.Add("mainColor", Serializer.Serialize(mainColor, ctx));
        return compoundTag;
    }

    public void Deserialize(SerializedProperty value, Serializer.SerializationContext ctx)
    {
        Mesh = Serializer.Deserialize<AssetRef<Mesh>>(value["Mesh"], ctx);
        Material = Serializer.Deserialize<AssetRef<Material>>(value["Material"], ctx);
        mainColor = Serializer.Deserialize<Color>(value["mainColor"], ctx);
    }
}
