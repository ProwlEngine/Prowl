using Prowl.Runtime.NodeSystem;

namespace Prowl.Runtime.Resources.RenderPipeline
{
    [DisallowMultipleNodes]
    public class CameraNode : Node
    {
        public override string Title => "Camera";
        public override float Width => 50;

        [Output] public GBuffer CameraOutput;

        public override object GetValue(NodePort port)
        {
            return Camera.Current.gBuffer;
        }
    }

    public class PBRDeferredNode : Node
    {
        public override string Title => "PBR Deferred Pass";
        public override float Width => 100;

        [Input(ShowBackingValue.Never)] public GBuffer CameraOutput;

        [Output] public AssetRef<RenderTexture> RenderTexture;

        public override object GetValue(NodePort port)
        {
            return 0;
        }
    }

    public class DepthOfFieldNode : Node
    {
        public override string Title => "Depth Of Field";
        public override float Width => 125;

        [Input(ShowBackingValue.Never)] public GBuffer CameraOutput;
        [Input(ShowBackingValue.Never)] public AssetRef<RenderTexture> RenderTexture;

        public float FocusStrength = 150f;
        public float Quality = 0.05f;
        public int BlurRadius = 10;

        [Output] public AssetRef<RenderTexture> DofRT;

        public override object GetValue(NodePort port)
        {
            return 0;
        }
    }

    public class AcesFittedNode : Node
    {
        public override string Title => "Aces Fitted Pass";
        public override float Width => 100;

        [Input(ShowBackingValue.Never)] public AssetRef<RenderTexture> RenderTexture;

        [Output] public AssetRef<RenderTexture> TonemappedRT;

        public override object GetValue(NodePort port)
        {
            return 0;
        }
    }

    [DisallowMultipleNodes]
    public class OutputNode : Node
    {
        public override string Title => "Output";
        public override float Width => 125;

        [Input] public AssetRef<RenderTexture> RenderTexture;

        public bool GammaCorrect = true;

        public override object GetValue(NodePort port)
        {
            return RenderTexture;
        }
    }
}
