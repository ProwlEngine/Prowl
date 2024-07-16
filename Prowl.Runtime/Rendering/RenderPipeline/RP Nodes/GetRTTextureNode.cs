using Prowl.Runtime.NodeSystem;

namespace Prowl.Runtime.RenderPipelines
{
    [Node("Rendering")]
    public class GetRTTextureNode : Node
    {
        public override string Title => "Get RT Texture";
        public override float Width => 150;

        [Input, SerializeIgnore] public NodeRenderTexture RT;

        [Output, SerializeIgnore] public Texture2D Texture;

        public RTBuffer.Type Type = RTBuffer.Type.Color;

        public override object GetValue(NodePort port)
        {
            var rt = GetInputValue<NodeRenderTexture>("RT");
            if (rt == null) return null;

            Error = "";
            if (rt.TargetOnly)
            {
                Error = "RenderTexture is Target Only Cannot Access Buffers!";
                return null;
            }

            return rt.GetTexture(Type);
        }
    }
}
