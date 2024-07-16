using Prowl.Runtime.NodeSystem;

namespace Prowl.Runtime.RenderPipelines
{
    [Node("Rendering/Blit Material")]
    public class BlitMaterialNode : InOutFlowNode
    {
        public override string Title => "Blit Material";
        public override float Width => 215;
        
        [Input, SerializeIgnore] public NodeRenderTexture Target;
        [Input, SerializeIgnore] public AssetRef<Material> Material;
        [Input, SerializeIgnore] public PropertyState Property;

        public override void Execute(NodePort port)
        {
            var target = GetInputValue<NodeRenderTexture>("Target");
            var material = GetInputValue<AssetRef<Material>>("Material");
            var property = GetInputValue<PropertyState>("Property");

            if(target == null)
            {
                Error = "Target is null!";
                return;
            }

            if (material.IsAvailable)
            {
                CommandBuffer cmd = CommandBufferPool.Get(Title);
                cmd.SetRenderTarget(target.RenderTexture);
                if (property != null)
                    cmd.ApplyPropertyState(property);
                cmd.SetMaterial(material.Res, 0);
                cmd.DrawSingle(Mesh.GetFullscreenQuad());

                (graph as RenderPipeline).Context.ExecuteCommandBuffer(cmd);

                CommandBufferPool.Release(cmd);
            }
            else
            {
                Error = "Material is not available!";
            }

            ExecuteNext();
        }
    }
}
