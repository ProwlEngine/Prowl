using Prowl.Runtime.NodeSystem;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime.RenderPipelines
{
    [Node("Rendering")]
    public class CameraNode : Node
    {
        public override string Title => "Camera";
        public override float Width => 100;

        [Output, SerializeIgnore] public Vector2 Resolution;
        [Output, SerializeIgnore] public Camera Camera;

        public override object GetValue(NodePort port)
        {
            if (port.fieldName == "Resolution")
                return (graph as RenderPipeline).Resolution;
            if (port.fieldName == "Camera")
                return (graph as RenderPipeline).CurrentCamera;
            return null;
        }
    }

    [Node("Rendering")]
    public class SplitRenderTextureNode : Node
    {
        public override string Title => "Split RenderTexture";
        public override float Width => 150;

        [Input, SerializeIgnore] public NodeRenderTexture RT;

        [Output, SerializeIgnore] public Texture2D Texture;

        public RTBuffer.Type Type = RTBuffer.Type.Color;

        public override object GetValue(NodePort port)
        {
            var rt = GetInputValue<NodeRenderTexture>("RT");
            if (rt == null) return null;

            return rt.GetTexture(Type);
        }
    }

    [Node("Rendering")]
    public class GetRenderablesNode : Node
    {
        public override string Title => "Get Renderables";
        public override float Width => 150;

        [Input, SerializeIgnore] public Camera Camera;

        [Output, SerializeIgnore] public List<Renderable> Renderables;

        public override object GetValue(NodePort port)
        {
            var cam = GetInputValue<Camera>("Camera");

            // Get the culling parameters from the current Camera
            var camFrustrum = cam.GetFrustrum((uint)(graph as RenderPipeline).Resolution.x, (uint)(graph as RenderPipeline).Resolution.y);

            // Use the culling parameters to perform a cull operation, and store the results
            var cullingResults = (graph as RenderPipeline).Context.Cull(camFrustrum);

            return cullingResults ?? new List<Renderable>();
        }
    }

    [Node("Rendering")]
    public class SortRenderablesNode : Node
    {
        public override string Title => "Sort Renderables";
        public override float Width => 200;

        [Input, SerializeIgnore] public List<Renderable> Renderables;

        [Output, SerializeIgnore] public List<Renderable> Sorted;

        public SortMode SortMode = SortMode.FrontToBack;

        public override object GetValue(NodePort port)
        {
            var renderables = GetInputValue<List<Renderable>>("Renderables");

            // Sort renderables
            SortedList<double, List<Renderable>> sorted = (graph as RenderPipeline).Context.SortRenderables(renderables, SortMode);

            // pack them into 1 sorted list
            List<Renderable> packed = new();
            foreach (var kvp in sorted)
                packed.AddRange(kvp.Value);

            return packed;
        }
    }

    [Node("Rendering")]
    public class DrawRenderablesNode : Node
    {
        public override string Title => "Draw Renderables";
        public override float Width => 215;

        [Input, SerializeIgnore] public List<Renderable> Renderables;
        [Input, SerializeIgnore] public NodeRenderTexture Target;

        [Output, SerializeIgnore] public NodeRenderTexture Result;

        public string ShaderTag = "Opaque";
        public AssetRef<Material> Material;
        public AssetRef<Material> Fallback;

        public override object GetValue(NodePort port)
        {
            var renderables = GetInputValue<List<Renderable>>("Renderables");
            var target = GetInputValue<NodeRenderTexture>("Target");

            CommandBuffer cmd = CommandBufferPool.Get("Draw Renderables");
            cmd.SetRenderTarget(target.RenderTexture);

            // Draw renderables
            (graph as RenderPipeline).Context.DrawRenderers(cmd, renderables, new(ShaderTag, Material.Res, Fallback.Res), (graph as RenderPipeline).CurrentCamera.LayerMask);

            (graph as RenderPipeline).Context.ExecuteCommandBuffer(cmd);

            CommandBufferPool.Release(cmd);

            return target;
        }
    }
}
