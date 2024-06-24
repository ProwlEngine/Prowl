using System;
using System.Collections.Generic;
using Veldrid;
using System.Linq;


namespace Prowl.Runtime
{
    public static class PipelineCache    
    {
        private struct PassPipelineDescription
        {
            public ShaderPass pass;
            public KeywordState? keywordState;
            public OutputDescription? output;
            public PolygonFillMode fillMode;
            public FrontFace frontFace;
            public PrimitiveTopology topology;
            public bool scissorTest;
            public bool depthClip;

            public override readonly int GetHashCode()
            {
                return HashCode.Combine(pass, keywordState, output, fillMode, frontFace, topology, scissorTest, depthClip);
            }
        }

        // Bundles a pipeline instance with its description.
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
            ShaderPass.Variant keywordProgram = passDesc.pass.GetVariant(passDesc.keywordState);

            ResourceLayout[] resourceLayouts = new ResourceLayout[keywordProgram.resourceSets.Count];

            for (int i = 0; i < resourceLayouts.Length; i++)
            {
                ShaderResource[] resources = keywordProgram.resourceSets[i];

                List<ResourceLayoutElementDescription> elements = new();                

                for (int res = 0; res < resources.Length; res++)
                    resources[res].GetDescription(elements);

                resourceLayouts[i] = Graphics.Factory.CreateResourceLayout(new ResourceLayoutDescription(elements.ToArray()));
            }

            VertexLayoutDescription[] vertexInputs = new VertexLayoutDescription[keywordProgram.vertexInputs.Count];

            for (int i = 0; i < vertexInputs.Length; i++)
            {   
                (MeshResource, VertexLayoutDescription) inputs = keywordProgram.vertexInputs[i];

                if (inputs.Item1 == MeshResource.Custom)
                    vertexInputs[i] = inputs.Item2;
                else
                    vertexInputs[i] = MeshUtility.GetLayoutForResource(inputs.Item1);
            }

            GraphicsPipelineDescription pipelineDesc = new()
            {
                BlendState = passDesc.pass.blend,
                DepthStencilState = passDesc.pass.depthStencil,

                RasterizerState = new RasterizerStateDescription(
                    cullMode: passDesc.pass.cullMode,
                    fillMode: passDesc.fillMode,
                    frontFace: passDesc.frontFace,
                    depthClipEnabled: passDesc.depthClip,
                    scissorTestEnabled: passDesc.scissorTest
                ),

                PrimitiveTopology = passDesc.topology,
                Outputs = passDesc.output ?? Graphics.ScreenFramebuffer.OutputDescription,

                ShaderSet = new ShaderSetDescription(
                    vertexLayouts: vertexInputs,
                    shaders: keywordProgram.compiledPrograms,
                    Graphics.GetSpecializations()
                ),

                ResourceLayouts = resourceLayouts,
            };

            return pipelineDesc;
        }

        public static PipelineInfo GetPipelineForPass(
            ShaderPass pass, 
            KeywordState? keywords = null,
            PolygonFillMode fillMode = PolygonFillMode.Solid,
            FrontFace frontFace = FrontFace.Clockwise,
            PrimitiveTopology topology = PrimitiveTopology.TriangleList,
            bool scissorTest = false,
            bool depthClip = true,
            OutputDescription? pipelineOutput = null)
        {
            PassPipelineDescription pipelineDesc = new()
            {
                pass = pass,
                keywordState = keywords,
                fillMode = fillMode,
                frontFace = frontFace,
                topology = topology,
                scissorTest = scissorTest,
                depthClip = depthClip,
                output = pipelineOutput
            };

            if (!pipelineCache.TryGetValue(pipelineDesc, out PipelineInfo pipelineInfo))
            {
                var description = CreateDescriptionForPass(pipelineDesc);
                var pipeline = Graphics.Factory.CreateGraphicsPipeline(description);

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

            pipelineCache.Clear();
        }
    }
}