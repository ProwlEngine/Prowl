using System;
using System.Collections.Generic;
using Veldrid;
using System.Linq;


namespace Prowl.Runtime
{
    public static class PipelineCache    
    {
        const bool scissorTest = false;
        const bool depthClip = true;

        private struct PassPipelineDescription
        {
            public Pass pass;
            public KeywordState? keywordState;
            public OutputDescription? output;
            public PolygonFillMode fillMode;
            public FrontFace frontFace;
            public PrimitiveTopology topology;

            public override readonly int GetHashCode()
            {
                return HashCode.Combine(pass, keywordState, output, fillMode, frontFace, topology);
            }
        }

        public readonly struct PipelineInfo
        {
            public readonly Pipeline pipeline;
            public readonly GraphicsPipelineDescription description; 


            internal PipelineInfo(GraphicsPipelineDescription description, Pipeline pipeline)
            {
                this.pipeline = pipeline;
                this.description = description;
            }
        }

        private static Dictionary<PassPipelineDescription, PipelineInfo> pipelineCache = new();

        private static GraphicsPipelineDescription CreateDescriptionForPass(in PassPipelineDescription passDesc)
        {
            Pass.Program keywordProgram = passDesc.pass.GetProgram(passDesc.keywordState);

            GraphicsPipelineDescription pipelineDesc = new()
            {
                BlendState = passDesc.pass.blend,
                DepthStencilState = passDesc.pass.depthStencil,

                RasterizerState = new RasterizerStateDescription(
                    cullMode: passDesc.pass.cullMode,
                    fillMode: passDesc.fillMode,
                    frontFace: passDesc.frontFace,
                    depthClipEnabled: depthClip,
                    scissorTestEnabled: scissorTest
                ),

                PrimitiveTopology = passDesc.topology,
                Outputs = passDesc.output ?? Graphics.Framebuffer.OutputDescription,

                ShaderSet = new ShaderSetDescription(
                    vertexLayouts: keywordProgram.vertexInputs.ToArray(),
                    shaders: keywordProgram.passPrograms
                ),

                ResourceLayouts = keywordProgram.resourceDescriptions.Select(x => Graphics.ResourceFactory.CreateResourceLayout(x)).ToArray()
            };

            return pipelineDesc;
        }

        public static PipelineInfo GetPipelineForPass(Pass pass, 
            KeywordState? keywords = null,
            PolygonFillMode fillMode = PolygonFillMode.Solid,
            FrontFace frontFace = FrontFace.Clockwise,
            PrimitiveTopology topology = PrimitiveTopology.TriangleList,
            OutputDescription? pipelineOutput = null)
        {
            PassPipelineDescription pipelineDesc = new()
            {
                pass = pass,
                keywordState = keywords,
                fillMode = fillMode,
                frontFace = frontFace,
                topology = topology,
                output = pipelineOutput
            };

            if (!pipelineCache.TryGetValue(pipelineDesc, out PipelineInfo pipelineInfo))
            {
                var description = CreateDescriptionForPass(pipelineDesc);
                var pipeline = Graphics.ResourceFactory.CreateGraphicsPipeline(description);

                pipelineInfo = new PipelineInfo(description, pipeline);

                pipelineCache.Add(pipelineDesc, pipelineInfo);
            }

            return pipelineInfo;
        }

        internal static void Dispose()
        {
            foreach (var info in pipelineCache.Values)
            {
                info.pipeline.Dispose();

                foreach (var layout in info.description.ResourceLayouts)
                    layout.Dispose();
            }
        }
    }
}