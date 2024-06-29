using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    public interface RenderingCommand   
    {
        internal void ExecuteCommand(CommandList list, ref RenderState state);
    }
}