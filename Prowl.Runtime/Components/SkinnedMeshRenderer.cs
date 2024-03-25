using Prowl.Icons;
using System.Collections.Generic;
using Material = Prowl.Runtime.Material;
using Mesh = Prowl.Runtime.Mesh;

namespace Prowl.Runtime;

[ExecuteAlways, AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Shapes}  Skinned Mesh Renderer")]
public class SkinnedMeshRenderer : MonoBehaviour, ISerializable
{
    public override RenderingOrder RenderOrder => RenderingOrder.Opaque;

    [Header("THIS COMPONENT IS NOT FULLY IMPLEMENTED YET")]
    public AssetRef<Mesh> Mesh;
    public GameObject Root;
    public AssetRef<Material> Material;

    private System.Numerics.Matrix4x4[] bindPoses;
    private System.Numerics.Matrix4x4[] boneTransforms;

    void GetBoneMatrices()
    {
        boneTransforms = new System.Numerics.Matrix4x4[Mesh.Res.boneNames.Length];
        bindPoses = new System.Numerics.Matrix4x4[Mesh.Res.bindPoses.Length];
        for (int i = 0; i < Mesh.Res.boneNames.Length; i++)
        {
            var t = Root.transform.Find(Mesh.Res.boneNames[i]);
            if (t == null)
            {
                boneTransforms[i] = System.Numerics.Matrix4x4.Identity;
                bindPoses[i] = Mesh.Res.bindPoses[i].ToFloat();
            }
            else
            {
                var pose = Mesh.Res.bindPoses[i];
                //pose = Matrix4x4.Transpose(pose);
                boneTransforms[i] = (pose * t.localToWorldMatrix).ToFloat();
                bindPoses[i] = Mesh.Res.bindPoses[i].ToFloat();
            }
        }
    }

    private Dictionary<int, Matrix4x4> prevMats = new();
    public override void OnRenderObject()
    {
        var mat = GameObject.GlobalCamRelative;
        int camID = Camera.Current.InstanceID;
        if (!prevMats.ContainsKey(camID)) prevMats[camID] = GameObject.GlobalCamRelative;
        var prevMat = prevMats[camID];
        
        if (Mesh.IsAvailable && Material.IsAvailable)
        {
            GetBoneMatrices();
            Material.Res!.EnableKeyword("SKINNED");
            Material.Res!.SetInt("ObjectID", GameObject.InstanceID);
            //Material.Res!.SetMatrices("bindPoses", bindPoses);
            Material.Res!.SetMatrices("boneTransforms", boneTransforms);
            for (int i = 0; i < Material.Res!.PassCount; i++)
            {
                Material.Res!.SetPass(i);
                Graphics.DrawMeshNow(Mesh.Res!, mat, Material.Res!, prevMat);
            }
            Material.Res!.DisableKeyword("SKINNED");
        }

        prevMats[camID] = mat;
    }

    public override void OnRenderObjectDepth()
    {
        if (Mesh.IsAvailable && Material.IsAvailable)
        {
            var mvp = Matrix4x4.Identity;
            mvp = Matrix4x4.Multiply(mvp, GameObject.GlobalCamRelative);
            mvp = Matrix4x4.Multiply(mvp, Graphics.MatDepthView);
            mvp = Matrix4x4.Multiply(mvp, Graphics.MatDepthProjection);
            Material.Res!.SetMatrix("mvp", mvp);
            Material.Res!.SetShadowPass(true);
            Graphics.DrawMeshNowDirect(Mesh.Res!);
        }
    }

    public SerializedProperty Serialize(Serializer.SerializationContext ctx)
    {
        SerializedProperty compoundTag = SerializedProperty.NewCompound();
        compoundTag.Add("Mesh", Serializer.Serialize(Mesh, ctx));
        compoundTag.Add("Material", Serializer.Serialize(Material, ctx));
        compoundTag.Add("Root", Serializer.Serialize(Root, ctx));

        return compoundTag;
    }

    public void Deserialize(SerializedProperty value, Serializer.SerializationContext ctx)
    {
        Mesh = Serializer.Deserialize<AssetRef<Mesh>>(value["Mesh"], ctx);
        Material = Serializer.Deserialize<AssetRef<Material>>(value["Material"], ctx);
        Root = Serializer.Deserialize<GameObject>(value["Root"], ctx);
    }
}
