// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Icons;

namespace Prowl.Runtime;

[ExecuteAlways]
[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Shapes}  Mesh Renderer")]
public class MeshRenderer : MonoBehaviour, ISerializable, IRenderable
{
    public AssetRef<Mesh> Mesh;
    public AssetRef<Material> Material;
    public Color MainColor = Color.white;
    public PropertyBlock Properties;

    public override void Update()
    {
        if (!Mesh.IsAvailable) return;
        if (!Material.IsAvailable) return;

        Properties ??= new();

        Properties.SetColor("_MainColor", MainColor);
        Properties.SetInt("_ObjectID", InstanceID);

        RenderPipelines.RenderPipeline.AddRenderable(this);
    }

    public SerializedProperty Serialize(Serializer.SerializationContext ctx)
    {
        SerializedProperty compoundTag = SerializedProperty.NewCompound();
        compoundTag.Add("Mesh", Serializer.Serialize(Mesh, ctx));
        compoundTag.Add("Material", Serializer.Serialize(Material, ctx));
        compoundTag.Add("MainColor", Serializer.Serialize(MainColor, ctx));
        return compoundTag;
    }

    public void Deserialize(SerializedProperty value, Serializer.SerializationContext ctx)
    {
        Mesh = Serializer.Deserialize<AssetRef<Mesh>>(value["Mesh"], ctx);
        Material = Serializer.Deserialize<AssetRef<Material>>(value["Material"], ctx);
        MainColor = Serializer.Deserialize<Color>(value["MainColor"], ctx);
    }


    public Material GetMaterial()
    {
        return Material.Res;
    }


    public void GetRenderingData(out PropertyBlock properties, out IGeometryDrawData drawData, out Matrix4x4 model)
    {
        drawData = Mesh.Res;
        properties = Properties;
        model = Transform.localToWorldMatrix;
    }


    public void GetCullingData(out bool isRenderable, out Bounds bounds)
    {
        isRenderable = true;
        bounds = Mesh.Res.bounds.Transform(Transform.localToWorldMatrix);
    }
}
