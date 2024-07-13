using Prowl.Runtime.NodeSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using Veldrid;

namespace Prowl.Runtime.RenderPipelines
{
    public class RTBuffer : ISerializationCallbackReceiver
    {
        public enum Type { Color, Normals, Position, Surface, Emissive, ObjectID, Custom }
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

        public RenderTexture RenderTexture => RT;

        public RTBuffer[] Buffers => buffers;

        public NodeRenderTexture(RenderTexture RT, RTBuffer[] buffers) { this.RT = RT; this.buffers = buffers; }

        public Texture2D GetTexture(RTBuffer.Type type)
        {
            var index = Array.FindIndex(buffers, b => b.type == type);
            if (index == -1)
                return null;
            return RT.ColorBuffers[index];
        }
    }

    [Node("Rendering")]
    public class RenderTextureNode : Node
    {

        public override string Title => "Render Texture";
        public override float Width => 250;

        private RenderPipeline renderGraph => (RenderPipeline)graph;

        [Input, SerializeIgnore] public double Scale = 1.0f;

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
        public Veldrid.PixelFormat DepthFormat = Veldrid.PixelFormat.D32_Float;

        public override void OnValidate()
        {
            if (ColorFormats == null || ColorFormats.Length == 0)
                ColorFormats = [new RTBuffer() { format = PixelFormat.R8_G8_B8_A8_UNorm, type = RTBuffer.Type.Color }];

            Scale = Math.Max(0.01f, Scale);
        }

        public override object GetValue(NodePort port)
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

            if (IsFixed)
            {
                desc.width = (uint)(FixedWidth * Scale);
                desc.height = (uint)(FixedHeight * Scale);
            }
            else
            {
                desc.width = (uint)(renderGraph.Resolution.x * Scale);
                desc.height = (uint)(renderGraph.Resolution.y * Scale);
            }

            var rt = renderGraph.GetRT(desc);
            if (StartCleared)
            {
                var cmd = CommandBufferPool.Get("RT Node Buffer");
                cmd.SetRenderTarget(rt);
                if(HasDepth)
                    cmd.ClearRenderTarget(true, true, ClearColor);
                else
                    cmd.ClearRenderTarget(false, true, ClearColor);
                (graph as RenderPipeline).Context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            NodeRenderTexture nodeRT = new(rt, ColorFormats);

            return nodeRT;
        }
    }
}
