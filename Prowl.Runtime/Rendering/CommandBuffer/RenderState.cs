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
    public class RenderState
    {
        private Framebuffer activeFramebuffer;
        internal Framebuffer ActiveFramebuffer => activeFramebuffer;
        
        internal ShaderPass activePass;

        internal PropertyState propertyState;
        internal KeywordState keywordState;

        internal ShaderPipelineDescription pipelineDescription;
        internal ShaderPipeline activePipeline;


        public void SetFramebuffer(Framebuffer framebuffer)
        {
            activeFramebuffer = framebuffer;
            pipelineDescription.output = activeFramebuffer.OutputDescription;
        }


        public RenderState()
        {
            activeFramebuffer = null;
            activePass = null;

            propertyState = new();
            keywordState = KeywordState.Default;

            activePipeline = null;

            pipelineDescription.variant = null;
            pipelineDescription.output = null;
            pipelineDescription.fillMode = PolygonFillMode.Solid;
            pipelineDescription.topology = PrimitiveTopology.TriangleList;
            pipelineDescription.scissorTest = false;

            keywordState = KeywordState.Default;

            activePipeline = null;
        }
    }
}