using System;
using System.Collections.Generic;
using System.IO;
using Veldrid;

namespace Prowl.Runtime
{  
    public interface IGeometryDrawData
    {
        public void SetDrawData(CommandList commandList, GraphicsPipeline pipeline);

        public int IndexCount { get; }

        public IndexFormat IndexFormat { get; }
    }
}