using Prowl.Icons;
using System.Collections.Generic;
using Material = Prowl.Runtime.Material;
using Mesh = Prowl.Runtime.Mesh;

namespace Prowl.Runtime;

[RequireComponent(typeof(Transform))]
[ExecuteAlways, AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Shapes}  Skinned Mesh Renderer")]
public class SkinnedMeshRenderer : MonoBehaviour, ISerializable
{
    public override RenderingOrder RenderOrder => RenderingOrder.Opaque;

    [Header("THIS COMPONENT IS NOT FULLY IMPLEMENTED YET")]
    public AssetRef<Mesh> Mesh;
    public GameObject Root;
    public AssetRef<Material> Material;

    GameObject[] bones;

    public void ProcessBoneTree()
    {
        // Get children bone tree
        List<GameObject> b = [];
        GetNodes(Root, ref b);
        bones = b.ToArray();
    }


    void GetNodes(GameObject obj, ref List<GameObject> bones)
    {
        bones.Add(obj);
        if (obj.Children.Count > 0) 
            foreach (var c in obj.Children) 
                GetNodes(c, ref bones);
    }

    System.Numerics.Matrix4x4[] GetBoneMatrices()
    {
        ProcessBoneTree();
        System.Numerics.Matrix4x4[] matrices = new System.Numerics.Matrix4x4[bones.Length];
        for (int i = 0; i < bones.Length; i++)
            matrices[i] = bones[i].Transform!.Local.ToFloat();
        return matrices;
    }

    private Dictionary<int, Matrix4x4> prevMats = new();
    public void OnRenderObject()
    {
        var mat = GameObject.Transform!.GlobalCamRelative;
        int camID = Camera.Current.InstanceID;
        if (!prevMats.ContainsKey(camID)) prevMats[camID] = GameObject.Transform!.GlobalCamRelative;
        var prevMat = prevMats[camID];
        
        if (Mesh.IsAvailable && Material.IsAvailable)
        {
            Material.Res!.EnableKeyword("SKINNED");
            Material.Res!.SetInt("ObjectID", InstanceID);
            //Material.Res!.SetMatrices("bindposes", GetBoneMatrices());
            for (int i = 0; i < Material.Res!.PassCount; i++)
            {
                Material.Res!.SetPass(i);
                Graphics.DrawMeshNow(Mesh.Res!, mat, Material.Res!, prevMat);
            }
            Material.Res!.DisableKeyword("SKINNED");
        }

        prevMats[camID] = mat;
    }

    public void OnRenderObjectDepth()
    {
        if (Mesh.IsAvailable && Material.IsAvailable)
        {
            var mvp = Matrix4x4.Identity;
            mvp = Matrix4x4.Multiply(mvp, GameObject.Transform!.GlobalCamRelative);
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
