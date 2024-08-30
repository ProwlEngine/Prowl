using System;
using System.Collections.Generic;
using System.Linq;
using Veldrid;

namespace Prowl.Runtime
{
    /// <summary>
    /// The current rendering state passed to RenderCommand.ExecuteCommand().
    /// Defines relevant information about keywords, targets, and pipeline state for commands to use.
    /// </summary>
    internal class RenderState : IDisposable
    {
        private Framebuffer activeFramebuffer;
        internal Framebuffer ActiveFramebuffer => activeFramebuffer;
        
        private KeywordState keywordState;
        internal ShaderPass ActivePass => pipelineDescription.pass;
        internal ShaderVariant ActiveVariant => pipelineDescription.variant;

        public PropertyState propertyState;
        
        private GraphicsPipelineDescription pipelineDescription;

        public PolygonFillMode fill;
        public PrimitiveTopology topology;
        public bool scissor;

        public GraphicsPipeline graphicsPipeline;
        public BindableResourceSet pipelineResources;
        public Pipeline actualActivePipeline;



        public void SetFramebuffer(Framebuffer framebuffer)
        {
            activeFramebuffer = framebuffer;
            pipelineDescription.output = activeFramebuffer.OutputDescription;
        }


        public void SetPass(ShaderPass pass)
        {
            pipelineDescription.pass = pass;
        }


        public void SetKeyword(string keyword, string value)
        {
            keywordState.SetKey(keyword, value);

            pipelineDescription.variant = pipelineDescription.pass.GetVariant(keywordState);
        }


        public void GetPipeline(out GraphicsPipeline graphicsPipeline, out Pipeline actualPipeline)
        {
            graphicsPipeline = GraphicsPipelineCache.GetPipeline(pipelineDescription);
            actualPipeline = graphicsPipeline.GetPipeline(fill, topology, scissor);

            if (this.graphicsPipeline != graphicsPipeline)
            {
                pipelineResources?.Dispose();
                pipelineResources = graphicsPipeline.CreateResources();
            }

            this.graphicsPipeline = graphicsPipeline;
            this.actualActivePipeline = actualPipeline;
        }


        public RenderState()
        {
            activeFramebuffer = null;

            propertyState = new();
            keywordState = KeywordState.Default;

            graphicsPipeline = null;
            actualActivePipeline = null;

            pipelineDescription.pass = null;
            pipelineDescription.variant = null;
            pipelineDescription.output = null;

            keywordState = KeywordState.Default;
        }
        

        public void Dispose()
        {
            pipelineResources?.Dispose();
        }
    }
}