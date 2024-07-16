using Prowl.Runtime.NodeSystem;
using System.Collections.Generic;

namespace Prowl.Runtime.RenderPipelines
{
    [Node("Rendering")]
    public class DrawRenderablesNode : InOutFlowNode
    {
        public override string Title => "Draw Renderables";
        public override float Width => 215;

        [Input, SerializeIgnore] public List<Renderable> Renderables;
        [Input, SerializeIgnore] public NodeRenderTexture Target;
        [Input, SerializeIgnore] public PropertyState Property;

        public string ShaderTag = "Opaque";
        public AssetRef<Material> Material;
        public AssetRef<Material> Fallback;

        public override void Execute()
        {
            var renderables = GetInputValue<List<Renderable>>("Renderables");
            var target = GetInputValue<NodeRenderTexture>("Target");
            var property = GetInputValue<PropertyState>("Property");

            if(target == null)
            {
                Error = "Target is null!";
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get("Draw Renderables");
            cmd.SetRenderTarget(target.RenderTexture);

            if (property != null)
                cmd.ApplyPropertyState(property);

            // Draw renderables
            (graph as RenderPipeline).Context.DrawRenderers(cmd, renderables, new(ShaderTag, Material.Res, Fallback.Res), (graph as RenderPipeline).CurrentCamera.LayerMask);

            (graph as RenderPipeline).Context.ExecuteCommandBuffer(cmd);

            CommandBufferPool.Release(cmd);

            ExecuteNext();
        }
    }
}
