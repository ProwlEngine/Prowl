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
    public class RenderState : IDisposable
    {
        private Framebuffer activeFramebuffer;
        internal Framebuffer ActiveFramebuffer => activeFramebuffer;

        internal IGeometryDrawData activeDrawData;
        
        internal PropertyState propertyState;
        internal KeywordState keywordState;

        internal PassPipelineDescription pipelineSettings;
        internal Pipeline activePipeline;

        private List<ResourceSet> resourceSets;
        private Dictionary<ShaderResource, DeviceBuffer> uniformBuffers;


        public void SetFramebuffer(Framebuffer framebuffer)
        {
            activeFramebuffer = framebuffer;
            pipelineSettings.output = activeFramebuffer.OutputDescription;
        }


        public DeviceBuffer GetBufferForResource(ShaderResource resource, uint bufferSizeInBytes)
        {
            if (!uniformBuffers.TryGetValue(resource, out DeviceBuffer buffer) || buffer.SizeInBytes < bufferSizeInBytes)
            {
                buffer = Graphics.Factory.CreateBuffer(new BufferDescription(bufferSizeInBytes, BufferUsage.UniformBuffer | BufferUsage.DynamicWrite));
                uniformBuffers[resource] = buffer;
            }
            
            return buffer;
        }


        public void RegisterSetForDisposal(ResourceSet set) =>
            resourceSets.Add(set);

        public void RegisterSetsForDisposal(IEnumerable<ResourceSet> sets) =>
            resourceSets.AddRange(sets);

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


        public void Dispose()
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

            GC.SuppressFinalize(this);
        }
    }
}