using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    internal struct DrawCommand : RenderingCommand
    {
        public Mesh Mesh;
        public int IndexCount;
        public int IndexOffset;


        readonly void RenderingCommand.ExecuteCommand(CommandList list, ref RenderState state)
        {
            Material material = state.activeMaterial;
            Shader shader = material.Shader.Res;
            ShaderPass pass = shader.GetPass(state.activePass);

            PipelineCache.PipelineInfo pipelineInfo = PipelineCache.GetPipelineForPass(
                pass, 
                state.keywordState, 
                state.fillMode, 
                FrontFace.Clockwise, 
                Mesh.MeshTopology, 
                state.scissorTest,  
                state.activeFramebuffer.OutputDescription);

            if (state.lastSetPipeline != pipelineInfo.pipeline)
                list.SetPipeline(pipelineInfo.pipeline);

            state.lastSetPipeline = pipelineInfo.pipeline;

            List<ResourceSet> resources = new();

            MeshUtility.UploadMeshResources(list, Mesh, pipelineInfo.variant.VertexInputs);

            List<BindableResource> bindableResources = new();

            for (int set = 0; set < pipelineInfo.description.ResourceLayouts.Length; set++)
            {
                ShaderResource[] resourceSet = pipelineInfo.variant.ResourceSets[set];

                bindableResources.Clear();

                for (int res = 0; res < resourceSet.Length; res++)
                    resourceSet[res].BindResource(list, bindableResources, state);

                ResourceSetDescription description = new ResourceSetDescription
                {
                    Layout = pipelineInfo.description.ResourceLayouts[set],
                    BoundResources = bindableResources.ToArray()
                };

                resources.Add(Graphics.Factory.CreateResourceSet(description));

                list.SetGraphicsResourceSet((uint)set, resources[set]);
            }

            list.DrawIndexed(
                indexCount: (uint)(IndexCount <= 0 ? Mesh.IndexCount : IndexCount),
                instanceCount: 1,
                indexStart: (uint)IndexOffset,
                vertexOffset: 0,
                instanceStart: 0
            );

            state.resourceSets.AddRange(resources);
        }
    }
}