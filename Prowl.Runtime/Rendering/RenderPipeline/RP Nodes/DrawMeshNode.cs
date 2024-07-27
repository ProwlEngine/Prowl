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
        [Input, SerializeIgnore] public int ShaderPass;

        public override void Execute(NodePort port)
        {
            var mesh = GetInputValue<AssetRef<Mesh>>("Mesh", Mesh);
            var material = GetInputValue<AssetRef<Material>>("Material", Material);
            var target = GetInputValue<NodeRenderTexture>("Target");

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

            var context = (graph as RenderPipeline).Context;
            context.SetRenderTarget(target.RenderTexture);

            context.SetMatrix("Mat_V", (graph as RenderPipeline).Context.Mat_V);
            context.SetMatrix("Mat_P", (graph as RenderPipeline).Context.Mat_P);

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
            if(sun != null)
                context.SetVector("_SunDir", sun.Transform.forward);
            else
                context.SetVector("_SunDir", new Vector3(0, 1, 0));

            context.SetMaterial(material.Res, ShaderPass);
            context.DrawSingle(mesh.Res);

            ExecuteNext();
        }
    }
}
