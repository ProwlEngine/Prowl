using Prowl.Runtime.NodeSystem;
using Prowl.Runtime.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Runtime.RenderPipelines
{
    public interface IRenderPipeline<T>
    {
        public void Render(RenderTexture target, T data);   
    }
}
