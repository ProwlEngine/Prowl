using System;
using System.Collections.Generic;
using System.Linq;

using Veldrid;

namespace Prowl.Runtime
{
    internal struct SetPassCommand : RenderingCommand
    {
        public ShaderPass Pass;

        readonly void RenderingCommand.ExecuteCommand(CommandList list, RenderState state)
        {
            state.SetPass(Pass);

            state.GetPipeline(out _, out _);
        }
    }
}
