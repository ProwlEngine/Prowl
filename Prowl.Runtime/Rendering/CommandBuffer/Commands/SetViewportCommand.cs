using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    internal struct SetViewportCommand : RenderingCommand
    {
        public int Index; 
        public bool SetFull;
        public int X, Y;
        public int Width, Height;
        public float Z, Depth;

        readonly void RenderingCommand.ExecuteCommand(CommandList list, RenderState state)
        {
            if (SetFull)
            {   
                if (Index < 0)
                    list.SetFullViewports();
                else
                    list.SetFullViewport((uint)Index);

                return;
            }

            Viewport viewport = new Viewport(X, Y, Width, Height, Z, Depth);

            if (Index < 0)
            {
                list.SetViewport(0, viewport);

                for (uint i = 1; i < state.ActiveFramebuffer.ColorTargets.Length; i++)
                    list.SetViewport(i, viewport);
            }
            else
            {
                list.SetViewport((uint)Index, viewport);
            }
        }
    }
}