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

    private Matrix4x4? _prevTransform;

    public override void Update()
    {
        if (!Mesh.IsAvailable) return;
        if (!Material.IsAvailable) return;

        Properties ??= new();

        RenderPipeline.AddRenderable(this);
    }

    public Material GetMaterial() => Material.Res;
    public byte GetLayer() => GameObject.layerIndex;
    
    public void GetRenderingData(out PropertyState properties, out IGeometryDrawData drawData, out Matrix4x4 model, out Matrix4x4 prevModel)
    {
        properties = Properties;
        drawData = Mesh.Res;
        model = Transform.localToWorldMatrix;
        prevModel = _prevTransform ?? model;
        _prevTransform = model;
    }

    public void GetCullingData(out bool isRenderable, out Bounds bounds)
    {
        isRenderable = _enabledInHierarchy;
        bounds = Mesh.Res.bounds.Transform(Transform.localToWorldMatrix);
    }
}
