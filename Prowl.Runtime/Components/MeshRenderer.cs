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

    public override void OnRenderObject()
    {
        Matrix4x4 mat = GameObject.GlobalCamRelative;

        int camID = Camera.Current.InstanceID;
        if (!prevMats.ContainsKey(camID)) prevMats[camID] = mat;
        var prevMat = prevMats[camID];

        var material = Material.Res;
        if (material == null)
        {
            InvalidMat ??= new Material(Shader.Find("Defaults/Invalid.shader"));
            material = InvalidMat;
        }

        if (Mesh.IsAvailable && material != null)
        {
            material.SetColor("_MainColor", mainColor);
            material.SetInt("ObjectID", GameObject.InstanceID);
            /*for (int i = 0; i < material.PassCount; i++)
            {

                material.SetPass(i);
                #warning Veldrid change
                //Graphics.DrawMeshNow(Mesh.Res!, mat, material, prevMat);
            }
            */
        }

        prevMats[camID] = mat;
    }

    public override void OnRenderObjectDepth()
    {
        if (Mesh.IsAvailable && Material.IsAvailable)
        {

            Matrix4x4 mat = GameObject.GlobalCamRelative;

            var mvp = Matrix4x4.Identity;
            mvp = Matrix4x4.Multiply(mvp, mat);
            #warning Veldrid change
            /*
            mvp = Matrix4x4.Multiply(mvp, Graphics.MatDepthView);
            mvp = Matrix4x4.Multiply(mvp, Graphics.MatDepthProjection);
            Material.Res!.SetMatrix("mvp", mvp);
            Material.Res!.SetShadowPass(true);
            Graphics.DrawMeshNowDirect(Mesh.Res!);
            */
        }
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
