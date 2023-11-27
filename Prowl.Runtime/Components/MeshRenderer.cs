using Prowl.Icons;
using System.Numerics;
using Material = Prowl.Runtime.Resources.Material;
using Mesh = Prowl.Runtime.Resources.Mesh;

namespace Prowl.Runtime.Components;

[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Shapes}  Mesh Renderer")]
public class MeshRenderer : MonoBehaviour
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
            Graphics.DepthMat.SetMatrix("mvp",Matrix4x4.Transpose(mvp));
            Graphics.DepthMat.SetPass(0, true);
            Graphics.DrawMeshNowDirect(Mesh.Res!);
            Graphics.DepthMat.EndPass();
        }
    }
}
