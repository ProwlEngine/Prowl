using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    internal struct SetFramebufferCommand : RenderingCommand
    {   
        public Framebuffer Framebuffer;

        readonly void RenderingCommand.ExecuteCommand(CommandList list, ref RenderState state)
        {
            list.SetFramebuffer(Framebuffer);
            state.activeFramebuffer = Framebuffer;
        }
    }
}