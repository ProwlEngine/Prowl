using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    internal struct ScissorCommand : RenderingCommand
    {
        public int Index; 
        public bool SetFull;
        public int X, Y, Width, Height;
        public bool SetActive;

        readonly void RenderingCommand.ExecuteCommand(CommandList list, RenderState state)
        {
            if (SetActive)
                state.pipelineSettings.scissorTest = true;
            else
                state.pipelineSettings.scissorTest = false;

            if (SetFull)
            {   
                if (Index < 0)
                    list.SetFullScissorRects();
                else
                    list.SetFullScissorRect((uint)Index);

                return;
            }

            if (Index < 0)
            {
                for (uint i = 0; i < state.ActiveFramebuffer.ColorTargets.Length; i++)
                    list.SetScissorRect(i, (uint)X, (uint)Y, (uint)Width, (uint)Height);
            }
            else
            {
                list.SetScissorRect((uint)Index, (uint)X, (uint)Y, (uint)Width, (uint)Height);
            }
        }
    }
}