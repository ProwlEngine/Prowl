using System;
using System.Collections.Generic;
using Veldrid;
using System.Linq;


namespace Prowl.Runtime
{
    public static class ResourceCache    
    {
        const bool depthClip = true;

        private struct PassPipelineDescription
        {
            public Pass pass;
            public KeywordState? keywordState;
            public OutputDescription? output;
            public PolygonFillMode fillMode;
            public FrontFace frontFace;
            public PrimitiveTopology topology;
            public bool scissorTest;

            public override readonly int GetHashCode()
            {
                return HashCode.Combine(pass, keywordState, output, fillMode, frontFace, topology);
            }
        }

        /// <summary>
        /// Bundles a pipeline instance with its description.
        /// </summary>
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
        private static Dictionary<string, WeakReference<Shader>> shaderCache = new();


        private static GraphicsPipelineDescription CreateDescriptionForPass(in PassPipelineDescription passDesc)
        {
            Pass.Variant keywordProgram = passDesc.pass.GetVariant(passDesc.keywordState);

            ResourceLayout[] resourceLayouts = new ResourceLayout[keywordProgram.resourceSets.Count];

            for (int i = 0; i < resourceLayouts.Length; i++)
            {
                ShaderResource[] resources = keywordProgram.resourceSets[i];

                var description = new ResourceLayoutDescription(resources.Select(x => x.ToDescription()).ToArray());

                resourceLayouts[i] = Graphics.ResourceFactory.CreateResourceLayout(description);
            }


            GraphicsPipelineDescription pipelineDesc = new()
            {
                BlendState = passDesc.pass.blend,
                DepthStencilState = passDesc.pass.depthStencil,

                RasterizerState = new RasterizerStateDescription(
                    cullMode: passDesc.pass.cullMode,
                    fillMode: passDesc.fillMode,
                    frontFace: passDesc.frontFace,
                    depthClipEnabled: depthClip,
                    scissorTestEnabled: passDesc.scissorTest
                ),

                PrimitiveTopology = passDesc.topology,
                Outputs = passDesc.output ?? Graphics.Framebuffer.OutputDescription,

                ShaderSet = new ShaderSetDescription(
                    vertexLayouts: keywordProgram.vertexInputs.Select(x => Mesh.GetLayoutForResource(x.Item1, x.Item2)).ToArray(),
                    shaders: keywordProgram.compiledPrograms,
                    [ new SpecializationConstant(0, Graphics.Device.IsClipSpaceYInverted) ]
                ),

                ResourceLayouts = resourceLayouts,
            };

            return pipelineDesc;
        }

        public static PipelineInfo GetPipelineForPass(
            Pass pass, 
            KeywordState? keywords = null,
            PolygonFillMode fillMode = PolygonFillMode.Solid,
            FrontFace frontFace = FrontFace.Clockwise,
            PrimitiveTopology topology = PrimitiveTopology.TriangleList,
            bool scissor = false,
            OutputDescription? pipelineOutput = null)
        {
            PassPipelineDescription pipelineDesc = new()
            {
                pass = pass,
                keywordState = keywords,
                fillMode = fillMode,
                frontFace = frontFace,
                topology = topology,
                scissorTest = scissor,
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

            pipelineCache.Clear();

            foreach (var shader in shaderCache.Values)
                if (shader.TryGetTarget(out Shader sh))
                    sh.Destroy();
                
            shaderCache.Clear();
        }

        internal static void RegisterShader(Shader shader)
        {
            shaderCache.Add(shader.Name, new WeakReference<Shader>(shader));
        }
    }
}