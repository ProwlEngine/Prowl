using Prowl.Runtime.NodeSystem;
using System.Collections.Generic;

namespace Prowl.Runtime.RenderPipelines
{
    [Node("Rendering")]
    public class GetRenderablesNode : Node
    {
        public override string Title => "Get Renderables";
        public override float Width => 150;

        [Input, SerializeIgnore] public Camera.CameraData Camera;

        [Output, SerializeIgnore] public List<Renderable> Renderables;

        public override object GetValue(NodePort port)
        {
            var cam = GetInputValue<Camera.CameraData>("Camera");

            // Get the culling parameters from the current Camera
            var camFrustrum = cam.GetFrustrum((uint)(graph as RenderPipeline).Resolution.x, (uint)(graph as RenderPipeline).Resolution.y);

            // Use the culling parameters to perform a cull operation, and store the results
            var cullingResults = (graph as RenderPipeline).Context.Cull(camFrustrum);

            return cullingResults ?? new List<Renderable>();
        }
    }
}
