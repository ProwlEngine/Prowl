using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    internal struct SetDrawDataCommand : RenderingCommand
    {
        public IGeometryDrawData DrawData;
        public VertexLayoutDescription[] Resources;

        readonly void RenderingCommand.ExecuteCommand(CommandList list, ref RenderState state)
        {
            DrawData.SetDrawData(list, Resources);
            state.activeDrawData = DrawData;
        }
    }
}