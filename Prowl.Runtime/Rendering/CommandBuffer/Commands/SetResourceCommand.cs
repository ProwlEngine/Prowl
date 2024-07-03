using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    internal struct UploadResourceCommand : RenderingCommand
    {
        public uint Slot;

        readonly void RenderingCommand.ExecuteCommand(CommandList list, ref RenderState state)
        {
            PipelineCache.GetDescriptionForPipeline(state.activePipeline, out GraphicsPipelineDescription pipelineDescription);

            ShaderVariant variant = state.pipelineSettings.variant;

            if (pipelineDescription.ResourceLayouts.Length <= Slot || variant.ResourceSets.Length <= Slot)
                throw new ArgumentOutOfRangeException(nameof(Slot), Slot, "Invalid slot number");

            List<BindableResource> bindableResources = new();

            ShaderResource[] resourceSet = variant.ResourceSets[Slot];

            bindableResources.Clear();

            for (int res = 0; res < resourceSet.Length; res++)
                resourceSet[res].BindResource(list, bindableResources, state);

            ResourceSetDescription description = new ResourceSetDescription
            {
                Layout = pipelineDescription.ResourceLayouts[Slot],
                BoundResources = bindableResources.ToArray()
            };

            ResourceSet resource = Graphics.Factory.CreateResourceSet(description);

            list.SetGraphicsResourceSet(Slot, resource);

            state.resourceSets.Add(resource);
        }
    }
}