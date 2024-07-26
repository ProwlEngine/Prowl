using Prowl.Runtime.NodeSystem;

namespace Prowl.Runtime.RenderPipelines
{
    [Node("Rendering/Draw Mesh")]
    public class DrawMeshNode : InOutFlowNode
    {
        public override string Title => "Draw Mesh";
        public override float Width => 215;

        [Input] public AssetRef<Mesh> Mesh;
        [Input] public AssetRef<Material> Material;
        [Input, SerializeIgnore] public NodeRenderTexture Target;
        [Input, SerializeIgnore] public PropertyState Property;
        [Input, SerializeIgnore] public int ShaderPass;

        public override void Execute(NodePort port)
        {
            var mesh = GetInputValue<AssetRef<Mesh>>("Mesh", Mesh);
            var material = GetInputValue<AssetRef<Material>>("Material", Material);
            var target = GetInputValue<NodeRenderTexture>("Target");
            var property = GetInputValue<PropertyState>("Property");

            Error = "";
            if (mesh == null || !mesh.IsAvailable)
            {
                Error = "Mesh is null!";
                return;
            }

            if (material == null || !material.IsAvailable)
            {
                Error = "Material is null!";
                return;
            }
            if (target == null)
            {
                Error = "Target is null!";
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get("Draw Mesh");
            cmd.SetRenderTarget(target.RenderTexture);

            if (property != null)
                cmd.ApplyPropertyState(property);

            cmd.SetMatrix("Mat_V", (graph as RenderPipeline).Context.Mat_V);
            cmd.SetMatrix("Mat_P", (graph as RenderPipeline).Context.Mat_P);

            // TODO: This shouldnt be here
            DirectionalLight sun = null;
            foreach (var gameObj in SceneManagement.SceneManager.AllGameObjects)
                foreach (var l in gameObj.GetComponentsInChildren<Light>())
                {
                    if (l is DirectionalLight)
                    {
                        sun = l as DirectionalLight;
                        break;
                    }
                }
            cmd.SetVector("_SunDir", sun.Transform.forward);

            cmd.SetMaterial(material.Res, ShaderPass);
            cmd.DrawSingle(mesh.Res);

            (graph as RenderPipeline).Context.ExecuteCommandBuffer(cmd);

            CommandBufferPool.Release(cmd);

            ExecuteNext();
        }
    }
}
