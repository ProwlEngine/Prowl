// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;


namespace Prowl.Runtime
{
    public static class ShaderPipelineCache
    {
        private static Dictionary<ShaderPipelineDescription, ShaderPipeline> pipelineCache = new();


        internal static ShaderPipeline GetPipeline(in ShaderPipelineDescription description)
        {
            if (pipelineCache.TryGetValue(description, out ShaderPipeline pipeline))
                return pipeline;

            pipeline = new ShaderPipeline(description);

            pipelineCache.Add(description, pipeline);

            return pipeline;
        }

        internal static void Dispose()
        {
            foreach (var pipeline in pipelineCache.Values)
                pipeline.Dispose();
        }
    }
}
