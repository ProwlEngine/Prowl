using Prowl.Runtime.NodeSystem;
using System;
using System.Linq;
using Veldrid;

namespace Prowl.Runtime.RenderPipelines
{
    public struct RTBuffer : ISerializationCallbackReceiver
    {
        public enum Type { Color, Normals, Position, Surface, Emissive, ObjectID, Velocity, Custom }
        public Type type;

        [SerializeField, HideInInspector] private byte serialized_format;
        [ShowInInspector] public Veldrid.PixelFormat format { get => (Veldrid.PixelFormat)serialized_format; set => serialized_format = (byte)value; }

        public void OnBeforeSerialize()
        {
            serialized_format = (byte)format;
        }

        public void OnAfterDeserialize()
        {
            format = (Veldrid.PixelFormat)serialized_format;
        }
    }

    public class NodeRenderTexture
    {
        private RenderTexture RT;
        private RTBuffer[] buffers;
        internal bool HasBeenReleased;

        public bool TargetOnly => RT.TargetOnly;

        public RenderTexture RenderTexture => RT;

        public RTBuffer[] Buffers => buffers;

        public NodeRenderTexture(RenderTexture RT, RTBuffer[] buffers) { this.RT = RT; this.buffers = buffers; }
        public NodeRenderTexture(RenderTexture RT) 
        { 
            this.RT = RT; 
        }

        public Texture2D GetTexture(RTBuffer.Type type)
        {
            if(TargetOnly) throw new Exception("Cannot get a buffer from a TargetOnly RenderTexture!");

            var index = Array.FindIndex(buffers, b => b.type == type);
            if (index == -1)
                return null;
            return RT.ColorBuffers[index];
        }
    }

    [Node("Rendering")]
    public class RenderTextureNode : InOutFlowNode
    {
        public override string Title => "Render Texture";
        public override float Width => 250;

        private RenderPipeline renderGraph => (RenderPipeline)graph;

        [Input] public double Scale = 1.0f;

        [Output, SerializeIgnore] public NodeRenderTexture RT;

        public enum ResolutionType { Target, Fixed }
        public ResolutionType Resolution = ResolutionType.Target;
        private bool IsFixed => Resolution == ResolutionType.Fixed;

        [ShowIf(nameof(IsFixed))] public float FixedWidth = 512;
        [ShowIf(nameof(IsFixed))] public float FixedHeight = 512;

        public bool StartCleared = true;
        [ShowIf(nameof(StartCleared))] public Color ClearColor = Color.black;
        public bool EnableRandomWrite = false;

        public RTBuffer[] ColorFormats = [new RTBuffer() { format = PixelFormat.R8_G8_B8_A8_UNorm, type = RTBuffer.Type.Color }];

        public bool HasDepth = true;
        public Veldrid.PixelFormat DepthFormat = Veldrid.PixelFormat.D24_UNorm_S8_UInt;

        public override void OnValidate()
        {
            if (ColorFormats == null || ColorFormats.Length == 0)
                ColorFormats = [new RTBuffer() { format = PixelFormat.R8_G8_B8_A8_UNorm, type = RTBuffer.Type.Color }];

            Scale = Math.Max(0.01f, Scale);

            Error = "";

            if (ColorFormats.Length > 8)
            {
                Error = "Cannot have more than 8 Buffers!";
            }

            // Cannot have two of the same type
            var types = ColorFormats.Select(f => f.type).ToArray();
            if (types.Distinct().Count() != types.Length)
            {
                Error = "Cannot have two Buffers of the same type!";
            }

            if (IsFixed)
            {
                FixedWidth = Math.Max(1, FixedWidth);
                FixedHeight = Math.Max(1, FixedHeight);
            }
        }

        private NodeRenderTexture meRenderTexture;

        public override object GetValue(NodePort port)
        {
            if(meRenderTexture != null)
                if (meRenderTexture.HasBeenReleased == false)
                    return meRenderTexture;
            throw new Exception("Cannot get Render Texture, it has not been created yet!");
        }

        public override void Execute()
        {
            var pixelFormats = ColorFormats.Select(f => f.format).ToArray();
            RenderTextureDescription desc = new()
            {
                enableRandomWrite = EnableRandomWrite,
                sampled = true,
                depthBufferFormat = HasDepth ? DepthFormat : null,
                sampleCount = Veldrid.TextureSampleCount.Count1,
                colorBufferFormats = pixelFormats,
            };

            var scale = GetInputValue<double>("Scale", Scale);

            if (IsFixed)
            {
                desc.width = (uint)(FixedWidth * scale);
                desc.height = (uint)(FixedHeight * scale);
            }
            else
            {
                desc.width = (uint)(renderGraph.Resolution.x * scale);
                desc.height = (uint)(renderGraph.Resolution.y * scale);
            }

            meRenderTexture = renderGraph.GetRT(desc, ColorFormats);
            if (StartCleared)
            {
                var cmd = CommandBufferPool.Get("RT Node Buffer");
                cmd.SetRenderTarget(meRenderTexture.RenderTexture);
                if (HasDepth)
                    cmd.ClearRenderTarget(true, true, ClearColor);
                else
                    cmd.ClearRenderTarget(false, true, ClearColor);
                (graph as RenderPipeline).Context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            ExecuteNext();
        }
    }
}
