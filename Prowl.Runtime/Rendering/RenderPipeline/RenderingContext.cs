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
            if (internalCommandList.Count == 0)
                return;

            CommandList commandList = Graphics.GetCommandList();

            RenderState state = new RenderState();

            state.SetFramebuffer(TargetFramebuffer);
            commandList.SetFramebuffer(TargetFramebuffer);

            for (int i = 0; i < internalCommandList.Count; i++)
            {
                try 
                {
                    internalCommandList[i].ExecuteCommand(commandList, state);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to execute command: {internalCommandList[i]}", ex);
                }
            }

            Graphics.ExecuteCommandList(commandList, true);

            commandList.Dispose();
            state.Dispose();
        }
    }
}