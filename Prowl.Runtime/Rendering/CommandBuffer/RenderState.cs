using System;
using System.Collections.Generic;
using System.Linq;
using Veldrid;

namespace Prowl.Runtime
{
    public class RenderState
    {
        internal Framebuffer activeFramebuffer;
        
        internal PropertyState propertyState;

        internal PolygonFillMode fillMode;
        internal PrimitiveTopology topology;
        internal bool scissorTest;
        internal Utils.KeyGroup<string, string> keywordState;

        internal Material activeMaterial;
        internal int activePass;
        
        internal Pipeline lastSetPipeline;

        internal List<ResourceSet> resourceSets;
        internal Dictionary<ShaderResource, DeviceBuffer> uniformBuffers;


        public RenderState()
        {
            activeFramebuffer = null;
                    
            propertyState = new();

            fillMode = PolygonFillMode.Solid;
            topology = PrimitiveTopology.TriangleList;
            scissorTest = false;

            keywordState = Utils.KeyGroup<string, string>.Default;
            activeMaterial = null;
            activePass = -1;
                    
            lastSetPipeline = null;

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

            propertyState.Clear();

            fillMode = PolygonFillMode.Solid;
            topology = PrimitiveTopology.TriangleList;  
            scissorTest = false;

            keywordState = Utils.KeyGroup<string, string>.Default;
            activeMaterial = null;
            activePass = -1;

            lastSetPipeline = null;

            uniformBuffers.Clear();
            resourceSets.Clear();
        }
    }
}