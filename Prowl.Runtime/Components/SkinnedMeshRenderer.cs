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

    System.Numerics.Matrix4x4[] GetBoneMatrices()
    {
        System.Numerics.Matrix4x4[] matrices = new System.Numerics.Matrix4x4[Mesh.Res.boneNames.Length];
        for (int i = 0; i < Mesh.Res.boneNames.Length; i++)
        {
            var t = Root.transform.Find(Mesh.Res.boneNames[i]);
            if (t == null)
                matrices[i] = System.Numerics.Matrix4x4.Identity;
            else
            {
                //matrices[i] = t.localToWorldMatrix.ToFloat();
                var startOffset = Mesh.Res.boneOffsets[i];
                Matrix4x4 mat = Matrix4x4.TRS(t.localPosition - startOffset.Item1, t.localRotation - startOffset.Item2, t.localScale - startOffset.Item3);
                mat = t.parent != null ? t.parent.localToWorldMatrix * mat : mat;
                matrices[i] = mat.ToFloat();

            }
        }
        return matrices;
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
            Material.Res!.EnableKeyword("SKINNED");
            Material.Res!.SetInt("ObjectID", GameObject.InstanceID);
            Material.Res!.SetMatrices("bindposes", GetBoneMatrices());
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

    public CompoundTag Serialize(TagSerializer.SerializationContext ctx)
    {
        CompoundTag compoundTag = new CompoundTag();
        compoundTag.Add("Mesh", TagSerializer.Serialize(Mesh, ctx));
        compoundTag.Add("Material", TagSerializer.Serialize(Material, ctx));
        compoundTag.Add("Root", TagSerializer.Serialize(Root, ctx));

        return compoundTag;
    }

    public void Deserialize(CompoundTag value, TagSerializer.SerializationContext ctx)
    {
        Mesh = TagSerializer.Deserialize<AssetRef<Mesh>>(value["Mesh"], ctx);
        Material = TagSerializer.Deserialize<AssetRef<Material>>(value["Material"], ctx);
        Root = TagSerializer.Deserialize<GameObject>(value["Root"], ctx);
    }
}
