using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    public class GraphicsBufferResource : ShaderResource
    {        
        [SerializeField, HideInInspector]
        private string bufferName;
        public string BufferName => bufferName;

        [SerializeField, HideInInspector]
        private ShaderStages stages;
        public ShaderStages Stages => stages;

        private GraphicsBufferResource() { }

        public GraphicsBufferResource(string bufferName, ShaderStages stages)
        {
            this.bufferName = bufferName;
            this.stages = stages;
        }

        public override void GetDescription(List<ResourceLayoutElementDescription> elements)
        {
            ResourceKind kind = ResourceKind.StructuredBufferReadWrite;
            
            elements.Add(new ResourceLayoutElementDescription(bufferName, kind, stages));
        }

        public override void BindResource(CommandList commandList, List<BindableResource> resources, RenderState state)
        {
            GraphicsBuffer buffer = state.propertyState.GetBuffer(bufferName);

            resources.Add(buffer.Buffer);
        }
    }
}