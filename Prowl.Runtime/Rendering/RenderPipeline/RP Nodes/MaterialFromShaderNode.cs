using Prowl.Runtime.NodeSystem;

namespace Prowl.Runtime.RenderPipelines
{
    [Node("Rendering/Create Material")]
    public class MaterialFromShaderNode : Node
    {
        public override string Title => "Create Material";
        public override float Width => 215;
        [Output, SerializeIgnore] public AssetRef<Material> Material;

        public string ShaderName = "Defaults/Blit";

        [SerializeIgnore] AssetRef<Material> savedMat;
        [SerializeIgnore] string savedName = "";

        public override object GetValue(NodePort port)
        {
            if(savedName == ShaderName)
                return savedMat;

            savedMat = new AssetRef<Material>(new Material(Application.AssetProvider.LoadAsset<Shader>(ShaderName + ".shader")));
            savedName = ShaderName;
            return savedMat;
        }
    }
}
