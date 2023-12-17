using Prowl.Icons;
using System;
using System.Collections.Generic;
using Material = Prowl.Runtime.Material;
using Mesh = Prowl.Runtime.Mesh;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Shapes}  Mesh Renderer")]
public class MeshRenderer : MonoBehaviour, ISerializable
{
    public override RenderingOrder RenderOrder => RenderingOrder.Opaque;

    public AssetRef<Mesh> Mesh;
    public AssetRef<Material> Material;
    public Color mainColor = Color.white;

    private Dictionary<int, Matrix4x4> prevMats = new();

    public void OnRenderObject()
    {
        var mat = GameObject.GlobalCamRelative;
        int camID = Camera.Current.InstanceID;
        if (!prevMats.ContainsKey(camID)) prevMats[camID] = GameObject.GlobalCamRelative;
        var prevMat = prevMats[camID];

        if (Mesh.IsAvailable && Material.IsAvailable) {
            Material.Res!.SetColor("_MainColor", mainColor);
            Material.Res!.SetInt("ObjectID", InstanceID);
            for (int i = 0; i < Material.Res!.PassCount; i++) {

                Material.Res!.SetPass(i);
                Graphics.DrawMeshNow(Mesh.Res!, mat, Material.Res!, prevMat);

                Material.Res!.EndPass();
            }
        }

        prevMats[camID] = mat;
    }

    public void OnRenderObjectDepth()
    {
        if (Mesh.IsAvailable)
        {
            var mvp = Matrix4x4.Identity;
            mvp = Matrix4x4.Multiply(mvp, GameObject.GlobalCamRelative);
            mvp = Matrix4x4.Multiply(mvp, Graphics.MatDepthView);
            mvp = Matrix4x4.Multiply(mvp, Graphics.MatDepthProjection);
            Material.Res!.SetMatrix("mvp", Matrix4x4.Transpose(mvp));
            Material.Res!.SetShadowPass(true);
            Graphics.DrawMeshNowDirect(Mesh.Res!);
            Material.Res!.EndPass();
        }
    }

    public CompoundTag Serialize(TagSerializer.SerializationContext ctx)
    {
        CompoundTag compoundTag = new CompoundTag();
        compoundTag.Add("Mesh", TagSerializer.Serialize(Mesh, ctx));
        compoundTag.Add("Material", TagSerializer.Serialize(Material, ctx));
        compoundTag.Add("mainColor", TagSerializer.Serialize(mainColor, ctx));
        return compoundTag;
    }

    public void Deserialize(CompoundTag value, TagSerializer.SerializationContext ctx)
    {
        Mesh = TagSerializer.Deserialize<AssetRef<Mesh>>(value["Mesh"], ctx);
        Material = TagSerializer.Deserialize<AssetRef<Material>>(value["Material"], ctx);
        mainColor = TagSerializer.Deserialize<Color>(value["mainColor"], ctx);
    }
}
