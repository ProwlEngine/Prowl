using System;
using System.Collections.Generic;
using System.Linq;
using Veldrid;

namespace Prowl.Runtime
{
    internal struct SetMaterialCommand : RenderingCommand
    {
        public Material Material;
        public int Pass;

        readonly void RenderingCommand.ExecuteCommand(CommandList list, RenderState state)
        {
            state.propertyState.ApplyOverride(Material.Properties);
        
            KeywordState keys = KeywordState.Combine(Material.LocalKeywords, state.keywordState);

            state.pipelineSettings.pass = Material.Shader.Res.GetPass(Pass);
            state.pipelineSettings.variant = state.pipelineSettings.pass.GetVariant(keys);

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