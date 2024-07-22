using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    public class StructuredBufferResource : ShaderResource
    {
        [SerializeField, HideInInspector]
        private string structuredBufferName;
        public string StructuredBufferName => structuredBufferName;

        [SerializeField, HideInInspector]
        private ShaderStages stages;
        public ShaderStages Stages => stages;


        private StructuredBufferResource() { }

        public StructuredBufferResource(string bufferName, ShaderStages stages)
        {
            this.structuredBufferName = bufferName;
            this.stages = stages;
        }

        public override void GetDescription(List<ResourceLayoutElementDescription> elements)
        {
            ResourceKind kind = ResourceKind.StructuredBufferReadWrite;
            
            elements.Add(new ResourceLayoutElementDescription(structuredBufferName, kind, stages));
        }

        public override void BindResource(CommandList commandList, List<BindableResource> resources, RenderState state)
        {
            ComputeBuffer buffer = state.propertyState.GetBuffer(structuredBufferName);

            resources.Add(buffer.Buffer);
        }

        public override string GetResourceName() => structuredBufferName;
    }
}