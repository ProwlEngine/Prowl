// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;

namespace Prowl.Runtime;

public class MeshRenderer : MonoBehaviour
{
    public AssetRef<Mesh> Mesh;
    public AssetRef<Material> Material;
    public Color mainColor = Color.white;

    public override void Update()
    {
        if (Mesh.IsAvailable && Material.IsAvailable)
        {
            PropertyState properties = new PropertyState();
            properties.SetInt("_ObjectID", InstanceID);
            RenderPipeline.AddRenderable(new MeshRenderable(
                Mesh,
                Material,
                Transform.localToWorldMatrix,
                this.GameObject.layerIndex,
                properties));
        }
    }
}
