using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    internal struct SetDrawDataCommand : RenderingCommand
    {
        public IGeometryDrawData DrawData;

        readonly void RenderingCommand.ExecuteCommand(CommandList list, RenderState state)
        {
            DrawData.SetDrawData(list, state.graphicsPipeline);
            state.activeDrawData = DrawData;
        }
    }
}