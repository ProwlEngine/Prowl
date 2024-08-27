using System;
using System.Collections.Generic;
using Veldrid;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;


namespace Prowl.Runtime
{
    public static class ShaderPipelineCache    
    {
        private static Dictionary<ShaderPipelineDescription, ShaderPipeline> pipelineCache;


        private static ShaderPipeline GetPipeline(ShaderPipelineDescription description)
        {
            if (pipelineCache.TryGetValue(description, out ShaderPipeline pipeline))
                return pipeline;

            pipeline = new ShaderPipeline(description);

            pipelineCache.Add(description, pipeline);

            return pipeline;
        }
        

        internal static ShaderPipeline GetPipelineForPass(
            ShaderPass pass, 
            KeywordState? keywords = null,
            PolygonFillMode fillMode = PolygonFillMode.Solid,
            PrimitiveTopology topology = PrimitiveTopology.TriangleList,
            bool scissorTest = false,
            OutputDescription? pipelineOutput = null)
        {
            keywords ??= KeywordState.Empty;

            ShaderPipelineDescription description = new()
            {
                pass = pass,
                variant = pass.GetVariant(keywords),
                fillMode = fillMode,
                topology = topology,
                scissorTest = scissorTest,
                output = pipelineOutput
            };

            return GetPipeline(description);
        }

        internal static void Dispose()
        {
            foreach (var pipeline in pipelineCache.Values)
                pipeline.pipelineObject.Dispose();

            foreach (var description in pipelineInfo.Values)
            {
                foreach (var layout in description.ResourceLayouts)
                {
                    layout.Dispose();
                }
            }

            foreach (var shader in shaderCache.Values)
                shader.Dispose();

            pipelineCache.Clear();
            pipelineInfo.Clear();
            shaderCache.Clear();
        }
    }
}