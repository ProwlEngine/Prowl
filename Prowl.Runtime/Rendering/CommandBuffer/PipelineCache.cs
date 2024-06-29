using System;
using System.Collections.Generic;
using Veldrid;
using System.Linq;
using System.Diagnostics.CodeAnalysis;


namespace Prowl.Runtime
{
    public static class PipelineCache    
    {
        public struct PassPipelineDescription
        {
            public ShaderPass pass;
            public Utils.KeyGroup<string, string> keywords;
            public OutputDescription? output;
            public PolygonFillMode fillMode;
            public FrontFace frontFace;
            public PrimitiveTopology topology;
            public bool scissorTest;

            public override readonly int GetHashCode()
            {
                return HashCode.Combine(pass, keywords, output, fillMode, frontFace, topology, scissorTest);
            }

            public override bool Equals([NotNullWhen(true)] object? obj)
            {
                if (obj is not PassPipelineDescription desc)
                    return false;
                
                return Equals(desc);
            }

            public bool Equals(PassPipelineDescription other)
            {
                return 
                    pass.Equals(other.pass) && 
                    keywords.Equals(other.keywords) && 
                    output.Equals(other.output) && 
                    fillMode.Equals(other.fillMode) && 
                    frontFace.Equals(other.frontFace) && 
                    topology.Equals(other.topology) && 
                    scissorTest.Equals(other.scissorTest);
            }
        }

        // Bundles a pipeline instance with its description.
        public readonly struct PipelineInfo
        {
            public readonly Pipeline pipeline;
            public readonly GraphicsPipelineDescription description; 
            public readonly ShaderPass.Variant variant;

            internal PipelineInfo(GraphicsPipelineDescription description, Pipeline pipeline, ShaderPass.Variant variant)
            {
                this.pipeline = pipeline;
                this.description = description;
                this.variant = variant;
            }

            public bool Equals(PipelineInfo other)
            {
                return description.Equals(other.description);
            }
        }

        private static Dictionary<PassPipelineDescription, PipelineInfo> pipelineCache = new();


        private static GraphicsPipelineDescription CreateDescriptionForPass(in PassPipelineDescription passDesc)
        {
            var keywordProgram = passDesc.pass.GetVariant(passDesc.keywords);

            ResourceLayout[] resourceLayouts = new ResourceLayout[keywordProgram.resourceSets.Count];

            for (int i = 0; i < resourceLayouts.Length; i++)
            {
                ShaderResource[] resources = keywordProgram.resourceSets[i];

                List<ResourceLayoutElementDescription> elements = new();                

                for (int res = 0; res < resources.Length; res++)
                    resources[res].GetDescription(elements);

                resourceLayouts[i] = Graphics.Factory.CreateResourceLayout(new ResourceLayoutDescription(elements.ToArray()));
            }

            VertexLayoutDescription[] vertexInputs = 
                keywordProgram.vertexInputs.Select(MeshUtility.GetLayoutForResource).ToArray();

            GraphicsPipelineDescription pipelineDesc = new()
            {
                BlendState = passDesc.pass.blend,
                DepthStencilState = passDesc.pass.depthStencil,

                RasterizerState = new RasterizerStateDescription(
                    cullMode: passDesc.pass.cullMode,
                    fillMode: passDesc.fillMode,
                    frontFace: passDesc.frontFace,
                    depthClipEnabled: passDesc.pass.depthClipEnabled,
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
            Utils.KeyGroup<string, string>? keywords = null,
            PolygonFillMode fillMode = PolygonFillMode.Solid,
            FrontFace frontFace = FrontFace.Clockwise,
            PrimitiveTopology topology = PrimitiveTopology.TriangleList,
            bool scissorTest = false,
            OutputDescription? pipelineOutput = null)
        {
            keywords ??= Utils.KeyGroup<string, string>.Default;

            PassPipelineDescription pipelineDesc = new()
            {
                pass = pass,
                keywords = keywords,
                fillMode = fillMode,
                frontFace = frontFace,
                topology = topology,
                scissorTest = scissorTest,
                output = pipelineOutput
            };

            if (!pipelineCache.TryGetValue(pipelineDesc, out PipelineInfo pipelineInfo))
            {
                var description = CreateDescriptionForPass(pipelineDesc);
                var pipeline = Graphics.Factory.CreateGraphicsPipeline(description);

                pipelineInfo = new PipelineInfo(description, pipeline, pass.GetVariant(keywords));

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