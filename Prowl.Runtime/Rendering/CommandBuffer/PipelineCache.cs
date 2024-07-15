using System;
using System.Collections.Generic;
using Veldrid;
using System.Linq;
using System.Diagnostics.CodeAnalysis;


namespace Prowl.Runtime
{
    public struct PassPipelineDescription
    {
        public ShaderPass pass;
        public ShaderVariant variant;
        public OutputDescription? output;
        public PolygonFillMode fillMode;
        public FrontFace frontFace;
        public PrimitiveTopology topology;
        public bool scissorTest;

        public override readonly int GetHashCode()
        {
            return HashCode.Combine(pass, variant, output, fillMode, frontFace, topology, scissorTest);
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
                variant.Equals(other.variant) && 
                output.Equals(other.output) && 
                fillMode.Equals(other.fillMode) && 
                frontFace.Equals(other.frontFace) && 
                topology.Equals(other.topology) && 
                scissorTest.Equals(other.scissorTest);
        }
    }

    public static class PipelineCache    
    {
        private static Dictionary<PassPipelineDescription, Pipeline> pipelineCache = new();
        private static Dictionary<ShaderDescription, Veldrid.Shader> shaderCache = new();
        private static Dictionary<Pipeline, GraphicsPipelineDescription> pipelineInfo = new();


        private static Veldrid.Shader[] CreateShaders(ShaderDescription[] sources)
        {
            Veldrid.Shader[] shaders = new Veldrid.Shader[sources.Length];

            for (int i = 0; i < shaders.Length; i++)
            {
                if (!shaderCache.TryGetValue(sources[i], out Veldrid.Shader value))
                {
                    value = Graphics.Factory.CreateShader(sources[i]);
                    shaderCache.Add(sources[i], value);
                }
                
                shaders[i] = value;
            }

            return shaders;
        }

        private static Pipeline CreatePipeline(in PassPipelineDescription passDesc, out GraphicsPipelineDescription description)
        {
            ResourceLayout[] resourceLayouts = new ResourceLayout[passDesc.variant.ResourceSets.Length];

            for (int i = 0; i < resourceLayouts.Length; i++)
            {
                ShaderResource[] resources = passDesc.variant.ResourceSets[i];

                List<ResourceLayoutElementDescription> elements = new();                

                for (int res = 0; res < resources.Length; res++)
                    resources[res].GetDescription(elements);

                resourceLayouts[i] = Graphics.Factory.CreateResourceLayout(new ResourceLayoutDescription(elements.ToArray()));
            }

            description = new()
            {
                BlendState = passDesc.pass.Blend,
                DepthStencilState = passDesc.pass.DepthStencilState,

                RasterizerState = new RasterizerStateDescription(
                    cullMode: passDesc.pass.CullMode,
                    fillMode: passDesc.fillMode,
                    frontFace: passDesc.frontFace,
                    depthClipEnabled: passDesc.pass.DepthClipEnabled,
                    scissorTestEnabled: passDesc.scissorTest
                ),

                PrimitiveTopology = passDesc.topology,
                Outputs = passDesc.output ?? Graphics.ScreenTarget.Framebuffer.OutputDescription,

                ShaderSet = new ShaderSetDescription(
                    vertexLayouts: passDesc.variant.VertexInputs,
                    shaders: CreateShaders(passDesc.variant.GetProgramsForBackend()),
                    Graphics.GetSpecializations()
                ),

                ResourceLayouts = resourceLayouts,
            };

            return Graphics.Factory.CreateGraphicsPipeline(description);
        }

        public static Pipeline GetPipelineForDescription(PassPipelineDescription description)
        {
            if (!pipelineCache.TryGetValue(description, out Pipeline pipeline))
            {
                Debug.Log("Created pass");

                pipeline = CreatePipeline(description, out GraphicsPipelineDescription pipelineDesc);

                pipelineCache.Add(description, pipeline);
                pipelineInfo.Add(pipeline, pipelineDesc);
            }

            return pipeline;
        }

        public static Pipeline GetPipelineForPass(
            ShaderPass pass, 
            KeywordState? keywords = null,
            PolygonFillMode fillMode = PolygonFillMode.Solid,
            FrontFace frontFace = FrontFace.Clockwise,
            PrimitiveTopology topology = PrimitiveTopology.TriangleList,
            bool scissorTest = false,
            OutputDescription? pipelineOutput = null)
        {
            keywords ??= KeywordState.Empty;

            PassPipelineDescription description = new()
            {
                pass = pass,
                variant = pass.GetVariant(keywords),
                fillMode = fillMode,
                frontFace = frontFace,
                topology = topology,
                scissorTest = scissorTest,
                output = pipelineOutput
            };

            return GetPipelineForDescription(description);
        }

        public static bool GetDescriptionForPipeline(Pipeline pipeline, out GraphicsPipelineDescription description) =>
            pipelineInfo.TryGetValue(pipeline, out description);

        internal static void Dispose()
        {
            foreach (var pipeline in pipelineCache.Values)
                pipeline.Dispose();

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