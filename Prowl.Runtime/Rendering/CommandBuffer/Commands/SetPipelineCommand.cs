using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    internal struct SetPipelineCommand : RenderingCommand
    {
        public ShaderPass Pass;
        public ShaderVariant Variant;

        readonly void RenderingCommand.ExecuteCommand(CommandList list, RenderState state)
        {
            state.pipelineSettings.pass = Pass;
            state.pipelineSettings.variant = Variant;

            Pipeline pipeline = GraphicsPipelineCache.GetPipelineForDescription(state.pipelineSettings);
            GraphicsPipelineCache.GetDescriptionForPipeline(pipeline, out _);

            if (state.graphicsPipeline != pipeline)
            {
                state.graphicsPipeline = pipeline;
                list.SetPipeline(pipeline);
            }
        }
    }
}