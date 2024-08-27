using Prowl.Runtime.NodeSystem;
using Prowl.Runtime.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Runtime.RenderPipelines
{
    public abstract class RenderPipeline<T> : EngineObject
    {
        public abstract void Render(RenderTexture target, T data);   
    }
}
