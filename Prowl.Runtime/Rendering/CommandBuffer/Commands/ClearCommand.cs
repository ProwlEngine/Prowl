using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    internal struct ClearCommand : RenderingCommand
    {
        public bool ClearDepthStencil;
        public bool ClearColor;
        public int ColorAttachment;
        public Color BackgroundColor;
        public float Depth;
        public byte Stencil;

        readonly void RenderingCommand.ExecuteCommand(CommandList list, ref RenderState state)
        {
            if (ClearDepthStencil)
                list.ClearDepthStencil(Depth, Stencil);
            
            if (ClearColor)
            {
                RgbaFloat bgColor = new RgbaFloat(
                    BackgroundColor.r,
                    BackgroundColor.g, 
                    BackgroundColor.b, 
                    BackgroundColor.a);

                if (ColorAttachment < 0)
                {
                    for (uint i = 0; i < state.activeFramebuffer.ColorTargets.Count; i++)
                    {
                        list.ClearColorTarget(i, bgColor);
                    }
                }
                else
                {
                    list.ClearColorTarget((uint)ColorAttachment, bgColor);
                }
            }
        }
    }
}