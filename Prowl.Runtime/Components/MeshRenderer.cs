using Prowl.Icons;
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

    private static Material? InvalidMat;

    public void OnRenderObject()
    {
        Matrix4x4 mat;
        if (GameObject.Transform != null)
            mat = GameObject.Transform!.GlobalCamRelative;
        else {
            mat = Matrix4x4.Identity;
            mat.Translation -= Camera.Current.GameObject.Transform?.GlobalPosition ?? Vector3.Zero;
        }

        int camID = Camera.Current.InstanceID;
        if (!prevMats.ContainsKey(camID)) prevMats[camID] = mat;
        var prevMat = prevMats[camID];

        var material = Material.Res;
        if(material == null) {
            InvalidMat ??= new Material(Shader.Find("Defaults/Invalid.shader"));
            material = InvalidMat;
        }

        if (Mesh.IsAvailable && material != null) {
            material.SetColor("_MainColor", mainColor);
            material.SetInt("ObjectID", GameObject.InstanceID);
            for (int i = 0; i < material.PassCount; i++) {

                material.SetPass(i);
                Graphics.DrawMeshNow(Mesh.Res!, mat, material, prevMat);
            }
        }

        prevMats[camID] = mat;
    }

    public void OnRenderObjectDepth()
    {
        if (Mesh.IsAvailable && Material.IsAvailable) {

            Matrix4x4 mat;
            if (GameObject.Transform != null)
                mat = GameObject.Transform!.GlobalCamRelative;
            else {
                mat = Matrix4x4.Identity;
                mat.Translation -= Camera.Current.GameObject.Transform?.GlobalPosition ?? Vector3.Zero;
            }

            var mvp = Matrix4x4.Identity;
            mvp = Matrix4x4.Multiply(mvp, mat);
            mvp = Matrix4x4.Multiply(mvp, Graphics.MatDepthView);
            mvp = Matrix4x4.Multiply(mvp, Graphics.MatDepthProjection);
            Material.Res!.SetMatrix("mvp", mvp);
            Material.Res!.SetShadowPass(true);
            Graphics.DrawMeshNowDirect(Mesh.Res!);
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
