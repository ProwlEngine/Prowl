using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    internal struct SetFillCommand : RenderingCommand
    {
        public bool Wireframe;

        readonly void RenderingCommand.ExecuteCommand(CommandList list, ref RenderState state)
        {
            state.fillMode = Wireframe ? PolygonFillMode.Wireframe : PolygonFillMode.Solid;
        }
    }
}