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

            Pipeline pipeline = ShaderPipelineCache.GetPipelineForDescription(state.pipelineSettings);
            ShaderPipelineCache.GetDescriptionForPipeline(pipeline, out _);

            if (state.activePipeline != pipeline)
            {
                state.activePipeline = pipeline;
                list.SetPipeline(pipeline);
            }
        }
    }
}