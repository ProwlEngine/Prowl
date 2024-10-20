// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;


namespace Prowl.Runtime.Rendering;

public static class ComputePipelineCache
{
    private static readonly Dictionary<ComputeVariant, ComputePipeline> pipelineCache = new();


    internal static ComputePipeline GetPipeline(in ComputeVariant variant)
    {
        if (pipelineCache.TryGetValue(variant, out ComputePipeline pipeline))
            return pipeline;

        pipeline = new ComputePipeline(variant);

        pipelineCache.Add(variant, pipeline);

        return pipeline;
    }

    internal static void Dispose()
    {
        foreach (var pipeline in pipelineCache.Values)
            pipeline.Dispose();
    }
}
