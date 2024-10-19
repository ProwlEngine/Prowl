// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Icons;

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Rendering.Pipelines;

namespace Prowl.Runtime;

[ExecuteAlways]
[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Shapes}  Mesh Renderer")]
public class MeshRenderer : MonoBehaviour, IRenderable
{
    public AssetRef<Mesh> Mesh;
    public AssetRef<Material> Material;

    public PropertyState Properties;

    public override void Update()
    {
        if (!Mesh.IsAvailable) return;
        if (!Material.IsAvailable) return;

        Properties ??= new();

        Properties.SetInt("_ObjectID", InstanceID);

        RenderPipeline.AddRenderable(this);
    }

    public Material GetMaterial()
    {
        return Material.Res;
    }

    public void GetRenderingData(out PropertyState properties, out IGeometryDrawData drawData, out Matrix4x4 model)
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
