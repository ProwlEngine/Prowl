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
        internal Framebuffer activeFramebuffer;
        internal IGeometryDrawData activeDrawData;
        
        internal PropertyState propertyState;
        internal KeywordState keywordState;

        internal PassPipelineDescription pipelineSettings;
        internal Pipeline activePipeline;

        internal List<ResourceSet> resourceSets;
        internal Dictionary<ShaderResource, DeviceBuffer> uniformBuffers;


        public RenderState()
        {
            activeFramebuffer = null;
            activeDrawData = null;
                    
            propertyState = new();

            pipelineSettings.pass = null;
            pipelineSettings.variant = null;
            pipelineSettings.output = null;
            pipelineSettings.frontFace = FrontFace.Clockwise;
            pipelineSettings.fillMode = PolygonFillMode.Solid;
            pipelineSettings.topology = PrimitiveTopology.TriangleList;
            pipelineSettings.scissorTest = false;

            keywordState = KeywordState.Default;

            activePipeline = null;

            resourceSets = new();
            uniformBuffers = new();
        }


        public void Clear()
        {
            foreach (var set in resourceSets)
                set.Dispose();
            
            foreach (var buf in uniformBuffers.Values)
                buf.Dispose();

            activeFramebuffer = null;
            activeDrawData = null;

            propertyState.Clear();

            pipelineSettings.pass = null;
            pipelineSettings.variant = null;
            pipelineSettings.output = null;
            pipelineSettings.frontFace = FrontFace.Clockwise;
            pipelineSettings.fillMode = PolygonFillMode.Solid;
            pipelineSettings.topology = PrimitiveTopology.TriangleList;  
            pipelineSettings.scissorTest = false;

            keywordState = KeywordState.Default;

            activePipeline = null;

            uniformBuffers.Clear();
            resourceSets.Clear();
        }
    }
}