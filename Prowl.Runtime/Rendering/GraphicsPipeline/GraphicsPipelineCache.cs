// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;


namespace Prowl.Runtime
{
    public static class GraphicsPipelineCache
    {
        private static Dictionary<GraphicsPipelineDescription, GraphicsPipeline> pipelineCache = new();


        internal static GraphicsPipeline GetPipeline(in GraphicsPipelineDescription description)
        {
            if (pipelineCache.TryGetValue(description, out GraphicsPipeline pipeline))
                return pipeline;

            pipeline = new GraphicsPipeline(description);

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
