using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    // Basically does SetPipeline, SetDrawData, SetResource, and ManualDraw all at once.
    internal struct DrawCommand : RenderingCommand
    {
        public IGeometryDrawData DrawData;
        public int IndexCount;
        public int IndexOffset;


        readonly void RenderingCommand.ExecuteCommand(CommandList list, RenderState state)
        {   
            // Set Pipeline
            Pipeline pipeline = PipelineCache.GetPipelineForDescription(state.pipelineSettings);
            PipelineCache.GetDescriptionForPipeline(pipeline, out GraphicsPipelineDescription pipelineDescription);

            if (state.activePipeline != pipeline)
            {
                state.activePipeline = pipeline;
                list.SetPipeline(pipeline);
            }

            // Set Draw Data
            DrawData.SetDrawData(list, pipelineDescription.ShaderSet.VertexLayouts);
            state.activeDrawData = DrawData;
            
            // Recreate ALL resource sets
            List<ResourceSet> resources = new();

            ShaderVariant variant = state.pipelineSettings.variant;

            List<BindableResource> bindableResources = new();

            for (int set = 0; set < pipelineDescription.ResourceLayouts.Length; set++)
            {
                ShaderResource[] resourceSet = variant.ResourceSets[set];

                bindableResources.Clear();

                for (int res = 0; res < resourceSet.Length; res++)
                    resourceSet[res].BindResource(list, bindableResources, state);

                ResourceSetDescription description = new ResourceSetDescription
                {
                    Layout = pipelineDescription.ResourceLayouts[set],
                    BoundResources = bindableResources.ToArray()
                };

                resources.Add(Graphics.Factory.CreateResourceSet(description));

                list.SetGraphicsResourceSet((uint)set, resources[set]);
            }

            state.RegisterSetsForDisposal(resources);

            // Manual Draw
            list.DrawIndexed(
                indexCount: (uint)(IndexCount <= 0 ? DrawData.IndexCount : IndexCount),
                instanceCount: 1,
                indexStart: (uint)IndexOffset,
                vertexOffset: 0,
                instanceStart: 0
            );
        }
    }
}