using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    internal struct SetFillCommand : RenderingCommand
    {
        public bool Wireframe;

        readonly void RenderingCommand.ExecuteCommand(CommandList list, RenderState state)
        {
            state.pipelineSettings.fillMode = Wireframe ? PolygonFillMode.Wireframe : PolygonFillMode.Solid;
        }
    }
}