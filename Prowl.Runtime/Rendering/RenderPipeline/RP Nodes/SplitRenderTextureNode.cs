using Prowl.Runtime.NodeSystem;

namespace Prowl.Runtime.RenderPipelines
{
    [Node("Rendering")]
    public class SplitRenderTextureNode : Node
    {
        public override string Title => "Split RenderTexture";
        public override float Width => 150;

        [Input, SerializeIgnore] public NodeRenderTexture RT;

        [Output, SerializeIgnore] public Texture2D Color;
        [Output, SerializeIgnore] public Texture2D Normals;
        [Output, SerializeIgnore] public Texture2D Position;
        [Output, SerializeIgnore] public Texture2D Surface;
        [Output, SerializeIgnore] public Texture2D Emissive;
        [Output, SerializeIgnore] public Texture2D ObjectID;
        [Output, SerializeIgnore] public Texture2D Custom;
        [Output, SerializeIgnore] public Texture2D Depth;

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


            if (port.fieldName == nameof(Color))
                return rt.GetTexture(RTBuffer.Type.Color);

            if (port.fieldName == nameof(Normals))
                return rt.GetTexture(RTBuffer.Type.Normals);

            if (port.fieldName == nameof(Position))
                return rt.GetTexture(RTBuffer.Type.Position);

            if (port.fieldName == nameof(Surface))
                return rt.GetTexture(RTBuffer.Type.Surface);

            if (port.fieldName == nameof(Emissive))
                return rt.GetTexture(RTBuffer.Type.Emissive);

            if (port.fieldName == nameof(ObjectID))
                return rt.GetTexture(RTBuffer.Type.ObjectID);

            if (port.fieldName == nameof(Custom))
                return rt.GetTexture(RTBuffer.Type.Custom);

            if (port.fieldName == nameof(Depth))
                return rt.RenderTexture.DepthBuffer;

            throw new System.Exception("Output port not found");
        }
    }
}
