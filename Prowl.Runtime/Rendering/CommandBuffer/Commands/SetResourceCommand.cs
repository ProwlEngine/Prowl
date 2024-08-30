using System;
using System.Collections.Generic;

using Veldrid;

namespace Prowl.Runtime
{
    internal struct SetResourceCommand : RenderingCommand
    {
        public string? BufferName;

        readonly void RenderingCommand.ExecuteCommand(CommandList list, RenderState state)
        {
            if (BufferName == null)
                state.pipelineResources.Bind(list);
            else
                state.pipelineResources.UpdateBuffer(list, BufferName);
        }
    }
}
