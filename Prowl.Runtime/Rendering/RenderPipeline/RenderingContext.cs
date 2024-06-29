using Prowl.Runtime.Utils;
using Veldrid;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Prowl.Runtime.RenderPipelines
{
    public class RenderingContext
    {
        public Framebuffer TargetFramebuffer;

        private List<RenderingCommand> internalCommandList = new();


        public void Submit(CommandBuffer buffer)
        {
            internalCommandList.AddRange(buffer.Buffer);
        }        

        public void Execute()
        {
            CommandList commandList = Graphics.GetCommandList();

            RenderState state = new RenderState();


            for (int i = 0; i < internalCommandList.Count; i++)
                internalCommandList[i].ExecuteCommand(commandList, ref state);

            Graphics.SubmitCommands(commandList, true);

            state.Clear();
            commandList.Dispose();
        }
    }
}