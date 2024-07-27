using Prowl.Runtime.NodeSystem;
using System.Collections.Generic;

namespace Prowl.Runtime.RenderPipelines
{
    [Node("Rendering/Draw Renderables")]
    public class DrawRenderablesNode : InOutFlowNode
    {
        public override string Title => "Draw Renderables";
        public override float Width => 215;

        [Input, SerializeIgnore] public List<Renderable> Renderables;
        [Input, SerializeIgnore] public NodeRenderTexture Target;

        public string ShaderTag = "Opaque";
        public AssetRef<Material> Material;
        public AssetRef<Material> Fallback;

        public override void Execute(NodePort port)
        {
            var renderables = GetInputValue<List<Renderable>>("Renderables");
            var target = GetInputValue<NodeRenderTexture>("Target");

            Error = "";
            if (target == null)
            {
                Error = "Target is null!";
                return;
            }

            (graph as RenderPipeline).Context.SetRenderTarget(target.RenderTexture);

            // Draw renderables
            (graph as RenderPipeline).Context.DrawRenderers(renderables, new(ShaderTag, Material.Res, Fallback.Res), (graph as RenderPipeline).CurrentCamera.LayerMask);

            ExecuteNext();
        }
    }
}
