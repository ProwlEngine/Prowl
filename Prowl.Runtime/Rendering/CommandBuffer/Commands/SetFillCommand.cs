using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    internal struct SetFillCommand : RenderingCommand
    {
        public PolygonFillMode FillMode;

        readonly void RenderingCommand.ExecuteCommand(CommandList list, RenderState state)
        {
            state.fill = FillMode;
        }
    }
}